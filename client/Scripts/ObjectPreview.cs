using Godot;

namespace LifeVerse.Client.Scripts;

/// A small live picture of the selected object for the build window (the framed 3D view on the left of each tab): the object is shown
/// alone, centred and scaled to fit, over a faint grid. Drag with the left button to turn it. It has its own world, so nothing in it
/// touches the island; it only needs the object's state.
public partial class ObjectPreview : PanelContainer
{
    private readonly SubViewport _viewport;
    private readonly Node3D _stage = new() { Name = "Stage" };
    private readonly Label _empty;
    private Node3D? _holder;
    private float _yaw = Mathf.DegToRad(-32);

    public bool HasObject => _holder is not null;

    public ObjectPreview(int minHeight = 300)
    {
        CustomMinimumSize = new Vector2(0, minHeight);
        SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(.02f, .08f, .11f, .78f), new Color(UiStyle.Accent, .5f), 14, 1, 2, 2));

        var container = new SubViewportContainer { Stretch = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Stop };
        _viewport = new SubViewport { OwnWorld3D = true, TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible, Msaa3D = Viewport.Msaa.Msaa4X };
        container.AddChild(_viewport); AddChild(container);

        _viewport.AddChild(_stage);
        _stage.AddChild(new TextureCache { Name = "TextureCache" });
        // the node is not in the tree yet, so the camera is aimed with a plain transform instead of LookAt
        var camera = new Camera3D { Fov = 40, KeepAspect = Camera3D.KeepAspectEnum.Width, Current = true, Transform = new Transform3D(Basis.Identity, new Vector3(2.5f, 1.9f, 3.3f)).LookingAt(new Vector3(0, -.05f, 0), Vector3.Up) };
        _viewport.AddChild(camera);
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-48, 38, 0), LightEnergy = 1.25f });
        _viewport.AddChild(new WorldEnvironment { Environment = new Godot.Environment { AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(.78f, .85f, .95f), AmbientLightEnergy = .85f, BackgroundMode = Godot.Environment.BGMode.ClearColor } });

        var grid = new MeshInstance3D { Name = "Floor", Mesh = new PlaneMesh { Size = new Vector2(5, 5) }, MaterialOverride = GridMaterial(), Position = new Vector3(0, -.8f, 0) };
        _stage.AddChild(grid);

        _empty = new Label { Text = "Nada selecionado", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _empty.SetAnchorsPreset(LayoutPreset.FullRect); _empty.AddThemeColorOverride("font_color", UiStyle.TextMuted); _empty.AddThemeFontSizeOverride("font_size", 19);
        AddChild(_empty);

        container.GuiInput += input =>
        {
            if (input is InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } motion && _holder is not null) { _yaw -= motion.Relative.X * .012f; ApplyYaw(); }
        };
    }

    private static StandardMaterial3D GridMaterial()
    {
        var image = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        var line = new Color(UiStyle.Accent, .55f);
        for (var i = 0; i < 64; i++) for (var j = 0; j < 2; j++) { image.SetPixel(i, j, line); image.SetPixel(j, i, line); }
        return new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(image), TextureRepeat = true, Uv1Scale = new Vector3(8, 8, 1), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
    }

    private void ApplyYaw() { if (_holder is not null) _holder.Rotation = new Vector3(0, _yaw, 0); }

    /// Shows this object (or nothing). The picture is rebuilt from the state, so it always matches what the server confirmed.
    public void Show(BuildObjectState? state)
    {
        if (_holder is not null) { _stage.RemoveChild(_holder); _holder.QueueFree(); _holder = null; }
        _empty.Visible = state is null;
        if (state is null) return;

        var copy = new BuildObjectState
        {
            Id = Guid.NewGuid(), Name = state.Name, PrimitiveType = state.PrimitiveType, GeometryJson = state.GeometryJson, MaterialJson = state.MaterialJson, PhysicsJson = "{}",
            ScaleX = state.ScaleX, ScaleY = state.ScaleY, ScaleZ = state.ScaleZ
        };
        var view = new BuildObjectView();
        var holder = new Node3D { Name = "Holder" };
        holder.AddChild(view); _stage.AddChild(holder);
        view.Apply(copy);

        var mesh = view.GetNodeOrNull<MeshInstance3D>("PrimitiveMesh");
        var box = mesh?.GetAabb() ?? new Aabb(-Vector3.One / 2, Vector3.One);
        var size = box.Size * view.Scale;
        var longest = Mathf.Max(.01f, Mathf.Max(size.X, Mathf.Max(size.Y, size.Z)));
        var factor = 1.3f / longest;
        var center = (box.Position + box.Size / 2) * view.Scale;
        view.Position = -center;
        holder.Scale = Vector3.One * factor;
        _holder = holder; ApplyYaw();
        if (_stage.GetNodeOrNull<MeshInstance3D>("Floor") is { } floor) floor.Position = new Vector3(0, -size.Y * factor / 2 - .02f, 0);
    }
}
