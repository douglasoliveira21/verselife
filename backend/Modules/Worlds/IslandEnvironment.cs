using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Land;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public sealed record LookRequest(JsonElement Look, DateTimeOffset ExpectedUpdatedAt);
public sealed record PresetRequest(string Name, JsonElement Look);

/// <summary>
/// The Ambiente tab: the look of the whole island (hour of the day, weather, fog, light, sky colour) that the game applies wherever a parcel does not set its own. The game asks for it when a person
/// enters and again every half minute, so a change reaches the people already there within about 30 seconds. The owner can save looks by name and reuse them.
/// </summary>
public static class IslandEnvironmentManagement
{
    public const int MaxPresets = 8, NameLimit = 40;

    public sealed record Preset(string Id, string Title, string Look);

    /// <summary>Looks every island has. The last one is "no look at all": the game's own sky.</summary>
    public static readonly Preset[] BuiltIn =
    [
        new("day", "Dia claro", "{\"hour\":12,\"weather\":\"clear\",\"light\":1}"),
        new("sunset", "Pôr do sol", "{\"hour\":18.5,\"weather\":\"clear\",\"light\":.85,\"sky\":\"#FF9A5A\"}"),
        new("night", "Noite", "{\"hour\":0,\"weather\":\"clear\",\"light\":.4}"),
        new("fog", "Neblina", "{\"hour\":7,\"weather\":\"fog\",\"fog\":.6,\"light\":.8}"),
        new("storm", "Tempestade", "{\"hour\":15,\"weather\":\"storm\",\"fog\":.3,\"light\":.55}"),
        new("default", "Padrão do jogo", "{}"),
    ];

    public static IEndpointRouteBuilder MapIslandEnvironmentManagement(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/worlds/islands/{islandId:guid}/environment", ReadAsync).WithTags("Worlds").RequireAuthorization();
        var group = endpoints.MapGroup("/api/v1/worlds/islands/{islandId:guid}/management/environment").WithTags("Worlds").RequireAuthorization();
        group.MapGet("", GetAsync); group.MapPut("", SaveAsync); group.MapPost("/presets", SavePresetAsync); group.MapDelete("/presets/{name}", DeletePresetAsync);
        return endpoints;
    }

    private static bool Who(HttpContext c, out Guid id) => Guid.TryParse(c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);

