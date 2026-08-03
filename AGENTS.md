# Auditworthy

A free, open-source, AI-first compliance and audit management system, built as a **thin product host
on the Plenipo platform**. Auth, multi-tenancy, RBAC-before-the-model, approvals, append-only audit,
jobs, chat transports, documents, OCR and RAG all come from platform packages vendored in
`.packages/`. This repo owns the `compliance` domain module — nothing else.

## Build and test

```bash
dotnet run --project src/Auditworthy.AppHost     # run it (Aspire: Postgres, Redis, API)
dotnet test Auditworthy.slnx                     # prove it
```

Needs the **.NET 10 SDK** (pinned in `global.json`) and **Docker Desktop running**. No AI key — the
assistant runs on Plenipo's deterministic `Mock` provider, which performs real, audited tool calls
including the approval gate.

Use **`aspire run`** when you need telemetry: an AppHost started with `dotnet run` is invisible to
the Aspire MCP, which is the agent-readable observability path.

`dotnet build` proves nothing about behaviour. **There is no RUNBOOK.md yet** — run
`/deliver:install-runbook` before working the first feature issue, and until then say plainly that
this product has no runtime proof.

## Layout

```text
src/Auditworthy.AppHost      Aspire orchestration — see the comments, each is a lost dev database
src/Auditworthy.Host         thin host: AddPlenipoPlatform() + module + three role baselines
src/Auditworthy.Compliance   the compliance module — all the real domain code
tests/Auditworthy.Compliance.Tests   manifest + tenancy guard (no Docker)
tests/Auditworthy.IntegrationTests   empty shell; /deliver:install-runbook owns its content
.packages/                   vendored Plenipo nupkgs — the only source for Plenipo.*
```

## Rules most often broken

- **A tool must be registered in two places.** A `ToolDescriptor` in `ComplianceModule`'s manifest
  *and* a `ModuleTool` in `ComplianceToolSource`. Miss either and the tool is silently never
  callable. Verify with `GET /api/admin/security/catalog`.
- **The approval gate is the union of two flags.** `RequiresApproval = true` on *either* the
  descriptor or the `ModuleTool` gates the tool, because the runner unions both sets. Set both and
  keep them in sync — reviewing only one will mislead you.
- **Permission strings must match across both places.** Always
  `Permissions.ForTool(ComplianceModule.Id, "<tool_name>")`, never a hand-written string; a mismatch
  403s even for `system_admin`.
- **Every `ITenantOwned` entity needs its own `HasQueryFilter`** in `ComplianceDbContext`.
  `PlatformDbContext` applies filters by reflection; a module context does **not**. `ManifestGuardTests`
  fails the build if you forget — do not delete that test to go faster.
- **`compliance-analyst` must never gain `chat.approvals.manage`** or a `tools.compliance.*` wildcard.
  Its whole purpose is to propose without approving. Losing that makes the product's central claim
  false. See SECURITY.md.
- **Never scaffold what the platform provides** — no auth, no audit trail, no tenant filter helper,
  no chat endpoint, no job scheduler, no secrets store.
- **Never edit the Plenipo platform from this repo.** Climb the escalation ladder, apply a local shim
  tagged `TODO(plenipo#N)` so you are never blocked, then file a platform request.
- **Never weaken an invariant to unblock yourself.** If a screen is awkward because of a permission
  boundary, that boundary is the product.
- **Never describe readiness as certification.** Certification is issued by an accredited body.

## Facts verified against source — do not contradict these

Trust ranking: **source > tests > platform docs > product docs.**

- The host API is `builder.AddPlenipoPlatform()` / `app.UsePlenipoPlatform()`, from
  `Plenipo.AspNetCore.Hosting` (`AddPlenipoModule<T>` is in `Plenipo.AspNetCore.Modules`). Plenipo's
  `BUILDING_A_PRODUCT.md` documents `AddPlenipo()` / `UsePlenipo()` — **those do not exist.**
- **Plenipo packages are not on nuget.org.** They are vendored in `.packages/` and pinned by
  `packageSourceMapping` in `nuget.config` to prevent dependency-confusion fallback.
- **Postgres must be `pgvector/pgvector`** — the platform's RAG migration creates a vector column at
  startup and stock `postgres` fails on the `vector` type.
- **This AppHost pins host port 15433, not 15432.** 15432 is Networthy's. Two AppHosts mounting one
  data volume destroy the cluster; the pinned port makes the second run fail fast instead. Never
  unpin it to resolve a conflict.
- `EF Core 10`: `IReadOnlyEntityType.GetQueryFilter()` is obsolete — use `GetDeclaredQueryFilters()`.
- The module id is `compliance`. Roles are `compliance-reader`, `compliance-analyst`,
  `compliance-owner`. A client organisation is a tenant.

## How work is judged

State the level of your evidence. Never report an L4 conclusion with L1 confidence.

| Level | Meaning |
|---|---|
| L1 | deterministic — a command's exit code decided it |
| L2 | rule/constraint — a linter, schema, or audit decided it |
| L3 | delayed field truth — the integration suite, a deploy, a real user |
| L4 | **model as judge — your opinion, not field truth** |
| L5 | human checkpoint — not automated verification at all |

**Prove the verifier.** A regression test must be seen **red before the fix and green after**.

End work in exactly one named state: `Success`, `No-op`, `Blocked`, `Stalled`, `Exhausted`, or
`Approval-required`. Three failures for three different reasons means you are `Stalled` — the
diagnosis is wrong, not the fix.

**Never merge your own pull request.** The maker is not the approver. The autonomy level in
`workflow.json` is **0** — read it, never infer it.
