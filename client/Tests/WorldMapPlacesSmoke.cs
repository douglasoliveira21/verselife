using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts.Map;

namespace LifeVerse.Client.Tests;

/// <summary>Stage 3 of the world map against a pretend server: landmarks (list, add, errors, manage), parcels over the map, the chosen spot and the teleport to it, and the Terrenos, Pessoas, Grupos and Eventos tabs.</summary>
public partial class WorldMapPlacesSmoke : Node
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), Luna = Guid.NewGuid(), Mark1 = Guid.NewGuid(), Folder1 = Guid.NewGuid(), P1 = Guid.NewGuid(), P2 = Guid.NewGuid(), P3 = Guid.NewGuid(), P4 = Guid.NewGuid(), Friend = Guid.NewGuid(), Event1 = Guid.NewGuid();
    private readonly List<string> _calls = [], _said = [];
    private readonly List<(Guid Id, string Name, float X, float Z, Guid? Folder)> _marks = [];
    private readonly List<(Guid Id, string Name)> _folders = [];
    private (Guid Island, float X, float Z)? _went;

    private static byte[] Png() { var image = Image.CreateEmpty(16, 16, false, Image.Format.Rgb8); image.Fill(Colors.Green); return image.SavePngToBuffer(); }

    private string Atlas() => JsonSerializer.Serialize(new
    {
        items = new[] { (A, "Centro", 0, 0, "open"), (B, "Praia Azul", 1, 0, "open"), (C, "Loja Central", 0, 1, "full") }.Select(i => new
        {
            id = i.Item1, name = i.Item2, description = "Descrição", category = "social", contentRating = "livre", language = "pt-BR", isPublic = true, isWelcome = false, cellX = i.Item3, cellY = i.Item4, x = i.Item3 * 768.0, y = i.Item4 * 768.0, size = 256, spawnX = 0, spawnZ = 0,
            maxPlayers = 30, online = true, visitors = 3, access = i.Item5, coverImageUrl = (string?)null, hasTile = true, tileVersion = "v1", favorite = false, favorites = 0, createdAt = DateTimeOffset.UtcNow.AddDays(-30), isOwner = false, owner = new { id = Luna, displayName = "Luna" }
        }),
        total = 3, cellMeters = 768, tileSize = 256, truncated = false
    });

    private string Panels() => JsonSerializer.Serialize(new
    {
        folders = _folders.Select(f => new { id = f.Id, name = f.Name }),
        landmarks = _marks.Select(m => new { id = m.Id, name = m.Name, description = "", islandId = A, islandName = "Centro", x = m.X, y = 0f, z = m.Z, isPublic = false, folderId = m.Folder, createdAt = DateTimeOffset.UtcNow }),
        favorites = new[] { new { islandId = B, name = "Praia Azul" } }, recent = new[] { new { islandId = A, name = "Centro", x = 5f, z = 6f, at = DateTimeOffset.UtcNow } },
        friends = new[] { new { islandId = B, name = "Praia Azul", ownerName = "Amiga" } },
        groups = new[] { new { groupId = Guid.NewGuid(), name = "Clube", tag = "CLB", color = "#12F2E0", places = new object[] { new { islandId = A, name = "Centro", kind = "headquarters", parcels = 0 }, new { islandId = B, name = "Praia Azul", kind = "land", parcels = 2 } } } },
        limits = new { landmarks = 200, folders = 20 }
    });

    private static object Parcel(Guid id, string name, string state, double minX, double minZ, double maxX, double maxZ, bool teleport = true, long? sale = null) => new
    {
        id, code = "X", name, description = "", category = "residencial", area = (maxX - minX) * (maxZ - minZ), state, bounds = new { minX, minZ, maxX, maxZ }, spawnX = 0, spawnZ = 0, teleport, salePrice = sale, rentPrice = (long?)null, rentPeriodDays = (int?)null, owner = state == "sale" ? "Luna" : null, isGroup = false
    };

    private (int, string) Server(string method, string path, string? body)
    {
        _calls.Add(method + " " + path + (body is null ? "" : " " + body));
        if (method == "GET" && path.StartsWith("/api/v1/map/atlas")) return (200, Atlas());
        if (method == "GET" && path == "/api/v1/map/landmarks") return (200, Panels());
        if (method == "POST" && path == "/api/v1/map/landmarks")
        {
            using var d = JsonDocument.Parse(body!); var r = d.RootElement; var name = r.GetProperty("name").GetString() ?? ""; var island = r.GetProperty("islandId").GetGuid();
            if (island == C) return (403, "{\"error\":\"no_access\"}"); if (name.Length == 0) return (400, "{\"error\":\"bad_name\"}");
            _marks.Add((Guid.NewGuid(), name, r.GetProperty("x").GetSingle(), r.GetProperty("z").GetSingle(), r.TryGetProperty("folderId", out var f) && f.ValueKind == JsonValueKind.String ? f.GetGuid() : null)); return (201, "{}");
        }
        if (method == "DELETE" && path.StartsWith("/api/v1/map/landmarks/")) { var id = Guid.Parse(path.Split('/')[^1]); _marks.RemoveAll(m => m.Id == id); return (204, ""); }
        if (method == "POST" && path == "/api/v1/map/landmark-folders") { using var d = JsonDocument.Parse(body!); _folders.Add((Guid.NewGuid(), d.RootElement.GetProperty("name").GetString()!)); return (201, "{}"); }
        if (method == "GET" && path == $"/api/v1/map/islands/{A}/parcels")
            return (200, JsonSerializer.Serialize(new { islandId = A, size = 256, truncated = false, items = new[] { Parcel(P1, "Minha casa", "mine", -100, -100, -40, -40), Parcel(P2, "Lote à venda", "sale", 0, 0, 50, 50, sale: 5000), Parcel(P3, "Terreno restrito", "restricted", 60, -60, 100, -20, teleport: false), Parcel(P4, "Praça", "common", -30, 40, 20, 90) } }));
        if (method == "GET" && path.StartsWith("/api/v1/map/islands/")) return (200, JsonSerializer.Serialize(new { islandId = Guid.Empty, size = 256, truncated = false, items = Array.Empty<object>() }));
        if (method == "GET" && path == "/api/v1/map/people") return (200, JsonSerializer.Serialize(new[] { new { userId = Friend, name = "Amiga", username = "amiga", islandId = B, islandName = "Praia Azul", x = 10.0, z = 20.0 } }));
        if (method == "POST" && path == "/api/v1/map/arrival")
        {
            using var d = JsonDocument.Parse(body!); var r = d.RootElement; var island = r.GetProperty("islandId").GetGuid(); var x = r.GetProperty("x").GetSingle(); var z = r.GetProperty("z").GetSingle();
            if (island == C) return (200, JsonSerializer.Serialize(new { allowed = false, reason = "full", x, z, parcelId = (Guid?)null, parcelName = (string?)null, forced = false }));
            if (island == A && x is >= 60 and <= 100 && z is >= -60 and <= -20) return (200, JsonSerializer.Serialize(new { allowed = false, reason = "parcel_restricted", x, z, parcelId = (Guid?)null, parcelName = (string?)null, forced = false }));
            if (island == A && x is >= -100 and <= -40 && z is >= -100 and <= -40) return (200, JsonSerializer.Serialize(new { allowed = true, reason = (string?)null, x = 3f, z = 4f, parcelId = P1, parcelName = "Minha casa", forced = true }));
            return (200, JsonSerializer.Serialize(new { allowed = true, reason = (string?)null, x, z, parcelId = (Guid?)null, parcelName = (string?)null, forced = false }));
        }
        if (method == "GET" && path == "/api/v1/discovery/events") return (200, JsonSerializer.Serialize(new[] { new { id = Event1, name = "Festa na praia", islandId = B, startsAt = DateTimeOffset.UtcNow.AddHours(-1), endsAt = DateTimeOffset.UtcNow.AddHours(2), arrivalX = 5.0, arrivalZ = 6.0 } }));
        if (method == "GET" && path.StartsWith("/api/v1/discovery/islands/") && path.EndsWith("/info")) return (200, JsonSerializer.Serialize(new { access = "open" }));
        return (404, "{}");
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Settle() => await ToSignal(GetTree().CreateTimer(1.4), Godot.Timer.SignalName.Timeout);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public override async void _Ready()
    {
        try
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://test_map_places.cfg")); DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://map_search_history.json"));
            _marks.Add((Mark1, "Minha casa", 10, -20, Folder1)); _folders.Add((Folder1, "Casas"));
            var hub = new MapHub { Name = "Hub", JoinIsland = id => _went = (id, float.NaN, float.NaN), GoToPoint = (id, x, z) => _went = (id, x, z), CurrentIsland = () => A, PlayerLocal = () => (10f, 20f, 0f), Say = text => _said.Add(text) }; hub.Settings.Path = "user://test_map_places.cfg"; AddChild(hub);
            hub.Api.Transport = Server; hub.Api.BytesTransport = _ => (200, Png(), "v1"); hub.Tiles.DiskFolder = string.Empty;
            var window = new WorldMapWindow { Name = "Map", Hub = hub }; AddChild(window); await Frames(2); window.WindowForTesting.Size = new Vector2(1280, 760); var canvas = window.Canvas;
            window.Open(); await Frames(4);

            // landmarks column
            Require(window.MarkNamesForTesting.SequenceEqual(["Minha casa"]) && window.FolderNamesForTesting.SequenceEqual(["Casas"]), "the landmarks and folders were read: " + string.Join(",", window.MarkNamesForTesting));
            var buttons = window.PlaceButtonsForTesting; Require(buttons.Contains("Meus Marcos (1)") && buttons.Contains("Favoritos (1)") && buttons.Contains("Visita Recente (1)") && buttons.Contains("Ilhas de Amigos (1)") && buttons.Contains("Grupos (1)"), "the five groups with counts: " + string.Join("|", buttons));
            Require(buttons.Any(b => b.StartsWith("▸ Casas")) && !buttons.Contains("Praia Azul"), "folders are collapsed and other groups start closed: " + string.Join("|", buttons));
            window.ExpandForTesting("f" + Folder1); window.ExpandForTesting("favorites"); window.ExpandForTesting("recent"); window.ExpandForTesting("friends"); window.ExpandForTesting("groups"); Require(window.PlaceButtonsForTesting.Contains("Minha casa") && window.PlaceButtonsForTesting.Contains("Praia Azul"), "expanded groups list their places");
            Require(canvas.Pins.Any(p => p.Kind == "landmark" && p.Name == "Minha casa") && canvas.Pins.Any(p => p.Kind == "friend" && p.Name == "Amiga"), "the landmark and the friend are drawn on the map");
            window.PressPlaceForTesting("Minha casa"); await Settle(); Require(canvas.Selected == A && canvas.Point is { } p0 && p0.X == 10 && p0.Z == -20 && window.CardShownForTesting && window.PointLineForTesting.StartsWith("Ponto: x 10, z -20") && window.TeleportTextForTesting == "Teletransportar até aqui", "a landmark opens its island and its spot: " + window.PointLineForTesting);

            // adding a landmark
            canvas.PickPoint(canvas.Islands.First(i => i.Id == A), 30, 40); window.PressAddMarkForTesting(); await Frames(2); Require(window.AddWindowOpenForTesting, "the add window opens");
            window.FillAddMarkForTesting("   "); await Frames(1); Require(window.AddMarkNoteForTesting.Contains("Dê um nome") && _marks.Count == 1, "an empty name is refused before asking the server");
            window.FillAddMarkForTesting("Mirante", "vista", 1); await Frames(2); Require(!window.AddWindowOpenForTesting && _marks.Count == 2 && _marks[1].Name == "Mirante" && _marks[1].Folder == Folder1 && _marks[1].X == 30 && _marks[1].Z == 40, "the landmark was saved at the chosen spot and folder");
            Require(_calls.Any(c => c.StartsWith("POST /api/v1/map/landmarks") && c.Contains("\"isPublic\":false") && c.Contains("\"name\":\"Mirante\"")) && window.MarkNamesForTesting.Contains("Mirante") && _said.Any(s => s.Contains("Mirante")), "the column was read again");
            canvas.PickPoint(canvas.Islands.First(i => i.Id == C), 1, 1); window.PressAddMarkForTesting(); window.FillAddMarkForTesting("Loja"); await Frames(2); Require(window.AddWindowOpenForTesting && window.AddMarkNoteForTesting.Contains("não pode entrar") && _marks.Count == 2, "a refusal of the server is said in the window");
            canvas.ClearPoint(); canvas.Select(Guid.Empty, announce: false); window.PressAddMarkForTesting(); await Frames(1); Require(window.AddMarkNoteForTesting.Contains("x 10, z 20"), "without a chosen spot the landmark takes where the player stands: " + window.AddMarkNoteForTesting);

            // managing
            window.OpenManageForTesting(); await Frames(2); Require(window.ManageOpenForTesting, "the manage window opens"); window.CreateFolderForTesting("Lojas"); await Frames(2); Require(window.FolderNamesForTesting.SequenceEqual(["Casas", "Lojas"]), "a folder was made");
            var before = _marks.Count; window.PressManageForTesting("Excluir", 0); await Frames(1); Require(_marks.Count == before && !_calls.Any(c => c.StartsWith("DELETE")), "deleting asks once more"); window.PressManageForTesting("Excluir de vez? Confirmar", 0); await Frames(2); Require(_marks.Count == before - 1 && window.MarkNamesForTesting.Count == before - 1, "the second press deletes");

            // parcels over the map
            var a = canvas.Islands.First(i => i.Id == A); canvas.SetView(a.X, a.Y, 1.6); window.SetParcelsForTesting(true); canvas.Layout(); await Frames(3);
            Require(canvas.ShowParcels && window.ParcelLegendForTesting && _calls.Contains($"GET /api/v1/map/islands/{A}/parcels") && canvas.Parcels.TryGetValue(A, out var shown) && shown.Count == 4, "the parcels are asked for the island that is big enough");
            var at = (double lx, double lz) => canvas.ToScreen(a.X + lx, a.Y + lz);
            canvas.ClickForTesting((Vector2)at(20, 20)); await Frames(1); Require(canvas.SelectedParcel == P2 && window.PointLineForTesting.Contains("Lote à venda") && window.PointLineForTesting.Contains("à venda"), "a click picks the spot and its parcel: " + window.PointLineForTesting);
            window.PressTeleportForTesting(); await Frames(2); Require(_went is { } w1 && w1.Island == A && w1.X == 20 && w1.Z == 20 && _calls.Any(c => c.StartsWith("POST /api/v1/map/arrival")), "teleport to the spot asks the server and goes to the answer");
            _went = null; canvas.ClickForTesting((Vector2)at(80, -40)); await Frames(1); window.PressTeleportForTesting(); await Frames(2); Require(_went is null && _said.Last().Contains("restrito"), "a restricted parcel refuses the teleport and says why: " + _said.Last());
            canvas.ClickForTesting((Vector2)at(-70, -70)); await Frames(1); window.PressTeleportForTesting(); await Frames(2); Require(_went is { } w2 && w2.X == 3 && w2.Z == 4 && _said.Last().Contains("leva você"), "a parcel with its own arrival point sends the avatar there: " + _said.Last());
            _went = null; var c = canvas.Islands.First(i => i.Id == C); window.TeleportToPointForTesting(C, 1, 1); await Frames(2); Require(_went is null && _said.Last().Contains("cheia"), "a full island refuses: " + _said.Last());
            window.SetParcelsForTesting(false); Require(!canvas.ShowParcels && !window.ParcelLegendForTesting, "the overlay can be turned off"); _ = c;

            // the tabs
            canvas.Select(A, announce: false); window.SelectTabForTesting(2); await Frames(2); Require(window.ParcelTitleForTesting == "Terrenos de Centro" && window.ParcelRowsForTesting.Count == 4, "Terrenos lists the parcels of the chosen island: " + window.ParcelRowsForTesting.Count);
            window.SetParcelFilterForTesting(2); Require(window.ParcelRowsForTesting.SequenceEqual(["Lote à venda"]), "the filter keeps only those for sale"); window.SetParcelFilterForTesting(5); Require(window.ParcelRowsForTesting.SequenceEqual(["Terreno restrito"]) && !window.ParcelButtonEnabledForTesting(0, "Teletransportar"), "a restricted parcel cannot be teleported to"); window.SetParcelFilterForTesting(0);
            window.PressParcelForTesting(2, "Ver no mapa"); await Settle(); Require(window.TabForTesting == 0 && canvas.ShowParcels && canvas.SelectedParcel != Guid.Empty, "Ver no mapa shows the parcel with the overlay on");
            window.SelectTabForTesting(3); await Frames(2); Require(window.PeopleForTesting.SequenceEqual(["● Amiga"]), "Pessoas lists the friends who can be seen"); _went = null; window.PressPersonForTesting(0, "Teletransportar"); await Frames(2); Require(_went is { } w3 && w3.Island == B && w3.X == 10 && w3.Z == 20, "goes to where the friend is");
            window.SelectTabForTesting(4); await Frames(1); Require(window.GroupRowsForTesting.Count == 3 && window.GroupRowsForTesting[0].StartsWith("Clube"), "Grupos lists the places of the group: " + string.Join("|", window.GroupRowsForTesting)); _went = null; window.PressGroupPlaceForTesting(1, "Teletransportar"); await Frames(2);
            window.SelectTabForTesting(5); await Frames(2); Require(window.EventRowsForTesting.SequenceEqual(["Festa na praia"]), "Eventos lists the events"); _went = null; window.PressEventForTesting(0, "Teletransportar"); await Frames(2); Require(_went is { } w4 && w4.Island == B && w4.X == 5 && w4.Z == 6, "goes to the arrival point of the event");

            // errors
            window.SelectTabForTesting(0); hub.Api.Transport = (m, p, b) => p.StartsWith("/api/v1/map/landmarks") && m == "GET" ? (500, "{}") : Server(m, p, b); window.LoadPanels(); await Frames(1); Require(window.PanelsNoteForTesting.Contains("HTTP 500"), "a failure of the column is said, not silent");
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://test_map_places.cfg"));
            GD.Print("event=world_map_places_smoke result=passed"); GetTree().Quit();
        }
        catch (Exception exception)
        {
            var where = exception.StackTrace?.Split((char)10).Where(l => l.Contains("WorldMapPlacesSmoke")).Take(2).Aggregate("", (s, l) => s + l.Trim() + " ; ");
            GD.PushError($"event=world_map_places_smoke result=failed error={exception.Message} at {where}"); GetTree().Quit(1);
        }
    }
}
