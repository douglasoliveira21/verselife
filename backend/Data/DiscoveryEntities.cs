namespace LifeVerse.Api.Data;

public sealed class IslandFavorite { public Guid Id { get; set; } public Guid UserId { get; set; } public Guid IslandId { get; set; } public DateTimeOffset CreatedAt { get; set; } }
public sealed class Landmark { public Guid Id { get; set; } public Guid OwnerUserId { get; set; } public Guid IslandId { get; set; } public required string Name { get; set; } public float PositionX { get; set; } public float PositionY { get; set; } public float PositionZ { get; set; } public float RotationY { get; set; } public bool IsPublic { get; set; } public DateTimeOffset CreatedAt { get; set; } /* moves with every change, so the search index finds what changed */ public DateTimeOffset UpdatedAt { get; set; } public DateTimeOffset? DeletedAt { get; set; } /* what the owner wrote about the place, and the folder it is kept in (null: loose) */ public string Description { get; set; } = string.Empty; public Guid? FolderId { get; set; } }
public sealed class DiscoveryEvent { public Guid Id { get; set; } public Guid IslandId { get; set; } public Guid OrganizerUserId { get; set; } public required string Name { get; set; } public required string Description { get; set; } public DateTimeOffset StartsAt { get; set; } public DateTimeOffset EndsAt { get; set; } public int Capacity { get; set; } public bool IsPublic { get; set; } public DateTimeOffset CreatedAt { get; set; } /* set for an event only the members of that group can see and join */ public Guid? GroupId { get; set; }
    /// <summary>One of EventRules.Categories.</summary>
    public string Category { get; set; } = "outros";
    /// <summary>An https picture of the event, or empty.</summary>
    public string ImageUrl { get; set; } = string.Empty;
    /// <summary>What is charged at the door, in L$ (0: free). Told in the listing; the door collects it.</summary>
    public long PriceCoins { get; set; }
    /// <summary>livre, moderado or adulto.</summary>
    public string ContentRating { get; set; } = "livre";
    /// <summary>Set when the organizer (or the owner of the island) called it off.</summary>
    public DateTimeOffset? CancelledAt { get; set; }
    /// <summary>Where people arrive on the island (null: its spawn point).</summary>
    public float? ArrivalX { get; set; }
    public float? ArrivalZ { get; set; }
    /// <summary>Moves with every change, so the search index finds what changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
public sealed class EventAttendee
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    /// <summary>The person signed up to go (counts toward the capacity). A line with only InCalendar is just a saved reminder.</summary>
    public bool Going { get; set; } = true;
    /// <summary>The event is in the personal calendar of this person.</summary>
    public bool InCalendar { get; set; }
}
public sealed class PhotoSnapshot { public Guid Id { get; set; } public Guid OwnerUserId { get; set; } public Guid? IslandId { get; set; } public required string StorageKey { get; set; } public required string MetadataJson { get; set; } public bool IsPublic { get; set; } public DateTimeOffset CreatedAt { get; set; } }
public sealed class TeleportReservation { public Guid Id { get; set; } public Guid UserId { get; set; } public Guid IslandId { get; set; } public required string Status { get; set; } public DateTimeOffset ExpiresAt { get; set; } public DateTimeOffset CreatedAt { get; set; } }

