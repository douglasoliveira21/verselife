using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public sealed record IslandGeneralRequest(string Name, string? Description, string? CoverImageUrl, string Category, string ContentRating, string Language, int MaxPlayers, bool ShowInSearch, bool ShowOnMap,
    float SpawnX, float SpawnZ, DateTimeOffset ExpectedUpdatedAt);

/// <summary>
/// What the island management window shows and saves on its General tab. Everything is decided here: who may read it (the owner and people with the admin permission), what is valid, and
/// whether somebody else saved in the meantime (a stale save is refused with 409 instead of overwriting the newer settings).
/// </summary>
public static class IslandManagement
{
    public static readonly string[] Categories = ["social", "nature", "sandbox", "games", "shopping", "music", "art", "education", "roleplay", "other"];
    public static readonly string[] Ratings = ["livre", "moderado", "adulto"];
    public static readonly string[] Languages = ["pt-BR", "en-US", "es-ES", "fr-FR", "de-DE", "it-IT", "ja-JP", "other"];
    public const int NameLimit = 48, DescriptionLimit = 500;

    public static IEndpointRouteBuilder MapIslandManagement(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/worlds/islands/{islandId:guid}/management", GetAsync).WithTags("Worlds").RequireAuthorization();
        endpoints.MapPut("/api/v1/worlds/islands/{islandId:guid}/management/general", SaveGeneralAsync).WithTags("Worlds").RequireAuthorization();
        return endpoints;
    }

    private static bool Who(HttpContext c, out Guid id) => Guid.TryParse(c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);

    private static string Label(string key) => key switch
    {
        "name" => "nome", "description" => "descrição", "coverImageUrl" => "imagem", "category" => "categoria", "contentRating" => "classificação", "language" => "idioma", "maxPlayers" => "capacidade",
        "showInSearch" => "aparecer no buscador", "showOnMap" => "aparecer no mapa", "spawnX" or "spawnZ" => "ponto de chegada", _ => key,
    };

    private static async Task<bool> IsAdminAsync(LifeVerseDbContext db, Island island, Guid user, CancellationToken ct) =>
        island.OwnerUserId == user || await db.IslandPermissions.AnyAsync(p => p.IslandId == island.Id && p.UserId == user && p.Permission == "admin" && p.RevokedAt == null, ct);

