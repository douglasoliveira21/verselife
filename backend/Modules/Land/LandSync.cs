using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

/// <summary>
/// What an island server asks for every few seconds: the parcels with a closed door or a ban (so it can keep people out), and what the land
/// system did to objects behind its back (returned, handed to a new owner) so everyone on the island sees it at once.
/// </summary>
public static class LandSync
{
    public sealed record SyncAnswer(object Access, IReadOnlyList<JsonElement> Events, long Cursor, object? Terrain = null, object? Teleport = null, object? Rules = null);

    public static async Task<SyncAnswer?> BuildAsync(LifeVerseDbContext db, Guid instanceId, long sinceTicks, TimeProvider clock, CancellationToken ct)
    {
        var server = await db.ServerInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == instanceId && x.StoppedAt == null, ct);
        if (server is null) return null;
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(i => i.Id == server.IslandId && i.DeletedAt == null, ct);
        if (island is null) return null;
        var now = clock.GetUtcNow();

        var admins = (await db.IslandPermissions.AsNoTracking().Where(p => p.IslandId == island.Id && p.UserId != null && p.RevokedAt == null && p.Permission == "admin").Select(p => p.UserId!.Value).ToListAsync(ct)).Append(island.OwnerUserId).Distinct().ToList();
        var parcels = await db.IslandParcels.AsNoTracking().Where(p => p.IslandId == island.Id && p.DeletedAt == null).ToListAsync(ct);
        var parcelIds = parcels.Select(p => p.Id).ToList();
        var entries = await db.ParcelAccess.AsNoTracking().Where(a => parcelIds.Contains(a.ParcelId) && a.RevokedAt == null && (a.ExpiresAt == null || a.ExpiresAt > now)).ToListAsync(ct);
        var people = await LandAuthority.PeopleAsync(db, parcels, ct);
        var restricted = parcels.Where(p => p.AccessMode != "public" || entries.Any(e => e.ParcelId == p.Id && e.Kind == "ban")).ToList();
        var restrictedIds = restricted.Select(p => p.Id).ToList();
        var tenants = await db.ParcelRentals.AsNoTracking().Where(r => r.Status == "active" && restrictedIds.Contains(r.ParcelId)).ToDictionaryAsync(r => r.ParcelId, r => r.TenantUserId, ct);
        // one read of the cells serves the access rules, the terrain rights and the teleport rules
        var allCells = (await db.ParcelCells.AsNoTracking().Where(c => c.IslandId == island.Id).Select(c => new { c.ParcelId, c.Cx, c.Cz }).ToListAsync(ct)).GroupBy(c => c.ParcelId).ToDictionary(g => g.Key, g => g.Select(c => LandGrid.Key(c.Cx, c.Cz)).ToList());
        var access = new
        {
            admins,
            parcels = restricted.Select(p => new
            {
                id = p.Id, mode = p.GroupId != null && p.AccessMode == "private" ? "list" : p.AccessMode, owner = p.OwnerUserId, tenant = tenants.TryGetValue(p.Id, out var t) ? (Guid?)t : null,
                allow = entries.Where(e => e.ParcelId == p.Id && e.Kind == "allow").Select(e => e.UserId).Concat(people.TryGetValue(p.Id, out var crowd) ? crowd.Members.Concat(p.AccessMode == "list" ? crowd.Guests : Enumerable.Empty<Guid>()) : Enumerable.Empty<Guid>()).Distinct().ToList(),
                bans = entries.Where(e => e.ParcelId == p.Id && e.Kind == "ban").Select(e => new { userId = e.UserId, untilMs = e.ExpiresAt?.ToUnixTimeMilliseconds() }).ToList(),
                runs = allCells.TryGetValue(p.Id, out var list) ? LandGrid.Runs(list) : []
            }).ToList()
        };

        var events = new List<JsonElement>();
        if (sinceTicks > 0)
        {
            var since = new DateTimeOffset(Math.Min(sinceTicks, now.UtcTicks), TimeSpan.Zero);
            var operations = await db.BuildOperations.AsNoTracking().Where(o => o.IslandId == island.Id && o.CreatedAt > since && o.ObjectId != null && (o.Operation == "return" || o.Operation == "land_transfer" || o.Operation == "physics_admin")).OrderBy(o => o.CreatedAt).Take(500).ToListAsync(ct);
            foreach (var operation in operations)
            {
                if (operation.Operation == "return") { events.Add(Element(new { operation = "delete", objectId = operation.ObjectId, returned = true })); continue; }
                var item = await db.BuildObjects.AsNoTracking().SingleOrDefaultAsync(o => o.Id == operation.ObjectId && o.DeletedAt == null, ct);
                if (item is not null) events.Add(Element(new { operation = "update", objectState = BuildEndpoints.ToResponse(item), objectId = item.Id, revision = item.Revision }));
            }
        }
        // who may reshape the ground where: the owner, the tenant and the builders of every parcel (nobody where the parcel is closed to building)
        var allTenants = await db.ParcelRentals.AsNoTracking().Where(r => r.Status == "active" && parcelIds.Contains(r.ParcelId)).ToDictionaryAsync(r => r.ParcelId, r => r.TenantUserId, ct);
        var terrain = new
        {
            parcels = parcels.Where(p => allCells.ContainsKey(p.Id)).Select(p => new { id = p.Id, editors = LandService.TerrainEditors(p, allTenants.TryGetValue(p.Id, out var tn) ? tn : null, entries.Where(e => e.ParcelId == p.Id).ToList(), people.TryGetValue(p.Id, out var team) ? team.Managers.Concat(team.Builders) : null), runs = LandGrid.Runs(allCells[p.Id]) }).ToList()
        };
        // where teleports land and which parcels refuse them; an arrival point only counts while it is still on the parcel's land
        var teleport = new
        {
            parcels = parcels.Where(p => allCells.ContainsKey(p.Id)).Select(p =>
            {
                var own = allCells[p.Id].ToHashSet();
                var set = own.Contains(LandGrid.Key(LandGrid.CellOf(p.SpawnX), LandGrid.CellOf(p.SpawnZ)));
                return new { id = p.Id, mode = p.TeleportMode, arrival = set ? new { x = p.SpawnX, z = p.SpawnZ, yaw = p.ArrivalYaw } : null, runs = LandGrid.Runs(allCells[p.Id]) };
            }).ToList()
        };
        // the parcels where voice is off: the island server drops the audio of whoever stands on them and does not play it to them
        var rules = new
        {
            parcels = parcels.Where(p => !p.AllowVoice && allCells.ContainsKey(p.Id)).Select(p => new { id = p.Id, voice = false, runs = LandGrid.Runs(allCells[p.Id]) }).ToList()
        };
        return new SyncAnswer(access, events, now.UtcTicks, terrain, teleport, rules);
    }

    private static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
