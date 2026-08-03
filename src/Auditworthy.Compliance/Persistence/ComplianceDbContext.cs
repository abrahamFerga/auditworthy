using Auditworthy.Compliance;
using Microsoft.EntityFrameworkCore;
using Plenipo.Core.Multitenancy;
using Plenipo.Modules.Sdk;

namespace Auditworthy.Compliance.Persistence;

/// <summary>
/// The compliance module's own persistence, in its own schema.
/// </summary>
/// <remarks>
/// Two things here are load-bearing and must never be "simplified":
/// <list type="number">
/// <item>It derives from <see cref="ModuleDbContext"/>, not <see cref="DbContext"/>. The platform's
/// audit interceptor covers only the platform's own context; a module context deriving straight
/// from <c>DbContext</c> silently persists <c>default(DateTimeOffset)</c> timestamps.</item>
/// <item><b>Every <see cref="ITenantOwned"/> entity declares its own <c>HasQueryFilter</c>.</b>
/// <c>PlatformDbContext</c> applies filters by reflection; a module context does not, so a new
/// entity added without a filter is a silent cross-tenant leak — the highest-consequence mistake
/// available in this codebase. Adding an entity below without a matching filter is a bug, and
/// <c>ComplianceDbContextTests</c> fails the build if you do.</item>
/// </list>
/// </remarks>
public sealed class ComplianceDbContext(
    DbContextOptions<ComplianceDbContext> options,
    ITenantContext tenantContext)
    : ModuleDbContext(options)
{
    public const string Schema = "compliance";

    public DbSet<Control> Controls => Set<Control>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Control>(entity =>
        {
            entity.ToTable("controls");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Reference).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(4000);
            entity.Property(e => e.Owner).HasMaxLength(256);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(e => new { e.TenantId, e.Reference });

            // The tenant boundary. One per ITenantOwned entity, always.
            entity.HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
        });
    }
}
