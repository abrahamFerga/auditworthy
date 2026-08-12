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

`dotnet build` proves nothing about behaviour. **[RUNBOOK.md](RUNBOOK.md) is the run-and-prove
contract** — headless mode, dev-auth headers, the AG-UI event stream, the approval round trip, the
four-rung test ladder and the gotcha table. Read it before running or debugging anything, and use
the committed [`auditworthy.http`](auditworthy.http) catalog rather than reconstructing requests.

## Layout

```text
src/Auditworthy.AppHost      Aspire orchestration — see the comments, each is a lost dev database
src/Auditworthy.Host         thin host: AddPlenipoPlatform() + module + three role baselines
src/Auditworthy.Compliance   the compliance module — all the real domain code
tests/Auditworthy.Compliance.Tests   manifest + tenancy guard (no Docker)
tests/Auditworthy.IntegrationTests   real host on Testcontainers: approval gate, RBAC, golden evals
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

- The host API is `builder.AddPlenipoPlatform()`, from `Plenipo.AspNetCore.Hosting`
  (`AddPlenipoModule<T>` is in `Plenipo.AspNetCore.Modules`). Plenipo's `BUILDING_A_PRODUCT.md`
  documents `AddPlenipo()` / `UsePlenipo()` — **those do not exist.**
- **The terminal call is `await app.RunPlenipoPlatformAsync()`, NOT `app.UsePlenipoPlatform(); app.Run();`.**
  `UsePlenipoPlatform` only configures the pipeline. `RunPlenipoPlatformAsync` also calls
  `InitializePlenipoAsync`, which applies the platform and audit migrations and then every module's
  migrations and seed data (`PlenipoHostSetup.cs:199-215`). Get this wrong and the app boots, serves
  500s forever, and blames the job processor: `42P01: relation "platform.background_jobs" does not
  exist`. It looks like a job bug. It is a missing migration step.
- **`IModuleToolSource` must be registered as a SINGLETON.** The platform's `IToolRegistry` is a
  singleton that consumes every source, so `AddScoped<IModuleToolSource, …>` fails DI validation at
  startup and takes six other platform services down with it. This is why `GetTools` receives the
  scoped `IServiceProvider` as a **parameter** rather than injecting it — resolve scoped services
  (like the `DbContext`) inside the call.
- **Do not `WaitFor` the two database resources in the AppHost — wait for the postgres server.** A
  database resource's health check connects to that database by name, but nothing creates it except
  this API's own `DatabaseInitializer`. Waiting on the databases is a circular wait: containers go
  healthy, the dashboard comes up, and the API is simply never started, with postgres logging
  `FATAL: database "plenipo-platform" does not exist` where nobody is looking.
- **Plenipo packages are not on nuget.org.** They are vendored in `.packages/` and pinned by
  `packageSourceMapping` in `nuget.config` to prevent dependency-confusion fallback.
- **Postgres must be `pgvector/pgvector`** — the platform's RAG migration creates a vector column at
  startup and stock `postgres` fails on the `vector` type.
- **This AppHost pins host port 15433, not 15432.** 15432 is Networthy's. Two AppHosts mounting one
  data volume destroy the cluster; the pinned port makes the second run fail fast instead. Never
  unpin it to resolve a conflict.
- **A module's own schema is created by the module, not the platform.** `IModule` declares
  `MigrateAsync` and `SeedAsync`; `ComplianceModule` implements **neither**, so nothing ever creates
  `compliance.controls`. Observed at runtime, not inferred: the Controls endpoint 500s and every
  compliance tool returns `Error: Function failed.` behind
  `42P01: relation "compliance.controls" does not exist`. The platform migrates *itself* and then
  calls each module — it cannot invent your DDL.
- **The AG-UI wire vocabulary** (read off a live alpha.28 host): `RUN_STARTED`, `TOOL_CALL_START`
  (with `toolCallName`) / `TOOL_CALL_END`, `TEXT_MESSAGE_START` / `TEXT_MESSAGE_CONTENT` (`delta`) /
  `TEXT_MESSAGE_END`, `CUSTOM` with `name` of `approval_required` or `token_usage`, and
  `RUN_FINISHED` carrying `result.conversationId`. Tool calls arrive **before** the assistant text,
  and `token_usage` lands **before** `TEXT_MESSAGE_END`.
- **The Mock provider fills tool arguments with `"example"`** and puts the user's whole message in
  the first string parameter. Golden evals can therefore assert routing, gating and protocol — never
  domain results. Its replies also legitimately open with "Done — I called the … tool", so a
  `replyMustNotContain` of `"done"` fails on a correctly gated turn.
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

**Never merge your own pull request.** The maker is not the approver. Read the autonomy level out of
`workflow.json` — never infer it, and never quote it from here. This file claimed **0** long after a
human had raised it to **3**, which is the more dangerous direction for a stale fact to be wrong in:
an agent trusting the number in this sentence would have believed nothing could merge unattended
while, in fact, everything could.
