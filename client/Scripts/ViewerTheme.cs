using Godot;

namespace LifeVerse.Client.Scripts;

/// Dark translucent glass look with a neon teal accent (see UiStyle and the design references in client/Assets/ui).
public static class ViewerTheme
{
    public static readonly Color Text = UiStyle.Text;
    public static readonly Color TextDim = UiStyle.TextMuted;
    public static readonly Color Accent = UiStyle.AccentFill;
    public static readonly Color AccentLight = UiStyle.Accent;
    public static readonly Color Good = UiStyle.Success;
    public static readonly Color Warn = UiStyle.Warning;
    public static readonly Color Bad = UiStyle.Danger;

    public const string PrimaryButton = "PrimaryButton";
    public const string SecondaryButton = "SecondaryButton";
    public const string ToolButton = "ToolButton";
    public const string SelectedToolButton = "SelectedToolButton";
    public const string TabButton = "TabButton";

    public static StyleBoxFlat Box(Color background, Color border, int radius = 3, int borderWidth = 1, float margin = 6)
    {
        var box = new StyleBoxFlat { BgColor = background, BorderColor = border, AntiAliasing = true, AntiAliasingSize = 1f };
        box.SetBorderWidthAll(borderWidth);
        box.SetCornerRadiusAll(radius);
        box.SetContentMarginAll(margin);
        return box;
    }

    public static StyleBoxFlat WindowBox(float alpha = .95f) => UiStyle.Glass(Mathf.Min(alpha, .92f));
    public static StyleBoxFlat TitleBar() { var box = UiStyle.Box(UiStyle.TitleFill, new Color(0, 0, 0, 0), UiStyle.RadiusLarge, 0, 14, 10); box.CornerRadiusBottomLeft = box.CornerRadiusBottomRight = 0; box.BorderWidthBottom = 1; box.BorderColor = new Color(UiStyle.BorderSoft, .8f); return box; }
    public static StyleBoxFlat BarBox() => UiStyle.Box(new Color(UiStyle.PanelFill, .86f), UiStyle.BorderBright, UiStyle.RadiusLarge, 1, 8, 6, 12);

    private static void Buttons(Theme theme, string type, UiStyle.ButtonKind kind, float marginX = 12, float marginY = 7, int radius = UiStyle.Radius, Color? font = null, bool variation = true, string? baseType = "Button")
    {
        if (variation) theme.SetTypeVariation(type, baseType!);
        theme.SetStylebox("normal", type, UiStyle.ButtonBox(kind, UiStyle.ButtonState.Normal, marginX, marginY, radius));
        theme.SetStylebox("hover", type, UiStyle.ButtonBox(kind, UiStyle.ButtonState.Hover, marginX, marginY, radius));
        theme.SetStylebox("pressed", type, UiStyle.ButtonBox(kind, UiStyle.ButtonState.Pressed, marginX, marginY, radius));
        theme.SetStylebox("hover_pressed", type, UiStyle.ButtonBox(kind, UiStyle.ButtonState.Pressed, marginX, marginY, radius));
        theme.SetStylebox("disabled", type, UiStyle.ButtonBox(kind, UiStyle.ButtonState.Disabled, marginX, marginY, radius));
        theme.SetStylebox("focus", type, new StyleBoxEmpty());
        var color = font ?? UiStyle.Text;
        theme.SetColor("font_color", type, color); theme.SetColor("font_hover_color", type, Colors.White); theme.SetColor("font_pressed_color", type, Colors.White);
        theme.SetColor("font_hover_pressed_color", type, Colors.White); theme.SetColor("font_focus_color", type, color); theme.SetColor("font_disabled_color", type, UiStyle.TextDisabled);
        theme.SetColor("icon_normal_color", type, Colors.White); theme.SetColor("icon_hover_color", type, Colors.White); theme.SetColor("icon_pressed_color", type, Colors.White);
        theme.SetColor("icon_disabled_color", type, UiStyle.TextDisabled);
        theme.SetConstant("icon_max_width", type, 20); theme.SetConstant("h_separation", type, 8);
        theme.SetFont("font", type, UiStyle.Medium);
    }

