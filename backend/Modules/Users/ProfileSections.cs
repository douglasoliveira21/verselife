using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Discovery;
using LifeVerse.Shared.Search;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Users;

/// <summary>
/// The tabs of the profile that are read from facts the game already knows: favorites (islands, places and events the person marked, only what the viewer may see), achievements (milestones that are worked out
/// from real records, with the date of the record, never invented) and statistics (counts of things the profile already shows). What the person hid stays hidden here too.
/// </summary>
public static class ProfileSections
{
    public const int MaxFavorites = 30;
    private static readonly string[] FavoriteKinds = [SearchKinds.Island, SearchKinds.Place, SearchKinds.Event];

    public sealed record Achievement(string Key, string Title, string Description, string Icon, DateTimeOffset UnlockedAt);

    public static async Task<List<object>> FavoritesAsync(LifeVerseDbContext db, Guid viewer, Guid target, DateTimeOffset now, CancellationToken ct)
    {
        var marked = await db.SearchFavorites.AsNoTracking().Where(f => f.UserId == target && FavoriteKinds.Contains(f.Kind)).OrderByDescending(f => f.CreatedAt).Take(MaxFavorites * 2).Select(f => new { f.Kind, f.RefId }).ToListAsync(ct);
        if (marked.Count == 0) return [];
        var ids = marked.Select(m => m.RefId).ToList();
        var entries = await db.SearchEntries.AsNoTracking().Where(e => !e.Hidden && ids.Contains(e.RefId) && FavoriteKinds.Contains(e.Kind)).ToListAsync(ct);
        entries = [.. entries.Where(e => marked.Any(m => m.Kind == e.Kind && m.RefId == e.RefId))];
        var allowed = await GlobalSearch.AllowedAsync(db, viewer, entries, now, ct);
        var order = marked.Select((m, i) => (m.Kind, m.RefId, i)).ToDictionary(x => (x.Kind, x.RefId), x => x.i);
        return [.. allowed.OrderBy(e => order[(e.Kind, e.RefId)]).Take(MaxFavorites).Select(e => (object)new { kind = e.Kind, label = SearchKinds.Label(e.Kind), id = e.RefId, title = e.Title, subtitle = e.Subtitle, islandId = e.IslandId, x = e.X, z = e.Z })];
    }

    /// <summary>Milestones, each backed by a record with a date. Things the person hides (groups, land, marriage) do not produce an achievement for other people.</summary>
    public static async Task<List<object>> AchievementsAsync(LifeVerseDbContext db, User user, ProfileService.Privacy privacy, bool self, DateTimeOffset? marriedSince, DateTimeOffset now, CancellationToken ct) =>
        [.. (await AchievementListAsync(db, user, privacy, self, marriedSince, now, ct)).Select(a => (object)new { key = a.Key, title = a.Title, description = a.Description, icon = a.Icon, unlockedAt = a.UnlockedAt })];

