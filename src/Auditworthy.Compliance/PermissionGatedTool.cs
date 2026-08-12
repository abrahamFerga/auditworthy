using Microsoft.Extensions.AI;
using Plenipo.Core.Identity;

namespace Auditworthy.Compliance;

/// <summary>
/// Wraps a tool's <see cref="AIFunction"/> so the caller is re-checked against the tool's own
/// permission <b>at execution time</b>, not only before the model was offered the tool.
/// </summary>
/// <remarks>
/// <para>
/// <b>TODO(plenipo#145)</b> — this is a local shim for a platform hole, and it is deleted the day
/// the platform closes it. At <c>0.1.0-alpha.28</c>, <c>ApprovalExecutor.ExecuteAsync</c>
/// (<c>src/Plenipo.Infrastructure/Approvals/ApprovalExecutor.cs</c>) resolves the
/// <c>ModuleTool</c> — <c>tool.Permission</c> literally in hand — and calls
/// <c>tool.Function.InvokeAsync</c> without reading it. The only per-tool permission check in the
/// platform is <c>AuthorizedAgentRunner</c>'s pre-model filter, which runs on the <i>propose</i>
/// path. <c>POST /api/chat/approvals/{id}/approve</c> is gated on
/// <c>chat.approvals.manage</c> alone.
/// </para>
/// <para>
/// The consequence, and the defect this closes (auditworthy#76): a principal RBAC has denied a
/// tool can commit that tool's write by approving someone else's parked call. Observed with a role
/// holding approval authority and nothing else, and with the platform's own <c>tenant_admin</c>.
/// </para>
/// <para>
/// <b>It throws rather than returning a refusal string, and that is the same decision
/// <c>ProposeControlChangeAsync</c> documents.</b> To the executor a returned string is a tool that
/// ran to completion: the approval resolves <c>Executed</c> with <c>error: null</c>, and the
/// approver is told a change landed that never did. Throwing surfaces as <c>Failed</c> + a 422 with
/// the reason, which is what the approval lane and the audit trail must record.
/// </para>
/// <para>
/// The permission semantics are the platform's own — <see cref="ICurrentUser.HasPermission"/>
/// delegates to <c>PermissionMatcher</c>, so <c>*</c>, <c>tools.*</c> and
/// <c>tools.compliance.*</c> all satisfy this exactly as they satisfy the pre-model filter. This
/// grants nobody anything and denies nobody who was already entitled: it makes the execution path
/// ask the question the proposal path already asked.
/// </para>
/// </remarks>
internal sealed class PermissionGatedTool(AIFunction innerFunction, string permission, ICurrentUser currentUser)
    : DelegatingAIFunction(innerFunction)
{
    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(permission))
        {
            throw new UnauthorizedAccessException(
                $"\"{Name}\" requires the permission \"{permission}\", which the approver does not "
                + "hold. Approval authority alone does not carry the authority to perform the "
                + "action being approved; the change has NOT been applied.");
        }

        return base.InvokeCoreAsync(arguments, cancellationToken);
    }
}
