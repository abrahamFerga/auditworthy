# Auditworthy.IntegrationTests

Rungs 3 and 4 of the test ladder. Installed and owned by **`/deliver:install-runbook`** — see
[RUNBOOK.md](../../RUNBOOK.md) §6 for the contract, and re-run the skill to reconcile this project
rather than hand-rolling a second source of truth for how the product is run and proved.

```bash
dotnet test tests/Auditworthy.IntegrationTests
```

Needs **Docker Desktop running**. No API key.

| File | What it holds |
|---|---|
| `IntegrationFixture.cs` | the real host on a Testcontainers `pgvector/pgvector:pg17`; `AdminClient()` and `AuthorizedScopeAsync()` |
| `AguiStream.cs` | parses one AG-UI turn out of the SSE body — tool calls, custom events, reassembled reply |
| `SmokeTests.cs` | boot, `/alive` + `/health`, the module and its tabs, tool/permission wiring in the security catalog |
| `ChatAndApprovalTests.cs` | the human-in-the-loop gate over HTTP, end to end, plus the analyst and reader boundaries |
| `Evals/` | rung 4: golden conversations as `cases/*.json`, driven through the real AG-UI endpoint |

**Anything security-shaped goes through `AdminClient()`.** `AuthorizedScopeAsync()` deliberately
bypasses RBAC and the approval gate, so a test written against it passes while the gate is broken.

To re-prove the verifier, flip both `RequiresApproval` flags on `propose_control_change` to `false`
(the manifest descriptor *and* the `ModuleTool`) and re-run: **7** tests must go red.
