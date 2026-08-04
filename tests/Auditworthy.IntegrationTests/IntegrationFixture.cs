using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plenipo.Infrastructure.Context;
using Plenipo.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// The real Auditworthy host on a throwaway Postgres: platform + module migrations run, the dev
/// tenant and seed data land, the job processor and hosted services start. Everything is real
/// except the AI provider (Mock) — the same keyless posture the Plenipo platform's own suite uses.
/// </summary>
public sealed class IntegrationFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public WebApplicationFactory<Program> Factory { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        // Ryuk is the Testcontainers reaper container. It needs the Docker socket mounted, which
        // Docker Desktop on Windows does not always grant; the containers this fixture creates are
        // disposed explicitly below, so the reaper buys nothing here and costs a startup failure.
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");

        _postgres = new PostgreSqlBuilder()
            // pgvector, not stock postgres: the platform's RAG migration creates a vector column at
            // startup and fails on the `vector` type without the extension. Keep this major in sync
            // with the AppHost (src/Auditworthy.AppHost/AppHost.cs pins pg17) — a product that runs
            // on pg17 and tests on pg16 is testing something it does not ship.
            .WithImage("pgvector/pgvector:pg17")
            .WithDatabase("plenipo_platform")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await _postgres.StartAsync();

        // The platform resolves both databases by connection-string NAME. In production they are
        // separate databases (see the AppHost); one container is enough to exercise the schemas.
        Environment.SetEnvironmentVariable("ConnectionStrings__plenipo-platform", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__plenipo-audit", _postgres.GetConnectionString());

        Factory = new AuditworthyAppFactory();

        // The first request boots the host (migrations + seeding); the authenticated call makes the
        // request enricher provision the dev-tenant user that AuthorizedScopeAsync relies on.
        using var warmup = AdminClient();
        (await warmup.GetAsync("/alive")).EnsureSuccessStatusCode();
        (await warmup.GetAsync("/api/platform/modules")).EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__plenipo-platform", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__plenipo-audit", null);

        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    /// <summary>
    /// An authorized HTTP client for the dev tenant. PREFER THIS: it goes through the real
    /// pipeline, so it is the only way to prove RBAC, the approval gate and the AG-UI protocol.
    /// Pass a narrower role to assert a 403.
    /// </summary>
    public HttpClient AdminClient(string roles = "system_admin", string subject = "it-admin")
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Subject", subject);
        client.DefaultRequestHeaders.Add("X-Dev-Tenant", "dev");
        client.DefaultRequestHeaders.Add("X-Dev-Roles", roles);
        return client;
    }

    /// <summary>
    /// A DI scope with tenant + user + permissions populated — how module tools run AFTER the
    /// platform's auth/approval pipeline has done its part. Deliberately bypasses RBAC and the
    /// approval gate, so it can never prove either works: use <see cref="AdminClient"/> for those.
    /// </summary>
    public async Task<(IServiceScope Scope, Guid TenantId, Guid UserId)> AuthorizedScopeAsync()
    {
        var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var context = scope.ServiceProvider.GetRequiredService<RequestContext>();
        var tenant = await db.Tenants.FirstAsync(t => t.Slug == "dev");
        context.SetTenant(tenant.Id);
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.TenantId == tenant.Id);
        context.SetUser(user.Id, user.Subject, user.DisplayName);
        context.SetPermissions(["*"]);
        return (scope, tenant.Id, user.Id);
    }

    private sealed class AuditworthyAppFactory : WebApplicationFactory<Program>
    {
        // Development is what turns on dev-auth and the Mock AI provider
        // (src/Auditworthy.Host/appsettings.Development.json).
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<IntegrationFixture>;
