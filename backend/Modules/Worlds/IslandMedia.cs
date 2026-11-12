using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Land;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public sealed record MediaRequest(string? MusicUrl, float Volume, bool Autoplay, string? Name, DateTimeOffset ExpectedUpdatedAt);

/// <summary>
/// The Mídia tab: the music of the whole island, played wherever a parcel does not play its own. It uses the same rules as the music of a parcel, because the player's own computer downloads the file:
/// HTTPS, no login in the address, no machine inside a network, an mp3 or ogg file. There is no video: the game cannot show one, so there is nothing to save.
/// </summary>
public static class IslandMediaManagement
{
    public const int NameLimit = 80;

    public static IEndpointRouteBuilder MapIslandMediaManagement(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/worlds/islands/{islandId:guid}/management/media").WithTags("Worlds").RequireAuthorization();
        group.MapGet("", GetAsync); group.MapPut("", SaveAsync);
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

    private static string Why(string code) => code switch
    {
        "not_https" => "O endereço da música precisa começar com https://.", "bad_host" => "Esse endereço não é permitido (rede interna ou nome inválido).",
        "bad_type" => "Só arquivos .mp3 ou .ogg podem ser usados.", "too_long" => "O endereço é longo demais (até 512 caracteres).", _ => "Não foi possível usar esse endereço.",
    };

    private static object? Music(Island island)
    {
        try
        {
            using var document = JsonDocument.Parse(island.MediaJson); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("musicUrl", out var url) || url.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(url.GetString())) return null;
            return new { url = url.GetString(), volume = root.TryGetProperty("volume", out var v) && v.TryGetDouble(out var volume) ? volume : 1.0, autoplay = !root.TryGetProperty("autoplay", out var a) || a.ValueKind != JsonValueKind.False, name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : "" };
        }
        catch (JsonException) { return null; }
    }

    public static async Task<IResult> GetAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var (island, _, refusal) = await AdminOf(islandId, context, db, false, clock.GetUtcNow(), ct); if (island is null) return refusal!;
        return Results.Ok(new { music = Music(island), updatedAt = island.UpdatedAt, limits = new { nameLimit = NameLimit, urlLimit = 512, formats = new[] { ".mp3", ".ogg" }, maxMegabytes = 12 }, video = new { supported = false } });
    }

    public static async Task<IResult> SaveAsync(Guid islandId, MediaRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, true, now, ct); if (island is null) return refusal!; if (!await IslandOwnerOnly.AllowsAsync(db, island, me, now)) return IslandOwnerOnly.Refusal();
        var url = (request.MusicUrl ?? "").Trim(); string json;
        if (url.Length == 0) json = "{}";
        else
        {
            if (LandPresentation.CheckMediaUrl(url) is { } why) return Results.ValidationProblem(new Dictionary<string, string[]> { ["musicUrl"] = [Why(why)] });
            if (!float.IsFinite(request.Volume) || request.Volume is < 0 or > 1) return Results.ValidationProblem(new Dictionary<string, string[]> { ["volume"] = ["O volume deve ficar entre 0 e 100%."] });
            var name = (request.Name ?? "").Trim();
            if (name.Length > NameLimit) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [$"O nome pode ter no máximo {NameLimit} caracteres."] });
            json = JsonSerializer.Serialize(new { musicUrl = url, volume = MathF.Round(request.Volume, 2), autoplay = request.Autoplay, name });
        }
        if (island.UpdatedAt != request.ExpectedUpdatedAt) return Results.Conflict(new { error = "stale", message = "Outra pessoa alterou a ilha antes de você. Recarregue para ver as mudanças.", updatedAt = island.UpdatedAt });
        var before = island.MediaJson; island.MediaJson = json; island.UpdatedAt = now;
        object? Doc(string text) { try { return JsonDocument.Parse(text).RootElement.Clone(); } catch (JsonException) { return null; } }
        IslandAudit.Add(db, islandId, me, "midia", "music_changed", json == "{}" ? "Tirou a música da ilha" : "Mudou a música da ilha", island.Name, Doc(before), Doc(json), now);
        await db.SaveChangesAsync(ct);
        return await GetAsync(islandId, context, db, clock, ct);
    }
}
