using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Plenipo.Core.Multitenancy;

namespace Auditworthy.Compliance.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> construct the context without booting the host.
/// </summary>
/// <remarks>
/// <para>
/// The module is a class library with no entry point, and <see cref="ComplianceDbContext"/> takes an
/// <see cref="ITenantContext"/> that only exists inside a request. Without this factory the EF tools
/// cannot build the model, so no migration can be scaffolded at all.
/// </para>
/// <para>
/// The connection string here is never opened — migration scaffolding needs a provider, not a
/// server. The runtime connection comes from configuration in <c>ComplianceModule.RegisterServices</c>.
/// <b>The history-table configuration must stay identical in both places</b>, or migrations get
/// scaffolded against one history table and applied against another.
/// </para>
/// </remarks>
internal sealed class ComplianceDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ComplianceDbContext>
{
    public ComplianceDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ComplianceDbContext>()
            .UseNpgsql(
                "Host=design-time;Database=design-time;Username=design-time",
                npgsql => npgsql.MigrationsHistoryTable(
                    ComplianceDbContext.MigrationsHistoryTable,
                    ComplianceDbContext.Schema))
            .Options;

        return new ComplianceDbContext(options, new DesignTimeTenantContext());
    }

    /// <summary>No request, so no tenant. The filter is compiled into the model, never evaluated here.</summary>
    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;

        public bool HasTenant => false;

        public Guid RequireTenantId() =>
            throw new InvalidOperationException("There is no tenant at design time.");
    }
}
