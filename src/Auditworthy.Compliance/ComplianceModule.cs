using Auditworthy.Compliance.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plenipo.Application.Authorization;
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
            options.UseNpgsql(configuration.GetConnectionString("plenipo-platform")));

        services.AddScoped<ComplianceTools>();
        services.AddScoped<IModuleToolSource, ComplianceToolSource>();
    }

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
