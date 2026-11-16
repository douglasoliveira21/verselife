using System.Globalization;
using System.Text.Json;

namespace LifeVerse.Client.Scripts.Map;

/// <summary>One island on the atlas, as the server describes it to this player: where it stands (cell and meters), what it is, how full it is and whether this player can come in.</summary>
public sealed record AtlasIsland(Guid Id, string Name, string Description, string Category, string Rating, string Language, bool IsPublic, bool IsWelcome, int CellX, int CellY, double X, double Y, int Size, float SpawnX, float SpawnZ,
    int MaxPlayers, bool Online, int? Visitors, string Access, string? CoverImageUrl, bool HasTile, string TileVersion, bool Favorite, bool IsOwner, Guid OwnerId, string OwnerName, int Favorites = 0, DateTimeOffset? CreatedAt = null)
{
    /// <summary>The player can enter (the server says so; this only reads it).</summary>
    public bool CanEnter => Access == "open";
    public string AccessWords => Access switch { "open" => "Aberta", "full" => "Cheia", "closed" => "Fechada", "private" => "Privada", "banned" => "Acesso negado", _ => "Indisponível" };
}

public sealed record AtlasPage(IReadOnlyList<AtlasIsland> Items, int Total, int CellMeters, bool Truncated)
{
    public static AtlasPage Empty { get; } = new([], 0, 768, false);
}

/// <summary>The questions the map asks the server, and how its answers are read. No nodes here, so it can be tested alone.</summary>
public static class MapApi
{
    public static string AtlasPath(string? text = null, string? category = null, IEnumerable<string>? ratings = null, (int MinX, int MaxX, int MinY, int MaxY)? cells = null, int limit = 300)
    {
        var parts = new List<string> { "limit=" + Math.Clamp(limit, 1, 500) };
        if (!string.IsNullOrWhiteSpace(text)) parts.Add("q=" + Uri.EscapeDataString(text.Trim()));
        if (!string.IsNullOrWhiteSpace(category)) parts.Add("category=" + Uri.EscapeDataString(category.Trim()));
        if (ratings?.ToList() is { Count: > 0 } list) parts.Add("ratings=" + Uri.EscapeDataString(string.Join(",", list)));
        if (cells is { } c) parts.Add($"minX={c.MinX}&maxX={c.MaxX}&minY={c.MinY}&maxY={c.MaxY}");
        return "/api/v1/map/atlas?" + string.Join("&", parts);
    }

    public static string TilePath(Guid island) => $"/api/v1/map/islands/{island}/tile";

