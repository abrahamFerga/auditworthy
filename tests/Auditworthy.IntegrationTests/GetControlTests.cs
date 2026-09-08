using Auditworthy.Compliance;
using Auditworthy.Compliance.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plenipo.Infrastructure.Context;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// The read tool's own behaviour — the half of #11 that registration could never prove.
/// </summary>
/// <remarks>
/// <para>
/// <c>get_control</c> shipped with the walking skeleton and has been registered in both places since,
/// but until this file <b>no committed test ever executed it</b>. The only two references in
/// <c>tests/</c> were to its permission STRING (<c>RoleBaselineTests</c>, <c>SmokeTests</c>), which
/// proves the wiring and says nothing about what the tool returns. A tool that returned the empty
/// string, another control's detail, or another tenant's row would have satisfied every assertion in
/// the suite.
/// </para>
/// <para>
/// <b>Why an unknown reference is asserted to RETURN rather than to throw, unlike its sibling.</b>
/// <c>ProposeControlChangeTests.An_unknown_reference_throws_instead_of_returning_a_description</c>
/// pins the opposite behaviour for <c>propose_control_change</c>, and the difference is deliberate
/// and documented on <c>ComplianceTools</c>: a returned string resolves an approval as
/// <c>Executed</c> with <c>error: null</c>, so a gated WRITE that did not happen must throw or it
/// lies to the person accountable for it. An ungated READ has a retry loop — the model can call
/// again with a better reference — so describing the miss is the right answer there. What must not
/// happen is the read failing *dirtily*: throwing into the runner, returning nothing, or handing
/// back some other control. That is what is asserted below.
/// </para>
/// <para>
/// The direct-call cases run through <see cref="IntegrationFixture.AuthorizedScopeAsync"/>, which
/// bypasses RBAC and the approval gate — nothing here may be read as evidence about either. The two
/// claims that ARE security-shaped (the tenant filter, and a <c>compliance-reader</c> reaching the
/// tool) go through <see cref="RequestContext"/> and <c>AdminClient</c> respectively.
/// </para>
/// </remarks>
[Collection("api")]
public sealed class GetControlTests(IntegrationFixture fixture)
{
    /// <summary>Criterion 1 — it returns THAT control's detail, not merely a string.</summary>
    /// <remarks>
    /// Asserted against the row read out of the database rather than against literals copied from
    /// <c>StarterRegister</c>. A test carrying its own copy of the seed data passes when the tool
    /// and the seed drift apart in the same direction, which is exactly the drift worth catching.
    /// </remarks>
    [Fact]
    public async Task A_seeded_reference_returns_that_controls_detail()
    {
        var (scope, _, _) = await fixture.AuthorizedScopeAsync();
        using var _scope = scope;
        var tools = scope.ServiceProvider.GetRequiredService<ComplianceTools>();
        var db = scope.ServiceProvider.GetRequiredService<ComplianceDbContext>();

        var expected = await db.Controls.AsNoTracking().FirstAsync(c => c.Reference == "A.5.1");

        var result = await tools.GetControlAsync("A.5.1");

        // Every field the tool claims to project, each asserted separately: they fail
        // independently, and a single Contains on the reference would stay green while the name,
        // status, owner or description silently vanished from the detail.
        Assert.Contains(expected.Reference, result, StringComparison.Ordinal);
        Assert.Contains(expected.Name, result, StringComparison.Ordinal);
        Assert.Contains(expected.Status.ToString(), result, StringComparison.Ordinal);
        Assert.Contains(expected.Owner!, result, StringComparison.Ordinal);
        Assert.Contains(expected.Description!, result, StringComparison.Ordinal);

        // …and it is ONE control's detail, not the register. A `get_control` that quietly delegated
        // to `ListControlsAsync` would satisfy every assertion above, because the list contains
        // A.5.1 too. Naming a sibling reference is what separates the two.
        var sibling = await db.Controls.AsNoTracking().FirstAsync(c => c.Reference != "A.5.1");
        Assert.DoesNotContain(sibling.Name, result, StringComparison.Ordinal);
    }

