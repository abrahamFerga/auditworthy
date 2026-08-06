using System.Text.Json;
using Auditworthy.Compliance;
using Plenipo.Application.Authorization;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// The shipped role baselines, asserted through the platform's own permission semantics.
/// </summary>
/// <remarks>
/// <para>
/// SECURITY.md, SPEC.md §6 and PLAN.md §7 all state the same load-bearing rule:
/// <c>compliance-analyst</c> may propose state changes and may approve none of them, and never holds
/// a wildcard that grants compliance tools. PLAN.md calls that exclusion "the load-bearing property
/// of this whole model".
/// </para>
/// <para>
/// <b>These assertions ask <see cref="PermissionMatcher"/> what is granted rather than inspecting the
/// spelling of the grant strings, and that distinction is the whole point of the file.</b> An earlier
/// draft matched <c>StartsWith("tools.compliance.") &amp;&amp; EndsWith('*')</c>, which looks equivalent and
/// is not: the platform honours <c>*</c> and <c>tools.*</c> as well, and neither starts with that
/// prefix. Appending <c>"tools.*"</c> to the analyst's list therefore passed every assertion while
/// granting the analyst every present and future compliance tool. A check written to a predicate
/// narrower than the rule it names is satisfiable by states that violate the rule.
/// </para>
/// <para>
/// Calling the platform's matcher also means this test cannot drift from the platform's semantics,
/// because it *is* the platform's semantics — including any future wildcard form the matcher learns.
/// </para>
/// <para>
/// <b>Why this file exists at all.</b>
/// <c>ChatAndApprovalTests.An_analyst_may_propose_but_never_clear_their_own_gate</c> proves the
/// approvals half at runtime through a real 403. The wildcard half has no behavioural proof and
/// cannot have one today: every tool that exists is already in the analyst's enumerated list, so a
/// wildcard is indistinguishable to every other test. The hole opens when epic 3 adds
/// <c>review_evidence</c> — a wildcard would grant it silently, and the failure would arrive with
/// the feature rather than with the mistake.
/// </para>
/// </remarks>
[Collection("api")]
public sealed class RoleBaselineTests(IntegrationFixture fixture)
{
    /// <summary>A tool the analyst must never reach — withheld by SPEC.md §6, not yet implemented.</summary>
    private static readonly string ReviewEvidence = Permissions.ForTool(ComplianceModule.Id, "review_evidence");

    /// <summary>Stands in for every write tool epic 3+ will add. A wildcard grants it; an enumeration cannot.</summary>
    private static readonly string FutureWriteTool = Permissions.ForTool(ComplianceModule.Id, "__future_write_tool");

    [Fact]
    public async Task The_analyst_is_granted_no_compliance_tool_it_does_not_enumerate()
    {
        var analyst = (await PermissionsForAsync("compliance-analyst")).ToHashSet(StringComparer.Ordinal);

        // Effect, not spelling: this fails for `*`, `tools.*`, `tools.compliance.*`, or an outright
        // grant of the withheld tool — every route to the same wrong outcome.
        Assert.False(PermissionMatcher.IsGranted(analyst, ReviewEvidence),
            "compliance-analyst is granted review_evidence, which SPEC.md §6 withholds from this role. "
            + $"Grants: {string.Join(", ", analyst.Order())}");

        Assert.False(PermissionMatcher.IsGranted(analyst, FutureWriteTool),
            "compliance-analyst is granted a compliance tool that does not exist yet, so it holds a "
            + $"wildcard. Every future write tool lands in this role silently. Grants: {string.Join(", ", analyst.Order())}");

        // The approvals half of the same rule. Also by effect: `chat.*` is a shape the platform ships
        // for tenant_admin, and an exact-string check would pass while the analyst held it.
        Assert.False(PermissionMatcher.IsGranted(analyst, Permissions.ManageApprovals),
            "compliance-analyst can clear its own gate — the approval lane is ceremony. "
            + $"Grants: {string.Join(", ", analyst.Order())}");

        // Not-granted is only half of it: the role must still actually enumerate its proposing tools,
        // or an empty role would satisfy everything above. GET /api/admin/roles returns [] rather than
        // 404 for a role with no rows, so this is a reachable state, not a hypothetical one.
        foreach (var tool in new[] { "list_controls", "get_control", "propose_control_change" })
        {
            Assert.Contains(Permissions.ForTool(ComplianceModule.Id, tool), analyst);
        }
    }

    /// <summary>
    /// Proves the assertions above can actually fail, on every wildcard shape the platform honours.
    /// </summary>
    /// <remarks>
    /// The previous version of this control used only <c>compliance-owner</c>, which holds
    /// <c>tools.compliance.*</c> — the one shape the old detector could see. It therefore certified
    /// the narrow path and could not expose the gap that made this file wrong. These literal sets need
    /// no host and cover the shapes that actually slipped through.
    /// </remarks>
    [Theory]
    [InlineData("*")]
    [InlineData("tools.*")]
    [InlineData("tools.compliance.*")]
    public void The_predicate_fires_on_every_wildcard_the_platform_honours(string wildcard)
    {
        var granted = new HashSet<string>(StringComparer.Ordinal) { "chat.use", wildcard };

        Assert.True(PermissionMatcher.IsGranted(granted, ReviewEvidence),
            $"\"{wildcard}\" is honoured by the platform but this predicate does not see it — the "
            + "guard above would pass while the analyst held it.");
        Assert.True(PermissionMatcher.IsGranted(granted, FutureWriteTool));
    }

    [Fact]
    public void An_exact_enumeration_grants_nothing_beyond_itself()
    {
        var granted = new HashSet<string>(StringComparer.Ordinal)
        {
            Permissions.ForTool(ComplianceModule.Id, "list_controls"),
            Permissions.ForTool(ComplianceModule.Id, "propose_control_change"),
        };

        // The negative half of the control: the predicate must not fire on a correct baseline, or
        // the guard above would be unfailable-by-construction in the other direction.
        Assert.False(PermissionMatcher.IsGranted(granted, ReviewEvidence));
        Assert.False(PermissionMatcher.IsGranted(granted, FutureWriteTool));
    }

    [Fact]
    public async Task The_owner_does_hold_the_wildcard_and_may_clear_the_gate()
    {
        var owner = (await PermissionsForAsync("compliance-owner")).ToHashSet(StringComparer.Ordinal);

        // The accountable role is the deliberate mirror image of the analyst.
        Assert.True(PermissionMatcher.IsGranted(owner, ReviewEvidence));
        Assert.True(PermissionMatcher.IsGranted(owner, Permissions.ManageApprovals));
    }

    private async Task<List<string>> PermissionsForAsync(string roleName)
    {
        using var client = fixture.AdminClient();
        var body = await client.GetStringAsync("/api/admin/roles");
        var root = JsonDocument.Parse(body).RootElement;

        // The property is `role`, not `name` — AdminEndpoints returns RoleDto(Role, Permissions,
        // Editable, BuiltIn). An earlier draft matched `name`, threw here, and produced a red run
        // that proved nothing about wildcards. A red whose message goes unread is not a proof.
        foreach (var role in root.EnumerateArray())
        {
            if (!role.TryGetProperty("role", out var name) || name.GetString() != roleName)
            {
                continue;
            }

            return role.GetProperty("permissions")
                .EnumerateArray()
                .Select(p => p.GetString() ?? string.Empty)
                .ToList();
        }

        throw new InvalidOperationException(
            $"Role '{roleName}' is not in GET /api/admin/roles. Either Program.cs no longer registers "
            + "it, or the response shape changed — both are worth failing loudly over.");
    }
}