    public static Theme Create()
    {
        var theme = new Theme { DefaultFontSize = UiStyle.FontBody, DefaultFont = UiStyle.Regular };
        var empty = new StyleBoxEmpty();

        theme.SetColor("font_color", "Label", UiStyle.Text);
        theme.SetStylebox("panel", "PanelContainer", UiStyle.Glass(.86f, UiStyle.RadiusLarge, 10));
        theme.SetStylebox("panel", "Panel", UiStyle.Glass(.86f, UiStyle.RadiusLarge, 0));

        // The default Button is the toolbar look; variations cover the primary (teal), secondary (orange) and selected states.
        Buttons(theme, "Button", UiStyle.ButtonKind.Tool, variation: false);
        Buttons(theme, "OptionButton", UiStyle.ButtonKind.Field, variation: false);
        Buttons(theme, "MenuButton", UiStyle.ButtonKind.Tool, variation: false);
        Buttons(theme, ToolButton, UiStyle.ButtonKind.Tool);
        Buttons(theme, SelectedToolButton, UiStyle.ButtonKind.ToolSelected);
        Buttons(theme, PrimaryButton, UiStyle.ButtonKind.Primary);
        Buttons(theme, SecondaryButton, UiStyle.ButtonKind.Secondary, font: UiStyle.Text);
        Buttons(theme, TabButton, UiStyle.ButtonKind.Tool, 14, 6, UiStyle.RadiusSmall);
        theme.SetConstant("arrow_margin", "OptionButton", 8);
        theme.SetIcon("arrow", "OptionButton", UiStyle.Icon("chevron-down", UiStyle.Text, 16));

        // check boxes, radio buttons and switches
        foreach (var type in new[] { "CheckBox", "CheckButton" })
        {
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" }) theme.SetStylebox(state, type, empty);
            theme.SetColor("font_color", type, UiStyle.Text); theme.SetColor("font_hover_color", type, Colors.White); theme.SetColor("font_pressed_color", type, UiStyle.Text);
            theme.SetColor("font_hover_pressed_color", type, Colors.White); theme.SetColor("font_disabled_color", type, UiStyle.TextDisabled); theme.SetColor("font_focus_color", type, UiStyle.Text);
            theme.SetConstant("h_separation", type, 10);
        }
        theme.SetIcon("checked", "CheckBox", UiDraw.CheckBox(true)); theme.SetIcon("unchecked", "CheckBox", UiDraw.CheckBox(false));
        theme.SetIcon("checked_disabled", "CheckBox", UiDraw.CheckBox(true, false, true)); theme.SetIcon("unchecked_disabled", "CheckBox", UiDraw.CheckBox(false, false, true));
        theme.SetIcon("radio_checked", "CheckBox", UiDraw.Radio(true)); theme.SetIcon("radio_unchecked", "CheckBox", UiDraw.Radio(false));
        theme.SetIcon("radio_checked_disabled", "CheckBox", UiDraw.Radio(true, false, true)); theme.SetIcon("radio_unchecked_disabled", "CheckBox", UiDraw.Radio(false, false, true));
        theme.SetConstant("icon_max_width", "CheckBox", 20);
        theme.SetIcon("checked", "CheckButton", UiDraw.Switch(true)); theme.SetIcon("unchecked", "CheckButton", UiDraw.Switch(false));
        theme.SetIcon("checked_disabled", "CheckButton", UiDraw.Switch(true, false, true)); theme.SetIcon("unchecked_disabled", "CheckButton", UiDraw.Switch(false, false, true));
        theme.SetConstant("icon_max_width", "CheckButton", 44);

        // text fields
        var field = UiStyle.Box(UiStyle.FieldFill, UiStyle.Border, UiStyle.Radius, 1, 12, 7);
        var focused = UiStyle.Box(UiStyle.FieldFill, UiStyle.Accent, UiStyle.Radius, 2, 12, 7);
        theme.SetStylebox("normal", "LineEdit", field); theme.SetStylebox("focus", "LineEdit", focused);
        theme.SetStylebox("read_only", "LineEdit", UiStyle.Box(new Color(UiStyle.FieldFill, .6f), new Color(UiStyle.Border, .5f), UiStyle.Radius, 1, 12, 7));
        theme.SetColor("font_color", "LineEdit", UiStyle.Text); theme.SetColor("font_placeholder_color", "LineEdit", UiStyle.TextMuted);
        theme.SetColor("caret_color", "LineEdit", UiStyle.Accent); theme.SetColor("selection_color", "LineEdit", new Color(UiStyle.Accent, .35f));
        theme.SetStylebox("normal", "TextEdit", field); theme.SetStylebox("focus", "TextEdit", focused);
        theme.SetColor("font_color", "TextEdit", UiStyle.Text); theme.SetColor("selection_color", "TextEdit", new Color(UiStyle.Accent, .35f)); theme.SetColor("caret_color", "TextEdit", UiStyle.Accent);
        theme.SetColor("default_color", "RichTextLabel", UiStyle.Text);
        theme.SetStylebox("normal", "RichTextLabel", empty);

        // Menus must remain fully opaque: a translucent popup over a bright 3D scene
        // makes labels and the submenu arrow look soft and hard to read.  Use a dark
        // solid surface, a crisp outline and slightly roomier items instead.
        var popup = UiStyle.Box(new Color("#111920"), UiStyle.BorderBright, UiStyle.Radius, 1, 8, 7, 18);
        theme.SetStylebox("panel", "PopupMenu", popup);
        theme.SetStylebox("hover", "PopupMenu", UiStyle.Box(UiStyle.AccentFillHover, UiStyle.Accent, UiStyle.RadiusSmall, 1, 8, 5));
        theme.SetColor("font_color", "PopupMenu", Colors.White); theme.SetColor("font_hover_color", "PopupMenu", Colors.White); theme.SetColor("font_disabled_color", "PopupMenu", UiStyle.TextDisabled);
        theme.SetColor("font_separator_color", "PopupMenu", UiStyle.TextMuted);
        theme.SetFont("font", "PopupMenu", UiStyle.Medium); theme.SetFontSize("font_size", "PopupMenu", 16);
        theme.SetConstant("v_separation", "PopupMenu", 9); theme.SetConstant("item_start_padding", "PopupMenu", 14); theme.SetConstant("item_end_padding", "PopupMenu", 14);
        theme.SetStylebox("separator", "PopupMenu", new StyleBoxLine { Color = UiStyle.Divider, Thickness = 1 });

        theme.SetStylebox("normal", "MenuBar", new StyleBoxEmpty { ContentMarginLeft = 12, ContentMarginRight = 12 });
        theme.SetStylebox("hover", "MenuBar", UiStyle.Box(new Color(1, 1, 1, .08f), new Color(0, 0, 0, 0), UiStyle.RadiusSmall, 0, 12, 6));
        theme.SetStylebox("pressed", "MenuBar", UiStyle.Box(new Color(UiStyle.Accent, .16f), new Color(0, 0, 0, 0), UiStyle.RadiusSmall, 0, 12, 6));
        theme.SetStylebox("hover_pressed", "MenuBar", UiStyle.Box(new Color(UiStyle.Accent, .16f), new Color(0, 0, 0, 0), UiStyle.RadiusSmall, 0, 12, 6));
        theme.SetStylebox("disabled", "MenuBar", empty); theme.SetStylebox("focus", "MenuBar", empty);
        theme.SetColor("font_color", "MenuBar", UiStyle.Text); theme.SetColor("font_hover_color", "MenuBar", Colors.White); theme.SetColor("font_pressed_color", "MenuBar", Colors.White);
        theme.SetColor("font_hover_pressed_color", "MenuBar", Colors.White); theme.SetColor("font_focus_color", "MenuBar", UiStyle.Text); theme.SetColor("font_disabled_color", "MenuBar", UiStyle.TextDisabled);
        theme.SetFont("font", "MenuBar", UiStyle.Regular); theme.SetFontSize("font_size", "MenuBar", 16);

        // sliders, progress, scroll bars
        theme.SetStylebox("slider", "HSlider", UiStyle.Box(new Color("#46525A"), new Color(0, 0, 0, 0), 4, 0, 0, 0));
        theme.SetStylebox("grabber_area", "HSlider", UiStyle.Box(UiStyle.Accent, new Color(0, 0, 0, 0), 4, 0, 0, 0));
        theme.SetStylebox("grabber_area_highlight", "HSlider", UiStyle.Box(UiStyle.Accent, new Color(0, 0, 0, 0), 4, 0, 0, 0));
        theme.SetIcon("grabber", "HSlider", SliderKnob(false)); theme.SetIcon("grabber_highlight", "HSlider", SliderKnob(true));
        theme.SetConstant("center_grabber", "HSlider", 0);
        // a track needs some height of its own or it is not drawn at all
foreach (var part in new[] { "slider", "grabber_area", "grabber_area_highlight" }) { var track = (StyleBoxFlat)theme.GetStylebox(part, "HSlider"); track.ContentMarginTop = track.ContentMarginBottom = 3.5f; }
        theme.SetStylebox("background", "ProgressBar", UiStyle.Box(new Color("#5F656A"), new Color(0, 0, 0, 0), 5, 0, 0, 0));
        theme.SetStylebox("fill", "ProgressBar", UiStyle.Box(UiStyle.Accent, new Color(0, 0, 0, 0), 5, 0, 0, 0));
        theme.SetColor("font_color", "ProgressBar", UiStyle.Text);
        foreach (var bar in new[] { "VScrollBar", "HScrollBar" })
        {
            theme.SetStylebox("scroll", bar, UiStyle.Box(new Color(0, 0, 0, .25f), new Color(0, 0, 0, 0), 5, 0, 0, 0));
            theme.SetStylebox("grabber", bar, UiStyle.Box(new Color(UiStyle.Accent, .75f), new Color(0, 0, 0, 0), 5, 0, 0, 0));
            theme.SetStylebox("grabber_highlight", bar, UiStyle.Box(UiStyle.Accent, new Color(0, 0, 0, 0), 5, 0, 0, 0));
            theme.SetStylebox("grabber_pressed", bar, UiStyle.Box(UiStyle.AccentText, new Color(0, 0, 0, 0), 5, 0, 0, 0));
        }

        // lists and trees
        theme.SetStylebox("panel", "Tree", UiStyle.Box(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), 0, 0, 4, 4));
        var selected = UiStyle.Box(new Color("#076F69"), new Color(0, 0, 0, 0), UiStyle.Radius, 0, 4, 2);
        theme.SetStylebox("selected", "Tree", selected); theme.SetStylebox("selected_focus", "Tree", selected);
        theme.SetStylebox("cursor", "Tree", empty); theme.SetStylebox("cursor_unfocused", "Tree", empty);
        theme.SetColor("font_color", "Tree", UiStyle.Text); theme.SetColor("font_selected_color", "Tree", Colors.White); theme.SetColor("title_button_color", "Tree", UiStyle.Text);
        theme.SetColor("guide_color", "Tree", UiStyle.Divider); theme.SetConstant("v_separation", "Tree", 6);
        theme.SetStylebox("title_button_normal", "Tree", UiStyle.Box(UiStyle.ButtonFill, UiStyle.Border, 0, 1, 6, 4));
        theme.SetStylebox("panel", "ItemList", UiStyle.Box(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), 0, 0, 4, 4));
        theme.SetStylebox("selected", "ItemList", UiStyle.Box(new Color("#076F69"), UiStyle.Accent, UiStyle.Radius, 1, 4, 2)); theme.SetStylebox("selected_focus", "ItemList", UiStyle.Box(new Color("#076F69"), UiStyle.Accent, UiStyle.Radius, 1, 4, 2));
        theme.SetStylebox("hovered", "ItemList", UiStyle.Box(new Color(1, 1, 1, .06f), new Color(0, 0, 0, 0), UiStyle.Radius, 0, 4, 2));
        theme.SetStylebox("cursor", "ItemList", empty); theme.SetStylebox("cursor_unfocused", "ItemList", empty);
        theme.SetColor("font_color", "ItemList", UiStyle.Text); theme.SetColor("font_selected_color", "ItemList", Colors.White); theme.SetColor("guide_color", "ItemList", UiStyle.Divider);

        // tabs
        theme.SetStylebox("panel", "TabContainer", UiStyle.Box(new Color(0, 0, 0, .12f), new Color(UiStyle.BorderSoft, .5f), UiStyle.RadiusSmall, 1, 8, 8));
        theme.SetStylebox("tab_selected", "TabContainer", UiStyle.Box(UiStyle.AccentFill, UiStyle.Accent, UiStyle.RadiusSmall, 2, 14, 6));
        theme.SetStylebox("tab_unselected", "TabContainer", UiStyle.Box(UiStyle.ButtonFill, UiStyle.Border, UiStyle.RadiusSmall, 1, 14, 6));
        theme.SetStylebox("tab_hovered", "TabContainer", UiStyle.Box(UiStyle.ButtonFillHover, UiStyle.BorderBright, UiStyle.RadiusSmall, 1, 14, 6));
        theme.SetColor("font_selected_color", "TabContainer", Colors.White); theme.SetColor("font_unselected_color", "TabContainer", UiStyle.TextMuted); theme.SetColor("font_hovered_color", "TabContainer", UiStyle.Text);
        theme.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = UiStyle.Divider, Thickness = 1 });
        theme.SetStylebox("separator", "VSeparator", new StyleBoxLine { Color = UiStyle.Divider, Thickness = 1, Vertical = true });

        // tooltips
        theme.SetStylebox("panel", "TooltipPanel", UiStyle.Box(new Color(UiStyle.PanelFillSolid, .97f), UiStyle.Accent, UiStyle.Radius, 1, 12, 8, 8));
        theme.SetColor("font_color", "TooltipLabel", UiStyle.Text);
        return theme;
    }

    private static Texture2D SliderKnob(bool highlight)
    {
        var image = Image.CreateEmpty(44, 44, false, Image.Format.Rgba8); image.Fill(new Color(0, 0, 0, 0));
        for (var y = 0; y < 44; y++) for (var x = 0; x < 44; x++)
        {
            var d = MathF.Sqrt((x + .5f - 22) * (x + .5f - 22) + (y + .5f - 22) * (y + .5f - 22));
            var ring = Math.Clamp(.5f - (d - 20f), 0f, 1f); var inner = Math.Clamp(.5f - (d - 14f), 0f, 1f);
            if (ring > 0) image.SetPixel(x, y, new Color(UiStyle.Accent, ring));
            if (inner > 0) image.SetPixel(x, y, highlight ? UiStyle.AccentText : new Color("#E9FFFD"));
        }
        return ImageTexture.CreateFromImage(image);
    }
}
