using System.Text.Json;

namespace LifeVerse.Shared.Land;

/// <summary>
/// Who may stand where, as the island server needs it: the parcels with a closed door or a ban, and the people the rules do not apply to.
/// The API builds it (land-sync), the island server checks every player against it on every tick, so no client can walk in, teleport in
/// or reconnect into a parcel it is kept out of: wherever a forbidden player ends up, the server moves them to the nearest allowed spot.
/// </summary>
public sealed class LandAccessMap
{
    private sealed record Zone(Guid Id, string Mode, Guid Owner, Guid? Tenant, HashSet<Guid> Allow, Dictionary<Guid, long?> Bans);

    private readonly Dictionary<long, Zone> _byCell = [];
    private readonly HashSet<Guid> _admins = [];

    public static LandAccessMap Empty { get; } = new();
    public bool HasZones => _byCell.Count > 0;
    public int Zones => _byCell.Values.Distinct().Count();

    /// <summary>Reads the "access" part of the API's answer: { admins: [...], parcels: [{ id, mode, owner, tenant, allow: [...], bans: [{ userId, untilMs }], runs: [[cz, x0, x1]] }] }.</summary>
    public static LandAccessMap Parse(JsonElement access)
    {
        var map = new LandAccessMap();
        if (access.ValueKind != JsonValueKind.Object) return map;
        if (access.TryGetProperty("admins", out var admins) && admins.ValueKind == JsonValueKind.Array)
            foreach (var admin in admins.EnumerateArray()) if (Guid.TryParse(admin.GetString(), out var id)) map._admins.Add(id);
        if (!access.TryGetProperty("parcels", out var parcels) || parcels.ValueKind != JsonValueKind.Array) return map;
        foreach (var item in parcels.EnumerateArray())
        {
            try
            {
                if (!Guid.TryParse(item.GetProperty("id").GetString(), out var id) || !Guid.TryParse(item.GetProperty("owner").GetString(), out var owner)) continue;
                Guid? tenant = item.TryGetProperty("tenant", out var t) && Guid.TryParse(t.GetString(), out var tid) ? tid : null;
                var allow = new HashSet<Guid>(); if (item.TryGetProperty("allow", out var a)) foreach (var user in a.EnumerateArray()) if (Guid.TryParse(user.GetString(), out var uid)) allow.Add(uid);
                var bans = new Dictionary<Guid, long?>();
                if (item.TryGetProperty("bans", out var b))
                    foreach (var ban in b.EnumerateArray())
                        if (Guid.TryParse(ban.GetProperty("userId").GetString(), out var uid)) bans[uid] = ban.TryGetProperty("untilMs", out var until) && until.ValueKind == JsonValueKind.Number ? until.GetInt64() : null;
                var zone = new Zone(id, item.TryGetProperty("mode", out var m) ? m.GetString() ?? "public" : "public", owner, tenant, allow, bans);
                foreach (var cell in LandMath.FromRuns(item.GetProperty("runs").EnumerateArray().Select(r => r.EnumerateArray().Select(n => n.GetInt32()).ToArray()))) map._byCell[cell] = zone;
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException) { /* a damaged entry is skipped */ }
        }
        return map;
    }

    private static bool Forbids(Zone zone, Guid user, long nowMs)
    {
        if (zone.Owner == user || zone.Tenant == user) return false;
        if (zone.Bans.TryGetValue(user, out var until) && (until is null || until > nowMs)) return true;
        return zone.Mode switch { "private" => true, "list" => !zone.Allow.Contains(user), _ => false };
    }

    /// <summary>True when this person is not allowed to be at this spot.</summary>
    public bool Denies(Guid user, double x, double z, long nowMs) =>
        HasZones && !_admins.Contains(user) && _byCell.TryGetValue(LandMath.Key(LandMath.CellOf(x), LandMath.CellOf(z)), out var zone) && Forbids(zone, user, nowMs);

    /// <summary>The centre of the nearest cell where this person may stand, searching outwards; null when nothing is found nearby.</summary>
    public (double X, double Z)? NearestAllowed(Guid user, double x, double z, long nowMs, int maxRadiusCells = 80)
    {
        int cx0 = LandMath.CellOf(x), cz0 = LandMath.CellOf(z);
        for (var radius = 1; radius <= maxRadiusCells; radius++)
        {
            (double X, double Z)? best = null; var bestDistance = double.MaxValue;
            for (var dx = -radius; dx <= radius; dx++)
                for (var dz = -radius; dz <= radius; dz++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius) continue;
                    var px = (cx0 + dx + .5) * LandMath.CellSize; var pz = (cz0 + dz + .5) * LandMath.CellSize;
                    if (Denies(user, px, pz, nowMs)) continue;
                    var distance = (px - x) * (px - x) + (pz - z) * (pz - z);
                    if (distance < bestDistance) { best = (px, pz); bestDistance = distance; }
                }
            if (best is not null) return best;
        }
        return null;
    }
}
