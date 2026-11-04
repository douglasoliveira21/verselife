using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Shared.Avatar;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Users;

public sealed record LookNameRequest(string Name);
public sealed record LookImportRequest(string Code, string? Name);

/// <summary>What a saved look holds: the whole appearance except the name of the avatar and the camera preferences, which belong to the person.</summary>
public sealed record LookSnapshot(string BaseModel, string SkinColor, string HairColor, string HairStyle, string EyeColor, string BeardStyle,
    string BodySettingsJson, string FaceSettingsJson, string OutfitsJson, string AttachmentsJson, IReadOnlyList<EquippedItemRequest> EquippedItems);

public sealed record LookSummary(Guid Id, string Name, DateTimeOffset UpdatedAt, string? ShareCode, string SkinColor, string HairColor, string HairStyle, IReadOnlyList<EquippedItemRequest> EquippedItems);

public sealed record LookResult(bool Ok, string? Error = null, object? Data = null);

/// <summary>Saved looks: several named appearances per avatar that can be saved, put on, duplicated, shared with a code and imported.</summary>
public static class AvatarLooks
{
    public const int MaxLooks = 20;
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAvatarLookEndpoints(this RouteGroupBuilder group)
    {
        var looks = group.MapGroup("/avatar/looks");
        looks.MapGet("", async (HttpContext c, LifeVerseDbContext db, CancellationToken ct) => User(c) is { } user ? Results.Ok(await ListAsync(db, user, ct)) : Results.Unauthorized());
        looks.MapPost("", async (LookNameRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => await Run(c, user => SaveCurrentAsync(db, user, r.Name, t.GetUtcNow(), ct)));
        looks.MapPost("/import", async (LookImportRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => await Run(c, user => ImportAsync(db, user, r.Code, r.Name, t.GetUtcNow(), ct)));
        looks.MapPost("/{id:guid}/apply", async (Guid id, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => await Run(c, user => ApplyAsync(db, user, id, t.GetUtcNow(), ct)));
        looks.MapPost("/{id:guid}/duplicate", async (Guid id, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => await Run(c, user => DuplicateAsync(db, user, id, t.GetUtcNow(), ct)));
        looks.MapPut("/{id:guid}", async (Guid id, LookNameRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => await Run(c, user => RenameAsync(db, user, id, r.Name, t.GetUtcNow(), ct)));
        looks.MapPost("/{id:guid}/share", async (Guid id, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => await Run(c, user => ShareAsync(db, user, id, true, t.GetUtcNow(), ct)));
        looks.MapDelete("/{id:guid}/share", async (Guid id, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => await Run(c, user => ShareAsync(db, user, id, false, t.GetUtcNow(), ct)));
        looks.MapDelete("/{id:guid}", async (Guid id, HttpContext c, LifeVerseDbContext db, CancellationToken ct) => await Run(c, user => DeleteAsync(db, user, id, ct)));
        return group;
    }

    private static Guid? User(HttpContext context) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;

    private static async Task<IResult> Run(HttpContext context, Func<Guid, Task<LookResult>> operation)
    {
        if (User(context) is not { } user) return Results.Unauthorized();
        var result = await operation(user);
        return result.Ok ? Results.Ok(result.Data ?? new { }) : result.Error switch
        {
            "not_found" => Results.NotFound(new { error = result.Error }),
            "bad_name" or "bad_code" or "look_invalid" => Results.BadRequest(new { error = result.Error }),
            _ => Results.Conflict(new { error = result.Error })
        };
    }

    private static async Task<Avatar?> AvatarOfAsync(LifeVerseDbContext db, Guid user, CancellationToken ct) =>
        await db.Avatars.Include(a => a.Appearance).Include(a => a.EquippedItems).SingleOrDefaultAsync(a => a.UserId == user, ct);

    private static string? CleanName(string? name)
    {
        var text = (name ?? string.Empty).Trim();
        return text.Length is < 1 or > 40 || text.Any(char.IsControl) ? null : text;
    }

    /// <summary>The current appearance of an avatar as a snapshot.</summary>
    public static LookSnapshot Snapshot(Avatar avatar)
    {
        var response = UserEndpoints.ToResponse(avatar);
        return new LookSnapshot(response.BaseModel, response.SkinColor, response.HairColor, response.HairStyle, response.EyeColor, response.BeardStyle,
            response.BodySettingsJson, response.FaceSettingsJson, response.OutfitsJson, response.AttachmentsJson, response.EquippedItems);
    }

    /// <summary>The request that would save a snapshot over an avatar, so the same validation as a normal save applies.</summary>
    public static SaveAvatarRequest ToRequest(Avatar avatar, LookSnapshot look) => new(avatar.DisplayName, look.BaseModel, look.SkinColor, look.HairColor, look.HairStyle,
        avatar.Appearance?.CameraSensitivity ?? .003f, avatar.Appearance?.InvertCameraY ?? false, look.EquippedItems, look.EyeColor, look.BeardStyle, look.BodySettingsJson, look.FaceSettingsJson, look.OutfitsJson, look.AttachmentsJson);

    private static LookSnapshot? Read(string json) { try { return JsonSerializer.Deserialize<LookSnapshot>(json, Options); } catch (JsonException) { return null; } }

    private static LookSummary Summary(AvatarLook look)
    {
        var snapshot = Read(look.SnapshotJson);
        return new LookSummary(look.Id, look.Name, look.UpdatedAt, look.ShareCode, snapshot?.SkinColor ?? "#C98F65", snapshot?.HairColor ?? "#2B1B14", snapshot?.HairStyle ?? "short", snapshot?.EquippedItems ?? []);
    }

    public static async Task<IReadOnlyList<LookSummary>> ListAsync(LifeVerseDbContext db, Guid user, CancellationToken ct)
    {
        var avatar = await db.Avatars.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == user, ct); if (avatar is null) return [];
        return [.. (await db.AvatarLooks.AsNoTracking().Where(l => l.AvatarId == avatar.Id).OrderBy(l => l.Name).ToListAsync(ct)).Select(Summary)];
    }

    /// <summary>A name that is free for this avatar: the wanted one, or with " (2)", " (3)" added.</summary>
    private static async Task<string?> FreeNameAsync(LifeVerseDbContext db, Guid avatarId, string wanted, CancellationToken ct)
    {
        var taken = (await db.AvatarLooks.AsNoTracking().Where(l => l.AvatarId == avatarId).Select(l => l.Name).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(wanted)) return wanted;
        for (var n = 2; n < 100; n++)
        {
            var suffix = $" ({n})"; var candidate = (wanted.Length + suffix.Length > 40 ? wanted[..(40 - suffix.Length)] : wanted) + suffix;
            if (!taken.Contains(candidate)) return candidate;
        }
        return null;
    }

    private static async Task<LookResult> SaveAsync(LifeVerseDbContext db, CancellationToken ct, object? data = null)
    {
        try { await db.SaveChangesAsync(ct); return new LookResult(true, null, data); }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException) { foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached; return new LookResult(false, "conflict"); }
    }

    /// <summary>Saves what the avatar looks like now under a name; a name already in use is refused.</summary>
    public static async Task<LookResult> SaveCurrentAsync(LifeVerseDbContext db, Guid user, string? name, DateTimeOffset now, CancellationToken ct)
    {
        var clean = CleanName(name); if (clean is null) return new LookResult(false, "bad_name");
        var avatar = await AvatarOfAsync(db, user, ct); if (avatar is null) return new LookResult(false, "not_found");
        var existing = await db.AvatarLooks.Where(l => l.AvatarId == avatar.Id).ToListAsync(ct);
        if (existing.Any(l => string.Equals(l.Name, clean, StringComparison.OrdinalIgnoreCase))) return new LookResult(false, "name_taken");
        if (existing.Count >= MaxLooks) return new LookResult(false, "too_many_looks");
        var look = new AvatarLook { Id = Guid.NewGuid(), AvatarId = avatar.Id, Name = clean, SnapshotJson = JsonSerializer.Serialize(Snapshot(avatar), Options), CreatedAt = now, UpdatedAt = now };
        db.AvatarLooks.Add(look);
        return await SaveAsync(db, ct, Summary(look));
    }

    /// <summary>Puts a saved look on. The snapshot goes through the same validation as any appearance, so a look that no longer fits the rules is refused instead of applied.</summary>
    public static async Task<LookResult> ApplyAsync(LifeVerseDbContext db, Guid user, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var avatar = await AvatarOfAsync(db, user, ct); if (avatar is null) return new LookResult(false, "not_found");
        var look = await db.AvatarLooks.SingleOrDefaultAsync(l => l.Id == id && l.AvatarId == avatar.Id, ct); if (look is null) return new LookResult(false, "not_found");
        if (Read(look.SnapshotJson) is not { } snapshot) return new LookResult(false, "look_invalid");
        var request = ToRequest(avatar, snapshot);
        if (AvatarCatalog.Validate(request, await AvatarWearables.ResolverAsync(db, avatar.Id, ct)).Count > 0 || (request.EquippedItems.Any(i => AvatarRules.IsCustom(i.ItemKey)) && (await AvatarWearables.CheckOutfitAsync(db, avatar, request.EquippedItems, ct)).Count > 0)) return new LookResult(false, "look_invalid");
        UserEndpoints.ApplyAvatar(db, avatar, request, now);
        return await SaveAsync(db, ct, UserEndpoints.ToResponse(avatar, await AvatarModels.ActiveAsync(db, avatar, ct), await AvatarWearables.WornInfoAsync(db, avatar, ct), await AvatarAnimations.FilesInfoAsync(db, avatar, ct)));
    }

    public static async Task<LookResult> DuplicateAsync(LifeVerseDbContext db, Guid user, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var avatar = await AvatarOfAsync(db, user, ct); if (avatar is null) return new LookResult(false, "not_found");
        var look = await db.AvatarLooks.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id && l.AvatarId == avatar.Id, ct); if (look is null) return new LookResult(false, "not_found");
        if (await db.AvatarLooks.CountAsync(l => l.AvatarId == avatar.Id, ct) >= MaxLooks) return new LookResult(false, "too_many_looks");
        var name = await FreeNameAsync(db, avatar.Id, (look.Name.Length > 33 ? look.Name[..33] : look.Name) + " (cópia)", ct); if (name is null) return new LookResult(false, "name_taken");
        var copy = new AvatarLook { Id = Guid.NewGuid(), AvatarId = avatar.Id, Name = name, SnapshotJson = look.SnapshotJson, CreatedAt = now, UpdatedAt = now };
        db.AvatarLooks.Add(copy);
        return await SaveAsync(db, ct, Summary(copy));
    }

    public static async Task<LookResult> RenameAsync(LifeVerseDbContext db, Guid user, Guid id, string? name, DateTimeOffset now, CancellationToken ct)
    {
        var clean = CleanName(name); if (clean is null) return new LookResult(false, "bad_name");
        var avatar = await AvatarOfAsync(db, user, ct); if (avatar is null) return new LookResult(false, "not_found");
        var look = await db.AvatarLooks.SingleOrDefaultAsync(l => l.Id == id && l.AvatarId == avatar.Id, ct); if (look is null) return new LookResult(false, "not_found");
        if (await db.AvatarLooks.AnyAsync(l => l.AvatarId == avatar.Id && l.Id != id && l.Name.ToLower() == clean.ToLower(), ct)) return new LookResult(false, "name_taken");
        look.Name = clean; look.UpdatedAt = now;
        return await SaveAsync(db, ct, Summary(look));
    }

    public static async Task<LookResult> DeleteAsync(LifeVerseDbContext db, Guid user, Guid id, CancellationToken ct)
    {
        var avatar = await db.Avatars.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == user, ct); if (avatar is null) return new LookResult(false, "not_found");
        var look = await db.AvatarLooks.SingleOrDefaultAsync(l => l.Id == id && l.AvatarId == avatar.Id, ct); if (look is null) return new LookResult(false, "not_found");
        db.AvatarLooks.Remove(look);
        return await SaveAsync(db, ct);
    }

    /// <summary>Turns sharing on (a short code anybody can import) or off (the code stops working).</summary>
    public static async Task<LookResult> ShareAsync(LifeVerseDbContext db, Guid user, Guid id, bool on, DateTimeOffset now, CancellationToken ct)
    {
        var avatar = await db.Avatars.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == user, ct); if (avatar is null) return new LookResult(false, "not_found");
        var look = await db.AvatarLooks.SingleOrDefaultAsync(l => l.Id == id && l.AvatarId == avatar.Id, ct); if (look is null) return new LookResult(false, "not_found");
        if (on && look.ShareCode is null)
        {
            string code; do { code = NewCode(); } while (await db.AvatarLooks.AnyAsync(l => l.ShareCode == code, ct));
            look.ShareCode = code;
        }
        else if (!on) look.ShareCode = null;
        look.UpdatedAt = now;
        return await SaveAsync(db, ct, Summary(look));
    }

    private static string NewCode() => new([.. Enumerable.Range(0, 8).Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)])]);

    /// <summary>Copies a shared look into the looks of this avatar. The copy is validated like any look, so nothing the rules forbid can come in through a code.</summary>
    public static async Task<LookResult> ImportAsync(LifeVerseDbContext db, Guid user, string? code, string? name, DateTimeOffset now, CancellationToken ct)
    {
        var clean = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (clean.Length != 8 || clean.Any(c => !CodeAlphabet.Contains(c))) return new LookResult(false, "bad_code");
        var avatar = await AvatarOfAsync(db, user, ct); if (avatar is null) return new LookResult(false, "not_found");
        var source = await db.AvatarLooks.AsNoTracking().SingleOrDefaultAsync(l => l.ShareCode == clean, ct); if (source is null) return new LookResult(false, "not_found");
        if (Read(source.SnapshotJson) is not { } snapshot || AvatarCatalog.Validate(ToRequest(avatar, snapshot)).Count > 0) return new LookResult(false, "look_invalid");
        if (await db.AvatarLooks.CountAsync(l => l.AvatarId == avatar.Id, ct) >= MaxLooks) return new LookResult(false, "too_many_looks");
        var wanted = CleanName(name) ?? source.Name;
        var free = await FreeNameAsync(db, avatar.Id, wanted, ct); if (free is null) return new LookResult(false, "name_taken");
        var copy = new AvatarLook { Id = Guid.NewGuid(), AvatarId = avatar.Id, Name = free, SnapshotJson = source.SnapshotJson, CreatedAt = now, UpdatedAt = now };
        db.AvatarLooks.Add(copy);
        return await SaveAsync(db, ct, Summary(copy));
    }
}