    public static async Task<IResult> GetAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Who(context, out var user)) return Results.Unauthorized();
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return Results.NotFound();
        var admin = await IsAdminAsync(db, island, user, ct);
        if (!admin && !island.IsPublic) return Results.Forbid();
        var owner = await db.Users.AsNoTracking().Where(u => u.Id == island.OwnerUserId).Select(u => u.Username).FirstOrDefaultAsync(ct) ?? "";
        var ownerName = await db.Avatars.AsNoTracking().Where(a => a.UserId == island.OwnerUserId).Select(a => a.DisplayName).FirstOrDefaultAsync(ct) ?? owner;
        var admins = await db.IslandPermissions.AsNoTracking().Where(p => p.IslandId == islandId && p.Permission == "admin" && p.RevokedAt == null && p.UserId != null && p.UserId != island.OwnerUserId).Select(p => p.UserId).Distinct().CountAsync(ct) + 1;
        var live = db.BuildObjects.AsNoTracking().Where(o => o.IslandId == islandId && o.DeletedAt == null);
        var objects = await live.CountAsync(ct); var cost = await live.SumAsync(o => (int?)o.Cost, ct) ?? 0;
        var now = clock.GetUtcNow();
        var instance = await db.ServerInstances.AsNoTracking().Where(i => i.IslandId == islandId && i.StoppedAt == null && i.LastHeartbeatAt > now.AddSeconds(-60)).OrderByDescending(i => i.StartedAt).FirstOrDefaultAsync(ct);
        var state = instance is null ? (island.RuntimeState is "starting" or "restarting" ? island.RuntimeState : "offline") : instance.Status == "running" ? "online" : instance.Status;
        var maxCost = BuildCost.Limit(island.ResourceLimitsJson, "cost", BuildCost.DefaultIslandBudget);
        return Results.Ok(new
        {
            id = island.Id, name = island.Name, description = island.Description, coverImageUrl = island.CoverImageUrl, category = island.Category, contentRating = island.ContentRating, language = island.Language,
            isPublic = island.IsPublic, accessMode = IslandAccess.ModeOf(island), showInSearch = island.ShowInSearch, showOnMap = island.ShowOnMap, maxPlayers = island.MaxPlayers, size = island.Size,
            spawn = new { x = island.SpawnX, z = island.SpawnZ },
            ownerUserId = island.OwnerUserId, ownerName, adminCount = admins, createdAt = island.CreatedAt, updatedAt = island.UpdatedAt,
            state, visitors = instance?.PlayerCount ?? 0,
            usage = new { objects, maxObjects = BuildCost.Limit(island.ResourceLimitsJson, "objects", 5000), cost, maxCost },
            canEdit = admin, isOwner = island.OwnerUserId == user,
            options = new { categories = Categories, ratings = Ratings, languages = Languages, nameLimit = NameLimit, descriptionLimit = DescriptionLimit, maxPlayersLimit = 256 },
        });
    }

    public static async Task<IResult> SaveGeneralAsync(Guid islandId, IslandGeneralRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Who(context, out var user)) return Results.Unauthorized();
        var island = await db.Islands.SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return Results.NotFound();
        if (!await IsAdminAsync(db, island, user, ct)) return Results.Forbid();
        if (!await IslandOwnerOnly.AllowsAsync(db, island, user)) return IslandOwnerOnly.Refusal();
        var errors = new Dictionary<string, string[]>();
        var name = (request.Name ?? "").Trim(); var description = (request.Description ?? "").Trim(); var cover = request.CoverImageUrl?.Trim();
        if (name.Length is < 1 or > NameLimit) errors["name"] = [$"O nome deve ter de 1 a {NameLimit} caracteres."];
        if (description.Length > DescriptionLimit) errors["description"] = [$"A descrição pode ter no máximo {DescriptionLimit} caracteres."];
        if (cover is { Length: > 1024 }) errors["coverImageUrl"] = ["O endereço da imagem é longo demais."];
        if (!Categories.Contains(request.Category)) errors["category"] = ["Categoria inválida."];
        if (!Ratings.Contains(request.ContentRating)) errors["contentRating"] = ["Classificação inválida."];
        if (!Languages.Contains(request.Language)) errors["language"] = ["Idioma inválido."];
        if (request.MaxPlayers is < 1 or > 256) errors["maxPlayers"] = ["A capacidade deve ficar entre 1 e 256 visitantes."];
        if (!float.IsFinite(request.SpawnX) || !float.IsFinite(request.SpawnZ) || Math.Abs(request.SpawnX) > island.Size || Math.Abs(request.SpawnZ) > island.Size) errors["spawn"] = ["O ponto de chegada está fora da ilha."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (island.UpdatedAt != request.ExpectedUpdatedAt) return Results.Conflict(new { error = "stale", message = "Outra pessoa salvou a ilha antes de você. Recarregue para ver as mudanças.", updatedAt = island.UpdatedAt });
        var before = new Dictionary<string, object?> { ["name"] = island.Name, ["description"] = island.Description, ["coverImageUrl"] = island.CoverImageUrl, ["category"] = island.Category, ["contentRating"] = island.ContentRating, ["language"] = island.Language, ["maxPlayers"] = island.MaxPlayers, ["showInSearch"] = island.ShowInSearch, ["showOnMap"] = island.ShowOnMap, ["spawnX"] = island.SpawnX, ["spawnZ"] = island.SpawnZ };
        island.Name = name; island.Description = description; island.CoverImageUrl = string.IsNullOrEmpty(cover) ? null : cover; island.Category = request.Category; island.ContentRating = request.ContentRating; island.Language = request.Language;
        island.MaxPlayers = request.MaxPlayers; island.ShowInSearch = request.ShowInSearch; island.ShowOnMap = request.ShowOnMap;
        island.SpawnX = request.SpawnX; island.SpawnZ = request.SpawnZ;
        island.UpdatedAt = clock.GetUtcNow();
        var after = new Dictionary<string, object?> { ["name"] = island.Name, ["description"] = island.Description, ["coverImageUrl"] = island.CoverImageUrl, ["category"] = island.Category, ["contentRating"] = island.ContentRating, ["language"] = island.Language, ["maxPlayers"] = island.MaxPlayers, ["showInSearch"] = island.ShowInSearch, ["showOnMap"] = island.ShowOnMap, ["spawnX"] = island.SpawnX, ["spawnZ"] = island.SpawnZ };
        var changed = after.Keys.Where(k => !Equals(before[k], after[k])).ToList();
        if (changed.Count > 0) IslandAudit.Add(db, islandId, user, "geral", "settings_changed", $"Alterou as configurações gerais ({string.Join(", ", changed.Select(Label).Distinct())})", island.Name, changed.ToDictionary(k => k, k => before[k]), changed.ToDictionary(k => k, k => after[k]), island.UpdatedAt);
        await db.SaveChangesAsync(ct);
        return await GetAsync(islandId, context, db, clock, ct);
    }
}
