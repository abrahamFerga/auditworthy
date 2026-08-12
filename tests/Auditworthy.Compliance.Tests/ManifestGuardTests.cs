using Auditworthy.Compliance;
using Auditworthy.Compliance.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Plenipo.Application.Authorization;
using Plenipo.Core.Identity;
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

    /// <summary>
    /// A principal holding nothing. The guards below only read declarations, so any
    /// <see cref="ICurrentUser"/> would do — except for
    /// <see cref="Every_approval_gated_tool_refuses_a_caller_without_its_permission"/>, which is
    /// about precisely this principal.
    /// </summary>
    private sealed class NoPermissionsCurrentUser : ICurrentUser
    {
        public Guid? UserId => Guid.Parse("00000000-0000-0000-0000-0000000000b1");
        public string? Subject => "no-permissions";
        public string? DisplayName => "No Permissions";
        public Guid? TenantId => Guid.Parse("00000000-0000-0000-0000-0000000000a1");
        public bool IsAuthenticated => true;
        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>(StringComparer.Ordinal);
        public bool HasPermission(string permission) => PermissionMatcher.IsGranted(Permissions, permission);
    }

    /// <summary>
    /// An approval-gated tool re-checks its own permission when it runs — the cheapest rung that
    /// catches #76.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ApprovalLaneRbacTests</c> proves the same rule through the real approve endpoint, which is
    /// the evidence that matters; this one catches the regression a second earlier and without
    /// Docker. It is a declaration-level guard in the same spirit as the two above: the failure it
    /// prevents — a gated tool that executes for whoever happens to hold
    /// <c>chat.approvals.manage</c> — is silent, and arrives with the next write tool rather than
    /// with the mistake.
    /// </para>
    /// <para>
    /// TODO(plenipo#145): when the platform's <c>ApprovalExecutor</c> checks the approver itself,
    /// this guard and <c>PermissionGatedTool</c> both go.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Every_approval_gated_tool_refuses_a_caller_without_its_permission()
    {
        var gated = BuildToolSourceTools().Where(t => t.RequiresApproval).ToList();

        // An empty set would make the loop below vacuously true, and "no write tool is gated" is
        // itself the failure this module must never ship.
        Assert.NotEmpty(gated);

        // Arguments the tool would accept, deliberately: called with an empty bag the refusal is
        // indistinguishable from argument binding failing first, and the red this test must produce
        // is "the tool went ahead and tried to do the work", not "it could not be called at all".
        var arguments = new AIFunctionArguments
        {
            ["reference"] = "A.5.1",
            ["status"] = "Effective",
            ["reason"] = "Module guard: a caller with no permissions must never reach the write.",
        };

        foreach (var tool in gated)
        {
            var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                async () => await tool.Function.InvokeAsync(arguments, CancellationToken.None));

            Assert.Contains(tool.Permission, denied.Message, StringComparison.Ordinal);
        }
    }

    private static IReadOnlyList<ModuleTool> BuildToolSourceTools()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, FixedTenantContext>();
        services.AddSingleton<ICurrentUser, NoPermissionsCurrentUser>();
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
