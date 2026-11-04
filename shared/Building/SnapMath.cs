using System.Numerics;

namespace LifeVerse.Shared.Building;

public enum SnapKind { Vertex, Edge, Face, Contact }

/// <summary>A point objects can be snapped by: a corner, the middle of an edge or the centre of a face of an object's bounding box (or a touching face).</summary>
public readonly record struct SnapPoint(Vector3 Position, SnapKind Kind);

/// <summary>Which kinds of snap points are in use, and how close counts as "close".</summary>
public sealed record SnapSettings(bool Vertices = true, bool Edges = true, bool Faces = true, bool Contact = true, float Radius = 0.3f);

/// <summary>What a snap decided: the amount to move instead, what was snapped, and the point it was snapped to (to show a marker).</summary>
public readonly record struct SnapResult(float Amount, SnapKind Kind, Vector3 Target);

/// <summary>
/// Magnetic snap: while an object is dragged along an axis, it is pulled to the corners, edge middles and face centres of other objects that lie close to its path,
/// or its face is made to touch a face of another. Works on oriented bounding boxes, so what snaps are the corners of an object's box (for a cylinder, a sphere or a
/// model that box can be a little away from the visible shape). Pure arithmetic, tested alone.
/// </summary>
public static class SnapMath
{
    /// <summary>The corners (8), edge middles (12) and face centres (6) of an oriented box, chosen by the settings.</summary>
    public static IReadOnlyList<SnapPoint> Features(Vector3 center, Quaternion rotation, Vector3 halfExtents, SnapSettings settings)
    {
        var points = new List<SnapPoint>(26); var turn = Quaternion.Normalize(rotation);
        for (var x = -1; x <= 1; x++) for (var y = -1; y <= 1; y++) for (var z = -1; z <= 1; z++)
        {
            var zeros = (x == 0 ? 1 : 0) + (y == 0 ? 1 : 0) + (z == 0 ? 1 : 0);
            if (zeros == 3) continue;
            var kind = zeros == 0 ? SnapKind.Vertex : zeros == 1 ? SnapKind.Edge : SnapKind.Face;
            if ((kind == SnapKind.Vertex && !settings.Vertices) || (kind == SnapKind.Edge && !settings.Edges) || (kind == SnapKind.Face && !settings.Faces)) continue;
            points.Add(new SnapPoint(center + Vector3.Transform(new Vector3(x * halfExtents.X, y * halfExtents.Y, z * halfExtents.Z), turn), kind));
        }
        return points;
    }

    /// <summary>
    /// The amount along <paramref name="axis"/> (a unit vector) that makes some point of the dragged objects land on some point of the targets, if one lies within the radius
    /// of the line it travels along and its position along the line is close to the amount asked. The nearest such point wins. Null when nothing is close.
    /// </summary>
    public static SnapResult? AlongAxis(IReadOnlyList<SnapPoint> dragged, IReadOnlyList<SnapPoint> targets, Vector3 axis, float amount, float radius)
    {
        SnapResult? best = null; var bestDistance = float.MaxValue; var direction = Vector3.Normalize(axis);
        foreach (var from in dragged)
            foreach (var to in targets)
            {
                var along = Vector3.Dot(to.Position - from.Position, direction); var across = (to.Position - from.Position) - along * direction;
                if (across.Length() > radius) continue;
                var gap = MathF.Abs(along - amount); if (gap > radius) continue;
                var rank = gap + across.Length() * .5f + (to.Kind == SnapKind.Vertex ? 0f : to.Kind == SnapKind.Edge ? radius * .02f : radius * .04f); // corners beat edge middles beat face centres when equally close
                if (rank < bestDistance) { bestDistance = rank; best = new SnapResult(along, to.Kind, to.Position); }
            }
        return best;
    }

