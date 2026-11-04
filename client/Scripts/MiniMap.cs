using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.Client.Scripts;

/// Top-down map centred on the avatar, north up. Shows land, beach, water depth, nearby avatars and the camera cone.
public partial class MiniMap : Control
{
    private static readonly Color Water = new("#2c6fae");
    private IslandTerrain? _terrain;
    private PlayerController? _player;
    private NetworkManager? _network;
    private ImageTexture? _texture;
    private float _viewMeters = 300f;
    private bool _showPlayers = true;
    private static readonly Color[] DotColors = [new("#16D6C4"), new("#F4A340"), new("#E94B9A"), new("#7C5CFF"), new("#3BF45F")];

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(188, 188); MouseFilter = MouseFilterEnum.Pass;
        ClipContents = true;
        TextureFilter = TextureFilterEnum.Linear;
        TooltipText = "Mini-mapa · roda do mouse para zoom";
    }

    public void Bind(IslandTerrain terrain, PlayerController? player, NetworkManager network)
    {
        _terrain = terrain; _player = player; _network = network;
        terrain.Changed += Rebuild;
        Rebuild();
    }

    public override void _ExitTree() { if (_terrain is not null) _terrain.Changed -= Rebuild; }

    public override void _Process(double delta) { if (IsVisibleInTree()) QueueRedraw(); }

    public override void _GuiInput(InputEvent input)
    {
        if (input is not InputEventMouseButton { Pressed: true } button) return;
        if (button.ButtonIndex == MouseButton.WheelUp) _viewMeters = Mathf.Clamp(_viewMeters * .85f, 80f, 700f);
        else if (button.ButtonIndex == MouseButton.WheelDown) _viewMeters = Mathf.Clamp(_viewMeters / .85f, 80f, 700f);
    }

    private void Rebuild()
    {
        if (_terrain is null) return;
        var map = _terrain.Map;
        var count = map.VertexCount;
        var image = Image.CreateEmpty(count, count, false, Image.Format.Rgb8);
        Color[] layers = [.. IslandTerrain.Layers.Select(l => l.Map)];
        for (var z = 0; z < count; z++)
        for (var x = 0; x < count; x++)
        {
            var height = map.HeightAt(x, z);
            var depth = map.SeaLevel - height;
            Color color;
            if (depth > 0) color = new Color("#58a8d4").Lerp(new Color("#133f73"), Mathf.Clamp(depth / 6f, 0f, 1f));
            else
            {
                color = new Color(0, 0, 0);
                for (var layer = 0; layer < TerrainMap.LayerCount; layer++) color += layers[layer] * (map.SplatAt(x, z, layer) / 255f);
                color = color.Lightened(Mathf.Clamp(height * .03f, 0f, .35f));
            }
            image.SetPixel(x, z, color);
        }
        _texture = ImageTexture.CreateFromImage(image);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Water);
        var center = Size / 2f;
        var scale = Size.X / _viewMeters;
        var origin = _player is null ? Vector3.Zero : _player.GlobalPosition;
        if (_texture is not null && _terrain is not null)
        {
            var half = _terrain.Map.HalfExtent;
            DrawTextureRect(_texture, new Rect2(center + new Vector2(-half - origin.X, -half - origin.Z) * scale, new Vector2(half * 2, half * 2) * scale), false);
        }
        if (_network is not null && _showPlayers)
            foreach (var (other, index) in _network.RemotePositions.Select((position, i) => (position, i)))
            {
                var point = center + new Vector2(other.X - origin.X, other.Z - origin.Z) * scale;
                DrawCircle(point, 7f, DotColors[index % DotColors.Length]); DrawArc(point, 7f, 0, Mathf.Tau, 16, new Color(1, 1, 1, .9f), 1.6f);
            }
        // Your own marker: a white arrow pointing where the camera looks.
        var camera = GetViewport().GetCamera3D();
        var forward = camera is null ? Vector3.Forward : -camera.GlobalTransform.Basis.Z;
        var direction = new Vector2(forward.X, forward.Z).Normalized();
        if (direction.LengthSquared() < .01f) direction = Vector2.Up;
        var side = new Vector2(-direction.Y, direction.X);
        Vector2[] arrow = [center + direction * 17f, center - direction * 11f + side * 11f, center - direction * 4f, center - direction * 11f - side * 11f];
        DrawColoredPolygon(arrow.Select(p => p + new Vector2(1.5f, 2f)).ToArray(), new Color(0, 0, 0, .35f));
        DrawColoredPolygon(arrow, Colors.White);
    }

    public void ZoomIn() => _viewMeters = Mathf.Clamp(_viewMeters * .8f, 80f, 700f);
    public void ZoomOut() => _viewMeters = Mathf.Clamp(_viewMeters / .8f, 80f, 700f);
    public bool ShowPlayers { get => _showPlayers; set => _showPlayers = value; }
    public float ViewMeters => _viewMeters;

    /// The round compass letters, the zoom buttons and the layers button drawn over the map (client/Assets/ui/06).
    public void BuildOverlay(Control frame)
    {
        void Letter(string text, Vector2 anchor, Vector2 offset)
        {
            var badge = new CompassBadge(text) { CustomMinimumSize = new Vector2(34, 34), MouseFilter = MouseFilterEnum.Ignore };
            frame.AddChild(badge);
            badge.SetAnchorsPreset(LayoutPreset.TopLeft);
            badge.AnchorLeft = badge.AnchorRight = anchor.X; badge.AnchorTop = badge.AnchorBottom = anchor.Y;
            badge.OffsetLeft = offset.X - 17; badge.OffsetRight = offset.X + 17; badge.OffsetTop = offset.Y - 17; badge.OffsetBottom = offset.Y + 17;
        }
        Letter("N", new Vector2(.5f, 0), new Vector2(0, 26)); Letter("S", new Vector2(.5f, 1), new Vector2(0, -26));
        Letter("O", new Vector2(0, .5f), new Vector2(26, 0)); Letter("L", new Vector2(1, .5f), new Vector2(-26, 0));
        MapButton(frame, "plus", "Aproximar", 1, 0, new Vector2(-30, 40), ZoomIn);
        MapButton(frame, "minus", "Afastar", 1, 0, new Vector2(-30, 94), ZoomOut);
        var layers = MapButton(frame, "layers", "Camadas: mostrar ou ocultar jogadores", 0, 1, new Vector2(34, -34), () => { });
        layers.ToggleMode = true; layers.ButtonPressed = true;
        layers.Toggled += on => _showPlayers = on;
    }

    private static Button MapButton(Control frame, string icon, string tooltip, float anchorX, float anchorY, Vector2 offset, Action action)
    {
        var button = new Button { Icon = UiStyle.Icon(icon, Colors.White, 24), TooltipText = tooltip, CustomMinimumSize = new Vector2(46, 46), FocusMode = FocusModeEnum.None, ExpandIcon = false };
        var normal = UiStyle.Box(new Color("#14202B"), UiStyle.Accent, 12, 2, 0, 0); var hover = UiStyle.Box(new Color("#1F3340"), UiStyle.Accent, 12, 2, 0, 0);
        foreach (var state in new[] { "normal", "pressed", "hover_pressed" }) button.AddThemeStyleboxOverride(state, normal);
        button.AddThemeStyleboxOverride("hover", hover); button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeConstantOverride("icon_max_width", 24);
        button.Pressed += action;
        frame.AddChild(button);
        button.AnchorLeft = button.AnchorRight = anchorX; button.AnchorTop = button.AnchorBottom = anchorY;
        button.OffsetLeft = offset.X - 23; button.OffsetRight = offset.X + 23; button.OffsetTop = offset.Y - 23; button.OffsetBottom = offset.Y + 23;
        return button;
    }

    private sealed partial class CompassBadge(string letter) : Control
    {
        public override void _Draw()
        {
            var center = Size / 2f;
            DrawCircle(center, 16.5f, new Color("#0F1B26", .85f));
            DrawArc(center, 16f, 0, Mathf.Tau, 28, UiStyle.Accent, 2f);
            var font = UiStyle.Bold; var width = font.GetStringSize(letter, HorizontalAlignment.Left, -1, 17).X;
            DrawString(font, new Vector2(center.X - width / 2f, center.Y + 6f), letter, HorizontalAlignment.Left, -1, 17, Colors.White);
        }
    }
}
