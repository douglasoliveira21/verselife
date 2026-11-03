using Godot;

namespace LifeVerse.Client.Scripts;

/// A row of tabs as in the design (client/Assets/ui/11 and 05): outlined buttons, the selected one filled teal with a neon
/// line under it. Tabs are added with AddTab; clicking one selects it and raises TabChanged.
public partial class UiTabs : HBoxContainer
{
    private readonly List<TabButton> _tabs = [];
    private int _selected = -1;

    public event Action<int>? TabChanged;
    public int Selected => _selected;
    public int Count => _tabs.Count;

    /// Equal-width tabs fill the row (the people window); otherwise each tab is as wide as its label.
    public bool EqualWidth { get; set; }

    /// Flat tabs as in the build window: no boxes, just the text, a neon line under the selected one and a thin line under the row.
    public bool Flat { get; init; }

    /// Size of the labels of flat tabs.
    public int FontSize { get; init; } = 18;

    public UiTabs() { AddThemeConstantOverride("separation", 4); }

    public Button AddTab(string text, string? icon = null, bool iconOnly = false)
    {
        var tab = new TabButton(Flat, FontSize) { Text = iconOnly ? string.Empty : text, TooltipText = text, ToggleMode = true, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(iconOnly ? 48 : 0, Flat ? 46 : 44) };
        if (icon is not null) tab.Icon = UiStyle.Icon(icon, UiStyle.Text, 20);
        tab.ExpandIcon = false;
        if (EqualWidth) tab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var index = _tabs.Count;
        tab.Pressed += () => Select(index);
        _tabs.Add(tab); AddChild(tab);
        if (_selected < 0) Select(0, notify: false);
        return tab;
    }

    public void Select(int index, bool notify = true)
    {
        if (index < 0 || index >= _tabs.Count) return;
        _selected = index;
        for (var i = 0; i < _tabs.Count; i++) _tabs[i].SetPressedNoSignal(i == index);
        if (notify) TabChanged?.Invoke(index);
    }

    public Button TabAt(int index) => _tabs[index];

    public override void _Draw()
    {
        if (Flat) DrawRect(new Rect2(0, Size.Y - 1, Size.X, 1), new Color(UiStyle.Divider, .9f));
    }

    public partial class TabButton : Button
    {
        private readonly bool _flat;

        public TabButton(bool flat = false, int fontSize = 18)
        {
            _flat = flat;
            if (flat)
            {
                var none = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 8 };
                var hover = new StyleBoxFlat { BgColor = new Color(1, 1, 1, .06f), ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 8, CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8 };
                foreach (var state in new[] { "normal", "pressed" }) AddThemeStyleboxOverride(state, none);
                foreach (var state in new[] { "hover", "hover_pressed" }) AddThemeStyleboxOverride(state, hover);
                AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
                AddThemeFontOverride("font", UiStyle.Bold); AddThemeFontSizeOverride("font_size", fontSize);
                AddThemeConstantOverride("icon_max_width", 26); AddThemeConstantOverride("h_separation", 10);
                foreach (var color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" }) AddThemeColorOverride(color, Colors.White);
                return;
            }
            const float marginX = 18, marginY = 6;
            AddThemeStyleboxOverride("normal", UiStyle.Box(new Color(UiStyle.ButtonFill, .55f), new Color(UiStyle.Border, .75f), UiStyle.RadiusSmall + 2, 1, marginX, marginY));
            AddThemeStyleboxOverride("hover", UiStyle.Box(new Color(UiStyle.AccentFill, .55f), UiStyle.BorderBright, UiStyle.RadiusSmall + 2, 1, marginX, marginY));
            AddThemeStyleboxOverride("pressed", UiStyle.Box(new Color("#0D4A4B"), UiStyle.Accent, UiStyle.RadiusSmall + 2, 1, marginX, marginY));
            AddThemeStyleboxOverride("hover_pressed", UiStyle.Box(new Color("#115A5A"), UiStyle.Accent, UiStyle.RadiusSmall + 2, 1, marginX, marginY));
            AddThemeStyleboxOverride("disabled", UiStyle.Box(new Color(UiStyle.ButtonFillDisabled, .6f), new Color(UiStyle.Border, .4f), UiStyle.RadiusSmall + 2, 1, marginX, marginY));
            AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            AddThemeFontOverride("font", UiStyle.Medium); AddThemeFontSizeOverride("font_size", 17);
            foreach (var color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color" }) AddThemeColorOverride(color, Colors.White);
            AddThemeColorOverride("font_disabled_color", UiStyle.TextDisabled);
            AddThemeConstantOverride("icon_max_width", 20); AddThemeConstantOverride("h_separation", 10);
        }

        public override void _Draw()
        {
            if (!ButtonPressed) return;
            DrawRect(_flat ? new Rect2(4, Size.Y - 3, Size.X - 8, 3) : new Rect2(6, Size.Y - 4, Size.X - 12, 3), UiStyle.Accent);
        }
    }
}
