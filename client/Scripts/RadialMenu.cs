using Godot;
using LifeVerse.Shared.Gameplay;
using NVector2 = System.Numerics.Vector2;

namespace LifeVerse.Client.Scripts;

/// One slice of a radial menu: an icon and a name, and either an action or a smaller circle of further choices.
public sealed class RadialItem
{
    public required string Label { get; init; }
    public string Icon { get; init; } = "dots";
    public string? Tooltip { get; init; }
    public Action? Run { get; init; }
    public List<RadialItem>? Children { get; init; }
    public bool Enabled { get; init; } = true;
    /// The orange look of the design (for closing and for things that remove).
    public bool Accent { get; init; }
}

/// The right-click menu: a dark glass circle cut into slices around the cursor (client/Assets/ui, like the pie menu of the old virtual
/// worlds). The middle disc names what was clicked and, inside a smaller circle, takes you back. A hovered slice lights up in neon; click
/// runs it or opens its circle; Esc, a click outside or a right click closes it. Numbers 1 to 8, the arrows, Enter and Backspace work too.
/// A list longer than eight is split over several circles linked by a "Mais" slice, so nothing is left out.
public partial class RadialMenu : Control
{
    private const float OuterRadius = 204, InnerRadius = 90, LabelRadius = 150, IconSize = 38, OpenSeconds = .14f;

    private sealed record Level(string Title, string Icon, List<RadialItem> Items);
    private readonly List<Level> _levels = [];
    private Vector2 _center;
    private int _hover = -1;
    private bool _centerHover;
    private float _anim = 1f;
    private Tween? _tween;

    public bool IsOpen => Visible;
    public int Depth => _levels.Count;
    public string Title => _levels.Count > 0 ? _levels[^1].Title : string.Empty;
    public IReadOnlyList<string> Labels => _levels.Count > 0 ? _levels[^1].Items.Select(x => x.Label).ToList() : [];
    public Vector2 CenterForTesting => _center;
    public event Action? Closed;

