using Auditworthy.Compliance;
using Plenipo.Infrastructure.Context;

namespace Auditworthy.Host.Tenancy;

/// <summary>
/// Gives a tenant created at runtime the same starter control register the host seeds at startup.
/// </summary>
/// <remarks>
/// <para>
/// #78: <c>POST /api/admin/tenants</c> returned 201 and the tenant was then permanently unusable —
/// an empty register, no create-control endpoint, and the one write tool refusing every reference
/// with <c>422 No control found</c>. <c>ComplianceModule.SeedAsync</c> already knows how to give a
/// tenant a register; it had simply never run for any tenant except the one that existed at
/// startup. This is the missing call, not a second seeding implementation — the register lives in
/// <see cref="StarterRegister"/> and both callers use it, so no customer can receive a different
/// starting register depending on when they were created.
/// </para>
/// <para>
/// <b>Tenant isolation is preserved by construction.</b> It opens its own DI scope and establishes
/// the new tenant on that scope's <see cref="RequestContext"/> — exactly what the platform does in
/// <c>EstablishDevTenantContextAsync</c> before calling module seeding — so the module's
/// <c>HasQueryFilter</c> scopes both the "does it already have controls?" check and the write to
/// that one tenant. Nothing here calls <c>IgnoreQueryFilters</c>, and no filter is relaxed. The
/// scope is its own rather than the operator's request scope for the same reason: the operator's
/// scope carries the operator's tenant, and mutating it mid-request would point the rest of that
/// request at somebody else's data.
/// </para>
/// <para>
/// Failures are logged and never rethrown. The tenant already exists and its creation has already
/// been reported to the operator; an unseeded register is recoverable (any later provisioning of
/// the same tenant seeds it, because the seed is idempotent) whereas throwing here would turn a
/// created tenant into a 500 that says nothing about what happened. This mirrors the platform's own
/// rule for <c>ITenantProvisionedHook</c>: hooks run after the commit and a hook failure never
/// rolls the tenant back.
/// </para>
/// </remarks>
public sealed class NewTenantRegisterProvisioner(
    IServiceScopeFactory scopeFactory,
    ILogger<NewTenantRegisterProvisioner> logger)
{
    /// <summary>Seeds the starter register into <paramref name="tenantId"/> if it has none yet.</summary>
    public async Task EnsureStarterRegisterAsync(
        Guid tenantId, string? slug, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            // The mutable ITenantContext. Setting it here is what makes every read and write below
            // tenant-scoped; without it StarterRegister.SeedAsync sees no tenant and does nothing.
            var context = scope.ServiceProvider.GetRequiredService<RequestContext>();
            context.SetTenant(tenantId);

            var seeded = await StarterRegister.SeedAsync(scope.ServiceProvider, cancellationToken);

            if (seeded > 0)
            {
                logger.LogInformation(
                    "Seeded the starter control register ({Count} controls) into new tenant {Slug} ({TenantId}).",
                    seeded, slug ?? "(unknown slug)", tenantId);
            }
            else
            {
                // Not an error: a tenant that already holds controls must keep them. This is the
                // branch that makes the operation safe to run twice for the same tenant.
                logger.LogInformation(
                    "Tenant {Slug} ({TenantId}) already has a control register; left it untouched.",
                    slug ?? "(unknown slug)", tenantId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Failed to seed the starter control register into new tenant {Slug} ({TenantId}). "
              + "The tenant exists but its register is empty.",
                slug ?? "(unknown slug)", tenantId);
        }
    }
}
