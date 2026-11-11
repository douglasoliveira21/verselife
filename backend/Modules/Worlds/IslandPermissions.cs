using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public sealed record PermissionSetRequest(string Name, string[] Permissions);
public sealed record PermissionChangeRequest(string[] Permissions);

/// <summary>
/// The Permissões tab: what each person may do on the island besides coming in. Only the permissions the server really checks are offered: build (create and edit objects), run_scripts (edit
/// scripts), edit_terrain, ban and admin. The roles are ready-made sets of them, not a separate thing stored on the island. Only the owner can give or take away "admin", and nobody can change
/// their own permissions or the owner's, so nobody locks themselves out or climbs above where they are.
/// </summary>
public static class IslandPermissionManagement
{
    public const int PageSize = 10;

    public sealed record Definition(string Key, string Title, string Text);
    public sealed record Role(string Id, string Title, string[] Permissions);

    public static readonly Definition[] Definitions =
    [
        new("build", "Construir", "Criar, editar e mover objetos na ilha."),
        new("run_scripts", "Scripts", "Escrever e rodar scripts nos objetos."),
        new("edit_terrain", "Terreno", "Modificar o relevo e a água da ilha."),
        new("ban", "Banir", "Banir e desbanir pessoas."),
        new("admin", "Administrar", "Tudo isto e mais: acesso, permissões e configurações da ilha."),
    ];

    public static readonly Role[] Roles =
    [
        new("owner", "Proprietário", [.. Definitions.Select(d => d.Key)]),
        new("admin", "Administrador", ["admin"]),
        new("moderator", "Moderador", ["ban"]),
        new("builder", "Construtor", ["build", "run_scripts"]),
        new("visitor", "Visitante", []),
    ];

    private static readonly string[] Keys = [.. Definitions.Select(d => d.Key)];

    public static IEndpointRouteBuilder MapIslandPermissionManagement(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/worlds/islands/{islandId:guid}/management/permissions").WithTags("Worlds").RequireAuthorization();
        group.MapGet("", ListAsync); group.MapPost("", AddAsync); group.MapPut("/{userId:guid}", ChangeAsync); group.MapDelete("/{userId:guid}", ClearAsync);
        return endpoints;
    }

    private static bool Who(HttpContext c, out Guid id) => Guid.TryParse(c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);

