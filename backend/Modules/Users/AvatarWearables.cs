using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Shared.Avatar;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Users;

public sealed record WearableSummary(Guid Id, string Name, string Slot, string Layer, string Kind, int Triangles, int SizeBytes, string Key, DateTimeOffset CreatedAt, string[] Covers, bool HidesHair, string Hash, string AttachBone, float[] Offset, float[] Rotation, float Scale);

/// <summary>
/// Clothes and accessories a person brought: a GLB is checked (see <see cref="WearableValidator"/>), stored by hash and listed. It is worn by
/// its key "custom:{id}" in the slot it was made for. The server only lets a person wear their own items, in the right slot, rigged clothing
/// only on a custom body, within the weight budget of an outfit, and with no conflicts with the rest of the outfit.
/// </summary>
public static class AvatarWearables
{
    public const int MaxWearables = 30;
    /// <summary>The most triangles all the custom items of one outfit may add up to.</summary>
    public const int OutfitTriangleBudget = 60_000;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAvatarWearableEndpoints(this RouteGroupBuilder group)
    {
        var items = group.MapGroup("/avatar/wearables");
        items.MapGet("", async (HttpContext c, LifeVerseDbContext db, CancellationToken ct) => User(c) is { } user ? Results.Ok(await ListAsync(db, user, ct)) : Results.Unauthorized());
        items.MapPost("", UploadHttpAsync).DisableAntiforgery();
        items.MapDelete("/{id:guid}", async (Guid id, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => await Run(c, user => DeleteAsync(db, user, id, t.GetUtcNow(), ct)));
        items.MapGet("/{id:guid}/report", async (Guid id, HttpContext c, LifeVerseDbContext db, CancellationToken ct) => User(c) is not { } user ? Results.Unauthorized() : await ReportAsync(db, user, id, ct) is { } json ? Results.Content(json, "application/json") : Results.NotFound());
        return group;
    }

    public static void MapWearableDownload(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/avatars/wearables/{id:guid}/file", async (Guid id, HttpContext c, LifeVerseDbContext db, AssetStorageService storage, CancellationToken ct) => await DownloadAsync(c, id, db, storage, ct)).RequireAuthorization().WithTags("Users");

    private static Guid? User(HttpContext context) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;

    private static async Task<IResult> Run(HttpContext context, Func<Guid, Task<ModelResult>> operation)
    {
        if (User(context) is not { } user) return Results.Unauthorized();
        var result = await operation(user);
        return result.Ok ? Results.Ok(result.Data ?? new { }) : result.Error switch { "not_found" => Results.NotFound(new { error = result.Error }), _ => Results.Conflict(new { error = result.Error }) };
    }

    private static async Task<IResult> UploadHttpAsync(HttpRequest request, HttpContext context, LifeVerseDbContext db, AssetStorageService storage, TimeProvider clock, CancellationToken ct)
    {
        if (User(context) is not { } user) return Results.Unauthorized();
        if (!request.HasFormContentType) return Results.BadRequest(new { error = "bad_file" });
        var form = await request.ReadFormAsync(ct); var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0 || file.Length > WearableValidator.MaxBytes) return Results.BadRequest(new { error = file is null || file.Length == 0 ? "bad_file" : "too_big" });
        using var memory = new MemoryStream(); await using (var input = file.OpenReadStream()) await input.CopyToAsync(memory, ct);
        var result = await UploadAsync(db, storage, user, file.FileName, memory.ToArray(), form["name"].ToString(), clock.GetUtcNow(), ct);
        return result.Ok ? Results.Ok(result.Data) : result.Error switch
        {
            "invalid_wearable" => Results.Json(new { error = result.Error, report = result.Data }, statusCode: 422),
            "bad_file" => Results.BadRequest(new { error = result.Error }),
            "not_found" => Results.NotFound(new { error = result.Error }),
            _ => Results.Conflict(new { error = result.Error })
        };
    }

    private static string CleanName(string? name, string fileName)
    {
        var text = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(fileName) : name;
        text = new string((text ?? string.Empty).Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_').ToArray()).Trim();
        return text.Length == 0 ? "Item" : text.Length > 60 ? text[..60] : text;
    }

    private static WearableSummary Summary(AvatarWearable w)
    {
        string bone = string.Empty; float[] offset = [0, 0, 0], rotation = [0, 0, 0]; var scale = 1f;
        try
        {
            using var report = JsonDocument.Parse(w.ReportJson); var root = report.RootElement;
            if (root.TryGetProperty("attachBone", out var b)) bone = b.GetString() ?? string.Empty;
            if (root.TryGetProperty("offset", out var o) && o.ValueKind == JsonValueKind.Array) offset = [.. o.EnumerateArray().Select(x => x.GetSingle())];
            if (root.TryGetProperty("rotation", out var r) && r.ValueKind == JsonValueKind.Array) rotation = [.. r.EnumerateArray().Select(x => x.GetSingle())];
            if (root.TryGetProperty("scale", out var s) && s.TryGetSingle(out var sv)) scale = sv;
        }
        catch (JsonException) { }
        return new(w.Id, w.Name, w.Slot, w.Layer, w.Kind, w.Triangles, w.SizeBytes, AvatarRules.CustomKey(w.Id), w.CreatedAt, w.Covers(), w.HidesHairFlag(), w.ContentHash, bone, offset, rotation, scale);
    }

    public static async Task<IReadOnlyList<WearableSummary>> ListAsync(LifeVerseDbContext db, Guid user, CancellationToken ct)
    {
        var avatar = await db.Avatars.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == user, ct); if (avatar is null) return [];
        return [.. (await db.AvatarWearables.AsNoTracking().Where(w => w.AvatarId == avatar.Id && w.DeletedAt == null).OrderBy(w => w.Slot).ThenBy(w => w.Name).ToListAsync(ct)).Select(Summary)];
    }

    public static async Task<ModelResult> UploadAsync(LifeVerseDbContext db, AssetStorageService storage, Guid user, string fileName, byte[] bytes, string? name, DateTimeOffset now, CancellationToken ct)
    {
        if (Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() != ".glb") return new ModelResult(false, "bad_file");
        var avatar = await db.Avatars.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == user, ct); if (avatar is null) return new ModelResult(false, "not_found");
        var report = WearableValidator.Validate(bytes);
        if (!report.Ok) return new ModelResult(false, "invalid_wearable", JsonSerializer.Deserialize<JsonElement>(report.ToJson()));
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var existing = await db.AvatarWearables.SingleOrDefaultAsync(w => w.AvatarId == avatar.Id && w.ContentHash == hash, ct);
        if (existing is { DeletedAt: null }) return new ModelResult(true, null, new { item = Summary(existing), report = JsonSerializer.Deserialize<JsonElement>(report.ToJson()) });
        if (await db.AvatarWearables.CountAsync(w => w.AvatarId == avatar.Id && w.DeletedAt == null, ct) >= MaxWearables) return new ModelResult(false, "too_many_items");
        var key = await storage.PutAsync(user, hash, bytes, ct);
        var item = existing ?? new AvatarWearable { Id = Guid.NewGuid(), AvatarId = avatar.Id, Name = "Item", Slot = report.Slot, ContentHash = hash, StorageKey = key, CreatedAt = now };
        item.DeletedAt = null; item.Name = CleanName(name, fileName!); item.Slot = report.Slot; item.Layer = report.Layer; item.Kind = report.Kind.ToString().ToLowerInvariant(); item.StorageKey = key;
        item.SizeBytes = bytes.Length; item.Triangles = report.Triangles; item.ReportJson = report.ToJson();
        if (existing is null) db.AvatarWearables.Add(item);
        try { await db.SaveChangesAsync(ct); }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException) { foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached; return new ModelResult(false, "conflict"); }
        return new ModelResult(true, null, new { item = Summary(item), report = JsonSerializer.Deserialize<JsonElement>(report.ToJson()) });
    }

    /// <summary>Deletes the item and takes it off the avatar if it was worn.</summary>
    public static async Task<ModelResult> DeleteAsync(LifeVerseDbContext db, Guid user, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var avatar = await db.Avatars.Include(a => a.EquippedItems).SingleOrDefaultAsync(a => a.UserId == user, ct); if (avatar is null) return new ModelResult(false, "not_found");
        var item = await db.AvatarWearables.SingleOrDefaultAsync(w => w.Id == id && w.AvatarId == avatar.Id && w.DeletedAt == null, ct); if (item is null) return new ModelResult(false, "not_found");
        item.DeletedAt = now;
        var key = AvatarRules.CustomKey(id);
        foreach (var worn in avatar.EquippedItems.Where(e => e.ItemKey == key).ToList())
        {
            // the slot goes back to a plain choice, and what the item covered is uncovered
            var fallback = worn.Slot is "top" or "bottom" or "shoes" ? AvatarRules.DefaultFor(worn.Slot) : "none";
            if (worn.Slot is "top" or "bottom" or "shoes") { worn.ItemKey = fallback; worn.EquippedAt = now; } else db.EquippedItems.Remove(worn);
            foreach (var covered in item.Covers()) if (avatar.EquippedItems.FirstOrDefault(e => e.Slot == covered && e.ItemKey == "covered") is { } cover) cover.ItemKey = AvatarRules.DefaultFor(covered);
        }
        avatar.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return new ModelResult(true);
    }

    public static async Task<string?> ReportAsync(LifeVerseDbContext db, Guid user, Guid id, CancellationToken ct)
    {
        var avatar = await db.Avatars.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == user, ct); if (avatar is null) return null;
        return await db.AvatarWearables.AsNoTracking().Where(w => w.Id == id && w.AvatarId == avatar.Id && w.DeletedAt == null).Select(w => w.ReportJson).SingleOrDefaultAsync(ct);
    }

    private static WearableItem ToItem(AvatarWearable w) { var covers = w.Covers(); return new WearableItem(w.Slot, AvatarRules.CustomKey(w.Id), w.Name, w.Layer, covers.Length == 0 ? null : covers, w.HidesHairFlag()); }

    /// <summary>What the rules need to know about the custom items of an avatar: look one up by slot and key.</summary>
    public static async Task<Func<string, string, WearableItem?>> ResolverAsync(LifeVerseDbContext db, Guid avatarId, CancellationToken ct)
    {
        var items = (await db.AvatarWearables.AsNoTracking().Where(w => w.AvatarId == avatarId && w.DeletedAt == null).ToListAsync(ct)).ToDictionary(w => AvatarRules.CustomKey(w.Id), ToItem);
        return (slot, key) => items.TryGetValue(key, out var item) && item.Slot == slot ? item : null;
    }

    /// <summary>
    /// Everything about wearing the custom items of a request that the catalog check cannot know: that they exist and are the person's own (and in the
    /// slot they were made for) is covered by the resolver; here rigged clothing needs a custom body, an outfit stays under the weight budget and
    /// the whole set has no conflicts. Returns the messages (empty when fine).
    /// </summary>
    public static async Task<List<string>> CheckOutfitAsync(LifeVerseDbContext db, Avatar avatar, IEnumerable<EquippedItemRequest> worn, CancellationToken ct)
    {
        var errors = new List<string>();
        var list = worn.ToList(); var customKeys = list.Where(w => AvatarRules.IsCustom(w.ItemKey)).Select(w => w.ItemKey).ToList();
        var resolver = await ResolverAsync(db, avatar.Id, ct);
        var problems = AvatarRules.CheckEquipment([.. list.Select(w => (w.Slot ?? string.Empty, w.ItemKey ?? string.Empty))], resolver);
        errors.AddRange(problems.Select(Describe));
        if (customKeys.Count == 0 || errors.Count > 0) return errors;
        var ids = customKeys.Select(k => Guid.Parse(k.AsSpan(AvatarRules.CustomPrefix.Length))).ToList();
        var rows = await db.AvatarWearables.AsNoTracking().Where(w => ids.Contains(w.Id) && w.AvatarId == avatar.Id && w.DeletedAt == null).ToListAsync(ct);
        if (rows.Any(r => r.Kind == "clothing") && avatar.CustomModelId is null) errors.Add("Rigged clothing can only be worn on a custom body. Choose a custom body first, or use rigid accessories.");
        if (rows.Sum(r => (long)r.Triangles) > OutfitTriangleBudget) errors.Add($"The custom items of the outfit add up to more than {OutfitTriangleBudget:N0} triangles.");
        return errors;
    }

    private static string Describe(EquipmentProblem problem) => problem.Code switch
    {
        "unknown_item" => $"The item {problem.Detail} in {problem.Slot} is unknown, is not yours, or is for another slot.",
        "duplicate_slot" => "Each equipment slot may appear only once.",
        "missing_required" => "Top, bottom, and shoes slots are required.",
        "slot_covered" => $"The {problem.Slot} slot is covered by {problem.Detail}; remove one of them.",
        "not_covered" => $"The {problem.Slot} slot is marked covered but nothing covers it.",
        "nothing_under" => $"{problem.Detail} is an outer layer and needs a top under it.",
        _ => "Equipment is not valid."
    };

    /// <summary>The custom items worn by an avatar with what a client needs to draw them: the file (by hash), the kind, the layer, the bone and the placement.</summary>
    public static async Task<string> WornInfoAsync(LifeVerseDbContext db, Avatar avatar, CancellationToken ct)
    {
        var keys = avatar.EquippedItems.Where(e => AvatarRules.IsCustom(e.ItemKey)).Select(e => e.ItemKey).ToList();
        if (keys.Count == 0) return "[]";
        var ids = keys.Select(k => Guid.Parse(k.AsSpan(AvatarRules.CustomPrefix.Length))).ToList();
        var rows = await db.AvatarWearables.AsNoTracking().Where(w => ids.Contains(w.Id) && w.AvatarId == avatar.Id && w.DeletedAt == null).ToListAsync(ct);
        return JsonSerializer.Serialize(rows.Select(r =>
        {
            using var report = JsonDocument.Parse(r.ReportJson); var root = report.RootElement;
            return new { id = r.Id, key = AvatarRules.CustomKey(r.Id), hash = r.ContentHash, slot = r.Slot, layer = r.Layer, kind = r.Kind, hidesHair = r.HidesHairFlag(), covers = r.Covers(), attachBone = root.TryGetProperty("attachBone", out var b) ? b.GetString() : string.Empty, offset = root.TryGetProperty("offset", out var o) ? o.Clone() : default, rotation = root.TryGetProperty("rotation", out var ro) ? ro.Clone() : default, scale = root.TryGetProperty("scale", out var s) && s.TryGetSingle(out var sv) ? sv : 1f };
        }), Options);
    }

    private static async Task<IResult> DownloadAsync(HttpContext context, Guid id, LifeVerseDbContext db, AssetStorageService storage, CancellationToken ct)
    {
        if (User(context) is null) return Results.Unauthorized();
        var item = await db.AvatarWearables.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id && w.DeletedAt == null, ct); if (item is null) return Results.NotFound();
        var stream = await storage.OpenReadAsync(item.StorageKey, ct); if (stream is null) return Results.NotFound();
        context.Response.Headers["X-Content-Hash"] = item.ContentHash; context.Response.Headers["Cache-Control"] = "private, max-age=86400";
        return Results.File(stream, "model/gltf-binary");
    }
}

public static class AvatarWearableExtensions
{
    public static string[] Covers(this AvatarWearable w) { try { using var d = JsonDocument.Parse(w.ReportJson); return d.RootElement.TryGetProperty("covers", out var c) && c.ValueKind == JsonValueKind.Array ? [.. c.EnumerateArray().Select(x => x.GetString() ?? string.Empty)] : []; } catch (JsonException) { return []; } }
    public static bool HidesHairFlag(this AvatarWearable w) { try { using var d = JsonDocument.Parse(w.ReportJson); return d.RootElement.TryGetProperty("hidesHair", out var h) && h.ValueKind == JsonValueKind.True; } catch (JsonException) { return false; } }
}
