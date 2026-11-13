using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

public sealed record ClaimResult(IslandParcel? Parcel, string? Error, Guid? ConflictParcelId = null);

public sealed record LandParcelView(Guid Id, string Name, string Description, Guid OwnerUserId, string OwnerName, bool Mine, string State, double Area, int Cells, int MaxObjects, int MaxCost,
    int Objects, int Cost, bool PublicBuild, bool CanEnter, int Revision, int[][] Runs, float SpawnX, float SpawnY, float SpawnZ, DateTimeOffset CreatedAt,
    long? SalePrice = null, bool SaleIncludesObjects = true, string SaleDescription = "", bool SaleExclusive = false,
    long? RentPrice = null, int RentPeriodDays = 0, int RentMinPeriods = 0, int RentMaxPeriods = 0, long RentDeposit = 0, int RentGraceDays = 0, bool RentAutoRenewAllowed = false,
    Guid? RentalId = null, string? TenantName = null, bool TenantIsMe = false, string? RentStatus = null, DateTimeOffset? RentExpiresAt = null, long RentNextCharge = 0, bool RentAutoRenew = false, string AccessMode = "public",
    string BuildMode = "owner", bool AllowScripts = false, int? AutoReturnMinutes = null, int OthersObjects = 0, int OthersCost = 0, int Builders = 0, bool CanEditTerrain = false,
    double? ArrivalX = null, double? ArrivalZ = null, float ArrivalYaw = 0, string TeleportMode = "free", string Environment = "{}", string Media = "{}", bool EnvironmentAllowed = true,
    Guid? GroupId = null, string? GroupName = null, bool AllowVoice = true, bool AllowMedia = true);

public sealed record LandView(Guid IslandId, int Size, int CellSize, long Version, IReadOnlyList<LandParcelView> Parcels);

/// <summary>
/// The rules of the territorial grid on the server: who may take which cells, that two parcels never share a cell and that a parcel stays in
/// one piece. The client only draws and asks; everything is checked again here.
/// </summary>
public static class LandService
{
    /// <summary>The parcels of an island as the build rules see them, with their cells when they have any.</summary>
    public static async Task<List<ParcelFacts>> LoadFactsAsync(LifeVerseDbContext db, Guid islandId, CancellationToken ct)
    {
        var parcels = await db.IslandParcels.AsNoTracking().Where(p => p.IslandId == islandId && p.DeletedAt == null).ToListAsync(ct);
        var cells = await db.ParcelCells.AsNoTracking().Where(c => c.IslandId == islandId).Select(c => new { c.ParcelId, c.Cx, c.Cz }).ToListAsync(ct);
        var byParcel = cells.GroupBy(c => c.ParcelId).ToDictionary(g => g.Key, g => (IReadOnlySet<long>)g.Select(c => LandGrid.Key(c.Cx, c.Cz)).ToHashSet());
        var parcelIds = parcels.Select(p => p.Id).ToList();
        var tenants = await db.ParcelRentals.AsNoTracking().Where(r => r.Status == "active" && parcelIds.Contains(r.ParcelId)).ToDictionaryAsync(r => r.ParcelId, r => r.TenantUserId, ct);
        var limits = await db.Islands.AsNoTracking().Where(i => i.Id == islandId).Select(i => i.ResourceLimitsJson).SingleOrDefaultAsync(ct);
        var nowFacts = TimeProvider.System.GetUtcNow();
        var entries = (await db.ParcelAccess.AsNoTracking().Where(a => parcelIds.Contains(a.ParcelId) && a.RevokedAt == null && (a.ExpiresAt == null || a.ExpiresAt > nowFacts)).Select(a => new { a.ParcelId, a.UserId, a.Kind }).ToListAsync(ct)).ToLookup(a => a.ParcelId);
        var areaById = parcels.ToDictionary(p => p.Id, p => p.Area);
        var people = await LandAuthority.PeopleAsync(db, parcels, ct);
        ParcelFacts WithRules(ParcelFacts f)
        {
            var mine = entries[f.Id].ToList(); var area = areaById[f.Id];
            people.TryGetValue(f.Id, out var crowd);
            return f with { Builders = mine.Where(a => a.Kind == "build").Select(a => a.UserId).ToHashSet(), Banned = mine.Where(a => a.Kind == "ban").Select(a => a.UserId).ToHashSet(), CostBudget = LandBudget.CostBudget(limits, area), Managers = crowd?.Managers, GroupBuilders = crowd?.Builders };
        }
        return [.. parcels.Select(ParcelFacts.From).OfType<ParcelFacts>().Select(f => (byParcel.TryGetValue(f.Id, out var set) ? f with { Cells = set } : f) with { TenantUserId = tenants.TryGetValue(f.Id, out var tenant) ? tenant : null }).Select(WithRules)];
    }

