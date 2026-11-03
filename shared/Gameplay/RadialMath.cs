using System.Numerics;

namespace LifeVerse.Shared.Gameplay;

/// <summary>
/// The geometry of the radial (pie) menu: which slice the cursor is over, where each slice's label goes, how to keep the circle on screen
/// and how to split a long list into several circles of at most eight. Slice 0 is centred at the top and the slices run clockwise.
/// </summary>
public static class RadialMath
{
    public const int MaxSlices = 8;

    /// <summary>The angle (radians, 0 = right, positive = clockwise on screen) of the middle of a slice.</summary>
    public static float SliceCenter(int index, int count) => -MathF.PI / 2f + index * 2f * MathF.PI / Math.Max(1, count);

    /// <summary>The slice under an offset from the circle's centre, or -1 inside the middle disc or outside the ring.</summary>
    public static int SliceAt(Vector2 offset, int count, float innerRadius, float outerRadius)
    {
        if (count <= 0) return -1;
        var distance = offset.Length();
        if (distance < innerRadius || distance > outerRadius) return -1;
        var size = 2f * MathF.PI / count;
        var angle = MathF.Atan2(offset.Y, offset.X) + MathF.PI / 2f + size / 2f; // 0 at the left edge of slice 0
        angle %= 2f * MathF.PI; if (angle < 0) angle += 2f * MathF.PI;
        return Math.Min(count - 1, (int)(angle / size));
    }

    /// <summary>The point where a slice's icon and text are drawn.</summary>
    public static Vector2 LabelPosition(int index, int count, float radius)
    {
        var angle = SliceCenter(index, count);
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    /// <summary>The centre closest to the wanted one at which the whole circle (plus a margin) stays inside the screen.</summary>
    public static Vector2 KeepOnScreen(Vector2 wanted, float radius, Vector2 screen, float margin = 8f)
    {
        var limit = radius + margin;
        return new Vector2(Math.Clamp(wanted.X, limit, Math.Max(limit, screen.X - limit)), Math.Clamp(wanted.Y, limit, Math.Max(limit, screen.Y - limit)));
    }

    /// <summary>
    /// Splits entries into circles of at most <see cref="MaxSlices"/>. When there are more, the first circle keeps seven and its eighth
    /// slice opens the next circle, and so on, so nothing is ever left out.
    /// </summary>
    public static List<List<T>> Pages<T>(IReadOnlyList<T> items)
    {
        var pages = new List<List<T>>();
        var rest = items.ToList();
        while (rest.Count > MaxSlices) { pages.Add(rest.Take(MaxSlices - 1).ToList()); rest = rest.Skip(MaxSlices - 1).ToList(); }
        pages.Add(rest);
        return pages;
    }

    /// <summary>Which slice a number key (1 to 8) picks, or -1.</summary>
    public static int SliceForKey(int number, int count) => number >= 1 && number <= count ? number - 1 : -1;

    /// <summary>The next slice in a direction (+1 clockwise, -1 anticlockwise), wrapping round; starts at the top when none is chosen.</summary>
    public static int Step(int current, int direction, int count) => count <= 0 ? -1 : current < 0 ? (direction >= 0 ? 0 : count - 1) : ((current + direction) % count + count) % count;
}
