using Godot;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>The colours, shapes and small widgets of the island management window, so every tab looks the same and no tab spreads colour codes of its own.</summary>
public static class IslandUi
{
    // ---- tokens (the table of the design) ---------------------------------------------------------------------------------
    public static readonly Color WindowBackground = new("#071923");
    public static readonly Color MainPanel = new("#0B202B");
    public static readonly Color SidebarBackground = new("#08202A");
    public static readonly Color CardBackground = new("#0C2733");
    public static readonly Color CardHeader = new("#102F3D");
    public static readonly Color InputBackground = new("#102B39");
    public static readonly Color TitleBar = new("#092532");
    public static readonly Color BorderDefault = new("#315B6B");
    public static readonly Color BorderLight = new("#6D818D");
    public static readonly Color AccentPrimary = new("#12F2E0");
    public static readonly Color AccentHover = new("#4FFFF0");
    public static readonly Color AccentSelected = new("#075C60");
    public static readonly Color AccentSecondary = new("#F4A340");
    public static readonly Color TextPrimary = new("#F5FAFC");
    public static readonly Color TextSecondary = new("#C0D1D8");
    public static readonly Color TextMuted = new("#8FA8B3");
    public static readonly Color Success = new("#20E36B");
    public static readonly Color Warning = new("#FFB347");
    public static readonly Color Error = new("#FF5D65");
    public static readonly Color Disabled = new("#627A84");

    public const int RadiusWindow = 20, RadiusCard = 14, RadiusButton = 9, RadiusInput = 7;

    public static StyleBoxFlat Box(Color fill, Color? border = null, int radius = RadiusCard, int borderWidth = 1, float marginX = 0, float marginY = 0, int shadow = 0)
    {
        var box = new StyleBoxFlat { BgColor = fill, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius, ContentMarginLeft = marginX, ContentMarginRight = marginX, ContentMarginTop = marginY, ContentMarginBottom = marginY, AntiAliasing = true };
        if (border is { } c && borderWidth > 0) { box.BorderColor = c; box.BorderWidthLeft = box.BorderWidthRight = box.BorderWidthTop = box.BorderWidthBottom = borderWidth; }
        if (shadow > 0) { box.ShadowColor = new Color(0, 0, 0, .55f); box.ShadowSize = shadow; box.ShadowOffset = new Vector2(0, 6); }
        return box;
    }

