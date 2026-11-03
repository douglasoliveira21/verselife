using System.Numerics;

namespace LifeVerse.Shared.Building;

/// <summary>
/// Geometry behind the move / rotate / scale handles, kept free of the engine so it can be tested:
/// where a mouse ray lands on an axis or a rotation plane, and how raw drags snap.
/// </summary>
public static class GizmoMath
{
    /// <summary>
    /// Parameter t of the point <c>origin + axis * t</c> on the axis line that is closest to the mouse ray.
    /// Dragging a handle changes t, and the difference between two t values is how far it moved along the axis.
    /// </summary>
    public static float AxisParameter(Vector3 origin, Vector3 axis, Vector3 rayOrigin, Vector3 rayDirection)
    {
        axis = Vector3.Normalize(axis); rayDirection = Vector3.Normalize(rayDirection);
        var offset = origin - rayOrigin;
        var b = Vector3.Dot(axis, rayDirection);
        var denominator = 1f - b * b;
        if (MathF.Abs(denominator) < 1e-4f) return 0f; // the ray runs along the axis: the drag is undefined
        var d = Vector3.Dot(axis, offset); var e = Vector3.Dot(rayDirection, offset);
        return (b * e - d) / denominator;
    }

    /// <summary>Where the mouse ray meets the plane through <paramref name="center"/> with normal <paramref name="normal"/>.</summary>
    public static Vector3? PlaneHit(Vector3 center, Vector3 normal, Vector3 rayOrigin, Vector3 rayDirection)
    {
        normal = Vector3.Normalize(normal);
        var denominator = Vector3.Dot(normal, rayDirection);
        if (MathF.Abs(denominator) < 1e-5f) return null;
        var t = Vector3.Dot(normal, center - rayOrigin) / denominator;
        return t < 0 ? null : rayOrigin + rayDirection * t;
    }

    /// <summary>Signed angle in radians (right-handed around <paramref name="axis"/>) from one point to another as seen from the center.</summary>
    public static float SignedAngle(Vector3 center, Vector3 axis, Vector3 from, Vector3 to)
    {
        axis = Vector3.Normalize(axis);
        var a = from - center; var b = to - center;
        a -= axis * Vector3.Dot(a, axis); b -= axis * Vector3.Dot(b, axis);
        if (a.LengthSquared() < 1e-8f || b.LengthSquared() < 1e-8f) return 0f;
        a = Vector3.Normalize(a); b = Vector3.Normalize(b);
        return MathF.Atan2(Vector3.Dot(axis, Vector3.Cross(a, b)), Vector3.Dot(a, b));
    }

    public static float Snap(float value, float step) => step <= 0f ? value : MathF.Round(value / step) * step;

    /// <summary>Stretch factor for a handle: how far the mouse is from the pivot now compared with when the drag began.</summary>
    public static float ScaleFactor(float startDistance, float currentDistance, float minimum = .05f)
        => MathF.Abs(startDistance) < 1e-3f ? 1f : Math.Max(minimum, currentDistance / startDistance);

    /// <summary>Rotates a point around a center by an angle about an axis.</summary>
    public static Vector3 RotateAround(Vector3 point, Vector3 center, Vector3 axis, float angle)
        => center + Vector3.Transform(point - center, Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle));
}
