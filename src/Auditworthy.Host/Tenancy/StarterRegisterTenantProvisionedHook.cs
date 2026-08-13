using Plenipo.Application.Commerce;

namespace Auditworthy.Host.Tenancy;

/// <summary>
/// The platform's own seam for "a tenant was just provisioned" — used for what it is for.
/// </summary>
/// <remarks>
/// <c>ITenantProvisionedHook</c> fires after <c>ITenantProvisioningService.ProvisionAsync</c>
/// commits, so it covers <c>POST /api/admin/tenants/provision</c> and the billing worker (verified
/// against <c>Plenipo.Infrastructure/Commerce/TenantProvisioningService.cs</c> at alpha.28, not
/// against documentation). It does <b>not</b> cover the bare <c>POST /api/admin/tenants</c>, which
/// inserts a <c>Tenant</c> row directly and fires nothing — that path is covered by
/// <see cref="CreatedTenantRegisterMiddleware"/> until the platform closes the gap.
/// </remarks>
public sealed class StarterRegisterTenantProvisionedHook(NewTenantRegisterProvisioner provisioner)
    : ITenantProvisionedHook
{
    public Task OnTenantProvisionedAsync(
        TenantProvisionedContext context, CancellationToken cancellationToken = default) =>
        provisioner.EnsureStarterRegisterAsync(context.TenantId, context.Slug, cancellationToken);
}
