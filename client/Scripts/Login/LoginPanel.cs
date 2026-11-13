using Godot;

namespace LifeVerse.Client.Scripts.LoginUi;

public enum LoginMode { Login, Register, Recovery }

/// <summary>The central glass card (700 px at the reference resolution): the tabs "Entrar" and "Criar Conta" and, under them, the form, the registration or the recovery notice. Changing mode fades the content.</summary>
public partial class LoginPanel : PanelContainer
{
    public const int Width = 700;

    public LoginForm Form { get; } = new();
    public RegisterPanel Register { get; } = new();
    public PasswordRecoveryPanel Recovery { get; } = new();
    public Button LoginTab { get; private set; } = null!;
    public Button RegisterTab { get; private set; } = null!;
    public LoginMode Mode { get; private set; } = LoginMode.Login;
    public VBoxContainer Footer { get; } = new();
    private VBoxContainer _stack = null!;
    private Tween? _fade;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(Width, 0); SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(.106f, .149f, .18f, .86f), new Color(UiStyle.BorderSoft, .9f), 18, 1, 0, 0, 30));
        var margin = new MarginContainer(); foreach (var side in new[] { "left", "right" }) margin.AddThemeConstantOverride($"margin_{side}", 52); margin.AddThemeConstantOverride("margin_top", 8); margin.AddThemeConstantOverride("margin_bottom", 40); AddChild(margin);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 28); margin.AddChild(column);
        var tabs = new HBoxContainer(); tabs.AddThemeConstantOverride("separation", 0); column.AddChild(tabs);
        LoginTab = Tab("Entrar", "user"); RegisterTab = Tab("Criar Conta", "user-add"); tabs.AddChild(LoginTab); tabs.AddChild(RegisterTab);
        LoginTab.Pressed += () => Show(LoginMode.Login); RegisterTab.Pressed += () => Show(LoginMode.Register);
        _stack = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; column.AddChild(_stack);
        foreach (Control page in new Control[] { Form, Register, Recovery }) { page.Visible = false; _stack.AddChild(page); }
        Recovery.Back.Pressed += () => Show(LoginMode.Login); Form.ForgotRequested += () => Show(LoginMode.Recovery);
        Footer.AddThemeConstantOverride("separation", 8); column.AddChild(Footer);
        Apply(LoginMode.Login);
    }

    private Button Tab(string text, string icon)
    {
        var button = new Button { Text = "", Flat = true, FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 66), MouseDefaultCursorShape = CursorShape.PointingHand };
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore }; center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); button.AddChild(center);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", 10); center.AddChild(row);
        var picture = new TextureRect { Name = "Icon", Texture = UiStyle.Icon(icon, UiStyle.TextMuted, 24), CustomMinimumSize = new Vector2(24, 24), MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter }; row.AddChild(picture);
        var label = new Label { Name = "Text", Text = text, MouseFilter = MouseFilterEnum.Ignore }; label.AddThemeFontOverride("font", UiStyle.Bold); label.AddThemeFontSizeOverride("font_size", 20); row.AddChild(label);
        return button;
    }

    private void StyleTab(Button tab, bool selected, string icon)
    {
        var line = new StyleBoxFlat { BgColor = selected ? new Color(UiStyle.Accent, .07f) : new Color(0, 0, 0, 0), BorderColor = selected ? UiStyle.Accent : new Color(UiStyle.BorderSoft, .35f), BorderWidthBottom = selected ? 3 : 1, ContentMarginTop = 8, ContentMarginBottom = 8 };
        foreach (var state in new[] { "normal", "pressed", "focus", "hover_pressed" }) tab.AddThemeStyleboxOverride(state, line);
        tab.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = new Color(1, 1, 1, .05f), BorderColor = selected ? UiStyle.Accent : UiStyle.BorderSoft, BorderWidthBottom = selected ? 3 : 2, ContentMarginTop = 8, ContentMarginBottom = 8 });
        var color = selected ? UiStyle.Accent : UiStyle.TextMuted; foreach (var name in new[] { "font_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" }) tab.AddThemeColorOverride(name, color); tab.AddThemeColorOverride("font_hover_color", selected ? UiStyle.Accent : UiStyle.Text);
        var parts = tab.FindChild("Icon", true, false) as TextureRect; if (parts is not null) parts.Texture = UiStyle.Icon(icon, color, 24); if (tab.FindChild("Text", true, false) is Label text) text.AddThemeColorOverride("font_color", color);
    }

    private void Apply(LoginMode mode)
    {
        Mode = mode; Form.Visible = mode == LoginMode.Login; Register.Visible = mode == LoginMode.Register; Recovery.Visible = mode == LoginMode.Recovery;
        StyleTab(LoginTab, mode != LoginMode.Register, "user"); StyleTab(RegisterTab, mode == LoginMode.Register, "user-add");
    }

    /// <summary>Changes what the card shows, fading the content in.</summary>
    public void Show(LoginMode mode)
    {
        if (mode == Mode) return; Apply(mode);
        _fade?.Kill(); _stack.Modulate = new Color(1, 1, 1, 0); _fade = CreateTween(); _fade.TweenProperty(_stack, "modulate:a", 1f, .22f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        (mode == LoginMode.Register ? Register.User : mode == LoginMode.Login ? Form.User : null)?.Focus();
    }

    public void SetBusy(bool busy) { Form.SetBusy(busy); Register.SetBusy(busy); LoginTab.Disabled = RegisterTab.Disabled = busy; }
}
