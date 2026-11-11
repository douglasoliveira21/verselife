using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public sealed record BanRequest(string Name, string Reason, DateTimeOffset? ExpiresAt);
public sealed record BanChangeRequest(DateTimeOffset? ExpiresAt);

/// <summary>
/// The Banimentos tab: ban and lift bans, with a reason and a duration, and the history of every ban. The ban is applied where people come in (<see cref="IslandAccess.AllowsAsync"/>), so it holds
/// whatever the interface says. Owner and administrators can never be banned; people with the "ban" permission may ban and lift bans but may not touch the owner or administrators.
/// </summary>
public static class IslandBanManagement
{
    public const int PageSize = 10, ReasonLimit = 200;

    public static IEndpointRouteBuilder MapIslandBanManagement(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/worlds/islands/{islandId:guid}/management/bans").WithTags("Worlds").RequireAuthorization();
        group.MapGet("", ListAsync); group.MapPost("", BanAsync); group.MapPut("/{banId:guid}", ChangeAsync); group.MapDelete("/{banId:guid}", LiftAsync);
        return endpoints;
    }

    private static bool Who(HttpContext c, out Guid id) => Guid.TryParse(c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);

    private static async Task<(Island? Island, Guid User, IResult? Refusal)> ModeratorOf(Guid islandId, HttpContext context, LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        if (!Who(context, out var user)) return (null, default, Results.Unauthorized());
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return (null, user, Results.NotFound());
        var allowed = island.OwnerUserId == user || await db.IslandPermissions.AnyAsync(p => p.IslandId == islandId && p.UserId == user && (p.Permission == "admin" || p.Permission == "ban") && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > now), ct);
        return allowed ? (island, user, null) : (null, user, Results.Forbid());
    }

    private static string StatusOf(IslandBan b, DateTimeOffset now) => b.RevokedAt is not null ? "lifted" : b.ExpiresAt is { } e && e <= now ? "expired" : "active";

    private static async Task<object> Row(LifeVerseDbContext db, IslandBan b, DateTimeOffset now)
    {
        async Task<string> NameOf(Guid id) => await db.Avatars.AsNoTracking().Where(a => a.UserId == id).Select(a => a.DisplayName).FirstOrDefaultAsync() ?? await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Username).FirstOrDefaultAsync() ?? "";
        return new { banId = b.Id, userId = b.UserId, name = await NameOf(b.UserId), reason = b.Reason, bannedBy = await NameOf(b.BannedByUserId), createdAt = b.CreatedAt, expiresAt = b.ExpiresAt, liftedAt = b.RevokedAt, status = StatusOf(b, now) };
    }

    public static async Task<IResult> ListAsync(Guid islandId, string? q, string? status, int? page, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, _, refusal) = await ModeratorOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        var all = db.IslandBans.AsNoTracking().Where(b => b.IslandId == islandId);
        var counts = new
        {
            active = await all.CountAsync(b => b.RevokedAt == null && (b.ExpiresAt == null || b.ExpiresAt > now), ct),
            temporary = await all.CountAsync(b => b.RevokedAt == null && b.ExpiresAt != null && b.ExpiresAt > now, ct),
            permanent = await all.CountAsync(b => b.RevokedAt == null && b.ExpiresAt == null, ct),
            expired = await all.CountAsync(b => b.RevokedAt == null && b.ExpiresAt != null && b.ExpiresAt <= now, ct),
        };
        var query = all;
        query = status switch
        {
            "active" => query.Where(b => b.RevokedAt == null && (b.ExpiresAt == null || b.ExpiresAt > now)),
            "expired" => query.Where(b => b.RevokedAt == null && b.ExpiresAt != null && b.ExpiresAt <= now),
            "lifted" => query.Where(b => b.RevokedAt != null),
            _ => query,
        };
        var term = (q ?? "").Trim().ToLowerInvariant();
        if (term.Length > 0)
        {
            var matching = db.Users.AsNoTracking().Where(u => u.Username.ToLower().Contains(term) || db.Avatars.Any(a => a.UserId == u.Id && a.DisplayName.ToLower().Contains(term))).Select(u => u.Id);
            query = query.Where(b => matching.Contains(b.UserId) || b.Reason.ToLower().Contains(term));
        }
        var total = await query.CountAsync(ct); var index = Math.Max(1, page ?? 1);
        var rows = await query.OrderByDescending(b => b.CreatedAt).Skip((index - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        var items = new List<object>(); foreach (var row in rows) items.Add(await Row(db, row, now));
        return Results.Ok(new { counts, total, page = index, pageSize = PageSize, items });
    }

    public static async Task<IResult> BanAsync(Guid islandId, BanRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await ModeratorOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        var errors = new Dictionary<string, string[]>();
        var name = (request.Name ?? "").Trim().ToLowerInvariant(); var reason = (request.Reason ?? "").Trim();
        if (name.Length == 0) errors["name"] = ["Digite o nome de quem você quer banir."];
        if (reason.Length == 0) errors["reason"] = ["Escreva o motivo do banimento."]; else if (reason.Length > ReasonLimit) errors["reason"] = [$"O motivo pode ter no máximo {ReasonLimit} caracteres."];
        if (request.ExpiresAt is { } when && when <= now) errors["expiresAt"] = ["A data de término precisa ser no futuro."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var target = await db.Users.AsNoTracking().Where(u => u.Username.ToLower() == name || db.Avatars.Any(a => a.UserId == u.Id && a.DisplayName.ToLower() == name)).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (target is null) return Results.NotFound(new { error = "user_not_found", message = "Não encontrei ninguém com esse nome." });
        if (target == me) return Results.Conflict(new { error = "self", message = "Você não pode banir a si mesmo." });
        if (target == island.OwnerUserId || await db.IslandPermissions.AnyAsync(p => p.IslandId == islandId && p.UserId == target && p.Permission == "admin" && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > now), ct))
            return Results.Conflict(new { error = "staff", message = "O proprietário e os administradores não podem ser banidos." });
        if (await db.IslandBans.AnyAsync(b => b.IslandId == islandId && b.UserId == target && b.RevokedAt == null && (b.ExpiresAt == null || b.ExpiresAt > now), ct)) return Results.Conflict(new { error = "already", message = "Essa pessoa já está banida." });
        var ban = new IslandBan { Id = Guid.NewGuid(), IslandId = islandId, UserId = target.Value, BannedByUserId = me, Reason = reason, CreatedAt = now, ExpiresAt = request.ExpiresAt };
        var bannedName = await IslandAudit.NameAsync(db, target.Value, ct);
        db.IslandBans.Add(ban); IslandAudit.Add(db, islandId, me, "banimento", "ban_added", $"Baniu {bannedName}", bannedName, null, new { reason, expiresAt = request.ExpiresAt }, now); await db.SaveChangesAsync(ct);
        return Results.Ok(await Row(db, ban, now));
    }

    public static async Task<IResult> ChangeAsync(Guid islandId, Guid banId, BanChangeRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, _, refusal) = await ModeratorOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        if (request.ExpiresAt is { } when && when <= now) return Results.ValidationProblem(new Dictionary<string, string[]> { ["expiresAt"] = ["A data de término precisa ser no futuro."] });
        var ban = await db.IslandBans.SingleOrDefaultAsync(b => b.Id == banId && b.IslandId == islandId && b.RevokedAt == null && (b.ExpiresAt == null || b.ExpiresAt > now), ct); if (ban is null) return Results.NotFound();
        var oldEnd = ban.ExpiresAt; var changedName = await IslandAudit.NameAsync(db, ban.UserId, ct); Who(context, out var changer);
        ban.ExpiresAt = request.ExpiresAt; IslandAudit.Add(db, islandId, changer, "banimento", "ban_changed", $"Alterou a duração do banimento de {changedName}", changedName, new { expiresAt = oldEnd }, new { expiresAt = request.ExpiresAt }, now); await db.SaveChangesAsync(ct);
        return Results.Ok(await Row(db, ban, now));
    }

    public static async Task<IResult> LiftAsync(Guid islandId, Guid banId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await ModeratorOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        var ban = await db.IslandBans.SingleOrDefaultAsync(b => b.Id == banId && b.IslandId == islandId && b.RevokedAt == null && (b.ExpiresAt == null || b.ExpiresAt > now), ct); if (ban is null) return Results.NotFound();
        var liftedName = await IslandAudit.NameAsync(db, ban.UserId, ct);
        ban.RevokedAt = now; ban.RevokedByUserId = me; IslandAudit.Add(db, islandId, me, "banimento", "ban_lifted", $"Desbaniu {liftedName}", liftedName, new { reason = ban.Reason, expiresAt = ban.ExpiresAt }, null, now); await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
