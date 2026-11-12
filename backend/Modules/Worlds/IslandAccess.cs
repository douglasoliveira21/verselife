using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public sealed record AccessModeRequest(string Mode, DateTimeOffset ExpectedUpdatedAt);
public sealed record AllowRequest(string Name, DateTimeOffset? ExpiresAt);
public sealed record AllowChangeRequest(DateTimeOffset? ExpiresAt);

/// <summary>
/// Who may enter an island. One rule, used where a person asks to come in (entry token, teleport): the owner and administrators always can; if the island is temporarily closed nobody else can;
/// otherwise a public island is open to everybody and a private one only to people on its list, as long as their permission has not expired.
/// </summary>
public static class IslandAccess
{
    public const string Public = "public", Invite = "invite", Closed = "closed";
    private static readonly string[] EntryPermissions = ["enter", "build", "edit", "admin"];

    public static string ModeOf(Island island) => island.AccessClosed ? Closed : island.IsPublic ? Public : Invite;

    public static async Task<bool> AllowsAsync(LifeVerseDbContext db, Island island, Guid user, DateTimeOffset now, CancellationToken ct)
    {
        if (island.OwnerUserId == user) return true;
        var permissions = await db.IslandPermissions.AsNoTracking().Where(p => p.IslandId == island.Id && p.UserId == user && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > now)).Select(p => p.Permission).ToListAsync(ct);
        if (permissions.Contains("admin")) return true;
        if (await db.IslandBans.AsNoTracking().AnyAsync(b => b.IslandId == island.Id && b.UserId == user && b.RevokedAt == null && (b.ExpiresAt == null || b.ExpiresAt > now), ct)) return false;
        if (island.AccessClosed) return false;
        return island.IsPublic || permissions.Any(p => EntryPermissions.Contains(p));
    }
}

/// <summary>The Acesso tab of the island management window: the mode and the list of people allowed in.</summary>
public static class IslandAccessManagement
{
    public const int PageSize = 10;

    public static IEndpointRouteBuilder MapIslandAccessManagement(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/worlds/islands/{islandId:guid}/management/access").WithTags("Worlds").RequireAuthorization();
        group.MapGet("", GetAsync); group.MapPut("/mode", SetModeAsync); group.MapPost("/allow", AllowAsync); group.MapPut("/allow/{permissionId:guid}", ChangeAsync); group.MapDelete("/allow/{permissionId:guid}", RemoveAsync);
        return endpoints;
    }

    private static string ModeTitle(string mode) => mode switch { IslandAccess.Public => "Pública", IslandAccess.Invite => "Somente convidados", _ => "Temporariamente fechada" };

    private static bool Who(HttpContext c, out Guid id) => Guid.TryParse(c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);

