# Auditworthy

**A free, open-source, AI-first compliance and audit management system** — obligations, controls and
evidence, with an accountable owner approving before anything is committed.

Built as a thin product host on the [Plenipo](https://github.com/abrahamFerga/Plenipo) platform:
auth, multi-tenancy, RBAC-before-the-model, approvals, append-only audit, jobs, chat transports,
documents, OCR and RAG all arrive as package references. This repo owns the `compliance` domain
module and nothing else.

## Run it

```bash
dotnet run --project src/Auditworthy.AppHost
```

Needs the **.NET 10 SDK** (pinned in `global.json`) and **Docker Desktop running**. **No AI key** —
the assistant runs on Plenipo's deterministic `Mock` provider, which performs real, audited tool
calls including the approval gate.

```bash
dotnet test Auditworthy.slnx
```

Use `aspire run` instead of `dotnet run` when you need telemetry — an AppHost started with
`dotnet run` is invisible to the Aspire MCP.

**[RUNBOOK.md](RUNBOOK.md) is the full contract**: how to run it headlessly, the dev-auth headers,
how to drive the assistant over AG-UI and through the approval round trip, what to read when
something misbehaves, and the four-rung test ladder. [`auditworthy.http`](auditworthy.http) is the
runnable request catalog.

## What it does

The compliance register, and the assertion trail behind it. An analyst asks the assistant to propose
a control status change; the change **parks on a human approval** and the reply does not claim it
happened. The accountable owner approves, and the platform records who proposed, who approved, and
when — which is the thing an auditor actually asks for.

## Status — early

**Epic 1 only: the controls register.** Three tools (`list_controls`, `get_control`,
`propose_control_change`), one server-driven Controls tab, three role baselines.

Deliberately **not** built yet, in build order: the framework library (ISO 27001 + NIS2), evidence
capture and review, readiness and gap analysis, remediation tracking, cited evidence Q&A, and the
auditor-ready export pack. See [PLAN.md](PLAN.md).

Hard scope limits, and they are not aspirational: **v1 ships two frameworks, not 150**, and evidence
is **upload-and-attest, not continuous automated collection**. The reasons are in
[research/regulatory-compliance.md](research/regulatory-compliance.md) §8.

**This product does not deliver certification.** Certification is issued by an accredited body.
Auditworthy organises the evidence you take to one.

## Layout

```text
src/Auditworthy.AppHost      Aspire orchestration — Postgres (pgvector) x2 DBs + Redis
src/Auditworthy.Host         thin host: AddPlenipoPlatform() + the module + role baselines
src/Auditworthy.Compliance   the compliance module — all the real domain code
tests/Auditworthy.Compliance.Tests    manifest + tenancy guard (no Docker needed)
tests/Auditworthy.IntegrationTests    real host, real Postgres, approval gate, golden evals
.packages/                   vendored Plenipo nupkgs — the only source for Plenipo.*
```

## Documents

[SPEC.md](SPEC.md) · [PLAN.md](PLAN.md) · [SECURITY.md](SECURITY.md) ·
[research/regulatory-compliance.md](research/regulatory-compliance.md) — including the honest
competitive map: CISO Assistant, Eramba, SimpleRisk, Vanta, Drata, Cynomi and GetCybr are all in
this space, and the research recommended *against* entering it. That was overridden deliberately,
and the record was kept rather than deleted.

## Licence

MIT — see [LICENSE](LICENSE). Auditworthy is an independent implementation and contains no code from
any other compliance product.