    /// <summary>
    /// Who may reshape the ground of a parcel: the owner, the tenant and the people on the builders list; not people banned from it, and nobody
    /// where the parcel is closed to building (only island administrators then).
    /// </summary>
    public static List<Guid> TerrainEditors(IslandParcel parcel, Guid? tenant, IReadOnlyCollection<ParcelAccess> entries, IEnumerable<Guid>? groupPeople = null)
    {
        if (parcel.BuildMode == "nobody") return [];
        var banned = entries.Where(e => e.Kind == "ban").Select(e => e.UserId).ToHashSet();
        var editors = new List<Guid> { parcel.OwnerUserId };
        if (tenant is { } t) editors.Add(t);
        editors.AddRange(entries.Where(e => e.Kind == "build").Select(e => e.UserId));
        if (groupPeople is not null) editors.AddRange(groupPeople);
        return [.. editors.Where(e => e == parcel.OwnerUserId || !banned.Contains(e)).Distinct()];
    }

    /// <summary>How many objects stand in a parcel and what they cost (an object being edited can be left out of the count).</summary>
    public static async Task<(int Objects, int Cost)> UsageAsync(LifeVerseDbContext db, Guid islandId, ParcelFacts parcel, Guid excluded, CancellationToken ct)
    {
        var inBox = db.BuildObjects.AsNoTracking().Where(x => x.IslandId == islandId && x.DeletedAt == null && x.Id != excluded && x.PositionX >= parcel.MinX && x.PositionX < parcel.MaxX && x.PositionZ >= parcel.MinZ && x.PositionZ < parcel.MaxZ);
        if (parcel.Cells is null) return (await inBox.CountAsync(ct), await inBox.SumAsync(x => (int?)x.Cost, ct) ?? 0);
        var rows = await inBox.Select(x => new { x.PositionX, x.PositionZ, x.Cost }).ToListAsync(ct);
        var inside = rows.Where(r => parcel.Contains(r.PositionX, r.PositionZ)).ToList();
        return (inside.Count, inside.Sum(r => r.Cost));
    }

