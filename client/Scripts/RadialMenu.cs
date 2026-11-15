using Godot;
using LifeVerse.Shared.Gameplay;
using NVector2 = System.Numerics.Vector2;

namespace LifeVerse.Client.Scripts;

/// One slice of a radial menu: an icon and a name, and either an action, a smaller circle of further choices or (SidePanel) a list beside the slice.
public sealed class RadialItem
{
    public required string Label { get; init; }
    public string Icon { get; init; } = "dots";
    public string? Tooltip { get; init; }
    public Action? Run { get; init; }
    public List<RadialItem>? Children { get; init; }
    public bool Enabled { get; init; } = true;
    /// The orange look of the design (for closing and for things that stay out of the way).
    public bool Accent { get; init; }
    /// Something that removes or cannot be undone: red icon. With Confirm it asks first inside the circle.
    public bool Destructive { get; init; }
    /// The question asked before a destructive slice runs (a circle with Confirmar / Cancelar). Null runs at once.
    public string? Confirm { get; init; }
    /// The children are shown as a list in a panel beside the slice instead of replacing the circle (for long choices).
    public bool SidePanel { get; init; }
    /// This slice goes back one circle instead of running anything.
    public bool IsBack { get; init; }
}

/// What the person chose for the menu in Configurações › Interface (size, transparency, glow, speed, tooltips), read by every menu that opens.
public static class RadialStyle
{
    public static int Size = 100, Opacity = 92, Glow = 60;
    public static float Speed = 1f;
    public static bool Tooltips = true;

    public static readonly Color Fill = new("#1B262E"), Secondary = new("#202D36"), Border = new("#6D818D"), Accent = new("#12F2E0"), Accent2 = new("#F4A340");
    public static readonly Color Text = new("#F5F7FA"), TextMuted = new("#AAB8C2"), Danger = new("#EF5350"), Success = new("#34C759"), Disabled = new("#6B7780");
    /// Dimensions at 100 % on a 1920×1080 screen: the outer and inner radius of the ring.
    public const float BaseOuter = 150, BaseInner = 55;

    /// The scale that fits the circle (and its margin) on a screen of this size.
    public static float Fit(Vector2 screen, int sizePercent) => Math.Min(sizePercent / 100f, Math.Max(.45f, screen.Y * .8f / (BaseOuter * 2f)));
}

/// The right-click menu: a dark glass ring cut into slices around the cursor (a pie menu in the style of the old virtual worlds, redone in the
/// colours of the game). Up to eight slices are spread out; nine to twelve get compact text; more become a chain of circles linked by "Mais".
/// The middle disc shows what was clicked (a picture or an icon) and, inside a smaller circle, takes you back. A hovered slice lights up in neon;
/// a click runs it, opens its circle or its side panel; Esc, a click outside or a right click closes it (Esc closes a side panel first).
public partial class RadialMenu : Control
{
    private const float PanelRowHeight = 34, PanelWidth = 236;

    private sealed record Level(string Title, string Icon, List<RadialItem> Items, Texture2D? Image = null);
    private readonly List<Level> _levels = [];
    private Vector2 _center;
    private float _outer = RadialStyle.BaseOuter, _inner = RadialStyle.BaseInner;
    private int _hover = -1, _panelOwner = -1, _panelHover = -1;
    private bool _centerHover;
    private float _anim = 1f;
    private float[] _glow = [];
    private Tween? _tween;

    public bool IsOpen => Visible;
    public int Depth => _levels.Count;
    public string Title => _levels.Count > 0 ? _levels[^1].Title : string.Empty;
    public IReadOnlyList<string> Labels => _levels.Count > 0 ? _levels[^1].Items.Select(x => x.Label).ToList() : [];
    public IReadOnlyList<string> EnabledLabels => _levels.Count > 0 ? _levels[^1].Items.Where(x => x.Enabled).Select(x => x.Label).ToList() : [];
    public Vector2 CenterForTesting => _center;
    public float OuterRadius => _outer;
    public float InnerRadius => _inner;
    public bool Compact => _levels.Count > 0 && _levels[^1].Items.Count > 8;
    public bool SidePanelOpen => _panelOwner >= 0;
    public IReadOnlyList<string> PanelLabels => _panelOwner >= 0 ? PanelItems.Select(x => x.Label).ToList() : [];
    public event Action? Closed;
    public event Action? DeleteRequested;

