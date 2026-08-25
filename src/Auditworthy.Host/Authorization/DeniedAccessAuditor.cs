using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Plenipo.Application.Auditing;
using Plenipo.Application.Authorization;
using Plenipo.Infrastructure.Context;

namespace Auditworthy.Host.Authorization;

/// <summary>
/// Records an <see cref="AuthAuditEventType.AccessDenied"/> event every time authorization refuses a
/// request, so a permission denial leaves a trace an auditor can read.
/// <para>
/// Issue #25: every 403 in this product was invisible. RBAC itself held everywhere — the denials
/// were correct — but <c>GET /api/admin/audit/auth-events</c> returned only
/// <c>UserProvisioned</c> rows, so nothing recorded that a reader had probed the approvals queue or
/// that an analyst had tried to clear their own gate. In a compliance product that is the one
/// surface an owner would use to notice probing, and the committed security catalog already
/// advertises the endpoint as "Sign-in and permission-denial events". The denial half was
/// advertised and never written.
/// </para>
/// <para>
/// <b>The platform is not missing the concept — only the write.</b>
/// <see cref="AuthAuditEventType.AccessDenied"/> is declared at
/// <c>Plenipo.Application/Auditing/AuthAuditEntry.cs</c>, and
/// <see cref="IAuditLog.RecordAuthEventAsync"/> is the public, product-callable way to append one;
/// <see cref="AuthAuditEntry.Detail"/> is documented for exactly this ("the permission affected or
/// the endpoint that denied access"). Read from source at <c>v0.1.0-alpha.28</c>, not from
/// documentation. Nothing in the platform calls it for an authorization failure.
/// </para>
/// <para>
/// The escalation ladder, in order, before writing anything here:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Is it already there?</b> No — see above. The event type and the writer both exist; no caller does.
/// </description></item>
/// <item><description>
/// <b>Does a product seam cover it?</b> Yes. ASP.NET resolves a single
/// <see cref="IAuthorizationMiddlewareResultHandler"/> from DI and hands it EVERY authorization
/// outcome, failures included, which is the one place a denial is observable without wrapping
/// platform middleware. <b>At <c>v0.1.0-alpha.28</c> the platform registers none</b> — verified
/// against the vendored <c>Plenipo.AspNetCore.dll</c> itself, which contains no implementation of
/// that interface — so ASP.NET's own <see cref="AuthorizationMiddlewareResultHandler"/> is what
/// runs, and this type simply supplies the handler the DI container otherwise defaults in.
/// </description></item>
/// <item><description>
/// <b>Why not an <see cref="IAuthorizationHandler"/>?</b> A handler sees its own requirement, not
/// the verdict. It cannot distinguish "this requirement failed but another policy allowed the
/// request" from a genuine refusal, so auditing there would invent denials that never happened.
/// The result handler is the only seam that sees the decided outcome.
/// </description></item>
/// </list>
/// <para>
/// <b>This is still a shim, and the fix belongs upstream.</b> Every Plenipo product wants denial
/// auditing; none should have to write this class. A platform request has NOT been filed yet — that
/// is the next step for whoever picks this up, and this type should be deleted when it lands. It is
/// deliberately not tagged with an invented <c>plenipo#</c> number, because a wrong number sends the
/// next reader to an unrelated issue.
/// </para>
/// <para>
/// <b>Strictly additive.</b> ASP.NET's stock <see cref="AuthorizationMiddlewareResultHandler"/>
/// still makes every decision and still writes every response byte; this only appends an audit row
/// first. It is constructed directly rather than resolved from DI because this type occupies that
/// registration — resolving <see cref="IAuthorizationMiddlewareResultHandler"/> here would return
/// this instance and recurse.
/// </para>
/// <para>
/// <b>Upgrade hazard (#69).</b> A newer platform than <c>alpha.28</c> DOES ship its own handler
/// (<c>UnresolvedTenantAuthorizationResultHandler</c>, which gives a tenant-caused 403 a body that
/// names the cause). It is absent here, which is why delegating to the framework default is
/// correct <em>today</em> — but on the platform bump that introduces it, this registration would
/// silently displace it and that diagnostic would be lost. When upgrading: delegate to the
/// platform's handler instead of <see cref="AuthorizationMiddlewareResultHandler"/>, or delete this
/// class if the upgrade also brings denial auditing. Do not simply retest the 403 status and assume
/// nothing changed — the status is identical either way; it is the response BODY that goes missing.
/// </para>
/// </summary>
public sealed class DeniedAccessAuditor : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _inner = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        // Only a decided refusal of a known caller. A Challenge is a 401 — the client has not
        // failed a permission check, it has not identified itself yet, and recording those would
        // bury real denials under every unauthenticated probe.
        if (authorizeResult.Forbidden && context.User?.Identity?.IsAuthenticated == true)
        {
            await RecordDenialAsync(context, policy);
        }

        await _inner.HandleAsync(next, context, policy, authorizeResult);
    }

    private static async Task RecordDenialAsync(HttpContext context, AuthorizationPolicy policy)
    {
        try
        {
            // This handler is a singleton; IAuditLog and RequestContext are scoped, so both must be
            // resolved per request. Same reason the platform's handler resolves RequestContext here.
            var audit = context.RequestServices.GetService<IAuditLog>();
            if (audit is null)
            {
                return;
            }

            var requestContext = context.RequestServices.GetService<RequestContext>();

            await audit.RecordAuthEventAsync(
                new AuthAuditEntry
                {
                    EventType = AuthAuditEventType.AccessDenied,
                    TenantId = requestContext?.TenantId,
                    UserId = requestContext?.UserId,
                    Subject = requestContext?.Subject,
                    UserDisplay = requestContext?.DisplayName,
                    Detail = Describe(context, policy),
                    IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                },
                context.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An audit write must never turn a clean 403 into a 500. The platform's own contract is
            // that audit goes through an outbox and "never blocks or fails the user-facing
            // operation" — this catch keeps that true even if resolution or enqueue throws.
            context.RequestServices
                .GetService<ILoggerFactory>()
                ?.CreateLogger<DeniedAccessAuditor>()
                .LogWarning(ex, "Failed to record an AccessDenied audit event; the denial itself still stands.");
        }
    }

    /// <summary>
    /// Names what was refused: the permission the policy required, plus the route that refused it.
    /// The permission is the useful half — "who was denied compliance.manage" is the question an
    /// auditor asks — but a policy carrying no <see cref="PermissionRequirement"/> (a bare
    /// <c>RequireAuthorization()</c>, or a product-added handler like the AI-decision guard) still
    /// deserves a row, so the route stands in rather than dropping the event.
    /// </summary>
    private static string Describe(HttpContext context, AuthorizationPolicy policy)
    {
        var permissions = policy.Requirements
            .OfType<PermissionRequirement>()
            .Select(r => r.Permission)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var route = $"{context.Request.Method} {context.Request.Path}";

        return permissions.Length > 0
            ? $"Denied '{string.Join("', '", permissions)}' on {route}"
            : $"Denied on {route}";
    }
}
