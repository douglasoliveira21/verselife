using Godot;

namespace LifeVerse.Client.Build;

/// <summary>The geometry behind the handles: where a camera ray touches an axis or a ring, how far an axis was dragged, how much a ring was turned, and snapping.</summary>
public static class BuildGizmoMath
{
    /// <summary>The parameter t of the point <c>axisPoint + axisDirection * t</c> that is closest to the ray, and the distance between the two lines.</summary>
    public static (float T, float Distance) ClosestOnAxis(Vector3 rayOrigin, Vector3 rayDirection, Vector3 axisPoint, Vector3 axisDirection)
    {
        var w = rayOrigin - axisPoint; var a = rayDirection.Dot(rayDirection); var b = rayDirection.Dot(axisDirection); var c = axisDirection.Dot(axisDirection);
        var d = rayDirection.Dot(w); var e = axisDirection.Dot(w); var denominator = a * c - b * b;
        if (Mathf.Abs(denominator) < 1e-6f) return (0, float.MaxValue); // the ray looks along the axis: it cannot be dragged
        var s = (b * e - c * d) / denominator; var t = (a * e - b * d) / denominator;
        var onRay = rayOrigin + rayDirection * s; var onAxis = axisPoint + axisDirection * t;
        return (t, onRay.DistanceTo(onAxis));
    }

    /// <summary>Where a ray meets a plane (null when it is parallel or behind).</summary>
    public static Vector3? RayPlane(Vector3 rayOrigin, Vector3 rayDirection, Vector3 planePoint, Vector3 planeNormal)
    {
        var denominator = rayDirection.Dot(planeNormal);
        if (Mathf.Abs(denominator) < 1e-6f) return null;
        var t = (planePoint - rayOrigin).Dot(planeNormal) / denominator;
        return t < 0 ? null : rayOrigin + rayDirection * t;
    }

    /// <summary>The angle, in radians and around <paramref name="axis"/>, of a point seen from the centre of a ring.</summary>
    public static float AngleAround(Vector3 centre, Vector3 axis, Vector3 point)
    {
        var u = axis.Cross(Mathf.Abs(axis.Y) < .9f ? Vector3.Up : Vector3.Right).Normalized(); var v = axis.Cross(u).Normalized(); var d = point - centre;
        return Mathf.Atan2(d.Dot(v), d.Dot(u));
    }

    /// <summary>The smallest turn between two angles, so a drag across the seam does not jump a full circle.</summary>
    public static float Delta(float from, float to) => Mathf.Wrap(to - from, -Mathf.Pi, Mathf.Pi);

    public static float Snap(float value, float step) => step <= 1e-6f ? value : Mathf.Round(value / step) * step;

    /// <summary>The Euler angles (degrees) a basis is stored as: the same order the renderer uses for an object (Y, then X, then Z).</summary>
    public static Vector3 EulerDegrees(Basis basis) { var e = basis.Orthonormalized().GetEuler(EulerOrder.Yxz); return new Vector3(Mathf.RadToDeg(e.X), Mathf.RadToDeg(e.Y), Mathf.RadToDeg(e.Z)); }

    public static Basis BasisOf(float xDegrees, float yDegrees, float zDegrees) => Basis.FromEuler(new Vector3(Mathf.DegToRad(xDegrees), Mathf.DegToRad(yDegrees), Mathf.DegToRad(zDegrees)), EulerOrder.Yxz);
}
