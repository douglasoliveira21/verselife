using System.Numerics;

namespace LifeVerse.Shared.Building;

/// <summary>
/// The arithmetic of the editing camera, which circles a point (usually the selection): where the camera stands for a yaw, a pitch and a
/// distance, how panning moves the point, how far to stand to see an object of a given size, and how zooming steps. Kept apart from the
/// engine so the numbers can be tested.
/// </summary>
public static class OrbitMath
{
    public const float MinDistance = .6f, MaxDistance = 160f, MinPitch = -1.5f, MaxPitch = 1.5f;

    /// <summary>The camera position for a target seen from a direction (yaw turns around the vertical axis, pitch looks down when negative).</summary>
    public static Vector3 Position(Vector3 target, float yaw, float pitch, float distance)
    {
        pitch = Math.Clamp(pitch, MinPitch, MaxPitch);
        var horizontal = MathF.Cos(pitch) * distance;
        return target + new Vector3(MathF.Sin(yaw) * horizontal, -MathF.Sin(pitch) * distance, MathF.Cos(yaw) * horizontal);
    }

    /// <summary>How far to stand so a sphere of the given radius fills about two thirds of the view.</summary>
    public static float FocusDistance(float radius, float verticalFovDegrees)
    {
        var half = MathF.Max(.1f, verticalFovDegrees) * MathF.PI / 360f;
        return Math.Clamp(Math.Max(radius, .25f) / MathF.Tan(half) * 1.5f, MinDistance, MaxDistance);
    }

    /// <summary>One wheel step zooms by a fixed fraction, so it feels the same close up and far away.</summary>
    public static float Zoom(float distance, float steps) => Math.Clamp(distance * MathF.Pow(.88f, steps), MinDistance, MaxDistance);

    /// <summary>Moves the target sideways and up as seen from the camera; farther away, the same mouse movement covers more ground.</summary>
    public static Vector3 Pan(Vector3 target, Vector3 right, Vector3 up, float mouseDx, float mouseDy, float distance) =>
        target + (-right * mouseDx + up * mouseDy) * (distance * .0016f);

    public static float ClampPitch(float pitch) => Math.Clamp(pitch, MinPitch, MaxPitch);
}
