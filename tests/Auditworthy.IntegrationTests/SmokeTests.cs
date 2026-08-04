using System.Net;
using System.Text.Json;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// The host boots, the module loads, and every tool is wired to a permission. These are the
/// assertions that catch a broken product before anyone reads a single domain result.
/// </summary>
[Collection("api")]
public sealed class SmokeTests(IntegrationFixture fixture)
{
    [Theory]
    [InlineData("/alive")]
    [InlineData("/health")]
    public async Task Probe_endpoints_answer(string path)
    {
        // /health as well as /alive, deliberately: health endpoints have been mapped
        // Development-only by accident before, so a production liveness probe 404s against a
        // perfectly healthy app and the orchestrator restarts it forever.
        using var client = fixture.AdminClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Compliance_module_is_loaded_with_its_tabs()
    {
        using var client = fixture.AdminClient();

        var modules = JsonDocument.Parse(await client.GetStringAsync("/api/platform/modules")).RootElement;

        var compliance = modules.EnumerateArray()
            .SingleOrDefault(m => m.GetProperty("id").GetString() == "compliance");

        Assert.True(compliance.ValueKind is JsonValueKind.Object,
            "The compliance module did not load — AddPlenipoModule<ComplianceModule>() or the manifest is broken.");

        var tabs = compliance.GetProperty("tabs").EnumerateArray()
            .Select(t => t.GetProperty("id").GetString() ?? "")
            .ToArray();

        Assert.Equal(["chat", "controls"], tabs);
    }

    [Fact]
    public async Task Every_module_tool_appears_in_the_security_catalog()
    {
        // If a tool is in the manifest but not IModuleToolSource (or the permission strings
        // disagree), the tool is silently never callable. The catalog is where that gap shows.
        using var client = fixture.AdminClient();

        var catalog = await client.GetStringAsync("/api/admin/security/catalog");
        var permissions = Permissions(JsonDocument.Parse(catalog).RootElement).ToHashSet();

        Assert.Contains("tools.compliance.list_controls", permissions);
        Assert.Contains("tools.compliance.get_control", permissions);
        Assert.Contains("tools.compliance.propose_control_change", permissions);
    }

    [Fact]
    public async Task The_write_tool_is_the_only_one_the_catalog_marks_approval_gated()
    {
        // The product's central claim in one assertion: reads flow, writes wait for a human.
        using var client = fixture.AdminClient();

        var catalog = JsonDocument.Parse(await client.GetStringAsync("/api/admin/security/catalog")).RootElement;

        var gated = Entries(catalog)
            .Where(e => e.TryGetProperty("requiresApproval", out var r) && r.GetBoolean())
            .Select(e => e.GetProperty("permission").GetString() ?? "")
            .Where(p => p.StartsWith("tools.compliance.", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(["tools.compliance.propose_control_change"], gated);
    }

    [Fact]
    public async Task The_caller_is_resolved_into_the_dev_tenant()
    {
        using var client = fixture.AdminClient();

        var me = JsonDocument.Parse(await client.GetStringAsync("/api/platform/me")).RootElement;

        Assert.NotEqual(Guid.Empty, me.GetProperty("tenantId").GetGuid());
        Assert.Contains("*", me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
    }

    /// <summary>Every object in the catalog that carries a <c>permission</c>, at any depth.</summary>
    private static IEnumerable<JsonElement> Entries(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("permission", out _)) yield return element;
                foreach (var property in element.EnumerateObject())
                    foreach (var found in Entries(property.Value))
                        yield return found;
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var found in Entries(item))
                        yield return found;
                break;
        }
    }

    private static IEnumerable<string> Permissions(JsonElement element) =>
        Entries(element)
            .Select(e => e.GetProperty("permission").GetString())
            .Where(p => p is not null)!;
}
