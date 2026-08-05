using System.Text.Json;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// The shipped role baselines, read back from the running platform.
/// </summary>
/// <remarks>
/// <para>
/// SECURITY.md, SPEC.md §6 and PLAN.md §7 all state the same load-bearing rule:
/// <c>compliance-analyst</c> may propose state changes and may approve none of them, and holds an
/// <b>enumerated</b> allowlist — never a <c>tools.compliance.*</c> wildcard. PLAN.md calls that
/// exclusion "the load-bearing property of this whole model".
/// </para>
/// <para>
/// <b>Why this file exists rather than relying on the behavioural tests.</b>
/// <c>ChatAndApprovalTests.An_analyst_may_propose_but_never_clear_their_own_gate</c> proves the
/// <c>chat.approvals.manage</c> half at runtime, through a real 403. The <b>wildcard</b> half has no
/// behavioural proof and cannot have one today: every tool that currently exists is already in the
/// analyst's enumerated list, so swapping that list for <c>tools.compliance.*</c> would be
/// indistinguishable — every other test would stay green. The hole opens the moment epic 3 adds
/// <c>review_evidence</c>, the tool SPEC.md deliberately withholds from the analyst: a wildcard
/// would grant it silently, and the failure would arrive with the feature rather than with the
/// mistake that caused it.
/// </para>
/// <para>
/// Read through <see cref="IntegrationFixture.AdminClient"/> — this is a claim about what the
/// platform actually seeded, not about what a source file says.
/// </para>
/// </remarks>
[Collection("api")]
public sealed class RoleBaselineTests(IntegrationFixture fixture)
{
    private const string CompliancePrefix = "tools.compliance.";

    [Fact]
    public async Task The_analyst_holds_an_enumerated_allowlist_and_never_a_compliance_wildcard()
    {
        var analyst = await PermissionsForAsync("compliance-analyst");

        var wildcards = analyst
            .Where(p => p.StartsWith(CompliancePrefix, StringComparison.Ordinal) && p.EndsWith('*'))
            .ToList();

        Assert.True(wildcards.Count == 0,
            "compliance-analyst holds a compliance wildcard: " + string.Join(", ", wildcards)
            + ". A wildcard silently grants every future write tool the module gains — including "
            + "review_evidence, which SPEC.md §6 withholds from this role. Enumerate instead.");

        // Not a wildcard is only half of it: the grant must still be a real enumeration of the
        // proposing tools, or this test would pass just as happily against an empty role.
        Assert.Contains(CompliancePrefix + "propose_control_change", analyst);
        Assert.Contains(CompliancePrefix + "list_controls", analyst);

        // The other half of the same rule, asserted here as configuration to sit beside the
        // behavioural proof in ChatAndApprovalTests.
        Assert.DoesNotContain("chat.approvals.manage", analyst);
    }

    /// <summary>
    /// Proves the detector above can actually see a wildcard, using the role that legitimately has one.
    /// </summary>
    /// <remarks>
    /// Without this, a typo in the matching logic would make the analyst test pass for the wrong
    /// reason — the classic way a security assertion becomes decoration. <c>compliance-owner</c> is
    /// the accountable role and holds <c>tools.compliance.*</c> deliberately, so it is the natural
    /// positive control.
    /// </remarks>
    [Fact]
    public async Task The_owner_does_hold_the_wildcard_so_the_detector_is_known_to_work()
    {
        var owner = await PermissionsForAsync("compliance-owner");

        Assert.Contains(owner, p =>
            p.StartsWith(CompliancePrefix, StringComparison.Ordinal) && p.EndsWith('*'));
        Assert.Contains("chat.approvals.manage", owner);
    }

    private async Task<List<string>> PermissionsForAsync(string roleName)
    {
        using var client = fixture.AdminClient();
        var body = await client.GetStringAsync("/api/admin/roles");
        var root = JsonDocument.Parse(body).RootElement;

        var roles = root.ValueKind == JsonValueKind.Array
            ? root
            : root.GetProperty("roles");

        // The property is `role`, not `name` — confirmed against a live response. Matching the
        // wrong key made an earlier draft of this test throw on lookup and "fail" for a reason
        // that had nothing to do with wildcards, which is precisely the false red the guard below
        // exists to make impossible to miss.
        foreach (var role in roles.EnumerateArray())
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
