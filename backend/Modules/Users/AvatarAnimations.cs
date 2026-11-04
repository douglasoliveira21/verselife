using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Shared.Avatar;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Users;

public sealed record AnimationFileSummary(Guid Id, string Name, int SizeBytes, string Hash, IReadOnlyList<AnimationClipSummary> Clips, DateTimeOffset CreatedAt);
public sealed record AnimationClipSummary(string Name, float Duration, int Bones, bool RootMotion, bool Loopable);
public sealed record OverridesRequest(Dictionary<string, string>? Overrides);

/// <summary>
/// Animations of the avatar. A person uploads GLB files with animations (checked: see <see cref="AnimationValidator"/>), and chooses, for each state
/// (walk, run, idle, dance, sit, gestures…), which animation plays instead of the default: one of the alternatives the game carries (the library)
/// or one of their own files. Only their own files, only existing animations, only real states. Everyone who sees the avatar is told what to play.
/// </summary>
public static class AvatarAnimations
{
    public const int MaxFiles = 20;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAvatarAnimationEndpoints(this RouteGroupBuilder group)
    {
        var animations = group.MapGroup("/avatar/animations");
        animations.MapGet("", async (HttpContext c, LifeVerseDbContext db, CancellationToken ct) => User(c) is { } user ? Results.Ok(await ListAsync(db, user, ct)) : Results.Unauthorized());
        animations.MapPost("", UploadHttpAsync).DisableAntiforgery();
        animations.MapDelete("/{id:guid}", async (Guid id, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => await Run(c, user => DeleteAsync(db, user, id, t.GetUtcNow(), ct)));
        animations.MapGet("/{id:guid}/report", async (Guid id, HttpContext c, LifeVerseDbContext db, CancellationToken ct) => User(c) is not { } user ? Results.Unauthorized() : await ReportAsync(db, user, id, ct) is { } json ? Results.Content(json, "application/json") : Results.NotFound());
        group.MapPut("/avatar/animation-overrides", async (OverridesRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => await Run(c, user => SetOverridesAsync(db, user, r.Overrides ?? [], t.GetUtcNow(), ct)));
        group.MapGet("/avatar/animation-library", () => Results.Ok(new { states = AnimationStates.All.ToDictionary(p => p.Key, p => new { loop = p.Value, builtin = AnimationStates.BuiltinFor(p.Key) }) }));
        return group;
    }

    public static void MapAnimationDownload(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/avatars/animations/{id:guid}/file", async (Guid id, HttpContext c, LifeVerseDbContext db, AssetStorageService storage, CancellationToken ct) => await DownloadAsync(c, id, db, storage, ct)).RequireAuthorization().WithTags("Users");

    private static Guid? User(HttpContext context) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;

    private static async Task<IResult> Run(HttpContext context, Func<Guid, Task<ModelResult>> operation)
    {
        if (User(context) is not { } user) return Results.Unauthorized();
        var result = await operation(user);
        return result.Ok ? Results.Ok(result.Data ?? new { }) : result.Error switch
        {
            "not_found" => Results.NotFound(new { error = result.Error }),
            "bad_state" or "bad_source" or "too_many" => Results.BadRequest(new { error = result.Error, detail = result.Data }),
            _ => Results.Conflict(new { error = result.Error })
        };
    }

    private static async Task<IResult> UploadHttpAsync(HttpRequest request, HttpContext context, LifeVerseDbContext db, AssetStorageService storage, TimeProvider clock, CancellationToken ct)
    {
        if (User(context) is not { } user) return Results.Unauthorized();
        if (!request.HasFormContentType) return Results.BadRequest(new { error = "bad_file" });
        var form = await request.ReadFormAsync(ct); var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0 || file.Length > AnimationValidator.MaxBytes) return Results.BadRequest(new { error = file is null || file.Length == 0 ? "bad_file" : "too_big" });
        using var memory = new MemoryStream(); await using (var input = file.OpenReadStream()) await input.CopyToAsync(memory, ct);
        var result = await UploadAsync(db, storage, user, file.FileName, memory.ToArray(), form["name"].ToString(), clock.GetUtcNow(), ct);
        return result.Ok ? Results.Ok(result.Data) : result.Error switch
        {
            "invalid_animation" => Results.Json(new { error = result.Error, report = result.Data }, statusCode: 422),
            "bad_file" => Results.BadRequest(new { error = result.Error }),
            "not_found" => Results.NotFound(new { error = result.Error }),
            _ => Results.Conflict(new { error = result.Error })
        };
    }

    private static string CleanName(string? name, string fileName)
    {
        var text = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(fileName) : name;
        text = new string((text ?? string.Empty).Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_').ToArray()).Trim();
        return text.Length == 0 ? "Animações" : text.Length > 60 ? text[..60] : text;
    }

    private static IReadOnlyList<AnimationClipSummary> Clips(string reportJson)
    {
        try
        {
            using var report = JsonDocument.Parse(reportJson);
            return report.RootElement.TryGetProperty("clips", out var clips) ? [.. clips.EnumerateArray().Select(c => new AnimationClipSummary(c.GetProperty("name").GetString() ?? string.Empty, c.GetProperty("duration").GetSingle(), c.GetProperty("bones").GetInt32(), c.GetProperty("rootMotion").GetBoolean(), c.GetProperty("loopable").GetBoolean()))] : [];
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { return []; }
    }

    private static AnimationFileSummary Summary(AvatarAnimationFile f) => new(f.Id, f.Name, f.SizeBytes, f.ContentHash, Clips(f.ReportJson), f.CreatedAt);

    public static async Task<IReadOnlyList<AnimationFileSummary>> ListAsync(LifeVerseDbContext db, Guid user, CancellationToken ct)
    {
        var avatar = await db.Avatars.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == user, ct); if (avatar is null) return [];
        return [.. (await db.AvatarAnimationFiles.AsNoTracking().Where(f => f.AvatarId == avatar.Id && f.DeletedAt == null).OrderBy(f => f.Name).ToListAsync(ct)).Select(Summary)];
    }

    public static async Task<ModelResult> UploadAsync(LifeVerseDbContext db, AssetStorageService storage, Guid user, string fileName, byte[] bytes, string? name, DateTimeOffset now, CancellationToken ct)
    {
        if (Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() != ".glb") return new ModelResult(false, "bad_file");
        var avatar = await db.Avatars.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == user, ct); if (avatar is null) return new ModelResult(false, "not_found");
        var report = AnimationValidator.Validate(bytes);
        if (!report.Ok) return new ModelResult(false, "invalid_animation", JsonSerializer.Deserialize<JsonElement>(report.ToJson()));
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var existing = await db.AvatarAnimationFiles.SingleOrDefaultAsync(f => f.AvatarId == avatar.Id && f.ContentHash == hash, ct);
        if (existing is { DeletedAt: null }) return new ModelResult(true, null, new { file = Summary(existing), report = JsonSerializer.Deserialize<JsonElement>(report.ToJson()) });
        if (await db.AvatarAnimationFiles.CountAsync(f => f.AvatarId == avatar.Id && f.DeletedAt == null, ct) >= MaxFiles) return new ModelResult(false, "too_many_files");
        var key = await storage.PutAsync(user, hash, bytes, ct);
        var file = existing ?? new AvatarAnimationFile { Id = Guid.NewGuid(), AvatarId = avatar.Id, Name = "Animações", ContentHash = hash, StorageKey = key, CreatedAt = now };
        file.DeletedAt = null; file.Name = CleanName(name, fileName!); file.StorageKey = key; file.SizeBytes = bytes.Length; file.ReportJson = report.ToJson();
        if (existing is null) db.AvatarAnimationFiles.Add(file);
        try { await db.SaveChangesAsync(ct); }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException) { foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached; return new ModelResult(false, "conflict"); }
        return new ModelResult(true, null, new { file = Summary(file), report = JsonSerializer.Deserialize<JsonElement>(report.ToJson()) });
    }

    /// <summary>Deletes the file and takes away every override that used one of its animations (those states play the default again).</summary>
    public static async Task<ModelResult> DeleteAsync(LifeVerseDbContext db, Guid user, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var avatar = await db.Avatars.Include(a => a.Appearance).SingleOrDefaultAsync(a => a.UserId == user, ct); if (avatar is null) return new ModelResult(false, "not_found");
        var file = await db.AvatarAnimationFiles.SingleOrDefaultAsync(f => f.Id == id && f.AvatarId == avatar.Id && f.DeletedAt == null, ct); if (file is null) return new ModelResult(false, "not_found");
        file.DeletedAt = now;
        if (avatar.Appearance is { } appearance)
        {
            var map = ReadOverrides(appearance.AnimationOverridesJson);
            var kept = map.Where(p => !(AnimationStates.TryParseCustom(p.Value, out var fileId, out _) && fileId == id)).ToDictionary(p => p.Key, p => p.Value);
            if (kept.Count != map.Count) { appearance.AnimationOverridesJson = WriteOverrides(kept); appearance.UpdatedAt = now; avatar.UpdatedAt = now; }
        }
        await db.SaveChangesAsync(ct);
        return new ModelResult(true);
    }

    public static async Task<string?> ReportAsync(LifeVerseDbContext db, Guid user, Guid id, CancellationToken ct)
    {
        var avatar = await db.Avatars.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == user, ct); if (avatar is null) return null;
        return await db.AvatarAnimationFiles.AsNoTracking().Where(f => f.Id == id && f.AvatarId == avatar.Id && f.DeletedAt == null).Select(f => f.ReportJson).SingleOrDefaultAsync(ct);
    }

