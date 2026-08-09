using System.Net;
using System.Text.Json;
using Auditworthy.Compliance;
using Auditworthy.Compliance.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plenipo.Infrastructure.Persistence;
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

    /// <summary>
    /// Approving runs the call and resolves it, whatever the outcome.
    /// </summary>
    /// <remarks>
    /// <b>This test previously asserted <c>200 OK</c> and <c>status == "Executed"</c>, and those two
    /// assertions were removed deliberately — they encoded the defect in #29 rather than any
    /// requirement.</b> The Mock provider always sends an invalid status, so the call this test
    /// approves has never once applied a change; it only *looked* successful because the tool
    /// returned its validation failure as an ordinary string. Keeping those assertions would mean
    /// pinning the product to reporting failed writes as successes, which is precisely the reward
    /// hack the loop is supposed to refuse.
    /// <para>
    /// What survives is this test's one durable claim: approving must not leave the call parked.
    /// The outcome semantics moved to <see cref="Approving_a_write_that_cannot_be_applied_does_not_report_success"/>,
    /// and the happy path — a valid status actually changing the row — is proven in
    /// <c>ProposeControlChangeTests</c>, which is the only rung that can supply valid arguments.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Approving_runs_the_call_and_clears_it_from_the_queue()
    {
        using var client = fixture.AdminClient();
        var turn = await AguiStream.PostAsync(client, Module, WritePrompt);
        var id = (await FindApprovalAsync(client, turn.ConversationId)).GetProperty("id").GetGuid();

        var response = await client.PostAsync($"/api/chat/approvals/{id}/approve", null);

        // The tool really ran: the response carries the tool's OWN message, which no generic
        // platform error path could produce.
        Assert.Contains("is not a valid status", await response.Content.ReadAsStringAsync());

        // Nothing may stay parked after it has run, or the owner approves the same write twice.
        Assert.Null(await TryFindApprovalAsync(client, turn.ConversationId));
    }

    /// <summary>
    /// An approved write that cannot be applied must not resolve as if it had been.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tool used to *return* its validation failure as an ordinary string, so the platform saw a
    /// tool that ran to completion: it resolved the approval <c>Executed</c>, wrote "✅ … ran" into
    /// the transcript and reported <c>error: null</c> — while the control was untouched. An
    /// accountable owner was told the opposite of what happened, which is the one outcome this
    /// product exists to prevent. Through <c>AdminClient</c>, because it is a claim about the
    /// approval lane.
    /// </para>
    /// <para>
    /// <b>The HTTP response is the weaker half of this test.</b> What #29 is actually about is the
    /// <i>durable</i> record: a response is read once, but `ai-decisions` is what someone reviewing
    /// the decision later reads, and SECURITY.md makes that trail this product's deliverable rather
    /// than a safety net. So the load-bearing assertion here is the non-null <c>error</c> on the
    /// recorded decision, which read <c>null</c> before the fix.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Approving_a_write_that_cannot_be_applied_does_not_report_success()
    {
        using var client = fixture.AdminClient();
        var turn = await AguiStream.PostAsync(client, Module, WritePrompt);
        var id = (await FindApprovalAsync(client, turn.ConversationId)).GetProperty("id").GetGuid();

        // The Mock provider fills every argument after the first with "example", so the status is
        // always invalid — which makes this the one failure path reachable end to end today.
        var response = await client.PostAsync($"/api/chat/approvals/{id}/approve", null);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("\"status\":\"Executed\"", await response.Content.ReadAsStringAsync());

        // The durable record must not say the approved action succeeded. This is the assertion that
        // goes red on the unfixed code, where `error` was null.
        var decisions = JsonDocument.Parse(
            await client.GetStringAsync("/api/platform/ai-decisions")).RootElement;

        var decision = decisions.EnumerateArray()
            .FirstOrDefault(d => d.TryGetProperty("id", out var did)
                              && did.GetString() == id.ToString());

        Assert.True(decision.ValueKind == JsonValueKind.Object,
            $"No ai-decisions entry recorded for approval {id} — the decision left no durable trace at all.");

        var hasError = decision.TryGetProperty("error", out var error)
                       && error.ValueKind != JsonValueKind.Null
                       && !string.IsNullOrWhiteSpace(error.GetString());

        Assert.True(hasError,
            "ai-decisions recorded the approved write with no error, so the durable record still "
            + "claims an action succeeded that never applied — the defect in #29.");
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
    /// Approving a write with valid arguments actually commits it — the linkage nothing proved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the product's central claim end to end: an accountable owner approves, and *then* the
    /// state changes. Every other test in this file exercises the failure half, because the Mock
    /// provider always sends <c>status: "example"</c> and <c>POST /api/chat/approvals/{id}/approve</c>
    /// accepts no request body — so no model-driven path can supply arguments that would commit.
    /// <c>ProposeControlChangeTests</c> proves the tool commits when invoked *directly*, which says
    /// nothing about the approval lane because that fixture bypasses it by design.
    /// </para>
    /// <para>
    /// <b>The approval is parked by the real path and approved through the real endpoint.</b> The only
    /// substitution is the parked <c>ArgumentsJson</c>, rewritten to what a competent model would have
    /// sent. That keeps everything this issue is about inside the test — the approve endpoint, the
    /// platform's executor, the tool, and the database write — and fakes only the one thing the Mock
    /// provider is incapable of producing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_approved_write_with_valid_arguments_commits_the_change()
    {
        using var client = fixture.AdminClient();
        var turn = await AguiStream.PostAsync(client, Module, WritePrompt);
        var id = (await FindApprovalAsync(client, turn.ConversationId)).GetProperty("id").GetGuid();

        var (scope, _, _) = await fixture.AuthorizedScopeAsync();
        using var _scope = scope;
        var compliance = scope.ServiceProvider.GetRequiredService<ComplianceDbContext>();

        // Target whichever status the control is not currently in, so the assertion cannot pass by
        // the row already happening to hold the value.
        var before = await compliance.Controls.AsNoTracking().FirstAsync(c => c.Reference == "A.5.1");
        var target = before.Status == ControlStatus.Effective
            ? ControlStatus.Implemented
            : ControlStatus.Effective;

        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var parked = await platform.PendingApprovals.FirstAsync(a => a.Id == id);
        parked.ArgumentsJson =
            $$"""{"reference":"A.5.1","status":"{{target}}","reason":"Approval-lane regression test"}""";
        await platform.SaveChangesAsync();

        var response = await client.PostAsync($"/api/chat/approvals/{id}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resolved = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Executed", resolved.GetProperty("status").GetString());

        // The response is not the evidence — the row is. This is the assertion the product's central
        // claim rests on, and until now nothing asserted it.
        var after = await compliance.Controls.AsNoTracking().FirstAsync(c => c.Reference == "A.5.1");
        Assert.Equal(target, after.Status);
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
