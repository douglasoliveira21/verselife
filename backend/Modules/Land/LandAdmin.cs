using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

public sealed record AccessEntryView(Guid Id, Guid UserId, string UserName, string Kind, string Reason, DateTimeOffset? ExpiresAt, DateTimeOffset CreatedAt);
public sealed record AccessView(string Mode, IReadOnlyList<AccessEntryView> Entries);
public sealed record ParcelObjectView(Guid Id, string Name, Guid OwnerUserId, string OwnerName, string PrimitiveType, int Cost, bool HasScripts, float X, float Y, float Z, DateTimeOffset CreatedAt, bool Authorized);
public sealed record AuditEntryView(string Operation, string ActorName, string Summary, DateTimeOffset At);

/// <summary>
/// Managing a parcel after it exists: cutting it in two, joining neighbours, who may enter, and what stands on it. Like everything else
/// in the land system the client only asks; every rule is checked here, and each change is one unit of work with a line in the parcel's history.
/// </summary>
public static class LandAdmin
{
    public static readonly string[] Modes = ["public", "list", "private"];
    public const int MaxEntriesPerKind = 200;
    public static readonly TimeSpan EjectBan = TimeSpan.FromMinutes(1);

    private static async Task<IslandParcel?> ParcelAsync(LifeVerseDbContext db, Guid id, CancellationToken ct) => await db.IslandParcels.SingleOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct);

    private static async Task<HashSet<long>> CellsAsync(LifeVerseDbContext db, Guid parcelId, CancellationToken ct) =>
        (await db.ParcelCells.AsNoTracking().Where(c => c.ParcelId == parcelId).Select(c => new { c.Cx, c.Cz }).ToListAsync(ct)).Select(c => LandGrid.Key(c.Cx, c.Cz)).ToHashSet();

    private static Task<bool> LiveRentalAsync(LifeVerseDbContext db, Guid parcelId, CancellationToken ct) => db.ParcelRentals.AnyAsync(r => r.ParcelId == parcelId && (r.Status == "active" || r.Status == "grace"), ct);

    private static void Log(LifeVerseDbContext db, IslandParcel parcel, Guid actor, string operation, object data, DateTimeOffset now)
    {
        parcel.Revision++; parcel.UpdatedAt = now;
        db.ParcelChanges.Add(new ParcelChange { Id = Guid.NewGuid(), ParcelId = parcel.Id, ActorUserId = actor, Operation = operation, PayloadJson = JsonSerializer.Serialize(data), Revision = parcel.Revision, CreatedAt = now });
    }

    private static void Reshape(IslandParcel parcel, IReadOnlyCollection<long> cells)
    {
        var (minX, minZ, maxX, maxZ) = LandGrid.Bounds(cells);
        parcel.BoundsJson = JsonSerializer.Serialize(new { minX, minZ, maxX, maxZ }); parcel.Area = LandGrid.AreaOf(cells.Count);
    }

    private static void ClearOffers(IslandParcel parcel) { parcel.SalePrice = null; parcel.SaleBuyerUserId = null; parcel.SaleDescription = string.Empty; parcel.RentPrice = null; }

    private static async Task<CommerceResult> SaveAsync(LifeVerseDbContext db, CancellationToken ct, object? data = null)
    {
        try { await db.SaveChangesAsync(ct); return CommerceResult.Done(data); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException or InvalidOperationException)
        {
            foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached;
            return CommerceResult.Fail("conflict");
        }
    }

    // ---- splitting -------------------------------------------------------------------------------------------

    /// <summary>
    /// Cuts the chosen cells out of a parcel as a new parcel of the same owner. Both pieces must stay in one piece; the objects stay where they
    /// are (and with whoever owns them); the building budget is shared out by area, but never below what each piece already holds.
    /// </summary>
    public static async Task<CommerceResult> SplitAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, IReadOnlyCollection<long> cutCells, string name, TimeProvider clock, CancellationToken ct)
    {
        var parcel = await ParcelAsync(db, parcelId, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        if (!await LandAuthority.CanManageAsync(db, parcel, actor, ct)) return CommerceResult.Fail("forbidden");
        if (await LiveRentalAsync(db, parcelId, ct)) return CommerceResult.Fail("rented");
        name = (name ?? string.Empty).Trim(); if (name.Length == 0 || name.Length > 120) return CommerceResult.Fail("bad_name");
        if (await db.IslandParcels.AnyAsync(p => p.IslandId == parcel.IslandId && p.Name == name && p.DeletedAt == null, ct)) return CommerceResult.Fail("name_taken");
        var all = await CellsAsync(db, parcelId, ct);
        var cut = cutCells as HashSet<long> ?? [.. cutCells];
        if (cut.Count == 0) return CommerceResult.Fail("empty");
        if (!cut.IsSubsetOf(all)) return CommerceResult.Fail("not_in_parcel");
        var rest = new HashSet<long>(all); rest.ExceptWith(cut);
        if (rest.Count == 0) return CommerceResult.Fail("whole_parcel");
        if (!LandGrid.IsContiguous(cut) || !LandGrid.IsContiguous(rest)) return CommerceResult.Fail("not_contiguous");

        // how much is already built on each side
        var (minX, minZ, maxX, maxZ) = LandGrid.Bounds(all);
        var objects = await db.BuildObjects.AsNoTracking().Where(o => o.IslandId == parcel.IslandId && o.DeletedAt == null && o.PositionX >= minX && o.PositionX < maxX && o.PositionZ >= minZ && o.PositionZ < maxZ).Select(o => new { o.PositionX, o.PositionZ }).ToListAsync(ct);
        var inCut = objects.Count(o => cut.Contains(LandGrid.Key(LandGrid.CellOf(o.PositionX), LandGrid.CellOf(o.PositionZ))));
        var inRest = objects.Count(o => rest.Contains(LandGrid.Key(LandGrid.CellOf(o.PositionX), LandGrid.CellOf(o.PositionZ))));
        int lowNew = Math.Max(1, inCut), lowOld = Math.Max(1, inRest), total = parcel.MaxObjects;
        if (total < lowNew + lowOld) return CommerceResult.Fail("limit_too_low");
        var newMax = Math.Clamp((int)Math.Round(total * (double)cut.Count / all.Count), lowNew, total - lowOld);

        var now = clock.GetUtcNow();
        var created = new IslandParcel
        {
            Id = Guid.NewGuid(), IslandId = parcel.IslandId, OwnerUserId = parcel.OwnerUserId, GroupId = parcel.GroupId, Name = name, Description = string.Empty, BoundsJson = "{}", MaxObjects = newMax,
            PermissionsJson = parcel.PermissionsJson, EnvironmentJson = parcel.EnvironmentJson, AccessMode = parcel.AccessMode, BuildMode = parcel.BuildMode, AutoReturnMinutes = parcel.AutoReturnMinutes, CreatedAt = now, UpdatedAt = now
        };
        Reshape(created, cut);
        var (cMinX, cMinZ, cMaxX, cMaxZ) = LandGrid.Bounds(cut);
        created.SpawnX = (float)((cMinX + cMaxX) / 2); created.SpawnY = parcel.SpawnY; created.SpawnZ = (float)((cMinZ + cMaxZ) / 2);
        db.IslandParcels.Add(created);
        foreach (var row in await db.ParcelCells.Where(c => c.ParcelId == parcelId).ToListAsync(ct))
            if (cut.Contains(LandGrid.Key(row.Cx, row.Cz))) row.ParcelId = created.Id;
        // the new piece keeps the same doors open and shut
        foreach (var entry in await db.ParcelAccess.AsNoTracking().Where(a => a.ParcelId == parcelId && a.RevokedAt == null).ToListAsync(ct))
            db.ParcelAccess.Add(new ParcelAccess { Id = Guid.NewGuid(), ParcelId = created.Id, UserId = entry.UserId, Kind = entry.Kind, GrantedByUserId = entry.GrantedByUserId, Reason = entry.Reason, ExpiresAt = entry.ExpiresAt, CreatedAt = now });
        parcel.MaxObjects = total - newMax; ClearOffers(parcel); Reshape(parcel, rest);
        db.ParcelChanges.Add(new ParcelChange { Id = Guid.NewGuid(), ParcelId = created.Id, ActorUserId = actor, Operation = "split_from", PayloadJson = JsonSerializer.Serialize(new { from = parcelId, cells = cut.Count }), Revision = created.Revision, CreatedAt = now });
        Log(db, parcel, actor, "split", new { newParcel = created.Id, cut = cut.Count, remaining = rest.Count, newMax, oldMax = parcel.MaxObjects }, now);
        return await SaveAsync(db, ct, new { parcelId, newParcelId = created.Id, newMaxObjects = newMax, remainingMaxObjects = parcel.MaxObjects });
    }

    // ---- expanding and shrinking -----------------------------------------------------------------------------

    /// <summary>
    /// Adds free cells that touch the parcel. The parcel's object limit grows by <paramref name="extraObjects"/> (0 keeps it), never past what the island still has free.
    /// Open offers are withdrawn because the thing offered changed.
    /// </summary>
    public static async Task<CommerceResult> ExpandAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, IReadOnlyCollection<long> addCells, int extraObjects, TimeProvider clock, CancellationToken ct)
    {
        var parcel = await ParcelAsync(db, parcelId, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        if (!await LandAuthority.CanManageAsync(db, parcel, actor, ct)) return CommerceResult.Fail("forbidden");
        if (await LiveRentalAsync(db, parcelId, ct)) return CommerceResult.Fail("rented");
        var add = addCells as HashSet<long> ?? [.. addCells];
        if (add.Count == 0) return CommerceResult.Fail("empty");
        if (extraObjects < 0 || (long)parcel.MaxObjects + extraObjects > 100000) return CommerceResult.Fail("bad_limit");
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(i => i.Id == parcel.IslandId, ct); if (island is null) return CommerceResult.Fail("not_found");
        var own = await CellsAsync(db, parcelId, ct);
        if (add.Overlaps(own)) return CommerceResult.Fail("overlap");
        var union = new HashSet<long>(own); union.UnionWith(add);
        if (union.Count > LandGrid.MaxCellsPerParcel) return CommerceResult.Fail("too_big");
        foreach (var key in add) { var (cx, cz) = LandGrid.Unkey(key); if (!LandGrid.InsideIsland(cx, cz, island.Size)) return CommerceResult.Fail("outside_island"); }
        if (!LandGrid.IsContiguous(union)) return CommerceResult.Fail("not_contiguous");
        var (minX, minZ, maxX, maxZ) = LandGrid.Bounds(add);
        var (minCx, minCz) = (LandGrid.CellOf(minX), LandGrid.CellOf(minZ)); var (maxCx, maxCz) = (LandGrid.CellOf(maxX) - 1, LandGrid.CellOf(maxZ) - 1);
        var taken = await db.ParcelCells.AsNoTracking().Where(c => c.IslandId == island.Id && c.Cx >= minCx && c.Cx <= maxCx && c.Cz >= minCz && c.Cz <= maxCz).ToListAsync(ct);
        if (taken.Any(c => add.Contains(LandGrid.Key(c.Cx, c.Cz)))) return CommerceResult.Fail("overlap");
        if (extraObjects > 0)
        {
            var allocated = await db.IslandParcels.Where(p => p.IslandId == island.Id && p.DeletedAt == null).SumAsync(p => (int?)p.MaxObjects, ct) ?? 0;
            if (allocated + extraObjects > LandBudget.IslandObjects(island)) return CommerceResult.Fail("budget_exceeded");
        }

        var now = clock.GetUtcNow();
        foreach (var key in add) { var (cx, cz) = LandGrid.Unkey(key); db.ParcelCells.Add(new ParcelCell { IslandId = island.Id, Cx = cx, Cz = cz, ParcelId = parcelId }); }
        parcel.MaxObjects += extraObjects; ClearOffers(parcel); Reshape(parcel, union);
        Log(db, parcel, actor, "expand", new { added = add.Count, cells = union.Count, extraObjects }, now);
        return await SaveAsync(db, ct, new { parcelId, cells = union.Count, area = parcel.Area, maxObjects = parcel.MaxObjects });
    }

    /// <summary>Gives cells back to the island. What is left must stay in one piece and nothing may stand on the cells given up; the object limit stays as it was.</summary>
    public static async Task<CommerceResult> ShrinkAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, IReadOnlyCollection<long> removeCells, TimeProvider clock, CancellationToken ct)
    {
        var parcel = await ParcelAsync(db, parcelId, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        if (!await LandAuthority.CanManageAsync(db, parcel, actor, ct)) return CommerceResult.Fail("forbidden");
        if (await LiveRentalAsync(db, parcelId, ct)) return CommerceResult.Fail("rented");
        var remove = removeCells as HashSet<long> ?? [.. removeCells];
        if (remove.Count == 0) return CommerceResult.Fail("empty");
        var all = await CellsAsync(db, parcelId, ct);
        if (!remove.IsSubsetOf(all)) return CommerceResult.Fail("not_in_parcel");
        var rest = new HashSet<long>(all); rest.ExceptWith(remove);
        if (rest.Count == 0) return CommerceResult.Fail("whole_parcel");
        if (!LandGrid.IsContiguous(rest)) return CommerceResult.Fail("not_contiguous");
        var (minX, minZ, maxX, maxZ) = LandGrid.Bounds(all);
        var positions = await db.BuildObjects.AsNoTracking().Where(o => o.IslandId == parcel.IslandId && o.DeletedAt == null && o.PositionX >= minX && o.PositionX < maxX && o.PositionZ >= minZ && o.PositionZ < maxZ).Select(o => new { o.PositionX, o.PositionZ }).ToListAsync(ct);
        if (positions.Any(o => remove.Contains(LandGrid.Key(LandGrid.CellOf(o.PositionX), LandGrid.CellOf(o.PositionZ))))) return CommerceResult.Fail("has_objects");

        var now = clock.GetUtcNow();
        foreach (var row in await db.ParcelCells.Where(c => c.ParcelId == parcelId).ToListAsync(ct)) if (remove.Contains(LandGrid.Key(row.Cx, row.Cz))) db.ParcelCells.Remove(row);
        ClearOffers(parcel); Reshape(parcel, rest);
        var (rMinX, rMinZ, rMaxX, rMaxZ) = LandGrid.Bounds(rest);
        if (parcel.SpawnX < rMinX || parcel.SpawnX >= rMaxX || parcel.SpawnZ < rMinZ || parcel.SpawnZ >= rMaxZ || !rest.Contains(LandGrid.Key(LandGrid.CellOf(parcel.SpawnX), LandGrid.CellOf(parcel.SpawnZ))))
        { var first = rest.OrderBy(k => k).First(); var (fx, fz) = LandGrid.Unkey(first); parcel.SpawnX = (float)((fx + .5) * LandGrid.CellSize); parcel.SpawnZ = (float)((fz + .5) * LandGrid.CellSize); }
        Log(db, parcel, actor, "shrink", new { removed = remove.Count, cells = rest.Count }, now);
        return await SaveAsync(db, ct, new { parcelId, cells = rest.Count, area = parcel.Area, maxObjects = parcel.MaxObjects });
    }

    // ---- joining ---------------------------------------------------------------------------------------------

    /// <summary>Joins parcels of one owner that touch. When their settings differ the owner says which parcel's settings win.</summary>
    public static async Task<CommerceResult> MergeAsync(LifeVerseDbContext db, IReadOnlyCollection<Guid> parcelIds, Guid actor, Guid? primaryId, TimeProvider clock, CancellationToken ct)
    {
        var ids = parcelIds.Distinct().ToList();
        if (ids.Count is < 2 or > 20) return CommerceResult.Fail("bad_selection");
        var parcels = await db.IslandParcels.Where(p => ids.Contains(p.Id) && p.DeletedAt == null).OrderBy(p => p.CreatedAt).ToListAsync(ct);
        if (parcels.Count != ids.Count) return CommerceResult.Fail("not_found");
        if (parcels.Select(p => p.IslandId).Distinct().Count() != 1) return CommerceResult.Fail("other_island");
        foreach (var each in parcels) if (!await LandAuthority.CanManageAsync(db, each, actor, ct)) return CommerceResult.Fail("forbidden");
        if (parcels.Select(p => p.GroupId).Distinct().Count() > 1) return CommerceResult.Fail("mixed_owners"); // a parcel of a group never joins a personal one
        foreach (var parcel in parcels) if (await LiveRentalAsync(db, parcel.Id, ct)) return CommerceResult.Fail("rented");
        var union = new HashSet<long>();
        foreach (var parcel in parcels) { var cells = await CellsAsync(db, parcel.Id, ct); if (cells.Count == 0) return CommerceResult.Fail("no_cells"); union.UnionWith(cells); }
        if (!LandGrid.IsContiguous(union)) return CommerceResult.Fail("not_adjacent");

        var conflict = parcels.Select(p => (p.PermissionsJson, p.EnvironmentJson, p.AccessMode, p.BuildMode, p.AutoReturnMinutes)).Distinct().Count() > 1;
        IslandParcel primary;
        if (primaryId is { } chosen) { primary = parcels.FirstOrDefault(p => p.Id == chosen)!; if (primary is null) return CommerceResult.Fail("bad_primary"); }
        else if (conflict) return CommerceResult.Fail("primary_required");
        else primary = parcels[0];

        var now = clock.GetUtcNow();
        var others = parcels.Where(p => p.Id != primary.Id).ToList();
        var otherIds = others.Select(p => p.Id).ToList();
        var known = (await db.ParcelAccess.AsNoTracking().Where(a => a.ParcelId == primary.Id && a.RevokedAt == null).ToListAsync(ct)).Select(a => (a.UserId, a.Kind)).ToHashSet();
        foreach (var entry in await db.ParcelAccess.AsNoTracking().Where(a => otherIds.Contains(a.ParcelId) && a.RevokedAt == null).ToListAsync(ct))
            if (known.Add((entry.UserId, entry.Kind)))
                db.ParcelAccess.Add(new ParcelAccess { Id = Guid.NewGuid(), ParcelId = primary.Id, UserId = entry.UserId, Kind = entry.Kind, GrantedByUserId = entry.GrantedByUserId, Reason = entry.Reason, ExpiresAt = entry.ExpiresAt, CreatedAt = now });
        foreach (var row in await db.ParcelCells.Where(c => otherIds.Contains(c.ParcelId)).ToListAsync(ct)) row.ParcelId = primary.Id;
        primary.MaxObjects = (int)Math.Min(100000L, parcels.Sum(p => (long)p.MaxObjects));
        ClearOffers(primary); Reshape(primary, union);
        foreach (var other in others)
        {
            other.DeletedAt = now; other.UpdatedAt = now;
            Log(db, other, actor, "merged_into", new { into = primary.Id }, now);
        }
        Log(db, primary, actor, "merge", new { merged = others.Select(o => o.Id).ToArray(), cells = union.Count, area = primary.Area }, now);
        return await SaveAsync(db, ct, new { parcelId = primary.Id, merged = others.Count, area = primary.Area, maxObjects = primary.MaxObjects });
    }

    // ---- who may enter ---------------------------------------------------------------------------------------

    public static async Task<CommerceResult> SetAccessModeAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, string mode, TimeProvider clock, CancellationToken ct)
    {
        var parcel = await ParcelAsync(db, parcelId, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        if (!await LandAuthority.CanManageAsync(db, parcel, actor, ct)) return CommerceResult.Fail("forbidden");
        if (!Modes.Contains(mode)) return CommerceResult.Fail("bad_mode");
        parcel.AccessMode = mode;
        Log(db, parcel, actor, "access_mode", new { mode }, clock.GetUtcNow());
        return await SaveAsync(db, ct, new { parcelId, mode });
    }

    /// <summary>Adds someone to the list of people let in ("allow") or kept out ("ban"), for good or for a number of minutes. The owner and island administrators cannot be banned.</summary>
    public static readonly string[] BuildModes = ["owner", "list", "everyone", "nobody"];
    public static readonly int?[] AutoReturnChoices = [null, 5, 15, 30, 60];

    /// <summary>
    /// The building rules of a parcel: who may build (owner, builders list, everyone, nobody), whether visitors who build may put scripts in
    /// their objects, and after how many minutes the objects of people who may not build here are sent back (null: never; 1 to 10080).
    /// </summary>
    public static async Task<CommerceResult> SetOptionsAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, string buildMode, bool allowScripts, int? autoReturnMinutes, TimeProvider clock, CancellationToken ct, bool? allowVoice = null, bool? allowMedia = null)
    {
        var parcel = await ParcelAsync(db, parcelId, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        if (!await LandAuthority.CanManageAsync(db, parcel, actor, ct)) return CommerceResult.Fail("forbidden");
        if (!BuildModes.Contains(buildMode)) return CommerceResult.Fail("bad_mode");
        if (autoReturnMinutes is { } minutes && (minutes < 1 || minutes > 10_080)) return CommerceResult.Fail("bad_duration");
        parcel.BuildMode = buildMode; parcel.AutoReturnMinutes = autoReturnMinutes;
        if (allowVoice is { } voice) parcel.AllowVoice = voice; if (allowMedia is { } media) parcel.AllowMedia = media; // left alone when the request does not say
        // the old "build" and "run_scripts" keys keep meaning the same to everything that reads them
        var permissions = System.Text.Json.Nodes.JsonNode.Parse(parcel.PermissionsJson) as System.Text.Json.Nodes.JsonObject ?? [];
        permissions["build"] = buildMode == "everyone"; permissions["run_scripts"] = allowScripts;
        parcel.PermissionsJson = permissions.ToJsonString();
        Log(db, parcel, actor, "build_options", new { buildMode, allowScripts, autoReturnMinutes, voice = parcel.AllowVoice, media = parcel.AllowMedia }, clock.GetUtcNow());
        return await SaveAsync(db, ct, new { parcelId, buildMode, allowScripts, autoReturnMinutes, allowVoice = parcel.AllowVoice, allowMedia = parcel.AllowMedia });
    }

    public static async Task<CommerceResult> AddAccessAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, Guid userId, string kind, int? minutes, string? reason, TimeProvider clock, CancellationToken ct, string operation = "access_add")
    {
        var parcel = await ParcelAsync(db, parcelId, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        if (!await LandAuthority.CanManageAsync(db, parcel, actor, ct)) return CommerceResult.Fail("forbidden");
        if (kind is not ("allow" or "ban" or "build")) return CommerceResult.Fail("bad_kind");
        if (userId == actor || userId == parcel.OwnerUserId) return CommerceResult.Fail("bad_target");
        if (minutes is { } m && (m < 1 || m > 525_600)) return CommerceResult.Fail("bad_duration");
        var island = await db.Islands.AsNoTracking().SingleAsync(i => i.Id == parcel.IslandId, ct);
        if (kind == "ban" && (island.OwnerUserId == userId || await db.IslandPermissions.AnyAsync(p => p.IslandId == island.Id && p.UserId == userId && p.RevokedAt == null && p.Permission == "admin", ct))) return CommerceResult.Fail("protected");
        var now = clock.GetUtcNow();
        var current = await db.ParcelAccess.Where(a => a.ParcelId == parcelId && a.RevokedAt == null).ToListAsync(ct);
        // a person is on one list at a time for entering (allowed or banned) and on the builders list or not
        var sameDimension = kind == "build" ? (Func<ParcelAccess, bool>)(a => a.Kind == "build") : a => a.Kind != "build";
        foreach (var old in current.Where(a => a.UserId == userId && sameDimension(a))) old.RevokedAt = now;
        if (current.Count(a => a.Kind == kind && a.UserId != userId && a.RevokedAt == null) >= MaxEntriesPerKind) return CommerceResult.Fail("list_full");
        db.ParcelAccess.Add(new ParcelAccess { Id = Guid.NewGuid(), ParcelId = parcelId, UserId = userId, Kind = kind, GrantedByUserId = actor, Reason = (reason ?? string.Empty).Trim()[..Math.Min(200, (reason ?? string.Empty).Trim().Length)], ExpiresAt = minutes is { } mm ? now.AddMinutes(mm) : null, CreatedAt = now });
        Log(db, parcel, actor, operation, new { userId, kind, minutes }, now);
        return await SaveAsync(db, ct, new { parcelId, userId, kind });
    }

    /// <summary>Sends a person out of the parcel: a one-minute ban, so the island pushes them out and they cannot walk straight back in.</summary>
    public static Task<CommerceResult> EjectAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, Guid userId, TimeProvider clock, CancellationToken ct) =>
        AddAccessAsync(db, parcelId, actor, userId, "ban", (int)EjectBan.TotalMinutes, "expulso", clock, ct, "eject");

    public static async Task<CommerceResult> RemoveAccessAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, Guid entryId, TimeProvider clock, CancellationToken ct)
    {
        var parcel = await ParcelAsync(db, parcelId, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        if (!await LandAuthority.CanManageAsync(db, parcel, actor, ct)) return CommerceResult.Fail("forbidden");
        var entry = await db.ParcelAccess.SingleOrDefaultAsync(a => a.Id == entryId && a.ParcelId == parcelId && a.RevokedAt == null, ct);
        if (entry is null) return CommerceResult.Fail("not_found");
        var now = clock.GetUtcNow(); entry.RevokedAt = now;
        Log(db, parcel, actor, "access_remove", new { entry.UserId, entry.Kind }, now);
        return await SaveAsync(db, ct);
    }

    public static async Task<AccessView?> AccessAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, TimeProvider clock, CancellationToken ct)
    {
        var parcel = await db.IslandParcels.AsNoTracking().SingleOrDefaultAsync(p => p.Id == parcelId && p.DeletedAt == null, ct);
        if (parcel is null || !await LandAuthority.CanManageAsync(db, parcel, actor, ct)) return null;
        var now = clock.GetUtcNow();
        var entries = await db.ParcelAccess.AsNoTracking().Where(a => a.ParcelId == parcelId && a.RevokedAt == null && (a.ExpiresAt == null || a.ExpiresAt > now)).OrderBy(a => a.Kind).ThenBy(a => a.CreatedAt).ToListAsync(ct);
        var names = await NamesAsync(db, entries.Select(e => e.UserId), ct);
        return new AccessView(parcel.AccessMode, [.. entries.Select(e => new AccessEntryView(e.Id, e.UserId, names.GetValueOrDefault(e.UserId, "—"), e.Kind, e.Reason, e.ExpiresAt, e.CreatedAt))]);
    }

    public static async Task<Dictionary<Guid, string>> NamesAsync(LifeVerseDbContext db, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.Username }).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        foreach (var avatar in await db.Avatars.AsNoTracking().Where(a => ids.Contains(a.UserId)).Select(a => new { a.UserId, a.DisplayName }).ToListAsync(ct)) names[avatar.UserId] = avatar.DisplayName;
        return names;
    }

    // ---- what stands on the land -----------------------------------------------------------------------------

    private static async Task<(IslandParcel Parcel, Island Island, HashSet<long> Cells, bool Admin)?> ManagedAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, CancellationToken ct)
    {
        var parcel = await db.IslandParcels.AsNoTracking().SingleOrDefaultAsync(p => p.Id == parcelId && p.DeletedAt == null, ct); if (parcel is null) return null;
        var island = await db.Islands.AsNoTracking().SingleAsync(i => i.Id == parcel.IslandId, ct);
        var admin = island.OwnerUserId == actor || await db.IslandPermissions.AnyAsync(p => p.IslandId == island.Id && p.UserId == actor && p.RevokedAt == null && p.Permission == "admin", ct);
        if (!admin && !await LandAuthority.CanManageAsync(db, parcel, actor, ct)) return null;
        return (parcel, island, await CellsAsync(db, parcelId, ct), admin);
    }

    internal static async Task<List<BuildObject>> ObjectsOnAsync(LifeVerseDbContext db, IslandParcel parcel, HashSet<long> cells, CancellationToken ct)
    {
        if (cells.Count == 0) return [];
        var (minX, minZ, maxX, maxZ) = LandGrid.Bounds(cells);
        var inBox = await db.BuildObjects.AsNoTracking().Where(o => o.IslandId == parcel.IslandId && o.DeletedAt == null && o.PositionX >= minX && o.PositionX < maxX && o.PositionZ >= minZ && o.PositionZ < maxZ).ToListAsync(ct);
        return [.. inBox.Where(o => cells.Contains(LandGrid.Key(LandGrid.CellOf(o.PositionX), LandGrid.CellOf(o.PositionZ))))];
    }

    private static bool HasScripts(string contentJson) => contentJson.Contains("\"scripts\"", StringComparison.Ordinal);

    /// <summary>The objects on a parcel, for its owner and island administrators: who made them, who owns them, what they cost.</summary>
    public static async Task<IReadOnlyList<ParcelObjectView>?> ObjectsAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, TimeProvider clock, CancellationToken ct)
    {
        if (await ManagedAsync(db, parcelId, actor, ct) is not { } managed) return null;
        var objects = (await ObjectsOnAsync(db, managed.Parcel, managed.Cells, ct)).OrderByDescending(o => o.CreatedAt).Take(500).ToList();
        var authorized = await AuthorizedAsync(db, managed.Parcel, clock.GetUtcNow(), ct);
        var names = await NamesAsync(db, objects.Select(o => o.OwnerUserId), ct);
        return [.. objects.Select(o => new ParcelObjectView(o.Id, o.Name, o.OwnerUserId, names.GetValueOrDefault(o.OwnerUserId, "—"), o.PrimitiveType, o.Cost, HasScripts(o.ContentJson), o.PositionX, o.PositionY, o.PositionZ, o.CreatedAt, authorized.Contains(o.OwnerUserId)))];
    }

    /// <summary>People whose objects belong on the parcel: its owner, its tenant and everyone on the allow list.</summary>
    internal static async Task<HashSet<Guid>> AuthorizedAsync(LifeVerseDbContext db, IslandParcel parcel, DateTimeOffset now, CancellationToken ct)
    {
        var set = new HashSet<Guid> { parcel.OwnerUserId };
        foreach (var tenant in await db.ParcelRentals.AsNoTracking().Where(r => r.ParcelId == parcel.Id && r.Status == "active").Select(r => r.TenantUserId).ToListAsync(ct)) set.Add(tenant);
        foreach (var allowed in await db.ParcelAccess.AsNoTracking().Where(a => a.ParcelId == parcel.Id && (a.Kind == "allow" || a.Kind == "build") && a.RevokedAt == null && (a.ExpiresAt == null || a.ExpiresAt > now)).Select(a => a.UserId).ToListAsync(ct)) set.Add(allowed);
        return set;
    }

    /// <summary>
    /// Sends objects back to their owners' inventories (saved as constructions, nothing is destroyed), with the reason on record. "ids" returns
    /// the objects named; "others" returns everything that belongs to someone who is not the owner, the tenant or on the allow list.
    /// </summary>
    public static async Task<CommerceResult> ReturnObjectsAsync(LifeVerseDbContext db, AssetStorageService storage, Guid parcelId, Guid actor, string scope, IReadOnlyCollection<Guid>? ids, string? reason, TimeProvider clock, CancellationToken ct)
    {
        if (await ManagedAsync(db, parcelId, actor, ct) is not { } managed) return CommerceResult.Fail("forbidden");
        reason = (reason ?? string.Empty).Trim(); if (reason.Length is < 1 or > 200) return CommerceResult.Fail("bad_reason");
        var now = clock.GetUtcNow();
        var here = await ObjectsOnAsync(db, managed.Parcel, managed.Cells, ct);
        List<BuildObject> chosen;
        if (scope == "ids") { var wanted = (ids ?? []).ToHashSet(); chosen = [.. here.Where(o => wanted.Contains(o.Id))]; }
        else if (scope == "others") { var ok = await AuthorizedAsync(db, managed.Parcel, now, ct); chosen = [.. here.Where(o => !ok.Contains(o.OwnerUserId))]; }
        else return CommerceResult.Fail("bad_scope");
        if (chosen.Count == 0) return CommerceResult.Fail("nothing_to_return");
        var returned = 0;
        foreach (var batch in chosen.Chunk(BuildPackageLimits.MaxReturnAtOnce))
        {
            var result = await BuildOperations.ReturnForLandAsync(managed.Island, actor, [.. batch.Select(o => o.Id)], db, storage, now, ct);
            if ((result as IStatusCodeHttpResult)?.StatusCode is { } status && status >= 400) return CommerceResult.Fail("conflict");
            returned += batch.Length;
        }
        var parcel = await db.IslandParcels.SingleAsync(p => p.Id == parcelId, ct);
        Log(db, parcel, actor, "return_objects", new { scope, count = returned, reason }, now);
        return await SaveAsync(db, ct, new { returned });
    }

    // ---- the history -----------------------------------------------------------------------------------------

    public static async Task<IReadOnlyList<AuditEntryView>?> AuditAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, CancellationToken ct)
    {
        if (await ManagedAsync(db, parcelId, actor, ct) is null) return null;
        var changes = await db.ParcelChanges.AsNoTracking().Where(c => c.ParcelId == parcelId).OrderByDescending(c => c.CreatedAt).Take(100).ToListAsync(ct);
        var names = await NamesAsync(db, changes.Select(c => c.ActorUserId), ct);
        return [.. changes.Select(c => new AuditEntryView(c.Operation, names.GetValueOrDefault(c.ActorUserId, "—"), c.PayloadJson.Length > 300 ? c.PayloadJson[..300] : c.PayloadJson, c.CreatedAt))];
    }
}
