using Godot;

namespace LifeVerse.Client.Scripts.Settings;

/// <summary>
/// The pieces the settings window is made of: panels, rows with help, tooltips, the confirmation dialog. They share the tokens of <see cref="UiStyle"/> and the
/// theme of the viewer (buttons, sliders, switches, drop-downs), so every control has the same states everywhere.
/// </summary>
public static class SettingsUi
{
    public static readonly Color Inner = new("#15252E"), HeaderFill = new("#202733"), Frame = new("#1B262E"), TextMain = new("#F4F7FA"), TextSub = new("#A9BBC5"), Line = new("#6D818D");
    public const int Radius = 14;

    public static StyleBoxFlat Panel(Color fill, int radius = Radius, float borderAlpha = .45f, float margin = 16) => UiStyle.Box(fill, new Color(Line, borderAlpha), radius, 1, margin, margin);

    public static Label Text(string text, int size = 16, Color? color = null, bool bold = false)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color ?? TextMain);
        label.AddThemeFontOverride("font", bold ? UiStyle.Bold : UiStyle.Regular);
        return label;
    }

    /// <summary>The "?" next to a name: explains what the setting does, what it costs in performance and whether it needs a restart.</summary>
    public static Control HelpIcon(string tip)
    {
        var icon = UiStyle.IconRect("info", TextSub, 18); icon.MouseFilter = Control.MouseFilterEnum.Stop; icon.TooltipText = tip; icon.MouseDefaultCursorShape = Control.CursorShape.Help;
        return icon;
    }

    /// <summary>A titled panel with a body to fill.</summary>
    public static PanelContainer Card(string title, string? help, out VBoxContainer body)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", Panel(Inner));
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 10); card.AddChild(column);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8); column.AddChild(head);
        head.AddChild(Text(title, 22, bold: true));
        if (help is not null) head.AddChild(HelpIcon(help));
        column.AddChild(new ColorRect { Color = new Color(Line, .35f), CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore });
        body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 12); column.AddChild(body);
        return card;
    }

    /// <summary>A name on the left (with its help icon) and the control on the right.</summary>
    public static HBoxContainer Row(Label name, string? help, Control control, float controlWidth = 0)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 38) }; row.AddThemeConstantOverride("separation", 10);
        var left = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; left.AddThemeConstantOverride("separation", 8);
        name.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; name.AutowrapMode = TextServer.AutowrapMode.Off; left.AddChild(name);
        if (help is not null) left.AddChild(HelpIcon(help));
        row.AddChild(left);
        if (controlWidth > 0) control.CustomMinimumSize = new Vector2(controlWidth, control.CustomMinimumSize.Y);
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(control);
        return row;
    }

    /// <summary>A slider with the number it stands for beside it.</summary>
    public static HBoxContainer Slider(double min, double max, double step, Func<double, string> show, out HSlider slider, out Label value)
    {
        var box = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 12);
        slider = new HSlider { MinValue = min, MaxValue = max, Step = step <= 0 ? .01 : step, CustomMinimumSize = new Vector2(170, 28), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, FocusMode = Control.FocusModeEnum.All };
        value = Text("", 16, TextMain); value.CustomMinimumSize = new Vector2(78, 0); value.HorizontalAlignment = HorizontalAlignment.Right;
        var shown = value; var bound = slider; bound.ValueChanged += v => shown.Text = show(v);
        box.AddChild(slider); box.AddChild(value);
        return box;
    }

    public static Button ActionButton(string text, UiStyle.ButtonKind kind, string? icon = null, int minWidth = 0)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, 46), FocusMode = Control.FocusModeEnum.All };
        if (icon is not null) { button.Icon = UiStyle.Icon(icon, Colors.White, 20); button.ExpandIcon = false; }
        UiStyle.StyleButton(button, kind, 18, 8, 12);
        return button;
    }

    /// <summary>Drop-downs of the window: the same size of text as the rest of the row.</summary>
    public static OptionButton Option(Vector2? size = null)
    {
        var option = new OptionButton { FocusMode = Control.FocusModeEnum.All, CustomMinimumSize = size ?? new Vector2(280, 42) };
        option.AddThemeFontSizeOverride("font_size", 17); option.AddThemeFontOverride("font", UiStyle.Medium);
        option.GetPopup().AddThemeFontSizeOverride("font_size", 17);
        return option;
    }

    public static Button IconButton(string icon, string tooltip, int size = 40)
    {
        var button = new Button { Icon = UiStyle.Icon(icon, Colors.White, 22), TooltipText = tooltip, CustomMinimumSize = new Vector2(size, size), FocusMode = Control.FocusModeEnum.All, ExpandIcon = false };
        UiStyle.StyleButton(button, UiStyle.ButtonKind.Tool, 6, 6, 10);
        return button;
    }
}

