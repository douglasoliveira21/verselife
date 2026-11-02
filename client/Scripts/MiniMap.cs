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

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(188, 188);
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
        Color[] layers = [new("#5a9a47"), new("#8a6a48"), new("#d8c995"), new("#8d8d90")];
        for (var z = 0; z < count; z++)
        for (var x = 0; x < count; x++)
        {
            var height = map.HeightAt(x, z);
            var depth = TerrainMap.SeaLevel - height;
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
        if (_network is not null)
            foreach (var other in _network.RemotePositions)
            {
                var point = center + new Vector2(other.X - origin.X, other.Z - origin.Z) * scale;
                DrawCircle(point, 3.5f, new Color("#66e070")); DrawArc(point, 3.5f, 0, Mathf.Tau, 12, new Color(0, 0, 0, .7f), 1f);
            }
        var camera = GetViewport().GetCamera3D();
        var forward = camera is null ? Vector3.Forward : -camera.GlobalTransform.Basis.Z;
        var direction = new Vector2(forward.X, forward.Z).Normalized();
        if (direction.LengthSquared() < .01f) direction = Vector2.Up;
        var side = new Vector2(-direction.Y, direction.X);
        DrawColoredPolygon([center + direction * 24f * .55f + side * 11f * .55f, center, center + direction * 24f * .55f - side * 11f * .55f], new Color(1, .95f, .4f, .35f));
        DrawColoredPolygon([center + direction * 9f, center - direction * 5f + side * 5.5f, center - direction * 2f, center - direction * 5f - side * 5.5f], new Color("#ffd23a"));
        var font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(center.X - 4, 14), "N", HorizontalAlignment.Left, -1, 12, new Color(1, 1, 1, .85f));
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, .55f), false, 1f);
    }
}
