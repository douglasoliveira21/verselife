namespace LifeVerse.Api.Data;

public sealed class IslandFavorite { public Guid Id { get; set; } public Guid UserId { get; set; } public Guid IslandId { get; set; } public DateTimeOffset CreatedAt { get; set; } }
public sealed class Landmark { public Guid Id { get; set; } public Guid OwnerUserId { get; set; } public Guid IslandId { get; set; } public required string Name { get; set; } public float PositionX { get; set; } public float PositionY { get; set; } public float PositionZ { get; set; } public float RotationY { get; set; } public bool IsPublic { get; set; } public DateTimeOffset CreatedAt { get; set; } /* moves with every change, so the search index finds what changed */ public DateTimeOffset UpdatedAt { get; set; } public DateTimeOffset? DeletedAt { get; set; } }
public sealed class DiscoveryEvent { public Guid Id { get; set; } public Guid IslandId { get; set; } public Guid OrganizerUserId { get; set; } public required string Name { get; set; } public required string Description { get; set; } public DateTimeOffset StartsAt { get; set; } public DateTimeOffset EndsAt { get; set; } public int Capacity { get; set; } public bool IsPublic { get; set; } public DateTimeOffset CreatedAt { get; set; } /* set for an event only the members of that group can see and join */ public Guid? GroupId { get; set; } }
public sealed class EventAttendee { public Guid Id { get; set; } public Guid EventId { get; set; } public Guid UserId { get; set; } public DateTimeOffset JoinedAt { get; set; } }
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
