using System.Net;
using System.Text.Json;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// The human-in-the-loop gate, over HTTP, end to end.
/// </summary>
/// <remarks>
/// <para>
/// <c>ManifestGuardTests</c> asserts <c>RequiresApproval = true</c> on the descriptor and the
/// <c>ModuleTool</c>. That is a <b>static</b> check: it proves the flag is set, not that the gate
/// fires. Without the tests in this file, Auditworthy could ship a broken approval gate with fully
/// green CI — the worst failure available on this platform, because the product's central claim is
/// that an accountable owner approves before anything is committed.
/// </para>
/// <para>
/// Everything here goes through <see cref="IntegrationFixture.AdminClient"/> on purpose.
/// <c>AuthorizedScopeAsync()</c> bypasses RBAC and the approval gate by design, so a test written
/// against it would pass while the gate was broken.
/// </para>
/// </remarks>
[Collection("api")]
public sealed class ChatAndApprovalTests(IntegrationFixture fixture)
{
    private const string Module = "compliance";
    private const string WriteTool = "propose_control_change";
    private const string WritePrompt = "Propose moving A.5.1 to Effective because the policy was approved";

    [Fact]
    public async Task A_write_parks_on_the_gate_and_the_reply_does_not_claim_it_happened()
    {
        using var client = fixture.AdminClient();

        var turn = await AguiStream.PostAsync(client, Module, WritePrompt);

        Assert.False(turn.Failed, $"RUN_ERROR: {turn.Error}");
        Assert.Contains("RUN_STARTED", turn.EventTypes);
        Assert.Contains("RUN_FINISHED", turn.EventTypes);
        Assert.Contains(WriteTool, turn.ToolCalls);
        Assert.True(turn.RequiredApproval, "The write emitted no CUSTOM(approval_required) — the gate did not fire.");

        // "Control A.5.1 moved from X to Y" is what the tool returns once it really runs. The
        // assistant must never say that before a human has approved.
        Assert.DoesNotContain("moved from", turn.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("approval", turn.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_parked_call_is_listed_as_pending_with_its_arguments()
    {
        using var client = fixture.AdminClient();
        var turn = await AguiStream.PostAsync(client, Module, WritePrompt);

        var pending = await FindApprovalAsync(client, turn.ConversationId);

        Assert.Equal(Module, pending.GetProperty("moduleId").GetString());
        Assert.Equal(WriteTool, pending.GetProperty("toolName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(pending.GetProperty("argumentsJson").GetString()),
            "A pending approval with no arguments cannot be reviewed by a human.");
    }

    [Fact]
    public async Task Approving_executes_the_call_and_clears_it_from_the_queue()
    {
        using var client = fixture.AdminClient();
        var turn = await AguiStream.PostAsync(client, Module, WritePrompt);
        var id = (await FindApprovalAsync(client, turn.ConversationId)).GetProperty("id").GetGuid();

        var response = await client.PostAsync($"/api/chat/approvals/{id}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resolved = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Executed", resolved.GetProperty("status").GetString());

        // Nothing may stay parked after it has run, or the owner approves the same write twice.
        Assert.Null(await TryFindApprovalAsync(client, turn.ConversationId));
    }

    [Fact]
    public async Task Rejecting_discards_the_call_without_running_it()
    {
        using var client = fixture.AdminClient();
        var turn = await AguiStream.PostAsync(client, Module, WritePrompt);
        var id = (await FindApprovalAsync(client, turn.ConversationId)).GetProperty("id").GetGuid();

        var response = await client.PostAsync($"/api/chat/approvals/{id}/reject", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resolved = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Rejected", resolved.GetProperty("status").GetString());
        Assert.Null(await TryFindApprovalAsync(client, turn.ConversationId));
    }

    [Fact]
    public async Task An_analyst_may_propose_but_never_clear_their_own_gate()
    {
        // THE load-bearing role. compliance-analyst holds propose_control_change and deliberately
        // NOT chat.approvals.manage: it can park a change and cannot release it. If this test ever
        // goes green-by-widening, the approval lane becomes ceremony. See SECURITY.md.
        using var analyst = fixture.AdminClient(roles: "compliance-analyst", subject: "it-analyst");

        var turn = await AguiStream.PostAsync(analyst, Module, WritePrompt);

        Assert.Contains(WriteTool, turn.ToolCalls);
        Assert.True(turn.RequiredApproval, "The analyst's write did not park on the gate.");

        var queue = await analyst.GetAsync("/api/chat/approvals");
        Assert.Equal(HttpStatusCode.Forbidden, queue.StatusCode);
    }

    [Fact]
    public async Task A_reader_is_never_offered_the_write_tool_at_all()
    {
        // RBAC runs BEFORE the model: an unpermitted tool is not filtered out of the answer, it is
        // never put in front of the model, so there is nothing to jailbreak.
        using var reader = fixture.AdminClient(roles: "compliance-reader", subject: "it-reader");

        var write = await AguiStream.PostAsync(reader, Module, WritePrompt);

        Assert.DoesNotContain(WriteTool, write.ToolCalls);
        Assert.False(write.RequiredApproval);

        // …and the reader still gets the reads they are entitled to, so this is a boundary and not
        // an outage.
        var read = await AguiStream.PostAsync(reader, Module, "Which controls are not yet effective?");
        Assert.Contains("list_controls", read.ToolCalls);
    }

    private static async Task<JsonElement> FindApprovalAsync(HttpClient client, Guid? conversationId)
    {
        var found = await TryFindApprovalAsync(client, conversationId);
        Assert.NotNull(found);
        return found.Value;
    }

    /// <summary>
    /// Matches on the turn's own conversation: the queue is tenant-wide, so a test that took
    /// "the first pending approval" would resolve another test's call.
    /// </summary>
    private static async Task<JsonElement?> TryFindApprovalAsync(HttpClient client, Guid? conversationId)
    {
        Assert.NotNull(conversationId);

        var queue = JsonDocument.Parse(await client.GetStringAsync("/api/chat/approvals")).RootElement;

        foreach (var approval in queue.EnumerateArray())
        {
            if (approval.GetProperty("conversationId").GetGuid() == conversationId)
            {
                return approval.Clone();
            }
        }

        return null;
    }
}
