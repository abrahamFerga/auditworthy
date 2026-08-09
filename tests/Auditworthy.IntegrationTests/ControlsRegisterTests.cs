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
    public async Task A_reader_can_load_the_register_behind_the_tab()
    {
        // Every other test of this endpoint authenticates as `system_admin`, which holds `*` — so
        // none of them can tell whether `compliance-reader` reaches the register or is refused by
        // it. The role is the one SPEC.md §6 gives the widest population, and the Controls tab is
        // the surface it lands on first; nothing asserted it worked.
        using var reader = fixture.AdminClient(roles: "compliance-reader", subject: "it-reader");

        var response = await reader.GetAsync("/api/compliance/controls");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains(
            "A.5.1",
            rows.EnumerateArray().Select(r => r.GetProperty("reference").GetString()));
    }

    /// <summary>
    /// The tab endpoint is gated on <c>compliance.view</c> specifically — not merely on being
    /// authenticated, and not on being privileged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Deleting <c>.RequireAuthorization(PermissionRequirement.PolicyName(ViewCompliance))</c>
    /// from the endpoint left the entire suite green</b> before this test existed, because every
    /// caller in it was <c>system_admin</c> and <c>*</c> satisfies any policy. The register would
    /// have become readable by anyone who could reach the shell, in a product whose whole claim is
    /// that a client organisation's compliance posture is not casually readable.
    /// <c>ManifestGuardTests.Tabs_that_expose_data_declare_a_permission</c> does not cover this: it
    /// asserts the <i>TabDescriptor</i> names a permission, which says nothing about the endpoint
    /// enforcing one.
    /// </para>
    /// <para>
    /// <c>tenant_admin</c> is the load-bearing case rather than a role with no grants at all. It
    /// holds <c>chat.*</c>, <c>files.*</c> and nine <c>platform.*</c> permissions, so a 403 for it
    /// cannot be explained by "the caller had nothing" — only by the endpoint requiring this
    /// module's own permission. A test written against an unknown role would pass just as well
    /// against an endpoint gated on nothing but authentication.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("tenant_admin")]
    [InlineData("user")]
    [InlineData("guest")]
    public async Task A_caller_without_compliance_view_cannot_read_the_register(string role)
    {
        using var client = fixture.AdminClient(roles: role, subject: $"it-{role}");

        var response = await client.GetAsync("/api/compliance/controls");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// The register the assistant reports is the register the tab renders.
    /// </summary>
    /// <remarks>
    /// <c>ChatAndApprovalTests.A_reader_is_never_offered_the_write_tool_at_all</c> asserts
    /// <c>list_controls</c> was <i>invoked</i>. That is a claim about routing, not about data: the
    /// tool could return an empty string, an error, or another tenant's rows and the assertion
    /// would still hold. This is the half of the acceptance criteria that says the turn streams
    /// control names and statuses, and it pins the chat surface and the tab surface to each other
    /// so the two cannot drift into disagreeing about what the register contains.
    /// </remarks>
    [Fact]
    public async Task The_assistant_reports_the_same_register_the_tab_renders()
    {
        using var client = fixture.AdminClient();

        var rows = JsonDocument.Parse(await client.GetStringAsync("/api/compliance/controls"))
            .RootElement.EnumerateArray()
            .Select(r => (
                Reference: r.GetProperty("reference").GetString()!,
                Status: r.GetProperty("status").GetString()!))
            .ToArray();

        // Without this the loop below iterates nothing and the test passes vacuously the moment
        // seeding regresses.
        Assert.NotEmpty(rows);

        var turn = await AguiStream.PostAsync(client, "compliance", "Which controls do we have?");

        Assert.False(turn.Failed, $"RUN_ERROR: {turn.Error}");
        Assert.Contains("list_controls", turn.ToolCalls);

        foreach (var (reference, status) in rows)
        {
            // Segment-matched rather than whole-string-matched: the tool lists controls separated
            // by ';', so this pairs each reference with ITS status instead of merely confirming
            // both strings appear somewhere in the reply — which every row would satisfy as long
            // as one row of each status was present.
            var segment = turn.Text
                .Split(';')
                .FirstOrDefault(s => s.Contains(reference, StringComparison.Ordinal));

            Assert.True(segment is not null,
                $"The reply never mentions control {reference}, which the tab renders. Reply: {turn.Text}");

            Assert.True(segment!.Contains(status, StringComparison.Ordinal),
                $"The reply reports {reference} without the status the tab shows ({status}). Segment: {segment}");
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
