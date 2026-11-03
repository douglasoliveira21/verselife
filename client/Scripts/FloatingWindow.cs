using Godot;

namespace LifeVerse.Client.Scripts;

/// Draggable floating glass window: an icon and title on the left, minimize / expand / close on the right (see client/Assets/ui).
public partial class FloatingWindow : PanelContainer
{
    private readonly Label _title = new();
    private readonly Control _body;
    private bool _dragging;
    private bool _minimized;
    private bool _expanded;

    public VBoxContainer Content { get; } = new();
    public event Action? CloseRequested;
    /// Raised when the expand button is pressed; the argument says whether the window should now be larger.
    public event Action<bool>? ExpandToggled;

    public bool Minimized => _minimized;

    public FloatingWindow(string title, bool closable = true, string? icon = null, bool minimizable = false, bool expandable = false)
    {
        Name = title.Replace(' ', '_').Replace('-', '_');
        AddThemeStyleboxOverride("panel", ViewerTheme.WindowBox());
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);
        var bar = new PanelContainer { MouseDefaultCursorShape = CursorShape.Move };
        bar.AddThemeStyleboxOverride("panel", ViewerTheme.TitleBar());
        root.AddChild(bar);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
        bar.AddChild(row);
        if (icon is not null) { var glyph = UiStyle.IconRect(icon, UiStyle.Text, 26); glyph.MouseFilter = MouseFilterEnum.Ignore; row.AddChild(glyph); }
        _title.Text = title; _title.SizeFlagsHorizontal = SizeFlags.ExpandFill; _title.MouseFilter = MouseFilterEnum.Ignore; _title.VerticalAlignment = VerticalAlignment.Center;
        _title.AddThemeColorOverride("font_color", UiStyle.Text); _title.AddThemeFontOverride("font", UiStyle.Bold); _title.AddThemeFontSizeOverride("font_size", 19);
        row.AddChild(_title);
        if (minimizable) row.AddChild(ChromeButton("minimize", "Minimizar", ToggleMinimized));
        if (expandable) row.AddChild(ChromeButton("expand", "Expandir", () => { _expanded = !_expanded; ExpandToggled?.Invoke(_expanded); }));
        if (closable) row.AddChild(ChromeButton("close", "Fechar", () => { Visible = false; CloseRequested?.Invoke(); }));
        var body = new MarginContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) body.AddThemeConstantOverride(side, 14);
        root.AddChild(body);
        _body = body;
        body.AddChild(Content);
        Content.AddThemeConstantOverride("separation", 8);
        bar.GuiInput += OnBarInput;
        GuiInput += input => { if (input is InputEventMouseButton { Pressed: true }) MoveToFront(); };
    }

    private static Button ChromeButton(string icon, string tooltip, Action action)
    {
        var button = new Button { Icon = UiStyle.Icon(icon, UiStyle.Text, 22), Flat = true, FocusMode = FocusModeEnum.None, TooltipText = tooltip, CustomMinimumSize = new Vector2(34, 34), ExpandIcon = false };
        var hover = UiStyle.Box(new Color(1, 1, 1, .10f), new Color(0, 0, 0, 0), UiStyle.RadiusSmall, 0, 0, 0);
        button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); button.AddThemeStyleboxOverride("hover", hover); button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("hover_pressed", hover); button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.Pressed += action;
        return button;
    }

    /// Hides the body and leaves only the title bar.
    public void ToggleMinimized() { _minimized = !_minimized; _body.Visible = !_minimized; ResetSize(); }

    public void SetOpacity(float alpha) => AddThemeStyleboxOverride("panel", ViewerTheme.WindowBox(alpha));

    public void Place(float anchorX, float anchorY, float left, float top, float right, float bottom)
    {
        AnchorLeft = AnchorRight = anchorX; AnchorTop = AnchorBottom = anchorY;
        OffsetLeft = left; OffsetTop = top; OffsetRight = right; OffsetBottom = bottom;
        GrowHorizontal = anchorX > .5f ? GrowDirection.Begin : GrowDirection.End;
        GrowVertical = anchorY > .5f ? GrowDirection.Begin : GrowDirection.End;
    }

    private void OnBarInput(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button) { _dragging = button.Pressed; if (button.Pressed) MoveToFront(); }
        else if (input is InputEventMouseMotion motion && _dragging) { Position += motion.Relative; Clamp(this); }
    }

    /// Lets any existing panel be dragged by its background and brought to the front.
    public static void MakeDraggable(Control panel)
    {
        var dragging = false;
        panel.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
            {
                dragging = button.Pressed;
                if (button.Pressed)
                {
                    var rect = panel.GetGlobalRect();
                    panel.SetAnchorsPreset(LayoutPreset.TopLeft, true);
                    panel.GlobalPosition = rect.Position;
                    panel.GetParent()?.MoveChild(panel, -1);
                }
            }
            else if (input is InputEventMouseMotion motion && dragging) { panel.Position += motion.Relative; Clamp(panel); }
        };
    }

    private static void Clamp(Control control)
    {
        var viewport = control.GetViewportRect().Size;
        control.Position = new Vector2(Mathf.Clamp(control.Position.X, 40 - control.Size.X, viewport.X - 40), Mathf.Clamp(control.Position.Y, 28, viewport.Y - 40));
    }
}
