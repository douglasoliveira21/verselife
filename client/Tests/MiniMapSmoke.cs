using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Map;

namespace LifeVerse.Client.Tests;

/// <summary>The circular mini-map: zoom and its limits, remembered preferences, orientation (north fixed or turning with the camera), the destination (pin inside, arrow at the edge with the distance, arrival), the menu, and sharing with the world map through the hub.</summary>
public partial class MiniMapSmoke : Node
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();
    private readonly List<string> _said = [];

    private (int, string) Server(string method, string path, string? body)
    {
        if (path.StartsWith("/api/v1/map/atlas")) return (200, JsonSerializer.Serialize(new { items = new[] { (A, "Centro", 0), (B, "Praia Azul", 1) }.Select(i => new { id = i.Item1, name = i.Item2, description = "", category = "social", contentRating = "livre", language = "pt-BR", isPublic = true, isWelcome = false, cellX = i.Item3, cellY = 0, x = i.Item3 * 768.0, y = 0.0, size = 256, spawnX = 0, spawnZ = 0, maxPlayers = 30, online = true, visitors = 1, access = "open", coverImageUrl = (string?)null, hasTile = false, tileVersion = "", favorite = false, favorites = 0, createdAt = DateTimeOffset.UtcNow, isOwner = false, owner = new { id = Guid.NewGuid(), displayName = "Luna" } }), total = 2, cellMeters = 768, tileSize = 256, truncated = false }));
        if (path == "/api/v1/map/people") return (200, JsonSerializer.Serialize(new[] { new { userId = Guid.NewGuid(), name = "Bia", username = "bia", islandId = A, islandName = "Centro", x = 50.0, z = 0.0 } }));
        return (404, "{}");
    }

    private static bool SettingsCatalogHas(string id) => LifeVerse.Client.Scripts.Settings.SettingsCatalog.All.Any(d => d.Id == id);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    public override async void _Ready()
    {
        try
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://test_mini.cfg")); var opened = 0; var here = A; var map = default(MiniMap)!;
            var hub = new MapHub { Name = "Hub", CurrentIsland = () => here, PlayerLocal = () => (10f, 20f, 0f), Say = text => _said.Add(text), OpenWorldMap = () => opened++ }; hub.Settings.Path = "user://test_mini.cfg"; AddChild(hub); hub.Api.Transport = Server;
            var camera = new Camera3D(); AddChild(camera); camera.Current = true;
            map = new MiniMap { Name = "Mini", Size = new Vector2(200, 200) }; AddChild(map); var frame = new Control { Size = new Vector2(200, 200) }; AddChild(frame); map.BuildOverlay(frame); map.Hub = hub; map.Size = new Vector2(200, 200); await Frames(3);
            Require(hub.Atlas.Find(A) is not null && hub.Friends.Count == 1 && hub.IsFriendNamed("bia"), "the atlas and the friends were read through the hub");

            // zoom
            Require(map.ViewMeters == 300, "starts at 300 m"); map.WheelForTesting(true); Require(Math.Abs(map.ViewMeters - 255) < .01, "the wheel zooms in: " + map.ViewMeters); map.ZoomOut(); Require(map.ViewMeters > 255, "zoom out");
            for (var i = 0; i < 30; i++) map.ZoomIn(); Require(map.ViewMeters == MiniMap.MinMeters, "the zoom stops at the closest"); for (var i = 0; i < 60; i++) map.ZoomOut(); Require(map.ViewMeters == MiniMap.MaxMeters, "and at the widest");
            map.SetMeters(300); map.ProcessForTesting(2); var saved = new MapSettings { Path = "user://test_mini.cfg" }; saved.Load(); Require(saved.MiniMeters == 300 && !saved.MiniRotate, "the zoom is remembered");

            // north fixed: the point north of the avatar is above the centre; the round trip is exact
            var north = map.Project(0, -100); Require(Math.Abs(north.X - 100) < .01 && north.Y < 100, "north is up: " + north); var back = map.Unproject(map.Project(30, -40)); Require(back.DistanceTo(new Vector2(30, -40)) < .01, "unproject undoes project");
            // turning with the camera: facing west, north is on the right
            camera.RotationDegrees = new Vector3(0, 90, 0); await Frames(2); map.Rotates = true; Require(map.RotateButtonOnForTesting && Math.Abs(map.Heading + Mathf.Pi / 2) < .05f, "the camera faces west: " + map.Heading);
            var turned = map.Project(0, -100); Require(turned.X > 150 && Math.Abs(turned.Y - 100) < 1, "north is on the right when facing west: " + turned); var ahead = map.Project(-100, 0); Require(Math.Abs(ahead.X - 100) < 1 && ahead.Y < 50, "what is west is ahead (up): " + ahead);
            map.Rotates = false; Require(!map.RotateButtonOnForTesting && map.Project(0, -100).X is > 99 and < 101, "back to north fixed");
            map.ProcessForTesting(2); saved.Load(); Require(!saved.MiniRotate, "orientation is remembered");

            // destination: none, a pin inside, an arrow at the edge, another island, arrival
            Require(map.DestinationState == "none", "no destination at first");
            Require(map.SetDestinationAt(new Vector2(30, -40)) && hub.Destination is { LocalX: 40f, LocalZ: -20f } && map.DestinationState == "pin" && Math.Abs(map.Destination()!.Value.Meters - 50) < .01, "a spot of the island becomes the destination: " + hub.Destination);
            map.SetMeters(40); Require(map.DestinationState == "arrow", "at a close zoom it is an arrow at the edge"); map.SetMeters(300);
            hub.SetDestination(new MapDestination(B, "Praia Azul", 768, 0, 0f, 0f)); var far = map.Destination()!.Value; Require(map.DestinationState == "arrow" && Math.Abs(far.Meters - new Vector2(758, -20).Length()) < .5 && MiniMap.Distance(far.Meters) == "758 m" && MiniMap.Distance(1500) == $"{1.5f:0.0} km", "an island far away shows an arrow and the distance: " + far.Meters);
            hub.SetDestination(new MapDestination(A, "Centro", 13, 22, 13f, 22f)); _said.Clear(); map.ProcessForTesting(.1); Require(hub.Destination is null && _said.Any(s => s.Contains("chegou")), "arriving clears the destination and says so");
            here = B; Require(map.SetDestinationAt(new Vector2(1, 1)) is false || hub.Destination is not null, "another island is handled"); hub.ClearDestination(); here = A;

            // menu
            var menu = map.MenuForTesting(new Vector2(100, 100)); Require(menu.SequenceEqual(["Marcar destino aqui", "Abrir Mapa do Mundo", "Orientação: girar com a câmera", "Esconder nomes", "Esconder avatares", "Esconder construções", "Copiar coordenadas"]), "the menu: " + string.Join("|", menu));
            map.ChooseMenuForTesting("Marcar destino aqui"); Require(hub.Destination is not null && map.MenuForTesting(new Vector2(100, 100)).Contains("Cancelar destino"), "the menu marks the destination and then offers to cancel it");
            map.ChooseMenuForTesting("Cancelar destino"); Require(hub.Destination is null, "and cancels it"); map.ChooseMenuForTesting("Abrir Mapa do Mundo"); Require(opened == 1, "the menu opens the world map");
            map.ChooseMenuForTesting("Orientação: girar com a câmera"); Require(map.Rotates && map.RotateButtonOnForTesting, "the menu turns the orientation on and the button follows"); map.MenuForTesting(new Vector2(100, 100)); map.ChooseMenuForTesting("Orientação: norte fixo");
            map.ChooseMenuForTesting("Esconder avatares"); Require(!map.ShowPlayers && !map.PlayersButtonOnForTesting, "the menu hides the avatars and the button follows"); map.ShowPlayers = true;

            // the settings window and the maps share the preferences by id
            var changes = new List<(string, object)>(); hub.SettingChanged = (id, value) => changes.Add((id, value)); hub.ApplySetting("world.minimap_rotate", true); Require(map.Rotates && map.RotateButtonOnForTesting && changes.Count == 0, "a value from the settings window turns the map and the button, without echoing back");
            hub.ApplySetting("world.minimap_zoom", 500); Require(map.ViewMeters == 500, "the range comes from the settings"); hub.ApplySetting("world.minimap_zoom", 99999); Require(map.ViewMeters == 1000, "and is limited"); hub.ApplySetting("world.minimap_rotate", false); hub.ApplySetting("nonsense", 1);
            map.ShowNames = false; map.ShowBuildings = false; Require(changes.Contains(("world.minimap_names", false)) && changes.Contains(("world.minimap_buildings", false)), "a change made in the map goes to the settings window"); map.ShowNames = true; map.ShowBuildings = true;
            map.SetMeters(200); map.ProcessForTesting(2); Require(changes.Contains(("world.minimap_zoom", 200)), "the zoom reaches the settings after it settles");
            Require(MapHub.SettingIds.All(id => SettingsCatalogHas(id)), "every map preference has a place in the settings catalog");
            // shared with the world map: the same hub carries the destination
            var window = new WorldMapWindow { Name = "World", Hub = hub }; AddChild(window); await Frames(2); hub.SetDestination(new MapDestination(A, "Centro", 40, -20, 40f, -20f)); Require(map.DestinationState != "none" && window.Hub.Destination is not null, "both maps read the same destination");
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://test_mini.cfg"));
            GD.Print("event=mini_map_smoke result=passed"); GetTree().Quit();
        }
        catch (Exception exception)
        {
            var where = exception.StackTrace?.Split((char)10).Where(l => l.Contains("MiniMapSmoke")).Take(2).Aggregate("", (s, l) => s + l.Trim() + " ; ");
            GD.PushError($"event=mini_map_smoke result=failed error={exception.Message} at {where}"); GetTree().Quit(1);
        }
    }
}
