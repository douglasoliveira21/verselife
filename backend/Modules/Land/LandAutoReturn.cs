using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

/// <summary>
/// Parcels with automatic return send back the objects of people who may not build there once they have stood for the chosen time. The
/// owner, the tenant, the builders and the people on the allow list are never touched, and neither are objects marked protected. It runs on
/// the server every minute, whether or not the owner is online.
/// </summary>
public static class LandAutoReturn
{
    private static bool Protected(string permissionsJson) => permissionsJson.Contains("\"protected\":true", StringComparison.Ordinal) || permissionsJson.Contains("\"protected\": true", StringComparison.Ordinal);

    public static async Task<int> RunAsync(LifeVerseDbContext db, AssetStorageService? storage, DateTimeOffset now, CancellationToken ct)
    {
        if (storage is null) return 0;
        var total = 0;
        var ids = await db.IslandParcels.AsNoTracking().Where(p => p.DeletedAt == null && p.AutoReturnMinutes != null).Select(p => p.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            var parcel = await db.IslandParcels.SingleOrDefaultAsync(p => p.Id == id && p.DeletedAt == null && p.AutoReturnMinutes != null, ct);
            if (parcel?.AutoReturnMinutes is not { } minutes) continue;
            var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(i => i.Id == parcel.IslandId && i.DeletedAt == null, ct);
            if (island is null) continue;
            var cells = (await db.ParcelCells.AsNoTracking().Where(c => c.ParcelId == id).Select(c => new { c.Cx, c.Cz }).ToListAsync(ct)).Select(c => LandGrid.Key(c.Cx, c.Cz)).ToHashSet();
            var here = await LandAdmin.ObjectsOnAsync(db, parcel, cells, ct);
            var authorized = await LandAdmin.AuthorizedAsync(db, parcel, now, ct);
            var cutoff = now.AddMinutes(-minutes);
            var stale = here.Where(o => !authorized.Contains(o.OwnerUserId) && o.CreatedAt <= cutoff && !Protected(o.PermissionsJson)).Select(o => o.Id).ToArray();
            if (stale.Length == 0) continue;
            foreach (var batch in stale.Chunk(BuildPackageLimits.MaxReturnAtOnce)) await BuildOperations.ReturnForLandAsync(island, parcel.OwnerUserId, batch, db, storage, now, ct);
            parcel = await db.IslandParcels.SingleAsync(p => p.Id == id, ct);
            parcel.Revision++; parcel.UpdatedAt = now;
            db.ParcelChanges.Add(new ParcelChange { Id = Guid.NewGuid(), ParcelId = id, ActorUserId = parcel.OwnerUserId, Operation = "auto_return", PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { count = stale.Length, minutes }), Revision = parcel.Revision, CreatedAt = now });
            await db.SaveChangesAsync(ct);
            total += stale.Length;
        }
        return total;
    }
}
