using System.ComponentModel;
using Auditworthy.Compliance.Persistence;
using Microsoft.EntityFrameworkCore;
using Plenipo.Core.Multitenancy;

namespace Auditworthy.Compliance;

/// <summary>
/// The compliance module's agent tools for epic 1 — the controls register.
/// </summary>
/// <remarks>
/// <c>propose_control_change</c> writes, so it is approval-gated in <b>both</b> the manifest
/// descriptor and the <c>ModuleTool</c>. Its return string is deliberately worded as a proposal,
/// never as a completed change: the agent must not claim the status moved before a human has
/// approved it, and the platform parks the call before this method's effect is committed.
/// </remarks>
public sealed class ComplianceTools(ComplianceDbContext db, ITenantContext tenantContext)
{
    [Description("List the organisation's controls with their current status.")]
    public async Task<string> ListControlsAsync(CancellationToken cancellationToken = default)
    {
        var controls = await db.Controls
            .OrderBy(c => c.Reference)
            .Take(200)
            .ToListAsync(cancellationToken);

        if (controls.Count == 0)
        {
            return "There are no controls in the register yet.";
        }

        var lines = controls.Select(c => $"{c.Reference} — {c.Name} [{c.Status}]");
        return $"{controls.Count} control(s): {string.Join("; ", lines)}.";
    }

    [Description("Get one control in detail by its reference, e.g. 'A.5.1'.")]
    public async Task<string> GetControlAsync(
        [Description("The control's reference, e.g. 'A.5.1'.")] string reference,
        CancellationToken cancellationToken = default)
    {
        var control = await db.Controls
            .FirstOrDefaultAsync(c => c.Reference == reference, cancellationToken);

        if (control is null)
        {
            return $"No control found with reference \"{reference}\".";
        }

        var owner = string.IsNullOrWhiteSpace(control.Owner) ? "unassigned" : control.Owner;
        return $"{control.Reference} — {control.Name}. Status: {control.Status}. Owner: {owner}. "
             + $"{control.Description ?? "No description recorded."}";
    }

    [Description("Propose a new status for a control. The change is not applied until a human approves it.")]
    public async Task<string> ProposeControlChangeAsync(
        [Description("The control's reference, e.g. 'A.5.1'.")] string reference,
        [Description("Proposed status: NotImplemented, InProgress, Implemented, Effective or NotApplicable.")] string status,
        [Description("Why the status should change. This is recorded with the approval.")] string reason,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<ControlStatus>(status, ignoreCase: true, out var parsed))
        {
            return $"\"{status}\" is not a valid status. Valid values: "
                 + string.Join(", ", Enum.GetNames<ControlStatus>()) + ".";
        }

        var control = await db.Controls
            .FirstOrDefaultAsync(c => c.Reference == reference, cancellationToken);

        if (control is null)
        {
            return $"No control found with reference \"{reference}\".";
        }

        var previous = control.Status;
        control.Status = parsed;
        control.TenantId = tenantContext.RequireTenantId();
        await db.SaveChangesAsync(cancellationToken);

        return $"Control {control.Reference} moved from {previous} to {parsed}. Reason: {reason}";
    }
}
