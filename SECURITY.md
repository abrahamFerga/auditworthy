# Security

## Reporting a vulnerability

Open a [private security advisory](https://github.com/abrahamFerga/auditworthy/security/advisories/new).
Please do not open a public issue for a vulnerability.

## The security contract

Auditworthy inherits its security spine from the Plenipo platform and **must never weaken it**. The
following are invariants, not preferences. If a screen is awkward because of one of them, the
boundary is the product.

| Invariant | What it means here |
|---|---|
| **RBAC before the model** | Tool permissions are filtered *before* the model call. The agent is never offered a tool the caller may not run. |
| **Approval-first writes** | Every state-changing tool is approval-gated. The gate is the **union** of `RequiresApproval` on the `ToolDescriptor` *and* the `ModuleTool` — set both, review both. |
| **Tenant isolation** | Every `ITenantOwned` entity declares a `HasQueryFilter` in `ComplianceDbContext`. A module context does **not** get filters by reflection the way `PlatformDbContext` does, so an entity added without one is a silent cross-tenant leak. `ManifestGuardTests` fails the build if you add one. |
| **Append-only audit** | The audit database is separate and append-only. In this product the audit trail is not a safety net — it is the deliverable. |
| **Write-only secrets** | Credentials go to the platform's secret vault. No provider key belongs in `appsettings*.json`, in CI, or in a commit. |

## The role model, and the one property that matters

`compliance-analyst` may **propose** control changes and may **approve none of them**, and is
deliberately excluded from `chat.approvals.manage` so an analyst cannot clear their own gate. It is
an enumerated allowlist rather than a wildcard, so a future write tool is not silently granted.

If that exclusion is ever lost, the approval lane becomes ceremony and this product's central claim —
*an accountable owner approves before anything is committed* — becomes false. Treat any change to it
as a security change.

## What this product does not do

- **It does not deliver certification.** ISO 27001 and NIS2 conformity are assessed by accredited
  bodies and regulators. Auditworthy organises evidence; it does not confer status.
- **It does not satisfy a regulation on your behalf.** Audit, RBAC and tenant isolation *support*
  regimes such as NIS2; they do not deliver compliance with them. The obligations remain the
  operator's.
