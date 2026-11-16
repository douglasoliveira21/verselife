using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts.Map;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The circular mini-map: the ground around the avatar as it is now, with the four directions (N, S, L, O), the avatars nearby (friends marked, names optional), the buildings and the destination chosen on the world
/// map (a pin when it is inside, an arrow at the edge with the distance when it is not). North can stay up or the map can turn with the camera. The wheel and the buttons zoom; the right button opens a menu.
/// It shares <see cref="MapHub"/> with the world map, so the destination, the friends and the preferences are the same in both.
/// </summary>
public partial class MiniMap : Control
{
    private static readonly Color Water = new("#2c6fae");
    private static readonly Color[] DotColors = [new("#16D6C4"), new("#F4A340"), new("#E94B9A"), new("#7C5CFF"), new("#3BF45F")];
    public const float MinMeters = 40f, MaxMeters = 1000f;

    private IslandTerrain? _terrain;
    private PlayerController? _player;
    private NetworkManager? _network;
    private ImageTexture? _texture;
    private MapHub? _hub;
    private readonly MapSettings _fallback = new() { Path = string.Empty };
    private PopupMenu? _menu;
    private readonly Dictionary<int, Action> _menuActions = [];
    private Button? _rotateButton, _playersButton;
    private int _visibleBuildings;
    private double _sinceSave; private bool _saveDue;

    private MapSettings Settings => _hub?.Settings ?? _fallback;

    /// <summary>The shared maps hub (set when the game has made it): destination, friends, atlas and preferences.</summary>
    public MapHub? Hub
    {
        get => _hub;
        set
        {
            _hub = value; if (value is null) return;
            value.Watch(IsVisibleInTree); value.SettingsApplied += OnSettingsApplied; value.DestinationChanged += QueueRedraw; value.FriendsChanged += QueueRedraw; value.RefreshFriends();
            if (value.Atlas.Page.Items.Count == 0 && !value.Atlas.Loading) value.Atlas.Load(null, null, value.Settings.ShowAdult ? null : ["livre", "moderado"], null, 500);
            SyncButtons();
        }
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(188, 188); MouseFilter = MouseFilterEnum.Stop; ClipContents = true; TextureFilter = TextureFilterEnum.Linear;
        TooltipText = "Mini-mapa · roda do mouse para zoom · botão direito para o menu";
    }

    public void Bind(IslandTerrain terrain, PlayerController? player, NetworkManager network)
    {
        _terrain = terrain; _player = player; _network = network; terrain.Changed += Rebuild; Rebuild();
    }

