using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts.Attachments;

/// <summary>
/// Objects worn at a HUD point: they sit on the screen, not on the body. Each one is drawn in its own small 3D view with a transparent background, anchored to its corner (or the middle) of the screen; its position
/// moves it from the anchor (a metre is 200 pixels), its rotation turns it and its scale sizes it. They never take the mouse, so the world stays clickable under them.
/// </summary>
public sealed partial class HudAttachmentLayer : CanvasLayer
{
    public const int Cell = 160, Margin = 24, PixelsPerMetre = 200;
    private readonly Dictionary<Guid, Control> _shown = [];
    private AttachmentStore? _store;
    private int _token;
    public Node? Cache { get; set; }

    public int Shown => _shown.Count(p => IsInstanceValid(p.Value));
    public Control? Holder(Guid id) => _shown.TryGetValue(id, out var c) && IsInstanceValid(c) ? c : null;

    public override void _Ready()
    {
        Layer = 6; Name = string.IsNullOrEmpty(Name) ? "HudAttachments" : Name;
        _store = GetNodeOrNull<AvatarManager>("/root/AvatarManager")?.Attachments; if (_store is not null) _store.Changed += Rebuild;
        GetViewport().SizeChanged += Reposition; Rebuild();
    }

    public override void _ExitTree() { if (_store is not null) _store.Changed -= Rebuild; }

    /// <summary>Where a HUD point puts the top-left corner of the picture, for a screen of this size.</summary>
    public static Vector2 Anchor(string point, Vector2 screen, Vector2 offsetMetres)
    {
        var half = Cell / 2f; var x = point switch { "hud_top_left" or "hud_bottom_left" => Margin, "hud_top_right" or "hud_bottom_right" => screen.X - Cell - Margin, _ => screen.X / 2f - half };
        var y = point switch { "hud_top_left" or "hud_top" or "hud_top_right" => Margin, "hud_bottom_left" or "hud_bottom" or "hud_bottom_right" => screen.Y - Cell - Margin, _ => screen.Y / 2f - half };
        return new Vector2(x + offsetMetres.X * PixelsPerMetre, y - offsetMetres.Y * PixelsPerMetre);
    }

    public void Rebuild()
    {
        var token = ++_token; foreach (var control in _shown.Values) if (IsInstanceValid(control)) control.QueueFree(); _shown.Clear();
        foreach (var data in _store?.Worn.Where(a => AttachmentPoints.Find(a.Point)?.IsHud == true) ?? [])
        {
            var d = data; LoadModel(d.AssetId, (model, real) => { if (token == _token && IsInstanceValid(this) && model is not null) Show(d, model, real); else model?.QueueFree(); });
        }
    }

    private void LoadModel(Guid asset, Action<Node3D?, Vector3> done)
    {
        if (AvatarRig.AttachmentModelProvider is { } provider) { var found = provider(asset); done(found?.Node, found?.Real ?? Vector3.One); return; }
        for (Node? n = Cache ?? GetParent(); n is not null; n = n.GetParent()) if (n.GetNodeOrNull<ModelCache>("ModelCache") is { } cache) { cache.Request(asset, m => done(m?.Node, m?.Real ?? Vector3.One)); return; }
        done(null, Vector3.One);
    }

    private void Show(AttachmentData data, Node3D model, Vector3 real)
    {
        var container = new SubViewportContainer { Name = "Hud_" + data.Point, Stretch = true, MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(Cell, Cell), CustomMinimumSize = new Vector2(Cell, Cell) };
        var view = new SubViewport { TransparentBg = true, Size = new Vector2I(Cell * 2, Cell * 2), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, HandleInputLocally = false, GuiDisableInput = true };
        container.AddChild(view);
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 1.3f, Position = new Vector3(0, 0, 2), Current = true }; view.AddChild(camera);
        view.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, -30, 0), LightEnergy = 1.3f });
        view.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0, 0, 0, 0), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .6f } });
        var holder = new Node3D { Name = "Model" }; view.AddChild(holder); holder.AddChild(model);
        var longest = Mathf.Max(real.X, Mathf.Max(real.Y, real.Z)); var fit = longest > .0001f ? Mathf.Min(longest, 3f) : 1f;
        holder.Basis = BuildGizmoMath(data.Rotation).Scaled(new Vector3(data.Scale[0], data.Scale[1], data.Scale[2]) * fit);
        AddChild(container); _shown[data.Id] = container; Reposition(); AvatarRig.StartModelAnimations(model);
        container.SetMeta("offset", new Vector2(data.Position[0], data.Position[1]));
    }

    private static Basis BuildGizmoMath(float[] degrees) => Basis.FromEuler(new Vector3(Mathf.DegToRad(degrees[0]), Mathf.DegToRad(degrees[1]), Mathf.DegToRad(degrees[2])));

    private void Reposition()
    {
        var screen = GetViewport().GetVisibleRect().Size;
        foreach (var (id, control) in _shown)
        {
            if (!IsInstanceValid(control) || _store?.Items.FirstOrDefault(a => a.Id == id) is not { } data) continue;
            control.Position = Anchor(data.Point, screen, new Vector2(data.Position[0], data.Position[1]));
        }
    }
}