    public RadialMenu()
    {
        Name = "RadialMenu"; Visible = false; MouseFilter = MouseFilterEnum.Stop; FocusMode = FocusModeEnum.All;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    private float OpenSeconds => Accessibility.ReduceMotion ? .01f : .15f / Math.Max(.25f, RadialStyle.Speed);

    // ---- opening, going deeper and back ---------------------------------------------------------------------

    /// Opens the circle around a screen position (moved inwards if it would run off the screen). <paramref name="image"/> fills the middle disc when given.
    public void Open(Vector2 screenPosition, string title, string icon, IReadOnlyList<RadialItem> items, Texture2D? image = null)
    {
        var size = GetViewportRect().Size;
        var scale = RadialStyle.Fit(size, RadialStyle.Size);
        _outer = RadialStyle.BaseOuter * scale; _inner = RadialStyle.BaseInner * scale;
        var kept = RadialMath.KeepOnScreen(new NVector2(screenPosition.X, screenPosition.Y), _outer + 8, new NVector2(size.X, size.Y));
        _center = new Vector2(kept.X, kept.Y);
        _levels.Clear(); _hover = -1; _centerHover = false; _panelOwner = -1; _panelHover = -1;
        _levels.Add(new Level(title, icon, Chain(items.ToList()), image)); ResetGlow();
        Visible = true; GrabFocus(); Animate(.8f); SetProcess(true);
        QueueRedraw();
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false; _levels.Clear(); _panelOwner = -1; _tween?.Kill(); ReleaseFocus(); SetProcess(false);
        Closed?.Invoke();
    }

    /// More than twelve entries become a chain of circles: eleven slices and a "Mais" slice that opens the rest.
    private static List<RadialItem> Chain(List<RadialItem> items)
    {
        if (items.Count <= RadialMath.MaxSlices) return items;
        var first = items.Take(RadialMath.MaxSlices - 1).ToList();
        first.Add(new RadialItem { Label = "Mais", Icon = "dots", Children = Chain(items.Skip(RadialMath.MaxSlices - 1).ToList()) });
        return first;
    }

    private void ResetGlow() => _glow = new float[Items.Count];

    /// Swaps the slices of the first circle while it is open (information arrived: a friendship is now known). The hover and an open side panel stay where they are.
    public bool ReplaceRoot(IReadOnlyList<RadialItem> items)
    {
        if (!Visible || _levels.Count != 1) return false;
        var openLabel = _panelOwner >= 0 && _panelOwner < Items.Count ? Items[_panelOwner].Label : null;
        var old = _levels[0]; var chained = Chain(items.ToList());
        _levels[0] = old with { Items = chained };
        _panelOwner = openLabel is null ? -1 : chained.FindIndex(x => x.Label == openLabel && x.Children is { Count: > 0 });
        if (_hover >= chained.Count) _hover = -1;
        ResetGlow(); QueueRedraw();
        return true;
    }

    private void Push(RadialItem item)
    {
        _levels.Add(new Level(item.Label, item.Icon, Chain(item.Children!.ToList()))); _hover = -1; _centerHover = false; _panelOwner = -1; ResetGlow();
        Animate(.7f); QueueRedraw();
    }

    public void Back()
    {
        if (_panelOwner >= 0) { _panelOwner = -1; _panelHover = -1; QueueRedraw(); return; }
        if (_levels.Count <= 1) { Close(); return; }
        _levels.RemoveAt(_levels.Count - 1); _hover = -1; ResetGlow(); Animate(.7f); QueueRedraw();
    }

    private void Animate(float from)
    {
        _tween?.Kill(); _anim = from;
        _tween = CreateTween(); _tween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        _tween.TweenMethod(Callable.From<float>(value => { _anim = value; QueueRedraw(); }), from, 1f, OpenSeconds);
    }

    public override void _Process(double delta)
    {
        if (!Visible || _glow.Length != Items.Count) return;
        var step = (float)delta / (Accessibility.ReduceMotion ? .01f : .1f / Math.Max(.25f, RadialStyle.Speed)); var changed = false;
        for (var i = 0; i < _glow.Length; i++)
        {
            var target = i == _hover || i == _panelOwner ? 1f : 0f;
            var next = Mathf.MoveToward(_glow[i], target, step);
            if (!Mathf.IsEqualApprox(next, _glow[i])) { _glow[i] = next; changed = true; }
        }
        if (changed) QueueRedraw();
    }

    // ---- choosing --------------------------------------------------------------------------------------------

    private List<RadialItem> Items => _levels.Count > 0 ? _levels[^1].Items : [];
    private List<RadialItem> PanelItems => _panelOwner >= 0 && _panelOwner < Items.Count ? Items[_panelOwner].Children ?? [] : [];

    /// Runs (or opens) a slice. Returns false for a slice that cannot be used.
    public bool Pick(int index)
    {
        if (!Visible || index < 0 || index >= Items.Count || !Items[index].Enabled) return false;
        var item = Items[index];
        if (item.IsBack) { Back(); return true; }
        if (item.Children is { Count: > 0 })
        {
            if (item.SidePanel) { _panelOwner = _panelOwner == index ? -1 : index; _panelHover = -1; QueueRedraw(); return true; }
            Push(item); return true;
        }
        return Execute(item);
    }

    /// A destructive slice with a question opens a circle that asks first; anything else closes the menu and runs.
    private bool Execute(RadialItem item)
    {
        if (item.Confirm is { Length: > 0 } question && item.Run is not null)
        {
            var yes = new RadialItem { Label = "Confirmar", Icon = "check", Destructive = true, Run = item.Run, Tooltip = question };
            var no = new RadialItem { Label = "Cancelar", Icon = "close", IsBack = true };
            _levels.Add(new Level(question, item.Icon, [yes, no])); _hover = -1; _panelOwner = -1; ResetGlow(); Animate(.7f); QueueRedraw();
            return true;
        }
        Close();
        item.Run?.Invoke();
        return true;
    }

    /// Picks a row of the side panel.
    public bool PickPanel(int index)
    {
        var rows = PanelItems;
        if (index < 0 || index >= rows.Count || !rows[index].Enabled) return false;
        var row = rows[index];
        if (row.Children is { Count: > 0 }) { _panelOwner = -1; Push(row); return true; }
        _panelOwner = -1;
        return Execute(row);
    }

    public void PickForTesting(int index) => Pick(index);
    public void PickPanelForTesting(int index) => PickPanel(index);
    public void BackForTesting() => Back();
    public void HoverForTesting(Vector2 offset) => UpdateHover(_center + offset);
    public int HoverIndexForTesting => _hover;
    public Rect2 PanelRectForTesting => PanelRect();

    /// The side panel: beside the slice that opened it, on the side with room, and kept on the screen.
    private Rect2 PanelRect()
    {
        if (_panelOwner < 0) return default;
        var count = PanelItems.Count; var height = count * PanelRowHeight + 16; var screen = GetViewportRect().Size;
        var angle = RadialMath.SliceCenter(_panelOwner, Items.Count); var anchor = _center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * _outer;
        // beside the ring, on the side of the slice that opened it; on the other side when there is no room
        var roomRight = _center.X + _outer + 10 + PanelWidth + 6 <= screen.X; var roomLeft = _center.X - _outer - 10 - PanelWidth - 6 >= 0;
        var prefersLeft = Mathf.Cos(angle) < -.2f;
        var right = prefersLeft ? !roomLeft && roomRight || !roomLeft : roomRight || !roomLeft;
        var x = right ? _center.X + _outer + 10 : _center.X - _outer - 10 - PanelWidth;
        x = Mathf.Clamp(x, 6, Math.Max(6, screen.X - PanelWidth - 6));
        var y = Mathf.Clamp(anchor.Y - height / 2f, 6, Math.Max(6, screen.Y - height - 6));
        return new Rect2(x, y, PanelWidth, height);
    }

    private int PanelRowAt(Vector2 position)
    {
        if (_panelOwner < 0) return -1;
        var rect = PanelRect();
        if (!rect.HasPoint(position)) return -1;
        var row = (int)((position.Y - rect.Position.Y - 8) / PanelRowHeight);
        return row >= 0 && row < PanelItems.Count ? row : -1;
    }

    private void UpdateHover(Vector2 position)
    {
        if (!Visible || _levels.Count == 0) return;
        _panelHover = PanelRowAt(position);
        if (_panelHover >= 0)
        {
            _hover = -1; _centerHover = false; TooltipText = RadialStyle.Tooltips ? PanelItems[_panelHover].Tooltip ?? string.Empty : string.Empty; QueueRedraw(); return;
        }
        var offset = position - _center;
        _centerHover = offset.Length() < _inner - 4;
        _hover = RadialMath.SliceAt(new NVector2(offset.X, offset.Y), Items.Count, _inner, _outer + 4);
        TooltipText = RadialStyle.Tooltips && _hover >= 0 ? Items[_hover].Tooltip ?? string.Empty : string.Empty;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        if (!Visible) return;
        switch (input)
        {
            case InputEventMouseMotion motion: UpdateHover(motion.Position); break;
            case InputEventMouseButton { Pressed: true } click:
                if (click.ButtonIndex == MouseButton.Left)
                {
                    UpdateHover(click.Position);
                    var distance = click.Position.DistanceTo(_center);
                    if (_panelHover >= 0) PickPanel(_panelHover);
                    else if (distance > _outer + 4) Close();
                    else if (distance < _inner - 4) Back();
                    else Pick(_hover);
                }
                else if (click.ButtonIndex == MouseButton.Right) Close();
                AcceptEvent();
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                switch (key.Keycode)
                {
                    case Key.Escape: if (_panelOwner >= 0) Back(); else Close(); break;
                    case Key.Backspace: Back(); break;
                    case Key.Delete: DeleteRequested?.Invoke(); break;
                    case Key.Enter or Key.KpEnter or Key.Space: if (_panelHover >= 0) PickPanel(_panelHover); else Pick(_hover); break;
                    case Key.Left or Key.Up: if (_panelOwner >= 0) { _panelHover = (PanelItems.Count + _panelHover - 1) % PanelItems.Count; QueueRedraw(); } else { _hover = RadialMath.Step(_hover, -1, Items.Count); QueueRedraw(); } break;
                    case Key.Right or Key.Down: if (_panelOwner >= 0) { _panelHover = (_panelHover + 1) % PanelItems.Count; QueueRedraw(); } else { _hover = RadialMath.Step(_hover, 1, Items.Count); QueueRedraw(); } break;
                    default:
                        var number = key.Keycode is >= Key.Key1 and <= Key.Key9 ? (int)(key.Keycode - Key.Key0) : key.Keycode is >= Key.Kp1 and <= Key.Kp9 ? (int)(key.Keycode - Key.Kp0) : 0;
                        if (number > 0) { if (_panelOwner >= 0) PickPanel(number - 1); else Pick(RadialMath.SliceForKey(number, Items.Count)); }
                        break;
                }
                AcceptEvent();
                break;
        }
    }

    // ---- drawing -----------------------------------------------------------------------------------------------

    private static Color Fade(Color color, float alpha) => new(color, color.A * alpha);

    public override void _Draw()
    {
        if (!Visible || _levels.Count == 0) return;
        var items = Items; var count = items.Count; var compact = count > 8;
        var scale = .9f + .1f * _anim; var alpha = Mathf.Clamp(_anim * 1.25f, 0f, 1f);
        var outer = _outer * scale; var inner = _inner * scale; var glow = RadialStyle.Glow / 100f; var body = RadialStyle.Opacity / 100f;
        var uiScale = _outer / RadialStyle.BaseOuter;
        for (var i = 3; i >= 1; i--) DrawCircle(_center + new Vector2(0, 5), outer + i * 4, new Color(0, 0, 0, .10f * alpha));
        DrawCircle(_center, outer, Fade(RadialStyle.Fill, body * alpha));

        var size = Mathf.Tau / count;
        for (var i = 0; i < count; i++)
        {
            var start = RadialMath.SliceCenter(i, count) - size / 2f; var end = start + size;
            var heat = i < _glow.Length ? _glow[i] : 0f; var enabled = items[i].Enabled;
            if (heat > 0f && enabled)
            {
                var sector = Sector(start, end, inner, outer);
                DrawColoredPolygon(sector, Fade(Colors.Transparent.Lerp(new Color(RadialStyle.Accent, .20f + .12f * glow), heat), alpha));
                Vector2[] edge = [.. sector, sector[0]];
                for (var halo = 3; halo >= 1; halo--) DrawPolyline(edge, new Color(RadialStyle.Accent, (.07f + .05f * glow) * heat * alpha), 2f + halo * 2.2f * glow, true);
                DrawPolyline(edge, new Color(RadialStyle.Accent, heat * alpha), 2f, true);
            }
            else if (count > 1)
            {
                var line = new Vector2(Mathf.Cos(start), Mathf.Sin(start));
                DrawLine(_center + line * inner, _center + line * outer, new Color(RadialStyle.Border, .5f * alpha), 1f, true);
            }
        }
        DrawArc(_center, outer, 0, Mathf.Tau, 96, new Color(RadialStyle.Border, .9f * alpha), 1.6f, true);

        var font = UiStyle.Medium; var fontSize = (int)Mathf.Round((compact ? 12f : 13.5f) * Math.Max(.85f, uiScale));
        var iconSize = (int)Mathf.Round((compact ? 20f : 24f) * Math.Max(.85f, uiScale)); var labelRadius = (outer + inner) / 2f + 2f;
        for (var i = 0; i < count; i++)
        {
            var item = items[i]; var lit = (i == _hover || i == _panelOwner) && item.Enabled;
            var color = !item.Enabled ? RadialStyle.Disabled : item.Destructive ? RadialStyle.Danger : item.Accent ? RadialStyle.Accent2 : lit ? RadialStyle.Accent : RadialStyle.Text;
            color = Fade(color, alpha);
            var at = RadialMath.LabelPosition(i, count, labelRadius); var middle = _center + new Vector2(at.X, at.Y);
            DrawTextureRect(UiStyle.Icon(item.Icon, color, iconSize), new Rect2(middle - new Vector2(iconSize / 2f, iconSize / 2f + fontSize * .9f), new Vector2(iconSize, iconSize)), false);
            var arc = Mathf.Tau * labelRadius / count; var width = Mathf.Clamp(arc + 18, 64, 108 * Math.Max(.85f, uiScale));
            DrawMultilineString(font, middle + new Vector2(-width / 2f, fontSize * .9f), item.Label, HorizontalAlignment.Center, width, fontSize, 2, color);
            if (item.Children is { Count: > 0 })
            {
                // a small orange arrow on the outer edge: this slice opens something
                var angle = RadialMath.SliceCenter(i, count); var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)); var side = new Vector2(-dir.Y, dir.X);
                var tip = _center + dir * (outer - 5); var baseAt = _center + dir * (outer - 13);
                DrawColoredPolygon([tip, baseAt + side * 4.5f, baseAt - side * 4.5f], Fade(item.Enabled ? RadialStyle.Accent2 : RadialStyle.Disabled, alpha));
            }
        }

