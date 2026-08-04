<!-- Installed by plenipo-agents /deliver:install-runbook. Edit freely: this file is Auditworthy's
     source of truth for how it runs. Re-running the skill reconciles, never clobbers. -->

# Running and testing Auditworthy

Everything an agent (or a human) needs to take Auditworthy from a cold clone to a **proven**
change. Nothing here requires an API key, a cloud account, or a Plenipo checkout.

Auditworthy is a **thin product host on the Plenipo platform**: the security spine (auth,
multi-tenancy, RBAC-before-the-model, approvals, append-only audit, jobs, chat transports,
documents, OCR, RAG) comes from platform packages vendored in `.packages/`. This repo owns
`compliance` — the domain — and nothing else.

## 0. The one-screen version

```bash
dotnet run --project src/Auditworthy.AppHost
```

```bash
dotnet test Auditworthy.slnx
```

If both are green and you exercised the change through the API, you are done. If you only ran
`dotnet build`, you are not.

## 1. Prerequisites

| Need | Why | Check |
|---|---|---|
| **.NET 10 SDK** | pinned in `global.json` (`10.0.100`, `rollForward: latestFeature`) | `dotnet --version` → `10.*` |
| **Docker Desktop, running** | Postgres + Redis containers; Testcontainers for rungs 3–4 | `docker ps` |

No AI key, and no frontend toolchain. v1 ships **no bespoke UI** — the module's tabs are
server-driven and render in the platform shell, so there is no `frontend/` directory, no `pnpm`
step, and no `docker-compose.yml` in this repo.

The assistant runs on Plenipo's dependency-free **`Mock` provider**
(`src/Auditworthy.Host/appsettings.Development.json`), which streams deterministic replies **and
performs real, audited tool calls including the approval gate**. That is what makes the whole
security pipeline testable in CI and on a fresh clone. A real provider is configured per tenant at
runtime under **Admin → AI Settings** and stored write-only in the platform vault — never in
deployment config. `appsettings.json` sets `Ai:Provider = None` for exactly that reason.

## 2. Run

### Mode A — Aspire AppHost (the default; use this unless you have a reason not to)

```bash
dotnet run --project src/Auditworthy.AppHost
```

Brings up Postgres (`plenipo-platform` + `plenipo-audit` databases), Redis, and the API, then opens
the **Aspire dashboard** (URL printed to the console, with a login token). Take the API's external
HTTP endpoint from the dashboard resource **`auditworthy-api`**; that base URL is what every call
below targets.

**`dotnet run` and `aspire run` are not equivalent.** They start the same stack, but an AppHost
launched with `dotnet run` is **invisible to the Aspire MCP** — which is the entire agent-readable
observability path. If you intend to read logs or traces through tooling rather than eyeballs, use
`aspire run`. See §5.

**Mode A is slow to first byte and the console lies about it.** Postgres, Redis and the dashboard
are up within seconds; `auditworthy-api` builds, then waits on the Postgres health check, and took
**~4 minutes** to appear when this was last measured (2026-08-03, Aspire 13.4.6). Until then the
banner says *"Distributed application started"* and the API simply is not there. Do not conclude the
stack is broken before four minutes have passed — and do not go looking for the API's output in the
AppHost's stdout, because Aspire routes project logs to the **dashboard**, not the console you
launched from. **If you need a running instance right now, use Mode B**: the same host, booting in
seconds, and it is what the assertions in §4 were verified against.

To pin the dashboard to a known URL instead of a random port (this is what `.claude/launch.json`
does), pass it through — and it must be **https**, or Aspire refuses to start:

```bash
dotnet run --project src/Auditworthy.AppHost -- --ASPNETCORE_URLS=https://localhost:18888
```

### Mode B — headless (scripted verification, CI, no dashboard)

```powershell
dotnet build Auditworthy.slnx
docker rm -f auditworthy-pg-test 2>$null
docker run -d --name auditworthy-pg-test -e POSTGRES_PASSWORD=postgres -p 5433:5432 pgvector/pgvector:pg17

$bin = "src/Auditworthy.Host/bin/Debug/net10.0"
$pg  = "Host=127.0.0.1;Port=5433;Database={0};Username=postgres;Password=postgres"
$env:ASPNETCORE_ENVIRONMENT = "Development"
$api = Start-Process dotnet -WorkingDirectory $bin -PassThru -ArgumentList @(
  "$PWD/$bin/Auditworthy.Host.dll",
  "--ConnectionStrings:plenipo-platform=$($pg -f 'auditworthy_platform')",
  "--ConnectionStrings:plenipo-audit=$($pg -f 'auditworthy_audit')",
  "--urls=http://127.0.0.1:8094")

1..60 | ForEach-Object { Start-Sleep 2; try { if ((iwr http://127.0.0.1:8094/alive -UseBasicParsing).StatusCode -eq 200) { "ready"; break } } catch {} }

# ... exercise it (§4) ...

Stop-Process -Id $api.Id -Force; docker rm -f auditworthy-pg-test
```

