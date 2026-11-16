using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Map;

public sealed record ArrivalRequest(Guid IslandId, float X, float Z);

/// <summary>The answer to "may I go to that spot?": allowed (and where exactly, since land can fix the arrival point) or the reason it is not.</summary>
public sealed record ArrivalAnswer(bool Allowed, string? Reason, float X, float Z, Guid? ParcelId, string? ParcelName, bool Forced);

/// <summary>
/// Choosing a spot on the map is a request, not a command: the server checks the island (can this person enter, is it full), then the land at that spot (a parcel that refuses visitors, one that only lets people
/// arrive at its own landing point, one that cannot be teleported to) and gives back the point to go to. The game then enters the island and the island server still has the last word.
/// </summary>
public static class MapArrival
{
    public static async Task<ArrivalAnswer> ResolveAsync(LifeVerseDbContext db, Guid viewer, ArrivalRequest r, DateTimeOffset now, CancellationToken ct)
    {
        ArrivalAnswer No(string reason) => new(false, reason, r.X, r.Z, null, null, false);
        if (!float.IsFinite(r.X) || !float.IsFinite(r.Z)) return No("bad_position");
        var island = await MapService.Visible(db, viewer, now).SingleOrDefaultAsync(i => i.Id == r.IslandId, ct); if (island is null) return No("island_not_found");
        if (!await IslandAccess.AllowsAsync(db, island, viewer, now, ct))
        {
            if (await db.IslandBans.AsNoTracking().AnyAsync(b => b.IslandId == island.Id && b.UserId == viewer && b.RevokedAt == null && (b.ExpiresAt == null || b.ExpiresAt > now), ct)) return No("banned");
            return No(island.AccessClosed ? "closed" : "private");
        }
        if (island.OwnerUserId != viewer && await TeleportService.TakenPlacesAsync(db, island.Id, viewer, now, ct) >= island.MaxPlayers) return No("full");
        var half = island.Size / 2f; var x = Math.Clamp(r.X, -half, half); var z = Math.Clamp(r.Z, -half, half);

        var staff = island.OwnerUserId == viewer;
        var parcels = await db.IslandParcels.AsNoTracking().Where(p => p.IslandId == island.Id && p.DeletedAt == null).ToListAsync(ct);
        IslandParcel? here = null; double best = double.MaxValue;
        foreach (var p in parcels)
        {
            try
            {
                using var d = System.Text.Json.JsonDocument.Parse(p.BoundsJson); var b = d.RootElement;
                if (x >= b.GetProperty("minX").GetDouble() && x <= b.GetProperty("maxX").GetDouble() && z >= b.GetProperty("minZ").GetDouble() && z <= b.GetProperty("maxZ").GetDouble() && p.Area < best) { here = p; best = p.Area; }
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException) { }
        }
        if (here is null) return new(true, null, x, z, null, null, false);

        var mine = staff || here.OwnerUserId == viewer || await db.ParcelRentals.AsNoTracking().AnyAsync(t => t.ParcelId == here.Id && t.TenantUserId == viewer && (t.Status == "active" || t.Status == "grace"), ct);
        if (await db.ParcelAccess.AsNoTracking().AnyAsync(a => a.ParcelId == here.Id && a.UserId == viewer && a.Kind == "ban" && a.RevokedAt == null && (a.ExpiresAt == null || a.ExpiresAt > now), ct) && !mine) return new(false, "parcel_banned", x, z, here.Id, here.Name, false);
        if (here.AccessMode != "public" && !mine
            && !await db.ParcelAccess.AsNoTracking().AnyAsync(a => a.ParcelId == here.Id && a.UserId == viewer && a.Kind == "allow" && a.RevokedAt == null && (a.ExpiresAt == null || a.ExpiresAt > now), ct)
            && !(here.GroupId is { } g && await db.GroupMembers.AsNoTracking().AnyAsync(m => m.GroupId == g && m.UserId == viewer, ct))) return new(false, "parcel_restricted", x, z, null, null, false);
        if (here.TeleportMode == "disabled" && !mine) return new(false, "parcel_no_teleport", x, z, here.Id, here.Name, false);
        if (here.TeleportMode == "forced" && !mine) return new(true, null, here.SpawnX, here.SpawnZ, here.Id, here.Name, true);
        return new(true, null, x, z, here.Id, here.Name, false);
    }
}
