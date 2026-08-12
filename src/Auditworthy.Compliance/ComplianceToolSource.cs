using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Plenipo.Application.Authorization;
using Plenipo.Core.Identity;
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
        var currentUser = scopedServices.GetRequiredService<ICurrentUser>();

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
                // TODO(plenipo#145): drop the wrapper once the platform's ApprovalExecutor checks
                // the approver against tool.Permission itself. Until then the approval lane
                // executes a parked call for anyone holding chat.approvals.manage, so an
                // approval-gated tool MUST carry its own execution-time check — see
                // PermissionGatedTool and auditworthy#76.
                Function = PermissionGated(
                    AIFunctionFactory.Create(
                        tools.ProposeControlChangeAsync, name: "propose_control_change"),
                    Permissions.ForTool(ModuleId, "propose_control_change"),
                    currentUser),
                // Kept in sync with the manifest descriptor deliberately: the runner unions both
                // sets, so setting one and reviewing only that one hides a broken gate.
                RequiresApproval = true,
            },
        ];
    }

    /// <summary>
    /// Re-checks <paramref name="permission"/> when the function actually runs.
    /// </summary>
    /// <remarks>
    /// Every approval-gated tool goes through this, and only approval-gated tools need it: a read
    /// tool is never parked, so it is never reachable through
    /// <c>POST /api/chat/approvals/{id}/approve</c> — the one path that reaches a tool without
    /// having asked whether the caller may use it.
    /// </remarks>
    private static AIFunction PermissionGated(AIFunction function, string permission, ICurrentUser currentUser) =>
        new PermissionGatedTool(function, permission, currentUser);
}