> **The one gotcha that will cost you an hour:** `Start-Process` must set
> **`-WorkingDirectory` to the bin folder**. Otherwise ASP.NET's ContentRoot never finds
> `appsettings.Development.json`, the chat provider silently falls back to `None`, and every turn
> answers `RUN_ERROR "AI provider is not configured"`. Alternatively pass `--Ai:Provider=Mock`
> explicitly on the command line.

> Port **5433**, not 5432, and container name `auditworthy-pg-test`: a stray stock-`postgres`
> container on 5432 is the most common local collision, and it lacks pgvector.

### Ready signals

| Signal | Meaning |
|---|---|
| `GET /alive` → 200 `Healthy` | process is up. **Never calls the LLM** — safe to poll |
| `GET /health` → 200 `Healthy` | dependencies (DB, Redis) are reachable |
| `GET /api/platform/modules` → contains `compliance` | the module loaded and its manifest parsed |

## 3. Dev authentication

Development with no IdP configured uses the dev-auth fallback. Send these on **every** call:

```http
X-Dev-Subject: dev-user
X-Dev-Tenant:  dev
X-Dev-Roles:   system_admin
```

`system_admin` holds the `*` permission. To test RBAC, send a **narrower** role and assert the
403 — that is the point of the header being per-request. The three shipped baselines are
`compliance-reader`, `compliance-analyst` and `compliance-owner`
(`src/Auditworthy.Host/Program.cs`).

## 4. Exercise it

### The committed request catalog

[`auditworthy.http`](auditworthy.http) is the canonical, runnable list of every endpoint — open it
in VS Code (REST Client) or a JetBrains IDE and fire requests at a running instance. It uses
dev-auth headers, so there is no token setup. **When you add an endpoint, add its request here in
the same PR.**

### A chat turn over AG-UI (the core feature)

```powershell
$h = @{ "X-Dev-Subject"="dev-user"; "X-Dev-Tenant"="dev"; "X-Dev-Roles"="system_admin" }
$body = @{ messages = @(@{ id="m1"; role="user"; content="Which controls are not yet effective?" }) } | ConvertTo-Json
$r = Invoke-WebRequest "$base/api/agui/compliance" -Method Post -Headers $h `
       -ContentType "application/json" -Body $body -UseBasicParsing
