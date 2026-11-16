using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts.Map;

namespace LifeVerse.Client.Tests;

/// <summary>The base the two maps share: coordinates and their text forms, the questions to the server and how answers are read, the atlas service, and the cache of pictures (memory, disk, versions, "no picture", limits).</summary>
public partial class MapBaseSmoke : Node
{
    private static byte[] Png(Color color) { var image = Image.CreateEmpty(8, 8, false, Image.Format.Rgb8); image.Fill(color); return image.SavePngToBuffer(); }

    private static string AtlasJson(params (Guid Id, string Name, int CellX, int CellY, bool Pub)[] islands) =>
        JsonSerializer.Serialize(new { items = islands.Select(i => new { id = i.Id, name = i.Name, description = "d", category = "social", contentRating = "livre", language = "pt-BR", isPublic = i.Pub, isWelcome = i.CellX == 0 && i.CellY == 0, cellX = i.CellX, cellY = i.CellY, x = i.CellX * 768.0, y = i.CellY * 768.0, size = 256, spawnX = 0f, spawnZ = 0f, maxPlayers = 64, online = i.Pub, visitors = i.Pub ? (int?)3 : null, access = i.Pub ? "open" : "private", coverImageUrl = (string?)null, hasTile = true, tileVersion = "v1", favorite = false, isOwner = false, owner = i.Pub ? new { id = Guid.NewGuid(), displayName = "Luna" } : null }), total = islands.Length, cellMeters = 768, tileSize = 256, truncated = false });

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public override void _Ready()
    {
        try
        {
            // coordinates: the global ones place the island, the local ones a spot inside it
            var id = Guid.NewGuid(); var island = new AtlasIsland(id, "Ilha", "", "social", "livre", "pt-BR", true, false, 2, -1, 1536, -768, 256, 0, 0, 64, true, 3, "open", null, true, "v1", false, false, Guid.Empty, "");
            Require(MapCoordinates.Rect(island) == (1408, -896, 1664, -640), "an island covers its size around the centre of its cell");
            Require(MapCoordinates.Contains(island, 1536, -768) && MapCoordinates.Contains(island, 1408, -896) && !MapCoordinates.Contains(island, 1407, -768), "inside and outside");
            Require(MapCoordinates.ToGlobal(island, 10, -20) == (1546, -788), "a local spot as a global point");
            var (lx, lz, inside) = MapCoordinates.ToLocal(island, 1546, -788); Require(lx == 10 && lz == -20 && inside, "and back");
            var (cx, cz, outside) = MapCoordinates.ToLocal(island, 5000, -5000); Require(cx == 128 && cz == -128 && !outside, "a point outside is clamped to the ground and said to be outside");
            var other = island with { Id = Guid.NewGuid(), X = 0, Y = 0, CellX = 0, CellY = 0 };
            Require(MapCoordinates.IslandAt([island, other], 3, 4) == other && MapCoordinates.IslandAt([island, other], 400, 400) is null, "the island under a point");
            Require(MapCoordinates.CellOf(1536, -768, 768) == (2, -1) && MapCoordinates.CellsOf(-100, -100, 800, 900, 768) == (0, 1, 0, 1) && MapCoordinates.CellsOf(800, 900, -100, -100, 768) == (0, 1, 0, 1), "cells of a point and of a rectangle (any corner order)");
            foreach (var (text, ea, eb) in new[] { ("12, -3", 12.0, -3.0), ("12 -3", 12, -3), ("12;-3", 12, -3), ("1.5,2.25", 1.5, 2.25), (" 7 , 8 ", 7, 8) }) Require(MapCoordinates.TryParsePair(text, out var pa, out var pb) && pa == ea && pb == eb, "a pair: " + text);
            foreach (var notPair in new[] { "", "12", "a,b", "1,2,3", "NaN,1", "∞,1", new string('1', 50) }) Require(!MapCoordinates.TryParsePair(notPair, out _, out _), "not a pair: " + notPair);
            Require(MapCoordinates.TryParsePlace("Praia Azul/120/-40", out var name, out var px, out var pz) && name == "Praia Azul" && px == 120 && pz == -40 && !MapCoordinates.TryParsePlace("Praia/1", out _, out _, out _) && !MapCoordinates.TryParsePlace("/1/2", out _, out _, out _), "a place written Name/x/z");
            Require(MapCoordinates.Format("Praia Azul", 120.4f, -40.6f) == "Praia Azul/120/-41", "a place as text, with dots always: " + MapCoordinates.Format("Praia Azul", 120.4f, -40.6f));
            Require(MapCoordinates.Toward(0, 0, 0, -100) is { Meters: 100, Bearing: 0 } && MapCoordinates.Toward(0, 0, 100, 0) is { Bearing: 90 } && MapCoordinates.Toward(0, 0, 0, 100) is { Bearing: 180 } && MapCoordinates.Toward(0, 0, -100, 0) is { Bearing: 270 }, "north is up, east is right: distance and bearing");

            // the questions and the answers
            Require(MapApi.AtlasPath() == "/api/v1/map/atlas?limit=300" && MapApi.AtlasPath("praia", "social", ["livre", "moderado"], (-1, 2, 0, 3), 999) == "/api/v1/map/atlas?limit=500&q=praia&category=social&ratings=livre%2Cmoderado&minX=-1&maxX=2&minY=0&maxY=3", "the atlas path");
            var a = Guid.NewGuid(); var b2 = Guid.NewGuid(); var page = MapApi.ParseAtlas(AtlasJson((a, "Centro", 0, 0, true), (b2, "Reservada", 1, 0, false)));
            Require(page.Items.Count == 2 && page.Total == 2 && page.CellMeters == 768 && page.Items[0].IsWelcome && page.Items[0].CanEnter && page.Items[0].OwnerName == "Luna" && page.Items[0].Visitors == 3 && page.Items[1].OwnerName == "" && page.Items[1].Visitors is null && !page.Items[1].CanEnter && page.Items[1].AccessWords == "Privada" && page.Items[1].X == 768, "the atlas is read");
            Require(MapApi.ParseAtlas("not json").Items.Count == 0 && MapApi.ParseAtlas("{}").Items.Count == 0 && MapApi.ParseAtlas("{\"items\":[{\"name\":\"sem id\"},1,null]}").Items.Count == 0, "bad answers are empty");

            // the atlas service
            var api = new MapApiClient { Name = "Api" }; AddChild(api); var calls = new List<string>(); var status = 200;
            api.Transport = (method, path, body) => { calls.Add(method + " " + path); return (status, status == 200 ? AtlasJson((a, "Centro", 0, 0, true)) : "{}"); };
            var atlas = new MapAtlasService { Name = "Atlas", Api = api }; AddChild(atlas); var changed = 0; var failed = new List<int>(); atlas.Changed += () => changed++; atlas.Failed += s => failed.Add(s);
            atlas.Load("praia"); Require(atlas.Page.Items.Count == 1 && changed == 1 && !atlas.Loading && calls[0] == "GET /api/v1/map/atlas?limit=300&q=praia" && atlas.Find(a)?.Name == "Centro" && atlas.Find(Guid.NewGuid()) is null, "the atlas loads and is kept");
            status = 500; atlas.Load(); Require(failed.SequenceEqual([500]) && atlas.Page.Items.Count == 1, "a failure is told and what was there stays"); status = 0; atlas.Load(); Require(failed.SequenceEqual([500, 0]), "no connection is status 0");
            status = -1; atlas.Load(); Require(atlas.Loading, "while waiting it is loading"); atlas.Cancel(); Require(!atlas.Loading, "and a request can be forgotten");

            // the cache of pictures
            var folder = "user://test_map_cache"; foreach (var f in DirAccess.GetFilesAt(folder)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(folder + "/" + f));
            var fetches = new List<string>(); var respond = 200; var etag = "v2"; var now = DateTimeOffset.UtcNow;
            api.BytesTransport = path => { fetches.Add(path); return respond < 0 ? (-1, [], null) : respond == 200 ? (200, Png(Colors.Green), etag) : (respond, [], null); };
            var cache = new MapTileCache { Name = "Tiles", Api = api, DiskFolder = folder, Clock = () => now, MemoryTiles = 2 }; AddChild(cache);
            Texture2D? got = null; var answered = 0; void Take(Texture2D? t) { got = t; answered++; }
            cache.Get(a, "v1", Take); Require(answered == 1 && got is { } && got.GetWidth() == 8 && fetches.Count == 1 && fetches[0] == $"/api/v1/map/islands/{a}/tile" && cache.Has(a, "v2") && !cache.Has(a, "v1"), "a picture is fetched once and kept under the version the server sent");
            cache.Get(a, "v2", Take); Require(answered == 2 && fetches.Count == 1, "asked again at that version: no request");
            cache.Get(a, "v3", Take); Require(fetches.Count == 2, "another version is fetched again");

            // nothing there: remembered for a while
            respond = 404; cache.Get(b2, "v1", Take); Require(got is null && answered == 4 && fetches.Count == 3, "an island with no picture answers null"); cache.Get(b2, "v1", Take); Require(fetches.Count == 3 && got is null, "and is not asked again at once");
            now = now.AddMinutes(6); respond = 200; cache.Get(b2, "v1", Take); Require(fetches.Count == 4 && got is { }, "after a while it is asked again");

            // bad bytes are no picture
            respond = 200; var bad = Guid.NewGuid(); api.BytesTransport = path => { fetches.Add(path); return (200, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17], "x"); }; cache.Get(bad, "v1", Take); Require(got is null && !cache.Has(bad, "x"), "bytes that are not a PNG are not kept");
            api.BytesTransport = path => { fetches.Add(path); return (200, Png(Colors.Blue), "v1"); };

            // limit of memory: the least recently used goes first
            var c1 = Guid.NewGuid(); var c2 = Guid.NewGuid(); var c3 = Guid.NewGuid(); cache.Clear();
            cache.Get(c1, "v1", Take); cache.Get(c2, "v1", Take); cache.Get(c1, "v1", Take); cache.Get(c3, "v1", Take);
            Require(cache.CachedCount == 2 && cache.Has(c1, "v1") && !cache.Has(c2, "v1") && cache.Has(c3, "v1"), "the least recently used picture is let go");

            // the disk keeps what the next session needs
            var disk = new MapTileCache { Name = "Tiles2", Api = api, DiskFolder = folder, Clock = () => now }; AddChild(disk); var before = fetches.Count;
            disk.Get(c3, "v1", Take); Require(fetches.Count == before && got is { } && disk.FetchesStarted == 0, "a new session reads the picture from disk without asking");
            disk.Get(c3, "v9", Take); Require(fetches.Count == before + 1, "unless the version moved on");

            // on the way: joined, limited, cancelled
            var slow = new MapTileCache { Name = "Tiles3", Api = api, DiskFolder = string.Empty }; AddChild(slow); api.BytesTransport = path => { fetches.Add(path); return (-1, [], null); };
            var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList(); foreach (var i in ids) slow.Get(i, "v1", Take); slow.Get(ids[0], "v1", Take);
            Require(slow.PendingCount == 5 && slow.FetchesStarted == MapTileCache.MaxParallel, "no more than a few pictures are asked at once, and the same one is not asked twice");
            slow.CancelExcept(new HashSet<Guid> { ids[4] }); Require(slow.PendingCount == 1, "what is no longer wanted is dropped");
            slow.Clear(); Require(slow.PendingCount == 0 && slow.CachedCount == 0, "and everything can be forgotten");

            foreach (var f in DirAccess.GetFilesAt(folder)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(folder + "/" + f));
            GD.Print("event=map_base_smoke result=passed"); GetTree().Quit();
        }
        catch (Exception exception)
        {
            var where = exception.StackTrace?.Split((char)10).Where(l => l.Contains("MapBaseSmoke")).Take(2).Aggregate("", (s, l) => s + l.Trim() + " ; ");
            GD.PushError($"event=map_base_smoke result=failed error={exception.Message} at {where}"); GetTree().Quit(1);
        }
    }
}