    public static AtlasPage ParseAtlas(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement; if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("items", out var array) || array.ValueKind != JsonValueKind.Array) return AtlasPage.Empty;
            var items = new List<AtlasIsland>();
            foreach (var e in array.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object || !Guid.TryParse(Str(e, "id"), out var id)) continue; var owner = e.TryGetProperty("owner", out var o) && o.ValueKind == JsonValueKind.Object ? o : default;
                items.Add(new AtlasIsland(id, Str(e, "name"), Str(e, "description"), Str(e, "category"), Str(e, "contentRating"), Str(e, "language"), Flag(e, "isPublic"), Flag(e, "isWelcome"), Int(e, "cellX"), Int(e, "cellY"), Num(e, "x"), Num(e, "y"), Int(e, "size"), (float)Num(e, "spawnX"), (float)Num(e, "spawnZ"),
                    Int(e, "maxPlayers"), Flag(e, "online"), e.TryGetProperty("visitors", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null, Str(e, "access"), Str(e, "coverImageUrl") is { Length: > 0 } cover ? cover : null, Flag(e, "hasTile"), Str(e, "tileVersion"), Flag(e, "favorite"), Flag(e, "isOwner"),
                    Guid.TryParse(Str(owner, "id"), out var ownerId) ? ownerId : Guid.Empty, Str(owner, "displayName"), Int(e, "favorites"), DateTimeOffset.TryParse(Str(e, "createdAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var created) ? created : null));
            }
            return new AtlasPage(items, Int(root, "total"), Int(root, "cellMeters") is > 0 and var m ? m : 768, Flag(root, "truncated"));
        }
        catch (JsonException) { return AtlasPage.Empty; }
    }

    private static string Str(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
    private static bool Flag(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
    private static int Int(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
    private static double Num(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) ? n : 0;
}

/// <summary>
/// Where things are on the map. Two kinds of coordinates are kept apart: the global ones place an island on the atlas (meters, the centre of its cell), and the local ones place a spot inside an island (meters from its
/// centre, the same ones the game uses while playing). Nothing here moves anybody: it only converts between the two, and reads and writes places as text.
/// </summary>
public static class MapCoordinates
{
    /// <summary>The square an island covers on the atlas (centred on its cell).</summary>
    public static (double MinX, double MinY, double MaxX, double MaxY) Rect(AtlasIsland island) { var half = island.Size / 2.0; return (island.X - half, island.Y - half, island.X + half, island.Y + half); }

    public static bool Contains(AtlasIsland island, double globalX, double globalY) { var r = Rect(island); return globalX >= r.MinX && globalX <= r.MaxX && globalY >= r.MinY && globalY <= r.MaxY; }

    /// <summary>A spot inside the island as a point of the atlas.</summary>
    public static (double X, double Y) ToGlobal(AtlasIsland island, double localX, double localZ) => (island.X + localX, island.Y + localZ);

    /// <summary>A point of the atlas as a spot inside the island (clamped to its ground), and whether the point was really inside.</summary>
    public static (double LocalX, double LocalZ, bool Inside) ToLocal(AtlasIsland island, double globalX, double globalY)
    {
        var half = island.Size / 2.0; var x = globalX - island.X; var z = globalY - island.Y;
        return (Math.Clamp(x, -half, half), Math.Clamp(z, -half, half), Math.Abs(x) <= half && Math.Abs(z) <= half);
    }

    /// <summary>The island under a point of the atlas, if there is one.</summary>
    public static AtlasIsland? IslandAt(IEnumerable<AtlasIsland> islands, double globalX, double globalY) => islands.FirstOrDefault(i => Contains(i, globalX, globalY));

    /// <summary>The cell of the atlas a point falls in.</summary>
    public static (int CellX, int CellY) CellOf(double globalX, double globalY, int cellMeters) => ((int)Math.Round(globalX / cellMeters), (int)Math.Round(globalY / cellMeters));

    /// <summary>The cells a rectangle of the atlas touches, for asking the server only for what is on screen.</summary>
    public static (int MinX, int MaxX, int MinY, int MaxY) CellsOf(double minX, double minY, double maxX, double maxY, int cellMeters)
    {
        var (a, b) = CellOf(minX, minY, cellMeters); var (c, d) = CellOf(maxX, maxY, cellMeters); return (Math.Min(a, c), Math.Max(a, c), Math.Min(b, d), Math.Max(b, d));
    }

    /// <summary>"x, y" or "x y" or "x;y" as two numbers (a point of the atlas, or a spot inside an island).</summary>
    public static bool TryParsePair(string? text, out double a, out double b)
    {
        (a, b) = (0, 0); if (string.IsNullOrWhiteSpace(text) || text.Length > 40) return false;
        var parts = text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); if (parts.Length != 2) return false;
        return double.TryParse(parts[0].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out a) && double.TryParse(parts[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out b) && double.IsFinite(a) && double.IsFinite(b);
    }

    /// <summary>A place written "Name/x/z" (a name, then the spot inside it).</summary>
    public static bool TryParsePlace(string? text, out string name, out double x, out double z)
    {
        (name, x, z) = (string.Empty, 0, 0); if (string.IsNullOrWhiteSpace(text) || text.Length > 120) return false;
        var parts = text.Split('/', StringSplitOptions.TrimEntries); if (parts.Length != 3 || parts[0].Length == 0) return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x) || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z) || !double.IsFinite(x) || !double.IsFinite(z)) return false;
        name = parts[0]; return true;
    }

    public static string Format(string island, double localX, double localZ) => string.Create(CultureInfo.InvariantCulture, $"{island}/{localX:0}/{localZ:0}");

    /// <summary>Distance and bearing from one point to another on the same plane: metres, and degrees clockwise from north (the atlas has north up, so Y grows to the south).</summary>
    public static (double Meters, double Bearing) Toward(double fromX, double fromY, double toX, double toY)
    {
        var dx = toX - fromX; var dy = toY - fromY; var meters = Math.Sqrt(dx * dx + dy * dy); var bearing = Math.Atan2(dx, -dy) * 180 / Math.PI; return (meters, (bearing + 360) % 360);
    }
}
