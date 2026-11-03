namespace LifeVerse.Shared.Building;

/// <summary>
/// Who is editing which object right now. A player who selects objects takes a lease on them; everyone else sees "em edição por …" and the
/// island refuses their changes until the lease ends. A lease lasts <see cref="Lease"/> and is renewed while the player keeps the selection,
/// so a player who crashes or loses connection never locks anything for long, and leaving the island releases everything at once.
/// The island server holds these in memory: the leases are short-lived by design and a restart simply clears them.
/// </summary>
public sealed class EditLocks
{
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(20);
    public const int MaxPerUser = 500;

    private readonly Dictionary<Guid, (Guid User, DateTimeOffset Expires)> _held = [];

    public int Count => _held.Count;

    /// <summary>Takes (or renews) the leases on all the objects, or on none: if someone else holds one of them, nothing changes and the holder is returned.</summary>
    public bool TryAcquire(IReadOnlyCollection<Guid> objects, Guid user, DateTimeOffset now, out Guid holder)
    {
        holder = Guid.Empty;
        Expire(now);
        foreach (var id in objects) if (_held.TryGetValue(id, out var current) && current.User != user) { holder = current.User; return false; }
        var already = _held.Count(x => x.Value.User == user && !objects.Contains(x.Key));
        if (already + objects.Count > MaxPerUser) { holder = user; return false; }
        foreach (var id in objects) _held[id] = (user, now + Lease);
        return true;
    }

    /// <summary>Gives up the player's leases on these objects. Leases that belong to someone else are left alone. Returns the ones actually released.</summary>
    public IReadOnlyList<Guid> Release(IEnumerable<Guid> objects, Guid user)
    {
        var released = new List<Guid>();
        foreach (var id in objects) if (_held.TryGetValue(id, out var current) && current.User == user) { _held.Remove(id); released.Add(id); }
        return released;
    }

    /// <summary>Drops every lease of a player (they left the island). Returns the objects that were freed.</summary>
    public IReadOnlyList<Guid> ReleaseAll(Guid user)
    {
        var freed = _held.Where(x => x.Value.User == user).Select(x => x.Key).ToList();
        foreach (var id in freed) _held.Remove(id);
        return freed;
    }

    /// <summary>Removes leases that ran out and returns the objects that were freed, with who held them.</summary>
    public IReadOnlyList<(Guid Object, Guid User)> Expire(DateTimeOffset now)
    {
        var gone = _held.Where(x => x.Value.Expires <= now).Select(x => (x.Key, x.Value.User)).ToList();
        foreach (var (id, _) in gone) _held.Remove(id);
        return gone;
    }

    public Guid? HolderOf(Guid objectId, DateTimeOffset now) => _held.TryGetValue(objectId, out var current) && current.Expires > now ? current.User : null;

    /// <summary>The first object among these that someone other than <paramref name="user"/> is editing, or null when all are free for the user.</summary>
    public (Guid Object, Guid Holder)? FirstHeldByOther(IEnumerable<Guid> objects, Guid user, DateTimeOffset now)
    {
        foreach (var id in objects) if (HolderOf(id, now) is { } holder && holder != user) return (id, holder);
        return null;
    }

    public IReadOnlyList<(Guid Object, Guid User, DateTimeOffset Expires)> Snapshot(DateTimeOffset now) =>
        _held.Where(x => x.Value.Expires > now).Select(x => (x.Key, x.Value.User, x.Value.Expires)).ToList();
}
