using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Auditworthy.AppHost.Tests;

/// <summary>
/// The AppHost composition guard.
///
/// Mode A — <c>dotnet run --project src/Auditworthy.AppHost</c> — is what AGENTS.md and RUNBOOK.md
/// both name as *the* way to run this product. It once did not work at all: the API resource was
/// launched with no environment of its own, so it started in Production, and the platform refuses
/// to fall back to X-Dev-* dev-auth outside Development. The API went Starting -> Running ->
/// Finished in about three seconds and never listened on any port.
///
/// Every ordinary signal said the stack was fine. The build was green, the containers went healthy,
/// and the console printed "Distributed application started" — because Aspire routes project logs
/// to the dashboard, not to the terminal you launched from. Nothing failed loudly; the API was
/// simply absent.
///
/// These tests build the distributed application model and resolve the resource's execution
/// configuration. They never call StartAsync, so no container is ever pulled or run.
/// </summary>
public sealed class AppHostCompositionTests
{
    private const string ApiResourceName = "auditworthy-api";

    private static async Task<IReadOnlyDictionary<string, string>> ApiEnvironmentAsync()
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Auditworthy_AppHost>();

        await using var app = await builder.BuildAsync();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var api = Assert.Single(
            model.Resources.OfType<ProjectResource>(),
            r => r.Name == ApiResourceName);

        // Resolve the environment the way the orchestrator does for a `run` operation, rather than
        // reading the annotations — a value contributed by WithReference or by a callback is only
        // visible once the configuration is actually built.
        var executionContext = app.Services.GetRequiredService<DistributedApplicationExecutionContext>();

        var configuration = await ExecutionConfigurationBuilder.Create(api)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(executionContext, NullLogger.Instance, CancellationToken.None);

        Assert.Null(configuration.Exception);

        // The resolved set is a sequence, not a map, and a key may be contributed more than once.
        // Fold it the way a process environment is actually applied: last write wins.
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var variable in configuration.EnvironmentVariables)
        {
            environment[variable.Key] = variable.Value;
        }

        return environment;
    }

    [Fact]
    public async Task Api_resource_is_launched_in_an_environment_that_can_actually_boot()
    {
        var environment = await ApiEnvironmentAsync();

        Assert.True(
            environment.TryGetValue("ASPNETCORE_ENVIRONMENT", out var value),
            $"The AppHost gives '{ApiResourceName}' no ASPNETCORE_ENVIRONMENT, so it starts in " +
            "Production and AddPlenipoPlatform() throws on startup: dev-auth is Development-only " +
            "and no Auth section is configured. Aspire does NOT inherit the AppHost's own " +
            "environment for project resources, so exporting the variable before `dotnet run` does " +
            "not fix this — the AppHost has to hand it to the resource.");

        Assert.Equal("Development", value);
    }
}
