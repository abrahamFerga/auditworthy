using Auditworthy.Compliance;
using Auditworthy.Compliance.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plenipo.Application.Authorization;
using Plenipo.Core.Multitenancy;
using Plenipo.Modules.Sdk;
using Xunit;

namespace Auditworthy.Compliance.Tests;

/// <summary>
/// The module guard. These tests exist because every failure they catch is silent at runtime:
/// a tool registered in one place and not the other is simply never callable, a permission string
/// that disagrees between the two 403s even for <c>system_admin</c>, and a tenant-owned entity
/// without a query filter leaks across client organisations without erroring once.
/// </summary>
public sealed class ManifestGuardTests
{
    private static readonly ComplianceModule Module = new();

    private sealed class FixedTenantContext : ITenantContext
    {
        private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
        public Guid? TenantId => Tenant;
        public bool HasTenant => true;
        public Guid RequireTenantId() => Tenant;
    }

    [Fact]
    public void Manifest_has_a_stable_non_empty_id()
    {
        Assert.False(string.IsNullOrWhiteSpace(Module.Manifest.Id));
        Assert.Equal("compliance", Module.Manifest.Id);
        Assert.Equal(ComplianceModule.Id, Module.Manifest.Id);
    }

    [Fact]
    public void Tab_ids_and_routes_are_unique()
    {
        var tabs = Module.Manifest.Tabs;

        Assert.Equal(tabs.Select(t => t.Id).Distinct().Count(), tabs.Count);
        Assert.Equal(tabs.Select(t => t.Route).Distinct().Count(), tabs.Count);
        Assert.All(tabs, t => Assert.False(string.IsNullOrWhiteSpace(t.Route)));
    }

    [Fact]
    public void Tabs_that_expose_data_declare_a_permission()
    {
        // A tab with a DataEndpoint returns rows; without a permission it would be readable by
        // anyone who can reach the shell.
        foreach (var tab in Module.Manifest.Tabs.Where(t => !string.IsNullOrWhiteSpace(t.DataEndpoint)))
        {
            Assert.False(string.IsNullOrWhiteSpace(tab.Permission));
        }
    }

    [Fact]
    public void Tool_names_are_unique_and_permissions_use_the_platform_helper()
    {
        var tools = Module.Manifest.Tools;

        Assert.Equal(tools.Select(t => t.Name).Distinct().Count(), tools.Count);

        foreach (var tool in tools)
        {
            Assert.Equal(Permissions.ForTool(ComplianceModule.Id, tool.Name), tool.Permission);
        }
    }

    [Fact]
    public void Every_descriptor_has_a_matching_executable_tool_with_the_same_permission()
    {
        var executable = BuildToolSourceTools();

        foreach (var descriptor in Module.Manifest.Tools)
        {
            var match = executable.SingleOrDefault(t => t.Name == descriptor.Name);

            Assert.True(match is not null,
                $"Tool '{descriptor.Name}' is in the manifest but has no ModuleTool — it is silently never callable.");
            Assert.Equal(descriptor.Permission, match!.Permission);
        }
    }

    [Fact]
    public void Every_executable_tool_is_declared_in_the_manifest()
    {
        var declared = Module.Manifest.Tools.Select(t => t.Name).ToHashSet();

        foreach (var tool in BuildToolSourceTools())
        {
            Assert.True(declared.Contains(tool.Name),
                $"Tool '{tool.Name}' is executable but absent from the manifest — it will not be offered to the model.");
        }
    }

    [Fact]
    public void Writes_are_approval_gated_in_both_places()
    {
        // The runner unions the two RequiresApproval flags, so setting one and reviewing only that
        // one hides a broken gate. Both must be true for every write.
        string[] writes = ["propose_control_change"];

        var executable = BuildToolSourceTools();

        foreach (var name in writes)
        {
            var descriptor = Module.Manifest.Tools.Single(t => t.Name == name);
            var tool = executable.Single(t => t.Name == name);

            Assert.True(descriptor.RequiresApproval, $"Manifest descriptor for '{name}' is not approval-gated.");
            Assert.True(tool.RequiresApproval, $"ModuleTool for '{name}' is not approval-gated.");
        }
    }

    [Fact]
    public void Every_tenant_owned_entity_declares_a_query_filter()
    {
        // The single highest-consequence mistake available in this codebase. PlatformDbContext
        // applies filters by reflection; a module context does not, so an entity added without a
        // filter is a silent cross-tenant leak. This test is what makes that unrepresentable.
        using var db = BuildDbContext();

        var tenantOwned = db.Model.GetEntityTypes()
            .Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType))
            .ToList();

        Assert.NotEmpty(tenantOwned);

        foreach (var entity in tenantOwned)
        {
            Assert.True(entity.GetDeclaredQueryFilters().Count > 0,
                $"Entity '{entity.ClrType.Name}' is ITenantOwned but has no HasQueryFilter — cross-tenant leak.");
        }
    }

    private static IReadOnlyList<ModuleTool> BuildToolSourceTools()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, FixedTenantContext>();
        services.AddDbContext<ComplianceDbContext>(o => o.UseNpgsql("Host=localhost;Database=guard"));
        services.AddScoped<ComplianceTools>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        return new ComplianceToolSource().GetTools(scope.ServiceProvider);
    }

    private static ComplianceDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<ComplianceDbContext>()
            .UseNpgsql("Host=localhost;Database=guard")
            .Options;

        return new ComplianceDbContext(options, new FixedTenantContext());
    }
}
