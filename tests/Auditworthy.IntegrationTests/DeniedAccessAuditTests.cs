using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// A permission denial leaves a trace an auditor can read (#25).
/// </summary>
/// <remarks>
/// <para>
/// RBAC held everywhere before this fix — the denials were correct, and this file does not re-prove
/// them (<c>ApprovalLaneRbacTests</c> and <c>ChatAndApprovalTests</c> own that). What was missing was
/// the <em>record</em>: <c>GET /api/admin/audit/auth-events</c> returned only <c>UserProvisioned</c>
/// rows, so nothing showed that an analyst had tried to clear their own gate. The committed security
/// catalog advertises that endpoint as "Sign-in and permission-denial events"; the denial half was
/// advertised and never written.
/// </para>
/// <para>
/// <b>The 403 is asserted first, deliberately.</b> If the endpoint ever stopped denying, the audit
/// assertion below would fail too — and would be read as "auditing broke" when the truth would be far
/// worse: the approvals queue had opened up to analysts. Asserting the refusal first keeps those two
/// failures distinguishable.
/// </para>
/// <para>
/// <b>Why this polls.</b> Audit writes go through the platform's outbox precisely so they never block
/// the user-facing response, so the row is not durable the instant the 403 is returned. A test that
/// read once and asserted would be a flake generator. It polls to a deadline and fails with what it
/// actually saw.
/// </para>
/// <para>
/// The subject is unique to this file because <c>[Collection("api")]</c> shares one host: filtering on
/// it keeps the assertion from passing on some other test's denial.
/// </para>
/// </remarks>
[Collection("api")]
public sealed class DeniedAccessAuditTests(IntegrationFixture fixture)
{
    /// <summary>Distinct from every other test's subject so a shared host cannot cross-contaminate.</summary>
    private const string Subject = "dana.denied";

    /// <summary>The permission the approvals queue requires and compliance-analyst must never hold.</summary>
    private const string ApprovalsPermission = "chat.approvals.manage";

    [Fact]
    public async Task A_permission_denial_is_recorded_as_an_auth_event()
    {
        using var dana = fixture.AdminClient(roles: "compliance-analyst", subject: Subject);

        // The refusal itself — the product's central claim, and the precondition for the audit row.
        var denied = await dana.GetAsync("/api/chat/approvals");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var entry = await WaitForDenialAsync();

        Assert.NotNull(entry);
        Assert.Equal(Subject, entry!.Value.GetProperty("subject").GetString());

        // The detail must name the permission, not just the route. "Someone was denied something,
        // somewhere" is not an audit trail — an auditor asks who was refused WHAT.
        var detail = entry.Value.GetProperty("detail").GetString();
        Assert.NotNull(detail);
        Assert.Contains(ApprovalsPermission, detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Polls the auth-event feed for this test's denial until it lands or the deadline passes.
    /// Returns null on timeout so the caller can produce a failure message about the audit row
    /// rather than an opaque timeout exception.
    /// </summary>
    private async Task<JsonElement?> WaitForDenialAsync()
    {
        using var admin = fixture.AdminClient();
        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < TimeSpan.FromSeconds(30))
        {
            var events = await admin.GetFromJsonAsync<JsonElement[]>("/api/admin/audit/auth-events");
            var match = (events ?? [])
                .Where(e => e.TryGetProperty("eventType", out var type)
                            && type.GetString() == "AccessDenied"
                            && e.TryGetProperty("subject", out var subject)
                            && subject.GetString() == Subject)
                .Cast<JsonElement?>()
                .FirstOrDefault();

            if (match is not null)
            {
                return match;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        return null;
    }
}
