using Godot;

namespace LifeVerse.Client.Scripts;

/// Dark-grey desktop-viewer look: flat grey windows with light title bars, compact buttons and a blue selection accent.
public static class ViewerTheme
{
    public static readonly Color Text = new("#dcdcdc");
    public static readonly Color TextDim = new("#9a9a9a");
    public static readonly Color Accent = new("#2e6fae");
    public static readonly Color AccentLight = new("#5aa0e0");
    public static readonly Color Good = new("#5fcf6b");
    public static readonly Color Warn = new("#e0b050");
    public static readonly Color Bad = new("#e0605a");

    public static StyleBoxFlat Box(Color background, Color border, int radius = 3, int borderWidth = 1, float margin = 6)
    {
        var box = new StyleBoxFlat { BgColor = background, BorderColor = border };
        box.SetBorderWidthAll(borderWidth);
        box.SetCornerRadiusAll(radius);
        box.SetContentMarginAll(margin);
        return box;
    }

    public static StyleBoxFlat WindowBox(float alpha = .95f) => Box(new Color(.15f, .15f, .15f, alpha), new Color("#4f4f4f"), 4, 1, 0);
    public static StyleBoxFlat TitleBar() => Box(new Color("#3b3b3b"), new Color("#4f4f4f"), 3, 0, 5);
    public static StyleBoxFlat BarBox() => Box(new Color(.14f, .14f, .14f, .96f), new Color("#4a4a4a"), 0, 1, 4);

    public static Theme Create()
    {
        var theme = new Theme { DefaultFontSize = 14 };
        var empty = new StyleBoxEmpty();

        theme.SetColor("font_color", "Label", Text);
        theme.SetStylebox("panel", "PanelContainer", Box(new Color(.16f, .16f, .16f, .96f), new Color("#4f4f4f"), 4, 1, 8));
        theme.SetStylebox("panel", "Panel", Box(new Color(.16f, .16f, .16f, .96f), new Color("#4f4f4f"), 4, 1, 0));

        var normal = Box(new Color("#404040"), new Color("#5a5a5a"), 3, 1, 6);
        var hover = Box(new Color("#515151"), new Color("#7a7a7a"), 3, 1, 6);
        var pressed = Box(Accent, AccentLight, 3, 1, 6);
        var disabled = Box(new Color("#303030"), new Color("#3c3c3c"), 3, 1, 6);
        foreach (var type in new[] { "Button", "OptionButton", "MenuButton" })
        {
            theme.SetStylebox("normal", type, normal); theme.SetStylebox("hover", type, hover); theme.SetStylebox("pressed", type, pressed);
            theme.SetStylebox("hover_pressed", type, Box(new Color("#3a7fc2"), AccentLight, 3, 1, 6)); theme.SetStylebox("disabled", type, disabled); theme.SetStylebox("focus", type, empty);
            theme.SetColor("font_color", type, Text); theme.SetColor("font_hover_color", type, Colors.White); theme.SetColor("font_pressed_color", type, Colors.White);
            theme.SetColor("font_hover_pressed_color", type, Colors.White); theme.SetColor("font_focus_color", type, Text); theme.SetColor("font_disabled_color", type, new Color("#6e6e6e"));
        }
        foreach (var type in new[] { "CheckButton", "CheckBox" })
        {
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" }) theme.SetStylebox(state, type, empty);
            theme.SetColor("font_color", type, Text); theme.SetColor("font_hover_color", type, Colors.White); theme.SetColor("font_pressed_color", type, Text); theme.SetColor("font_hover_pressed_color", type, Colors.White);
        }

        var field = Box(new Color("#171717"), new Color("#565656"), 3, 1, 6);
        theme.SetStylebox("normal", "LineEdit", field);
        theme.SetStylebox("focus", "LineEdit", Box(new Color("#171717"), AccentLight, 3, 1, 6));
        theme.SetStylebox("read_only", "LineEdit", Box(new Color("#202020"), new Color("#444444"), 3, 1, 6));
        theme.SetColor("font_color", "LineEdit", Text); theme.SetColor("font_placeholder_color", "LineEdit", new Color("#7c7c7c"));
        theme.SetColor("caret_color", "LineEdit", Colors.White); theme.SetColor("selection_color", "LineEdit", Accent);
        theme.SetStylebox("normal", "TextEdit", field); theme.SetStylebox("focus", "TextEdit", Box(new Color("#171717"), AccentLight, 3, 1, 6));
        theme.SetColor("font_color", "TextEdit", Text); theme.SetColor("selection_color", "TextEdit", Accent); theme.SetColor("caret_color", "TextEdit", Colors.White);

        theme.SetColor("default_color", "RichTextLabel", Text);
        theme.SetStylebox("normal", "RichTextLabel", empty);

        theme.SetStylebox("panel", "PopupMenu", Box(new Color("#262626"), new Color("#5a5a5a"), 2, 1, 4));
        theme.SetStylebox("hover", "PopupMenu", Box(Accent, Accent, 2, 0, 3));
        theme.SetColor("font_color", "PopupMenu", Text); theme.SetColor("font_hover_color", "PopupMenu", Colors.White); theme.SetColor("font_disabled_color", "PopupMenu", new Color("#6e6e6e"));
        theme.SetColor("font_separator_color", "PopupMenu", TextDim);
        theme.SetConstant("v_separation", "PopupMenu", 7); theme.SetConstant("item_start_padding", "PopupMenu", 8); theme.SetConstant("item_end_padding", "PopupMenu", 8);

        theme.SetStylebox("normal", "MenuBar", Box(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), 2, 0, 8)); theme.SetStylebox("hover", "MenuBar", Box(new Color("#4b4b4b"), new Color("#4b4b4b"), 2, 0, 8));
        theme.SetStylebox("pressed", "MenuBar", Box(Accent, Accent, 2, 0, 8)); theme.SetStylebox("hover_pressed", "MenuBar", Box(Accent, Accent, 2, 0, 8)); theme.SetStylebox("disabled", "MenuBar", empty);
        theme.SetColor("font_color", "MenuBar", Text); theme.SetColor("font_hover_color", "MenuBar", Colors.White); theme.SetColor("font_pressed_color", "MenuBar", Colors.White); theme.SetColor("font_hover_pressed_color", "MenuBar", Colors.White);