    public static Dictionary<string, string> ReadOverrides(string? json)
    {
        try { using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty) : []; }
        catch (JsonException) { return []; }
    }

    public static string WriteOverrides(IReadOnlyDictionary<string, string> map) => JsonSerializer.Serialize(map.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value), Options);

    /// <summary>Sets which animation plays for which state (the whole map is replaced; an empty map goes back to all defaults).</summary>
    public static async Task<ModelResult> SetOverridesAsync(LifeVerseDbContext db, Guid user, IReadOnlyDictionary<string, string> wanted, DateTimeOffset now, CancellationToken ct)
    {
        var avatar = await db.Avatars.Include(a => a.Appearance).SingleOrDefaultAsync(a => a.UserId == user, ct); if (avatar is null) return new ModelResult(false, "not_found");
        if (wanted.Count > AnimationStates.MaxOverrides) return new ModelResult(false, "too_many");
        var files = (await db.AvatarAnimationFiles.AsNoTracking().Where(f => f.AvatarId == avatar.Id && f.DeletedAt == null).ToListAsync(ct)).ToDictionary(f => f.Id, f => Clips(f.ReportJson).Select(c => c.Name).ToHashSet());
        var clean = new Dictionary<string, string>();
        foreach (var (state, source) in wanted)
        {
            if (!AnimationStates.All.ContainsKey(state)) return new ModelResult(false, "bad_state", state);
            if (string.IsNullOrWhiteSpace(source) || source == "default") continue; // the default needs no entry
            if (source.StartsWith(AnimationStates.BuiltinPrefix, StringComparison.Ordinal))
            {
                if (!AnimationStates.BuiltinFor(state).Contains(source[AnimationStates.BuiltinPrefix.Length..])) return new ModelResult(false, "bad_source", $"{state}: {source}");
            }
            else if (!AnimationStates.TryParseCustom(source, out var fileId, out var animation) || !files.TryGetValue(fileId, out var names) || !names.Contains(animation)) return new ModelResult(false, "bad_source", $"{state}: {source}");
            clean[state] = source;
        }
        if (avatar.Appearance is null) return new ModelResult(false, "not_found");
        avatar.Appearance.AnimationOverridesJson = WriteOverrides(clean); avatar.Appearance.UpdatedAt = now; avatar.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return new ModelResult(true, null, new { overrides = clean });
    }

    /// <summary>
    /// Keeps the entries of a saved look that still work (a built-in animation of that state, or a clip of a file the avatar still has) and names the
    /// ones that no longer do, so a look can be put on without refusing it for an animation that was deleted.
    /// </summary>
    public static async Task<(Dictionary<string, string> Clean, List<string> Dropped)> FilterOverridesAsync(LifeVerseDbContext db, Guid avatarId, IReadOnlyDictionary<string, string> wanted, CancellationToken ct)
    {
        var files = (await db.AvatarAnimationFiles.AsNoTracking().Where(f => f.AvatarId == avatarId && f.DeletedAt == null).ToListAsync(ct)).ToDictionary(f => f.Id, f => Clips(f.ReportJson).Select(c => c.Name).ToHashSet());
        var clean = new Dictionary<string, string>(); var dropped = new List<string>();
        foreach (var (state, source) in wanted.Take(AnimationStates.MaxOverrides))
        {
            if (!AnimationStates.All.ContainsKey(state) || string.IsNullOrWhiteSpace(source) || source == "default") { if (!AnimationStates.All.ContainsKey(state)) dropped.Add(state); continue; }
            var ok = source.StartsWith(AnimationStates.BuiltinPrefix, StringComparison.Ordinal)
                ? AnimationStates.BuiltinFor(state).Contains(source[AnimationStates.BuiltinPrefix.Length..])
                : AnimationStates.TryParseCustom(source, out var fileId, out var animation) && files.TryGetValue(fileId, out var names) && names.Contains(animation);
            if (ok) clean[state] = source; else dropped.Add(state);
        }
        return (clean, dropped);
    }

    /// <summary>The animation files the overrides of an avatar use (id and hash), so everyone who sees the avatar can download them.</summary>
    public static async Task<string> FilesInfoAsync(LifeVerseDbContext db, Avatar avatar, CancellationToken ct)
    {
        var ids = ReadOverrides(avatar.Appearance?.AnimationOverridesJson).Values.Select(v => AnimationStates.TryParseCustom(v, out var id, out _) ? id : Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return "[]";
        var files = await db.AvatarAnimationFiles.AsNoTracking().Where(f => ids.Contains(f.Id) && f.AvatarId == avatar.Id && f.DeletedAt == null).Select(f => new { id = f.Id, hash = f.ContentHash }).ToListAsync(ct);
        return JsonSerializer.Serialize(files, Options);
    }

    private static async Task<IResult> DownloadAsync(HttpContext context, Guid id, LifeVerseDbContext db, AssetStorageService storage, CancellationToken ct)
    {
        if (User(context) is null) return Results.Unauthorized();
        var file = await db.AvatarAnimationFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id && f.DeletedAt == null, ct); if (file is null) return Results.NotFound();
        var stream = await storage.OpenReadAsync(file.StorageKey, ct); if (stream is null) return Results.NotFound();
        context.Response.Headers["X-Content-Hash"] = file.ContentHash; context.Response.Headers["Cache-Control"] = "private, max-age=86400";
        return Results.File(stream, "model/gltf-binary");
    }
}

