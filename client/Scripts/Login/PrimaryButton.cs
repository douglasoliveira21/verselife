using Godot;

namespace LifeVerse.Client.Scripts.LoginUi;

/// <summary>
/// The big turquoise button of the login. Hover and press change its colour and press it a little; while <see cref="Busy"/> it shows a spinner, says what is happening and ignores clicks.
/// </summary>
public partial class PrimaryButton : Button
{
    private TextureRect _spinner = null!;
    private string _idle = "";
    private string _busyText = "";
    private bool _busy;

    public PrimaryButton(string idleText, string busyText) { _idle = idleText; _busyText = busyText; }

    public bool Busy
    {
        get => _busy;
        set { _busy = value; if (_spinner is null) return; _spinner.Visible = value; Text = value ? _busyText : _idle; MouseDefaultCursorShape = value ? CursorShape.Busy : CursorShape.PointingHand; }
    }

    public void SetIdleText(string text) { _idle = text; if (!_busy) Text = text; }

    public override void _Ready()
    {
        Text = _idle; CustomMinimumSize = new Vector2(0, 62); SizeFlagsHorizontal = SizeFlags.ExpandFill; FocusMode = FocusModeEnum.All; MouseDefaultCursorShape = CursorShape.PointingHand;
        AddThemeFontOverride("font", UiStyle.Bold); AddThemeFontSizeOverride("font_size", 21);
        var dark = new Color("#06242A"); foreach (var color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" }) AddThemeColorOverride(color, dark); AddThemeColorOverride("font_disabled_color", new Color("#3F6F72"));
        AddThemeStyleboxOverride("normal", Look("#16E8D8", "#0CBFC0")); AddThemeStyleboxOverride("hover", Look("#3CF7E8", "#14D3CE")); AddThemeStyleboxOverride("pressed", Look("#0DBFB3", "#0A9FA2")); AddThemeStyleboxOverride("hover_pressed", Look("#0DBFB3", "#0A9FA2"));
        AddThemeStyleboxOverride("disabled", Look("#1D6F6F", "#185E62")); AddThemeStyleboxOverride("focus", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = Colors.White, DrawCenter = false, CornerRadiusTopLeft = 16, CornerRadiusTopRight = 16, CornerRadiusBottomLeft = 16, CornerRadiusBottomRight = 16, BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2 });
        _spinner = new TextureRect { Texture = UiStyle.Icon("reset", dark, 26), Visible = false, MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(26, 26), PivotOffset = new Vector2(13, 13) };
        _spinner.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterLeft, LayoutPresetMode.KeepSize); _spinner.Position = new Vector2(26, 18); AddChild(_spinner);
        ButtonDown += () => Scale = new Vector2(.985f, .985f); ButtonUp += () => Scale = Vector2.One; Resized += () => PivotOffset = Size / 2;
    }

    public override void _Process(double delta) { if (_busy) _spinner.Rotation += (float)delta * 6.5f; }

    public override void _Pressed() { if (_busy) return; }

    private static StyleBoxFlat Look(string top, string bottom)
    {
        var box = UiStyle.Box(new Color(top), new Color(bottom).Lightened(.25f), 16, 1, 20, 10, 16); box.ShadowColor = new Color(UiStyle.Accent, .28f); box.ShadowOffset = new Vector2(0, 4);
        box.BgColor = new Color(top).Lerp(new Color(bottom), .35f); return box;
    }
}