    private static async Task<(Island? Island, IResult? Refusal)> AdminOf(Guid islandId, HttpContext context, LifeVerseDbContext db, bool track, CancellationToken ct)
    {
        if (!Who(context, out var user)) return (null, Results.Unauthorized());
        var query = track ? db.Islands.AsQueryable() : db.Islands.AsNoTracking();
        var island = await query.SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return (null, Results.NotFound());
        var admin = island.OwnerUserId == user || await db.IslandPermissions.AnyAsync(p => p.IslandId == islandId && p.UserId == user && p.Permission == "admin" && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > DateTimeOffset.UtcNow), ct);
        return admin ? (island, null) : (null, Results.Forbid());
    }

    private static IQueryable<IslandPermission> Active(LifeVerseDbContext db, Guid islandId, DateTimeOffset now) =>
        db.IslandPermissions.Where(p => p.IslandId == islandId && p.Permission == "enter" && p.PrincipalType == "user" && p.UserId != null && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > now));

    private static async Task<object> Row(LifeVerseDbContext db, IslandPermission p)
    {
        var user = await db.Users.AsNoTracking().Where(u => u.Id == p.UserId).Select(u => u.Username).FirstOrDefaultAsync() ?? "";
        var avatar = await db.Avatars.AsNoTracking().Where(a => a.UserId == p.UserId).Select(a => a.DisplayName).FirstOrDefaultAsync() ?? user;
        return new { permissionId = p.Id, userId = p.UserId, name = avatar, username = user, addedAt = p.CreatedAt, expiresAt = p.ExpiresAt };
    }

    public static async Task<IResult> GetAsync(Guid islandId, string? q, int? page, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var (island, refusal) = await AdminOf(islandId, context, db, false, ct); if (island is null) return refusal!;
        var now = clock.GetUtcNow();
        var query = Active(db, islandId, now).AsNoTracking();
        var term = (q ?? "").Trim().ToLowerInvariant();
        if (term.Length > 0)
        {
            var matching = db.Users.AsNoTracking().Where(u => u.Username.ToLower().Contains(term) || db.Avatars.Any(a => a.UserId == u.Id && a.DisplayName.ToLower().Contains(term))).Select(u => u.Id);
            query = query.Where(p => matching.Contains(p.UserId!.Value));
        }
        var total = await query.CountAsync(ct); var index = Math.Max(1, page ?? 1);
        var rows = await query.OrderByDescending(p => p.CreatedAt).Skip((index - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        var items = new List<object>(); foreach (var row in rows) items.Add(await Row(db, row));
        return Results.Ok(new { mode = IslandAccess.ModeOf(island), updatedAt = island.UpdatedAt, total, page = index, pageSize = PageSize, items });
    }

    public static async Task<IResult> SetModeAsync(Guid islandId, AccessModeRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var (island, refusal) = await AdminOf(islandId, context, db, true, ct); if (island is null) return refusal!;
        if (!Who(context, out var asker) || island.OwnerUserId != asker) return IslandOwnerOnly.Refusal();
        if (request.Mode is not (IslandAccess.Public or IslandAccess.Invite or IslandAccess.Closed)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["mode"] = ["Modo de acesso inválido."] });
        if (island.UpdatedAt != request.ExpectedUpdatedAt) return Results.Conflict(new { error = "stale", message = "Outra pessoa alterou a ilha antes de você. Recarregue para ver as mudanças.", updatedAt = island.UpdatedAt });
        Who(context, out var actor); var was = IslandAccess.ModeOf(island);
        // Closing keeps whether the island was public, so opening it again brings it back as it was.
        if (request.Mode == IslandAccess.Closed) island.AccessClosed = true;
        else { island.AccessClosed = false; island.IsPublic = request.Mode == IslandAccess.Public; }
        IslandAudit.Add(db, islandId, actor, "acesso", "mode_changed", $"Mudou o acesso de {ModeTitle(was)} para {ModeTitle(IslandAccess.ModeOf(island))}", island.Name, new { mode = was }, new { mode = IslandAccess.ModeOf(island) }, clock.GetUtcNow());
        island.UpdatedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct);
        return await GetAsync(islandId, null, 1, context, db, clock, ct);
    }

    public static async Task<IResult> AllowAsync(Guid islandId, AllowRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var (island, refusal) = await AdminOf(islandId, context, db, false, ct); if (island is null) return refusal!;
        Who(context, out var me); var now = clock.GetUtcNow();
        var name = (request.Name ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Digite o nome de quem você quer permitir."] });
        if (request.ExpiresAt is { } when && when <= now) return Results.ValidationProblem(new Dictionary<string, string[]> { ["expiresAt"] = ["A data de expiração precisa ser no futuro."] });
        var target = await db.Users.AsNoTracking().Where(u => u.Username.ToLower() == name || db.Avatars.Any(a => a.UserId == u.Id && a.DisplayName.ToLower() == name)).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (target is null) return Results.NotFound(new { error = "user_not_found", message = "Não encontrei ninguém com esse nome." });
        if (target == island.OwnerUserId) return Results.Conflict(new { error = "owner", message = "O proprietário já pode entrar sempre." });
        if (await Active(db, islandId, now).AnyAsync(p => p.UserId == target, ct)) return Results.Conflict(new { error = "already", message = "Essa pessoa já está na lista." });
        // An entry that expired is still "not revoked" for the unique index, so it is renewed instead of adding a second one.
        var expired = await db.IslandPermissions.SingleOrDefaultAsync(p => p.IslandId == islandId && p.UserId == target && p.Permission == "enter" && p.RevokedAt == null, ct);
        if (expired is not null) { expired.PrincipalType = "user"; expired.GrantedByUserId = me; expired.CreatedAt = now; expired.ExpiresAt = request.ExpiresAt; IslandAudit.Add(db, islandId, me, "acesso", "allow_added", $"Permitiu a entrada de {await IslandAudit.NameAsync(db, target.Value, ct)}", await IslandAudit.NameAsync(db, target.Value, ct), null, new { expiresAt = request.ExpiresAt }, now); await db.SaveChangesAsync(ct); return Results.Ok(await Row(db, expired)); }
        var entry = new IslandPermission { Id = Guid.NewGuid(), IslandId = islandId, UserId = target, Permission = "enter", PrincipalType = "user", GrantedByUserId = me, CreatedAt = now, ExpiresAt = request.ExpiresAt };
        db.IslandPermissions.Add(entry); IslandAudit.Add(db, islandId, me, "acesso", "allow_added", $"Permitiu a entrada de {await IslandAudit.NameAsync(db, target.Value, ct)}", await IslandAudit.NameAsync(db, target.Value, ct), null, new { expiresAt = request.ExpiresAt }, now); await db.SaveChangesAsync(ct);
        return Results.Ok(await Row(db, entry));
    }

    public static async Task<IResult> ChangeAsync(Guid islandId, Guid permissionId, AllowChangeRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var (island, refusal) = await AdminOf(islandId, context, db, false, ct); if (island is null) return refusal!;
        var now = clock.GetUtcNow();
        if (request.ExpiresAt is { } when && when <= now) return Results.ValidationProblem(new Dictionary<string, string[]> { ["expiresAt"] = ["A data de expiração precisa ser no futuro."] });
        var entry = await Active(db, islandId, now).SingleOrDefaultAsync(p => p.Id == permissionId, ct); if (entry is null) return Results.NotFound();
        Who(context, out var actor); var oldExpiry = entry.ExpiresAt; var personName = await IslandAudit.NameAsync(db, entry.UserId!.Value, ct);
        entry.ExpiresAt = request.ExpiresAt; IslandAudit.Add(db, islandId, actor, "acesso", "allow_changed", $"Alterou a expiração da entrada de {personName}", personName, new { expiresAt = oldExpiry }, new { expiresAt = request.ExpiresAt }, now); await db.SaveChangesAsync(ct);
        return Results.Ok(await Row(db, entry));
    }

    public static async Task<IResult> RemoveAsync(Guid islandId, Guid permissionId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var (island, refusal) = await AdminOf(islandId, context, db, false, ct); if (island is null) return refusal!;
        var now = clock.GetUtcNow();
        var entry = await Active(db, islandId, now).SingleOrDefaultAsync(p => p.Id == permissionId, ct); if (entry is null) return Results.NotFound();
        Who(context, out var actor); var personName = await IslandAudit.NameAsync(db, entry.UserId!.Value, ct);
        entry.RevokedAt = now; IslandAudit.Add(db, islandId, actor, "acesso", "allow_removed", $"Tirou {personName} da lista de permitidos", personName, new { expiresAt = entry.ExpiresAt }, null, now); await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
