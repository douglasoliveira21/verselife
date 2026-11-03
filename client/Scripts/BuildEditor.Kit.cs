using System.Globalization;
using Godot;

namespace LifeVerse.Client.Scripts;

/// Small building blocks of the build window's tabs (client/Assets/ui/construir): framed cards with a title, number fields with an axis
/// chip and little arrows, sliders with a value box, colour swatches and the red button. Only looks live here.
public partial class BuildEditor
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Color CardFill = new(.07f, .12f, .15f, .66f);

    /// A number the way people read it in Portuguese: "1,500".
    private static string Disp(double value, int decimals = 3) => value.ToString("0." + new string('0', decimals), Pt);

    private static Color AxisColor(int i) => i switch { 0 => new Color("#FF5A5A"), 1 => new Color("#5AE05A"), _ => new Color("#5A9BFF") };

    private static Label Txt(string text, int size = 19, bool bold = false, Color? color = null)
    {
        var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontOverride("font", bold ? UiStyle.Bold : UiStyle.Medium); label.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) label.AddThemeColorOverride("font_color", c);
        return label;
    }

    private static ColorRect Rule() => new() { CustomMinimumSize = new Vector2(0, 1), Color = new Color(UiStyle.Divider, .75f), MouseFilter = Control.MouseFilterEnum.Ignore };

    /// A framed card; with a title it starts with an icon, the title and a thin line. `header` is the title row, for things on its right.
    private static PanelContainer Card(string? title, string? icon, out VBoxContainer body, out HBoxContainer header, int gap = 10, bool rule = true, float marginX = 16, float marginY = 12)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", UiStyle.Box(CardFill, new Color(UiStyle.BorderSoft, .75f), 14, 1, marginX, marginY));
        body = new VBoxContainer(); body.AddThemeConstantOverride("separation", gap); card.AddChild(body);
        header = Row(10);
        if (title is not null)
        {
            if (icon is not null) header.AddChild(UiStyle.IconRect(icon, UiStyle.Text, 26));
            var heading = Txt(title, 21, true); heading.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; header.AddChild(heading);
            body.AddChild(header);
            if (rule) body.AddChild(Rule());
        }
        return card;
    }

    private static Control Filler() => new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };

    // ---- number fields ---------------------------------------------------------------------------------------

    /// The little up/down arrows next to a number field: each press moves the number by one step and commits.
    private static VBoxContainer Stepper(LineEdit edit, Func<float> step, Action committed)
    {
        var column = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; column.AddThemeConstantOverride("separation", 0);
        foreach (var (icon, direction) in new[] { ("caret-up", 1), ("caret-down", -1) })
        {
            var button = new Button { Icon = UiStyle.Icon(icon, UiStyle.Text, 16), Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(30, 22), ExpandIcon = false, TooltipText = direction > 0 ? "Aumenta" : "Diminui" };
            var none = new StyleBoxEmpty(); var hover = UiStyle.Box(new Color(1, 1, 1, .12f), new Color(0, 0, 0, 0), 5, 0, 0, 0);
            button.AddThemeStyleboxOverride("normal", none); button.AddThemeStyleboxOverride("hover", hover); button.AddThemeStyleboxOverride("pressed", hover); button.AddThemeStyleboxOverride("focus", none);
            var sign = direction;
            button.Pressed += () => { edit.Text = Disp(NumOr(edit, 0) + sign * step()); committed(); };
            column.AddChild(button);
        }
        return column;
    }

    /// X, Y or Z: a coloured chip with the axis letter, the number and its arrows.
    private HBoxContainer AxisField(LineEdit[] fields, int i, Func<float> step, Action<int> committed)
    {
        var row = Row(0); row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var color = AxisColor(i);
        var chip = new PanelContainer { CustomMinimumSize = new Vector2(38, 44) };
        var box = UiStyle.Box(new Color(color, .16f), new Color(color, .95f), 9, 1, 0, 0); box.CornerRadiusTopRight = 0; box.CornerRadiusBottomRight = 0;
        chip.AddThemeStyleboxOverride("panel", box);
        var letter = Txt("XYZ"[i].ToString(), 20, true, color); letter.HorizontalAlignment = HorizontalAlignment.Center; chip.AddChild(letter);
        row.AddChild(chip);
        var edit = fields[i] = Field("0", 14); edit.Text = Disp(0); edit.CustomMinimumSize = new Vector2(70, 44);
        var index = i;
        edit.TextSubmitted += _ => committed(index);
        row.AddChild(edit);
        row.AddChild(Stepper(edit, step, () => committed(index)));
        return row;
    }

    /// A button that is only an icon (the copy / reset / link squares beside the fields).
    private static Button SquareIcon(string icon, string tip, Action action, bool toggle = false)
    {
        var button = new Button { Icon = UiStyle.Icon(icon, UiStyle.Text, 28), TooltipText = tip, ToggleMode = toggle, CustomMinimumSize = new Vector2(48, 44), FocusMode = Control.FocusModeEnum.None, ExpandIcon = false };
        UiStyle.StyleButton(button, UiStyle.ButtonKind.Tool, 6, 6, 12); button.AddThemeConstantOverride("icon_max_width", 28);
        if (toggle) button.AddThemeStyleboxOverride("pressed", UiStyle.ButtonBox(UiStyle.ButtonKind.ToolSelected, UiStyle.ButtonState.Normal, 6, 6, 12));
        button.Pressed += action;
        return button;
    }

    // ---- sliders -----------------------------------------------------------------------------------------------

    private static HSlider NewSlider(double min, double max, double step, double value) =>
        new() { MinValue = min, MaxValue = max, Step = step, Value = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(0, 30) };

    /// Icon, name, the slider and its value: in a field-like box (the material sliders) or as plain text (the shape sliders).
    private static HBoxContainer SliderLine(string? icon, string label, HSlider slider, int decimals, bool boxed, int labelWidth = 150)
    {
        var row = Row(12);
        if (icon is not null) row.AddChild(UiStyle.IconRect(icon, UiStyle.Text, 26));
        var name = Txt(label); name.CustomMinimumSize = new Vector2(labelWidth, 0); row.AddChild(name);
        row.AddChild(slider);
        var value = Txt(Disp(slider.Value, decimals), 19); value.HorizontalAlignment = HorizontalAlignment.Center;
        slider.ValueChanged += v => value.Text = Disp(v, decimals);
        if (boxed)
        {
            var box = new PanelContainer { CustomMinimumSize = new Vector2(96, 40), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            box.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.FieldFill, .85f), new Color(UiStyle.Border, .8f), 9, 1, 6, 4));
            box.AddChild(value); row.AddChild(box);
        }
        else { value.CustomMinimumSize = new Vector2(80, 0); value.HorizontalAlignment = HorizontalAlignment.Right; row.AddChild(value); }
        return row;
    }

    // ---- colours -----------------------------------------------------------------------------------------------

    private static readonly Color[] QuickColors = [Colors.White, new("#9AA3A8"), new("#4A5258"), new("#F03E3E"), new("#F5A524"), new("#F5E02B"), new("#2DE07A"), new("#2F7BEA"), new("#A855F7")];
    private static readonly Color[] LightColors = [Colors.White, new("#FFE9C2"), new("#FFF04D"), new("#FF9A1F"), new("#FF4040"), new("#C66BFF"), new("#2C86F5"), new("#12F2E0"), new("#3DE84A")];

    /// A row of small coloured squares; pressing one chooses that colour.
    private static HBoxContainer Swatches(Color[] colors, Action<Color> picked, int size = 40)
    {
        var row = Row(6);
        foreach (var color in colors)
        {
            var swatch = new Button { CustomMinimumSize = new Vector2(size, size), FocusMode = Control.FocusModeEnum.None, TooltipText = "#" + color.ToHtml(false).ToUpperInvariant() };
            var normal = UiStyle.Box(color, new Color(1, 1, 1, .35f), 9, 1, 0, 0); var hover = UiStyle.Box(color, UiStyle.Accent, 9, 3, 0, 0);
            swatch.AddThemeStyleboxOverride("normal", normal); swatch.AddThemeStyleboxOverride("hover", hover); swatch.AddThemeStyleboxOverride("pressed", hover); swatch.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            var chosen = color; swatch.Pressed += () => picked(chosen);
            row.AddChild(swatch);
        }
        return row;
    }

    private static ColorPickerButton ColorButton(Color color, int width = 200) =>
        new() { Color = color, EditAlpha = false, CustomMinimumSize = new Vector2(width, 54), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };

    // ---- buttons and cards of a page ---------------------------------------------------------------------------

    /// The red "Remover" button.
    private static Button DangerButton(string icon, string text, string tip, Action action)
    {
        var button = IconButton(icon, text, tip, action, UiStyle.ButtonKind.Secondary, 46);
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
        {
            var fill = state == "hover" ? new Color("#6E2F30") : new Color("#5A2A2B");
            button.AddThemeStyleboxOverride(state, UiStyle.Box(fill, UiStyle.Danger, 12, 2, 12, 6));
        }
        return button;
    }

    private static Button OrangeButton(string icon, string text, string tip, Action action, int height = 46)
    {
        var button = IconButton(icon, text, tip, action, UiStyle.ButtonKind.Secondary, height);
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" })
            button.AddThemeStyleboxOverride(state, UiStyle.Box(state == "hover" ? UiStyle.OrangeFillHover : UiStyle.OrangeFill, UiStyle.Orange, 12, 2, 12, 6));
        return button;
    }

    /// A strip with a leading info icon and a line of muted text: "Nota:", "Dica:".
    private static PanelContainer NoteBar(string lead, string text)
    {
        var bar = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        bar.AddThemeStyleboxOverride("panel", UiStyle.Box(CardFill, new Color(UiStyle.BorderSoft, .75f), 14, 1, 16, 10));
        var row = Row(12); bar.AddChild(row);
        row.AddChild(UiStyle.IconRect("info", UiStyle.Text, 26));
        var label = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 18);
        var bold = Txt(lead, 18, true); row.AddChild(bold);
        label.Text = text; label.AddThemeColorOverride("font_color", new Color("#D5DCE2")); row.AddChild(label);
        return bar;
    }

    /// A fixed picture of the three axes, the guide of the Alinhar tab.
    private sealed partial class AxesGlyph : Control
    {
        public override void _Draw()
        {
            var o = new Vector2(Size.X * .5f, Size.Y * .56f); var length = Mathf.Min(Size.X, Size.Y) * .36f;
            void Arrow(Vector2 direction, Color color, string name)
            {
                var end = o + direction.Normalized() * length;
                DrawLine(o, end, color, 4, true);
                var side = direction.Normalized().Rotated(Mathf.Pi / 2) * 8;
                var back = -direction.Normalized() * 16;
                DrawColoredPolygon([end, end + back + side, end + back - side], color);
                DrawString(UiStyle.Bold, end + direction.Normalized() * 8 + new Vector2(-9, 8), name, HorizontalAlignment.Left, -1, 26, color);
            }
            Arrow(new Vector2(0, -1), AxisColor(1), "Y"); Arrow(new Vector2(1, .55f), AxisColor(0), "X"); Arrow(new Vector2(-1, .55f), AxisColor(2), "Z");
            var cube = new Color(UiStyle.Accent, .85f); var half = length * .28f;
            Vector2[] top = [o + new Vector2(0, -half), o + new Vector2(half, -half * .45f), o + new Vector2(0, half * .1f), o + new Vector2(-half, -half * .45f)];
            DrawPolyline([top[0], top[1], top[2], top[3], top[0]], cube, 2.5f, true);
        }
    }
}
