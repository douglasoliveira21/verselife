namespace LifeVerse.Shared.Building;

/// <summary>An object as the linkset rules see it: which linkset (group) it belongs to and which object of that linkset is its parent.</summary>
public sealed record LinkNode(Guid Id, Guid? GroupId, Guid? ParentId);

/// <summary>
/// Linksets: objects that belong together under one root. A linkset is the set of objects with the same group id; exactly one of them (the root) has no
/// parent, and every other one hangs under another member, so the linkset is a tree. These rules are shared by the island's API (which refuses anything
/// that breaks them) and by the editor (which plans the changes it asks for), so both always agree.
/// </summary>
public static class LinksetRules
{
    public const int MaxMembers = 256, MaxDepth = 16;

    /// <summary>
    /// Null when the whole set of objects is consistent, otherwise the reason: a parent that is not there or is in another linkset, a parent without a linkset,
    /// a cycle, a tree deeper than <see cref="MaxDepth"/>, or a linkset with more than <see cref="MaxMembers"/> members.
    /// </summary>
    public static string? Validate(IReadOnlyCollection<LinkNode> nodes)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        foreach (var node in nodes)
        {
            if (node.ParentId is not { } parentId) continue;
            if (node.GroupId is null || parentId == node.Id) return "invalid_link";
            if (!byId.TryGetValue(parentId, out var parent) || parent.GroupId != node.GroupId) return "broken_link";
        }
        foreach (var node in nodes)
        {
            var depth = 0; var seen = new HashSet<Guid> { node.Id }; var at = node;
            while (at.ParentId is { } up)
            {
                if (!seen.Add(up)) return "link_cycle";
                if (++depth > MaxDepth) return "link_too_deep";
                at = byId[up];
            }
        }
        foreach (var group in nodes.Where(n => n.GroupId is not null).GroupBy(n => n.GroupId))
            if (group.Count() > MaxMembers) return "linkset_too_large";
        return null;
    }

    /// <summary>The members of the linkset that are below this object, at any depth (not the object itself).</summary>
    public static IReadOnlyList<Guid> Descendants(IReadOnlyCollection<LinkNode> nodes, Guid id)
    {
        var children = nodes.Where(n => n.ParentId is not null).ToLookup(n => n.ParentId!.Value, n => n.Id); var result = new List<Guid>(); var pending = new Queue<Guid>(); pending.Enqueue(id);
        while (pending.Count > 0 && result.Count <= MaxMembers) foreach (var child in children[pending.Dequeue()]) if (!result.Contains(child) && child != id) { result.Add(child); pending.Enqueue(child); }
        return result;
    }

    /// <summary>The root of a linkset: its member without a parent. With none (or several, as in groups made before roots existed) the first by id, so everyone picks the same one.</summary>
    public static Guid? RootOf(IReadOnlyCollection<LinkNode> nodes, Guid group) =>
        nodes.Where(n => n.GroupId == group).OrderBy(n => n.ParentId is null ? 0 : 1).ThenBy(n => n.Id).Select(n => (Guid?)n.Id).FirstOrDefault();

    /// <summary>
    /// Links the selected objects into one linkset under <paramref name="root"/>. Parents inside the selection are kept (the shape of a tree already built is
    /// not lost); every other member, including the roots of linksets being joined, hangs under the new root. Returns only the objects that change.
    /// </summary>
    public static List<LinkNode> Link(IReadOnlyCollection<LinkNode> world, IReadOnlyCollection<Guid> selection, Guid root, Guid group)
    {
        var chosen = selection.ToHashSet(); var byId = world.ToDictionary(n => n.Id); var changes = new List<LinkNode>();
        if (!chosen.Contains(root)) return changes;
        foreach (var id in chosen)
        {
            if (!byId.TryGetValue(id, out var node)) continue;
            Guid? parent = id == root ? null : node.ParentId is { } p && chosen.Contains(p) && p != id && !IsAncestor(byId, id, root) ? p : root;
            var after = new LinkNode(id, group, parent); if (after != node) changes.Add(after);
        }
        return changes;
    }

    /// <summary>Whether <paramref name="ancestor"/> is above <paramref name="id"/> in its tree (so keeping its parent link could loop back).</summary>
    private static bool IsAncestor(Dictionary<Guid, LinkNode> byId, Guid ancestor, Guid id)
    {
        var at = id; for (var depth = 0; depth <= MaxDepth && byId.TryGetValue(at, out var node) && node.ParentId is { } up; depth++) { if (up == ancestor) return true; at = up; }
        return false;
    }

    /// <summary>Makes another member the root of its linkset: the chain from it up to the old root turns around, so nobody leaves the tree. Returns only what changes.</summary>
    public static List<LinkNode> SetRoot(IReadOnlyCollection<LinkNode> world, Guid newRoot)
    {
        var byId = world.ToDictionary(n => n.Id); var changes = new List<LinkNode>();
        if (!byId.TryGetValue(newRoot, out var start) || start.GroupId is null || start.ParentId is null) return changes;
        var chain = new List<LinkNode> { start }; var at = start;
        while (at.ParentId is { } up && byId.TryGetValue(up, out var next) && chain.Count <= MaxDepth) { chain.Add(next); at = next; }
        for (var i = 0; i < chain.Count; i++)
        {
            var node = chain[i]; Guid? parent = i == 0 ? null : chain[i - 1].Id; var after = new LinkNode(node.Id, node.GroupId, parent); if (after != node) changes.Add(after);
        }
        return changes;
    }

    /// <summary>
    /// What happens to the rest of a linkset when some members leave it (unlinked, deleted or returned): children of a member that left hang under its nearest
    /// member that stayed; if the root left, one of its children becomes the new root and the others hang under it; a linkset left with one member is dissolved.
    /// <paramref name="leaving"/> members themselves are not in the answer (see <see cref="Detach"/>). Returns only the survivors that change.
    /// </summary>
    public static List<LinkNode> Heal(IReadOnlyCollection<LinkNode> world, IReadOnlyCollection<Guid> leaving)
    {
        var gone = leaving.ToHashSet(); var byId = world.ToDictionary(n => n.Id); var after = new Dictionary<Guid, LinkNode>();
        // 1. whoever hung under a member that left hangs under the nearest ancestor that stayed
        foreach (var node in world.Where(n => !gone.Contains(n.Id) && n.ParentId is { } p && gone.Contains(p)))
        {
            Guid? up = node.ParentId; for (var depth = 0; depth <= MaxDepth && up is { } u && gone.Contains(u); depth++) up = byId.TryGetValue(u, out var above) ? above.ParentId : null;
            after[node.Id] = new LinkNode(node.Id, node.GroupId, up is { } survivor && !gone.Contains(survivor) ? survivor : null);
        }
        // 2. a linkset that lost its root: one orphan becomes the root (the same one for everybody: the lowest id), the other orphans hang under it
        foreach (var group in after.Values.Where(a => a.ParentId is null && a.GroupId is not null).GroupBy(a => a.GroupId))
        {
            var survivors = world.Where(n => n.GroupId == group.Key && !gone.Contains(n.Id)).Select(n => after.TryGetValue(n.Id, out var changed) ? changed : n).ToList();
            var roots = survivors.Where(n => n.ParentId is null).OrderBy(n => n.Id).ToList(); if (roots.Count <= 1) continue;
            var chosen = roots[0].Id; foreach (var other in roots.Skip(1)) after[other.Id] = new LinkNode(other.Id, other.GroupId, chosen);
        }
        // 3. one member alone is not a linkset
        foreach (var group in world.Where(n => n.GroupId is not null && !gone.Contains(n.Id)).GroupBy(n => n.GroupId))
        {
            var members = group.ToList(); if (members.Count != 1) continue;
            var alone = new LinkNode(members[0].Id, null, null); if (alone != members[0]) after[alone.Id] = alone;
        }
        return [.. after.Values.Where(a => !byId.TryGetValue(a.Id, out var before) || before != a)];
    }

    /// <summary>Takes the selected objects out of their linksets, keeping them as separate objects: they lose group and parent, and the rest is healed.</summary>
    public static List<LinkNode> Detach(IReadOnlyCollection<LinkNode> world, IReadOnlyCollection<Guid> ids)
    {
        var result = Heal(world, ids); var byId = world.ToDictionary(n => n.Id);
        foreach (var id in ids.Distinct()) if (byId.TryGetValue(id, out var node) && (node.GroupId is not null || node.ParentId is not null)) result.Add(new LinkNode(id, null, null));
        return result;
    }
}
