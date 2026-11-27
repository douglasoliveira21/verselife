using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Identity;

/// <summary>One sign-in that is still open (one device): every renewal of its tokens stays inside it.</summary>
public sealed record OpenSession(Guid Id, bool Current, string Device, DateTimeOffset SignedInAt, DateTimeOffset LastSeenAt);

/// <summary>
/// The places an account is signed in: <c>GET /api/v1/auth/sessions</c>, and ending one (<c>DELETE .../{id}</c>) or all the others (<c>DELETE .../sessions</c>).
/// Ending one takes effect on its next request, because every request checks its session. Only the account's own sessions are ever shown or touched.
/// </summary>
public static class AccountSessions
{
    public const int MaxListed = 50;

    private static bool Who(HttpContext context, out Guid user, out Guid session)
    {
        session = Guid.Empty;
        return Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out user) && Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sid)?.Value, out session);
    }

    private static Task<Guid> FamilyOfAsync(LifeVerseDbContext db, Guid user, Guid session, CancellationToken ct) =>
        db.Sessions.AsNoTracking().Where(s => s.Id == session && s.UserId == user).Select(s => s.FamilyId).SingleOrDefaultAsync(ct);

    public static async Task<IResult> ListAsync(HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Who(context, out var me, out var session)) return Results.Unauthorized();
        var now = clock.GetUtcNow(); var mine = await FamilyOfAsync(db, me, session, ct);
        var open = await db.Sessions.AsNoTracking().Where(s => s.UserId == me && s.RevokedAt == null && s.ExpiresAt > now).OrderByDescending(s => s.CreatedAt).Take(MaxListed)
            .Select(s => new { s.FamilyId, s.UserAgent, s.CreatedAt }).ToListAsync(ct);
        var families = open.Select(s => s.FamilyId).Distinct().ToList();
        var started = (await db.Sessions.AsNoTracking().Where(s => s.UserId == me && families.Contains(s.FamilyId)).GroupBy(s => s.FamilyId).Select(g => new { Family = g.Key, At = g.Min(s => s.CreatedAt) }).ToListAsync(ct))
            .ToDictionary(x => x.Family, x => x.At);
        var sessions = open.GroupBy(s => s.FamilyId).Select(g => { var latest = g.First(); return new OpenSession(g.Key, g.Key == mine, latest.UserAgent ?? string.Empty, started.GetValueOrDefault(g.Key, latest.CreatedAt), latest.CreatedAt); })
            .OrderByDescending(s => s.Current).ThenByDescending(s => s.LastSeenAt).ToList();
        return Results.Ok(sessions);
    }

    /// <summary>Ends the open sessions the filter picks and says how many sign-ins that was. A renewal running at the same moment can add one behind the first pass, so it looks again.</summary>
    private static async Task<int> EndAsync(LifeVerseDbContext db, Guid user, Guid? only, Guid? keep, DateTimeOffset now, CancellationToken ct)
    {
        var ended = new HashSet<Guid>();
        for (var pass = 0; pass < 3; pass++)
        {
            var open = await db.Sessions.Where(s => s.UserId == user && s.RevokedAt == null && (only == null || s.FamilyId == only) && (keep == null || s.FamilyId != keep)).ToListAsync(ct);
            if (open.Count == 0) break;
            foreach (var one in open) { one.RevokedAt = now; ended.Add(one.FamilyId); }
            await db.SaveChangesAsync(ct);
        }
        return ended.Count;
    }

    public static async Task<IResult> EndOneAsync(Guid id, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Who(context, out var me, out _)) return Results.Unauthorized();
        return await EndAsync(db, me, id, null, clock.GetUtcNow(), ct) == 0 ? Results.NotFound() : Results.NoContent();
    }

    public static async Task<IResult> EndOthersAsync(HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Who(context, out var me, out var session)) return Results.Unauthorized();
        var mine = await FamilyOfAsync(db, me, session, ct); if (mine == Guid.Empty) return Results.Unauthorized();
        return Results.Ok(new { ended = await EndAsync(db, me, null, mine, clock.GetUtcNow(), ct) });
    }
}
