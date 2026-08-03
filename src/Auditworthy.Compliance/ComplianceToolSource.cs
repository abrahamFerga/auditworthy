using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Plenipo.Application.Authorization;
using Plenipo.Modules.Sdk;

namespace Auditworthy.Compliance;

/// <summary>
/// Supplies the compliance module's executable tools.
/// </summary>
/// <remarks>
/// A tool must be registered in <b>two</b> places — a <c>ToolDescriptor</c> in
/// <see cref="ComplianceModule"/>'s manifest and a <see cref="ModuleTool"/> here — carrying the
/// <b>same</b> permission string, always built with <c>Permissions.ForTool</c> rather than
/// hand-written. Miss either and the tool is silently never callable; mismatch the strings and it
/// 403s even for <c>system_admin</c>. Verify with <c>GET /api/admin/security/catalog</c>.
/// </remarks>
public sealed class ComplianceToolSource : IModuleToolSource
{
    public string ModuleId => ComplianceModule.Id;

    public IReadOnlyList<ModuleTool> GetTools(IServiceProvider scopedServices)
    {
        var tools = scopedServices.GetRequiredService<ComplianceTools>();

        return
        [
            new ModuleTool
            {
                ModuleId = ModuleId,
                Name = "list_controls",
                Permission = Permissions.ForTool(ModuleId, "list_controls"),
                Function = AIFunctionFactory.Create(tools.ListControlsAsync, name: "list_controls"),
            },
            new ModuleTool
            {
                ModuleId = ModuleId,
                Name = "get_control",
                Permission = Permissions.ForTool(ModuleId, "get_control"),
                Function = AIFunctionFactory.Create(tools.GetControlAsync, name: "get_control"),
            },
            new ModuleTool
            {
                ModuleId = ModuleId,
                Name = "propose_control_change",
                Permission = Permissions.ForTool(ModuleId, "propose_control_change"),
                Function = AIFunctionFactory.Create(
                    tools.ProposeControlChangeAsync, name: "propose_control_change"),
                // Kept in sync with the manifest descriptor deliberately: the runner unions both
                // sets, so setting one and reviewing only that one hides a broken gate.
                RequiresApproval = true,
            },
        ];
    }
}
