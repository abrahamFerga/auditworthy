using Plenipo.Testing;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// The real Auditworthy host on a throwaway Postgres: platform + module migrations run, the dev
/// tenant and seed data land, the job processor and hosted services start. Everything is real
/// except the AI provider (Mock) — the same keyless posture the Plenipo platform's own suite uses.
/// <para>
/// The container, the <c>WebApplicationFactory</c>, the dev-auth clients and
/// <see cref="PlenipoHostFixture{TProgram}.AuthorizedScopeAsync"/> now come from
/// <b>Plenipo.Testing</b> — the platform's conformance kit, shipped at <c>$(PlenipoVersion)</c> —
/// rather than from a copy maintained here (plenipo#189 / auditworthy#101). What survives in this
/// file is only what is Auditworthy's own: the <see cref="Contract"/> the kit's invariant packs run
/// against, the pg17 image this product ships on, and the #64 dev-auth identity derivation.
/// </para>
/// </summary>
public sealed class IntegrationFixture : PlenipoHostFixture<Program>
{
    /// <summary>
    /// What the kit needs to know about this product, read from the module manifest and
    /// <c>Program.cs</c>'s role baselines — never invented.
    /// <list type="bullet">
    /// <item><description><c>list_controls</c> is the read: audited, no approval.</description></item>
    /// <item><description><c>propose_control_change</c> is the write: <c>RequiresApproval = true</c> on
    /// both the descriptor and the <c>ModuleTool</c>.</description></item>
    /// <item><description><c>compliance-owner</c> is the accountable role — it alone holds
    /// <c>chat.approvals.manage</c> beside <c>tools.compliance.*</c>, so it is the only product role
    /// that can both park a write and release one. Deliberately not <c>system_admin</c>: the wildcard
    /// would prove the platform's own role rather than this product's.</description></item>
    /// <item><description><c>compliance-reader</c> is the narrow role: it may chat and read the
    /// register, holds neither the write tool's permission nor <c>chat.approvals.manage</c>.</description></item>
    /// </list>
    /// <para>
    /// <b><c>WritePrompt</c> is supplied rather than defaulted, and it is not decoration.</b> The
    /// kit's default turn ("Please propose control change for me, using a tool.") routes correctly,
    /// but the Mock provider fills required string arguments it cannot read out of the message with
    /// the placeholder <c>"example"</c> — and <c>propose_control_change</c> validates its arguments:
    /// <c>status</c> must parse as a <c>ControlStatus</c> and <c>reference</c> must name a control
    /// that exists. So the parked write always FAILED on release, and <c>S03</c>, which requires the
    /// approve call to succeed and one audited execution attributed to the requester, went red with
    /// <c>422 Unprocessable Entity</c> — the product's own
    /// <c>ChatAndApprovalTests.An_approved_write_that_cannot_be_applied_does_not_resolve_as_applied</c>
    /// asserts that same 422 deliberately, so this was a real product behaviour, not a kit bug.
    /// The Mock fills required string params from QUOTED spans in parameter order, so quoting the
    /// three arguments hands the tool a reference the starter register really holds and a status
    /// that really parses. S03 then proves the whole lane end to end instead of proving that a
    /// placeholder is not a control reference. Keep the quoted values in declaration order —
    /// <c>reference</c>, <c>status</c>, <c>reason</c> — and keep the word "controls" out of the
    /// sentence, or the Mock's name-token scoring routes the turn to <c>list_controls</c> instead.
    /// </para>
    /// </summary>
    public override ProductContract Contract { get; } = new(
        ModuleId: "compliance",
        ReadTool: "list_controls",
        WriteTool: "propose_control_change",
        ApproverRole: "compliance-owner",
        NarrowRole: "compliance-reader",
        ReadEndpoints: ["/api/compliance/controls"],
        WritePrompt: "Please propose control change: 'A.5.1' to 'Effective' because "
            + "'the platform conformance kit released this parked write'.");

    /// <summary>
    /// pgvector, not stock postgres: the platform's RAG migration creates a vector column at startup
    /// and fails on the <c>vector</c> type without the extension. Pinned to pg17 rather than taking
    /// the kit's pg16 default, to stay in step with the AppHost
    /// (<c>src/Auditworthy.AppHost/AppHost.cs</c>) — a product that runs on pg17 and tests on pg16 is
    /// testing something it does not ship.
    /// </summary>
    protected override string PostgresImage => "pgvector/pgvector:pg17";

