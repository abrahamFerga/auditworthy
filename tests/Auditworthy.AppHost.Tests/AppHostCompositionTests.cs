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

    /// <summary>
    /// Args that put the builder into publish mode. Verified empirically against the shipped
    /// Aspire.Hosting 13.4.6 assembly rather than taken from documentation: with no args the
    /// resolved <c>DistributedApplicationExecutionContext.Operation</c> is <c>Run</c>, and with
    /// these it is <c>Publish</c> (<c>--publisher manifest</c> and <c>--Publishing:Publisher=…</c>
    /// do the same; <c>publish</c> as a bare verb does NOT).
    /// </summary>
    private static readonly string[] PublishModeArgs = ["--operation", "publish"];

    private static async Task<IReadOnlyDictionary<string, string>> ApiEnvironmentAsync(
        DistributedApplicationOperation expectedOperation,
        params string[] args)
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Auditworthy_AppHost>(args);

        await using var app = await builder.BuildAsync();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var api = Assert.Single(
            model.Resources.OfType<ProjectResource>(),
            r => r.Name == ApiResourceName);

        // Resolve the environment the way the orchestrator does, rather than reading the
        // annotations — a value contributed by WithReference or by a callback is only visible once
        // the configuration is actually built.
        var executionContext = app.Services.GetRequiredService<DistributedApplicationExecutionContext>();

        // Guard the guard. A publish-mode test that quietly resolved in run mode (or the reverse)
        // would assert nothing while looking green, so pin the operation the args were chosen to
        // produce before trusting anything resolved under it.
        Assert.Equal(expectedOperation, executionContext.Operation);

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
        var environment = await ApiEnvironmentAsync(DistributedApplicationOperation.Run);

        Assert.True(
            environment.TryGetValue("ASPNETCORE_ENVIRONMENT", out var value),
            $"The AppHost gives '{ApiResourceName}' no ASPNETCORE_ENVIRONMENT for a run operation, " +
            "so it starts in Production and AddPlenipoPlatform() throws on startup: dev-auth is " +
            "Development-only and no Auth section is configured. The AppHost has to hand the value " +
            "to the resource — see the run-mode guard in AppHost.cs.");

        Assert.Equal("Development", value);
    }

    /// <summary>
    /// The other half of the same guard, and the one that matters for anything that ever leaves
    /// this machine.
    ///
    /// In this repo the environment NAME is the switch that turns on X-Dev-* header
    /// impersonation: <c>src/Auditworthy.Host/appsettings.json</c> configures no real Auth
    /// (Authority and Audience are both empty), and the platform's fallback guard is
    /// <c>environment.IsDevelopment()</c>. So an <c>ASPNETCORE_ENVIRONMENT=Development</c> that
    /// leaked into a published manifest would not be a diagnostics convenience — it would be a
    /// deployed API that accepts any caller's claimed tenant and role.
    ///
    /// Nothing publishes this AppHost today (<c>workflow.json</c>: <c>"cloud": "none"</c>, no
    /// <c>infra/</c>, no <c>azure.yaml</c>, no publish job), which is exactly why an unconditional
    /// annotation would have survived unnoticed until the day one of those changed. This test is
    /// what makes "run mode only" a structural fact instead of a comment.
    /// </summary>
    [Fact]
    public async Task Api_resource_is_not_handed_a_Development_environment_in_publish_mode()
    {
        var environment = await ApiEnvironmentAsync(
            DistributedApplicationOperation.Publish,
            PublishModeArgs);

        Assert.False(
            environment.ContainsKey("ASPNETCORE_ENVIRONMENT"),
            $"The AppHost hands '{ApiResourceName}' " +
            $"ASPNETCORE_ENVIRONMENT={environment.GetValueOrDefault("ASPNETCORE_ENVIRONMENT")} " +
            "for a PUBLISH operation, so a published manifest would carry it. In this repo no Auth " +
            "section is configured and the platform gates X-Dev-* header impersonation on " +
            "IsDevelopment(), so that is a deployed API trusting any caller's claimed tenant and " +
            "role. Wrap the WithEnvironment call in `if (builder.ExecutionContext.IsRunMode)`.");
    }
}
