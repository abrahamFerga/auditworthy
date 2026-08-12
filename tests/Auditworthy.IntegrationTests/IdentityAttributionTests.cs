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

    /// <summary>
    /// The regression for #64. Two different people must be two different names in the audit.
    /// <para>
    /// #55 made the request context prefer the persisted <c>User</c> record over the token claim, so
    /// identity is a lookup. That is necessary and not sufficient: dev-auth defaults the <c>name</c>
    /// claim to the constant "Dev User" for every subject
    /// (<c>Plenipo.AspNetCore/Auth/DevAuthenticationHandler.cs:24</c>, alpha.28), and JIT
    /// provisioning writes the record FROM that claim
    /// (<c>Plenipo.AspNetCore/Identity/RequestEnricher.cs</c> — <c>DisplayName = name</c> on the
    /// provision path only; the returning-user branch refreshes <c>LastSeenAt</c> and nothing else).
    /// So the constant stops being a claim and becomes persisted truth, and preferring the record
    /// faithfully reports it.
    /// </para>
    /// <para>
    /// Dev-auth is the ONLY identity path this repo's verification story can exercise — every
    /// integration test, every <c>.http</c> request and every agent sweep runs through it. Without
    /// this, a sweep keeps reporting "the audit cannot attribute an action" and is right about the
    /// symptom while #55's mechanism works perfectly.
    /// </para>
    /// <para>
    /// Before the fix this fails on the FIRST assertion: both subjects read "Dev User".
    /// </para>
    /// </summary>
    [Fact]
    public async Task Two_dev_subjects_are_two_different_names_in_the_audit()
    {
        // Subjects used by no other test in the suite: the fixture is shared and provisioning is
        // first-contact-wins, so a subject another test already created would prove nothing here.
        using var anna = fixture.AdminClient(subject: "analyst-anna");
        using var olivia = fixture.AdminClient(subject: "owner-olivia");

        // Touch each one so the platform provisions it, then read the persisted records.
        (await anna.GetAsync("/api/platform/me")).EnsureSuccessStatusCode();
        (await olivia.GetAsync("/api/platform/me")).EnsureSuccessStatusCode();

        var users = JsonDocument.Parse(await anna.GetStringAsync("/api/admin/users")).RootElement;
        var annaPersisted = PersistedNameOf(users, "analyst-anna");
        var oliviaPersisted = PersistedNameOf(users, "owner-olivia");

        Assert.False(
            annaPersisted == oliviaPersisted,
            $"Two subjects share one display name ({annaPersisted}), so the audit's actor column "
            + "cannot tell them apart — the exact failure #64 describes.");

        // ...and the identity the approvals queue and the tool-call audit actually operate on has
        // to agree with the record, or the distinctness above is cosmetic.
        // Asserted on displayName alone, deliberately: /api/platform/me does NOT return `subject` at
        // alpha.28 — measured, `{"userId","displayName","tenantId","permissions"}` and nothing else.
        // (The platform working tree's MeDto carries Subject, TenantResolved and TenantProblem; the
        // VENDORED package this product compiles against does not. Trust the running host over a
        // source checkout that may be ahead of the pinned release.) The subject-to-name pairing is
        // established above, off /api/admin/users, which does key on subject.
        foreach (var (client, expected) in new[] { (anna, annaPersisted), (olivia, oliviaPersisted) })
        {
            var me = JsonDocument.Parse(await client.GetStringAsync("/api/platform/me")).RootElement;
            Assert.Equal(expected, me.GetProperty("displayName").GetString());
        }
    }

    /// <summary>
    /// The second half of #64, found by the sweep of 2026-08-09T02:47Z after the name fix was
    /// already written: dev-auth defaults the <c>email</c> claim to the constant
    /// <c>dev@plenipo.local</c> for every subject
    /// (<c>Plenipo.AspNetCore/Auth/DevAuthenticationHandler.cs:23</c>), and JIT provisioning writes
    /// the record from it (<c>RequestEnricher</c> — <c>Email = email ?? subject</c>). So the name
    /// fix alone leaves the provisioned record with no distinguishing field but the opaque id and
    /// the display name, and `GET /api/admin/users` — the surface an auditor is shown — still lists
    /// four different people at one address.
    /// <para>
    /// Asserted against the derivation rather than merely "these two differ": distinctness alone
    /// would also pass if the platform fell back to <c>Email = subject</c>, which is a different
    /// mechanism that this product must not silently start depending on. Equality proves the header
    /// reached the persisted row.
    /// </para>
    /// <para>
    /// Before the fix this fails on the first assertion: both subjects read
    /// <c>dev@plenipo.local</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Two_dev_subjects_are_two_different_emails_in_the_audit()
    {
        // Subjects used by no other test: provisioning is first-contact-wins on a shared fixture, so
        // a subject someone else already created would be asserting about their headers, not these.
        using var aisha = fixture.AdminClient(subject: "auditor-aisha");
        using var raj = fixture.AdminClient(subject: "reviewer-raj");

        (await aisha.GetAsync("/api/platform/me")).EnsureSuccessStatusCode();
        (await raj.GetAsync("/api/platform/me")).EnsureSuccessStatusCode();

        var users = JsonDocument.Parse(await aisha.GetStringAsync("/api/admin/users")).RootElement;
        var aishaEmail = PersistedEmailOf(users, "auditor-aisha");
        var rajEmail = PersistedEmailOf(users, "reviewer-raj");

        Assert.False(
            aishaEmail == rajEmail,
            $"Two subjects share one email ({aishaEmail}), so Admin → Users lists two people at one "
            + "address and the only field telling them apart is a GUID — the residue of #64 the "
            + "display-name fix does not reach.");

        Assert.Equal(IntegrationFixture.DevEmail("auditor-aisha"), aishaEmail);
        Assert.Equal(IntegrationFixture.DevEmail("reviewer-raj"), rajEmail);
    }

    private static string? PersistedNameOf(JsonElement users, string subject) =>
        users.EnumerateArray()
            .Single(u => u.GetProperty("subject").GetString() == subject)
            .GetProperty("displayName").GetString();

    private static string? PersistedEmailOf(JsonElement users, string subject) =>
        users.EnumerateArray()
            .Single(u => u.GetProperty("subject").GetString() == subject)
            .GetProperty("email").GetString();

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

    /// <summary>
    /// Deliberately NOT <see cref="IntegrationFixture.DevDisplayName"/>. #55's claim is that the
    /// persisted record beats the token, and #64's fix makes the token claim per-subject — for
    /// <c>acme-admin</c> that derivation would produce "Acme Admin", the very name the record holds,
    /// and the assertion would pass whichever one won. A claim that is definitely not the record is
    /// what keeps that test able to fail.
    /// </summary>
    private const string ClaimNameThatMustLose = "Token Claim That Must Lose";

    /// <summary>
    /// The same reasoning as <see cref="ClaimNameThatMustLose"/>, for the email claim: the record
    /// for <c>acme-admin</c> is written by the provisioning call above with
    /// <c>admin@acme.example</c>, so a claim email derived from the subject could coincide with it
    /// and hide a regression. This one cannot be mistaken for the record.
    /// </summary>
    private const string ClaimEmailThatMustLose = "token-claim-that-must-lose@dev.auditworthy.local";

    private HttpClient Client(string tenant, string subject)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Subject", subject);
        client.DefaultRequestHeaders.Add("X-Dev-Tenant", tenant);
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "system_admin");
        client.DefaultRequestHeaders.Add("X-Dev-Name", ClaimNameThatMustLose);
        client.DefaultRequestHeaders.Add("X-Dev-Email", ClaimEmailThatMustLose);
        return client;
    }
}
