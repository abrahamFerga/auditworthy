using Auditworthy.Compliance;
using Auditworthy.Compliance.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// The gated write's own behaviour, driven directly with arguments the Mock provider cannot produce.
/// </summary>
/// <remarks>
/// <para>
/// These run through <see cref="IntegrationFixture.AuthorizedScopeAsync"/>, which bypasses RBAC and
/// the approval gate — so nothing here may be read as evidence that either works. That claim belongs
/// to <c>ChatAndApprovalTests</c> and is asserted there through <c>AdminClient</c>.
/// </para>
/// <para>
/// This file exists because of a coverage hole worth naming: the Mock provider puts the user's whole
/// message in the first string parameter and <c>"example"</c> in every other, and
/// <c>POST /api/chat/approvals/{id}/approve</c> accepts no request body. So there is <b>no</b> HTTP
/// path that can supply a valid status. Calling the tool directly is the only rung where a valid
/// status can be exercised at all.
/// </para>
/// <para>
/// <b>This does NOT close #30, and must not be read as doing so.</b> What is proven below is that the
/// tool commits when invoked directly — not that an <i>approved</i> write commits. The link between
/// the approval lane and the commit stays unproven, because the fixture bypasses that lane by
/// design. #30 stays open until something can drive valid arguments through the lane itself.
/// </para>
/// </remarks>
[Collection("api")]
public sealed class ProposeControlChangeTests(IntegrationFixture fixture)
{
    [Fact]
    public async Task A_valid_status_is_applied_and_reported_as_a_completed_change()
    {
        var (scope, _, _) = await fixture.AuthorizedScopeAsync();
        using var _scope = scope;
        var tools = scope.ServiceProvider.GetRequiredService<ComplianceTools>();
        var db = scope.ServiceProvider.GetRequiredService<ComplianceDbContext>();

        var before = await db.Controls.AsNoTracking().FirstAsync(c => c.Reference == "A.5.1");
        var target = before.Status == ControlStatus.Effective
            ? ControlStatus.Implemented
            : ControlStatus.Effective;

        var result = await tools.ProposeControlChangeAsync("A.5.1", target.ToString(), "Regression test");

        Assert.Contains($"moved from {before.Status} to {target}", result);

        // The string is not the evidence — the row is.
        var after = await db.Controls.AsNoTracking().FirstAsync(c => c.Reference == "A.5.1");
        Assert.Equal(target, after.Status);
    }

    /// <summary>
    /// The failure this file was added for: a status that will not parse must THROW, not return.
    /// </summary>
    /// <remarks>
    /// A returned string is, to the platform, a tool that ran to completion — it resolves the
    /// approval as <c>Executed</c> with <c>error: null</c>. For a gated write that is a lie told to
    /// the person accountable for it. Read tools may keep describing their problems; this one may not.
    /// </remarks>
    [Fact]
    public async Task An_unparseable_status_throws_instead_of_returning_a_description()
    {
        var (scope, _, _) = await fixture.AuthorizedScopeAsync();
        using var _scope = scope;
        var tools = scope.ServiceProvider.GetRequiredService<ComplianceTools>();
        var db = scope.ServiceProvider.GetRequiredService<ComplianceDbContext>();

        var before = await db.Controls.AsNoTracking().FirstAsync(c => c.Reference == "A.5.1");

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => tools.ProposeControlChangeAsync("A.5.1", "example", "Regression test"));

        Assert.Contains("is not a valid status", ex.Message);

        var after = await db.Controls.AsNoTracking().FirstAsync(c => c.Reference == "A.5.1");
        Assert.Equal(before.Status, after.Status);
    }

    [Fact]
    public async Task An_unknown_reference_throws_instead_of_returning_a_description()
    {
        var (scope, _, _) = await fixture.AuthorizedScopeAsync();
        using var _scope = scope;
        var tools = scope.ServiceProvider.GetRequiredService<ComplianceTools>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tools.ProposeControlChangeAsync("NO-SUCH-REF", "Effective", "Regression test"));

        Assert.Contains("No control found", ex.Message);
    }
}
