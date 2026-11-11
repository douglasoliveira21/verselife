using System.Text.Json;
using System.Text.Json.Nodes;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Users;

/// <summary>What a person changes about their own profile. Every field is optional: a missing one stays as it is.</summary>
public sealed record ProfileUpdate(
    string? Bio = null, string? FirstLifeBio = null, string? Interests = null,
    Guid? PhotoAssetId = null, bool ClearPhoto = false,
    bool? HideGroups = null, bool? HideMarriage = null, bool? PrivateProfile = null);

public sealed record ProfileFailure(string Code, string Message);

/// <summary>
/// The profile of a person (as Second Life has it, adapted): a picture, "about me", "real life", interests, birthday, the groups they chose to show, the type of account and whether they
/// are married in the game. Everything the person can hide is hidden by the server, never by the client; what only the administration decides (the type of account) cannot be changed here.
/// </summary>
public static class ProfileService
{
    public const int MaxBio = 2000, MaxInterests = 500, MaxGroupsShown = 30;
    public static readonly string[] AccountTypes = ["standard", "donor"];

    public sealed record Privacy(bool ProfilePrivate, bool GroupsHidden, bool MarriageHidden, bool PresencePrivate);

    public static Privacy PrivacyOf(string? json)
    {
        try
        {
            var root = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? [];
            string Text(string key) => root[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;
            return new Privacy(Text("profile") == "private", Text("groups") == "hidden", Text("marriage") == "hidden", Text("presence") == "private");
        }
        catch (JsonException) { return new Privacy(false, false, false, false); }
    }

    private static string WithPrivacy(string? json, ProfileUpdate update)
    {
        JsonObject root; try { root = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? []; } catch (JsonException) { root = []; }
        if (update.HideGroups is { } groups) { if (groups) root["groups"] = "hidden"; else root.Remove("groups"); }
        if (update.HideMarriage is { } marriage) { if (marriage) root["marriage"] = "hidden"; else root.Remove("marriage"); }
        if (update.PrivateProfile is { } hidden) { if (hidden) root["profile"] = "private"; else root.Remove("profile"); }
        return root.ToJsonString();
    }

    /// <summary>The birthday of a character is the day it was created (as the rez day of Second Life): nobody types it, so it cannot be false.</summary>
    public static DateTimeOffset CreatedOf(User user) => user.Avatar?.CreatedAt ?? user.CreatedAt;

    public static string BirthdayText(User user) => CreatedOf(user).ToString("dd/MM");

    // ---- reading -----------------------------------------------------------------------------------------------------------

    /// <summary>The profile of <paramref name="target"/> as <paramref name="viewer"/> may see it (null when the person does not exist).</summary>
    public static async Task<object?> ViewAsync(LifeVerseDbContext db, Guid viewer, Guid target, DateTimeOffset now, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Include(x => x.Avatar).SingleOrDefaultAsync(x => x.Id == target && x.DeletedAt == null, ct);
        if (user is null) return null;
        var self = viewer == target; var privacy = PrivacyOf(user.PrivacyJson); var open = self || !privacy.ProfilePrivate;
        var display = user.Avatar?.DisplayName ?? user.Username;
        var marriage = open && (self || !privacy.MarriageHidden) ? await CurrentMarriageAsync(db, target, ct) : null;
        var groups = open && (self || !privacy.GroupsHidden) ? await GroupsShownAsync(db, viewer, target, ct) : [];
        return new
        {
            id = user.Id, username = user.Username, displayName = display, isSelf = self, privateProfile = !open,
            joinedAt = CreatedOf(user), accountType = user.AccountType, donorSince = open ? user.DonorSince : null,
            presence = self || !privacy.PresencePrivate ? user.PresenceStatus : "offline",
            bio = open ? user.Bio : string.Empty, firstLifeBio = open ? user.FirstLifeBio : string.Empty, interests = open ? user.Interests : string.Empty,
            hasPhoto = open && (user.ProfilePhotoAssetId is not null || !string.IsNullOrWhiteSpace(user.ProfilePhotoUrl)), photoUrl = open ? user.ProfilePhotoUrl : null,
            birthday = open ? BirthdayText(user) : null, characterAgeYears = open ? Math.Max(0, (int)((now - CreatedOf(user)).TotalDays / 365.25)) : (int?)null,
            married = marriage is not null, marriage,
            groups,
            settings = self ? new { hideGroups = privacy.GroupsHidden, hideMarriage = privacy.MarriageHidden, privateProfile = privacy.ProfilePrivate } : null
        };
    }

    private static async Task<List<object>> GroupsShownAsync(LifeVerseDbContext db, Guid viewer, Guid target, CancellationToken ct)
    {
        var mine = viewer == target
            ? null
            : await db.GroupMembers.AsNoTracking().Where(m => m.UserId == viewer).Select(m => m.GroupId).ToListAsync(ct);
        var rows = await db.GroupMembers.AsNoTracking().Where(m => m.UserId == target && (viewer == target || m.ShowInProfile))
            .Join(db.SocialGroups.AsNoTracking().Where(g => g.DeletedAt == null), m => m.GroupId, g => g.Id, (m, g) => new { g.Id, g.Name, g.Tag, g.Privacy, g.AccentColor, m.Role, m.ShowInProfile })
            .OrderBy(x => x.Name).Take(MaxGroupsShown * 2).ToListAsync(ct);
        return rows.Where(x => viewer == target || x.Privacy == "public" || mine!.Contains(x.Id)).Take(MaxGroupsShown)
            .Select(x => (object)new { id = x.Id, name = x.Name, tag = x.Tag, role = x.Role, color = x.AccentColor, shown = x.ShowInProfile }).ToList();
    }

    // ---- editing -----------------------------------------------------------------------------------------------------------

    public static async Task<ProfileFailure?> UpdateAsync(LifeVerseDbContext db, Guid userId, ProfileUpdate update, DateTimeOffset now, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.DeletedAt == null, ct);
        if (user is null) return new("not_found", "Conta não encontrada.");
        if (update.Bio is { Length: > MaxBio } || update.FirstLifeBio is { Length: > MaxBio }) return new("too_long", $"Os textos aceitam até {MaxBio} letras.");
        if (update.Interests is { Length: > MaxInterests }) return new("too_long", $"Os interesses aceitam até {MaxInterests} letras.");

        if (update.PhotoAssetId is { } asset && !update.ClearPhoto)
        {
            var owned = await db.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == asset && x.OwnerUserId == userId && x.DeletedAt == null, ct);
            if (owned is null) return new("photo_not_owned", "Só uma imagem sua pode virar foto de perfil.");
            var version = await db.AssetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.AssetId == asset && x.Version == owned.CurrentVersion, ct);
            if (version is null || version.MimeType is not ("image/png" or "image/jpeg" or "image/webp")) return new("photo_not_image", "A foto precisa ser uma imagem PNG, JPG ou WebP.");
            user.ProfilePhotoAssetId = asset;
        }
        if (update.ClearPhoto) { user.ProfilePhotoAssetId = null; user.ProfilePhotoUrl = null; }

        if (update.Bio is not null) user.Bio = update.Bio.Trim();
        if (update.FirstLifeBio is not null) user.FirstLifeBio = update.FirstLifeBio.Trim();
        if (update.Interests is not null) user.Interests = update.Interests.Trim();
        user.PrivacyJson = WithPrivacy(user.PrivacyJson, update);
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Lists a group on the profile or not. Only for a group the person belongs to.</summary>
    public static async Task<bool> SetGroupShownAsync(LifeVerseDbContext db, Guid userId, Guid groupId, bool shown, CancellationToken ct)
    {
        var member = await db.GroupMembers.SingleOrDefaultAsync(x => x.UserId == userId && x.GroupId == groupId, ct);
        if (member is null) return false;
        member.ShowInProfile = shown; await db.SaveChangesAsync(ct); return true;
    }

    /// <summary>The type of account is decided by the administration only (a donor supports the game).</summary>
    public static async Task<ProfileFailure?> SetAccountTypeAsync(LifeVerseDbContext db, Guid userId, string type, DateTimeOffset now, CancellationToken ct)
    {
        if (!AccountTypes.Contains(type)) return new("invalid_type", "O tipo de conta deve ser standard ou donor.");
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.DeletedAt == null, ct);
        if (user is null) return new("not_found", "Conta não encontrada.");
        if (type == "donor" && user.AccountType != "donor") user.DonorSince = now;
        if (type == "standard") user.DonorSince = null;
        user.AccountType = type; user.UpdatedAt = now; await db.SaveChangesAsync(ct); return null;
    }

    // ---- marriage ------------------------------------------------------------------------------------------------------------

    public const int ProposalDays = 14, MaxOutgoing = 5;

    public static async Task<object?> CurrentMarriageAsync(LifeVerseDbContext db, Guid userId, CancellationToken ct)
    {
        var m = await db.Marriages.AsNoTracking().SingleOrDefaultAsync(x => x.Status == "married" && (x.ProposerUserId == userId || x.TargetUserId == userId), ct);
        if (m is null) return null;
        var spouse = m.ProposerUserId == userId ? m.TargetUserId : m.ProposerUserId;
        var name = await db.Users.AsNoTracking().Where(u => u.Id == spouse).Select(u => u.Avatar != null ? u.Avatar.DisplayName : u.Username).SingleOrDefaultAsync(ct);
        return new { id = m.Id, spouseId = spouse, spouseName = name ?? "?", since = m.MarriedAt };
    }

    private static async Task ExpireOldAsync(LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var limit = now.AddDays(-ProposalDays);
        var old = await db.Marriages.Where(x => x.Status == "proposed" && x.CreatedAt < limit).ToListAsync(ct);
        foreach (var m in old) { m.Status = "expired"; m.RespondedAt = now; }
        if (old.Count > 0) await db.SaveChangesAsync(ct);
    }

    /// <summary>My marriage and the proposals waiting for an answer (made to me and made by me).</summary>
    public static async Task<object> MineAsync(LifeVerseDbContext db, Guid me, DateTimeOffset now, CancellationToken ct)
    {
        await ExpireOldAsync(db, now, ct);
        var pending = await db.Marriages.AsNoTracking().Where(x => x.Status == "proposed" && (x.ProposerUserId == me || x.TargetUserId == me)).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        var ids = pending.SelectMany(x => new[] { x.ProposerUserId, x.TargetUserId }).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, Name = u.Avatar != null ? u.Avatar.DisplayName : u.Username }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        string Name(Guid id) => names.GetValueOrDefault(id, "?");
        return new
        {
            current = await CurrentMarriageAsync(db, me, ct),
            incoming = pending.Where(x => x.TargetUserId == me).Select(x => new { x.Id, fromId = x.ProposerUserId, fromName = Name(x.ProposerUserId), x.Message, x.CreatedAt }).ToList(),
            outgoing = pending.Where(x => x.ProposerUserId == me).Select(x => new { x.Id, toId = x.TargetUserId, toName = Name(x.TargetUserId), x.Message, x.CreatedAt }).ToList()
        };
    }

    private static Task<bool> IsMarriedAsync(LifeVerseDbContext db, Guid user, CancellationToken ct) =>
        db.Marriages.AnyAsync(x => x.Status == "married" && (x.ProposerUserId == user || x.TargetUserId == user), ct);

    public static async Task<(Marriage? Marriage, ProfileFailure? Failure)> ProposeAsync(LifeVerseDbContext db, Guid me, Guid target, string? message, DateTimeOffset now, CancellationToken ct)
    {
        await ExpireOldAsync(db, now, ct);
        if (me == target) return (null, new("self", "Você não pode casar consigo mesmo."));
        if (message is { Length: > 300 }) return (null, new("too_long", "A mensagem aceita até 300 letras."));
        if (!await db.Users.AnyAsync(x => x.Id == target && x.DeletedAt == null && x.Status == "active", ct)) return (null, new("not_found", "Essa pessoa não existe."));
        if (await IsMarriedAsync(db, me, ct)) return (null, new("already_married", "Você já é casado. Divorcie-se antes de pedir outra pessoa em casamento."));
        if (await IsMarriedAsync(db, target, ct)) return (null, new("target_married", "Essa pessoa já é casada."));
        if (await db.Marriages.AnyAsync(x => x.Status == "proposed" && ((x.ProposerUserId == me && x.TargetUserId == target) || (x.ProposerUserId == target && x.TargetUserId == me)), ct)) return (null, new("already_proposed", "Já existe um pedido em aberto entre vocês."));
        if (await db.Marriages.CountAsync(x => x.Status == "proposed" && x.ProposerUserId == me, ct) >= MaxOutgoing) return (null, new("too_many", $"Você já tem {MaxOutgoing} pedidos em aberto."));
        var m = new Marriage { Id = Guid.NewGuid(), ProposerUserId = me, TargetUserId = target, Status = "proposed", Message = message?.Trim() ?? string.Empty, CreatedAt = now };
        db.Marriages.Add(m); await db.SaveChangesAsync(ct); return (m, null);
    }

    public static async Task<ProfileFailure?> AnswerAsync(LifeVerseDbContext db, Guid me, Guid id, string answer, DateTimeOffset now, CancellationToken ct)
    {
        await ExpireOldAsync(db, now, ct);
        var m = await db.Marriages.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (m is null || (m.ProposerUserId != me && m.TargetUserId != me)) return new("not_found", "Pedido não encontrado.");
        if (m.Status != "proposed") return new("not_open", "Esse pedido não está mais em aberto.");
        if (answer == "cancel") { if (m.ProposerUserId != me) return new("not_yours", "Só quem pediu pode cancelar."); m.Status = "cancelled"; }
        else
        {
            if (m.TargetUserId != me) return new("not_yours", "Só quem recebeu o pedido responde a ele.");
            if (answer == "decline") m.Status = "declined";
            else
            {
                if (await IsMarriedAsync(db, m.ProposerUserId, ct) || await IsMarriedAsync(db, m.TargetUserId, ct)) return new("already_married", "Uma das pessoas já está casada.");
                m.Status = "married"; m.MarriedAt = now;
                var others = await db.Marriages.Where(x => x.Id != m.Id && x.Status == "proposed" && (x.ProposerUserId == m.ProposerUserId || x.TargetUserId == m.ProposerUserId || x.ProposerUserId == m.TargetUserId || x.TargetUserId == m.TargetUserId)).ToListAsync(ct);
                foreach (var other in others) { other.Status = "cancelled"; other.RespondedAt = now; }
            }
        }
        m.RespondedAt = now; await db.SaveChangesAsync(ct); return null;
    }

    public static async Task<ProfileFailure?> DivorceAsync(LifeVerseDbContext db, Guid me, DateTimeOffset now, CancellationToken ct)
    {
        var m = await db.Marriages.SingleOrDefaultAsync(x => x.Status == "married" && (x.ProposerUserId == me || x.TargetUserId == me), ct);
        if (m is null) return new("not_married", "Você não está casado.");
        m.Status = "ended"; m.EndedAt = now; m.EndedByUserId = me; await db.SaveChangesAsync(ct); return null;
    }

    public static async Task<Guid?> FindByNameAsync(LifeVerseDbContext db, string name, CancellationToken ct)
    {
        var key = name.Trim().ToLowerInvariant(); if (key.Length is 0 or > 64) return null; var upper = key.ToUpperInvariant();
        var byUser = await db.Users.AsNoTracking().Where(u => u.DeletedAt == null && u.NormalizedUsername == upper).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (byUser is not null) return byUser;
        return await db.Avatars.AsNoTracking().Where(a => a.DisplayName.ToLower() == key).Select(a => (Guid?)a.UserId).FirstOrDefaultAsync(ct);
    }
}