    public static async Task<List<Achievement>> AchievementListAsync(LifeVerseDbContext db, User user, ProfileService.Privacy privacy, bool self, DateTimeOffset? marriedSince, DateTimeOffset now, CancellationToken ct)
    {
        var found = new List<Achievement>(); var joined = ProfileService.CreatedOf(user);
        if (now >= joined.AddYears(1)) found.Add(new("veteran_1y", "Um ano de LifeVerse", "Completou um ano desde a criação do personagem.", "calendar", joined.AddYears(1)));
        if (user.AccountType == "donor" && user.DonorSince is { } since) found.Add(new("donor", "Apoia o LifeVerse", "Contribui para manter o mundo vivo.", "star", since));

        if (self || !privacy.ParcelsHidden)
        {
            var parcels = await db.IslandParcels.AsNoTracking().Where(p => p.OwnerUserId == user.Id && p.GroupId == null && p.DeletedAt == null).OrderBy(p => p.CreatedAt).Select(p => p.CreatedAt).ToListAsync(ct);
            if (parcels.Count >= 1) found.Add(new("first_parcel", "Primeiro terreno", "Tornou-se dono de um terreno.", "home", parcels[0]));
            if (parcels.Count >= 3) found.Add(new("landowner", "Proprietário(a)", "Tem três terrenos ou mais.", "map", parcels[2]));
        }
        if (self || !privacy.GroupsHidden)
        {
            var joins = await db.GroupMembers.AsNoTracking().Where(m => m.UserId == user.Id).Join(db.SocialGroups.AsNoTracking().Where(g => g.DeletedAt == null), m => m.GroupId, g => g.Id, (m, g) => new { m.JoinedAt, g.OwnerUserId }).OrderBy(x => x.JoinedAt).ToListAsync(ct);
            if (joins.Count > 0) found.Add(new("community", "Faz parte de uma comunidade", "Entrou em um grupo.", "people", joins[0].JoinedAt));
            if (joins.Where(x => x.OwnerUserId == user.Id).Select(x => (DateTimeOffset?)x.JoinedAt).FirstOrDefault() is { } founded) found.Add(new("founder", "Fundador(a)", "Criou um grupo.", "target", founded));
        }
        if (marriedSince is { } wed && (self || !privacy.MarriageHidden)) found.Add(new("married", "Casado(a) no LifeVerse", "Celebrou um casamento dentro do jogo.", "heart", wed));

        var gallery = await db.ProfileGallery.AsNoTracking().Where(g => g.UserId == user.Id).OrderBy(g => g.CreatedAt).Select(g => g.CreatedAt).ToListAsync(ct);
        if (gallery.Count >= 3) found.Add(new("photographer", "Fotógrafo(a)", "Montou uma galeria com três imagens ou mais.", "camera", gallery[2]));
        var firstEvent = await db.EventAttendees.AsNoTracking().Where(a => a.UserId == user.Id).OrderBy(a => a.JoinedAt).Select(a => (DateTimeOffset?)a.JoinedAt).FirstOrDefaultAsync(ct);
        if (firstEvent is { } went) found.Add(new("event_goer", "Frequentador(a) de eventos", "Confirmou presença em um evento.", "bell", went));
        var firstProduct = await db.MarketplaceProducts.AsNoTracking().Where(p => p.SellerUserId == user.Id && p.Status == "active").OrderBy(p => p.CreatedAt).Select(p => (DateTimeOffset?)p.CreatedAt).FirstOrDefaultAsync(ct);
        if (firstProduct is { } listed) found.Add(new("creator", "Criador(a)", "Colocou um produto à venda no mercado.", "hammer", listed));

        return [.. found.OrderByDescending(a => a.UnlockedAt)];
    }

    /// <summary>Counts of what the profile already shows (the land and groups of others are only those they chose to show).</summary>
    public static async Task<object> StatsAsync(LifeVerseDbContext db, User user, ProfileService.Privacy privacy, bool self, DateTimeOffset now, CancellationToken ct)
    {
        var joined = ProfileService.CreatedOf(user);
        var groups = self || !privacy.GroupsHidden ? await db.GroupMembers.AsNoTracking().Where(m => m.UserId == user.Id && (self || m.ShowInProfile)).Join(db.SocialGroups.AsNoTracking().Where(g => g.DeletedAt == null && (self || g.Privacy == "public")), m => m.GroupId, g => g.Id, (m, g) => g.Id).CountAsync(ct) : (int?)null;
        var parcels = self || !privacy.ParcelsHidden ? await db.IslandParcels.AsNoTracking().CountAsync(p => p.OwnerUserId == user.Id && p.GroupId == null && p.DeletedAt == null && (self || p.ShowInProfile), ct) : (int?)null;
        var products = await db.MarketplaceProducts.AsNoTracking().CountAsync(p => p.SellerUserId == user.Id && p.Status == "active", ct);
        var events = await db.EventAttendees.AsNoTracking().CountAsync(a => a.UserId == user.Id, ct);
        var pictures = await db.ProfileGallery.AsNoTracking().CountAsync(g => g.UserId == user.Id, ct);
        return new { joinedAt = joined, daysMember = Math.Max(0, (int)(now - joined).TotalDays), groups, parcels, creations = products, events, pictures };
    }
}