    /// <summary>
    /// Criterion 2 — before E2 lands, it returns the control ALONE without erroring.
    /// </summary>
    /// <remarks>
    /// Every seeded reference, not just the one the manifest's suggested prompt names. The
    /// linked-requirements half of criterion 2 is correctly deferred to epic 2; what is provable
    /// today is that the absence of requirement linkage is not an error path — no throw, no empty
    /// answer, and no "no control found" for a control that exists.
    /// </remarks>
    [Fact]
    public async Task Every_seeded_control_answers_alone_without_erroring()
    {
        var (scope, _, _) = await fixture.AuthorizedScopeAsync();
        using var _scope = scope;
        var tools = scope.ServiceProvider.GetRequiredService<ComplianceTools>();
        var db = scope.ServiceProvider.GetRequiredService<ComplianceDbContext>();

        var references = await db.Controls.AsNoTracking()
            .Select(c => c.Reference)
            .ToListAsync();

        // Without this the loop below iterates nothing and the test passes vacuously the moment
        // seeding regresses — the classic silent no-op test.
        Assert.NotEmpty(references);

        foreach (var reference in references)
        {
            var result = await tools.GetControlAsync(reference);

            Assert.False(string.IsNullOrWhiteSpace(result), $"{reference} returned nothing.");
            Assert.DoesNotContain("No control found", result, StringComparison.Ordinal);
            Assert.Contains(reference, result, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// An unknown reference misses cleanly: it says so, and it hands back no control at all.
    /// </summary>
    [Fact]
    public async Task An_unknown_reference_reports_the_miss_and_returns_no_control()
    {
        var (scope, _, _) = await fixture.AuthorizedScopeAsync();
        using var _scope = scope;
        var tools = scope.ServiceProvider.GetRequiredService<ComplianceTools>();
        var db = scope.ServiceProvider.GetRequiredService<ComplianceDbContext>();

        // No throw: for an ungated read the model retries, and a thrown exception costs it that
        // retry. Assert.ThrowsAsync would be the wrong shape here — see the remarks on the class.
        var result = await tools.GetControlAsync("NO-SUCH-REF");

        Assert.Contains("No control found", result, StringComparison.Ordinal);
        Assert.Contains("NO-SUCH-REF", result, StringComparison.Ordinal);

        // The load-bearing half: a miss must not fall back to *some* control. Nothing in the
        // register may appear in the answer.
        var seeded = await db.Controls.AsNoTracking().ToListAsync();
        Assert.NotEmpty(seeded);

        foreach (var control in seeded)
        {
            Assert.DoesNotContain(control.Name, result, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Criterion 3, first half — the tool is tenant-filtered, proven by a real query.
    /// </summary>
    /// <remarks>
    /// <c>ControlsRegisterTests.Controls_are_invisible_from_another_tenant</c> proves the
    /// <c>HasQueryFilter</c> on the <c>DbSet</c>. That is the same <c>DbSet</c> this tool reads, so
    /// the filter was inherited — but "inherited" was an inference, and inferences are how a tool
    /// that later reaches for <c>IgnoreQueryFilters</c> ships unnoticed. This asks the TOOL, from a
    /// scope pointed at a tenant that owns nothing, and requires it to find A.5.1 nowhere — even
    /// though A.5.1 exists, seeded, one tenant away.
    /// </remarks>
    [Fact]
    public async Task A_control_in_another_tenant_is_invisible_to_get_control()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RequestContext>();
        context.SetTenant(Guid.NewGuid());

        var db = scope.ServiceProvider.GetRequiredService<ComplianceDbContext>();
        var tools = scope.ServiceProvider.GetRequiredService<ComplianceTools>();

        // Otherwise a wholly empty database would satisfy the assertion below and prove nothing.
        var elsewhere = await db.Controls.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(c => c.Reference == "A.5.1");

        var result = await tools.GetControlAsync("A.5.1");

        Assert.Contains("No control found", result, StringComparison.Ordinal);
        Assert.DoesNotContain(elsewhere.Name, result, StringComparison.Ordinal);
    }

    /// <summary>
    /// Criterion 1 and 3, behaviourally — a <c>compliance-reader</c> reaches the tool over AG-UI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The narrowest role the product ships, driving the real HTTP pipeline through
    /// <c>AdminClient</c>, so RBAC actually runs: RBAC-before-the-model means an unpermitted tool is
    /// never put in front of the model at all, and a reader that had lost
    /// <c>tools.compliance.get_control</c> would produce a turn with no <c>get_control</c> call
    /// rather than an error. Deleting that grant from <c>Program.cs</c>'s reader baseline is what
    /// turns this red.
    /// </para>
    /// <para>
    /// The Mock provider routes by name-token match and fills string arguments from QUOTED spans,
    /// so the prompt quotes the reference and uses the tool's own tokens ("get", "control",
    /// singular) while keeping the plural "controls" out — that plural is what routes a turn to
    /// <c>list_controls</c> instead, as <c>IntegrationFixture.WritePrompt</c> documents.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_reader_can_call_get_control_over_agui()
    {
        using var reader = fixture.AdminClient(roles: "compliance-reader", subject: "it-reader");

        var turn = await AguiStream.PostAsync(reader, "compliance", "Please get control 'A.5.1' in detail.");

        Assert.False(turn.Failed, $"RUN_ERROR: {turn.Error}");
        Assert.Contains("get_control", turn.ToolCalls);

        // A read is not approval-gated, and a reader could not clear a gate anyway — a turn that
        // parked here would be a broken gate, not a cautious one.
        Assert.False(turn.RequiredApproval);
    }
}
