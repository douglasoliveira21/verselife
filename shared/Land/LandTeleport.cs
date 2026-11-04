using System.Text.Json;

namespace LifeVerse.Shared.Land;

/// <summary>
/// Where a teleport lands, as the island server decides it. A parcel can leave teleporting free, force arrivals to its landing point, or
/// refuse teleports altogether. The decision is made here from the parcel's settings and the access rules, never from what the client says.
/// </summary>
public sealed class LandTeleport
{
    private sealed record Zone(Guid Id, string Mode, (double X, double Z, double Yaw)? Arrival, HashSet<long> Cells);

    public readonly record struct Outcome(bool Ok, string Code, double X, double Z, double Yaw, Guid? ParcelId);

    private readonly Dictionary<Guid, Zone> _byId = [];
    private readonly Dictionary<long, Zone> _byCell = [];

    public static LandTeleport Empty { get; } = new();
    public int Parcels => _byId.Count;

    /// <summary>Reads { parcels: [{ id, mode, arrival: { x, z, yaw } or null, runs: [[cz, x0, x1]] }] }.</summary>
    public static LandTeleport Parse(JsonElement teleport)
    {
        var map = new LandTeleport();
        if (teleport.ValueKind != JsonValueKind.Object || !teleport.TryGetProperty("parcels", out var parcels) || parcels.ValueKind != JsonValueKind.Array) return map;
        foreach (var item in parcels.EnumerateArray())
        {
            try
            {
                if (!Guid.TryParse(item.GetProperty("id").GetString(), out var id)) continue;
                var mode = item.TryGetProperty("mode", out var m) ? m.GetString() ?? "free" : "free";
                (double, double, double)? arrival = item.TryGetProperty("arrival", out var a) && a.ValueKind == JsonValueKind.Object ? (a.GetProperty("x").GetDouble(), a.GetProperty("z").GetDouble(), a.TryGetProperty("yaw", out var y) ? y.GetDouble() : 0) : null;
                var cells = LandMath.FromRuns(item.GetProperty("runs").EnumerateArray().Select(r => r.EnumerateArray().Select(n => n.GetInt32()).ToArray()));
                if (cells.Count == 0) continue;
                var zone = new Zone(id, mode, arrival, cells);
                map._byId[id] = zone;
                foreach (var cell in cells) map._byCell[cell] = zone;
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException) { /* a damaged entry is skipped */ }
        }
        return map;
    }

    /// <summary>The landing spot of a parcel: its arrival point or, without one, the middle of its land.</summary>
    private static (double X, double Z, double Yaw) Landing(Zone zone)
    {
        if (zone.Arrival is { } arrival) return arrival;
        double sumX = 0, sumZ = 0;
        foreach (var key in zone.Cells) { var (cx, cz) = LandMath.Unkey(key); sumX += (cx + .5) * LandMath.CellSize; sumZ += (cz + .5) * LandMath.CellSize; }
        var centreX = sumX / zone.Cells.Count; var centreZ = sumZ / zone.Cells.Count;
        // the cell closest to the centre, so an L-shaped parcel does not land outside itself
        var best = zone.Cells.Select(LandMath.Unkey).OrderBy(c => Math.Pow((c.Cx + .5) * LandMath.CellSize - centreX, 2) + Math.Pow((c.Cz + .5) * LandMath.CellSize - centreZ, 2)).First();
        return ((best.Cx + .5) * LandMath.CellSize, (best.Cz + .5) * LandMath.CellSize, 0);
    }

    /// <summary>
    /// Decides a teleport. With a parcel id the person asked to go to that parcel; otherwise to a spot. A parcel that refuses teleports
    /// refuses both; one that forces arrivals sends everybody to its landing point; one that is free lets people land where they asked.
    /// <paramref name="denied"/> says whether this person may not stand at a spot at all (banned, or the parcel is closed to them).
    /// </summary>
    public Outcome Resolve(Guid? parcelId, double x, double z, Func<double, double, bool>? denied = null, double defaultYaw = 0)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z)) return new Outcome(false, "invalid", 0, 0, 0, null);
        (double X, double Z, double Yaw) target = (x, z, defaultYaw); Zone? zone = null;
        if (parcelId is { } id)
        {
            if (!_byId.TryGetValue(id, out zone)) return new Outcome(false, "unknown_parcel", 0, 0, 0, null);
            if (zone.Mode == "disabled") return new Outcome(false, "teleport_disabled", 0, 0, 0, id);
            target = Landing(zone);
        }
        else if (_byCell.TryGetValue(LandMath.Key(LandMath.CellOf(x), LandMath.CellOf(z)), out zone))
        {
            if (zone.Mode == "disabled") return new Outcome(false, "teleport_disabled", 0, 0, 0, zone.Id);
            if (zone.Mode == "forced") target = Landing(zone);
        }
        if (denied?.Invoke(target.X, target.Z) == true) return new Outcome(false, "no_access", 0, 0, 0, zone?.Id);
        return new Outcome(true, "ok", target.X, target.Z, target.Yaw, zone?.Id);
    }
}