        // the middle disc: what was clicked (a picture or an icon), or "Voltar" inside a circle
        var level = _levels[^1]; var back = _levels.Count > 1;
        DrawCircle(_center, inner - 3, Fade(RadialStyle.Secondary, .97f * alpha));
        var tint = Fade(RadialStyle.Text, alpha); var hot = Fade(RadialStyle.Accent, alpha);
        if (level.Image is { } image && !back) DrawDiscImage(image, inner - 8, alpha);
        else
        {
            var iconBig = (int)Mathf.Round(34 * Math.Max(.85f, uiScale));
            DrawTextureRect(UiStyle.Icon(back ? "undo" : level.Icon, _centerHover ? hot : tint, iconBig), new Rect2(_center - new Vector2(iconBig / 2f, iconBig / 2f + 14), new Vector2(iconBig, iconBig)), false);
        }
        var titleSize = (int)Mathf.Round(13 * Math.Max(.85f, uiScale));
        var title = back ? "Voltar" : level.Title; var titleWidth = (inner - 8) * 2f;
        if (level.Image is not null && !back) DrawRect(new Rect2(_center.X - titleWidth * .38f, _center.Y + inner * .45f, titleWidth * .76f, titleSize + 8), new Color(0, 0, 0, .55f * alpha));
        DrawMultilineString(UiStyle.Bold, new Vector2(_center.X - titleWidth / 2f, _center.Y + (level.Image is not null && !back ? inner * .45f + titleSize + 1 : titleSize * 1.4f)), title, HorizontalAlignment.Center, titleWidth, titleSize, 2, _centerHover ? hot : tint);
        DrawArc(_center, inner - 3, 0, Mathf.Tau, 64, _centerHover ? hot : new Color(RadialStyle.Border, .95f * alpha), _centerHover ? 2.4f : 1.6f, true);

