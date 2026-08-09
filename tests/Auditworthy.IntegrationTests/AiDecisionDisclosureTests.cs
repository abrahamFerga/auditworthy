using System.Net;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// The ADMT disclosure surface (<c>GET /api/platform/ai-decisions</c>) must be gated on
/// <c>compliance.view</c>, like every other door onto the same material.
/// </summary>
/// <remarks>
/// <para>
/// The platform maps this endpoint with a bare <c>RequireAuthorization()</c> — deliberately, so any
/// authenticated member of a tenant can read their own AI-decision history (CPPA ADMT transparency;
/// the platform pins it with <c>A_plain_user_needs_no_admin_permission_to_read_their_disclosure</c>).
/// For a generic product that is right. For Auditworthy it is a disclosure hole: the rows carry the
/// control reference, the proposed status and the reason text — the same compliance-register content
/// that <c>/api/compliance/controls</c> refuses without <c>compliance.view</c>. Two doors onto one
/// body of data, one of them unlocked, is the RBAC-before-the-model invariant broken.
/// </para>
/// <para>
/// These go through real HTTP with dev-auth headers rather than <c>AuthorizedScopeAsync()</c>, which
/// sets the identity itself and bypasses RBAC — it could never fail while the gate is missing.
/// </para>
/// </remarks>
[Collection("api")]
public sealed class AiDecisionDisclosureTests(IntegrationFixture fixture)
{
    private const string Disclosure = "/api/platform/ai-decisions";

    [Fact]
    public async Task A_caller_with_no_permissions_cannot_read_the_ai_decision_disclosure()
    {
        // Issue #70, reproduced: an authenticated principal holding a role that grants nothing at
        // all read six rows of tenant-wide register content. Authenticated is not authorized.
        using var nobody = fixture.AdminClient(roles: "nobody", subject: "admt-nobody");

        var response = await nobody.GetAsync(Disclosure);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_same_caller_is_refused_the_register_itself()
    {
        // The contrast that makes the line above a hole rather than a policy: the module's own
        // route already refuses this caller. If this ever goes green while the test above is red,
        // the two doors have diverged again.
        using var nobody = fixture.AdminClient(roles: "nobody", subject: "admt-nobody");

        var response = await nobody.GetAsync("/api/compliance/controls");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_compliance_reader_still_reads_the_disclosure()
    {
        // The counterweight. Closing the hole by denying everyone would "pass" the test above and
        // destroy the transparency surface the platform put there on purpose. Every role that holds
        // compliance.view must still get through.
        using var reader = fixture.AdminClient(roles: "compliance-reader", subject: "admt-reader");

        var response = await reader.GetAsync(Disclosure);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_administrator_still_reads_the_disclosure()
    {
        // system_admin satisfies any permission through PermissionMatcher; a guard that broke this
        // would break the admin console's own view of the same data.
        using var admin = fixture.AdminClient();

        var response = await admin.GetAsync(Disclosure);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