    /// <summary>
    /// Makes a face of the dragged group touch a face of another box when they are moving towards each other along an axis: the two boxes (axis-aligned, as
    /// <see cref="Bounds"/>) end up sharing a plane, side by side or flush. Only boxes that overlap in the other two axes count, so things beside the path are ignored.
    /// </summary>
    public static SnapResult? Contact(Bounds dragged, IReadOnlyList<Bounds> targets, int axis, float amount, float radius)
    {
        SnapResult? best = null; var bestGap = float.MaxValue;
        float Min(Bounds b) => axis == 0 ? b.Min.X : axis == 1 ? b.Min.Y : b.Min.Z; float Max(Bounds b) => axis == 0 ? b.Max.X : axis == 1 ? b.Max.Y : b.Max.Z;
        bool Overlaps(Bounds a, Bounds b, int other) => (other == 0 ? a.Min.X < b.Max.X && a.Max.X > b.Min.X : other == 1 ? a.Min.Y < b.Max.Y && a.Max.Y > b.Min.Y : a.Min.Z < b.Max.Z && a.Max.Z > b.Min.Z);
        var shifted = new Bounds(dragged.Min + AxisVector(axis) * amount, dragged.Max + AxisVector(axis) * amount);
        foreach (var target in targets)
        {
            if (!Overlaps(shifted, target, (axis + 1) % 3) || !Overlaps(shifted, target, (axis + 2) % 3)) continue;
            foreach (var wanted in new[] { Min(target) - Max(dragged), Max(target) - Min(dragged), Min(target) - Min(dragged), Max(target) - Max(dragged) }) // touching from either side, or flush
            {
                var gap = MathF.Abs(wanted - amount); if (gap > radius || gap >= bestGap) continue;
                bestGap = gap; var at = shifted.Center + AxisVector(axis) * (wanted - amount); best = new SnapResult(wanted, SnapKind.Contact, at);
            }
        }
        return best;
    }

    private static Vector3 AxisVector(int axis) => axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
}

/// <summary>A rectangle on the screen, in pixels.</summary>
public readonly record struct ScreenRect(float Left, float Top, float Right, float Bottom)
{
    public static ScreenRect Between(Vector2 a, Vector2 b) => new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));
    public bool Contains(ScreenRect other) => other.Left >= Left && other.Right <= Right && other.Top >= Top && other.Bottom <= Bottom;
    public bool Touches(ScreenRect other) => other.Left <= Right && other.Right >= Left && other.Top <= Bottom && other.Bottom >= Top;
    public float Width => Right - Left;
    public float Height => Bottom - Top;
}

/// <summary>Choosing objects by the area they are in: a rectangle drawn on the screen, or a box in the world.</summary>
public static class AreaSelect
{
    /// <summary>
    /// The objects whose on-screen rectangle is inside the drawn one (<paramref name="crossing"/> false) or at least touches it (true). An object with no rectangle (null:
    /// behind the camera, or partly behind it) is never chosen.
    /// </summary>
    public static IReadOnlyList<int> InRectangle(IReadOnlyList<ScreenRect?> objects, ScreenRect area, bool crossing)
    {
        var chosen = new List<int>();
        for (var i = 0; i < objects.Count; i++) if (objects[i] is { } box && (crossing ? area.Touches(box) : area.Contains(box))) chosen.Add(i);
        return chosen;
    }

    /// <summary>The screen rectangle that holds the projected corners, or null when any corner could not be projected (behind the camera).</summary>
    public static ScreenRect? RectangleOf(IReadOnlyList<Vector2?> corners)
    {
        if (corners.Count == 0 || corners.Any(c => c is null)) return null;
        return new ScreenRect(corners.Min(c => c!.Value.X), corners.Min(c => c!.Value.Y), corners.Max(c => c!.Value.X), corners.Max(c => c!.Value.Y));
    }

    /// <summary>The objects whose box is inside the world box (<paramref name="touching"/> false) or at least touches it (true).</summary>
    public static IReadOnlyList<int> InVolume(IReadOnlyList<Bounds> objects, Bounds volume, bool touching)
    {
        var chosen = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            var b = objects[i];
            var inside = b.Min.X >= volume.Min.X && b.Max.X <= volume.Max.X && b.Min.Y >= volume.Min.Y && b.Max.Y <= volume.Max.Y && b.Min.Z >= volume.Min.Z && b.Max.Z <= volume.Max.Z;
            var touch = b.Min.X <= volume.Max.X && b.Max.X >= volume.Min.X && b.Min.Y <= volume.Max.Y && b.Max.Y >= volume.Min.Y && b.Min.Z <= volume.Max.Z && b.Max.Z >= volume.Min.Z;
            if (touching ? touch : inside) chosen.Add(i);
        }
        return chosen;
    }

    /// <summary>A box from two corners in any order.</summary>
    public static Bounds VolumeBetween(Vector3 a, Vector3 b) => new(Vector3.Min(a, b), Vector3.Max(a, b));
}
