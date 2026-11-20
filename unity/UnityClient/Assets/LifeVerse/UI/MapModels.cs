using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace LifeVerse.Unity.UI
{
    /// <summary>One island on the atlas, as the server describes it to this player: where it stands (cell and meters), what it is, how full it is and whether this player can come in.</summary>
    public sealed class AtlasIsland
    {
        public Guid Id, OwnerId; public string Name = "", Description = "", Category = "", Rating = "", Language = "", Access = "", OwnerName = "", CoverImageUrl, TileVersion = "";
        public bool IsPublic, IsWelcome, Online, HasTile, Favorite, IsOwner; public int CellX, CellY, Size, MaxPlayers, Favorites; public int? Visitors; public double X, Y; public float SpawnX, SpawnZ;
        /// <summary>The player can enter (the server says so; this only reads it).</summary>
        public bool CanEnter => Access == "open";
        public string AccessWords => Access switch { "open" => "Aberta", "full" => "Cheia", "closed" => "Fechada", "private" => "Privada", "banned" => "Acesso negado", _ => "Indisponível" };
    }

    /// <summary>A parcel of an island as the map shows it: its square in meters from the island's centre, what it is to this player (theirs, of their group, for sale, for rent, open, restricted, common) and, when the platform says so, its price and owner.</summary>
    public sealed class MapParcel
    {
        public Guid Id; public string Code = "", Name = "", State = "public", Category = "", Owner = ""; public double Area, MinX, MinZ, MaxX, MaxZ; public float SpawnX, SpawnZ; public bool Teleport; public long? SalePrice, RentPrice; public int? RentPeriodDays;
        public bool Contains(double x, double z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
        public string StateWords => MapApi.ParcelWords(State);
    }

    public sealed class AtlasPage
    {
        public IReadOnlyList<AtlasIsland> Items = new List<AtlasIsland>(); public int Total, CellMeters = 768; public bool Truncated;
        public static AtlasPage Empty => new AtlasPage();
    }

    /// <summary>What the map asks the server, and how the answers are read. No view code, so it can be tested alone.</summary>
    public static class MapApi
    {
        public static string AtlasPath(string text = null, string category = null, IEnumerable<string> ratings = null, int limit = 300)
        {
            var parts = new List<string> { "limit=" + Math.Max(1, Math.Min(limit, 500)) };
            if (!string.IsNullOrWhiteSpace(text)) parts.Add("q=" + Uri.EscapeDataString(text.Trim()));
            if (!string.IsNullOrWhiteSpace(category)) parts.Add("category=" + Uri.EscapeDataString(category.Trim()));
            var list = ratings?.ToList(); if (list != null && list.Count > 0) parts.Add("ratings=" + Uri.EscapeDataString(string.Join(",", list)));
            return "/api/v1/map/atlas?" + string.Join("&", parts);
        }

        public static string ParcelsPath(Guid island) => "/api/v1/map/islands/" + island + "/parcels";

        public static string ParcelWords(string state) => state switch { "mine" => "Seu terreno", "group" => "Do seu grupo", "restricted" => "Restrito", "sale" => "À venda", "rent" => "Para alugar", "common" => "Área comum", _ => "Aberto" };

        public static UnityEngine.Color ParcelColor(string state) => state switch
        {
            "mine" => new UnityEngine.Color(.18f, .85f, .75f), "group" => new UnityEngine.Color(.5f, .66f, 1f), "restricted" => new UnityEngine.Color(.9f, .3f, .3f), "sale" => new UnityEngine.Color(1f, .6f, .2f), "rent" => new UnityEngine.Color(1f, .82f, .48f), "common" => new UnityEngine.Color(.66f, .73f, .76f), _ => new UnityEngine.Color(.35f, .85f, .45f),
        };

        /// <summary>The parcels of an island, as <c>GET /api/v1/map/islands/{id}/parcels</c> answers; parcels without a square are skipped.</summary>
        public static List<MapParcel> ParseParcels(string json)
        {
            var list = new List<MapParcel>();
            try
            {
                using var document = JsonDocument.Parse(json); var root = document.RootElement; if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return list;
                foreach (var p in items.EnumerateArray())
                {
                    if (p.ValueKind != JsonValueKind.Object || !Guid.TryParse(Str(p, "id"), out var id) || !p.TryGetProperty("bounds", out var b) || b.ValueKind != JsonValueKind.Object) continue;
                    long? Money(string key) => p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : (long?)null;
                    var parcel = new MapParcel { Id = id, Code = Str(p, "code"), Name = Str(p, "name"), State = Str(p, "state") is { Length: > 0 } s ? s : "public", Category = Str(p, "category"), Owner = Str(p, "owner"), Area = Num(p, "area"), MinX = Num(b, "minX"), MinZ = Num(b, "minZ"), MaxX = Num(b, "maxX"), MaxZ = Num(b, "maxZ"), SpawnX = (float)Num(p, "spawnX"), SpawnZ = (float)Num(p, "spawnZ"), Teleport = Flag(p, "teleport"), SalePrice = Money("salePrice"), RentPrice = Money("rentPrice"), RentPeriodDays = p.TryGetProperty("rentPeriodDays", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out var days) ? days : (int?)null };
                    if (parcel.MaxX > parcel.MinX && parcel.MaxZ > parcel.MinZ) list.Add(parcel);
                }
            }
            catch (JsonException) { }
            return list;
        }

        public static string TilePath(Guid island) => "/api/v1/map/islands/" + island + "/tile";

        public static AtlasPage ParseAtlas(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json); var root = document.RootElement; if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("items", out var array) || array.ValueKind != JsonValueKind.Array) return AtlasPage.Empty;
                var items = new List<AtlasIsland>();
                foreach (var e in array.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object || !Guid.TryParse(Str(e, "id"), out var id)) continue; var owner = e.TryGetProperty("owner", out var o) && o.ValueKind == JsonValueKind.Object ? o : default;
                    items.Add(new AtlasIsland
                    {
                        Id = id, Name = Str(e, "name"), Description = Str(e, "description"), Category = Str(e, "category"), Rating = Str(e, "contentRating"), Language = Str(e, "language"), IsPublic = Flag(e, "isPublic"), IsWelcome = Flag(e, "isWelcome"),
                        CellX = Int(e, "cellX"), CellY = Int(e, "cellY"), X = Num(e, "x"), Y = Num(e, "y"), Size = Int(e, "size"), SpawnX = (float)Num(e, "spawnX"), SpawnZ = (float)Num(e, "spawnZ"), MaxPlayers = Int(e, "maxPlayers"), Online = Flag(e, "online"),
                        Visitors = e.TryGetProperty("visitors", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : (int?)null, Access = Str(e, "access"), CoverImageUrl = Str(e, "coverImageUrl") is { Length: > 0 } cover ? cover : null, HasTile = Flag(e, "hasTile"), TileVersion = Str(e, "tileVersion"),
                        Favorite = Flag(e, "favorite"), IsOwner = Flag(e, "isOwner"), OwnerId = Guid.TryParse(Str(owner, "id"), out var ownerId) ? ownerId : Guid.Empty, OwnerName = Str(owner, "displayName"), Favorites = Int(e, "favorites"),
                    });
                }
                return new AtlasPage { Items = items, Total = Int(root, "total"), CellMeters = Int(root, "cellMeters") is var m && m > 0 ? m : 768, Truncated = Flag(root, "truncated") };
            }
            catch (JsonException) { return AtlasPage.Empty; }
        }

        private static string Str(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        private static bool Flag(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
        private static int Int(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
        private static double Num(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) ? n : 0;
    }

    /// <summary>
    /// Where things are on the map. Two kinds of coordinates are kept apart: the global ones place an island on the atlas (meters, the centre of its cell), and the local ones place a spot inside an island (meters from its centre,
    /// the same ones the game uses while playing). Nothing here moves anybody.
    /// </summary>
    public static class MapCoordinates
    {
        public static (double MinX, double MinY, double MaxX, double MaxY) Rect(AtlasIsland island) { var half = island.Size / 2.0; return (island.X - half, island.Y - half, island.X + half, island.Y + half); }
        public static bool Contains(AtlasIsland island, double globalX, double globalY) { var r = Rect(island); return globalX >= r.MinX && globalX <= r.MaxX && globalY >= r.MinY && globalY <= r.MaxY; }
        public static (double X, double Y) ToGlobal(AtlasIsland island, double localX, double localZ) => (island.X + localX, island.Y + localZ);

        /// <summary>A point of the atlas as a spot inside the island (clamped to its ground), and whether the point was really inside.</summary>
        public static (double LocalX, double LocalZ, bool Inside) ToLocal(AtlasIsland island, double globalX, double globalY)
        {
            var half = island.Size / 2.0; var x = globalX - island.X; var z = globalY - island.Y;
            return (Math.Max(-half, Math.Min(x, half)), Math.Max(-half, Math.Min(z, half)), Math.Abs(x) <= half && Math.Abs(z) <= half);
        }

        public static AtlasIsland IslandAt(IEnumerable<AtlasIsland> islands, double globalX, double globalY) => islands.FirstOrDefault(i => Contains(i, globalX, globalY));

        /// <summary>"x, y" or "x y" or "x;y" as two numbers (a spot inside an island).</summary>
        public static bool TryParsePair(string text, out double a, out double b)
        {
            a = b = 0; if (string.IsNullOrWhiteSpace(text) || text.Length > 40) return false;
            var parts = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries); if (parts.Length != 2) return false;
            return double.TryParse(parts[0].Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out a) && double.TryParse(parts[1].Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out b) && !double.IsNaN(a) && !double.IsInfinity(a) && !double.IsNaN(b) && !double.IsInfinity(b);
        }

        /// <summary>Distance and bearing from one point to another: metres, and degrees clockwise from north (north is up on the atlas, so Y grows to the south).</summary>
        public static (double Meters, double Bearing) Toward(double fromX, double fromY, double toX, double toY)
        {
            var dx = toX - fromX; var dy = toY - fromY; var meters = Math.Sqrt(dx * dx + dy * dy); var bearing = Math.Atan2(dx, -dy) * 180 / Math.PI; return (meters, (bearing + 360) % 360);
        }
    }
}