($r.Content -split "`n") | Where-Object { $_ -like "data:*" }
```

A healthy turn streams, **in this order** (read off a live alpha.28 host — note the tool calls come
*before* the assistant text, and `token_usage` lands *before* `TEXT_MESSAGE_END`):

```text
RUN_STARTED
TOOL_CALL_START(toolCallName) → TOOL_CALL_END          (per tool)
TEXT_MESSAGE_START → TEXT_MESSAGE_CONTENT(delta) …
CUSTOM(approval_required)                               (gated tools only)
CUSTOM(token_usage) → TEXT_MESSAGE_END
RUN_FINISHED(result.conversationId)
```

An **approval-gated** tool emits `CUSTOM(approval_required)` and the reply must *not* claim the
write happened. `RUN_ERROR` is always a failure, never noise. Two common ones:

- `"AI provider is not configured"` → the Mode B WorkingDirectory gotcha.
- `"Unknown module"` → wrong module id; it must be `compliance`.

SignalR (`/hubs/agent`) is the other transport and goes through the *same* authorized, audited
runner — verifying one verifies the pipeline.

`tests/Auditworthy.IntegrationTests/AguiStream.cs` parses this stream and is the fastest way to
assert on a turn from a test.

### The approval round trip

```powershell
Invoke-RestMethod "$base/api/chat/approvals" -Headers $h                       # what is parked
Invoke-RestMethod "$base/api/chat/approvals/$id/approve" -Method Post -Headers $h
Invoke-RestMethod "$base/api/chat/approvals/$id/reject"  -Method Post -Headers $h
```

Approving returns the resolved call with `status: "Executed"` and the tool's own return value in
`result`. Rejecting returns `status: "Rejected"` and runs nothing. A caller without
`chat.approvals.manage` — **`compliance-analyst`, deliberately** — gets a **403** on the queue.

### Admin, RBAC, audit, usage

```powershell
Invoke-RestMethod "$base/api/platform/modules"       -Headers $h   # modules + tabs the caller sees
Invoke-RestMethod "$base/api/platform/me"            -Headers $h   # user, tenant, effective permissions
Invoke-RestMethod "$base/api/admin/security/catalog" -Headers $h   # every tool + the permission gating it
Invoke-RestMethod "$base/api/admin/roles"            -Headers $h
Invoke-RestMethod "$base/api/admin/users"            -Headers $h
Invoke-RestMethod "$base/api/admin/usage?days=30"    -Headers $h   # populated only AFTER a chat turn
Invoke-RestMethod "$base/api/admin/audit/tool-calls" -Headers $h   # every invocation, append-only
```

After adding a tool, `security/catalog` **must** list it with its permission and the right
`requiresApproval` flag. If it does not, the manifest and `ComplianceToolSource` disagree — the tool
will never be callable, and nothing else will tell you.

### The UI

There is none to run. The workspace shell and admin console are served by the platform; the
module's Controls tab is a server-driven table bound to `/api/compliance/controls` via the
`Columns` in `ComplianceModule`'s `TabDescriptor`. To change what the tab shows, change the
manifest and the endpoint — not a React file.

## 5. Observe

**Aspire dashboard** — console logs, structured logs, distributed traces and metrics per resource.
First place to look when a request misbehaves: the trace shows the tool call, the approval
interception and the DB round-trips in one timeline.

**Aspire MCP / CLI** — the agent-readable view of the same OpenTelemetry. `list_resources`,
`list_console_logs`, `list_structured_logs`. Known reasons it reports *"No Aspire AppHost is
currently running"*:

| Cause | Fix |
|---|---|
| AppHost started with `dotnet run` | relaunch with **`aspire run`** — only the CLI opens the backchannel |
| CLI and AppHost SDK versions differ | update the CLI: `iex "& { $(irm https://aspire.dev/install.ps1) }"` |
| Stale zero-byte `~/.aspire/cli/backchannels/aux.sock.*` | delete them |
| Just started | discovery is push-based; wait a few seconds |

In a headless or cron run the dashboard and MCP may be unavailable — use Mode B and read stdout.

## 6. The test ladder

Climb only as far as the change requires, but **never skip the rung that would catch your bug.**

| Rung | What it proves | Command |
|---|---|---|
| **1. Build** | it compiles | `dotnet build Auditworthy.slnx` |
| **2. Unit / module** | manifest integrity, tool/permission parity, a query filter per tenant-owned entity | `dotnet test tests/Auditworthy.Compliance.Tests` |
| **3. Integration (E2E)** | the real host, real Postgres, real migrations, real approvals, real RBAC | `dotnet test tests/Auditworthy.IntegrationTests` |
| **4. Golden evals** | agent *behaviour*: routing, gating, protocol | part of rung 3 (`Evals/cases/*.json`) |

Everything at once, exactly as CI runs it:

```bash
dotnet build Auditworthy.slnx -c Release -warnaserror
```

```bash
dotnet test Auditworthy.slnx -c Release
```

**Without Docker**, rungs 3 and 4 cannot run. Skip them explicitly rather than staring at a wall of
red — and say in your report that you skipped them:

```bash
dotnet test Auditworthy.slnx --filter "FullyQualifiedName!~IntegrationTests"
```

**Keep the Postgres major consistent.** `src/Auditworthy.AppHost/AppHost.cs` and
`tests/Auditworthy.IntegrationTests/IntegrationFixture.cs` both pin `pgvector/pgvector:pg17`. A
product that runs on pg17 but tests on pg16 is testing something it does not ship.

### Rung 3 — how the E2E host is built

`IntegrationFixture.cs` boots the **real** `Auditworthy.Host` via `WebApplicationFactory<Program>`
against a **Testcontainers** Postgres. Platform *and* audit migrations run, the dev tenant seeds,
hosted services start. The **Mock AI provider is the only stand-in** — everything else is production
code.

```csharp
_postgres = new PostgreSqlBuilder()
    .WithImage("pgvector/pgvector:pg17")   // the platform's RAG migration needs the vector extension
    .WithDatabase("plenipo_platform")
    .Build();
await _postgres.StartAsync();

Environment.SetEnvironmentVariable("ConnectionStrings__plenipo-platform", _postgres.GetConnectionString());
Environment.SetEnvironmentVariable("ConnectionStrings__plenipo-audit",    _postgres.GetConnectionString());
```

Two entry points, and picking the right one matters:

- **`fixture.AdminClient(roles, subject)`** — an `HttpClient` carrying the dev-auth headers. Use it
  to prove anything that must hold **through the HTTP surface**: routes, RBAC 403s, the AG-UI
  stream, approvals, admin endpoints. *Prefer this.* Pass a narrower role to assert a boundary.
- **`fixture.AuthorizedScopeAsync()`** — a DI scope with tenant, user and permissions populated, so
  you can resolve tool classes and call them directly. This is how tools run *after* the approval
  pipeline has done its part. Use it for dense domain assertions; it deliberately **bypasses** RBAC
  and the approval gate, so it can never prove those work.

A test that asserts "this write is approval-gated" **must** go through `AdminClient()`.

#### The approval-gate test is mandatory

`ChatAndApprovalTests` covers the full round trip over HTTP:

1. a chat turn on `propose_control_change` emits `CUSTOM(approval_required)`, and the reply does
   **not** claim the write happened;
2. `GET /api/chat/approvals` lists the pending call with its arguments;
3. `POST …/approve` executes it and clears it from the queue;
4. `POST …/reject` discards another without running it;
5. `compliance-analyst` can park a write and is **403** on the approval queue;
6. `compliance-reader` is never offered the write tool at all, and still gets its reads.

Asserting `RequiresApproval = true` on a descriptor is a *static* check — `ManifestGuardTests` does
that. It proves the flag is set, not that the gate fires. Without these tests Auditworthy could ship
a broken human-in-the-loop gate with fully green CI, which would make the product's central claim
false. Flipping both `RequiresApproval` flags to `false` turns **7** of these tests red; that is how
you re-prove the verifier if you ever doubt it.

Note that each test finds *its own* approval by the `conversationId` from `RUN_FINISHED`. The queue
is tenant-wide, so a test that took "the first pending approval" would resolve another test's call.

### Rung 4 — golden conversation evals

Prompt-shaped changes — the module's `AgentInstructions`, a tool's `[Description]`, an agent
profile — change behaviour without changing code. Evals give them the same regression net code has.
One JSON file per case in `tests/Auditworthy.IntegrationTests/Evals/cases/`:

```json
{
  "name": "compliance-write-requires-approval",
  "module": "compliance",
  "message": "Propose moving A.5.1 to Effective because the policy was approved",
  "role": "system_admin",
  "expectToolCalls": ["propose_control_change"],
  "forbidToolCalls": [],
  "expectApproval": true,
  "replyMustContain": ["approval"],
  "replyMustNotContain": ["moved from", "is now effective", "is now compliant", "certified"]
}
```

Every case also implicitly asserts `RUN_STARTED` + `RUN_FINISHED` present and `RUN_ERROR` absent.
Unknown fields fail loudly (`JsonUnmappedMemberHandling.Disallow`), so typos surface instead of
silently passing.

Add or adjust a case when you change:

| Change | Assert |
|---|---|
| a tool name or `[Description]` | the intent still routes there (`expectToolCalls`) |
| a `RequiresApproval` flag | `expectApproval` **and** the reply doesn't claim success |
| `AgentInstructions` / an agent profile | the reply reflects the policy (`replyMustContain`) |
| RBAC baselines | a narrower `role` + `forbidToolCalls` |

**Limit, stated up front:** the Mock provider selects tools by name-token match, not by reasoning.
These cases prove the **platform contract** — routing, gating, protocol — and nothing about answer
quality. Never write a case only a real model could satisfy; it becomes a flake, and a flake is how
the next agent talks itself into deleting the harness.

Pick `replyMustNotContain` fragments from the tool's *success* string, not from generic words. The
Mock reply legitimately opens with "Done — I called the … tool", so forbidding `"done"` fails on a
correctly gated turn. `propose_control_change` returns `"Control A.5.1 moved from X to Y"`, so
`"moved from"` is the honest discriminator.

## 7. The verification loop

A change is not done when it compiles. It is done when a test that **fails without it** passes
with it.

1. **Reproduce** — drive the failure through the *narrowest* surface that still shows it: a `.http`
   request, an AG-UI turn. Write down the exact input and the exact wrong output.
2. **Observe** — read the trace/logs (§5), not the source. Find the first place reality diverges
   from what you expected.
3. **Diagnose** — state the cause in one sentence. If you can't, you are still at step 2.
4. **Fix** — the smallest change that addresses that cause.
5. **Lock in** — add the test at the **lowest rung that would have caught it**. Run it against the
   *unfixed* code first and watch it fail. A regression test never seen red is not a regression test.
6. **Re-run the ladder** to the rung the change touches.

Exit condition: rungs 1–4 green, the new test red-then-green, and the behaviour exercised through a
real request. Escalate instead of looping if the same rung fails three times for different reasons —
that means the diagnosis is wrong, not the fix.

## 8. Gotchas

| Symptom | Cause / fix |
|---|---|
| `RUN_ERROR "AI provider is not configured"` | ContentRoot didn't load dev appsettings — set `-WorkingDirectory` to the bin folder (Mode B), or pass `--Ai:Provider=Mock` |
| `RUN_ERROR "Unknown module"` | module id must be `compliance` |
| `42P01: relation "platform.background_jobs" does not exist`, endless 500s | `Program.cs` was reduced to `app.UsePlenipoPlatform(); app.Run();`. Only `await app.RunPlenipoPlatformAsync()` also runs `InitializePlenipoAsync`, which applies the migrations. It looks like a job bug; it is a missing migration step |
| `42P01: relation "compliance.<table>" does not exist` | the module's own schema is never created: `ComplianceModule` must implement `IModule.MigrateAsync` (and `SeedAsync`). The platform migrates *itself* and then calls each module — it cannot invent your DDL |
| Aspire: containers up, API not there yet, stack "hangs" after the banner | **first give it four minutes** (§2 Mode A). If it is still absent: a stale Postgres **data volume** initialized with a different password (`docker volume rm auditworthy-pg-data`; dev data is throwaway), or someone re-added `WaitFor` on the two **database** resources — wait for the postgres **server**, never the databases, or the wait is circular |
| Aspire: `auditworthy-api` appears and then exits, with nothing in the AppHost console | project logs go to the **dashboard**, not to the terminal you launched from — open `/consolelogs/resource/auditworthy-api`, or relaunch with `aspire run` and read them through the MCP. Reproduce the same boot in seconds with Mode B, which shows the exception on stderr |
| Aspire refuses to start: *"the 'applicationUrl' setting must be an https address"* | you pinned the dashboard with an `http` URL. Use `--ASPNETCORE_URLS=https://localhost:18888` |
| Postgres data corrupted / ghost rows after running two AppHosts | both mounted the same data volume; the second cleared the first's `postmaster.pid` as stale. Host port 15433 is pinned so the second run now fails fast at bind time — **don't unpin it**, and never move it to 15432 (that is Networthy's) |
| Migration fails on `vector` type | the image must be **pgvector**, not stock `postgres` |
| `DLL is locked by .NET Host` on rebuild | a previous API process is still running — stop it first |
| Admin/usage endpoints return empty | token usage only exists after at least one chat turn |
| New tool never called, no error | it's missing from the manifest **or** from `ComplianceToolSource` — both are required, and `security/catalog` shows the gap |
| Tool 403s for `system_admin` | the permission string in the manifest and the tool source disagree — use `Permissions.ForTool(ComplianceModule.Id, "name")` in both |
| A new entity's rows appear across client organisations | a missing `HasQueryFilter` in `ComplianceDbContext`. `PlatformDbContext` applies filters by reflection; a module context does **not**. `ManifestGuardTests` fails the build for this — never delete that test to go faster |
| Testcontainers fails to start the reaper | the fixture sets `TESTCONTAINERS_RYUK_DISABLED=true` for exactly this; it disposes its own containers |
| Port already in use | change `--urls` (Mode B) or stop the stale process |

## 9. CI

`.github/workflows/ci.yml` gates every PR: restore → Release build with `-warnaserror` → test.
Docker is available on the GitHub runner, so rungs 3 and 4 run there too — an integration failure
is a red PR, not a local-only inconvenience.

Green CI is the floor, not the proof. CI cannot tell you the feature does what was asked — only
§7 can. State the level of your evidence, and never report an L4 conclusion with L1 confidence.
