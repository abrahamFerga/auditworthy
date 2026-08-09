using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// Who the platform thinks the caller is. Auditworthy's central claim is that an accountable owner
/// approves a change and the append-only audit records who asked — both of which are false if the
/// actor is a constant. These go through <see cref="IntegrationFixture.AdminClient"/>-shaped real
/// HTTP calls on purpose: <c>AuthorizedScopeAsync</c> sets the identity itself, so it can never
/// prove the request pipeline resolves one.
/// </summary>
[Collection("api")]
public sealed class IdentityAttributionTests(IntegrationFixture fixture)
{
    private const string Slug = "acme";
    private const string Subject = "acme-admin";
    private const string PersistedName = "Acme Admin";

    /// <summary>
    /// The regression for #55. A user provisioned as "Acme Admin" must be "Acme Admin" everywhere,
    /// not just on the one surface that happens to read the database.
    /// <para>
    /// Before the fix this fails on the LAST assertion with "Dev User" — the dev-auth fallback claim
    /// — because <c>RequestEnricher</c> loads the persisted user and then calls
    /// <c>SetUser(user.Id, subject, name)</c> with the TOKEN's name, discarding
    /// <c>user.DisplayName</c>. Everything reading <c>ICurrentUser.DisplayName</c> inherits that:
    /// the approvals queue's <c>userDisplay</c> and every tool-call audit row.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_caller_display_name_is_the_persisted_record_not_the_token_claim()
    {
        await ProvisionAcmeAsync();

        using var acme = Client(Slug, Subject);

        // The persisted record — the name an auditor would be shown in Admin → Users.
        var users = JsonDocument.Parse(await acme.GetStringAsync("/api/admin/users")).RootElement;
        var persisted = users.EnumerateArray()
            .Single(u => u.GetProperty("subject").GetString() == Subject)
            .GetProperty("displayName").GetString();

        Assert.Equal(PersistedName, persisted);

        // ...and the identity the rest of the platform actually operates on. These two disagreeing
        // is the defect: it is what puts a constant in the actor column of an append-only audit.
        var me = JsonDocument.Parse(await acme.GetStringAsync("/api/platform/me")).RootElement;

        Assert.Equal(persisted, me.GetProperty("displayName").GetString());
    }

    /// <summary>
    /// The dev tenant's own user is unaffected. A JIT-provisioned user's persisted name IS the claim,
    /// so preferring the record must be a no-op there — this is what would catch a "fix" that
    /// blanked the display name for everyone whose record has none.
    /// </summary>
    [Fact]
    public async Task A_jit_provisioned_caller_still_reports_a_display_name()
    {
        using var client = fixture.AdminClient();

        var me = JsonDocument.Parse(await client.GetStringAsync("/api/platform/me")).RootElement;

        Assert.False(string.IsNullOrWhiteSpace(me.GetProperty("displayName").GetString()),
            "The caller lost their display name entirely — worse than the constant it replaced.");
    }

    /// <summary>Idempotent: the fixture is shared, so a second run must not 409 the whole class.</summary>
    private async Task ProvisionAcmeAsync()
    {
        using var admin = fixture.AdminClient();

        var response = await admin.PostAsJsonAsync("/api/admin/tenants/provision", new
        {
            name = "Acme Ltd",
            slug = Slug,
            adminEmail = "admin@acme.example",
            adminSubject = Subject,
            adminDisplayName = PersistedName,
            modules = new[] { "compliance" },
            maxSeats = 10,
            monthlyTokenBudget = 1_000_000,
        });

        Assert.True(
            response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"Provisioning the second tenant failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    private HttpClient Client(string tenant, string subject)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Subject", subject);
        client.DefaultRequestHeaders.Add("X-Dev-Tenant", tenant);
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "system_admin");
        return client;
    }
}
