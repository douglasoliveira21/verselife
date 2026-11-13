using System.Text.Json;

namespace LifeVerse.Shared.Land;

/// <summary>What anybody may know about a parcel: its name, who owns it and how it is opened. This is what scripts may ask; nothing here is private.</summary>
public sealed record ParcelInfo(Guid Id, string Name, Guid Owner, Guid? Group, string Access, string Build, bool ForSale);

/// <summary>Which parcel is where, as the island server needs it for scripts (the API's land-sync, section "info"): { parcels: [{ id, name, owner, group, access, build, forSale, runs }] }.</summary>
public sealed class LandInfo
{
    private readonly Dictionary<long, ParcelInfo> _byCell = [];
    public static LandInfo Empty { get; } = new();
    public int Parcels => _byCell.Values.Distinct().Count();

    public static LandInfo Parse(JsonElement info)
    {
        var map = new LandInfo();
        if (info.ValueKind != JsonValueKind.Object || !info.TryGetProperty("parcels", out var parcels) || parcels.ValueKind != JsonValueKind.Array) return map;
        foreach (var item in parcels.EnumerateArray())
        {
            try
            {
                if (!Guid.TryParse(item.GetProperty("id").GetString(), out var id) || !Guid.TryParse(item.GetProperty("owner").GetString(), out var owner)) continue;
                string Text(string name, string fallback) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
                var name = Text("name", ""); var parcel = new ParcelInfo(id, name.Length > 80 ? name[..80] : name, owner, item.TryGetProperty("group", out var g) && Guid.TryParse(g.GetString(), out var group) ? group : null, Text("access", "public"), Text("build", "owner"), item.TryGetProperty("forSale", out var sale) && sale.ValueKind == JsonValueKind.True);
                foreach (var cell in LandMath.FromRuns(item.GetProperty("runs").EnumerateArray().Select(r => r.EnumerateArray().Select(n => n.GetInt32()).ToArray()))) map._byCell[cell] = parcel;
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException) { /* a damaged entry is skipped */ }
        }
        return map;
    }

    /// <summary>The parcel that covers this spot, or null where the land belongs to nobody.</summary>
    public ParcelInfo? At(double x, double z) => _byCell.GetValueOrDefault(LandMath.Key(LandMath.CellOf(x), LandMath.CellOf(z)));
}
