using Microsoft.Extensions.Primitives;

namespace Auditworthy.Host.Tenancy;

/// <summary>
/// TODO(plenipo#172) — delete this middleware once <c>POST /api/admin/tenants</c> fires
/// <c>ITenantProvisionedHook</c> like the provisioning path already does.
/// <para>
/// The platform offers exactly one "a tenant now exists" seam, and only the one-call provisioning
/// endpoint reaches it. The bare create endpoint builds a <c>Tenant</c>, saves it and returns 201
/// with no hook, no event and no notification of any kind
/// (<c>Plenipo.AspNetCore/Endpoints/AdminEndpoints.cs</c>, <c>Admin_CreateTenant</c>, read at
/// v0.1.0-alpha.28 — the version this product vendors). That endpoint is the one #78 was reported
/// against, and a product cannot re-map or replace a platform route from its own module, so the
/// only thing left inside the product is to observe the outcome.
/// </para>
/// <para>
/// So this observes it, as narrowly as it can: one exact path, one method, one status code, and the
/// tenant id taken from the <c>Location</c> header the endpoint itself wrote
/// (<c>/api/admin/tenants/{id}</c>). It never inspects or buffers a request or response body, and
/// it does nothing at all to any other request. When the platform closes the gap this file and its
/// one line in <c>Program.cs</c> are deleted, and
/// <see cref="StarterRegisterTenantProvisionedHook"/> covers both paths on its own.
/// </para>
/// <para>
/// Known limitation, and the reason the platform fix is the real fix: seeding runs <i>after</i> the
/// 201 has been produced, so an operator who reads the register in the same millisecond can still
/// see it empty. Inside the provisioning transaction, where the platform can put it, that race does
/// not exist.
/// </para>
/// </summary>
public static class CreatedTenantRegisterMiddleware
{
    private const string TenantsPath = "/api/admin/tenants";

    /// <summary>
    /// Seeds the starter register for a tenant created through the bare admin create endpoint.
    /// Must be registered BEFORE <c>UsePlenipoPlatform()</c> so that it wraps the platform's
    /// endpoints and can see what they returned.
    /// </summary>
    public static IApplicationBuilder UseStarterRegisterForCreatedTenants(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            await next();

            // Exact path only. `/api/admin/tenants/provision` is deliberately NOT matched: the
            // platform fires ITenantProvisionedHook for it, and matching both would seed twice.
            if (!HttpMethods.IsPost(context.Request.Method)
                || !context.Request.Path.Equals(TenantsPath, StringComparison.OrdinalIgnoreCase)
                || context.Response.StatusCode != StatusCodes.Status201Created
                || !TryReadCreatedTenantId(context.Response.Headers.Location, out var tenantId))
            {
                return;
            }

            var provisioner = context.RequestServices.GetRequiredService<NewTenantRegisterProvisioner>();

            // CancellationToken.None on purpose: the tenant has been created and reported, and an
            // operator who hangs up between the 201 and the seed must not be the reason their
            // workspace is unusable. The work is a handful of inserts against a local connection.
            await provisioner.EnsureStarterRegisterAsync(tenantId, slug: null, CancellationToken.None);
        });

    /// <summary>Reads the tenant id out of the <c>Location</c> header the create endpoint wrote.</summary>
    private static bool TryReadCreatedTenantId(StringValues location, out Guid tenantId)
    {
        tenantId = Guid.Empty;

        if (location.Count != 1 || location[0] is not { } value)
        {
            return false;
        }

        // Anchored on the path the endpoint itself produced, so a redirect or a different Created()
        // result somewhere else can never be mistaken for a tenant.
        const string prefix = TenantsPath + "/";
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(value[prefix.Length..], out tenantId);
    }
}