    public RadialMenu()
    {
        Name = "RadialMenu"; Visible = false; MouseFilter = MouseFilterEnum.Stop; FocusMode = FocusModeEnum.All;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    // ---- opening, going deeper and back ---------------------------------------------------------------------

    /// Opens the circle around a screen position (moved inwards if it would run off the screen).
    public void Open(Vector2 screenPosition, string title, string icon, IReadOnlyList<RadialItem> items)
    {
        var size = GetViewportRect().Size;
        var kept = RadialMath.KeepOnScreen(new NVector2(screenPosition.X, screenPosition.Y), OuterRadius + 6, new NVector2(size.X, size.Y));
        _center = new Vector2(kept.X, kept.Y);
        _levels.Clear(); _hover = -1; _centerHover = false;
        _levels.Add(new Level(title, icon, Chain(items.ToList())));
        Visible = true; GrabFocus(); Animate(.82f);
        QueueRedraw();
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false; _levels.Clear(); _tween?.Kill(); ReleaseFocus();
        Closed?.Invoke();
    }

    /// More than eight entries become a chain of circles: seven slices and a "Mais" slice that opens the rest.
    private static List<RadialItem> Chain(List<RadialItem> items)
    {
        if (items.Count <= RadialMath.MaxSlices) return items;
        var first = items.Take(RadialMath.MaxSlices - 1).ToList();
        first.Add(new RadialItem { Label = "Mais", Icon = "dots", Children = Chain(items.Skip(RadialMath.MaxSlices - 1).ToList()) });
        return first;
    }

    private void Push(RadialItem item)
    {
        _levels.Add(new Level(item.Label, item.Icon, item.Children!)); _hover = -1; _centerHover = false;
        Animate(.7f); QueueRedraw();
    }

    public void Back()
    {
        if (_levels.Count <= 1) { Close(); return; }
        _levels.RemoveAt(_levels.Count - 1); _hover = -1; Animate(.7f); QueueRedraw();
    }

    private void Animate(float from)
    {
        _tween?.Kill(); _anim = from;
        _tween = CreateTween(); _tween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        _tween.TweenMethod(Callable.From<float>(value => { _anim = value; QueueRedraw(); }), from, 1f, OpenSeconds);
    }

    // ---- choosing --------------------------------------------------------------------------------------------

    private List<RadialItem> Items => _levels[^1].Items;

    /// Runs (or opens) a slice. Returns false for a slice that cannot be used.
    public bool Pick(int index)
    {
        if (!Visible || index < 0 || index >= Items.Count || !Items[index].Enabled) return false;
        var item = Items[index];
        if (item.Children is { Count: > 0 }) { Push(item); return true; }
        Close();
        item.Run?.Invoke();
        return true;
    }

    public void PickForTesting(int index) => Pick(index);
    public void BackForTesting() => Back();
    public void HoverForTesting(Vector2 offset) => UpdateHover(_center + offset);
    public int HoverIndexForTesting => _hover;

    private void UpdateHover(Vector2 position)
    {
        if (!Visible || _levels.Count == 0) return;
        var offset = position - _center;
        _centerHover = offset.Length() < InnerRadius - 8;
        _hover = RadialMath.SliceAt(new NVector2(offset.X, offset.Y), Items.Count, InnerRadius, OuterRadius + 4);
        TooltipText = _hover >= 0 ? Items[_hover].Tooltip ?? string.Empty : string.Empty;
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
                    if (distance > OuterRadius + 4) Close();
                    else if (distance < InnerRadius - 8) Back();
                    else Pick(_hover);
                }
                else if (click.ButtonIndex == MouseButton.Right) Close();
                AcceptEvent();
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                switch (key.Keycode)
                {
                    case Key.Escape: Close(); break;
                    case Key.Backspace: Back(); break;
                    case Key.Enter or Key.KpEnter or Key.Space: Pick(_hover); break;
                    case Key.Left or Key.Up: _hover = RadialMath.Step(_hover, -1, Items.Count); QueueRedraw(); break;
                    case Key.Right or Key.Down: _hover = RadialMath.Step(_hover, 1, Items.Count); QueueRedraw(); break;
                    default:
                        var number = key.Keycode is >= Key.Key1 and <= Key.Key8 ? (int)(key.Keycode - Key.Key0) : key.Keycode is >= Key.Kp1 and <= Key.Kp8 ? (int)(key.Keycode - Key.Kp0) : 0;
                        if (number > 0) Pick(RadialMath.SliceForKey(number, Items.Count));
                        break;
                }
                AcceptEvent();
                break;
        }
    }

    // ---- drawing -----------------------------------------------------------------------------------------------

    public override void _Draw()
    {
        if (!Visible || _levels.Count == 0) return;
        var items = Items; var count = items.Count;
        var scale = .9f + .1f * _anim; var alpha = Mathf.Clamp(_anim * 1.25f, 0f, 1f);
        var outer = OuterRadius * scale; var inner = InnerRadius * scale;
        // a soft shadow behind the circle
        for (var i = 3; i >= 1; i--) DrawCircle(_center + new Vector2(0, 6), outer + i * 5, new Color(0, 0, 0, .10f * alpha));
        var fill = new Color(UiStyle.PanelFillSolid, .88f * alpha);
        DrawCircle(_center, outer, fill);

        var size = Mathf.Tau / count;
        for (var i = 0; i < count; i++)
        {
            var start = RadialMath.SliceCenter(i, count) - size / 2f; var end = start + size;
            var lit = i == _hover && items[i].Enabled;
            if (lit)
            {
                var sector = Sector(start, end, inner, outer);
                DrawColoredPolygon(sector, new Color(UiStyle.AccentFill, .95f * alpha));
                DrawPolyline([.. sector, sector[0]], new Color(UiStyle.Accent, alpha), 2.2f, true);
            }
            if (count > 1)
            {
                var edge = new Vector2(Mathf.Cos(start), Mathf.Sin(start));
                if (!lit && !(i > 0 && _hover == i - 1) || count == 2) DrawLine(_center + edge * inner, _center + edge * outer, new Color(UiStyle.BorderSoft, .55f * alpha), 1.4f, true);
            }
        }
        DrawArc(_center, outer, 0, Mathf.Tau, 96, new Color(UiStyle.BorderBright, .85f * alpha), 2.4f, true);

        // slice contents
        var font = UiStyle.Medium;
        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            var lit = i == _hover && item.Enabled;
            var color = !item.Enabled ? new Color(UiStyle.TextDisabled, alpha) : item.Accent ? new Color(UiStyle.Orange, alpha) : lit ? new Color(UiStyle.AccentText, alpha) : new Color(1, 1, 1, alpha);
            var at = RadialMath.LabelPosition(i, count, LabelRadius * scale); var middle = _center + new Vector2(at.X, at.Y);
            DrawTextureRect(UiStyle.Icon(item.Icon, color, (int)IconSize), new Rect2(middle - new Vector2(IconSize / 2, IconSize / 2 + 16), new Vector2(IconSize, IconSize)), false);
            DrawMultilineString(font, middle + new Vector2(-60, 24), item.Label, HorizontalAlignment.Center, 120, 16, 2, color);
        }

        // the middle disc: what was clicked, or "Voltar" inside a circle
        DrawCircle(_center, inner - 4, new Color(UiStyle.TitleFill, .96f * alpha));
        DrawArc(_center, inner - 4, 0, Mathf.Tau, 64, _centerHover ? new Color(UiStyle.Accent, alpha) : new Color(UiStyle.Border, .9f * alpha), _centerHover ? 3f : 2f, true);
        var level = _levels[^1];
        var back = _levels.Count > 1;
        var tint = new Color(1, 1, 1, alpha);
        DrawTextureRect(UiStyle.Icon(back ? "undo" : level.Icon, _centerHover ? new Color(UiStyle.AccentText, alpha) : tint, 40), new Rect2(_center - new Vector2(20, 54), new Vector2(40, 40)), false);
        DrawMultilineString(UiStyle.Bold, _center + new Vector2(-(inner - 12), -2), back ? "Voltar" : level.Title, HorizontalAlignment.Center, (inner - 12) * 2, 17, 3, _centerHover ? new Color(UiStyle.AccentText, alpha) : tint);
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
