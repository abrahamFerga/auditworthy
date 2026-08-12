using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auditworthy.Compliance;
using Auditworthy.Compliance.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plenipo.Infrastructure.Persistence;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// The approval lane is not a second door into a tool the role model closed (#76).
/// </summary>
/// <remarks>
/// <para>
/// <c>ChatAndApprovalTests</c> proves the analyst half of the separation: whoever proposes cannot
/// clear their own gate. This file proves the converse half, which was open — whoever approves must
/// themselves be entitled to the action they are approving. Without it, <c>chat.approvals.manage</c>
/// alone is a general-purpose escalation: park the write as someone who may propose it, approve it
/// as someone RBAC-before-the-model refused the tool to seconds earlier, and the write lands.
/// </para>
/// <para>
/// Through <see cref="IntegrationFixture.AdminClient"/> and the real
/// <c>POST /api/chat/approvals/{id}/approve</c> endpoint, never <c>AuthorizedScopeAsync()</c> —
/// that scope sets <c>["*"]</c> and bypasses exactly the check under test.
/// </para>
/// <para>
/// The only substitution is the parked <c>ArgumentsJson</c>, rewritten to arguments that would
/// really commit, for the reason <c>ChatAndApprovalTests</c> documents at length: the approve
/// endpoint accepts no body, so if the parked arguments are junk the write cannot land and the test
/// would pass against the unfixed product for the wrong reason. Everything the defect is about —
/// the endpoint, the platform's executor, the tool, the row — stays inside the test.
/// </para>
/// </remarks>
[Collection("api")]
public sealed class ApprovalLaneRbacTests(IntegrationFixture fixture)
{
    private const string Module = "compliance";
    private const string WriteTool = "propose_control_change";
    private const string WritePrompt = "Propose moving A.8.7 to NotImplemented because the scanner lapsed";

    /// <summary>The role from the issue: approval authority and not one compliance permission.</summary>
    private const string ApproverOnly = "approver_only";

    [Fact]
    public async Task An_approver_without_the_tools_permission_cannot_commit_its_write()
    {
        using var admin = fixture.AdminClient();
        await EnsureApproverOnlyRoleAsync(admin);

        // Pat holds chat.approvals.manage and nothing else. RBAC-before-the-model already refuses
        // him this tool — assert that first, so the test cannot pass on a Pat who was quietly
        // entitled all along.
        using var pat = fixture.AdminClient(roles: ApproverOnly, subject: "pat.approver");
        Assert.Equal(HttpStatusCode.Forbidden, (await pat.GetAsync("/api/compliance/controls")).StatusCode);
        var patsOwnTurn = await AguiStream.PostAsync(pat, Module, WritePrompt);
        Assert.DoesNotContain(WriteTool, patsOwnTurn.ToolCalls);

        // Erin may propose, and may not approve. She parks the write through the real path.
        using var erin = fixture.AdminClient(roles: "compliance-analyst", subject: "erin.analyst");
        var turn = await AguiStream.PostAsync(erin, Module, WritePrompt);
        Assert.Contains(WriteTool, turn.ToolCalls);
        Assert.True(turn.RequiredApproval, "The analyst's write did not park on the gate.");

        var id = await FindApprovalIdAsync(admin, turn.ConversationId);

        var (scope, _, _) = await fixture.AuthorizedScopeAsync();
        using var _scope = scope;
        var compliance = scope.ServiceProvider.GetRequiredService<ComplianceDbContext>();
        var before = await compliance.Controls.AsNoTracking().FirstAsync(c => c.Reference == "A.8.7");

        // Arguments that would really commit, and a target the row is not already in — so "unchanged"
        // below cannot be true by coincidence.
        var target = before.Status == ControlStatus.NotImplemented
            ? ControlStatus.Effective
            : ControlStatus.NotImplemented;
        await RewriteParkedArgumentsAsync(scope, id, "A.8.7", target);

        var response = await pat.PostAsync($"/api/chat/approvals/{id}/approve", null);

        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);
        Assert.Contains(
            "tools.compliance.propose_control_change",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // The response is not the evidence — the row is.
        var after = await compliance.Controls.AsNoTracking().FirstAsync(c => c.Reference == "A.8.7");
        Assert.Equal(before.Status, after.Status);
    }

