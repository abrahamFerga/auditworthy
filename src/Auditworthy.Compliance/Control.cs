using Plenipo.Core.Entities;
using Plenipo.Core.Multitenancy;

namespace Auditworthy.Compliance;

/// <summary>
/// How well a control is actually operating. This is the field <c>propose_control_change</c>
/// moves, and the reason that tool is approval-gated: it is an assertion someone will later
/// rely on in front of an auditor.
/// </summary>
public enum ControlStatus
{
    NotImplemented = 0,
    InProgress = 1,
    Implemented = 2,
    Effective = 3,
    NotApplicable = 4,
}

/// <summary>
/// A control the organisation operates — the primary object of the register, and the anchor
/// everything else in the module hangs from: requirements link to it, evidence proves it, and
/// remediation closes its gaps.
/// </summary>
/// <remarks>
/// <see cref="ITenantOwned"/> is not decoration. The module's <c>DbContext</c> declares a
/// <c>HasQueryFilter</c> per entity so no query can cross a client-organisation boundary;
/// the platform context does this by reflection, a module context does not.
/// </remarks>
public sealed class Control : EntityBase, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>Short human reference, e.g. "A.5.1" or "NIS2-Art21-2a".</summary>
    public required string Reference { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public ControlStatus Status { get; set; } = ControlStatus.NotImplemented;

    /// <summary>Display name of the person accountable for the control. PII.</summary>
    public string? Owner { get; set; }
}
