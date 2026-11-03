using Godot;

namespace LifeVerse.Client.Scripts;

/// The name over an avatar's head (client/Assets/ui/09): a dark glass pill with a status dot, the name and, while the player
/// speaks, sound waves. Green dot with green waves is clean sound, red is too loud, grey means voice is off. The pill is
/// drawn once into a small viewport and shown as a billboard; it is only redrawn when the voice state changes.
public partial class NamePlate : Sprite3D
{
    private const int ViewWidth = 520, ViewHeight = 84;
    private readonly string _name;
    private readonly VoiceIndicator? _voice;
    private SubViewport _viewport = null!;
    private Control _dot = null!;
    private TextureRect _waves = null!;
    private PanelContainer _pill = null!;
    private int _state = -1;

    public NamePlate(string name, VoiceIndicator? voice) { _name = name; _voice = voice; }

    public override void _Ready()
    {
        _viewport = new SubViewport { Size = new Vector2I(ViewWidth, ViewHeight), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Once, Disable3D = true };
        AddChild(_viewport);
        var center = new CenterContainer(); center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _viewport.AddChild(center);
        _pill = new PanelContainer();
        _pill.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(.106f, .149f, .18f, .88f), new Color(UiStyle.BorderBright, .9f), 34, 2, 22, 8));
        center.AddChild(_pill);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12);
        _pill.AddChild(row);
        _dot = new DotControl(); row.AddChild(_dot);
        var label = new Label { Text = _name };
        label.AddThemeFontOverride("font", UiStyle.Bold); label.AddThemeFontSizeOverride("font_size", 34); label.AddThemeColorOverride("font_color", Colors.White);
        row.AddChild(label);
        _waves = new TextureRect { CustomMinimumSize = new Vector2(32, 32), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Visible = false, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        row.AddChild(_waves);

        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled; NoDepthTest = true; Shaded = false; DoubleSided = true; AlphaCut = AlphaCutMode.Disabled;
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps; PixelSize = .0046f; RenderPriority = 5;
        Texture = _viewport.GetTexture();
        Refresh(force: true);
    }

    public override void _Process(double delta) => Refresh(force: false);

    private void Refresh(bool force)
    {
        // 0 voice off, 1 voice on and quiet, 2 speaking, 3 speaking too loud
        var state = _voice is null || !_voice.Active ? 0 : _voice.Speaking > .3f ? (_voice.Red ? 3 : 2) : 1;
        if (!force && state == _state) return;
        _state = state;
        var color = state switch { 3 => UiStyle.Danger, 0 => new Color("#C9CDD1"), _ => UiStyle.SuccessBright };
        ((DotControl)_dot).Color = color; _dot.QueueRedraw();
        _waves.Visible = state >= 2;
        if (state >= 2) _waves.Texture = UiStyle.Icon("waves", state == 3 ? UiStyle.DangerBright : UiStyle.SuccessBright, 32);
        _dot.Visible = _voice is not null;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    private sealed partial class DotControl : Control
    {
        public Color Color { get; set; } = Colors.White;
        public DotControl() { CustomMinimumSize = new Vector2(22, 22); SizeFlagsVertical = SizeFlags.ShrinkCenter; }
        public override void _Draw() => DrawCircle(Size / 2, 10, Color);
    }
}
