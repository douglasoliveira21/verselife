using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

public sealed record ReadNoticesRequest(Guid[]? Ids);

/// <summary>
/// What the real estate tells people on its own: a rental that is about to run out (3 days and 1 day before, for the renter and the owner), one that fell into grace or ended, a renewal that could not be charged,
/// and a sale or a rental that someone made of what the person owns. A notice carries a key so the same thing is said once; the person reads them in Minhas Propriedades.
/// </summary>
public static class PropertyNotices
{
    public static readonly TimeSpan[] Warnings = [TimeSpan.FromDays(3), TimeSpan.FromDays(1)];

    /// <summary>Adds a notice to the unit of work (saved with whatever the caller saves). Nothing is added when this key was already said to this person.</summary>
    public static void Add(LifeVerseDbContext db, Guid user, string kind, string title, string body, string key, DateTimeOffset now)
    {
        if (db.PropertyNotices.Local.Any(n => n.UserId == user && n.DedupeKey == key)) return;
        db.PropertyNotices.Add(new PropertyNotice { Id = Guid.NewGuid(), UserId = user, Kind = kind, Title = title.Length > 200 ? title[..200] : title, Body = body.Length > 1000 ? body[..1000] : body, DedupeKey = key.Length > 128 ? key[..128] : key, CreatedAt = now });
    }

    private static string In(TimeSpan span) => span.TotalHours >= 48 ? $"{(int)Math.Round(span.TotalDays)} dias" : span.TotalHours >= 2 ? $"{(int)Math.Round(span.TotalHours)} horas" : "menos de 2 horas";

    /// <summary>Says to the renter and the owner that a contract is about to run out. Run by the rental worker every minute; each warning is said once per expiry date (a renewal moves the date, so the next one is said again).</summary>
    public static async Task<int> ScanAsync(LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var horizon = now + Warnings[0]; var candidates = new List<(Guid User, string Kind, string Title, string Body, string Key)>();
        var rentals = await db.ParcelRentals.AsNoTracking().Where(r => r.Status == "active" && r.ExpiresAt > now && r.ExpiresAt <= horizon).ToListAsync(ct);
        var leases = await db.IslandLeases.AsNoTracking().Where(l => l.Status == "active" && l.ExpiresAt > now && l.ExpiresAt <= horizon).ToListAsync(ct);
        var parcelIds = rentals.Select(r => r.ParcelId).ToList(); var islandIds = leases.Select(l => l.IslandId).ToList();
        var parcelNames = await db.IslandParcels.AsNoTracking().Where(p => parcelIds.Contains(p.Id)).Select(p => new { p.Id, p.Code, p.Name }).ToDictionaryAsync(p => p.Id, p => p.Code.Length > 0 ? $"{p.Code} — {p.Name}" : p.Name, ct);
        var islandNames = await db.Islands.AsNoTracking().Where(i => islandIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
        var people = rentals.SelectMany(r => new[] { r.LandlordUserId, r.TenantUserId }).Concat(leases.SelectMany(l => new[] { l.LandlordUserId, l.TenantUserId }));
        var names = await LandOverview.NamesAsync(db, people, ct);
        string Who(Guid id) => names.GetValueOrDefault(id, "—");

        void Warn(Guid id, string what, DateTimeOffset expires, Guid tenant, Guid landlord, bool auto)
        {
            var left = expires - now;
            // the nearest warning that already passed is the one to say (1 day, else 3 days); an older one is not said again
            var level = left <= Warnings[1] ? 1 : 0; var tag = $"exp{level}:{id}:{expires.UtcTicks}";
            var when = $"vence em {In(left)} ({expires.ToLocalTime():dd/MM/yyyy HH:mm})";
            candidates.Add((tenant, "rent_expiring", $"Seu aluguel de {what} {when}", auto ? "A renovação automática está ligada: um período a mais será cobrado da sua carteira. Mantenha saldo." : "Renove em Minhas Propriedades para não perder o terreno. Depois do vencimento há um prazo de tolerância e, no fim dele, o contrato termina.", tag + ":t"));
            candidates.Add((landlord, "rent_expiring_owner", $"O aluguel de {what} para {Who(tenant)} {when}", "Se não for renovado, o contrato entra em tolerância e depois termina sozinho.", tag + ":l"));
        }
        foreach (var r in rentals) Warn(r.Id, parcelNames.GetValueOrDefault(r.ParcelId, "um terreno"), r.ExpiresAt, r.TenantUserId, r.LandlordUserId, r.AutoRenew);
        foreach (var l in leases) Warn(l.Id, $"a ilha {islandNames.GetValueOrDefault(l.IslandId, "—")}", l.ExpiresAt, l.TenantUserId, l.LandlordUserId, l.AutoRenew);
        if (candidates.Count == 0) return 0;
        // a notice already said is not said again: ask which keys exist before adding
        var keys = candidates.Select(c => c.Key).Distinct().ToList();
        var said = (await db.PropertyNotices.AsNoTracking().Where(n => keys.Contains(n.DedupeKey)).Select(n => new { n.UserId, n.DedupeKey }).ToListAsync(ct)).Select(n => (n.UserId, n.DedupeKey)).ToHashSet();
        var added = 0;
        foreach (var c in candidates) if (!said.Contains((c.User, c.Key))) { Add(db, c.User, c.Kind, c.Title, c.Body, c.Key, now); added++; }
        if (added > 0) await db.SaveChangesAsync(ct);
        return added;
    }

    public static async Task<string> NameAsync(LifeVerseDbContext db, Guid user, CancellationToken ct) => (await LandOverview.NamesAsync(db, [user], ct)).GetValueOrDefault(user, "—");

    public static async Task<object> ListAsync(LifeVerseDbContext db, Guid user, int take, CancellationToken ct)
    {
        var rows = await db.PropertyNotices.AsNoTracking().Where(n => n.UserId == user).OrderByDescending(n => n.CreatedAt).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);
        var unread = await db.PropertyNotices.AsNoTracking().CountAsync(n => n.UserId == user && n.ReadAt == null, ct);
        return new { unread, items = rows.Select(n => new { id = n.Id, kind = n.Kind, title = n.Title, body = n.Body, at = n.CreatedAt, read = n.ReadAt != null }) };
    }

    public static async Task<int> MarkReadAsync(LifeVerseDbContext db, Guid user, IReadOnlyCollection<Guid>? ids, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.PropertyNotices.Where(n => n.UserId == user && n.ReadAt == null && (ids == null || ids.Count == 0 || ids.Contains(n.Id))).ToListAsync(ct);
        foreach (var n in rows) n.ReadAt = now;
        await db.SaveChangesAsync(ct); return rows.Count;
    }

    public static IEndpointRouteBuilder MapPropertyNoticeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        static bool Who(HttpContext c, out Guid id) => Guid.TryParse(c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);
        endpoints.MapGet("/api/v1/properties/notices", async (int? take, HttpContext c, LifeVerseDbContext db, CancellationToken ct) =>
            !Who(c, out var me) ? Results.Unauthorized() : Results.Ok(await ListAsync(db, me, take ?? 30, ct))).WithTags("Discovery").RequireAuthorization();
        endpoints.MapPost("/api/v1/properties/notices/read", async (ReadNoticesRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            !Who(c, out var me) ? Results.Unauthorized() : Results.Ok(new { read = await MarkReadAsync(db, me, r.Ids, t.GetUtcNow(), ct) })).WithTags("Discovery").RequireAuthorization();
        return endpoints;
    }
}
