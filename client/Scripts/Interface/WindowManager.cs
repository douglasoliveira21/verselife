using Godot;

namespace LifeVerse.Client.Scripts.Interface;

/// <summary>
/// Keeps track of every <see cref="FloatingWindow"/>: remembers where the person put each one and how big they made it (per account), keeps them reachable when the screen or the interface scale changes, and restores the default layout on request.
/// Windows register themselves when they enter the tree, so all of them are covered without each one asking.
/// </summary>
public static class WindowManager
{
    /// <summary>Space that must stay free at the top (the menu and location bars) and the margin kept at the other edges.</summary>
    public const float TopLimit = 72f, EdgeMargin = 0f;

    private sealed record Defaults(float AL, float AT, float AR, float AB, float OL, float OT, float OR, float OB, Control.GrowDirection GH, Control.GrowDirection GV);

    private static readonly List<FloatingWindow> Windows = [];
    private static readonly Dictionary<FloatingWindow, Defaults> Original = [];
    public static InterfaceSettings? Settings { get; set; }
    public static int Count { get { Prune(); return Windows.Count; } }

    private static void Prune() => Windows.RemoveAll(w => !GodotObject.IsInstanceValid(w));

    public static string IdOf(string name)
    {
        var safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        return safe.Length == 0 ? "janela" : safe;
    }

    /// <summary>Called by the window once it is in the tree (after its creator has placed it).</summary>
    public static void Register(FloatingWindow window)
    {
        if (!GodotObject.IsInstanceValid(window) || Windows.Contains(window) || window.GetParent() is Container) return;
        Prune(); Windows.Add(window);
        Original[window] = Snapshot(window);
        if (Settings is { } opacity) window.SetBaseOpacity(opacity.WindowOpacity);
        if (Settings is { } s && s.Windows.TryGetValue(window.LayoutId, out var saved)) Apply(window, saved);
        window.TreeExited += () => { Windows.Remove(window); Original.Remove(window); };
    }

    private static Defaults Snapshot(Control c) => new(c.AnchorLeft, c.AnchorTop, c.AnchorRight, c.AnchorBottom, c.OffsetLeft, c.OffsetTop, c.OffsetRight, c.OffsetBottom, c.GrowHorizontal, c.GrowVertical);

    /// <summary>Puts a window at this position and size (top-left anchored), then keeps it on the screen.</summary>
    public static void Apply(FloatingWindow window, Rect2 rect)
    {
        window.AnchorLeft = window.AnchorRight = 0; window.AnchorTop = window.AnchorBottom = 0;
        window.GrowHorizontal = Control.GrowDirection.End; window.GrowVertical = Control.GrowDirection.End;
        window.OffsetLeft = rect.Position.X; window.OffsetTop = rect.Position.Y; window.OffsetRight = rect.End.X; window.OffsetBottom = rect.End.Y;
        Keep(window);
    }

    /// <summary>The window's own header must always be reachable: it is brought inside the viewport (and shrunk if the viewport is smaller than it).</summary>
    public static void Keep(FloatingWindow window)
    {
        var view = window.GetViewportRect().Size; if (view.X < 100 || view.Y < 100) return;
        var min = window.GetCombinedMinimumSize();
        var size = new Vector2(Math.Min(Math.Max(window.Size.X, min.X), Math.Max(min.X, view.X - 2 * EdgeMargin)), Math.Min(Math.Max(window.Size.Y, min.Y), Math.Max(min.Y, view.Y - TopLimit - EdgeMargin)));
        var pos = new Vector2(Mathf.Clamp(window.Position.X, EdgeMargin, Math.Max(EdgeMargin, view.X - size.X - EdgeMargin)), Mathf.Clamp(window.Position.Y, TopLimit, Math.Max(TopLimit, view.Y - size.Y - EdgeMargin)));
        if (size != window.Size) window.Size = size;
        if (pos != window.Position) window.Position = pos;
    }

    /// <summary>Changes the opacity of every window that has no opacity of its own, and saves it.</summary>
    public static void SetWindowOpacity(float alpha)
    {
        alpha = Math.Clamp(alpha, .3f, 1f);
        Prune(); foreach (var w in Windows) w.SetBaseOpacity(alpha);
        if (Settings is { } s) { s.WindowOpacity = alpha; s.Save(); }
    }

    /// <summary>After the screen size or the scale changes: nothing may be left outside.</summary>
    public static void KeepAll() { Prune(); foreach (var w in Windows.ToArray()) if (w.Size.X > 0) Keep(w); }

    /// <summary>Remembers where this window is now (called when a drag or a resize ends).</summary>
    public static void Remember(FloatingWindow window)
    {
        if (Settings is not { } s || !Windows.Contains(window)) return;
        var size = window.Minimized && s.Windows.TryGetValue(window.LayoutId, out var before) ? before.Size : window.Size;
        if (!s.Windows.ContainsKey(window.LayoutId) && s.Windows.Count >= InterfaceSettings.MaxSavedWindows) return;
        s.Windows[window.LayoutId] = new Rect2(window.Position, size);
        s.Save();
    }

    public static bool HasSaved(FloatingWindow window) => Settings?.Windows.ContainsKey(window.LayoutId) ?? false;

    /// <summary>"Restaurar layout das janelas": forgets what was saved and puts every window back where the game placed it.</summary>
    public static void ResetLayout()
    {
        Prune();
        Settings?.Windows.Clear(); Settings?.Save();
        foreach (var (w, d) in Original.ToArray())
        {
            if (!GodotObject.IsInstanceValid(w)) continue;
            w.AnchorLeft = d.AL; w.AnchorTop = d.AT; w.AnchorRight = d.AR; w.AnchorBottom = d.AB;
            w.OffsetLeft = d.OL; w.OffsetTop = d.OT; w.OffsetRight = d.OR; w.OffsetBottom = d.OB; w.GrowHorizontal = d.GH; w.GrowVertical = d.GV;
            Keep(w);
        }
    }
}