/// <summary>A message with buttons over the window (nothing behind it can be clicked). Can count down and press a default button when the time is up, as the resolution confirmation does.</summary>
public partial class SettingsDialog : Control
{
    private readonly VBoxContainer _body = new();
    private readonly HBoxContainer _buttons = new();
    private readonly Label _title = SettingsUi.Text("", 22, bold: true), _message = SettingsUi.Text("", 17, SettingsUi.TextSub);
    private readonly Label _count = SettingsUi.Text("", 17, UiStyle.Orange);
    private double _remaining; private Action? _onTimeout;
    public bool IsOpen => Visible;
    public string MessageForTesting => _message.Text;
    public IReadOnlyList<Button> ButtonsForTesting => [.. _buttons.GetChildren().OfType<Button>()];
    public Control ContentForTesting => _body;

    public SettingsDialog()
    {
        Name = "Dialog"; Visible = false; MouseFilter = MouseFilterEnum.Stop; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var shade = new ColorRect { Color = new Color(0, 0, 0, .55f), MouseFilter = MouseFilterEnum.Ignore }; shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(shade);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore }; center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(520, 0) };
        panel.AddThemeStyleboxOverride("panel", UiStyle.Box(SettingsUi.Frame, new Color(UiStyle.Accent, .8f), 16, 2, 24, 20));
        center.AddChild(panel);
        _body.AddThemeConstantOverride("separation", 14); panel.AddChild(_body);
        _message.AutowrapMode = TextServer.AutowrapMode.WordSmart; _message.CustomMinimumSize = new Vector2(470, 0);
        _body.AddChild(_title); _body.AddChild(_message); _body.AddChild(_count);
        _buttons.AddThemeConstantOverride("separation", 10); _buttons.Alignment = BoxContainer.AlignmentMode.End; _body.AddChild(_buttons);
    }

    /// <summary>Opens the dialog. <paramref name="extra"/> adds a control (a text field) under the message. After <paramref name="seconds"/> the timeout action runs and the dialog closes.</summary>
    public void Ask(string title, string message, IEnumerable<(string Label, UiStyle.ButtonKind Kind, Action? Action)> choices, Control? extra = null, double seconds = 0, Action? onTimeout = null)
    {
        foreach (var old in _buttons.GetChildren()) { _buttons.RemoveChild(old); old.QueueFree(); }
        foreach (var old in _body.GetChildren().OfType<Control>().Where(c => c.HasMeta("extra")).ToList()) { _body.RemoveChild(old); old.QueueFree(); }
        _title.Text = title; _message.Text = message; _message.Visible = message.Length > 0;
        if (extra is not null) { extra.SetMeta("extra", true); _body.AddChild(extra); _body.MoveChild(extra, 2); }
        foreach (var (label, kind, action) in choices)
        {
            var button = SettingsUi.ActionButton(label, kind, minWidth: 130);
            button.Pressed += () => { Close(); action?.Invoke(); };
            _buttons.AddChild(button);
        }
        _remaining = seconds; _onTimeout = onTimeout; _count.Visible = seconds > 0; UpdateCount();
        Visible = true; SetProcess(seconds > 0);
        _buttons.GetChildren().OfType<Button>().FirstOrDefault()?.GrabFocus();
    }

    public void Close() { Visible = false; _onTimeout = null; _remaining = 0; SetProcess(false); }

    public override void _Process(double delta)
    {
        if (!Visible || _remaining <= 0) return;
        _remaining -= delta; UpdateCount();
        if (_remaining > 0) return;
        var timeout = _onTimeout; Close(); timeout?.Invoke();
    }

    private void UpdateCount() => _count.Text = _remaining > 0 ? $"Voltando ao que estava em {Math.Ceiling(_remaining):0} s…" : "";

    public override void _GuiInput(InputEvent input) { if (Visible) AcceptEvent(); }
}

/// <summary>A horizontal bar for how loud the microphone is, with a line where the open microphone starts sending.</summary>
public partial class LevelMeter : Control
{
    public float Value { get => _value; set { value = Mathf.Clamp(value, 0f, 1f); if (Mathf.IsEqualApprox(_value, value)) return; _value = value; QueueRedraw(); } }
    public float Threshold { get => _threshold; set { value = Mathf.Clamp(value, 0f, 1f); if (Mathf.IsEqualApprox(_threshold, value)) return; _threshold = value; QueueRedraw(); } }
    private float _value, _threshold = .2f;

    public LevelMeter() { CustomMinimumSize = new Vector2(220, 26); MouseFilter = MouseFilterEnum.Ignore; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ShrinkCenter; }

    public override void _Draw()
    {
        var box = new Rect2(0, 3, Size.X, Size.Y - 6);
        DrawRect(box, new Color(1, 1, 1, .08f)); DrawRect(box, new Color(SettingsUi.Line, .6f), false, 1f);
        var fill = new Rect2(box.Position, new Vector2(box.Size.X * _value, box.Size.Y));
        DrawRect(fill, _value >= _threshold ? UiStyle.SuccessBright : UiStyle.Accent);
        var x = box.Position.X + box.Size.X * _threshold; DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), UiStyle.Orange, 2f);
    }
}