        theme.SetStylebox("slider", "HSlider", Box(new Color("#101010"), new Color("#505050"), 2, 1, 0)); theme.SetStylebox("grabber_area", "HSlider", Box(Accent, Accent, 2, 0, 0)); theme.SetStylebox("grabber_area_highlight", "HSlider", Box(AccentLight, AccentLight, 2, 0, 0));

        theme.SetStylebox("panel", "Tree", Box(new Color("#171717"), new Color("#4a4a4a"), 2, 1, 4));
        theme.SetStylebox("selected", "Tree", Box(Accent, Accent, 2, 0, 2)); theme.SetStylebox("selected_focus", "Tree", Box(Accent, Accent, 2, 0, 2));
        theme.SetColor("font_color", "Tree", Text); theme.SetColor("font_selected_color", "Tree", Colors.White); theme.SetColor("title_button_color", "Tree", Text);
        theme.SetStylebox("title_button_normal", "Tree", Box(new Color("#383838"), new Color("#4a4a4a"), 0, 1, 4));

        theme.SetStylebox("panel", "TabContainer", Box(new Color("#1c1c1c"), new Color("#4a4a4a"), 2, 1, 6));
        theme.SetStylebox("tab_selected", "TabContainer", Box(new Color("#454545"), AccentLight, 2, 1, 6)); theme.SetStylebox("tab_unselected", "TabContainer", Box(new Color("#2e2e2e"), new Color("#444444"), 2, 1, 6));
        theme.SetStylebox("tab_hovered", "TabContainer", Box(new Color("#3a3a3a"), new Color("#666666"), 2, 1, 6));
        theme.SetColor("font_selected_color", "TabContainer", Colors.White); theme.SetColor("font_unselected_color", "TabContainer", TextDim); theme.SetColor("font_hovered_color", "TabContainer", Text);

        theme.SetStylebox("panel", "TooltipPanel", Box(new Color("#1a1a1a"), new Color("#6a6a6a"), 2, 1, 5));
        theme.SetColor("font_color", "TooltipLabel", Text);
        return theme;
    }
}
