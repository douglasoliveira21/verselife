using Godot;

namespace LifeVerse.Client.Scripts.Map;

/// <summary>Where the player is trying to get to (set on the world map, followed on the mini-map). Not a teleport: nobody moves until they walk or teleport themselves.</summary>
/// <summary>A friend who is online and can be seen: where they are (island and metres from its centre).</summary>
public sealed record MapFriend(Guid User, string Name, string Username, Guid Island, string IslandName, double X, double Z);

public sealed record MapDestination(Guid Island, string Name, double GlobalX, double GlobalY, float? LocalX, float? LocalZ);

/// <summary>Preferences of the two maps, kept on this computer (the settings window will read the same file). Plain values; reading never fails the map.</summary>
public sealed class MapSettings
{
    public string Path { get; set; } = "user://map.cfg";
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    /// <summary>Pixels per meter of the world map.</summary>
    public double Zoom { get; set; } = 0.35;
    public bool HasView { get; set; }
    public string Category { get; set; } = string.Empty;
    /// <summary>Adult islands are only listed after the player says they are 18 or older (the server has no age check of its own yet).</summary>
    public bool ShowAdult { get; set; }
    public bool ShowEvents { get; set; } = true;
    public bool ShowFavorites { get; set; } = true;
    public bool ShowFeatured { get; set; } = true;
    public bool ShowNames { get; set; } = true;
    public bool ShowParcels { get; set; }
    public bool ShowFriends { get; set; } = true;
    /// <summary>Mini-map: turns with the camera (false: north stays up), how many metres across it shows, names, buildings and other avatars.</summary>
    public bool MiniRotate { get; set; }
    public float MiniMeters { get; set; } = 300f;
    public bool MiniNames { get; set; } = true;
    public bool MiniBuildings { get; set; } = true;
    public bool MiniPlayers { get; set; } = true;

    public void Load()
    {
        var file = new ConfigFile(); if (file.Load(Path) != Error.Ok) return;
        CenterX = (double)file.GetValue("world", "center_x", CenterX); CenterY = (double)file.GetValue("world", "center_y", CenterY); Zoom = Math.Clamp((double)file.GetValue("world", "zoom", Zoom), 0.02, 6); HasView = (bool)file.GetValue("world", "has_view", HasView);
        Category = (string)file.GetValue("world", "category", Category); ShowAdult = (bool)file.GetValue("world", "show_adult", ShowAdult); ShowEvents = (bool)file.GetValue("world", "show_events", ShowEvents);
        ShowFavorites = (bool)file.GetValue("world", "show_favorites", ShowFavorites); ShowFeatured = (bool)file.GetValue("world", "show_featured", ShowFeatured); ShowNames = (bool)file.GetValue("world", "show_names", ShowNames); ShowParcels = (bool)file.GetValue("world", "show_parcels", ShowParcels); ShowFriends = (bool)file.GetValue("world", "show_friends", ShowFriends);
        MiniRotate = (bool)file.GetValue("mini", "rotate", MiniRotate); MiniMeters = Math.Clamp((float)(double)file.GetValue("mini", "meters", (double)MiniMeters), 40f, 1000f); MiniNames = (bool)file.GetValue("mini", "names", MiniNames); MiniBuildings = (bool)file.GetValue("mini", "buildings", MiniBuildings); MiniPlayers = (bool)file.GetValue("mini", "players", MiniPlayers);
    }

    public void Save()
    {
        var file = new ConfigFile(); file.Load(Path);
        file.SetValue("world", "center_x", CenterX); file.SetValue("world", "center_y", CenterY); file.SetValue("world", "zoom", Zoom); file.SetValue("world", "has_view", HasView); file.SetValue("world", "category", Category); file.SetValue("world", "show_adult", ShowAdult);
        file.SetValue("world", "show_events", ShowEvents); file.SetValue("world", "show_favorites", ShowFavorites); file.SetValue("world", "show_featured", ShowFeatured); file.SetValue("world", "show_names", ShowNames); file.SetValue("world", "show_parcels", ShowParcels); file.SetValue("world", "show_friends", ShowFriends);
        file.SetValue("mini", "rotate", MiniRotate); file.SetValue("mini", "meters", (double)MiniMeters); file.SetValue("mini", "names", MiniNames); file.SetValue("mini", "buildings", MiniBuildings); file.SetValue("mini", "players", MiniPlayers); file.Save(Path);
    }
}

/// <summary>
/// What the two maps share, in one place: the server talk, the atlas, the pictures, the preferences, the destination, and the few things only the game knows (which island the player is on, where they stand, how to enter
/// another island). The world map and the mini-map both read this, so they never disagree about an island or about the destination.
/// </summary>
public partial class MapHub : Node
{
    public MapApiClient Api { get; } = new() { Name = "Api" };
    public MapAtlasService Atlas { get; } = new() { Name = "Atlas" };
    public MapTileCache Tiles { get; } = new() { Name = "Tiles" };
    public MapSettings Settings { get; } = new();

    /// <summary>The game enters another island (the loading screen and the checks are the game's).</summary>
    public Action<Guid>? JoinIsland { get; set; }
    /// <summary>The island the player is on now (Guid.Empty: none).</summary>
    public Func<Guid>? CurrentIsland { get; set; }
    /// <summary>Where the player stands in that island (metres from its centre) and where they face, in degrees clockwise from north.</summary>
    public Func<(float X, float Z, float Yaw)?>? PlayerLocal { get; set; }
    /// <summary>The game goes to a spot of an island the server already allowed (the same island: moves there; another one: enters it and then moves). Metres from the island centre.</summary>
    public Action<Guid, float, float>? GoToPoint { get; set; }
    /// <summary>Tells the player something (a toast).</summary>
    public Action<string>? Say { get; set; }
    /// <summary>Opens the profile, search or other windows of the game (set by the main UI).</summary>
    public Action<string>? OpenLink { get; set; }