    /// <summary>Takes these cells for a new parcel. Null parcel and an error code when it cannot be done: empty, too_big, outside_island, not_contiguous, bad_name, name_taken, overlap.</summary>
    public static async Task<ClaimResult> ClaimAsync(LifeVerseDbContext db, Island island, Guid owner, string name, string? description, IReadOnlyCollection<long> cells, int maxObjects,
        string? permissionsJson, string? environmentJson, float? spawnX, float? spawnY, float? spawnZ, TimeProvider clock, CancellationToken ct)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0) return new ClaimResult(null, "bad_name");
        if (name.Length > 120) name = name[..120];
        if (maxObjects is < 1 or > 100000) return new ClaimResult(null, "bad_limit");
        // the parcels of an island share its object limit: they can never add up to more than the island allows
        var allocated = await db.IslandParcels.Where(p => p.IslandId == island.Id && p.DeletedAt == null).SumAsync(p => (int?)p.MaxObjects, ct) ?? 0;
        if (allocated + maxObjects > LandBudget.IslandObjects(island)) return new ClaimResult(null, "budget_exceeded");
        if (cells.Count == 0) return new ClaimResult(null, "empty");
        if (cells.Count > LandGrid.MaxCellsPerParcel) return new ClaimResult(null, "too_big");
        var set = cells as HashSet<long> ?? [.. cells];
        foreach (var key in set) { var (cx, cz) = LandGrid.Unkey(key); if (!LandGrid.InsideIsland(cx, cz, island.Size)) return new ClaimResult(null, "outside_island"); }
        if (!LandGrid.IsContiguous(set)) return new ClaimResult(null, "not_contiguous");
        if (await db.IslandParcels.AnyAsync(x => x.IslandId == island.Id && x.Name == name && x.DeletedAt == null, ct)) return new ClaimResult(null, "name_taken");

        var (minX, minZ, maxX, maxZ) = LandGrid.Bounds(set);
        var (minCx, minCz) = (LandGrid.CellOf(minX), LandGrid.CellOf(minZ)); var (maxCx, maxCz) = (LandGrid.CellOf(maxX) - 1, LandGrid.CellOf(maxZ) - 1);
        var taken = await db.ParcelCells.AsNoTracking().Where(c => c.IslandId == island.Id && c.Cx >= minCx && c.Cx <= maxCx && c.Cz >= minCz && c.Cz <= maxCz).ToListAsync(ct);
        var clash = taken.FirstOrDefault(c => set.Contains(LandGrid.Key(c.Cx, c.Cz)));
        if (clash is not null) return new ClaimResult(null, "overlap", clash.ParcelId);

        var now = clock.GetUtcNow();
        var parcel = new IslandParcel
        {
            Id = Guid.NewGuid(), IslandId = island.Id, OwnerUserId = owner, Name = name, Description = (description ?? string.Empty)[..Math.Min(4000, (description ?? string.Empty).Length)],
            BoundsJson = JsonSerializer.Serialize(new { minX, minZ, maxX, maxZ }), Area = LandGrid.AreaOf(set.Count), MaxObjects = maxObjects,
            PermissionsJson = ValidObject(permissionsJson, "{\"enter\":true,\"build\":false,\"run_scripts\":false,\"rezzing\":false}"), EnvironmentJson = ValidObject(environmentJson, "{}"),
            SpawnX = spawnX ?? (float)((minX + maxX) / 2), SpawnY = spawnY ?? 1f, SpawnZ = spawnZ ?? (float)((minZ + maxZ) / 2), CreatedAt = now, UpdatedAt = now
        };
        if (island.RulesJson is { Length: > 2 } && permissionsJson is null) IslandRules.Apply(parcel, IslandRules.Read(island)); // the island's house rules are where new land starts, unless the claim brings its own
        db.IslandParcels.Add(parcel);
        foreach (var key in set) { var (cx, cz) = LandGrid.Unkey(key); db.ParcelCells.Add(new ParcelCell { IslandId = island.Id, Cx = cx, Cz = cz, ParcelId = parcel.Id }); }
        db.ParcelChanges.Add(new ParcelChange { Id = Guid.NewGuid(), ParcelId = parcel.Id, ActorUserId = owner, Operation = "claim", PayloadJson = JsonSerializer.Serialize(new { cells = set.Count, area = parcel.Area }), Revision = parcel.Revision, CreatedAt = now });
        try { await db.SaveChangesAsync(ct); }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException)
        {
            // someone took one of the cells between the check and the save: the key of the cell table refuses the second owner
            foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached;
            return new ClaimResult(null, "overlap");
        }
        return new ClaimResult(parcel, null);
    }

    /// <summary>Frees the cells of a parcel that is being deleted.</summary>
    public static async Task ReleaseAsync(LifeVerseDbContext db, Guid parcelId, CancellationToken ct)
    {
        db.ParcelCells.RemoveRange(await db.ParcelCells.Where(c => c.ParcelId == parcelId).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The land version: moves when a parcel is added, removed or changed. Same formula as <see cref="LandView.Version"/>.</summary>
    public static async Task<long> VersionAsync(LifeVerseDbContext db, Guid islandId, CancellationToken ct)
    {
        var rows = await db.IslandParcels.AsNoTracking().Where(p => p.IslandId == islandId && p.DeletedAt == null).Select(p => p.UpdatedAt).ToListAsync(ct);
        return rows.Count == 0 ? 0 : rows.Max(t => t.UtcTicks) + rows.Count;
    }

    /// <summary>The parcels of an island as one person sees them: who owns what, how much is built, and the cells in compact rows.</summary>
    public static async Task<LandView> ViewAsync(LifeVerseDbContext db, Island island, Guid viewer, CancellationToken ct)
    {
        var facts = await LoadFactsAsync(db, island.Id, ct);
        var parcels = await db.IslandParcels.AsNoTracking().Where(p => p.IslandId == island.Id && p.DeletedAt == null).ToListAsync(ct);
        var parcelIds = parcels.Select(p => p.Id).ToList();
        var ownerIds = parcels.Select(p => p.OwnerUserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ownerIds.Contains(u.Id)).Select(u => new { u.Id, u.Username }).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        foreach (var avatar in await db.Avatars.AsNoTracking().Where(a => ownerIds.Contains(a.UserId)).Select(a => new { a.UserId, a.DisplayName }).ToListAsync(ct)) names[avatar.UserId] = avatar.DisplayName;
        var rentals = await db.ParcelRentals.AsNoTracking().Where(r => (r.Status == "active" || r.Status == "grace") && parcelIds.Contains(r.ParcelId)).ToListAsync(ct);
        var tenantIds = rentals.Select(r => r.TenantUserId).Distinct().ToList();
        foreach (var user in await db.Users.AsNoTracking().Where(u => tenantIds.Contains(u.Id)).Select(u => new { u.Id, u.Username }).ToListAsync(ct)) names.TryAdd(user.Id, user.Username);
        foreach (var avatar in await db.Avatars.AsNoTracking().Where(a => tenantIds.Contains(a.UserId)).Select(a => new { a.UserId, a.DisplayName }).ToListAsync(ct)) names[avatar.UserId] = avatar.DisplayName;
        var nowAccess = TimeProvider.System.GetUtcNow();
        var mineAccess = await db.ParcelAccess.AsNoTracking().Where(a => a.UserId == viewer && parcelIds.Contains(a.ParcelId) && a.RevokedAt == null && (a.ExpiresAt == null || a.ExpiresAt > nowAccess)).ToListAsync(ct);
        var objects = await db.BuildObjects.AsNoTracking().Where(o => o.IslandId == island.Id && o.DeletedAt == null).Select(o => new { o.PositionX, o.PositionZ, o.Cost, o.OwnerUserId }).ToListAsync(ct);
        var factById = facts.ToDictionary(f => f.Id);
        var rentalByParcel = rentals.GroupBy(r => r.ParcelId).ToDictionary(g => g.Key, g => g.First());
        // which objects stand in which parcel: one lookup per object instead of testing every parcel for every object
        var cellParcel = new Dictionary<long, Guid>();
        foreach (var f in facts) if (f.Cells is { } own) foreach (var key in own) cellParcel[key] = f.Id;
        var legacy = facts.Where(f => f.Cells is null).ToList();
        var insideBy = facts.ToDictionary(f => f.Id, _ => new List<(float PositionX, float PositionZ, int Cost, Guid OwnerUserId)>());
        foreach (var o in objects)
        {
            if (cellParcel.TryGetValue(LandGrid.Key(LandGrid.CellOf(o.PositionX), LandGrid.CellOf(o.PositionZ)), out var owner)) insideBy[owner].Add((o.PositionX, o.PositionZ, o.Cost, o.OwnerUserId));
            else foreach (var f in legacy) if (f.Contains(o.PositionX, o.PositionZ)) { insideBy[f.Id].Add((o.PositionX, o.PositionZ, o.Cost, o.OwnerUserId)); break; }
        }
        var people = await LandAuthority.PeopleAsync(db, parcels, ct);
        var groupIdsHere = parcels.Where(p => p.GroupId != null).Select(p => p.GroupId!.Value).Distinct().ToList();
        var groupNames = await db.SocialGroups.AsNoTracking().Where(g => groupIdsHere.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var views = new List<LandParcelView>();
        var parcelEnvironment = LandPresentation.IslandAllowsParcelEnvironment(island.EnvironmentJson);
        foreach (var parcel in parcels.OrderBy(p => p.Name))
        {
            if (!factById.TryGetValue(parcel.Id, out var fact)) continue;
            var inside = insideBy[parcel.Id];
            var cells = fact.Cells ?? LandGrid.CellsInRect(fact.MinX, fact.MinZ, fact.MaxX, fact.MaxZ);
            rentalByParcel.TryGetValue(parcel.Id, out var rentalHere);
            people.TryGetValue(parcel.Id, out var crowd);
            var ownerSide = parcel.GroupId is null ? parcel.OwnerUserId == viewer : crowd?.Members.Contains(viewer) == true; var guest = crowd?.Guests.Contains(viewer) == true;
            var closedToMe = !ownerSide && rentalHere?.TenantUserId != viewer && (mineAccess.Any(a => a.ParcelId == parcel.Id && a.Kind == "ban") || parcel.AccessMode == "private" || (parcel.AccessMode == "list" && !guest && !mineAccess.Any(a => a.ParcelId == parcel.Id && a.Kind == "allow")));
            var arrivalSet = cells.Contains(LandGrid.Key(LandGrid.CellOf(parcel.SpawnX), LandGrid.CellOf(parcel.SpawnZ)));
            var enter = EnterAllowed(parcel.PermissionsJson) && !closedToMe;
            var mine = parcel.GroupId is null ? parcel.OwnerUserId == viewer : crowd?.Managers.Contains(viewer) == true;
            var rental = rentalHere;
            var forSale = parcel.SalePrice is not null && (parcel.SaleBuyerUserId is null || parcel.SaleBuyerUserId == viewer);
            var forRent = parcel.RentPrice is not null && rental is null;
            var state = mine ? "own" : forSale ? "sale" : forRent ? "rent" : rental?.TenantUserId == viewer ? "rented_by_me" : fact.PublicBuild ? "public" : !enter ? "restricted" : "owned";
            views.Add(new LandParcelView(parcel.Id, parcel.Name, parcel.Description, parcel.OwnerUserId, parcel.GroupId is { } gid && groupNames.TryGetValue(gid, out var gname) ? gname : names.GetValueOrDefault(parcel.OwnerUserId, "—"), mine, state, parcel.Area, cells.Count, parcel.MaxObjects, fact.MaxCost,
                inside.Count, inside.Sum(o => o.Cost), fact.PublicBuild, enter, parcel.Revision, LandGrid.Runs(cells), parcel.SpawnX, parcel.SpawnY, parcel.SpawnZ, parcel.CreatedAt,
                parcel.SalePrice, parcel.SaleIncludesObjects, parcel.SaleDescription, parcel.SaleBuyerUserId is not null,
                parcel.RentPrice, parcel.RentPeriodDays, parcel.RentMinPeriods, parcel.RentMaxPeriods, parcel.RentDeposit, parcel.RentGraceDays, parcel.RentAutoRenewAllowed,
                rental?.Id, rental is null ? null : names.GetValueOrDefault(rental.TenantUserId, "—"), rental?.TenantUserId == viewer, rental?.Status, rental?.ExpiresAt, rental?.PricePerPeriod ?? 0, rental?.AutoRenew ?? false, parcel.AccessMode,
                parcel.BuildMode, fact.AllowScripts, parcel.AutoReturnMinutes, inside.Count(o => o.OwnerUserId != parcel.OwnerUserId), inside.Where(o => o.OwnerUserId != parcel.OwnerUserId).Sum(o => o.Cost), fact.Builders?.Count ?? 0,
                TerrainEditors(parcel, rental?.TenantUserId, mineAccess.Where(a => a.ParcelId == parcel.Id).Select(a => new ParcelAccess { Id = a.Id, ParcelId = a.ParcelId, UserId = a.UserId, Kind = a.Kind, GrantedByUserId = a.GrantedByUserId, CreatedAt = a.CreatedAt }).ToList(), crowd is null ? null : [.. crowd.Managers, .. crowd.Builders]).Contains(viewer),
                arrivalSet ? parcel.SpawnX : null, arrivalSet ? parcel.SpawnZ : null, parcel.ArrivalYaw, parcel.TeleportMode, parcel.EnvironmentJson, parcel.MediaJson, parcelEnvironment,
                parcel.GroupId, parcel.GroupId is { } g2 ? groupNames.GetValueOrDefault(g2) : null, parcel.AllowVoice, parcel.AllowMedia));
        }
        var version = parcels.Count == 0 ? 0 : parcels.Max(p => p.UpdatedAt.UtcTicks) + parcels.Count;
        return new LandView(island.Id, island.Size, LandGrid.CellSize, version, views);
    }

    private static bool EnterAllowed(string permissionsJson)
    {
        try { using var doc = JsonDocument.Parse(permissionsJson); return !doc.RootElement.TryGetProperty("enter", out var v) || v.ValueKind != JsonValueKind.False; }
        catch (JsonException) { return true; }
    }

    private static string ValidObject(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try { using var doc = JsonDocument.Parse(value); return doc.RootElement.ValueKind == JsonValueKind.Object ? value : fallback; } catch (JsonException) { return fallback; }
    }
}

/// <summary>
/// Parcels that were drawn as rectangles before the grid existed get their cells once, in the order they were created. A cell already taken
/// by an older parcel stays with it, so overlapping legacy parcels end up sharing nothing. A parcel left with no cell keeps working by its rectangle.
/// </summary>
public static class ParcelCellBackfill
{
    public static async Task<int> RunAsync(LifeVerseDbContext db, CancellationToken ct = default)
    {
        var done = 0;
        var withCells = await db.ParcelCells.Select(c => c.ParcelId).Distinct().ToListAsync(ct);
        var pending = await db.IslandParcels.Where(p => p.DeletedAt == null && !withCells.Contains(p.Id)).OrderBy(p => p.CreatedAt).ToListAsync(ct);
        foreach (var group in pending.GroupBy(p => p.IslandId))
        {
            var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(i => i.Id == group.Key, ct);
            if (island is null) continue;
            var taken = (await db.ParcelCells.AsNoTracking().Where(c => c.IslandId == island.Id).Select(c => new { c.Cx, c.Cz }).ToListAsync(ct)).Select(c => LandGrid.Key(c.Cx, c.Cz)).ToHashSet();
            foreach (var parcel in group)
            {
                ParcelFacts? facts = ParcelFacts.From(parcel);
                if (facts is null) continue;
                var cells = LandGrid.CellsInRect(facts.MinX, facts.MinZ, facts.MaxX, facts.MaxZ).Where(k => !taken.Contains(k) && LandGrid.InsideIsland(LandGrid.Unkey(k).Cx, LandGrid.Unkey(k).Cz, island.Size)).ToList();
                if (cells.Count == 0) continue;
                foreach (var key in cells) { var (cx, cz) = LandGrid.Unkey(key); db.ParcelCells.Add(new ParcelCell { IslandId = island.Id, Cx = cx, Cz = cz, ParcelId = parcel.Id }); taken.Add(key); }
                done++;
            }
            await db.SaveChangesAsync(ct);
        }
        return done;
    }
}
