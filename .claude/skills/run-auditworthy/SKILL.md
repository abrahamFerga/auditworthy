---
name: run-auditworthy
description: >
  Run, observe, and test Auditworthy locally — and prove a change actually works at runtime rather
  than merely compiling. Covers the Aspire AppHost, headless/CI mode, dev-auth headers, exercising
  the assistant over AG-UI, the approval round trip, reading Aspire telemetry, and the four-rung
  test ladder (build → module guard → Testcontainers E2E → golden evals). Zero API keys required.
  USE FOR: starting Auditworthy, reproducing a bug, calling its API, adding or running tests,
  verifying a feature before opening a PR. DO NOT USE FOR: platform-level Plenipo development (that
  lives in the Plenipo repo's own run skill).
license: MIT
---

# Run & test Auditworthy

**[RUNBOOK.md](../../../RUNBOOK.md) is the source of truth.** This skill is the index — read the
runbook section you need rather than guessing.

Auditworthy is a thin product host on the **Plenipo platform**. Auth, multi-tenancy,
RBAC-before-the-model, approvals, append-only audit, jobs, chat transports, documents and RAG come
from platform packages vendored in `.packages/`. This repo owns the `compliance` domain module.
**Do not rebuild platform concerns here** — if you find yourself writing a permission checker, an
audit log, or a tenant filter, stop and use the platform's.

## The two commands

```bash
dotnet run --project src/Auditworthy.AppHost
```

```bash
dotnet test Auditworthy.slnx
```

Docker Desktop must be running. No AI key: the assistant uses Plenipo's `Mock` provider, which
still performs **real, audited tool calls and triggers the approval gate**. There is no frontend to
build — v1's tabs are server-driven and render in the platform shell.

## Where to look

| I need to… | RUNBOOK section |
|---|---|
| start it (Aspire / headless) | §2 Run |
| know when it's ready | §2 Ready signals |
| authenticate a request | §3 Dev authentication |
| call an endpoint | §4 — plus the committed [`auditworthy.http`](../../../auditworthy.http) catalog |
| send a chat turn and read the event stream | §4 AG-UI |
| approve or reject a parked write | §4 The approval round trip |
| check a tool's permission wiring | §4 `/api/admin/security/catalog` |
| read logs, traces, metrics | §5 Observe |
| decide which tests to write and run | §6 The test ladder |
| add a behaviour regression for a prompt change | §6 rung 4, golden evals |
| debug a failure methodically | §7 The verification loop |
| a symptom I've seen before | §8 Gotchas |

## Non-negotiables

- **Prove it at runtime.** `dotnet build` proves nothing. Exercise the change through a real
  request, then lock it in with a test that fails without the fix — seen red before, green after.
- **Use `AdminClient()` for anything security-shaped.** `AuthorizedScopeAsync()` bypasses RBAC and
  the approval gate by design, so it can never prove they work.
- **A new tool needs three things**: the `ToolDescriptor` in `ComplianceModule`'s manifest, the
  `ModuleTool` in `ComplianceToolSource`, and `Permissions.ForTool(ComplianceModule.Id, name)` in
  both. `/api/admin/security/catalog` shows the gap; nothing else will.
- **Writes are approval-gated in both places.** The runner unions the two `RequiresApproval` flags,
  so setting one and reviewing only that one hides a broken gate.
- **`compliance-analyst` must never gain `chat.approvals.manage`** or a `tools.compliance.*`
  wildcard. Proposing without approving is the product's central claim. See SECURITY.md.
- **Every `ITenantOwned` entity needs its own `HasQueryFilter`** in `ComplianceDbContext` — a module
  context does not get them by reflection the way `PlatformDbContext` does.
- **Never commit a secret.** Provider keys are per-tenant, entered at runtime under
  **Admin → AI Settings**, and stored write-only in the vault.
- **Never describe readiness as certification.** Certification is issued by an accredited body.