    /// <summary>
    /// An authorized HTTP client for the dev tenant. PREFER THIS: it goes through the real
    /// pipeline, so it is the only way to prove RBAC, the approval gate and the AG-UI protocol.
    /// Pass a narrower role to assert a 403.
    /// <para>
    /// Hides the kit's two-argument <c>AdminClient</c> on purpose, and is not merely a convenience:
    /// it also sends <c>X-Dev-Name</c> and <c>X-Dev-Email</c> derived from the subject (#64), which
    /// the kit's client does not, and takes the tenant SLUG the caller presents (#78) so a test can
    /// work inside a tenant created during the test — the only way to prove what a second customer
    /// actually sees. <c>DevAuthHeaderConventionTests</c> counts every call site in the repository,
    /// so a caller assembled somewhere else re-introduces #64's constant actor.
    /// </para>
    /// </summary>
    public new HttpClient AdminClient(string roles = "system_admin", string subject = "it-admin") =>
        AdminClient(roles, subject, Contract.DevTenant);

    /// <summary>
    /// The same client in an arbitrary tenant SLUG (#78) — the only way to prove what a second
    /// client organisation actually sees. The tenant must already exist
    /// (<see cref="PlenipoHostFixture{TProgram}.EnsureTenantAsync"/> or the admin API creates one).
    /// <para>
    /// <paramref name="roles"/> carries no default deliberately: an optional third parameter beside
    /// the kit's two-parameter <c>AdminClient</c> makes an argument-less call ambiguous between the
    /// two, and the overload that would silently win is the kit's — the one that sends no
    /// <c>X-Dev-Email</c>. Requiring the role keeps the two entry points distinguishable at every
    /// call site.
    /// </para>
    /// </summary>
    public HttpClient AdminClient(string roles, string subject, string tenant)
    {
        var client = ClientFor(roles, tenant: tenant, subject: subject, displayName: DevDisplayName(subject));
        client.DefaultRequestHeaders.Add("X-Dev-Email", DevEmail(subject));
        return client;
    }

    /// <summary>
    /// A display name for a dev subject — <c>analyst-anna</c> becomes <c>Analyst Anna</c> (#64).
    /// <para>
    /// Dev-auth defaults the <c>name</c> claim to the constant "Dev User" for every subject, and the
    /// platform writes the persisted <c>User.DisplayName</c> from that claim at JIT provisioning and
    /// never again — the returning-user branch of <c>RequestEnricher</c> touches only
    /// <c>LastSeenAt</c>. So the constant is not a claim that #55's enricher can overrule; it becomes
    /// the record, and preferring the record faithfully reports it. The only place to break the tie
    /// is at the caller, before the row exists.
    /// </para>
    /// <para>
    /// Deliberately derived rather than looked up in a table: a table only names the subjects
    /// someone remembered to add, and the subject that silently falls back to "Dev User" is exactly
    /// the one #64 is about. Every subject gets a distinct name or the derivation is broken.
    /// </para>
    /// </summary>
    public static string DevDisplayName(string subject) =>
        string.Join(' ', subject
            .Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    /// <summary>
    /// An email for a dev subject — <c>analyst-anna</c> becomes
    /// <c>analyst-anna@dev.auditworthy.local</c> (#64, second half).
    /// <para>
    /// The display name was only half the constant. Dev-auth also defaults the <c>email</c> claim to
    /// <c>dev@plenipo.local</c> for EVERY subject, and <c>RequestEnricher</c> writes the persisted
    /// <c>User.Email</c> from that claim on the provision path — so Admin → Users lists every
    /// JIT-provisioned person at one address, which is the same failure as the name and is fixed the
    /// same way: at the caller, before the row exists.
    /// </para>
    /// <para>
    /// Derived rather than tabulated, for the reason given on <see cref="DevDisplayName"/>.
    /// <c>.local</c> mirrors the platform's own <c>dev@plenipo.local</c> — it is reserved for local
    /// use and cannot be mistaken for a deliverable address.
    /// </para>
    /// </summary>
    public static string DevEmail(string subject) => $"{subject}@dev.auditworthy.local";
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<IntegrationFixture>;
