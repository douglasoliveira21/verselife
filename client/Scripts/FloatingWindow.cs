using Godot;

namespace LifeVerse.Client.Scripts;

/// Draggable floating window with a title bar, like the panels of a desktop viewer.
public partial class FloatingWindow : PanelContainer
{
    private readonly Label _title = new();
    private bool _dragging;

    public VBoxContainer Content { get; } = new();
    public event Action? CloseRequested;

    public FloatingWindow(string title, bool closable = true)
    {
        Name = title.Replace(' ', '_').Replace('-', '_');
        AddThemeStyleboxOverride("panel", ViewerTheme.WindowBox());
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);
        var bar = new PanelContainer { MouseDefaultCursorShape = CursorShape.Move };
        bar.AddThemeStyleboxOverride("panel", ViewerTheme.TitleBar());
        root.AddChild(bar);
        var row = new HBoxContainer();
        bar.AddChild(row);
        _title.Text = title; _title.SizeFlagsHorizontal = SizeFlags.ExpandFill; _title.MouseFilter = MouseFilterEnum.Ignore;
        _title.AddThemeColorOverride("font_color", new Color("#e8e8e8"));
        row.AddChild(_title);
        if (closable)
        {
            var close = new Button { Text = "x", Flat = true, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(22, 0), TooltipText = "Fechar" };
            close.Pressed += () => { Visible = false; CloseRequested?.Invoke(); };
            row.AddChild(close);
        }
        var body = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) body.AddThemeConstantOverride(side, 8);
        root.AddChild(body);
        body.AddChild(Content);
        Content.AddThemeConstantOverride("separation", 6);
        bar.GuiInput += OnBarInput;
        GuiInput += input => { if (input is InputEventMouseButton { Pressed: true }) MoveToFront(); };
    }

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
