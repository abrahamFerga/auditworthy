// Auditworthy local orchestration — the full Plenipo-based stack in one command:
//   dotnet run --project src/Auditworthy.AppHost    (or `aspire run` for telemetry)
//
// Zero-config: the assistant uses Plenipo's dependency-free Mock provider and the RAG pipeline
// uses the deterministic Mock embedder, so no API keys are required. v1 ships no bespoke UI —
// the module's tabs are server-driven and render in the platform shell.

var builder = DistributedApplication.CreateBuilder(args);

// STABLE dev password. Postgres bakes the password into the data volume at first init and never
// re-reads it — with Aspire's default *generated* password, losing or regenerating user-secrets
// leaves the volume unopenable ("28P01 password authentication failed"), the API waits forever on
// WaitFor, and the console shows nothing at all. A fixed dev default cannot drift. Local demo
// container credential only, never a production one.
var pgPassword = builder.AddParameter("plenipo-pg-password", "auditworthy-dev-only", secret: true);

var postgres = builder.AddPostgres("plenipo-pg", password: pgPassword)
    // pgvector-enabled Postgres: the platform's RAG migration creates a vector column at startup
    // and fails hard on stock `postgres`. A volume created by a different Postgres major needs
    // `docker volume rm` to reset — dev data is throwaway, so that is the fix, not a mystery.
    .WithImage("pgvector/pgvector")
    .WithImageTag("pg17")
    // FIXED host port, deliberately, and deliberately NOT 15432 — that is Networthy's. The data
    // volume is shared by every instance mounting it, and two AppHosts mounting one volume destroy
    // the cluster (the second container clears the first's postmaster.pid as "stale"; the first
    // self-kills mid-write, leaving corrupted indexes and ghost rows). With the port pinned, a
    // second concurrent run dies at bind time with a clear error instead.
    // NEVER unpin this to resolve a conflict — the conflict is the guard doing its job.
    .WithHostPort(15433)
    // Explicit volume name so this product can never share Networthy's dev cluster.
    .WithDataVolume("auditworthy-pg-data");

// Two databases: the platform's, and the SEPARATE append-only audit database. The names are the
// connection-string keys the platform resolves — do not rename them.
var platformDb = postgres.AddDatabase("plenipo-platform");
var auditDb = postgres.AddDatabase("plenipo-audit");

var redis = builder.AddRedis("plenipo-redis");

// Deployment AI defaults live in the Host's appsettings (Development = Mock, Production = None);
// real provider credentials are configured per tenant under Admin → AI Settings and stored
// write-only in Plenipo's secret vault. There is no deployment-level key slot by design.
// Auditworthy's own API port. Keep in step with RUNBOOK §2 and with
// AppHostCompositionTests.ExpectedApiPort.
const int ApiPort = 9433;

