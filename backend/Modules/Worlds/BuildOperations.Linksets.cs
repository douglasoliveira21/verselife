using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Shared.Building;
using LifeVerse.Shared.Items;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

/// <summary>One object of a linkset change: which linkset it ends up in, which object is its parent (none for the root), and the revision the editor saw.</summary>
public sealed record StructureEntry(Guid ObjectId, Guid? GroupId, Guid? ParentId, int ExpectedRevision);

/// <summary>
/// Linksets and protection against accidental editing. Linksets are changed only here (a plain update keeps the group and the parent it has), in one step for
/// all the objects involved, and every result must satisfy <see cref="LinksetRules"/>: one tree per group, no cycles, at most 256 members, parents in the same group.
/// </summary>
public static partial class BuildOperations
{
    // ---- structure -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Sets the group and the parent of several objects at once (link, unlink, change the root). All or nothing: every object must be editable by the actor
    /// (their own, or on land they manage, or administrator), unprotected and modifiable; the revisions must be the ones the editor saw; and the whole
    /// result, including the objects of the same linksets that are not named, must be a consistent set of trees.
    /// </summary>
    private static async Task<IResult> StructureAsync(ServerBuildOperationRequest request, Island island, Guid actor, bool isAdmin, List<ParcelFacts> parcels, LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var entries = request.Links; if (entries is null || entries.Length == 0 || entries.Length > LinksetRules.MaxMembers || entries.Select(e => e.ObjectId).Distinct().Count() != entries.Length) return Refuse(400, "invalid_operation");
        var ids = entries.Select(e => e.ObjectId).ToList();
        var objects = await db.BuildObjects.Where(x => ids.Contains(x.Id) && x.IslandId == island.Id && x.DeletedAt == null).ToListAsync(ct); if (objects.Count != ids.Count) return Refuse(404, "not_found");
        var byId = objects.ToDictionary(o => o.Id);
        foreach (var entry in entries)
        {
            var o = byId[entry.ObjectId];
            if (o.Revision != entry.ExpectedRevision) return Results.Json(new { code = "revision_conflict", currentRevision = o.Revision }, statusCode: 409);
            if (BuildRules.CanEditObject(actor, o.OwnerUserId, isAdmin, BuildRules.ParcelAt(parcels, o.PositionX, o.PositionZ)) is { } notOwner) return Refuse(403, notOwner, $"{o.Name}: {notOwner}");
            if (o.EditLocked) return Refuse(403, "object_protected", $"{o.Name} está protegido contra edição.");
            if (!ItemRights.Has((await ObjectRights.EffectiveAsync(db, o, ct)).Owner, Right.Modify)) return Refuse(403, "no_modify", $"{o.Name}: não pode ser modificado.");
        }

        // the whole picture after the change: the named objects as asked, plus everyone else in the linksets they touch (before or after)
        var groups = objects.Where(o => o.GroupId is not null).Select(o => o.GroupId!.Value).Concat(entries.Where(e => e.GroupId is not null).Select(e => e.GroupId!.Value)).Distinct().ToList();
        var others = await db.BuildObjects.AsNoTracking().Where(x => x.IslandId == island.Id && x.DeletedAt == null && x.GroupId != null && groups.Contains(x.GroupId!.Value) && !ids.Contains(x.Id)).Select(x => new LinkNode(x.Id, x.GroupId, x.ParentObjectId)).ToListAsync(ct);
        var final = others.Concat(entries.Select(e => new LinkNode(e.ObjectId, e.GroupId, e.GroupId is null ? null : e.ParentId))).ToList();
        if (entries.Any(e => e.GroupId is null && e.ParentId is not null)) return Refuse(400, "invalid_link");
        if (LinksetRules.Validate(final) is { } problem) return Refuse(400, problem);

        var changed = new List<BuildObject>();
        foreach (var entry in entries)
        {
            var o = byId[entry.ObjectId]; var parent = entry.GroupId is null ? null : entry.ParentId; if (o.GroupId == entry.GroupId && o.ParentObjectId == parent) continue;
            o.GroupId = entry.GroupId; o.ParentObjectId = parent; o.Revision++; o.UpdatedAt = now; changed.Add(o);
            db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = island.Id, ObjectId = o.Id, ActorUserId = actor, Operation = "structure", PayloadJson = JsonSerializer.Serialize(new { groupId = entry.GroupId, parentId = parent }), Revision = o.Revision, CreatedAt = now });
        }
        if (changed.Count > 0 && await TrySaveAsync(db, changed[0].Id, ct) is { } conflict) return conflict;
        return Results.Ok(new { operation = "structure", objects = changed.Select(BuildEndpoints.ToResponse).ToArray() });
    }

    /// <summary>
    /// After objects leave their linksets by being deleted or returned, the objects that hung under them are re-hung (see <see cref="LinksetRules.Heal"/>), so a
    /// linkset is never left with a missing parent or without a root. The changes are applied in the caller's save. Returns the survivors that changed.
    /// </summary>
    internal static async Task<List<BuildObject>> HealAsync(LifeVerseDbContext db, Guid islandId, IReadOnlyCollection<BuildObject> leaving, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        var groups = leaving.Where(o => o.GroupId is not null).Select(o => o.GroupId!.Value).Distinct().ToList(); var gone = leaving.Select(o => o.Id).ToHashSet();
        if (groups.Count == 0 && !leaving.Any(o => o.ParentObjectId is not null)) return [];
        var members = await db.BuildObjects.Where(x => x.IslandId == islandId && x.DeletedAt == null && x.GroupId != null && groups.Contains(x.GroupId!.Value)).ToListAsync(ct);
        var world = members.Concat(leaving.Where(l => !members.Any(m => m.Id == l.Id))).Select(o => new LinkNode(o.Id, o.GroupId, o.ParentObjectId)).ToList();
        var changes = LinksetRules.Heal(world, gone); var healed = new List<BuildObject>();
        foreach (var change in changes)
        {
            var o = members.FirstOrDefault(m => m.Id == change.Id); if (o is null) continue;
            o.GroupId = change.GroupId; o.ParentObjectId = change.ParentId; o.Revision++; o.UpdatedAt = now; healed.Add(o);
            db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = islandId, ObjectId = o.Id, ActorUserId = actor, Operation = "structure", PayloadJson = JsonSerializer.Serialize(new { groupId = change.GroupId, parentId = change.ParentId, because = "member left" }), Revision = o.Revision, CreatedAt = now });
        }
        return healed;
    }

    /// <summary>Null when a new object may be created with this group and parent: the parent exists, is in the same linkset, and the linkset stays a valid tree within its size limit.</summary>
    private static async Task<string?> ValidateNewLinkAsync(LifeVerseDbContext db, Guid islandId, BuildObjectRequest o, CancellationToken ct)
    {
        if (o.GroupId is null) return o.ParentObjectId is null ? null : "invalid_link";
        var members = await db.BuildObjects.AsNoTracking().Where(x => x.IslandId == islandId && x.DeletedAt == null && x.GroupId == o.GroupId).Select(x => new LinkNode(x.Id, x.GroupId, x.ParentObjectId)).ToListAsync(ct);
        members.Add(new LinkNode(Guid.NewGuid(), o.GroupId, o.ParentObjectId)); return LinksetRules.Validate(members);
    }

    // ---- protection against accidental editing ----------------------------------------------------------------------

    /// <summary>
    /// Protects (or unprotects) objects against accidental editing: a protected object cannot be moved, changed, linked, deleted or returned by its owner until it
    /// is unprotected. It can be done by the owner, the owner of the land it stands on, or an island administrator.
    /// </summary>
    private static async Task<IResult> ProtectAsync(ServerBuildOperationRequest request, Island island, Guid actor, bool isAdmin, List<ParcelFacts> parcels, LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var ids = (request.ObjectIds ?? (request.ObjectId is { } one ? [one] : [])).Distinct().ToArray(); if (ids.Length == 0 || ids.Length > LinksetRules.MaxMembers || request.Locked is not { } locked) return Refuse(400, "invalid_operation");
        var objects = await db.BuildObjects.Where(x => ids.Contains(x.Id) && x.IslandId == island.Id && x.DeletedAt == null).ToListAsync(ct); if (objects.Count != ids.Length) return Refuse(404, "not_found");
        foreach (var o in objects) if (BuildRules.CanEditObject(actor, o.OwnerUserId, isAdmin, BuildRules.ParcelAt(parcels, o.PositionX, o.PositionZ)) is { } denied) return Refuse(403, denied, $"{o.Name}: {denied}");
        var changed = new List<BuildObject>();
        foreach (var o in objects)
        {
            if (o.EditLocked == locked) continue;
            o.EditLocked = locked; o.Revision++; o.UpdatedAt = now; changed.Add(o);
            db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = island.Id, ObjectId = o.Id, ActorUserId = actor, Operation = locked ? "protect" : "unprotect", PayloadJson = "{}", Revision = o.Revision, CreatedAt = now });
        }
        if (changed.Count > 0 && await TrySaveAsync(db, changed[0].Id, ct) is { } conflict) return conflict;
        return Results.Ok(new { operation = "protect", locked, objects = changed.Select(BuildEndpoints.ToResponse).ToArray() });
    }
}
