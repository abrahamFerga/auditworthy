# Copilot instructions — Auditworthy

Auditworthy is a free, open-source, AI-first compliance and audit management system: a thin product
host on the Plenipo platform. Auth, multi-tenancy, RBAC-before-the-model, approvals, append-only
audit, jobs, chat transports, documents, OCR and RAG come from platform packages vendored in
`.packages/`; this repo owns the `compliance` domain module and nothing else.

**The cross-tool contract is [`AGENTS.md`](../AGENTS.md); the run-and-prove contract is
[`RUNBOOK.md`](../RUNBOOK.md).** This file deliberately does not duplicate either — it carries only
what a github.com Chat session needs standalone. Edit facts there, not here, or the copies drift and
no tool defines which one wins.

## Verify

```bash
dotnet run --project src/Auditworthy.AppHost   # run it (needs .NET 10 SDK, Docker Desktop running)
dotnet test Auditworthy.slnx                   # prove it (no AI key — the Mock provider is real)
```

`dotnet build` proves nothing about behaviour. Claims about runtime behaviour need the evidence
ladder in `RUNBOOK.md`, and a regression test must be seen red before the fix and green after.

## The three rules that catch most mistakes

- **A tool is registered in two places** — a `ToolDescriptor` in the module manifest *and* a
  `ModuleTool` in the tool source — with `RequiresApproval` and the
  `Permissions.ForTool(ComplianceModule.Id, "<tool_name>")` string identical in both. Miss one and
  the tool is silently never callable, or 403s even for `system_admin`.
- **Every `ITenantOwned` entity needs its own `HasQueryFilter`** in `ComplianceDbContext` — the
  platform applies filters by reflection only to its own context, never to a module's.
- **`compliance-analyst` must never gain `chat.approvals.manage`** or a `tools.compliance.*`
  wildcard. Propose-without-approve is the product's central claim (see `SECURITY.md`).
