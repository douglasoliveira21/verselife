using System.Numerics;

namespace LifeVerse.Shared.Building;

/// <summary>Where an object is: its centre, its turn and its size (the three things the editor changes).</summary>
public readonly record struct Pose(Vector3 Position, Quaternion Rotation, Vector3 Scale);

/// <summary>Whether a number typed for a move, a turn or a stretch is along the world's axes or along the axes of a reference object.</summary>
public enum AxisSpace { World, Local }

/// <summary>
/// The exact arithmetic of the editor's precise transforms: move along the axes of the world or of an object, turn around a pivot, stretch around a
/// pivot, mirror through a plane, and the copies of a multiple duplication. Pure functions of numbers (no engine), so they are tested alone and the
/// editor only converts to and from its own types.
/// </summary>
public static class TransformMath
{
    /// <summary>The axis of a reference frame (0 = X, 1 = Y, 2 = Z) as a vector of the world: the world's own when the space is World.</summary>
    public static Vector3 Axis(int axis, AxisSpace space, Quaternion frame) => Vector3.Transform(axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ }, space == AxisSpace.Local ? Quaternion.Normalize(frame) : Quaternion.Identity);

    /// <summary>A vector written in the frame's axes, as a vector of the world.</summary>
    public static Vector3 ToWorld(Vector3 inFrame, AxisSpace space, Quaternion frame) => space == AxisSpace.Local ? Vector3.Transform(inFrame, Quaternion.Normalize(frame)) : inFrame;

    /// <summary>A vector of the world, written in the frame's axes.</summary>
    public static Vector3 FromWorld(Vector3 world, AxisSpace space, Quaternion frame) => space == AxisSpace.Local ? Vector3.Transform(world, Quaternion.Inverse(Quaternion.Normalize(frame))) : world;

    /// <summary>Where the pivot of an object that has its own pivot is in the world: its centre plus the offset, turned with the object.</summary>
    public static Vector3 PivotOf(Pose pose, Vector3 localOffset) => pose.Position + Vector3.Transform(localOffset, Quaternion.Normalize(pose.Rotation));

    /// <summary>The same offset kept when the object is the one that changes (a pivot offset is stored in the object's own axes, so it never moves with a turn).</summary>
    public static Pose MoveBy(Pose pose, Vector3 delta, AxisSpace space, Quaternion frame) => pose with { Position = pose.Position + ToWorld(delta, space, frame) };

    /// <summary>Turns the object by <paramref name="degrees"/> around an axis through the pivot (the axis of the world or of the reference frame): its centre goes around the pivot and it turns the same way.</summary>
    public static Pose RotateAbout(Pose pose, Vector3 pivot, int axis, float degrees, AxisSpace space, Quaternion frame)
    {
        var turn = Quaternion.CreateFromAxisAngle(Vector3.Normalize(Axis(axis, space, frame)), degrees * MathF.PI / 180f);
        return pose with { Position = pivot + Vector3.Transform(pose.Position - pivot, turn), Rotation = Quaternion.Normalize(turn * pose.Rotation) };
    }

    /// <summary>
    /// Stretches the object by a factor per axis of the frame, around the pivot: its centre moves away from (or towards) the pivot by those factors, and its size grows
    /// along the axes it points the same way as. A turned object cannot be stretched by different factors along axes it is not aligned with (that would shear it):
    /// then the answer is false with the reason, unless the three factors are equal.
    /// </summary>
    public static bool TryScaleAbout(Pose pose, Vector3 pivot, Vector3 factors, AxisSpace space, Quaternion frame, out Pose result, out string? problem)
    {
        result = pose; problem = null;
        if (factors.X <= 0 || factors.Y <= 0 || factors.Z <= 0 || !float.IsFinite(factors.X + factors.Y + factors.Z)) { problem = "bad_factor"; return false; }
        var around = FromWorld(pose.Position - pivot, space, frame) * factors; var position = pivot + ToWorld(around, space, frame);
        var uniform = Math.Abs(factors.X - factors.Y) < 1e-6f && Math.Abs(factors.Y - factors.Z) < 1e-6f;
        if (uniform) { result = pose with { Position = position, Scale = pose.Scale * factors.X }; return true; }
        var scale = pose.Scale; var rotation = Quaternion.Normalize(pose.Rotation);
        for (var own = 0; own < 3; own++)
        {
            var direction = Vector3.Transform(own switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ }, rotation); var best = -1; var bestDot = 0f;
            for (var k = 0; k < 3; k++) { var dot = MathF.Abs(Vector3.Dot(direction, Axis(k, space, frame))); if (dot > bestDot) { bestDot = dot; best = k; } }
            if (bestDot < 0.9962f) { problem = "needs_alignment"; return false; } // within 5 degrees of an axis of the frame
            scale = Set(scale, own, Get(scale, own) * Get(factors, best));
        }
        result = pose with { Position = position, Scale = scale }; return true;
    }

    /// <summary>
    /// Mirrors the object through the plane that goes through the pivot and is perpendicular to the world axis (0 = X, 1 = Y, 2 = Z). Its centre is reflected. For a shape
    /// that looks the same when flipped along one of its own axes (a box, a cylinder, a sphere…) the mirror image is the same shape turned the mirrored way: the local axis
    /// that points through the plane is flipped, so the result is an ordinary turn with positive sizes. Shapes that are not symmetric (a twist, an imported model) need more.
    /// </summary>
    public static Pose Mirror(Pose pose, Vector3 pivot, int axis)
    {
        var normal = axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
        Vector3 Reflect(Vector3 v) => v - 2f * Vector3.Dot(v, normal) * normal;
        var rotation = Quaternion.Normalize(pose.Rotation); var columns = new[] { Vector3.Transform(Vector3.UnitX, rotation), Vector3.Transform(Vector3.UnitY, rotation), Vector3.Transform(Vector3.UnitZ, rotation) };
        var mirrored = columns.Select(Reflect).ToArray(); var through = 0; var most = -1f;
        for (var i = 0; i < 3; i++) { var d = MathF.Abs(Vector3.Dot(columns[i], normal)); if (d > most) { most = d; through = i; } }
        mirrored[through] = -mirrored[through]; // the reflection has determinant -1; flipping the axis that points through the plane makes it a proper turn
        var basis = new Matrix4x4(mirrored[0].X, mirrored[0].Y, mirrored[0].Z, 0, mirrored[1].X, mirrored[1].Y, mirrored[1].Z, 0, mirrored[2].X, mirrored[2].Y, mirrored[2].Z, 0, 0, 0, 0, 1);
        return pose with { Position = pivot + Reflect(pose.Position - pivot), Rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(basis)) };
    }

    private static float Get(Vector3 v, int i) => i switch { 0 => v.X, 1 => v.Y, _ => v.Z };
    private static Vector3 Set(Vector3 v, int i, float x) => i switch { 0 => v with { X = x }, 1 => v with { Y = x }, _ => v with { Z = x } };
}