    public MapDestination? Destination { get; private set; }
    public event Action? DestinationChanged;

    public override void _Ready()
    {
        Atlas.Api = Api; Tiles.Api = Api; AddChild(Api); AddChild(Atlas); AddChild(Tiles); Settings.Load(); AddToGroup("lifeverse_map_hub");
    }


    /// <summary>Opens the world map window (set by the main UI).</summary>
    public Action? OpenWorldMap { get; set; }

    // ---- friends: where the player's friends are, read once for both maps -----------------------------------------------------------------------

    public IReadOnlyList<MapFriend> Friends { get; private set; } = [];
    public bool FriendsLoaded { get; private set; }
    public string FriendsError { get; private set; } = string.Empty;
    public event Action? FriendsChanged;
    private readonly List<Func<bool>> _watchers = [];
    private double _sinceFriends;

    /// <summary>A map that is on screen says so, and while any is, the friends are read again every 30 seconds.</summary>
    public void Watch(Func<bool> visible) => _watchers.Add(visible);

    public void RefreshFriends()
    {
        Api.Send(Godot.HttpClient.Method.Get, "/api/v1/map/people", null, (status, body) =>
        {
            if (status is < 200 or >= 300) { FriendsError = status == 0 ? "Sem conexão: não deu para ver onde seus amigos estão." : $"Não foi possível carregar seus amigos (HTTP {status})."; FriendsChanged?.Invoke(); return; }
            FriendsError = string.Empty; FriendsLoaded = true; var list = new List<MapFriend>();
            try
            {
                using var d = System.Text.Json.JsonDocument.Parse(body);
                foreach (var f in d.RootElement.EnumerateArray())
                {
                    string S(string k) => f.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty; double N(string k) => f.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetDouble() : 0;
                    if (Guid.TryParse(S("userId"), out var u) && Guid.TryParse(S("islandId"), out var i)) list.Add(new MapFriend(u, S("name"), S("username"), i, S("islandName"), N("x"), N("z")));
                }
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException) { list.Clear(); }
            Friends = list; FriendsChanged?.Invoke();
        });
    }

    public bool IsFriendNamed(string name) => Friends.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public override void _Process(double delta)
    {
        if (!Settings.ShowFriends || !_watchers.Any(w => w())) { _sinceFriends = 25; return; }
        _sinceFriends += delta; if (_sinceFriends > 30) { _sinceFriends = 0; RefreshFriends(); }
    }

    // ---- the settings window: the same preferences, by their ids in the settings catalog ------------------------------------------------------------

    /// <summary>The ids of the map preferences in the settings catalog.</summary>
    public static readonly string[] SettingIds = ["world.minimap_rotate", "world.minimap_zoom", "world.minimap_names", "world.minimap_buildings", "world.minimap_avatars", "world.map_parcels", "world.map_friends", "world.map_names", "world.map_events", "world.map_favorites", "world.map_featured"];
    /// <summary>A map changed a preference by itself (a menu, a button): the game writes it to the settings window's store (set by the main UI).</summary>
    public Action<string, object>? SettingChanged { get; set; }
    /// <summary>The settings window (or the start of the game) changed preferences: the maps redraw and refresh their buttons.</summary>
    public event Action? SettingsApplied;

    public void Changed(string id, object value) => SettingChanged?.Invoke(id, value);

    public void ApplySetting(string id, object value)
    {
        try
        {
            switch (id)
            {
                case "world.minimap_rotate": Settings.MiniRotate = Convert.ToBoolean(value); break;
                case "world.minimap_zoom": Settings.MiniMeters = Math.Clamp(Convert.ToSingle(value), 40f, 1000f); break;
                case "world.minimap_names": Settings.MiniNames = Convert.ToBoolean(value); break;
                case "world.minimap_buildings": Settings.MiniBuildings = Convert.ToBoolean(value); break;
                case "world.minimap_avatars": Settings.MiniPlayers = Convert.ToBoolean(value); break;
                case "world.map_parcels": Settings.ShowParcels = Convert.ToBoolean(value); break;
                case "world.map_friends": Settings.ShowFriends = Convert.ToBoolean(value); break;
                case "world.map_names": Settings.ShowNames = Convert.ToBoolean(value); break;
                case "world.map_events": Settings.ShowEvents = Convert.ToBoolean(value); break;
                case "world.map_favorites": Settings.ShowFavorites = Convert.ToBoolean(value); break;
                case "world.map_featured": Settings.ShowFeatured = Convert.ToBoolean(value); break;
                default: return;
            }
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException) { return; }
        Settings.Save(); SettingsApplied?.Invoke();
    }
    public void SetDestination(MapDestination destination) { Destination = destination; DestinationChanged?.Invoke(); }
    public void ClearDestination() { if (Destination is null) return; Destination = null; DestinationChanged?.Invoke(); }

    /// <summary>The player as a point of the atlas, when they are on an island the atlas knows.</summary>
    public (double X, double Y, AtlasIsland Island)? PlayerGlobal()
    {
        var id = CurrentIsland?.Invoke() ?? Guid.Empty; if (id == Guid.Empty || Atlas.Find(id) is not { } island) return null;
        var local = PlayerLocal?.Invoke(); var (x, y) = MapCoordinates.ToGlobal(island, local?.X ?? 0, local?.Z ?? 0); return (x, y, island);
    }
}
