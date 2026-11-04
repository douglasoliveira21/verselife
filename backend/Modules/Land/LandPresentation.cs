using System.Globalization;
using System.Net;
using System.Text.Json;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

/// <summary>
/// The parts of a parcel that shape how it feels: where people arrive when they teleport there and whether they may, the look of its sky
/// and weather, and the music it plays. The owner sets them; everything is checked here, so a client can only ask.
/// </summary>
public static class LandPresentation
{
    public static readonly string[] TeleportModes = ["free", "forced", "disabled"];
    public static readonly string[] Weathers = ["clear", "cloudy", "rain", "storm", "fog"];

    // ---- environment ------------------------------------------------------------------------------------------

    /// <summary>The settings as clean JSON with only valid, in-range fields, or null when the text is not an object at all. (The client reads the same with ParcelEnvironment; a test keeps the two equal.)</summary>
    public static string? NormalizeEnvironment(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "{}";
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string? Number(string key, double min, double max, bool wrap = false)
            {
                if (!root.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d) || !double.IsFinite(d)) return null;
                var value = wrap ? (((d % 24) + 24) % 24) : Math.Clamp(d, min, max);
                return $"\"{key}\":{((float)value is var f ? MathF.Round(f, 2) : 0f).ToString("0.##", CultureInfo.InvariantCulture)}";
            }
            var parts = new List<string?> { Number("hour", 0, 24, wrap: true) };
            if (root.TryGetProperty("weather", out var w) && w.ValueKind == JsonValueKind.String && Weathers.Contains(w.GetString())) parts.Add($"\"weather\":\"{w.GetString()}\"");
            parts.Add(Number("fog", 0, 1)); parts.Add(Number("light", .2, 1.6));
            if (root.TryGetProperty("sky", out var s) && s.ValueKind == JsonValueKind.String && s.GetString() is { Length: 7 } color && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit)) parts.Add($"\"sky\":\"{color.ToUpperInvariant()}\"");
            return "{" + string.Join(",", parts.Where(p => p is not null)) + "}";
        }
        catch (JsonException) { return null; }
    }

    /// <summary>An island lets its parcels change their own surroundings unless its environment says "parcelEnvironment": false.</summary>
    public static bool IslandAllowsParcelEnvironment(string? islandEnvironmentJson)
    {
        try { using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(islandEnvironmentJson) ? "{}" : islandEnvironmentJson); return !(document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("parcelEnvironment", out var v) && v.ValueKind == JsonValueKind.False); }
        catch (JsonException) { return true; }
    }

    public static async Task<CommerceResult> SetEnvironmentAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, string? json, TimeProvider clock, CancellationToken ct)
    {
        var parcel = await db.IslandParcels.SingleOrDefaultAsync(p => p.Id == parcelId && p.DeletedAt == null, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        if (parcel.OwnerUserId != actor) return CommerceResult.Fail("forbidden");
        var island = await db.Islands.AsNoTracking().SingleAsync(i => i.Id == parcel.IslandId, ct);
        if (!IslandAllowsParcelEnvironment(island.EnvironmentJson)) return CommerceResult.Fail("environment_not_allowed");
        var normalized = NormalizeEnvironment(json); if (normalized is null) return CommerceResult.Fail("bad_environment");
        parcel.EnvironmentJson = normalized;
        Touch(db, parcel, actor, "environment", new { normalized }, clock.GetUtcNow());
        return await SaveAsync(db, ct, new { parcelId, environment = normalized });
    }

    // ---- music ---------------------------------------------------------------------------------------------------

    /// <summary>The same rules as the client's: HTTPS, no login in the address, no machine inside a network, an mp3 or ogg file. Null when fine.</summary>
    public static string? CheckMediaUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "not_https";
        if (url.Length > 512) return "too_long";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0) return "not_https";
        var host = uri.Host.ToLowerInvariant().Trim('[', ']');
        var isAddress = IPAddress.TryParse(host, out var ip);
        if (host.Length is 0 or > 255 || (!host.Contains('.') && !isAddress && !host.Contains(':'))) return "bad_host";
        if (host is "localhost" or "postgres" or "redis" or "minio" || host.EndsWith(".internal", StringComparison.Ordinal) || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".localhost", StringComparison.Ordinal)) return "bad_host";
        if (isAddress && PrivateAddress(ip!)) return "bad_host";
        var path = uri.AbsolutePath.ToLowerInvariant();
        return path.EndsWith(".mp3", StringComparison.Ordinal) || path.EndsWith(".ogg", StringComparison.Ordinal) ? null : "bad_type";
    }

    private static bool PrivateAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 16) return (bytes[0] & 0xFE) == 0xFC;
        return bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 0 || (bytes[0] == 169 && bytes[1] == 254) || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) || bytes[0] >= 224;
    }

    /// <summary>Sets the music of a parcel (an empty address turns it off). Volume is kept between 0 and 1.</summary>
    public static async Task<CommerceResult> SetMediaAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, string? musicUrl, float volume, bool autoplay, string? name, TimeProvider clock, CancellationToken ct)
    {
        var parcel = await db.IslandParcels.SingleOrDefaultAsync(p => p.Id == parcelId && p.DeletedAt == null, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        if (parcel.OwnerUserId != actor) return CommerceResult.Fail("forbidden");
        string json;
        if (string.IsNullOrWhiteSpace(musicUrl)) json = "{}";
        else
        {
            if (CheckMediaUrl(musicUrl.Trim()) is { } why) return CommerceResult.Fail(why);
            if (!float.IsFinite(volume)) return CommerceResult.Fail("bad_volume");
            var label = (name ?? string.Empty).Trim(); if (label.Length > 80) label = label[..80];
            json = JsonSerializer.Serialize(new { musicUrl = musicUrl.Trim(), volume = MathF.Round(Math.Clamp(volume, 0f, 1f), 2), autoplay, name = label });
        }
        parcel.MediaJson = json;
        Touch(db, parcel, actor, "media", new { on = json != "{}" }, clock.GetUtcNow());
        return await SaveAsync(db, ct, new { parcelId, media = json });
    }

    // ---- arrival and teleporting --------------------------------------------------------------------------------

    /// <summary>
    /// Where people land when they teleport to the parcel, which way they face, and whether teleporting is free, forced to that point or refused.
    /// The point must be on the parcel's own land and not inside a solid object; the island checks the ground height when somebody lands.
    /// Without a point the middle of the parcel is used.
    /// </summary>
    public static async Task<CommerceResult> SetArrivalAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, double? x, double? z, double yaw, string mode, TimeProvider clock, CancellationToken ct)
    {
        var parcel = await db.IslandParcels.SingleOrDefaultAsync(p => p.Id == parcelId && p.DeletedAt == null, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        if (parcel.OwnerUserId != actor) return CommerceResult.Fail("forbidden");
        if (!TeleportModes.Contains(mode)) return CommerceResult.Fail("bad_mode");
        if (!double.IsFinite(yaw)) return CommerceResult.Fail("bad_point");
        var cells = (await db.ParcelCells.AsNoTracking().Where(c => c.ParcelId == parcelId).Select(c => new { c.Cx, c.Cz }).ToListAsync(ct)).Select(c => LandGrid.Key(c.Cx, c.Cz)).ToHashSet();
        if (cells.Count == 0) return CommerceResult.Fail("no_cells");
        double ax, az;
        if (x is null || z is null) { (ax, az) = Middle(cells); }
        else
        {
            ax = x.Value; az = z.Value;
            if (!double.IsFinite(ax) || !double.IsFinite(az) || !cells.Contains(LandGrid.Key(LandGrid.CellOf(ax), LandGrid.CellOf(az)))) return CommerceResult.Fail("outside_parcel");
            if (await BlockedAsync(db, parcel, cells, ax, az, ct)) return CommerceResult.Fail("blocked");
        }
        parcel.SpawnX = (float)ax; parcel.SpawnZ = (float)az; parcel.ArrivalYaw = (float)(((yaw % 360) + 360) % 360); parcel.TeleportMode = mode;
        Touch(db, parcel, actor, "arrival", new { x = ax, z = az, yaw = parcel.ArrivalYaw, mode }, clock.GetUtcNow());
        return await SaveAsync(db, ct, new { parcelId, x = ax, z = az, yaw = parcel.ArrivalYaw, mode });
    }

    /// <summary>The centre of the cell nearest to the middle of the parcel.</summary>
    public static (double X, double Z) Middle(IReadOnlyCollection<long> cells)
    {
        double sx = 0, sz = 0;
        foreach (var key in cells) { var (cx, cz) = LandGrid.Unkey(key); sx += (cx + .5) * LandGrid.CellSize; sz += (cz + .5) * LandGrid.CellSize; }
        double mx = sx / cells.Count, mz = sz / cells.Count;
        var best = cells.Select(LandGrid.Unkey).OrderBy(c => Math.Pow((c.Cx + .5) * LandGrid.CellSize - mx, 2) + Math.Pow((c.Cz + .5) * LandGrid.CellSize - mz, 2)).First();
        return ((best.Cx + .5) * LandGrid.CellSize, (best.Cz + .5) * LandGrid.CellSize);
    }

    /// <summary>Whether somebody standing here would be inside a solid object of the parcel (its footprint widened by the avatar's body).</summary>
    private static async Task<bool> BlockedAsync(LifeVerseDbContext db, IslandParcel parcel, HashSet<long> cells, double x, double z, CancellationToken ct)
    {
        var near = await db.BuildObjects.AsNoTracking().Where(o => o.IslandId == parcel.IslandId && o.DeletedAt == null && Math.Abs(o.PositionX - x) < 40 && Math.Abs(o.PositionZ - z) < 40).ToListAsync(ct);
        foreach (var o in near)
        {
            if (o.PhysicsJson.Contains("\"collision\":false", StringComparison.Ordinal) || o.PhysicsJson.Contains("\"collision\": false", StringComparison.Ordinal)) continue;
            var reach = Math.Max(o.ScaleX, o.ScaleZ) / 2d * 1.4143 + .45; // the widest the footprint can be once turned, plus the avatar
            if (Math.Abs(o.PositionX - x) <= reach && Math.Abs(o.PositionZ - z) <= reach) return true;
        }
        return false;
    }

    // ---- shared by the three -----------------------------------------------------------------------------------

    private static void Touch(LifeVerseDbContext db, IslandParcel parcel, Guid actor, string operation, object data, DateTimeOffset now)
    {
        parcel.Revision++; parcel.UpdatedAt = now;
        db.ParcelChanges.Add(new ParcelChange { Id = Guid.NewGuid(), ParcelId = parcel.Id, ActorUserId = actor, Operation = operation, PayloadJson = JsonSerializer.Serialize(data), Revision = parcel.Revision, CreatedAt = now });
    }

    private static async Task<CommerceResult> SaveAsync(LifeVerseDbContext db, CancellationToken ct, object? data = null)
    {
        try { await db.SaveChangesAsync(ct); return CommerceResult.Done(data); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException or InvalidOperationException)
        {
            foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached;
            return CommerceResult.Fail("conflict");
        }
    }
}