/// <summary>What a player looks like, as the one text that is stored with the island server's player and shown to everyone: the same builder serves the admission to an island and the refresh while playing.</summary>
public static class AvatarAppearanceJson
{
    public static async Task<string> BuildAsync(LifeVerseDbContext db, Avatar avatar, CancellationToken ct)
    {
        var customModel = await AvatarModels.ActiveAsync(db, avatar, ct);
        return JsonSerializer.Serialize(new
        {
            customWearablesJson = await AvatarWearables.WornInfoAsync(db, avatar, ct),
            animationOverridesJson = avatar.Appearance?.AnimationOverridesJson ?? "{}",
            animationFilesJson = await AvatarAnimations.FilesInfoAsync(db, avatar, ct),
            customModelId = customModel?.Id, customModelHash = customModel?.ContentHash, customModelTriangles = customModel?.Triangles ?? 0, customModelScale = customModel?.ScaleToStandard ?? 1f, customModelBonesJson = customModel?.BoneMapJson ?? "{}",
            id = avatar.Id, displayName = avatar.DisplayName, baseModel = avatar.BaseModel,
            skinColor = avatar.Appearance?.SkinColor ?? "#C98F65", hairColor = avatar.Appearance?.HairColor ?? "#2B1B14",
            hairStyle = avatar.Appearance?.HairStyle ?? "short", eyeColor = avatar.Appearance?.EyeColor ?? "#4A3326",
            beardStyle = avatar.Appearance?.BeardStyle ?? "none", bodySettingsJson = avatar.Appearance?.BodySettingsJson ?? "{}",
            faceSettingsJson = avatar.Appearance?.FaceSettingsJson ?? "{}", outfitsJson = avatar.Appearance?.OutfitsJson ?? "[]",
            attachmentsJson = avatar.Appearance?.AttachmentsJson ?? "[]", cameraSensitivity = avatar.Appearance?.CameraSensitivity ?? .003f,
            invertCameraY = avatar.Appearance?.InvertCameraY ?? false,
            equippedItems = avatar.EquippedItems.Select(x => new { slot = x.Slot, itemKey = x.ItemKey, tintColor = x.TintColor })
        });
    }
}