/// <summary>
/// One thing that can be found by the global search: a line of the index, kept from the real data by the indexer. The text is stored without accents or
/// punctuation (and the database builds its full-text vector from it). The index only ever holds public things; the search checks the source again.
/// </summary>
public sealed class SearchEntry
{
    public Guid Id { get; set; }
    /// <summary>island, parcel, person, group, store, product, event or poi.</summary>
    public required string Kind { get; set; }
    public Guid RefId { get; set; }
    public required string Title { get; set; }
    public string Subtitle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    /// <summary>Everything that can be matched, normalized: title, subtitle, category and description.</summary>
    public required string SearchText { get; set; }
    public Guid? IslandId { get; set; }
    /// <summary>Where to go: the spot inside the island, when the thing has one.</summary>
    public float? X { get; set; }
    public float? Z { get; set; }
    public string? ThumbnailUrl { get; set; }
    /// <summary>The person whose it is (for hiding what belongs to somebody who blocked you or whom you blocked).</summary>
    public Guid? OwnerUserId { get; set; }
    public double Popularity { get; set; }
    /// <summary>Taken out of the results by moderation.</summary>
    public bool Hidden { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    /// <summary>What it costs (events): L$, for the free/paid filter and the card.</summary>
    public long? PriceCoins { get; set; }
    /// <summary>livre, moderado or adulto, for the filter of content rating.</summary>
    public string Rating { get; set; } = string.Empty;
    /// <summary>For products: what the buyer may do with it (ItemRights bits), for the filter of permissions.</summary>
    public int? Rights { get; set; }
    /// <summary>For products: the creator of the item, normalized, for the filter by creator.</summary>
    public string Creator { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset SourceUpdatedAt { get; set; }
    public DateTimeOffset IndexedAt { get; set; }
}

/// <summary>Something a person marked in the search to find again (any kind of result, not only islands). Kept by kind and id, so it follows the index.</summary>
public sealed class SearchFavorite { public Guid Id { get; set; } public Guid UserId { get; set; } public required string Kind { get; set; } public Guid RefId { get; set; } public DateTimeOffset CreatedAt { get; set; } }

/// <summary>A report of a search result (what was reported is kept by kind and id, with the title it had).</summary>
public sealed class SearchReport
{
    public Guid Id { get; set; }
    public Guid ReporterUserId { get; set; }
    public required string Kind { get; set; }
    public Guid RefId { get; set; }
    public string Title { get; set; } = string.Empty;
    public required string Reason { get; set; }
    /// <summary>open, hidden (the result was kept out of the search) or dismissed (it was put back).</summary>
    public required string Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

/// <summary>How the search was used on a day, for one search text (no names): searches, those that found nothing, results opened and visited, time spent.</summary>
public sealed class SearchStat
{
    public DateOnly Day { get; set; }
    public required string Query { get; set; }
    public int Searches { get; set; }
    public int ZeroResults { get; set; }
    public int Opens { get; set; }
    public int Teleports { get; set; }
    public long TotalMs { get; set; }
}

/// <summary>A subject the person said they like (a word or two, normalized): the base of the recommendations.</summary>
public sealed class SearchInterest { public Guid Id { get; set; } public Guid UserId { get; set; } public required string Term { get; set; } public DateTimeOffset CreatedAt { get; set; } }

/// <summary>How far the indexer has read each kind of source (so it only reads what changed since).</summary>
public sealed class SearchState
{
    public required string Kind { get; set; }
    public DateTimeOffset Watermark { get; set; }
    public DateTimeOffset RanAt { get; set; }
    public int Entries { get; set; }
}

/// <summary>
/// A shop of a person or a group that exists only on the market (a virtual store): a name, a description, a category, a logo and a banner, and the catalog is what the owner sells. Shops in the world (a vending object
/// on an island) stay WorldStore; the search lists both, and says which has a place to visit.
/// </summary>
public sealed class Storefront
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    /// <summary>Set when a group runs the shop: it sells what the group sells.</summary>
    public Guid? GroupId { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>One of StoreRules.Categories.</summary>
    public string Category { get; set; } = "outros";
    public string LogoUrl { get; set; } = string.Empty;
    public string BannerUrl { get; set; } = string.Empty;
    /// <summary>The owner can close the shop without deleting it.</summary>
    public bool IsOpen { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>The picture of an island from above that the world map draws: made from the saved terrain and the objects, kept with what it was made from so it is only made again when that changes.</summary>
public sealed class MapTile
{
    public Guid IslandId { get; set; }
    /// <summary>The version of the saved terrain it was made from.</summary>
    public long SnapshotVersion { get; set; }
    /// <summary>A short mark of the objects on the island when it was made (their number and places).</summary>
    public string ObjectsStamp { get; set; } = string.Empty;
    public int Size { get; set; }
    public byte[] Png { get; set; } = [];
    /// <summary>What a client can send back to ask "has it changed?".</summary>
    public string Etag { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
}

/// <summary>A folder of a person's landmarks on the map.</summary>
public sealed class LandmarkFolder { public Guid Id { get; set; } public Guid OwnerUserId { get; set; } public required string Name { get; set; } public DateTimeOffset CreatedAt { get; set; } }
