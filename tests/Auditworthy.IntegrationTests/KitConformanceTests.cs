using Plenipo.Testing.Conformance;
using Plenipo.Testing.Evals;
using Xunit;

namespace Auditworthy.IntegrationTests;

// The platform's own invariants, executed against the Auditworthy host (plenipo#189 /
// auditworthy#101). These five lines are the whole adoption: each pack is written and maintained in
// the Plenipo repository, ships in Plenipo.Testing at $(PlenipoVersion), and runs here against the
// compliance module named by IntegrationFixture.Contract — so upgrading the platform upgrades the
// invariants this product is held to, and a platform change that would break Auditworthy fails in
// the platform's own consumer-conformance gate before it is released.
//
// They do NOT replace the product's own tests. The packs prove the SPINE (RBAC before the model,
// the approval gate, the audit trail, the AG-UI protocol, tenancy, the agent guardrails); the files
// beside this one prove what is Auditworthy's — the control register, the analyst boundary, the
// starter register for a new tenant, the identity derivation.

/// <summary>S01–S15: the security spine — RBAC, approvals, audit, protocol, transport.</summary>
[Collection("api")]
public sealed class PlenipoSpineConformance(IntegrationFixture fixture)
    : PlenipoSpineConformance<Program>(fixture);

/// <summary>Manifest integrity: descriptor, executable twin and security catalog agree.</summary>
[Collection("api")]
public sealed class PlenipoManifestConformance(IntegrationFixture fixture)
    : PlenipoManifestConformance<Program>(fixture);

/// <summary>Tenant isolation: a second client organisation sees nothing of the first.</summary>
[Collection("api")]
public sealed class PlenipoTenancyConformance(IntegrationFixture fixture)
    : PlenipoTenancyConformance<Program>(fixture);

/// <summary>R1–R4: prompt injection stopped before the model, sensitive data redacted or blocked.</summary>
[Collection("api")]
public sealed class PlenipoRedTeamConformance(IntegrationFixture fixture)
    : PlenipoRedTeamConformance<Program>(fixture);

/// <summary>
/// Rung 4: every <c>Evals/cases/*.json</c> through the real AG-UI endpoint.
/// <para>
/// <b>Limit, stated up front:</b> the assistant runs on Plenipo's <c>Mock</c> provider, which selects
/// a tool by matching name tokens in the user's message rather than by reasoning. So these cases
/// prove the contract around the model — that the right tool is reachable, that an unpermitted tool
/// is never offered, that a write parks on the gate, and that the reply does not claim a parked write
/// happened. They prove nothing about answer quality. Do not add a case whose expectation only a real
/// model could satisfy; it will be a flake that teaches the next agent to delete this harness.
/// </para>
/// </summary>
[Collection("api")]
public sealed class PlenipoGoldenEvals(IntegrationFixture fixture)
    : PlenipoGoldenEvals<Program>(fixture);
