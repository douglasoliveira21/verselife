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

    private Control? _grip;
    private bool _resizing;
    private Vector2 _minimumSize = new(340, 300);

    /// Size the window can be dragged down to (it never gets smaller than its content needs either).
    public Vector2 MinimumDragSize { get => _minimumSize; set => _minimumSize = value; }

    /// Adds a handle in the bottom-right corner to drag the window to the size that suits the person.
    public void MakeResizable()
    {
        if (_grip is not null) return;
        var overlay = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(overlay);
        _grip = new Control { CustomMinimumSize = new Vector2(22, 22), MouseDefaultCursorShape = CursorShape.Fdiagsize, TooltipText = "Arraste para redimensionar" };
        _grip.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight); _grip.OffsetLeft = -22; _grip.OffsetTop = -22; _grip.OffsetRight = -2; _grip.OffsetBottom = -2;
        _grip.Draw += () => { for (var i = 0; i < 3; i++) _grip.DrawLine(new Vector2(18, 6 + i * 5), new Vector2(6 + i * 5, 18), new Color(UiStyle.TextMuted, .8f), 1.5f); };
        _grip.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button) { _resizing = button.Pressed; if (button.Pressed) MoveToFront(); else Interface.WindowManager.Remember(this); }
            else if (input is InputEventMouseMotion motion && _resizing) ResizeBy(motion.Relative);
        };
        overlay.AddChild(_grip);
    }

    /// Grows or shrinks the window from its bottom-right corner, keeping it on the screen and no smaller than its content.
    public void ResizeBy(Vector2 delta)
    {
        var view = GetViewportRect().Size; var floor = GetCombinedMinimumSize().Max(_minimumSize);
        var wanted = (Size + delta).Max(floor); var room = new Vector2(Math.Max(floor.X, view.X - Position.X - 8), Math.Max(floor.Y, view.Y - Position.Y - 8));
        Size = wanted.Min(room);
    }

    private string? _layoutId;
    /// <summary>Identifies the window in the saved layout (its name when it entered the tree).</summary>
    public string LayoutId => _layoutId ??= Interface.WindowManager.IdOf(Name);
    /// <summary>Whether the edges and corners resize the window (on by default).</summary>
    public bool EdgeResizable { get; set; } = true;
    public static readonly Vector2 EdgeMinimumSize = new(180, 120);

    private static readonly (float x, float y, CursorShape cursor)[] Edges =
    [(-1, 0, CursorShape.Hsize), (1, 0, CursorShape.Hsize), (0, -1, CursorShape.Vsize), (0, 1, CursorShape.Vsize), (-1, -1, CursorShape.Fdiagsize), (1, 1, CursorShape.Fdiagsize), (1, -1, CursorShape.Bdiagsize), (-1, 1, CursorShape.Bdiagsize)];

    /// <summary>Eight handles (four edges, four corners) all around the window. dx/dy say which sides move: -1 left/top, +1 right/bottom.</summary>
    private void BuildEdgeHandles()
    {
        var overlay = new Control { Name = "EdgeHandles", MouseFilter = MouseFilterEnum.Ignore }; AddChild(overlay);
        const float edge = 5f, corner = 12f;
        foreach (var (x, y, cursor) in Edges)
        {
            var handle = new Control { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = cursor };
            var corners = x != 0 && y != 0;
            var thick = corners ? corner : edge;
            // a side that moves is a thin strip on that side; a side that does not move spans the window between the corners
            if (x < 0) { handle.AnchorLeft = handle.AnchorRight = 0; handle.OffsetLeft = 0; handle.OffsetRight = thick; }
            else if (x > 0) { handle.AnchorLeft = handle.AnchorRight = 1; handle.OffsetLeft = -thick; handle.OffsetRight = 0; }
            else { handle.AnchorLeft = 0; handle.AnchorRight = 1; handle.OffsetLeft = corner; handle.OffsetRight = -corner; }
            if (y < 0) { handle.AnchorTop = handle.AnchorBottom = 0; handle.OffsetTop = 0; handle.OffsetBottom = thick; }
            else if (y > 0) { handle.AnchorTop = handle.AnchorBottom = 1; handle.OffsetTop = -thick; handle.OffsetBottom = 0; }
            else { handle.AnchorTop = 0; handle.AnchorBottom = 1; handle.OffsetTop = corner; handle.OffsetBottom = -corner; }
            var dx = x; var dy = y; var active = false;
            handle.GuiInput += input =>
            {
                if (!EdgeResizable || _minimized) return;
                if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button) { active = button.Pressed; if (button.Pressed) MoveToFront(); else Interface.WindowManager.Remember(this); }
                else if (input is InputEventMouseMotion motion && active) ResizeEdge(dx, dy, motion.Relative);
            };
            overlay.AddChild(handle);
        }
    }

    /// <summary>Moves the sides named by dx/dy (-1 left/top, +1 right/bottom) by the mouse movement, never below the minimum size and never off the screen.</summary>
    public void ResizeEdge(float dx, float dy, Vector2 delta)
    {
        var view = GetViewportRect().Size; var floor = GetCombinedMinimumSize().Max(EdgeMinimumSize);
        var pos = Position; var size = Size;
        if (dx > 0) size.X = Math.Max(floor.X, size.X + delta.X); else if (dx < 0) { var w = Math.Max(floor.X, size.X - delta.X); pos.X += size.X - w; size.X = w; }
        if (dy > 0) size.Y = Math.Max(floor.Y, size.Y + delta.Y); else if (dy < 0) { var h = Math.Max(floor.Y, size.Y - delta.Y); pos.Y += size.Y - h; size.Y = h; }
        if (pos.X < 0) { size.X += pos.X; pos.X = 0; }
        if (pos.Y < Interface.WindowManager.TopLimit) { size.Y += pos.Y - Interface.WindowManager.TopLimit; pos.Y = Interface.WindowManager.TopLimit; }
        size = size.Max(floor); size.X = Math.Min(size.X, Math.Max(floor.X, view.X - pos.X)); size.Y = Math.Min(size.Y, Math.Max(floor.Y, view.Y - pos.Y));
        Position = pos; Size = size;
    }

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
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 7);
        bar.AddChild(row);
        if (icon is not null) { var glyph = UiStyle.IconRect(icon, UiStyle.Text, 21); glyph.MouseFilter = MouseFilterEnum.Ignore; row.AddChild(glyph); }
        _title.Text = title; _title.SizeFlagsHorizontal = SizeFlags.ExpandFill; _title.MouseFilter = MouseFilterEnum.Ignore; _title.VerticalAlignment = VerticalAlignment.Center;
        _title.AddThemeColorOverride("font_color", UiStyle.Text); _title.AddThemeFontOverride("font", UiStyle.Bold); _title.AddThemeFontSizeOverride("font_size", 16);
        row.AddChild(_title);
        if (minimizable) row.AddChild(ChromeButton("minimize", "Minimizar", ToggleMinimized));
        if (expandable) row.AddChild(ChromeButton("expand", "Expandir", () => { _expanded = !_expanded; ExpandToggled?.Invoke(_expanded); }));
        if (closable) row.AddChild(ChromeButton("close", "Fechar", () => { Visible = false; CloseRequested?.Invoke(); }));
        var body = new MarginContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) body.AddThemeConstantOverride(side, 10);
        root.AddChild(body);
        _body = body;
        body.AddChild(Content);
        Content.AddThemeConstantOverride("separation", 6);
        BuildEdgeHandles();
        bar.GuiInput += OnBarInput;
        TreeEntered += () => { _layoutId = Interface.WindowManager.IdOf(Name); Callable.From(() => Interface.WindowManager.Register(this)).CallDeferred(); };
        GuiInput += input => { if (input is InputEventMouseButton { Pressed: true }) MoveToFront(); };
    }

    private static Button ChromeButton(string icon, string tooltip, Action action)
    {
        var button = new Button { Icon = UiStyle.Icon(icon, UiStyle.Text, 18), Flat = true, FocusMode = FocusModeEnum.None, TooltipText = tooltip, CustomMinimumSize = new Vector2(28, 28), ExpandIcon = false };
        var hover = UiStyle.Box(new Color(1, 1, 1, .10f), new Color(0, 0, 0, 0), UiStyle.RadiusSmall, 0, 0, 0);
        button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); button.AddThemeStyleboxOverride("hover", hover); button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("hover_pressed", hover); button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.Pressed += action;
        return button;
    }

    /// Hides the body and leaves only the title bar.
    public void ToggleMinimized() { _minimized = !_minimized; _body.Visible = !_minimized; ResetSize(); }

    private bool _ownOpacity;
    /// <summary>This window's own opacity: it keeps it when the person changes the opacity of all windows.</summary>
    public void SetOpacity(float alpha) { _ownOpacity = true; AddThemeStyleboxOverride("panel", ViewerTheme.WindowBox(alpha)); }
    /// <summary>The opacity of every window (Configurações → Interface), except those with their own.</summary>
    public void SetBaseOpacity(float alpha) { if (!_ownOpacity) AddThemeStyleboxOverride("panel", ViewerTheme.WindowBox(alpha)); }

    public void Place(float anchorX, float anchorY, float left, float top, float right, float bottom)
    {
        AnchorLeft = AnchorRight = anchorX; AnchorTop = AnchorBottom = anchorY;
        OffsetLeft = left; OffsetTop = top; OffsetRight = right; OffsetBottom = bottom;
        GrowHorizontal = anchorX > .5f ? GrowDirection.Begin : GrowDirection.End;
        GrowVertical = anchorY > .5f ? GrowDirection.Begin : GrowDirection.End;
    }

    private void OnBarInput(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button) { _dragging = button.Pressed; if (button.Pressed) MoveToFront(); else Interface.WindowManager.Remember(this); }
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
        control.Position = new Vector2(Mathf.Clamp(control.Position.X, 40 - control.Size.X, viewport.X - 40), Mathf.Clamp(control.Position.Y, Interface.WindowManager.TopLimit, viewport.Y - 40));
    }
}