public enum ArrayKind { Line, Grid, Circle }

/// <summary>How a multiple duplication lays its copies out.</summary>
/// <param name="CountX">Line: how many copies. Grid: how many along each axis (the original is one of them). Circle: how many objects in total, the original included.</param>
/// <param name="Step">Line: the move from one copy to the next. Grid: the distance between neighbours along each axis (in the frame's axes).</param>
/// <param name="TurnStep">Line only: the turn in degrees about each axis of the frame from one copy to the next, around the pivot.</param>
/// <param name="ScaleStep">Line only: the factor (per axis of the frame) by which each copy is bigger than the one before, around the pivot.</param>
/// <param name="CircleAxis">Circle: the axis of the frame the copies go around (0 = X, 1 = Y, 2 = Z).</param>
/// <param name="FaceCenter">Circle: the copies turn as they go round (otherwise they keep the direction of the original).</param>
public sealed record ArraySpec(ArrayKind Kind, int CountX, int CountY, int CountZ, Vector3 Step, Vector3 TurnStep, Vector3 ScaleStep, int CircleAxis = 1, bool FaceCenter = true)
{
    public const int MaxCopies = 200;

    /// <summary>How many copies it makes (not counting the original), or -1 when the numbers are not acceptable.</summary>
    public int CopyCount => Kind switch
    {
        ArrayKind.Line => CountX is >= 1 and <= MaxCopies ? CountX : -1,
        ArrayKind.Grid => CountX >= 1 && CountY >= 1 && CountZ >= 1 && (long)CountX * CountY * CountZ - 1 is >= 1 and <= MaxCopies ? CountX * CountY * CountZ - 1 : -1,
        _ => CountX is >= 2 and <= MaxCopies + 1 ? CountX - 1 : -1,
    };
}

/// <summary>The copies of a multiple duplication: for each one, how to change a pose of the selection to get its place.</summary>
public static class ArrayMath
{
    /// <summary>The transform of every copy, in order (the original is not one of them). Empty when the numbers are not acceptable.</summary>
    public static IReadOnlyList<Func<Pose, Pose>> Copies(ArraySpec spec, Vector3 pivot, AxisSpace space, Quaternion frame)
    {
        var count = spec.CopyCount; if (count < 0) return [];
        var list = new List<Func<Pose, Pose>>(count);
        switch (spec.Kind)
        {
            case ArrayKind.Line:
                for (var k = 1; k <= count; k++)
                {
                    var n = k;
                    list.Add(pose =>
                    {
                        var moved = TransformMath.MoveBy(pose, spec.Step * n, space, frame);
                        for (var axis = 0; axis < 3; axis++) { var turn = Component(spec.TurnStep, axis) * n; if (Math.Abs(turn) > 1e-6f) moved = TransformMath.RotateAbout(moved, pivot, axis, turn, space, frame); }
                        var factors = new Vector3(MathF.Pow(spec.ScaleStep.X, n), MathF.Pow(spec.ScaleStep.Y, n), MathF.Pow(spec.ScaleStep.Z, n));
                        return factors == Vector3.One || !TransformMath.TryScaleAbout(moved, pivot, factors, space, frame, out var grown, out _) ? moved : grown;
                    });
                }
                break;
            case ArrayKind.Grid:
                for (var i = 0; i < spec.CountX; i++) for (var j = 0; j < spec.CountY; j++) for (var k = 0; k < spec.CountZ; k++)
                {
                    if (i == 0 && j == 0 && k == 0) continue;
                    var offset = new Vector3(spec.Step.X * i, spec.Step.Y * j, spec.Step.Z * k);
                    list.Add(pose => TransformMath.MoveBy(pose, offset, space, frame));
                }
                break;
            default:
                var each = 360f / spec.CountX;
                for (var k = 1; k <= count; k++)
                {
                    var angle = each * k;
                    list.Add(pose => spec.FaceCenter ? TransformMath.RotateAbout(pose, pivot, spec.CircleAxis, angle, space, frame) : TransformMath.RotateAbout(pose, pivot, spec.CircleAxis, angle, space, frame) with { Rotation = pose.Rotation });
                }
                break;
        }
        return list;
    }

    private static float Component(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };
}
