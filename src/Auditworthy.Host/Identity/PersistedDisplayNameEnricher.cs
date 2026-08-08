using Microsoft.EntityFrameworkCore;
using Plenipo.AspNetCore.Identity;
using Plenipo.Infrastructure.Context;
using Plenipo.Infrastructure.Persistence;
using System.Security.Claims;

namespace Auditworthy.Host.Identity;

/// <summary>
/// TODO(plenipo#149) — remove this once the platform prefers the persisted user record itself.
/// <para>
/// The platform's <c>RequestEnricher</c> loads the persisted <c>User</c> and then populates the
/// request context from the TOKEN's <c>name</c> claim instead of that record
/// (<c>Plenipo.AspNetCore/Identity/RequestEnricher.cs</c>, alpha.28: the record is read at line 70,
/// discarded at line 177). Everything downstream reads <c>ICurrentUser.DisplayName</c>, so the
/// approvals queue's <c>userDisplay</c> and every tool-call audit row inherit the claim.
/// </para>
/// <para>
/// That is load-bearing here in a way it would not be in most products. Auditworthy's central claim
/// is that an accountable owner approves a change and an append-only audit records who asked. An
/// actor column that is a constant does not weaken that claim, it voids it — so this is corrected in
/// the product rather than waited on, and the wait is tracked as plenipo#149.
/// </para>
/// <para>
/// A decorator rather than a replacement: the platform still owns tenant resolution, JIT
/// provisioning, seat limits, invite redemption, deactivation and permission resolution — none of
/// which this product should have an opinion about. It re-issues exactly one value, afterwards. The
/// seam is that <c>PlenipoHostSetup</c> registers the enricher with a plain <c>AddScoped</c> rather
/// than <c>TryAdd</c>, so the registration in <c>Program.cs</c> wins by coming after
/// <c>AddPlenipoPlatform()</c>.
/// </para>
/// </summary>
public sealed class PersistedDisplayNameEnricher(
    RequestEnricher inner,
    RequestContext requestContext,
    PlatformDbContext db) : IRequestEnricher
{
    public async Task<bool> EnrichAsync(
        ClaimsPrincipal principal, string? ipAddress, CancellationToken cancellationToken)
    {
        var admitted = await inner.EnrichAsync(principal, ipAddress, cancellationToken);

        // A denial is the platform's to make and it means no identity was established — do not
        // reach for a record on the way out.
        if (!admitted || requestContext.UserId is not { } userId || requestContext.Subject is not { } subject)
        {
            return admitted;
        }

        var persisted = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);

        // Only when the record actually carries a name. A JIT-provisioned user's record is written
        // FROM the claim and may hold nothing, and replacing a real name with null would be a worse
        // defect than the one being fixed: it would blank the actor rather than constant it.
        if (!string.IsNullOrWhiteSpace(persisted))
        {
            requestContext.SetUser(userId, subject, persisted);
        }

        return admitted;
    }
}
