using Auditworthy.Compliance;
using Microsoft.AspNetCore.Authorization;
using Plenipo.Core.Identity;

namespace Auditworthy.Host.Authorization;

/// <summary>
/// TODO(plenipo#155) — remove this once the platform can gate the ADMT disclosure surface.
/// <para>
/// The platform maps <c>GET /api/platform/ai-decisions</c> with a bare <c>RequireAuthorization()</c>
/// (<c>Plenipo.AspNetCore/Endpoints/DisclosureEndpoints.cs</c>, alpha.28), so ANY authenticated
/// member of the tenant reads it. That is deliberate, not a slip: the endpoint is the automated-
/// decision transparency surface CPPA ADMT rules expect, and the platform pins the behaviour with
/// <c>A_plain_user_needs_no_admin_permission_to_read_their_disclosure</c>. Transparency only an
/// administrator can see is not transparency.
/// </para>
/// <para>
/// For Auditworthy that generic default is a disclosure hole. Every AI decision in this product IS
/// compliance-register content — the rows carry the control reference, the proposed status, the
/// reason text, who proposed it and who approved it — and this product already gates exactly that
/// material behind <c>compliance.view</c>: <c>ComplianceModule.cs:293</c> puts
/// <c>RequireAuthorization(PermissionRequirement.PolicyName(ViewCompliance))</c> on
/// <c>/api/compliance/controls</c>, and <c>ComplianceModule.cs:101</c> puts the same permission on
/// the Controls tab that reads it. (The gate is a source fact, not a spec one —
/// <c>compliance.view</c> appears nowhere in <c>SPEC.md</c>, whose §6 names only the role baselines
/// and <c>tools.compliance.*</c>.) That endpoint enforces it and returns
/// 403; the disclosure route handed the same material to a caller whose <c>/api/platform/me</c>
/// reported <c>"permissions":[]</c>. Two doors onto one body of data with one of them unlocked is
/// the RBAC-before-the-model invariant broken, and the check belongs in front of the data rather
/// than in front of one of the two routes that return it. See issue #70.
/// </para>
/// <para>
/// Strictly additive, and deliberately not a replacement. The seam is that ASP.NET Core invokes
/// EVERY registered <see cref="IAuthorizationHandler"/> on every authorization evaluation, and a
/// single <see cref="AuthorizationHandlerContext.Fail()"/> is terminal regardless of what else
/// succeeded. So this adds a requirement the platform's policy does not carry without touching the
/// platform's own registrations. The obvious alternative — replacing
/// <c>IAuthorizationMiddlewareResultHandler</c>, the seam this product already uses for
/// <c>IRequestEnricher</c> — was rejected: alpha.28 registers none, but a later platform version
/// does (<c>UnresolvedTenantAuthorizationResultHandler</c>), and a "last wins" registration would
/// then silently delete that handler on upgrade. Failing the requirement leaves the platform's
/// 403 pipeline, including any future result handler, entirely intact.
/// </para>
/// <para>
/// The trade-off is recorded rather than hidden: a user who holds no <c>compliance.view</c> now
/// gets no ADMT disclosure at all, where the platform intended a scoped view of their own tenant's
/// history. In this product every role that has any business reading an AI decision already holds
/// <c>compliance.view</c> (all three compliance baselines do), so the practical surface lost is
/// nil — but a caller-scoped filter is the better answer and belongs in the platform, which is what
/// the linked request asks for.
/// </para>
/// </summary>
public sealed class AiDecisionDisclosureGuard : IAuthorizationHandler
{
    /// <summary>
    /// Matched on the route rather than the endpoint name. The path is the product's public
    /// contract and the exact thing issue #70 reproduced against; the endpoint's name
    /// (<c>Disclosure_ListAiDecisions</c>) is a platform internal that can be renamed without any
    /// caller noticing — and a guard that silently stops matching is worse than no guard.
    /// </summary>
    private const string DisclosurePath = "/api/platform/ai-decisions";

    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Under endpoint routing the resource IS the HttpContext. If that ever stops being true the
        // guard would silently pass everything, which is why the regression test drives real HTTP.
        if (context.Resource is not HttpContext http)
        {
            return Task.CompletedTask;
        }

        if (!http.Request.Path.StartsWithSegments(DisclosurePath, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        // Scoped service, singleton handler — resolve from the request, not from a captured root.
        // Enrichment runs between UseAuthentication() and UseAuthorization(), so the permission set
        // is already resolved here; this is the same service the platform's own permission handler
        // consults, so wildcards and system_admin are honoured identically.
        var currentUser = http.RequestServices.GetService<ICurrentUser>();

        if (currentUser is null || !currentUser.HasPermission(ComplianceModule.ViewCompliance))
        {
            // Fail-closed when the service is missing: an unresolvable permission set must not read
            // compliance content. Fail() beats any other handler's success, which is the point.
            context.Fail(new AuthorizationFailureReason(
                this,
                $"Reading the AI decision disclosure requires '{ComplianceModule.ViewCompliance}', "
                + "because it discloses compliance register content."));
        }

        return Task.CompletedTask;
    }
}