    private static async Task<(Island? Island, Guid User, IResult? Refusal)> AdminOf(Guid islandId, HttpContext context, LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        if (!Who(context, out var user)) return (null, default, Results.Unauthorized());
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return (null, user, Results.NotFound());
        var admin = island.OwnerUserId == user || await db.IslandPermissions.AnyAsync(p => p.IslandId == islandId && p.UserId == user && p.Permission == "admin" && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > now), ct);
        return admin ? (island, user, null) : (null, user, Results.Forbid());
    }

    /// <summary>"edit" is the old name of what "build" means now; both count as build.</summary>
    private static string Canonical(string permission) => permission == "edit" ? "build" : permission;

    private static IQueryable<IslandPermission> Held(LifeVerseDbContext db, Guid islandId, DateTimeOffset now) =>
        db.IslandPermissions.Where(p => p.IslandId == islandId && p.UserId != null && p.PrincipalType == "user" && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > now)
            && (p.Permission == "build" || p.Permission == "edit" || p.Permission == "run_scripts" || p.Permission == "edit_terrain" || p.Permission == "ban" || p.Permission == "admin"));

    public static string RoleOf(IEnumerable<string> permissions)
    {
        var set = permissions.Select(Canonical).Distinct().OrderBy(x => x).ToArray();
        if (set.Length == 0) return "visitor";
        if (set.Contains("admin")) return "admin";
        foreach (var role in Roles.Where(r => r.Id is "moderator" or "builder")) if (set.SequenceEqual(role.Permissions.OrderBy(x => x))) return role.Id;
        return "custom";
    }

    private static async Task<object> Row(LifeVerseDbContext db, Guid user, IReadOnlyCollection<string> permissions)
    {
        var username = await db.Users.AsNoTracking().Where(u => u.Id == user).Select(u => u.Username).FirstOrDefaultAsync() ?? "";
        var name = await db.Avatars.AsNoTracking().Where(a => a.UserId == user).Select(a => a.DisplayName).FirstOrDefaultAsync() ?? username;
        var canonical = permissions.Select(Canonical).Distinct().OrderBy(p => Array.IndexOf(Keys, p)).ToArray();
        return new { userId = user, name, username, permissions = canonical, role = RoleOf(canonical) };
    }

    public static async Task<IResult> ListAsync(Guid islandId, string? q, int? page, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        var held = await Held(db, islandId, now).AsNoTracking().Select(p => new { User = p.UserId!.Value, p.Permission }).ToListAsync(ct);
        var byUser = held.Where(h => h.User != island.OwnerUserId).GroupBy(h => h.User).ToDictionary(g => g.Key, g => g.Select(x => x.Permission).ToArray());
        var roles = Roles.Select(r => new { id = r.Id, title = r.Title, permissions = r.Permissions, people = r.Id == "owner" ? 1 : r.Id == "visitor" ? 0 : byUser.Values.Count(set => RoleOf(set) == r.Id) }).ToList();
        var ids = byUser.Keys.ToList();
        var term = (q ?? "").Trim().ToLowerInvariant();
        if (term.Length > 0)
        {
            var matching = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && (u.Username.ToLower().Contains(term) || db.Avatars.Any(a => a.UserId == u.Id && a.DisplayName.ToLower().Contains(term)))).Select(u => u.Id).ToListAsync(ct);
            ids = matching;
        }
        var ordered = new List<(Guid Id, string Name)>(); foreach (var id in ids) ordered.Add((id, await db.Avatars.AsNoTracking().Where(a => a.UserId == id).Select(a => a.DisplayName).FirstOrDefaultAsync(ct) ?? await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Username).FirstAsync(ct)));
        var total = ordered.Count; var index = Math.Max(1, page ?? 1);
        var items = new List<object>(); foreach (var (id, _) in ordered.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Skip((index - 1) * PageSize).Take(PageSize)) items.Add(await Row(db, id, byUser[id]));
        return Results.Ok(new { definitions = Definitions.Select(d => new { key = d.Key, title = d.Title, text = d.Text }), roles, total, page = index, pageSize = PageSize, items, isOwner = island.OwnerUserId == me });
    }

    /// <summary>Makes the person hold exactly these permissions. Returns an error answer or null when it went through.</summary>
    private static async Task<IResult?> ApplyAsync(LifeVerseDbContext db, Island island, Guid me, Guid target, string[] wanted, DateTimeOffset now, CancellationToken ct)
    {
        if (target == island.OwnerUserId) return Results.Conflict(new { error = "owner", message = "As permissões do proprietário não mudam: ele pode tudo." });
        if (target == me) return Results.Conflict(new { error = "self", message = "Você não pode mudar as suas próprias permissões." });
        var set = wanted.Select(Canonical).Distinct().ToArray();
        if (set.Any(p => !Keys.Contains(p))) return Results.ValidationProblem(new Dictionary<string, string[]> { ["permissions"] = ["Permissão desconhecida."] });
        var rows = await db.IslandPermissions.Where(p => p.IslandId == island.Id && p.UserId == target && p.RevokedAt == null && (p.Permission == "build" || p.Permission == "edit" || p.Permission == "run_scripts" || p.Permission == "edit_terrain" || p.Permission == "ban" || p.Permission == "admin")).ToListAsync(ct);
        var hadAdmin = rows.Any(r => r.Permission == "admin" && (r.ExpiresAt == null || r.ExpiresAt > now));
        if ((hadAdmin || set.Contains("admin")) && island.OwnerUserId != me && hadAdmin != set.Contains("admin")) return Results.Conflict(new { error = "owner_only", message = "Só o proprietário pode dar ou tirar a permissão de administrador." });
        if (hadAdmin && island.OwnerUserId != me) return Results.Conflict(new { error = "owner_only", message = "Só o proprietário pode mudar as permissões de um administrador." });
        var beforeSet = rows.Where(r => r.ExpiresAt == null || r.ExpiresAt > now).Select(r => Canonical(r.Permission)).Distinct().OrderBy(p => Array.IndexOf(Keys, p)).ToArray();
        foreach (var row in rows.Where(r => !set.Contains(Canonical(r.Permission)))) row.RevokedAt = now;
        foreach (var key in set)
        {
            var existing = rows.FirstOrDefault(r => r.RevokedAt == null && Canonical(r.Permission) == key);
            if (existing is not null) { existing.ExpiresAt = null; continue; }
            db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = island.Id, UserId = target, Permission = key, PrincipalType = "user", GrantedByUserId = me, CreatedAt = now });
        }
        var afterSet = set.OrderBy(p => Array.IndexOf(Keys, p)).ToArray();
        if (!beforeSet.SequenceEqual(afterSet)) { var person = await IslandAudit.NameAsync(db, target, ct); IslandAudit.Add(db, island.Id, me, "permissao", "permissions_changed", afterSet.Length == 0 ? $"Tirou todas as permissões de {person}" : $"Mudou as permissões de {person}", person, new { permissions = beforeSet }, new { permissions = afterSet }, now); }
        await db.SaveChangesAsync(ct); return null;
    }

    private static async Task<IResult> Show(LifeVerseDbContext db, Guid islandId, Guid user, DateTimeOffset now, CancellationToken ct)
    {
        var left = await Held(db, islandId, now).AsNoTracking().Where(p => p.UserId == user).Select(p => p.Permission).ToListAsync(ct);
        return Results.Ok(await Row(db, user, left));
    }

    public static async Task<IResult> AddAsync(Guid islandId, PermissionSetRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        var name = (request.Name ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Digite o nome da pessoa."] });
        if ((request.Permissions ?? []).Length == 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["permissions"] = ["Escolha ao menos uma permissão."] });
        var target = await db.Users.AsNoTracking().Where(u => u.Username.ToLower() == name || db.Avatars.Any(a => a.UserId == u.Id && a.DisplayName.ToLower() == name)).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (target is null) return Results.NotFound(new { error = "user_not_found", message = "Não encontrei ninguém com esse nome." });
        if (await Held(db, islandId, now).AnyAsync(p => p.UserId == target, ct)) return Results.Conflict(new { error = "already", message = "Essa pessoa já tem permissões. Ajuste na lista." });
        var failure = await ApplyAsync(db, island, me, target.Value, request.Permissions!, now, ct); if (failure is not null) return failure;
        return await Show(db, islandId, target.Value, now, ct);
    }

    public static async Task<IResult> ChangeAsync(Guid islandId, Guid userId, PermissionChangeRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        if (!await Held(db, islandId, now).AnyAsync(p => p.UserId == userId, ct)) return Results.NotFound();
        var failure = await ApplyAsync(db, island, me, userId, request.Permissions ?? [], now, ct); if (failure is not null) return failure;
        return await Show(db, islandId, userId, now, ct);
    }

    public static async Task<IResult> ClearAsync(Guid islandId, Guid userId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        if (!await Held(db, islandId, now).AnyAsync(p => p.UserId == userId, ct)) return Results.NotFound();
        var failure = await ApplyAsync(db, island, me, userId, [], now, ct); if (failure is not null) return failure;
        return Results.NoContent();
    }
}
