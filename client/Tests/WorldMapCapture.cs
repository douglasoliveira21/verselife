using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Map;

namespace LifeVerse.Client.Tests;

/// <summary>Windowed run that opens the world map over the world, against a pretend server, and saves screenshots to LIFEVERSE_CAPTURE_PATH.</summary>
public partial class WorldMapCapture : Node
{
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), Godot.Timer.SignalName.Timeout);

    private static byte[] Tile(int seed, Color land)
    {
        var noise = new FastNoiseLite { Seed = seed, Frequency = .035f, FractalOctaves = 4 }; const int size = 128; var image = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
        {
            var d = new Vector2(x - size / 2f, y - size / 2f).Length() / (size / 2f); var h = noise.GetNoise2D(x, y) * .35f + (1f - d * 1.15f); Color c;
            if (h < 0) c = new Color("#58a8d4").Lerp(new Color("#133f73"), Mathf.Clamp(-h * 3f, 0, 1)); else if (h < .08f) c = new Color("#d8c995"); else c = land.Lerp(new Color("#3f7a35"), Mathf.Clamp(noise.GetNoise2D(x * 2, y * 2) + .3f, 0, 1)).Lightened(Mathf.Clamp(h * .25f, 0, .3f));
            image.SetPixel(x, y, c);
        }
        return image.SavePngToBuffer();
    }

    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world); await Frames(40);
            if (world.FindChild("Entrar_em_uma_ilha", true, false) is Control join) join.Visible = false;
            var folderId = Guid.NewGuid(); var hub = (MapHub)world.FindChild("MapHub", true, false); var window = (WorldMapWindow)world.FindChild("WorldMapWindow", true, false); var path = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH");
            hub.Settings.Path = "user://capture_map.cfg"; DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://capture_map.cfg")); hub.Settings.HasView = false; hub.Settings.Category = string.Empty; hub.Tiles.DiskFolder = string.Empty;
            var names = new[] { ("Ilha Esmeralda", "social", "livre", 28, "open", true), ("VerseLife Brasil", "social", "livre", 41, "open", false), ("Praia Azul", "residencial", "livre", 3, "open", false), ("Centro Comercial", "comercial", "livre", 60, "full", false), ("Vila dos Artistas", "residencial", "livre", 8, "open", false),
                ("Porto Norte", "comercial", "moderado", 12, "open", false), ("Clube Lunar", "social", "moderado", 2, "closed", false), ("Reserva Natural", "social", "livre", 0, "private", false), ("Arena Games", "social", "livre", 17, "open", false), ("Mercado Sul", "comercial", "livre", 22, "open", false), ("Vale Verde", "residencial", "livre", 5, "open", false), ("Ilha do Sol", "social", "livre", 1, "open", false) };
            var cells = Enumerable.Range(0, names.Length).Select(i => (X: new[] { 0, 1, 1, 0, -1, -1, -1, 0, 1, 2, 2, 2 }[i], Y: new[] { 0, 0, 1, 1, 1, 0, -1, -1, -1, -1, 0, 1 }[i])).ToArray(); var ids = names.Select(_ => Guid.NewGuid()).ToArray(); var events = new HashSet<int> { 1, 4, 8 };
            hub.CurrentIsland = () => ids[0]; hub.PlayerLocal = () => (30f, -20f, 40f);
            hub.Api.Transport = (method, route, body) =>
            {
                if (route.StartsWith("/api/v1/map/atlas")) return (200, JsonSerializer.Serialize(new { items = names.Select((n, i) => (object)new { id = ids[i], name = n.Item1, description = i == 0 ? "Uma ilha tropical com praias, florestas e muitas atividades sociais." : "Uma ilha do LifeVerse.", category = n.Item2, contentRating = n.Item3, language = "pt-BR", isPublic = n.Item5 != "private", isWelcome = i == 0, cellX = cells[i].X, cellY = cells[i].Y, x = cells[i].X * 768.0, y = cells[i].Y * 768.0, size = 256, spawnX = 0f, spawnZ = 0f, maxPlayers = 60, online = n.Item4 > 0, visitors = n.Item4 > 0 ? (int?)n.Item4 : null, access = n.Item5, coverImageUrl = (string?)null, hasTile = true, tileVersion = "v1", favorite = i == 2, favorites = n.Item4 / 4, createdAt = DateTimeOffset.UtcNow.AddDays(-(i * 40)), isOwner = false, owner = new { id = Guid.NewGuid(), displayName = "Luna" } }), total = names.Length, cellMeters = 768, tileSize = 256, truncated = false }));
                if (route == "/api/v1/discovery/events") return (200, "[" + string.Join(",", events.Select(i => $"{{\"id\":\"{Guid.NewGuid()}\",\"islandId\":\"{ids[i]}\"}}")) + "]");
                if (route == "/api/v1/map/landmarks") return (200, JsonSerializer.Serialize(new { folders = new[] { new { id = folderId, name = "Casas" } }, landmarks = new[] { new { id = Guid.NewGuid(), name = "Minha casa", description = "", islandId = ids[0], islandName = names[0].Item1, x = 40f, y = 0f, z = 30f, isPublic = false, folderId = (Guid?)folderId, createdAt = DateTimeOffset.UtcNow }, new { id = Guid.NewGuid(), name = "Mirante", description = "", islandId = ids[0], islandName = names[0].Item1, x = -60f, y = 0f, z = -50f, isPublic = false, folderId = (Guid?)null, createdAt = DateTimeOffset.UtcNow } }, favorites = new[] { new { islandId = ids[2], name = names[2].Item1 } }, recent = new[] { new { islandId = ids[1], name = names[1].Item1, x = 0f, z = 0f, at = DateTimeOffset.UtcNow } }, friends = new[] { new { islandId = ids[4], name = names[4].Item1, ownerName = "Amiga" } }, groups = new[] { new { groupId = Guid.NewGuid(), name = "Clube Lunar", tag = "CLB", color = "#12F2E0", places = new[] { new { islandId = ids[6], name = names[6].Item1, kind = "headquarters", parcels = 0 } } } }, limits = new { landmarks = 200, folders = 20 } }));
                if (route == $"/api/v1/map/islands/{ids[0]}/parcels") { object P(string name, string state, double a, double b, double c, double d, long? sale = null) => new { id = Guid.NewGuid(), code = "X", name, description = "", category = "residencial", area = (c - a) * (d - b), state, bounds = new { minX = a, minZ = b, maxX = c, maxZ = d }, spawnX = 0, spawnZ = 0, teleport = state != "restricted", salePrice = sale, rentPrice = state == "rent" ? (long?)30 : null, rentPeriodDays = (int?)null, owner = state is "sale" or "rent" ? "Luna" : null, isGroup = false }; return (200, JsonSerializer.Serialize(new { islandId = ids[0], size = 256, truncated = false, items = new[] { P("Minha casa", "mine", 20, 10, 70, 60), P("Lote à venda", "sale", -50, 10, -5, 60, 5000), P("Aluguel", "rent", 80, 10, 120, 60), P("Reservado", "restricted", -50, -70, 0, -20), P("Praça central", "common", 10, -70, 60, -20), P("Aberto", "public", 70, -70, 120, -20), P("Do grupo", "group", -110, 10, -60, 60) } })); }
                if (route == "/api/v1/map/people") return (200, JsonSerializer.Serialize(new[] { new { userId = Guid.NewGuid(), name = "Marina", username = "marina", islandId = ids[0], islandName = names[0].Item1, x = 10.0, z = 0.0 }, new { userId = Guid.NewGuid(), name = "Rafa", username = "rafa", islandId = ids[2], islandName = names[2].Item1, x = -20.0, z = 40.0 } }));
                return (404, "{}");
            };
            hub.Api.BytesTransport = route => { var index = Array.FindIndex(ids, id => route.Contains(id.ToString())); return (200, Tile(index + 3, new[] { new Color("#5a9a47"), new Color("#6aa04f"), new Color("#7fae55"), new Color("#8a7d4a") }[index % 4]), "v1"); };
            window.Open(); await Wait(1.5); window.Canvas.FitAll(); await Wait(1.2); Save(path, "mundo-ajustado");
            window.Canvas.ClickForTesting(window.Canvas.ToScreen(0, 0)); window.Canvas.GlideTo(0, 0, .9); await Wait(1.5); Save(path, "mundo-cartao");
            window.Canvas.SetView(768, 384, .4); window.Canvas.ClickForTesting(window.Canvas.ToScreen(768, 768)); await Wait(.8); Save(path, "mundo-cheia");
            window.Canvas.SetView(0, 0, .05); await Wait(.8); Save(path, "mundo-longe");
            window.SelectCategory("comercial"); await Wait(1.2); Save(path, "mundo-comercial"); window.SelectCategory("comercial");
            window.SetQueryForTesting("praia"); await Wait(.6); Save(path, "mundo-busca"); window.SetQueryForTesting("");
            window.SelectTabForTesting(1); await Wait(1.0); Save(path, "regioes");
            window.WindowForTesting.Size = new Vector2(1280, 760); window.SelectTabForTesting(0); window.ExpandForTesting("f" + folderId); window.ExpandForTesting("favorites"); window.ScrollLeftForTesting(560); await Wait(.6); Save(path, "e3-marcos"); window.ScrollLeftForTesting(0);
            var centro = window.Canvas.Islands.First(i => i.Name == "Ilha Esmeralda"); window.SetParcelsForTesting(true); window.Canvas.SetView(centro.X, centro.Y, 1.7); await Wait(1.2); Save(path, "e3-terrenos");
            window.Canvas.ClickForTesting(window.Canvas.ToScreen(centro.X - 25, centro.Y + 30)); await Wait(.8); Save(path, "e3-ponto");
            window.SelectTabForTesting(2); await Wait(.8); Save(path, "e3-aba-terrenos"); window.SelectTabForTesting(3); await Wait(.8); Save(path, "e3-pessoas"); window.SelectTabForTesting(4); await Wait(.6); Save(path, "e3-grupos"); window.SelectTabForTesting(5); await Wait(.6); Save(path, "e3-eventos");
            window.SelectTabForTesting(0); window.OpenManageForTesting(); await Wait(.8); Save(path, "e3-gerenciar"); window.Canvas.PickPoint(centro, 10, 10); window.PressAddMarkForTesting(); await Wait(.8); Save(path, "e3-adicionar");
            window.SelectTabForTesting(0); window.WindowForTesting.Size = new Vector2(620, 640); await Wait(.8); Save(path, "estreito");
            MiniMap? FindMini(Node n) { if (n is MiniMap m) return m; foreach (var c in n.GetChildren()) if (FindMini(c) is { } r) return r; return null; }
            if (FindMini(world) is { } mini) { mini.Hub ??= hub; window.Close(); var a0 = window.Canvas.Islands.First(); hub.SetDestination(new MapDestination(a0.Id, a0.Name, a0.X + 60, a0.Y - 40, 60f, -40f)); await Wait(.8); Save(path, "e4-mini-destino"); hub.SetDestination(new MapDestination(ids[2], names[2].Item1, 768, 768, 0f, 0f)); mini.Rotates = true; await Wait(.8); Save(path, "e4-mini-seta"); mini.Rotates = false; hub.ClearDestination(); }
            hub.Api.Transport = (m, r, b) => (0, ""); window.Reload(); await Wait(.6); Save(path, "erro");
            GD.Print("event=world_map_capture result=passed"); GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=world_map_capture result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private void Save(string path, string name) { if (!string.IsNullOrWhiteSpace(path)) GetViewport().GetTexture().GetImage().SavePng($"{path}-{name}.png"); GD.Print($"event=world_map_capture name={name}"); }
}
