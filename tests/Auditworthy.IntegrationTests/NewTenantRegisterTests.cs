using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auditworthy.Host.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// A tenant created while the host is running is usable, not just created (#78).
/// </summary>
/// <remarks>
/// <para>
/// Every other test in this suite works inside the tenant the host seeded at startup, so the whole
/// suite stayed green while <c>POST /api/admin/tenants</c> returned 201 for a tenant that then had
/// an empty register, no create-control endpoint and a write tool that answered
/// <c>422 No control found with reference "A.5.1"</c> for everything. The second customer was the
/// case nothing exercised.
/// </para>
/// <para>
/// These assert through the real HTTP pipeline rather than through
/// <c>IntegrationFixture.AuthorizedScopeAsync</c>: the claim is about what an operator's request
/// produces and what the next request then sees, and a hand-built DI scope cannot make that claim.
/// </para>
/// </remarks>
[Collection("api")]
public sealed class NewTenantRegisterTests(IntegrationFixture fixture)
{
    /// <summary>The references <c>StarterRegister</c> lands, and that the manifest's prompts assume.</summary>
    private static readonly string[] StarterReferences =
        ["A.5.1", "A.5.15", "A.5.23", "A.6.3", "A.8.7", "A.8.16"];

    [Fact]
    public async Task A_tenant_created_through_the_admin_api_can_read_a_starter_register()
    {
        // Exactly the reproduction in #78, step 4 onward.
        using var operatorClient = fixture.AdminClient();
        var slug = NewSlug();

        var created = await operatorClient.PostAsJsonAsync(
            "/api/admin/tenants", new { name = "Acme Ltd", slug });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var tenantClient = fixture.AdminClient(subject: "acme-admin", tenant: slug);
        var response = await tenantClient.GetAsync("/api/compliance/controls");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var references = ReferencesIn(await response.Content.ReadAsStringAsync());

        // The whole register, not merely "not empty": a partial seed is the failure mode a
        // Assert.NotEmpty would wave through, and the manifest's suggested prompts name A.5.1.
        Assert.Equal(StarterReferences.OrderBy(r => r), references.OrderBy(r => r));
    }

    [Fact]
    public async Task A_provisioned_tenant_can_read_a_starter_register()
    {
        // The platform's supported one-call onboarding path, which fires ITenantProvisionedHook.
        // It had the same defect and would have kept it if only the bare create path were fixed.
        using var operatorClient = fixture.AdminClient();
        var slug = NewSlug();

        var created = await operatorClient.PostAsJsonAsync(
            "/api/admin/tenants/provision",
            new { name = "Beta Ltd", slug, adminEmail = $"{slug}@dev.auditworthy.local" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var tenantClient = fixture.AdminClient(subject: "beta-admin", tenant: slug);
        var references = ReferencesIn(await tenantClient.GetStringAsync("/api/compliance/controls"));

        Assert.Equal(StarterReferences.OrderBy(r => r), references.OrderBy(r => r));
    }

    [Fact]
    public async Task Seeding_a_new_tenant_leaves_every_other_tenant_alone()
    {
        // Seeding a tenant that did not exist a moment ago is a write made outside any request's
        // tenant context — the exact shape of change that leaks across the boundary if it reaches
        // for IgnoreQueryFilters. The dev tenant's register must be untouched, and the new tenant
        // must not be able to see it.
        using var operatorClient = fixture.AdminClient();
        var before = ReferencesIn(await operatorClient.GetStringAsync("/api/compliance/controls"));

        var slug = NewSlug();
        var created = await operatorClient.PostAsJsonAsync(
            "/api/admin/tenants", new { name = "Gamma Ltd", slug });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var after = ReferencesIn(await operatorClient.GetStringAsync("/api/compliance/controls"));
        Assert.Equal(before.OrderBy(r => r), after.OrderBy(r => r));

        using var tenantClient = fixture.AdminClient(subject: "gamma-admin", tenant: slug);
        var theirs = ReferencesIn(await tenantClient.GetStringAsync("/api/compliance/controls"));

        // Same references, different rows: six controls each, not one register shared by two
        // tenants. The count is what would move if the seed had landed cross-tenant.
        Assert.Equal(StarterReferences.Length, theirs.Length);
    }

    [Fact]
    public async Task Seeding_a_tenant_that_already_has_a_register_is_a_no_op()
    {
        // Seeding is triggered per tenant creation now, and both triggers can fire for one tenant
        // (an operator who provisions a slug the billing worker already provisioned). A second run
        // must do nothing: the unique index on (TenantId, Reference) would otherwise turn it into a
        // failed write rather than a no-op, and a register that duplicated itself would be worse
        // still. Called directly, because no HTTP path is allowed to create the same tenant twice —
        // which is exactly why the guarantee needs asserting somewhere else.
        using var operatorClient = fixture.AdminClient();
        var slug = NewSlug();

        var created = await operatorClient.PostAsJsonAsync(
            "/api/admin/tenants", new { name = "Delta Ltd", slug });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var tenantId = Guid.Parse(created.Headers.Location!.ToString().Split('/')[^1]);
        var provisioner = fixture.Factory.Services.GetRequiredService<NewTenantRegisterProvisioner>();

        await provisioner.EnsureStarterRegisterAsync(tenantId, slug, CancellationToken.None);
        await provisioner.EnsureStarterRegisterAsync(tenantId, slug, CancellationToken.None);

        using var tenantClient = fixture.AdminClient(subject: "delta-admin", tenant: slug);
        var references = ReferencesIn(await tenantClient.GetStringAsync("/api/compliance/controls"));

        Assert.Equal(StarterReferences.Length, references.Length);
    }

    private static string[] ReferencesIn(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateArray()
            .Select(r => r.GetProperty("reference").GetString()!)
            .ToArray();

    /// <summary>
    /// A slug no other test or run has used. The suite shares one host and one database, and the
    /// platform rejects a duplicate slug with 409 — a fixed slug would make these tests pass once
    /// and then fail for a reason that has nothing to do with what they assert.
    /// </summary>
    private static string NewSlug() => $"acme-{Guid.NewGuid():N}"[..16];
}
