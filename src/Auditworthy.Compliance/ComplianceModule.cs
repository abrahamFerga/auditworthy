using Auditworthy.Compliance.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plenipo.Application.Authorization;
using Plenipo.Core.Multitenancy;
using Plenipo.Modules.Sdk;

namespace Auditworthy.Compliance;

/// <summary>
/// Auditworthy's compliance and audit-management module.
/// </summary>
/// <remarks>
/// Epic 1 — the controls register — is the walking skeleton: the module loads, a read tool answers
/// a real domain question, a write tool parks on the approval gate, and a tab renders the register.
/// Frameworks, evidence, readiness, remediation, cited Q&amp;A and the export pack are epics 2–7 and
/// are deliberately absent. See PLAN.md.
/// </remarks>
public sealed class ComplianceModule : IModule
{
    /// <summary>Stable module id — used in the AG-UI route, permission strings and the manifest.</summary>
    public const string Id = "compliance";

    /// <summary>Permission required to view the module's tabs.</summary>
    public const string ViewCompliance = "compliance.view";

    /// <summary>Permission required to administer the register.</summary>
    public const string ManageCompliance = "compliance.manage";


    public ModuleManifest Manifest { get; } = new()
    {
        Id = Id,
        DisplayName = "Compliance",
        Version = "0.1.0",
        Description =
            "Obligations, controls and evidence — with an accountable owner approving before "
          + "anything is committed.",
        Icon = "shield-check",
        AgentInstructions =
            "You are a careful compliance assistant. Use list_controls and get_control to read the "
          + "control register before answering. Use propose_control_change to propose a new status "
          + "for a control. That proposal REQUIRES a human approval, so never state that a control's "
          + "status has changed, or that anything is now compliant or effective, before the approval "
          + "has been granted — say that you have proposed the change and it is awaiting review. "
          + "Never describe readiness as certification: certification is issued by an accredited "
          + "body, not by this system.",
        SuggestedPrompts =
        [
            "Which controls are not yet effective?",
            "Show me control A.5.1",
            "Propose moving A.5.1 to Effective because the policy was approved",
        ],
        Roles = ["compliance-reader", "compliance-analyst", "compliance-owner"],
        Tools =
        [
            new ToolDescriptor
            {
                Name = "list_controls",
                Description = "List the organisation's controls with their current status.",
                Permission = Permissions.ForTool(Id, "list_controls"),
            },
            new ToolDescriptor
            {
                Name = "get_control",
                Description = "Get one control in detail by its reference, e.g. 'A.5.1'.",
                Permission = Permissions.ForTool(Id, "get_control"),
            },
            new ToolDescriptor
            {
                Name = "propose_control_change",
                Description =
                    "Propose a new status for a control. The change is not applied until a human approves it.",
                Permission = Permissions.ForTool(Id, "propose_control_change"),
                // The gate is the union of this flag and the ModuleTool's — set BOTH and keep them
                // in sync, or a review of one will mislead you about whether the write is gated.
                RequiresApproval = true,
            },
        ],
        Tabs =
        [
            new TabDescriptor
            {
                Id = "chat",
                Label = "Chat",
                Route = "/compliance/chat",
                Icon = "message-circle",
                Order = 0,
            },
            new TabDescriptor
            {
                Id = "controls",
                Label = "Controls",
                Route = "/compliance/controls",
                Icon = "shield-check",
                Order = 1,
                Permission = ViewCompliance,
                DataEndpoint = "/api/compliance/controls",
                Columns =
                [
                    new("reference", "Ref"),
                    new("name", "Control"),
                    new("status", "Status"),
                    new("owner", "Owner"),
                ],
            },
        ],
    };

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ComplianceDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("plenipo-platform"),
                npgsql => npgsql.MigrationsHistoryTable(
                    ComplianceDbContext.MigrationsHistoryTable,
                    ComplianceDbContext.Schema)));

        services.AddScoped<ComplianceTools>();

        // IModuleToolSource MUST be a singleton: the platform's IToolRegistry is a singleton and
        // consumes every registered source, so a scoped registration fails DI validation at
        // startup with "Cannot consume scoped service IModuleToolSource from singleton
        // IToolRegistry" — and takes six other platform services down with it.
        // This is why IModuleToolSource.GetTools receives the scoped IServiceProvider as a
        // PARAMETER instead of injecting it: the source is a singleton that resolves the scoped
        // ComplianceTools (and its DbContext) per call.
        services.AddSingleton<IModuleToolSource, ComplianceToolSource>();
    }

    /// <summary>
    /// Creates the module's own schema. <b>The platform cannot do this for you.</b>
    /// </summary>
    /// <remarks>
    /// <c>InitializePlenipoAsync</c> migrates the platform and audit databases and then calls each
    /// module — it migrates *itself*, it cannot invent a module's DDL. Because
    /// <see cref="IModule.MigrateAsync"/> is a defaulted interface member, leaving it unimplemented
    /// compiles, passes every manifest guard, and boots; the only symptom is
    /// <c>42P01: relation "compliance.controls" does not exist</c> on the first real request.
    /// </remarks>
    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        // A scope of our own: this runs at startup, outside any request, and the context is scoped.
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComplianceDbContext>();

        // MigrateAsync, never EnsureCreatedAsync. The platform's initializer has already created
        // this database, so EnsureCreatedAsync would find it present, return false, and create no
        // tables at all — the silent version of exactly the bug this method fixes.
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// Seeds a starter control register into the scope's tenant — in Development only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The absence of a tenant is the production safety gate, and it is the platform's, not
    /// ours.</b> <c>SeedPlenipoModulesAsync</c> calls this method unconditionally, but it first
    /// calls <c>EstablishDevTenantContextAsync</c> on this very scope — and *that* is
    /// <c>IsDevelopment</c>-gated. So outside Development no tenant is ever established and this
    /// method returns having done nothing.
    /// </para>
    /// <para>
    /// Do not "improve" this by opening a new scope and looking the dev tenant up by slug. That
    /// discards the context the platform just prepared, re-implements a seam that has already run,
    /// and — because nothing reserves the slug <c>dev</c> — would write fabricated controls into any
    /// production tenant an operator happened to name that way. A client organisation's register is
    /// an assertion an auditor later relies on; a real tenant starts empty, which is the correct
    /// empty state.
    /// </para>
    /// <para>
    /// Idempotent across sequential boots: it does nothing once the tenant has any control. It is
    /// NOT safe against two hosts seeding the same fresh database concurrently — the unique index on
    /// (TenantId, Reference) is what makes that race fail loudly instead of duplicating the register.
    /// </para>
    /// </remarks>
    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        // The platform's scope, used as handed over. No tenant means production — nothing to seed,
        // and that is success, not an error.
        var tenant = services.GetRequiredService<ITenantContext>();

        if (!tenant.HasTenant)
        {
            return;
        }

        var db = services.GetRequiredService<ComplianceDbContext>();

        // No IgnoreQueryFilters: the tenant is established, so the query filter scopes this count to
        // exactly the tenant being seeded — which is the check we want.
        if (await db.Controls.AnyAsync(cancellationToken))
        {
            return;
        }

        db.Controls.AddRange(StarterRegister(tenant.RequireTenantId()));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A small ISO 27001 Annex A register with mixed statuses.
    /// </summary>
    /// <remarks>
    /// The references are clause identifiers; the wording is ours, not the standard's text. Statuses
    /// vary on purpose so the manifest's own suggested prompts — "Which controls are not yet
    /// effective?" and "Show me control A.5.1" — both return a real answer on a fresh clone.
    /// </remarks>
    private static IEnumerable<Control> StarterRegister(Guid tenantId) =>
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

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Backs the Controls tab's server-driven table: the platform renders the JSON array as a
        // grid using the tab's Columns, so the register needs no custom UI.
        var group = endpoints.MapGroup("/api/compliance").WithTags("Compliance").RequireAuthorization();

        group.MapGet("/controls", async (ComplianceDbContext db, CancellationToken ct) =>
            {
                var rows = await db.Controls
                    .OrderBy(c => c.Reference)
                    .Select(c => new
                    {
                        reference = c.Reference,
                        name = c.Name,
                        status = c.Status.ToString(),
                        owner = c.Owner,
                    })
                    .ToListAsync(ct);

                return Results.Ok(rows);
            })
            .RequireAuthorization(PermissionRequirement.PolicyName(ViewCompliance))
            .WithName("Compliance_ListControls");
    }
}
