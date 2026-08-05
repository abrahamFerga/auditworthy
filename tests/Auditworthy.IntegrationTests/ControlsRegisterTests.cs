using System.Net;
using System.Text.Json;
using Auditworthy.Compliance.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plenipo.Infrastructure.Context;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// The module's own schema exists at runtime, and the register it backs is tenant-filtered.
/// </summary>
/// <remarks>
/// These are integration tests and not module guards on purpose: <c>ComplianceModule</c> compiled,
/// every manifest guard passed, and the whole domain surface still 500'd on
/// <c>42P01: relation "compliance.controls" does not exist</c>, because <c>IModule.MigrateAsync</c>
/// and <c>SeedAsync</c> are defaulted members — omitting them is invisible to the compiler and to
/// every test that never asks a real database a question. Only a real migration against a real
/// Postgres can catch it.
/// </remarks>
[Collection("api")]
public sealed class ControlsRegisterTests(IntegrationFixture fixture)
{
    [Fact]
    public async Task The_controls_register_endpoint_answers()
    {
        // Went 500 with 42P01 before the module applied its own migrations.
        using var client = fixture.AdminClient();

        var response = await client.GetAsync("/api/compliance/controls");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Seed_controls_land_for_the_dev_tenant()
    {
        // The manifest's own suggested prompts reference control "A.5.1"; a register that does not
        // contain it makes the walking skeleton undemonstrable on a fresh clone.
        using var client = fixture.AdminClient();

        var rows = JsonDocument.Parse(await client.GetStringAsync("/api/compliance/controls")).RootElement;

        Assert.Equal(JsonValueKind.Array, rows.ValueKind);
        Assert.NotEmpty(rows.EnumerateArray());

        var references = rows.EnumerateArray()
            .Select(r => r.GetProperty("reference").GetString())
            .ToArray();

        Assert.Contains("A.5.1", references);
    }

    [Fact]
    public async Task Every_seeded_row_carries_the_columns_the_tab_renders()
    {
        // The Controls tab is server-driven: the platform renders these four keys as the grid, so a
        // renamed projection field shows an empty column rather than failing anything.
        using var client = fixture.AdminClient();

        var rows = JsonDocument.Parse(await client.GetStringAsync("/api/compliance/controls")).RootElement;

        // Without this guard the loop below iterates nothing and the test passes vacuously the
        // moment seeding regresses — asserting on an empty array is the classic silent no-op test.
        Assert.NotEmpty(rows.EnumerateArray());

        foreach (var row in rows.EnumerateArray())
        {
            foreach (var column in (string[])["reference", "name", "status", "owner"])
            {
                Assert.True(row.TryGetProperty(column, out _), $"Row is missing the '{column}' column.");
            }
        }
    }

    [Fact]
    public async Task Controls_are_invisible_from_another_tenant()
    {
        // The query filter, proven at runtime rather than by reflection. ManifestGuardTests asserts
        // a filter is DECLARED; only a real query proves it FILTERS. Seeded rows exist, and a scope
        // pointed at a different tenant must see none of them.
        using var scope = fixture.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RequestContext>();
        context.SetTenant(Guid.NewGuid());

        var db = scope.ServiceProvider.GetRequiredService<ComplianceDbContext>();

        var visible = await db.Controls.CountAsync();
        var everything = await db.Controls.IgnoreQueryFilters().CountAsync();

        Assert.True(everything > 0, "Nothing was seeded, so this proves nothing about the filter.");
        Assert.Equal(0, visible);
    }
}