    /// <summary>
    /// The other half of the same rule: the check must not have closed the lane for the people it is
    /// for.
    /// </summary>
    /// <remarks>
    /// <c>compliance-owner</c> holds <c>tools.compliance.*</c> — a wildcard, not the literal string —
    /// so this also pins that the execution-time check uses the platform's own matcher semantics
    /// rather than an equality test that would deny the accountable role.
    /// </remarks>
    [Fact]
    public async Task An_approver_who_does_hold_the_permission_still_commits_the_write()
    {
        using var admin = fixture.AdminClient();
        using var erin = fixture.AdminClient(roles: "compliance-analyst", subject: "erin.analyst");
        using var olivia = fixture.AdminClient(roles: "compliance-owner", subject: "olivia.owner");

        var turn = await AguiStream.PostAsync(erin, Module, WritePrompt);
        var id = await FindApprovalIdAsync(admin, turn.ConversationId);

        var (scope, _, _) = await fixture.AuthorizedScopeAsync();
        using var _scope = scope;
        var compliance = scope.ServiceProvider.GetRequiredService<ComplianceDbContext>();
        var before = await compliance.Controls.AsNoTracking().FirstAsync(c => c.Reference == "A.5.15");
        var target = before.Status == ControlStatus.Effective
            ? ControlStatus.Implemented
            : ControlStatus.Effective;
        await RewriteParkedArgumentsAsync(scope, id, "A.5.15", target);

        var response = await olivia.PostAsync($"/api/chat/approvals/{id}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await compliance.Controls.AsNoTracking().FirstAsync(c => c.Reference == "A.5.15");
        Assert.Equal(target, after.Status);
    }

    private static async Task EnsureApproverOnlyRoleAsync(HttpClient admin)
    {
        var created = await admin.PostAsJsonAsync("/api/admin/roles", new
        {
            role = ApproverOnly,
            permissions = new[] { "chat.use", "chat.conversations.view", "chat.approvals.manage" },
        });

        // Conflict means a previous test in this collection already created it, which is the state
        // this method is asking for. Anything else is a broken precondition and must not be swallowed.
        if (created.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.Conflict))
        {
            Assert.Fail($"Could not create '{ApproverOnly}': {created.StatusCode} {await created.Content.ReadAsStringAsync()}");
        }
    }

    private static async Task RewriteParkedArgumentsAsync(
        IServiceScope scope, Guid id, string reference, ControlStatus target)
    {
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var parked = await platform.PendingApprovals.FirstAsync(a => a.Id == id);
        parked.ArgumentsJson =
            $$"""{"reference":"{{reference}}","status":"{{target}}","reason":"Approval-lane RBAC regression test"}""";
        await platform.SaveChangesAsync();
    }

    /// <summary>
    /// The queue is tenant-wide, so the approval is matched on its own conversation — taking "the
    /// first pending approval" would resolve another test's call.
    /// </summary>
    private static async Task<Guid> FindApprovalIdAsync(HttpClient admin, Guid? conversationId)
    {
        Assert.NotNull(conversationId);

        var queue = JsonDocument.Parse(await admin.GetStringAsync("/api/chat/approvals")).RootElement;
        foreach (var approval in queue.EnumerateArray())
        {
            if (approval.GetProperty("conversationId").GetGuid() == conversationId)
            {
                return approval.GetProperty("id").GetGuid();
            }
        }

        Assert.Fail($"No pending approval found for conversation {conversationId}.");
        return Guid.Empty;
    }
}