// launchProfileName: null — the API's endpoints are declared HERE and nowhere else.
//
// Aspire derives a project resource's endpoints from its launch profile's `applicationUrl` when one
// exists. This repo commits no launchSettings.json, but `dotnet run`/IDE tooling GENERATES one into
// src/Auditworthy.Host/Properties/ unasked, and an untracked generated file then silently decides
// which ports the API binds — different on every machine, absent in CI. Measured, not inferred
// (#79): with a generated profile present the resource carried two endpoints on that profile's
// random pair; with it removed the resource carried ZERO. Passing null makes the profile
// unreadable, so the declaration below is the only source of truth in both cases.
var api = builder.AddProject<Projects.Auditworthy_Host>("auditworthy-api", launchProfileName: null)
    .WithReference(platformDb)
    .WithReference(auditDb)
    .WithReference(redis)
    // Wait for the postgres SERVER, not the two database resources — and this is load-bearing.
    //
    // A database resource's health check connects to that database by name, and on a fresh volume
    // neither database exists yet. Nothing else creates them: the platform's DatabaseInitializer
    // does, via MigrateAsync()/EnsureCreatedAsync(), and it runs *inside this API*. So waiting on
    // the databases is a circular wait — the check can only pass after the API starts, and the API
    // cannot start until the check passes. The symptom is silent: containers healthy, dashboard up,
    // API simply absent, and postgres logging `FATAL: database "plenipo-platform" does not exist`
    // where nobody is looking.
    //
    // Waiting on the server keeps the real intent (do not start before Postgres accepts
    // connections) with no deadlock. Do not "fix" a slow first boot by putting these back.
    .WaitFor(postgres)
    // DECLARE the endpoint. WithExternalHttpEndpoints() alone does not create one — it only flips
    // IsExternal on endpoints that already exist, and this resource had none. That was #79's
    // unverified inference; it is now an experiment, run against the shipped Aspire.Hosting 13.4.6
    // assembly and kept as AppHostCompositionTests.Api_resource_declares_the_pinned_external_http_endpoint.
    // With no endpoint declared, DCP launched the API with neither --urls nor a launch profile, so
    // Kestrel fell back to the ASP.NET default and served on http://127.0.0.1:5000 — a port Aspire
    // knows nothing about, absent from the dashboard, and unproxyable.
    //
    // FIXED host port 9433, on the same reasoning as the Postgres pin above, and for one more:
    // several Plenipo products run scheduled loops on this machine, and :5000 (and the RUNBOOK's
    // Mode B :8094) are claimed by more than one of them. An /alive probe against a shared port can
    // answer 200 from a DIFFERENT product and read as a green verification of this one. 9433 is
    // Auditworthy's alone — no other product in the fleet declares it. Do NOT unpin this to dodge a
    // bind conflict: the conflict is the guard working. Confirm identity, never liveness alone —
    // GET /api/platform/modules must list `compliance`.
    //
    // isProxied: false so `port` IS the port Kestrel binds, rather than a proxy fronting a random
    // target port. One address, printable in the RUNBOOK, and a collision fails loudly at bind.
    .WithHttpEndpoint(port: ApiPort, targetPort: ApiPort, isProxied: false)
    .WithExternalHttpEndpoints();

// Development, explicitly, for a LOCAL RUN ONLY — and both halves of that are load-bearing.
//
// Why it is needed at all: Aspire does NOT hand a project resource the AppHost's own environment,
// and this resource is declared `launchProfileName: null`, so no launch profile can supply one
// either — without this the API starts in **Production**.
// The platform then refuses to boot — the X-Dev-* dev-auth fallback is Development-only, and no
// "Auth" section (Entra External ID Authority/Audience) is configured here, by design, since this
// stack runs keyless. AddPlenipoPlatform() throws, and the failure is silent in the worst way:
// containers go healthy, the banner prints "Distributed application started", and the API resource
// goes Starting -> Running -> Finished in about three seconds. Project logs go to the dashboard,
// not to the console you launched from, so nothing appears to be wrong. That was #54.
//
// Exporting ASPNETCORE_ENVIRONMENT before `dotnet run` does NOT substitute for this, and that is a
// runtime observation, not an inference: with this block deleted entirely and the variable exported
// in the launching shell, the API still died with the same
// AuthSetup.AddPlenipoAuthentication exception. Aspire does not propagate the AppHost's process
// environment to a project resource — the AppHost has to hand the value over. (Note that the
// composition tests below CANNOT establish that: they resolve the *declared* configuration and
// never start a process, so an inherited environment block could not appear in them either way.)
//
// Why it is guarded: in THIS repo the environment name is not a diagnostics convenience, it is the
// switch that turns on X-Dev-* header impersonation, because the platform's fallback guard is
// environment.IsDevelopment() and Auth is unconfigured. Applied unconditionally, the annotation
// would also be emitted for a publish/manifest operation, and a deployed API carrying it would
// accept any caller's claimed tenant and role. Nothing publishes this AppHost today
// (workflow.json: "cloud": "none"; no infra/, no azure.yaml, no publish job) — which is precisely
// why an unconditional version would have survived until the day one of those changed.
//
// So this is NOT a weakening of the platform's guard: refusing dev-auth outside Development is
// correct and stays correct, and this line now says the true, narrow thing — when a human runs the
// local orchestrator, the local API is a Development one. Do not unwrap the `if`; that turns a
// structural fact back into a comment. AppHostCompositionTests asserts BOTH directions — present
// under a run operation, absent under a publish operation.
if (builder.ExecutionContext.IsRunMode)
{
    api.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
}

builder.Build().Run();