    // ---- text -------------------------------------------------------------------------------------------------------------
    public static Label Text(string text, int size = 15, Color? color = null, bool bold = false, bool wrap = false)
    {
        var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color ?? TextPrimary); label.AddThemeFontOverride("font", bold ? UiStyle.Bold : UiStyle.Medium);
        if (wrap) { label.AutowrapMode = TextServer.AutowrapMode.WordSmart; label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; }
        return label;
    }

    public static HBoxContainer Row(int gap = 10) { var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", gap); return row; }
    public static VBoxContainer Column(int gap = 8) { var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", gap); return column; }
    public static Control Spacer() => new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };

    /// <summary>
    /// Opens a dialog at the given width and then fits its height to what is really inside. A wrapping label has no width yet when the dialog first asks how tall it must be, so it answers as if it were
    /// one letter wide and the dialog opens thousands of pixels tall, with its buttons far off the screen. A few frames later the labels have their width: the dialog is put back to the size of its content
    /// (never taller than the screen) and centred.
    /// </summary>
    public static async void Fit(Window dialog, int width)
    {
        dialog.PopupCentered(new Vector2I(width, 120));
        var tree = dialog.GetTree();
        for (var i = 0; i < 3; i++) { await dialog.ToSignal(tree, SceneTree.SignalName.ProcessFrame); if (!GodotObject.IsInstanceValid(dialog)) return; }
        var view = dialog.GetParent() is Node parent && parent.GetViewport() is { } viewport ? viewport.GetVisibleRect().Size : new Vector2(1280, 720);
        dialog.Size = new Vector2I(width, 10); // the window grows back to the least its content needs
        var most = Mathf.Max(240, (int)(view.Y * 0.9f)); if (dialog.Size.Y > most) dialog.Size = new Vector2I(width, most);
        dialog.MoveToCenter();
    }

    public static MarginContainer Padded(Control child, int left, int top, int right, int bottom)
    {
        var margin = new MarginContainer(); margin.AddThemeConstantOverride("margin_left", left); margin.AddThemeConstantOverride("margin_top", top); margin.AddThemeConstantOverride("margin_right", right); margin.AddThemeConstantOverride("margin_bottom", bottom);
        margin.AddChild(child); return margin;
    }

    // ---- card -------------------------------------------------------------------------------------------------------------
    /// <summary>A panel with a header (icon and title) and a body to fill.</summary>
    public static (PanelContainer Card, VBoxContainer Body) Card(string title, string icon)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", Box(CardBackground, BorderDefault, RadiusCard));
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 0); card.AddChild(column);
        var header = new PanelContainer { CustomMinimumSize = new Vector2(0, 43) };
        var headerBox = Box(CardHeader, null, RadiusCard, 0, 14, 0); headerBox.CornerRadiusBottomLeft = 0; headerBox.CornerRadiusBottomRight = 0; header.AddThemeStyleboxOverride("panel", headerBox);
        var headerRow = Row(10); headerRow.AddChild(UiStyle.IconRect(icon, AccentPrimary, 22)); headerRow.AddChild(Text(title, 17, TextPrimary, true)); header.AddChild(headerRow);
        column.AddChild(header);
        var body = Column(8); body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        column.AddChild(Padded(body, 14, 12, 14, 12));
        return (card, body);
    }

    // ---- inputs -----------------------------------------------------------------------------------------------------------
    public static void StyleField(Control control)
    {
        var normal = Box(InputBackground, BorderDefault, RadiusInput, 1, 11, 7); var focus = Box(InputBackground, AccentPrimary, RadiusInput, 1, 11, 7); var off = Box(new Color(InputBackground, .55f), BorderDefault, RadiusInput, 1, 11, 7);
        foreach (var (name, box) in new[] { ("normal", normal), ("focus", focus), ("read_only", off) }) control.AddThemeStyleboxOverride(name, box);
        control.AddThemeColorOverride("font_color", TextPrimary); control.AddThemeColorOverride("font_placeholder_color", TextMuted); control.AddThemeColorOverride("font_uneditable_color", TextSecondary);
        control.AddThemeColorOverride("caret_color", AccentPrimary); control.AddThemeColorOverride("selection_color", new Color(AccentPrimary, .30f)); control.AddThemeFontSizeOverride("font_size", 15); control.AddThemeFontOverride("font", UiStyle.Medium);
    }

    public static LineEdit Input(string placeholder = "", int maxLength = 0)
    {
        var edit = new LineEdit { PlaceholderText = placeholder, MaxLength = maxLength, CustomMinimumSize = new Vector2(0, 40), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        StyleField(edit); return edit;
    }

    public static TextEdit TextArea(string placeholder = "", float height = 94)
    {
        var edit = new TextEdit { PlaceholderText = placeholder, CustomMinimumSize = new Vector2(0, height), WrapMode = TextEdit.LineWrappingMode.Boundary, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        StyleField(edit); return edit;
    }

    public static SpinBox Number(double min, double max, double step, string suffix = "", float width = 0)
    {
        var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, Suffix = suffix, CustomMinimumSize = new Vector2(width, 40), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = HorizontalAlignment.Center, UpdateOnTextChanged = true };
        StyleField(spin.GetLineEdit()); spin.GetLineEdit().AddThemeStyleboxOverride("normal", Box(InputBackground, BorderDefault, RadiusInput, 1, 6, 7)); return spin;
    }

    public static OptionButton Dropdown(IEnumerable<(string Text, string Id)> options)
    {
        var drop = new OptionButton { CustomMinimumSize = new Vector2(0, 40), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None, Alignment = HorizontalAlignment.Left };
        foreach (var (text, id) in options) { drop.AddItem(text); drop.SetItemMetadata(drop.ItemCount - 1, id); }
        var normal = Box(InputBackground, BorderDefault, RadiusInput, 1, 12, 7); var hover = Box(InputBackground, AccentPrimary, RadiusInput, 1, 12, 7);
        foreach (var (name, box) in new[] { ("normal", normal), ("hover", hover), ("pressed", hover), ("focus", hover), ("disabled", Box(new Color(InputBackground, .55f), BorderDefault, RadiusInput, 1, 12, 7)) }) drop.AddThemeStyleboxOverride(name, box);
        foreach (var color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) drop.AddThemeColorOverride(color, TextPrimary);
        drop.AddThemeColorOverride("font_disabled_color", TextSecondary); drop.AddThemeFontSizeOverride("font_size", 15); drop.AddThemeFontOverride("font", UiStyle.Medium);
        var popup = drop.GetPopup(); popup.AddThemeStyleboxOverride("panel", Box(CardBackground, BorderDefault, RadiusInput, 1, 6, 6)); popup.AddThemeStyleboxOverride("hover", Box(AccentSelected, null, 5, 0, 8, 4));
        popup.AddThemeColorOverride("font_color", TextPrimary); popup.AddThemeColorOverride("font_hover_color", TextPrimary); popup.AddThemeFontSizeOverride("font_size", 15);
        return drop;
    }

    public static string IdOf(OptionButton drop) => drop.Selected >= 0 ? drop.GetItemMetadata(drop.Selected).AsString() : "";
    public static void Select(OptionButton drop, string id) { for (var i = 0; i < drop.ItemCount; i++) if (drop.GetItemMetadata(i).AsString() == id) { drop.Select(i); return; } }

    // ---- buttons ----------------------------------------------------------------------------------------------------------
    public enum Kind { Primary, Secondary, Orange, Danger, Quiet, Purple }
    public static readonly Color RentPurple = new("#B47AE8");

    public static Button Action(string text, string? icon, Kind kind = Kind.Secondary, int height = 40, int iconSize = 18)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, height), FocusMode = Control.FocusModeEnum.None, ExpandIcon = false, IconAlignment = HorizontalAlignment.Left, Alignment = HorizontalAlignment.Center };
        var (fill, border, ink) = kind switch
        {
            Kind.Primary => (AccentPrimary, AccentPrimary, new Color("#032327")), Kind.Orange => (new Color(AccentSecondary, .08f), AccentSecondary, new Color("#FFC27A")), Kind.Purple => (new Color(RentPurple, .12f), RentPurple, new Color("#E0C4F7")),
            Kind.Danger => (new Color(Error, .10f), Error, new Color("#FFA0A5")), Kind.Quiet => (new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), TextSecondary), _ => (new Color(0, 0, 0, .10f), new Color("#4FA9C5"), TextPrimary),
        };
        var hoverFill = kind switch { Kind.Primary => AccentHover, Kind.Orange => new Color(AccentSecondary, .22f), Kind.Purple => new Color(RentPurple, .26f), Kind.Danger => new Color(Error, .24f), Kind.Quiet => new Color(1, 1, 1, .08f), _ => new Color(AccentPrimary, .16f) };
        var hoverBorder = kind switch { Kind.Primary => AccentHover, Kind.Orange => AccentSecondary, Kind.Purple => RentPurple, Kind.Danger => Error, Kind.Quiet => new Color(0, 0, 0, 0), _ => AccentPrimary };
        button.AddThemeStyleboxOverride("normal", Box(fill, border, RadiusButton, 1, 16, 6)); button.AddThemeStyleboxOverride("hover", Box(hoverFill, hoverBorder, RadiusButton, 1, 16, 6));
        button.AddThemeStyleboxOverride("pressed", Box(hoverFill.Darkened(.2f), hoverBorder, RadiusButton, 1, 16, 6)); button.AddThemeStyleboxOverride("focus", Box(new Color(0, 0, 0, 0), null, RadiusButton, 0, 16, 6));
        button.AddThemeStyleboxOverride("disabled", Box(new Color(Disabled, .16f), new Color(Disabled, .45f), RadiusButton, 1, 16, 6));
        foreach (var name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) button.AddThemeColorOverride(name, ink);
        button.AddThemeColorOverride("font_disabled_color", Disabled); button.AddThemeFontSizeOverride("font_size", 14); button.AddThemeFontOverride("font", UiStyle.Bold);
        if (icon is not null) { button.Icon = UiStyle.Icon(icon, ink, iconSize); button.AddThemeColorOverride("icon_normal_color", Colors.White); button.AddThemeConstantOverride("h_separation", 9); }
        return button;
    }

    /// <summary>A small square icon button (window controls, the buttons over the cover).</summary>
    public static Button IconButton(string icon, string tooltip, int size = 36, bool overImage = false)
    {
        var button = new Button { Icon = UiStyle.Icon(icon, TextPrimary, 20), TooltipText = tooltip, CustomMinimumSize = new Vector2(size, size), FocusMode = Control.FocusModeEnum.None, ExpandIcon = false, IconAlignment = HorizontalAlignment.Center };
        var normal = overImage ? Box(new Color(0, 0, 0, .55f), new Color(1, 1, 1, .25f), RadiusButton) : Box(new Color(0, 0, 0, 0), null, RadiusButton, 0);
        var hover = Box(overImage ? new Color(AccentPrimary, .30f) : new Color(1, 1, 1, .10f), overImage ? AccentPrimary : null, RadiusButton, overImage ? 1 : 0);
        button.AddThemeStyleboxOverride("normal", normal); button.AddThemeStyleboxOverride("hover", hover); button.AddThemeStyleboxOverride("pressed", hover); button.AddThemeStyleboxOverride("focus", normal); button.AddThemeStyleboxOverride("disabled", normal);
        return button;
    }

    // ---- progress and status ----------------------------------------------------------------------------------------------
    public static ProgressBar Progress(double percent, float height = 14)
    {
        var bar = new ProgressBar { MinValue = 0, MaxValue = 100, Value = percent, ShowPercentage = false, CustomMinimumSize = new Vector2(0, height), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        bar.AddThemeStyleboxOverride("background", Box(new Color("#27475A"), null, 8, 0)); SetProgressColor(bar, percent); return bar;
    }

    /// <summary>Teal up to 74%, orange from 75%, red from 90%.</summary>
    public static void SetProgressColor(ProgressBar bar, double percent, double warn = 75, double danger = 90) =>
        bar.AddThemeStyleboxOverride("fill", Box(percent >= danger ? Error : percent >= warn ? AccentSecondary : AccentPrimary, null, 8, 0));

    public static Color StateColor(string state) => state switch { "online" => Success, "starting" or "restarting" or "reiniciando" or "iniciando" => Warning, "offline" => Disabled, _ => Error };
    public static string StateText(string state) => state switch { "online" => "Online", "starting" => "Iniciando", "restarting" => "Reiniciando", "offline" => "Desligada", _ => "Indisponível" };

    public static Control Dot(Color color, int size = 12) => new ColorRect { Color = color, CustomMinimumSize = new Vector2(size, size), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };

    public static Control Pill(string text, Color color)
    {
        var pill = new PanelContainer(); pill.AddThemeStyleboxOverride("panel", Box(new Color(color, .16f), color, 7, 1, 11, 2));
        pill.AddChild(Text(text, 14, color, true)); pill.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; return pill;
    }

    public static ColorRect Divider() => new() { Color = new Color(BorderDefault, .45f), CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore };
}

/// <summary>An on/off switch: teal track with a white knob when on, grey track when off.</summary>
public partial class IslandToggle : Button
{
    public IslandToggle() { ToggleMode = true; CustomMinimumSize = new Vector2(52, 28); FocusMode = FocusModeEnum.None; Flat = true; MouseDefaultCursorShape = CursorShape.PointingHand; SizeFlagsVertical = SizeFlags.ShrinkCenter; }

    public override void _Draw()
    {
        var track = new Rect2(Vector2.Zero, new Vector2(52, 28));
        var color = Disabled ? new Color(IslandUi.Disabled, .5f) : ButtonPressed ? IslandUi.AccentPrimary : new Color("#4A6370");
        DrawStyleBox(IslandUi.Box(color, null, 14, 0), track);
        DrawCircle(new Vector2(ButtonPressed ? 38 : 14, 14), 10.5f, Disabled ? new Color(1, 1, 1, .55f) : Colors.White);
    }

    public override void _Toggled(bool pressed) => QueueRedraw();
    public override Vector2 _GetMinimumSize() => new(52, 28);
    public new void SetPressedNoSignal(bool pressed) { base.SetPressedNoSignal(pressed); QueueRedraw(); }
}