    public override void _ExitTree() { if (_terrain is not null) _terrain.Changed -= Rebuild; if (_hub is not null) { _hub.DestinationChanged -= QueueRedraw; _hub.FriendsChanged -= QueueRedraw; _hub.SettingsApplied -= OnSettingsApplied; } }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return; QueueRedraw();
        if (_saveDue) { _sinceSave += delta; if (_sinceSave > 1) { _saveDue = false; _sinceSave = 0; _hub?.Settings.Save(); if (_zoomDirty) { _zoomDirty = false; _hub?.Changed("world.minimap_zoom", (int)Math.Clamp(Math.Round(ViewMeters / 10f) * 10, MinMeters, MaxMeters)); } } }
        if (_hub?.Destination is { } destination && Destination() is { } d && d.Meters < 6 && destination.Island == (_hub.CurrentIsland?.Invoke() ?? Guid.Empty)) { _hub.ClearDestination(); _hub.Say?.Invoke("Você chegou ao destino."); }
    }

    // ---- the view -----------------------------------------------------------------------------------------------------------------------------

    public float ViewMeters => Settings.MiniMeters;
    public bool Rotates { get => Settings.MiniRotate; set { Settings.MiniRotate = value; Persist(); SyncButtons(); QueueRedraw(); _hub?.Changed("world.minimap_rotate", value); } }
    public bool ShowPlayers { get => Settings.MiniPlayers; set { Settings.MiniPlayers = value; Persist(); SyncButtons(); QueueRedraw(); _hub?.Changed("world.minimap_avatars", value); } }
    public bool ShowBuildings { get => Settings.MiniBuildings; set { Settings.MiniBuildings = value; Persist(); QueueRedraw(); _hub?.Changed("world.minimap_buildings", value); } }
    public bool ShowNames { get => Settings.MiniNames; set { Settings.MiniNames = value; Persist(); QueueRedraw(); _hub?.Changed("world.minimap_names", value); } }

    private void Persist() { _saveDue = true; _sinceSave = 0; }

    public void SetMeters(float meters) { Settings.MiniMeters = Mathf.Clamp(meters, MinMeters, MaxMeters); Persist(); _zoomDirty = true; QueueRedraw(); }
    private bool _zoomDirty;
    public void ZoomIn() => SetMeters(ViewMeters * .8f);
    public void ZoomOut() => SetMeters(ViewMeters / .8f);

    private float Radius => Mathf.Min(Size.X, Size.Y) / 2f;
    private float PixelsPerMeter => Radius * 2f / ViewMeters;

    /// <summary>Where the camera looks, in radians clockwise from north.</summary>
    public float Heading
    {
        get { var camera = GetViewport()?.GetCamera3D(); if (camera is null) return 0; var forward = -camera.GlobalTransform.Basis.Z; return Mathf.Atan2(forward.X, -forward.Z); }
    }

    private float Turn => Rotates ? -Heading : 0f;

    /// <summary>A spot given in metres from the avatar (east, south) as a point of this control.</summary>
    public Vector2 Project(float east, float south) => Size / 2f + new Vector2(east, south).Rotated(Turn) * PixelsPerMeter;

    /// <summary>The metres from the avatar (east, south) of a point of this control.</summary>
    public Vector2 Unproject(Vector2 point) => (point - Size / 2f).Rotated(-Turn) / PixelsPerMeter;

    private Vector3 Origin => _player is not null ? _player.GlobalPosition : _hub?.PlayerLocal?.Invoke() is { } p ? new Vector3(p.X, 0, p.Z) : Vector3.Zero;

    // ---- the destination --------------------------------------------------------------------------------------------------------------------

    /// <summary>The destination as metres from the avatar (east, south), and how far it is, when there is one and the avatar can be placed on the atlas (or is on the same island).</summary>
    public (Vector2 Delta, float Meters)? Destination()
    {
        if (_hub?.Destination is not { } d) return null;
        var current = _hub.CurrentIsland?.Invoke() ?? Guid.Empty; Vector2? delta = null;
        if (d.Island == current && d.LocalX is { } lx && d.LocalZ is { } lz) delta = new Vector2(lx - Origin.X, lz - Origin.Z);
        else if (_hub.PlayerGlobal() is { } me) delta = new Vector2((float)(d.GlobalX - me.X), (float)(d.GlobalY - me.Y));
        return delta is { } v ? (v, v.Length()) : null;
    }

    /// <summary>What the destination looks like now: "none", "pin" (inside the circle) or "arrow" (at the edge, pointing to it).</summary>
    public string DestinationState { get { if (Destination() is not { } d) return "none"; return d.Meters * PixelsPerMeter <= Radius - 14 ? "pin" : "arrow"; } }

    public static string Distance(float meters) => meters < 1000 ? $"{meters:0} m" : $"{meters / 1000f:0.0} km";

    // ---- drawing -------------------------------------------------------------------------------------------------------------------------------

    private void Rebuild()
    {
        if (_terrain is null) return;
        var map = _terrain.Map; var count = map.VertexCount; var image = Image.CreateEmpty(count, count, false, Image.Format.Rgb8); Color[] layers = [.. IslandTerrain.Layers.Select(l => l.Map)];
        for (var z = 0; z < count; z++)
        for (var x = 0; x < count; x++)
        {
            var height = map.HeightAt(x, z); var depth = map.SeaLevel - height; Color color;
            if (depth > 0) color = new Color("#58a8d4").Lerp(new Color("#133f73"), Mathf.Clamp(depth / 6f, 0f, 1f));
            else { color = new Color(0, 0, 0); for (var layer = 0; layer < TerrainMap.LayerCount; layer++) color += layers[layer] * (map.SplatAt(x, z, layer) / 255f); color = color.Lightened(Mathf.Clamp(height * .03f, 0f, .35f)); }
            image.SetPixel(x, z, color);
        }
        _texture = ImageTexture.CreateFromImage(image);
    }

    public override void _Draw()
    {
        var center = Size / 2f; var radius = Radius; var scale = PixelsPerMeter; var origin = Origin;
        DrawRect(new Rect2(Vector2.Zero, Size), Water);
        DrawSetTransform(center, Turn, Vector2.One); // the ground and the buildings turn together
        if (_texture is not null && _terrain is not null) { var half = _terrain.Map.HalfExtent; DrawTextureRect(_texture, new Rect2(new Vector2(-half - origin.X, -half - origin.Z) * scale, new Vector2(half * 2, half * 2) * scale), false); }
        DrawBuildings(origin, scale, radius);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        DrawAvatars(origin, radius); DrawDestination(radius); DrawYou(center); DrawCompass(center, radius);
        DrawArc(center, radius - 1.5f, 0, Mathf.Tau, 72, new Color(UiStyle.Accent, .9f), 3f, true);
        var scaleText = $"{ViewMeters:0} m"; DrawString(UiStyle.Medium, center + new Vector2(-40, radius * .52f), scaleText, HorizontalAlignment.Center, 80, 12, new Color(1, 1, 1, .9f));
    }

    private void DrawBuildings(Vector3 origin, float scale, float radius)
    {
        _visibleBuildings = 0; if (!Settings.MiniBuildings || !IsInsideTree()) return;
        foreach (var node in GetTree().GetNodesInGroup("lifeverse_build_objects"))
        {
            if (node is not BuildObjectView view || !IsInstanceValid(view) || !view.Visible) continue;
            var point = new Vector2(view.GlobalPosition.X - origin.X, view.GlobalPosition.Z - origin.Z) * scale; if (point.Length() > radius + 30) continue;
            var half = new Vector2(Mathf.Abs(view.Scale.X), Mathf.Abs(view.Scale.Z)) * .5f * scale; if (half.X < 1f) half.X = 1f; if (half.Y < 1f) half.Y = 1f;
            var x = new Vector2(view.GlobalTransform.Basis.X.X, view.GlobalTransform.Basis.X.Z).Normalized() * half.X; var z = new Vector2(view.GlobalTransform.Basis.Z.X, view.GlobalTransform.Basis.Z.Z).Normalized() * half.Y;
            if (x.LengthSquared() < .01f) x = Vector2.Right * half.X; if (z.LengthSquared() < .01f) z = Vector2.Down * half.Y;
            var footprint = new[] { point - x - z, point + x - z, point + x + z, point - x + z };
            var color = view.State.PrimitiveType.Equals("mesh", StringComparison.OrdinalIgnoreCase) ? new Color(.18f, .9f, 1f, .56f) : new Color(.92f, .78f, .34f, .48f);
            DrawColoredPolygon(footprint, color); DrawPolyline([.. footprint, footprint[0]], new Color(1, 1, 1, .72f), 1.2f, true); _visibleBuildings++;
        }
    }

    private void DrawAvatars(Vector3 origin, float radius)
    {
        if (!Settings.MiniPlayers) return; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var index = 0;
        if (_network is not null)
            foreach (var (_, name, position) in _network.RemoteAvatars)
            {
                seen.Add(name); var friend = _hub?.IsFriendNamed(name) ?? false; Dot(new Vector2(position.X - origin.X, position.Z - origin.Z), name, friend, friend ? UiStyle.Accent : DotColors[index++ % DotColors.Length]);
            }
        if (_hub is not null && Settings.ShowFriends) // friends the island server reported but this client does not draw (yet)
        {
            var current = _hub.CurrentIsland?.Invoke() ?? Guid.Empty;
            foreach (var f in _hub.Friends) if (f.Island == current && seen.Add(f.Name)) Dot(new Vector2((float)f.X - origin.X, (float)f.Z - origin.Z), f.Name, true, UiStyle.Accent);
        }
        void Dot(Vector2 meters, string name, bool friend, Color color)
        {
            var at = Project(meters.X, meters.Y); if (at.DistanceTo(Size / 2f) > radius - 6) return;
            DrawCircle(at, friend ? 8f : 7f, color); DrawArc(at, friend ? 8f : 7f, 0, Mathf.Tau, 16, friend ? Colors.White : new Color(1, 1, 1, .9f), friend ? 2.2f : 1.6f);
            if (Settings.MiniNames && name.Length > 0) { DrawString(UiStyle.Bold, at + new Vector2(-39, -11), name, HorizontalAlignment.Center, 80, 11, new Color(0, 0, 0, .75f)); DrawString(UiStyle.Bold, at + new Vector2(-40, -12), name, HorizontalAlignment.Center, 80, 11, friend ? UiStyle.AccentText : Colors.White); }
        }
    }

    private void DrawDestination(float radius)
    {
        if (Destination() is not { } d) return; var center = Size / 2f; var at = Project(d.Delta.X, d.Delta.Y); var orange = UiStyle.Orange; var name = _hub?.Destination?.Name ?? string.Empty;
        if (d.Meters * PixelsPerMeter <= radius - 14)
        {
            DrawCircle(at, 5, orange); DrawArc(at, 10, 0, Mathf.Tau, 20, orange, 2f, true); var pin = UiStyle.Icon("pin", orange, 28); DrawTexture(pin, at + new Vector2(-14, -38));
            DrawString(UiStyle.Bold, at + new Vector2(-45, 22), Distance(d.Meters), HorizontalAlignment.Center, 90, 12, Colors.White);
            return;
        }
        var direction = (at - center).Normalized(); if (direction.LengthSquared() < .01f) direction = Vector2.Up; var side = new Vector2(-direction.Y, direction.X); var tip = center + direction * (radius - 6); var back = tip - direction * 20;
        DrawColoredPolygon([tip, back + side * 11, back - side * 11], orange); DrawPolyline([tip, back + side * 11, back - side * 11, tip], Colors.White, 1.6f, true);
        var label = Distance(d.Meters) + (name.Length > 0 && radius > 110 ? " · " + name : string.Empty); var labelAt = center + direction * (radius - 52);
        DrawString(UiStyle.Bold, labelAt + new Vector2(-61, 1), label, HorizontalAlignment.Center, 120, 12, new Color(0, 0, 0, .75f)); DrawString(UiStyle.Bold, labelAt + new Vector2(-60, 0), label, HorizontalAlignment.Center, 120, 12, Colors.White);
    }

    private void DrawYou(Vector2 center)
    {
        var direction = Rotates ? Vector2.Up : new Vector2(Mathf.Sin(Heading), -Mathf.Cos(Heading)); var side = new Vector2(-direction.Y, direction.X);
        Vector2[] arrow = [center + direction * 17f, center - direction * 11f + side * 11f, center - direction * 4f, center - direction * 11f - side * 11f];
        DrawColoredPolygon(arrow.Select(p => p + new Vector2(1.5f, 2f)).ToArray(), new Color(0, 0, 0, .35f)); DrawColoredPolygon(arrow, Colors.White);
    }

    private void DrawCompass(Vector2 center, float radius)
    {
        foreach (var (letter, east, south) in new[] { ("N", 0f, -1f), ("S", 0f, 1f), ("L", 1f, 0f), ("O", -1f, 0f) })
        {
            var at = center + new Vector2(east, south).Rotated(Turn) * (radius - 22f);
            DrawCircle(at, 14, new Color("#0F1B26", .88f)); DrawArc(at, 14, 0, Mathf.Tau, 24, letter == "N" ? UiStyle.Orange : UiStyle.Accent, 2f, true); DrawString(UiStyle.Bold, at + new Vector2(-10, 6), letter, HorizontalAlignment.Center, 20, 16, Colors.White);
        }
    }

    // ---- input -------------------------------------------------------------------------------------------------------------------------------------

    public override void _GuiInput(InputEvent input)
    {
        if (input is not InputEventMouseButton { Pressed: true } button) return;
        if (button.ButtonIndex == MouseButton.WheelUp) { SetMeters(ViewMeters * .85f); AcceptEvent(); }
        else if (button.ButtonIndex == MouseButton.WheelDown) { SetMeters(ViewMeters / .85f); AcceptEvent(); }
        else if (button.ButtonIndex == MouseButton.Right) { ShowMenu(button.Position, GetScreenTransform() * button.Position); AcceptEvent(); }
    }

    private void Say(string text) => _hub?.Say?.Invoke(text);

    /// <summary>Marks a spot of the island the avatar is on (metres east and south of the avatar) as the destination, the same one the world map shows.</summary>
    public bool SetDestinationAt(Vector2 meters)
    {
        var current = _hub?.CurrentIsland?.Invoke() ?? Guid.Empty;
        if (_hub is null || current == Guid.Empty || _hub.Atlas.Find(current) is not { } island) { Say("O mapa do mundo ainda não conhece esta ilha. Abra o Mapa do Mundo uma vez e tente de novo."); return false; }
        var (lx, lz, inside) = MapCoordinates.ToLocal(island, Origin.X + meters.X, Origin.Z + meters.Y); if (!inside) { Say("Esse ponto fica fora da ilha."); return false; }
        var (gx, gy) = MapCoordinates.ToGlobal(island, lx, lz); _hub.SetDestination(new MapDestination(island.Id, island.Name, gx, gy, (float)lx, (float)lz)); Say($"Destino marcado: {MapCoordinates.Format(island.Name, lx, lz)}."); return true;
    }

    private void ShowMenu(Vector2 at, Vector2 screen)
    {
        _menu?.QueueFree(); _menu = new PopupMenu(); _menuActions.Clear(); var next = 0; var meters = Unproject(at);
        void Add(string text, Action action, bool disabled = false) { _menu.AddItem(text, next); _menu.SetItemDisabled(_menu.GetItemIndex(next), disabled); _menuActions[next++] = action; }
        Add("Marcar destino aqui", () => SetDestinationAt(meters), _hub is null);
        if (_hub?.Destination is not null) Add("Cancelar destino", () => { _hub.ClearDestination(); Say("Destino cancelado."); });
        Add("Abrir Mapa do Mundo", () => _hub?.OpenWorldMap?.Invoke(), _hub?.OpenWorldMap is null);
        Add(Rotates ? "Orientação: norte fixo" : "Orientação: girar com a câmera", () => Rotates = !Rotates);
        Add(ShowNames ? "Esconder nomes" : "Mostrar nomes", () => ShowNames = !ShowNames);
        Add(ShowPlayers ? "Esconder avatares" : "Mostrar avatares", () => ShowPlayers = !ShowPlayers);
        Add(ShowBuildings ? "Esconder construções" : "Mostrar construções", () => ShowBuildings = !ShowBuildings);
        Add("Copiar coordenadas", () => { var x = Origin.X + meters.X; var z = Origin.Z + meters.Y; DisplayServer.ClipboardSet(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{x:0}, {z:0}")); Say($"Coordenadas copiadas: {x:0}, {z:0}."); });
        _menu.IdPressed += id => { if (_menuActions.TryGetValue((int)id, out var action)) action(); }; AddChild(_menu); _menu.Position = (Vector2I)screen; _menu.Popup();
    }

    // ---- the overlay: the four corner buttons ----------------------------------------------------------------------------------------------

    /// <summary>The buttons in the corners of the square around the circle: turn with the camera (top left), zoom in (top right), zoom out (bottom right) and other avatars (bottom left).</summary>
    public void BuildOverlay(Control frame)
    {
        _rotateButton = MapButton(frame, "rotate", "Girar com a câmera (desligado: o norte fica em cima)", 0, 0, new Vector2(24, 24), () => { }); _rotateButton.ToggleMode = true; _rotateButton.Toggled += on => { if (on != Rotates) Rotates = on; };
        MapButton(frame, "plus", "Aproximar", 1, 0, new Vector2(-24, 24), ZoomIn); MapButton(frame, "minus", "Afastar", 1, 1, new Vector2(-24, -24), ZoomOut);
        _playersButton = MapButton(frame, "people", "Mostrar ou esconder os outros avatares", 0, 1, new Vector2(24, -24), () => { }); _playersButton.ToggleMode = true; _playersButton.Toggled += on => { if (on != ShowPlayers) ShowPlayers = on; };
        SyncButtons();
    }

    private void OnSettingsApplied() { SyncButtons(); QueueRedraw(); }

    private void SyncButtons() { _rotateButton?.SetPressedNoSignal(Rotates); _playersButton?.SetPressedNoSignal(ShowPlayers); }

    private static Button MapButton(Control frame, string icon, string tooltip, float anchorX, float anchorY, Vector2 offset, Action action)
    {
        var button = new Button { Icon = UiStyle.Icon(icon, Colors.White, 20), TooltipText = tooltip, CustomMinimumSize = new Vector2(38, 38), FocusMode = FocusModeEnum.None, ExpandIcon = false };
        var normal = UiStyle.Box(new Color("#14202B"), UiStyle.Accent, 12, 2, 0, 0); var hover = UiStyle.Box(new Color("#1F3340"), UiStyle.Accent, 12, 2, 0, 0); var on = UiStyle.Box(new Color("#1F4A52"), UiStyle.Accent, 12, 2, 0, 0);
        button.AddThemeStyleboxOverride("normal", normal); button.AddThemeStyleboxOverride("pressed", on); button.AddThemeStyleboxOverride("hover_pressed", on); button.AddThemeStyleboxOverride("hover", hover); button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty()); button.AddThemeConstantOverride("icon_max_width", 20);
        button.Pressed += action; frame.AddChild(button);
        button.AnchorLeft = button.AnchorRight = anchorX; button.AnchorTop = button.AnchorBottom = anchorY; button.OffsetLeft = offset.X - 19; button.OffsetRight = offset.X + 19; button.OffsetTop = offset.Y - 19; button.OffsetBottom = offset.Y + 19;
        return button;
    }

    // ---- tests --------------------------------------------------------------------------------------------------------------------------------------

    public int VisibleBuildings => _visibleBuildings;
    public IReadOnlyList<string> MenuForTesting(Vector2 at) { ShowMenu(at, at); return [.. Enumerable.Range(0, _menu!.ItemCount).Select(_menu.GetItemText)]; }
    public bool MenuEnabledForTesting(string text) { for (var i = 0; i < _menu!.ItemCount; i++) if (_menu.GetItemText(i) == text) return !_menu.IsItemDisabled(i); return false; }
    public void ChooseMenuForTesting(string text) { for (var i = 0; i < _menu!.ItemCount; i++) if (_menu.GetItemText(i) == text) { _menuActions[_menu.GetItemId(i)](); return; } throw new InvalidOperationException("no menu item " + text); }
    public void WheelForTesting(bool up) => _GuiInput(new InputEventMouseButton { ButtonIndex = up ? MouseButton.WheelUp : MouseButton.WheelDown, Pressed = true, Position = Size / 2f });
    public bool RotateButtonOnForTesting => _rotateButton?.ButtonPressed ?? false;
    public bool PlayersButtonOnForTesting => _playersButton?.ButtonPressed ?? false;
    public void PressRotateForTesting() => _rotateButton?.EmitSignal(BaseButton.SignalName.Toggled, !(_rotateButton.ButtonPressed));
    public void ProcessForTesting(double delta) => _Process(delta);
}