        if (_panelOwner >= 0) DrawPanel(alpha);
    }

    private void DrawDiscImage(Texture2D image, float radius, float alpha)
    {
        const int steps = 40; var points = new Vector2[steps]; var uvs = new Vector2[steps]; var colors = new Color[steps];
        for (var i = 0; i < steps; i++)
        {
            var angle = Mathf.Tau * i / steps; var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            points[i] = _center + direction * radius; uvs[i] = new Vector2(.5f, .5f) + direction * .5f; colors[i] = new Color(1, 1, 1, alpha);
        }
        DrawPolygon(points, colors, uvs, image);
    }

    private void DrawPanel(float alpha)
    {
        var rect = PanelRect(); var rows = PanelItems;
        var style = UiStyle.Box(Fade(RadialStyle.Fill, RadialStyle.Opacity / 100f * alpha), new Color(RadialStyle.Accent, .55f * alpha), 12, 1, 0, 0);
        style.ShadowColor = new Color(0, 0, 0, .4f * alpha); style.ShadowSize = 10;
        DrawStyleBox(style, rect);
        // a thin line joins the panel to the slice that opened it
        var angle = RadialMath.SliceCenter(_panelOwner, Items.Count); var anchor = _center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (_outer + 1);
        var edgeX = rect.Position.X + (anchor.X < rect.Position.X ? 0 : rect.Size.X);
        DrawLine(anchor, new Vector2(anchor.X < rect.Position.X ? rect.Position.X : edgeX, Mathf.Clamp(anchor.Y, rect.Position.Y + 10, rect.End.Y - 10)), new Color(RadialStyle.Accent, .6f * alpha), 1.4f, true);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i]; var box = new Rect2(rect.Position.X + 6, rect.Position.Y + 8 + i * PanelRowHeight, rect.Size.X - 12, PanelRowHeight - 2);
            var lit = i == _panelHover && row.Enabled;
            if (lit) DrawStyleBox(UiStyle.Box(new Color(RadialStyle.Accent, .16f * alpha), new Color(RadialStyle.Accent, .8f * alpha), 8, 1, 0, 0), box);
            var color = !row.Enabled ? RadialStyle.Disabled : row.Destructive ? RadialStyle.Danger : lit ? RadialStyle.Accent : RadialStyle.Text; color = Fade(color, alpha);
            DrawTextureRect(UiStyle.Icon(row.Icon, color, 20), new Rect2(box.Position + new Vector2(8, (box.Size.Y - 20) / 2f), new Vector2(20, 20)), false);
            DrawString(UiStyle.Medium, box.Position + new Vector2(38, box.Size.Y / 2f + 5), row.Label, HorizontalAlignment.Left, box.Size.X - 60, 14, color);
            if (row.Children is { Count: > 0 }) DrawString(UiStyle.Bold, box.Position + new Vector2(box.Size.X - 18, box.Size.Y / 2f + 5), "›", HorizontalAlignment.Left, -1, 16, Fade(RadialStyle.Accent2, alpha));
        }
    }

    /// The outline of an annular sector (the slice) as a polygon.
    private Vector2[] Sector(float start, float end, float inner, float outer)
    {
        const int steps = 20;
        var points = new Vector2[(steps + 1) * 2];
        for (var i = 0; i <= steps; i++)
        {
            var angle = Mathf.Lerp(start, end, i / (float)steps); var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            points[i] = _center + direction * outer; points[points.Length - 1 - i] = _center + direction * inner;
        }
        return points;
    }
}
