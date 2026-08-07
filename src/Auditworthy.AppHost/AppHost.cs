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
builder.AddProject<Projects.Auditworthy_Host>("auditworthy-api")
    // Development, explicitly, and this is load-bearing.
    //
    // Aspire does NOT hand a project resource the AppHost's own environment, and neither project
    // has a launchSettings.json, so without this line the API starts in **Production**. The
    // platform then refuses to boot: the X-Dev-* dev-auth fallback is Development-only, and no
    // "Auth" section (Entra External ID Authority/Audience) is configured here — by design, since
    // this stack runs keyless. AddPlenipoPlatform() throws, and the failure is silent in the worst
    // way: containers go healthy, the banner prints "Distributed application started", and the API
    // resource goes Starting -> Running -> Finished in about three seconds. Project logs go to the
    // dashboard, not to the console you launched from, so nothing appears to be wrong.
    //
    // Exporting ASPNETCORE_ENVIRONMENT before `dotnet run` does not fix it — verified, not assumed.
    // The AppHost has to hand the value to the resource, which is what this does.
    //
    // This is NOT a weakening of the platform's guard. Refusing dev-auth outside Development is
    // correct and stays correct; this file is the *local* orchestrator (workflow.json: "cloud":
    // "none"), so declaring it a Development environment is a true statement, not an exemption.
    // Nothing deployed anywhere runs through here. AppHostCompositionTests asserts this line.
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
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
    .WithExternalHttpEndpoints();

builder.Build().Run();
