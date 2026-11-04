using System.Numerics;

namespace LifeVerse.Shared.Building;

public enum AlignAxis { X = 0, Y = 1, Z = 2 }
public enum AlignEdge { Min, Center, Max }

/// <summary>An axis-aligned box in world space: the extent of an object as the align tools see it.</summary>
public readonly record struct Bounds(Vector3 Min, Vector3 Max)
{
    public Vector3 Center => (Min + Max) / 2f;
}

/// <summary>
/// The arithmetic of the align, distribute and snap tools. Each method returns how far every object must move along one
/// axis (never a new position), so the editor can send it as an ordinary revisioned move.
/// </summary>
public static class AlignMath
{
    private static float Component(Vector3 value, AlignAxis axis) => axis switch { AlignAxis.X => value.X, AlignAxis.Y => value.Y, _ => value.Z };

    private static float EdgeOf(Bounds box, AlignAxis axis, AlignEdge edge) => edge switch
    {
        AlignEdge.Min => Component(box.Min, axis), AlignEdge.Max => Component(box.Max, axis), _ => Component(box.Center, axis)
    };

    /// <summary>Moves every box so the chosen edge (low side, middle or high side) lines up with the same edge of the whole selection.</summary>
    public static float[] Align(IReadOnlyList<Bounds> boxes, AlignAxis axis, AlignEdge edge)
    {
        if (boxes.Count == 0) return [];
        var target = edge switch
        {
            AlignEdge.Min => boxes.Min(x => Component(x.Min, axis)),
            AlignEdge.Max => boxes.Max(x => Component(x.Max, axis)),
            _ => (boxes.Min(x => Component(x.Min, axis)) + boxes.Max(x => Component(x.Max, axis))) / 2f
        };
        return boxes.Select(x => target - EdgeOf(x, axis, edge)).ToArray();
    }

    /// <summary>
    /// Spreads the objects so the gaps between neighbours are equal along an axis. The two outermost objects stay where they are.
    /// The result is in the same order as the input.
    /// </summary>
    public static float[] Distribute(IReadOnlyList<Bounds> boxes, AlignAxis axis)
    {
        var offsets = new float[boxes.Count];
        if (boxes.Count < 3) return offsets;
        var order = Enumerable.Range(0, boxes.Count).OrderBy(i => Component(boxes[i].Center, axis)).ToArray();
        var first = boxes[order[0]]; var last = boxes[order[^1]];
        var inner = order.Skip(1).Take(order.Length - 2).ToArray();
        var span = Component(last.Min, axis) - Component(first.Max, axis);
        var used = inner.Sum(i => Component(boxes[i].Max, axis) - Component(boxes[i].Min, axis));
        var gap = (span - used) / (boxes.Count - 1);
        var cursor = Component(first.Max, axis) + gap;
        foreach (var i in inner)
        {
            offsets[i] = cursor - Component(boxes[i].Min, axis);
            cursor += Component(boxes[i].Max, axis) - Component(boxes[i].Min, axis) + gap;
        }
        return offsets;
    }

    /// <summary>How far to move the whole selection down (or up) so its lowest point rests on the ground at <paramref name="groundHeight"/>.</summary>
    public static float RestOnGround(IReadOnlyList<Bounds> boxes, float groundHeight) => boxes.Count == 0 ? 0f : groundHeight - boxes.Min(x => x.Min.Y);

    /// <summary>The nearest multiple of the grid size; a size of 0 means no grid.</summary>
    public static float SnapToGrid(float value, float size) => size <= 0f ? value : MathF.Round(value / size, MidpointRounding.AwayFromZero) * size;

    // ---- advanced alignment and distribution ----------------------------------------------------------------------

    /// <summary>Moves every box so the chosen edge lines up with the same edge of one of them (the reference, which stays where it is).</summary>
    public static float[] AlignTo(IReadOnlyList<Bounds> boxes, int reference, AlignAxis axis, AlignEdge edge)
    {
        if (boxes.Count == 0) return [];
        if (reference < 0 || reference >= boxes.Count) return new float[boxes.Count];
        var target = EdgeOf(boxes[reference], axis, edge);
        return boxes.Select(x => target - EdgeOf(x, axis, edge)).ToArray();
    }

    /// <summary>Which box to align the others to: the first, the last, the biggest or the smallest along the axis, or (-1) none, meaning the whole selection.</summary>
    public static int PickReference(IReadOnlyList<Bounds> boxes, AlignAxis axis, string which) => which switch
    {
        "first" => boxes.Count > 0 ? 0 : -1,
        "last" => boxes.Count - 1,
        "largest" => boxes.Count == 0 ? -1 : Enumerable.Range(0, boxes.Count).OrderByDescending(i => Component(boxes[i].Max - boxes[i].Min, axis)).ThenBy(i => i).First(),
        "smallest" => boxes.Count == 0 ? -1 : Enumerable.Range(0, boxes.Count).OrderBy(i => Component(boxes[i].Max - boxes[i].Min, axis)).ThenBy(i => i).First(),
        _ => -1,
    };

    /// <summary>
    /// Spreads the objects so their centres are equally far apart along an axis (the outermost two stay where they are), whatever their sizes.
    /// </summary>
    public static float[] DistributeCenters(IReadOnlyList<Bounds> boxes, AlignAxis axis)
    {
        var offsets = new float[boxes.Count];
        if (boxes.Count < 3) return offsets;
        var order = Enumerable.Range(0, boxes.Count).OrderBy(i => Component(boxes[i].Center, axis)).ThenBy(i => i).ToArray();
        var first = Component(boxes[order[0]].Center, axis); var last = Component(boxes[order[^1]].Center, axis); var step = (last - first) / (boxes.Count - 1);
        for (var k = 1; k < order.Length - 1; k++) offsets[order[k]] = first + step * k - Component(boxes[order[k]].Center, axis);
        return offsets;
    }

    /// <summary>
    /// Lines the objects up along an axis with the same <paramref name="gap"/> (metres, may be negative to overlap) between the edge of one and the start of the next,
    /// in the order they already have along that axis. The first one stays where it is.
    /// </summary>
    public static float[] DistributeWithGap(IReadOnlyList<Bounds> boxes, AlignAxis axis, float gap)
    {
        var offsets = new float[boxes.Count];
        if (boxes.Count < 2) return offsets;
        var order = Enumerable.Range(0, boxes.Count).OrderBy(i => Component(boxes[i].Center, axis)).ThenBy(i => i).ToArray();
        var cursor = Component(boxes[order[0]].Max, axis) + gap;
        for (var k = 1; k < order.Length; k++)
        {
            var box = boxes[order[k]]; offsets[order[k]] = cursor - Component(box.Min, axis); cursor += Component(box.Max, axis) - Component(box.Min, axis) + gap;
        }
        return offsets;
    }
}
