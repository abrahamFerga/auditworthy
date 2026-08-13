using Auditworthy.Compliance.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plenipo.Core.Multitenancy;

namespace Auditworthy.Compliance;

/// <summary>
/// The starter control register, and the one operation that lands it in a tenant.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <see cref="ComplianceModule.SeedAsync"/> so that the register a tenant receives
/// does not depend on <i>when</i> that tenant was created. Host startup seeds the tenant that
/// exists then; a tenant created afterwards through the admin API is seeded by the host's tenant
/// provisioning seam — both through this method, so the two can never drift into giving different
/// customers different starting registers (#78).
/// </para>
/// <para>
/// It reads the tenant from the scope's <see cref="ITenantContext"/> and never takes a tenant id as
/// an argument. That is deliberate: the caller must establish the tenant on the scope, which means
/// the <c>Controls</c> query filter scopes the idempotency check to exactly the tenant being seeded
/// — no <c>IgnoreQueryFilters</c>, and therefore no way for this to read or write across the tenant
/// boundary.
/// </para>
/// </remarks>
public static class StarterRegister
{
    /// <summary>
    /// Seeds the starter register into the scope's tenant, unless it already has any control.
    /// </summary>
    /// <returns>How many controls were written — zero when there was no tenant, or nothing to do.</returns>
    public static async Task<int> SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        // No tenant means nothing to seed, and that is success, not an error. At host startup
        // outside Development the platform establishes no tenant at all, which is what keeps a
        // production deployment from inventing a register for whoever happens to boot it.
        var tenant = services.GetRequiredService<ITenantContext>();

        if (!tenant.HasTenant)
        {
            return 0;
        }

        var db = services.GetRequiredService<ComplianceDbContext>();

        // No IgnoreQueryFilters: the tenant is established, so the query filter scopes this count to
        // exactly the tenant being seeded — which is the check we want, and the reason this is safe
        // to run for a tenant that already has a register.
        if (await db.Controls.AnyAsync(cancellationToken))
        {
            return 0;
        }

        var controls = Controls(tenant.RequireTenantId()).ToList();
        db.Controls.AddRange(controls);
        await db.SaveChangesAsync(cancellationToken);
        return controls.Count;
    }

    /// <summary>
    /// A small ISO 27001 Annex A register with mixed statuses.
    /// </summary>
    /// <remarks>
    /// The references are clause identifiers; the wording is ours, not the standard's text. Statuses
    /// vary on purpose so the manifest's own suggested prompts — "Which controls are not yet
    /// effective?" and "Show me control A.5.1" — both return a real answer for any tenant.
    /// </remarks>
    internal static IEnumerable<Control> Controls(Guid tenantId) =>
    [
        new()
        {
            TenantId = tenantId,
            Reference = "A.5.1",
            Name = "Information security policy",
            Description = "A management-approved security policy exists, is published, and is reviewed at planned intervals.",
            Status = ControlStatus.Implemented,
            Owner = "Head of Security",
        },
        new()
        {
            TenantId = tenantId,
            Reference = "A.5.15",
            Name = "Access control",
            Description = "Access to information and other associated assets is granted on a business need, and reviewed.",
            Status = ControlStatus.Effective,
            Owner = "IT Manager",
        },
        new()
        {
            TenantId = tenantId,
            Reference = "A.5.23",
            Name = "Information security for cloud services",
            Description = "Acquisition, use and exit of cloud services follow the organisation's security requirements.",
            Status = ControlStatus.InProgress,
            Owner = "Head of Platform",
        },
        new()
        {
            TenantId = tenantId,
            Reference = "A.6.3",
            Name = "Security awareness and training",
            Description = "Staff receive security awareness training relevant to their role, on joining and periodically.",
            Status = ControlStatus.Implemented,
            Owner = "People Operations",
        },
        new()
        {
            TenantId = tenantId,
            Reference = "A.8.7",
            Name = "Protection against malware",
            Description = "Malware protection is implemented and supported by appropriate user awareness.",
            Status = ControlStatus.Effective,
            Owner = "IT Manager",
        },
        new()
        {
            TenantId = tenantId,
            Reference = "A.8.16",
            Name = "Monitoring activities",
            Description = "Networks, systems and applications are monitored for anomalous behaviour and acted upon.",
            Status = ControlStatus.NotImplemented,
            Owner = "Head of Platform",
        },
    ];
}
