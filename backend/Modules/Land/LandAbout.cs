using System.Text.Json;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

/// <summary>
/// "Sobre este Terreno": everything one parcel has to say about itself, for whoever may look at it: what it is, who holds it and since when, what was done with the title, what is offered on it, the contract that runs on it,
/// how full it is and the rules and music that are set. What only the owner may change comes with flags, so the window shows the right buttons; the server still checks every change.
/// </summary>
public static class LandAbout
{
    private static readonly string[] TitleOperations = ["claim", "sold", "island_transfer", "split_from", "land_transfer"];

    private static (Guid? From, Guid? To, long Price) Parties(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload); var root = document.RootElement; if (root.ValueKind != JsonValueKind.Object) return (null, null, 0);
            Guid? Id(params string[] names) { foreach (var name in names) if (root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g)) return g; return null; }
            return (Id("seller", "from"), Id("buyer", "to"), root.TryGetProperty("price", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var price) ? price : 0);
        }
        catch (JsonException) { return (null, null, 0); }
    }

    public static async Task<object?> AboutAsync(LifeVerseDbContext db, Island island, Guid parcelId, Guid viewer, DateTimeOffset now, CancellationToken ct)
    {
        var parcel = await db.IslandParcels.AsNoTracking().SingleOrDefaultAsync(p => p.Id == parcelId && p.IslandId == island.Id && p.DeletedAt == null, ct); if (parcel is null) return null;
        var view = (await LandService.ViewAsync(db, island, viewer, ct)).Parcels.FirstOrDefault(p => p.Id == parcelId); if (view is null) return null;
        var canManage = await LandAuthority.CanManageAsync(db, parcel, viewer, ct); var staff = await LandOverview.IsStaffAsync(db, island, viewer, now, ct);
        var rental = await db.ParcelRentals.AsNoTracking().Where(r => r.ParcelId == parcelId && (r.Status == "active" || r.Status == "grace")).OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
        var isTenant = rental?.TenantUserId == viewer;
        var changes = await db.ParcelChanges.AsNoTracking().Where(c => c.ParcelId == parcelId && TitleOperations.Contains(c.Operation)).OrderByDescending(c => c.CreatedAt).Take(20).ToListAsync(ct);
        var sales = await db.ParcelTransactions.AsNoTracking().Where(t => t.ParcelId == parcelId && t.Type == "sale").OrderByDescending(t => t.CreatedAt).Take(20).ToListAsync(ct);
        var people = new List<Guid> { parcel.OwnerUserId }; if (rental is not null) people.AddRange([rental.TenantUserId, rental.LandlordUserId]);
        var parsed = changes.Select(c => (Change: c, Parties: Parties(c.PayloadJson))).ToList();
        people.AddRange(parsed.SelectMany(p => new[] { p.Parties.From, p.Parties.To, (Guid?)p.Change.ActorUserId }).Where(g => g is not null).Select(g => g!.Value));
        people.AddRange(sales.SelectMany(s => new[] { s.FromUserId, s.ToUserId }).Where(g => g is not null).Select(g => g!.Value));
        var names = await LandOverview.NamesAsync(db, people, ct); string Name(Guid? id) => id is { } g ? names.GetValueOrDefault(g, "—") : "—";
        var groupName = parcel.GroupId is { } gid ? await db.SocialGroups.AsNoTracking().Where(g => g.Id == gid).Select(g => g.Name).FirstOrDefaultAsync(ct) : null;
        var acquired = parsed.OrderByDescending(p => p.Change.CreatedAt).Select(p => (DateTimeOffset?)p.Change.CreatedAt).FirstOrDefault() ?? parcel.CreatedAt;
        var history = parsed.Select(p => (At: p.Change.CreatedAt, Row: (object)new { at = p.Change.CreatedAt, what = p.Change.Operation == "sold" ? "sale" : p.Change.Operation, from = p.Parties.From is { } f ? Name(f) : (string?)null, to = p.Parties.To is { } t ? Name(t) : Name(p.Change.ActorUserId), amount = p.Parties.Price }))
            .OrderByDescending(x => x.At).Take(20).Select(x => x.Row).ToList();
        object? media = null;
        try { using var doc = JsonDocument.Parse(parcel.MediaJson); if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("musicUrl", out var url) && url.ValueKind == JsonValueKind.String) media = new { musicUrl = url.GetString(), volume = doc.RootElement.TryGetProperty("volume", out var v) && v.TryGetDouble(out var vol) ? vol : .6, autoplay = !doc.RootElement.TryGetProperty("autoplay", out var a) || a.ValueKind != JsonValueKind.False, name = doc.RootElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : "" }; }
        catch (JsonException) { }
        var forMe = parcel.SaleBuyerUserId is null || parcel.SaleBuyerUserId == viewer || canManage || staff;
        return new
        {
            parcel = new
            {
                id = parcel.Id, code = parcel.Code, name = parcel.Name, description = parcel.Description, category = parcel.Category, area = parcel.Area, cells = view.Cells, imageUrl = parcel.ImageUrl, createdAt = parcel.CreatedAt, revision = parcel.Revision,
                accessMode = parcel.AccessMode, buildMode = parcel.BuildMode, allowScripts = view.AllowScripts, autoReturnMinutes = parcel.AutoReturnMinutes, teleportMode = parcel.TeleportMode, spawnX = view.ArrivalX, spawnZ = view.ArrivalZ, yaw = parcel.ArrivalYaw, media,
            },
            island = new { id = island.Id, name = island.Name, size = island.Size, coverImageUrl = island.CoverImageUrl },
            owner = new { id = parcel.OwnerUserId, name = parcel.GroupId is not null ? groupName ?? "Grupo" : Name(parcel.OwnerUserId), kind = parcel.GroupId is null ? "person" : "group", groupId = parcel.GroupId, groupName },
            acquiredAt = acquired, history,
            rights = new { canResell = canManage && parcel.GroupId is null && rental is null, canLease = canManage && parcel.GroupId is null && rental is null, ownedByIslandOwner = parcel.OwnerUserId == island.OwnerUserId },
            usage = new { objects = view.Objects, maxObjects = view.MaxObjects, cost = view.Cost, maxCost = view.MaxCost, others = view.OthersObjects, builders = view.Builders },
            sale = parcel.SalePrice is { } price && forMe ? new { price, exclusive = parcel.SaleBuyerUserId is not null, includeObjects = parcel.SaleIncludesObjects, description = parcel.SaleDescription } : null,
            rentOffer = parcel.RentPrice is { } rent && rental is null ? new { price = rent, periodDays = parcel.RentPeriodDays, minPeriods = parcel.RentMinPeriods, maxPeriods = parcel.RentMaxPeriods, deposit = parcel.RentDeposit, graceDays = parcel.RentGraceDays, autoRenewAllowed = parcel.RentAutoRenewAllowed } : null,
            rental = rental is not null && (canManage || staff || isTenant) ? new { id = rental.Id, tenant = Name(rental.TenantUserId), landlord = Name(rental.LandlordUserId), price = rental.PricePerPeriod, periodDays = rental.PeriodDays, status = rental.Status, startsAt = rental.StartsAt, expiresAt = rental.ExpiresAt, graceUntil = rental.GraceUntil, deposit = rental.DepositCoins, autoRenew = rental.AutoRenew, periods = rental.Periods } : null,
            me = new { canManage, isOwner = parcel.GroupId is null ? parcel.OwnerUserId == viewer : canManage, isTenant, isStaff = staff },
        };
    }

    /// <summary>Sets the picture of a parcel to an image the person owns (an asset id), or takes it off. The owner of the parcel and the island's staff may.</summary>
    public static async Task<CommerceResult> SetImageAsync(LifeVerseDbContext db, Guid islandId, Guid parcelId, Guid actor, string? image, TimeProvider clock, CancellationToken ct)
    {
        var parcel = await db.IslandParcels.SingleOrDefaultAsync(p => p.Id == parcelId && p.IslandId == islandId && p.DeletedAt == null, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        var island = await db.Islands.AsNoTracking().SingleAsync(i => i.Id == islandId, ct); var now = clock.GetUtcNow();
        if (!await LandAuthority.CanManageAsync(db, parcel, actor, ct) && !await LandOverview.IsStaffAsync(db, island, actor, now, ct)) return CommerceResult.Fail("forbidden");
        var value = (image ?? string.Empty).Trim();
        if (value.Length > 0)
        {
            if (!value.StartsWith("asset:", StringComparison.Ordinal) || !Guid.TryParse(value[6..], out var asset)) return CommerceResult.Fail("bad_image");
            var owns = await db.InventoryItems.AsNoTracking().AnyAsync(i => i.OwnerUserId == actor && i.AssetId == asset, ct);
            var current = await db.Assets.AsNoTracking().Where(a => a.Id == asset && a.DeletedAt == null).Select(a => a.CurrentVersion).FirstOrDefaultAsync(ct);
            var mime = await db.AssetVersions.AsNoTracking().Where(v => v.AssetId == asset && v.Version == current).Select(v => v.MimeType).FirstOrDefaultAsync(ct);
            if (!owns || mime is null || !mime.StartsWith("image/", StringComparison.Ordinal)) return CommerceResult.Fail("bad_image");
            value = $"asset:{asset:D}";
        }
        if (parcel.ImageUrl == value) return CommerceResult.Done(new { parcelId, imageUrl = value });
        var before = parcel.ImageUrl; parcel.ImageUrl = value; parcel.Revision++; parcel.UpdatedAt = now;
        db.ParcelChanges.Add(new ParcelChange { Id = Guid.NewGuid(), ParcelId = parcel.Id, ActorUserId = actor, Operation = "image", PayloadJson = JsonSerializer.Serialize(new { before, after = value }), Revision = parcel.Revision, CreatedAt = now });
        try { await db.SaveChangesAsync(ct); return CommerceResult.Done(new { parcelId, imageUrl = value }); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException or InvalidOperationException)
        {
            foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached;
            return CommerceResult.Fail("conflict");
        }
    }
}
