using Godot;

namespace LifeVerse.Client.Scripts.LoginUi;

/// <summary>A field of the login: an icon on the left, the text, and (for passwords) the eye that shows or hides it. The border turns turquoise while it has the focus.</summary>
public partial class IconField : PanelContainer
{
    public LineEdit Edit { get; } = new();
    public Button? Eye { get; private set; }
    private readonly string _icon, _placeholder;
    private readonly bool _secret;
    private bool _invalid;

    public IconField(string icon, string placeholder, bool secret = false) { _icon = icon; _placeholder = placeholder; _secret = secret; }

    public string Text { get => Edit.Text; set => Edit.Text = value; }
    public bool Revealed => !Edit.Secret;
    public event Action<string>? Submitted;
    public event Action? Changed;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 58); SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12); AddChild(row);
        var icon = UiStyle.IconRect(_icon, UiStyle.TextMuted, 22); icon.SizeFlagsVertical = SizeFlags.ShrinkCenter; row.AddChild(icon);
        Edit.PlaceholderText = _placeholder; Edit.Secret = _secret; Edit.Flat = true; Edit.SizeFlagsHorizontal = SizeFlags.ExpandFill; Edit.SizeFlagsVertical = SizeFlags.ExpandFill; Edit.SecretCharacter = "•";
        Edit.AddThemeFontOverride("font", UiStyle.Medium); Edit.AddThemeFontSizeOverride("font_size", 19); Edit.AddThemeColorOverride("font_color", UiStyle.Text); Edit.AddThemeColorOverride("font_placeholder_color", new Color("#7F909B")); Edit.AddThemeColorOverride("caret_color", UiStyle.Accent);
        var none = new StyleBoxEmpty(); foreach (var state in new[] { "normal", "focus", "read_only" }) Edit.AddThemeStyleboxOverride(state, none);
        Edit.FocusEntered += () => Look(true); Edit.FocusExited += () => Look(false); Edit.TextSubmitted += text => Submitted?.Invoke(text);
        Edit.TextChanged += _ => { if (_invalid) Invalid = false; Changed?.Invoke(); };
        row.AddChild(Edit);
        if (_secret)
        {
            Eye = new Button { Flat = true, FocusMode = FocusModeEnum.None, TooltipText = "Mostrar senha", CustomMinimumSize = new Vector2(38, 38), Icon = UiStyle.Icon("eye", UiStyle.TextMuted, 22), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            Eye.Pressed += () => { Edit.Secret = !Edit.Secret; Eye.TooltipText = Edit.Secret ? "Mostrar senha" : "Ocultar senha"; Eye.Icon = UiStyle.Icon(Edit.Secret ? "eye" : "ban", Edit.Secret ? UiStyle.TextMuted : UiStyle.Accent, 22); };
            row.AddChild(Eye);
        }
        Look(false);
    }

    public bool Invalid { get => _invalid; set { _invalid = value; Look(Edit.HasFocus()); } }
    public void Focus() => Edit.GrabFocus();

    private void Look(bool focused)
    {
        var border = _invalid ? UiStyle.Danger : focused ? UiStyle.Accent : new Color(UiStyle.BorderSoft, .75f);
        var box = UiStyle.Box(new Color(.07f, .1f, .13f, .72f), border, 14, focused || _invalid ? 2 : 1, 16, 6, focused ? 10 : 0); AddThemeStyleboxOverride("panel", box);
    }
}