    private static async Task<(Island? Island, Guid User, IResult? Refusal)> AdminOf(Guid islandId, HttpContext context, LifeVerseDbContext db, bool track, DateTimeOffset now, CancellationToken ct)
    {
        if (!Who(context, out var user)) return (null, default, Results.Unauthorized());
        var island = await (track ? db.Islands : db.Islands.AsNoTracking()).SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return (null, user, Results.NotFound());
        var admin = island.OwnerUserId == user || await db.IslandPermissions.AnyAsync(p => p.IslandId == islandId && p.UserId == user && p.Permission == "admin" && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > now), ct);
        return admin ? (island, user, null) : (null, user, Results.Forbid());
    }

    /// <summary>The look as clean JSON, or an error message about the first field that is wrong. A field that is not there is simply not set.</summary>
    public static (string? Json, string? Error) Check(JsonElement look)
    {
        if (look.ValueKind != JsonValueKind.Object) return (null, "O ambiente precisa ser um conjunto de ajustes.");
        foreach (var property in look.EnumerateObject())
        {
            switch (property.Name)
            {
                case "hour": if (!Num(property.Value, 0, 24)) return (null, "O horário deve ficar entre 0 e 24."); break;
                case "fog": if (!Num(property.Value, 0, 1)) return (null, "A neblina deve ficar entre 0 e 1."); break;
                case "light": if (!Num(property.Value, .2, 1.6)) return (null, "A luz deve ficar entre 0,2 e 1,6."); break;
                case "weather": if (property.Value.ValueKind != JsonValueKind.String || !LandPresentation.Weathers.Contains(property.Value.GetString())) return (null, "Clima desconhecido."); break;
                case "sky": if (property.Value.ValueKind != JsonValueKind.String || property.Value.GetString() is not { Length: 7 } c || c[0] != '#' || !c.Skip(1).All(Uri.IsHexDigit)) return (null, "A cor do céu deve ser como #FF9A5A."); break;
                default: return (null, $"Ajuste desconhecido: {property.Name}.");
            }
        }
        return (LandPresentation.NormalizeEnvironment(look.GetRawText()), null);
    }

    private static bool Num(JsonElement value, double min, double max) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d) && double.IsFinite(d) && d >= min && d <= max;

    private static JsonElement Parse(string json) { try { return JsonDocument.Parse(json).RootElement.Clone(); } catch (JsonException) { return JsonDocument.Parse("{}").RootElement.Clone(); } }

    private static List<(string Name, string Look)> Presets(Island island)
    {
        try { return [.. (JsonNode.Parse(island.LookPresetsJson) as JsonArray ?? []).Select(n => (n?["name"]?.GetValue<string>() ?? "", n?["look"]?.ToJsonString() ?? "{}")).Where(p => p.Item1.Length > 0)]; } catch (Exception e) when (e is JsonException or InvalidOperationException) { return []; }
    }

    private static void SetPresets(Island island, List<(string Name, string Look)> list) =>
        island.LookPresetsJson = new JsonArray([.. list.Select(p => (JsonNode)new JsonObject { ["name"] = p.Name, ["look"] = JsonNode.Parse(p.Look) })]).ToJsonString();

    /// <summary>What the game applies: anybody who may enter the island can ask.</summary>
    public static async Task<IResult> ReadAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Who(context, out var user)) return Results.Unauthorized();
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return Results.NotFound();
        if (!await IslandAccess.AllowsAsync(db, island, user, clock.GetUtcNow(), ct)) return Results.Forbid();
        return Results.Ok(new { look = Parse(island.LookJson), media = Parse(island.MediaJson), updatedAt = island.UpdatedAt });
    }

    public static async Task<IResult> GetAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var (island, _, refusal) = await AdminOf(islandId, context, db, false, clock.GetUtcNow(), ct); if (island is null) return refusal!;
        return Results.Ok(new
        {
            look = Parse(island.LookJson), updatedAt = island.UpdatedAt,
            builtIn = BuiltIn.Select(p => new { id = p.Id, title = p.Title, look = Parse(p.Look) }), custom = Presets(island).Select(p => new { name = p.Name, look = Parse(p.Look) }),
            limits = new { hourMin = 0, hourMax = 24, fogMin = 0, fogMax = 1, lightMin = .2, lightMax = 1.6, maxPresets = MaxPresets, nameLimit = NameLimit, weathers = LandPresentation.Weathers },
        });
    }

    public static async Task<IResult> SaveAsync(Guid islandId, LookRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, true, now, ct); if (island is null) return refusal!; if (island.OwnerUserId != me) return IslandOwnerOnly.Refusal();
        var (json, error) = Check(request.Look);
        if (json is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["look"] = [error ?? "Ambiente inválido."] });
        if (island.UpdatedAt != request.ExpectedUpdatedAt) return Results.Conflict(new { error = "stale", message = "Outra pessoa alterou a ilha antes de você. Recarregue para ver as mudanças.", updatedAt = island.UpdatedAt });
        var before = island.LookJson; island.LookJson = json; island.UpdatedAt = now;
        IslandAudit.Add(db, islandId, me, "ambiente", "look_changed", json == "{}" ? "Voltou o ambiente da ilha ao padrão do jogo" : "Mudou o ambiente da ilha", island.Name, Parse(before), Parse(json), now);
        await db.SaveChangesAsync(ct);
        return await GetAsync(islandId, context, db, clock, ct);
    }

    public static async Task<IResult> SavePresetAsync(Guid islandId, PresetRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, true, now, ct); if (island is null) return refusal!; if (island.OwnerUserId != me) return IslandOwnerOnly.Refusal();
        var name = (request.Name ?? "").Trim();
        if (name.Length is 0 or > NameLimit) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [$"O nome deve ter de 1 a {NameLimit} caracteres."] });
        if (BuiltIn.Any(p => string.Equals(p.Title, name, StringComparison.OrdinalIgnoreCase))) return Results.Conflict(new { error = "reserved", message = "Esse nome já é de uma predefinição do jogo." });
        var (json, error) = Check(request.Look);
        if (json is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["look"] = [error ?? "Ambiente inválido."] });
        var list = Presets(island); var existing = list.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing < 0 && list.Count >= MaxPresets) return Results.Conflict(new { error = "full", message = $"Você já tem {MaxPresets} predefinições. Exclua uma para salvar outra." });
        if (existing >= 0) list[existing] = (name, json); else list.Add((name, json));
        SetPresets(island, list); IslandAudit.Add(db, islandId, me, "ambiente", "preset_saved", $"Salvou a predefinição de ambiente {name}", name, null, Parse(json), now);
        await db.SaveChangesAsync(ct);
        return await GetAsync(islandId, context, db, clock, ct);
    }

    public static async Task<IResult> DeletePresetAsync(Guid islandId, string name, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, true, now, ct); if (island is null) return refusal!; if (island.OwnerUserId != me) return IslandOwnerOnly.Refusal();
        var list = Presets(island); var index = list.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); if (index < 0) return Results.NotFound();
        var removed = list[index]; list.RemoveAt(index); SetPresets(island, list);
        IslandAudit.Add(db, islandId, me, "ambiente", "preset_deleted", $"Excluiu a predefinição de ambiente {removed.Name}", removed.Name, Parse(removed.Look), null, now);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
