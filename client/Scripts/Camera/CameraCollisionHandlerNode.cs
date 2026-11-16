using Godot;
using LifeVerse.Shared.Camera;

namespace LifeVerse.Client.Scripts.Camera;

/// <summary>The camera in front of walls: the spring arm of the player already pulls the camera in when something is in the way; this switches it on and off by the person's choice and says how close the camera really is.</summary>
public static class CameraCollision
{
    public const float Margin = .15f;

    public static void Apply(SpringArm3D arm, bool enabled) { arm.CollisionMask = enabled ? 1u : 0u; arm.Margin = Margin; }

    /// <summary>How far from the focus the camera really stands (shorter than asked when a wall is in the way).</summary>
    public static float ActualDistance(SpringArm3D arm) => arm.CollisionMask == 0 ? arm.SpringLength : arm.GetHitLength();
}
