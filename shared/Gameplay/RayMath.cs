using System.Numerics;

namespace LifeVerse.Shared.Gameplay;

public static class RayMath
{
    /// <summary>
    /// How close a ray passes to a straight line segment (an avatar's body from feet to head), and how far along the ray that closest point is.
    /// Avatars have no collision shape in the world, so this is how a click is matched to a person. The ray is limited to <paramref name="maxLength"/>.
    /// </summary>
    public static float DistanceToSegment(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, float maxLength, out float along)
    {
        direction = Vector3.Normalize(direction);
        var segment = b - a; var offset = origin - a;
        var length2 = Vector3.Dot(segment, segment);
        var c = Vector3.Dot(direction, offset); var bb = Vector3.Dot(direction, segment); var f = Vector3.Dot(segment, offset);
        var denominator = length2 - bb * bb;
        float t;
        if (length2 < 1e-9f) t = 0f;
        else
        {
            var s0 = denominator > 1e-6f ? Math.Clamp((bb * f - c * length2) / denominator, 0f, maxLength) : 0f;
            t = Math.Clamp((bb * s0 + f) / length2, 0f, 1f);
        }
        along = Math.Clamp(bb * t - c, 0f, maxLength);
        return Vector3.Distance(origin + direction * along, a + segment * t);
    }
}
