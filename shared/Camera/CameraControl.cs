using System.Numerics;
using LifeVerse.Shared.Building;

namespace LifeVerse.Shared.Camera;

/// <summary>How the person wants the camera to behave. Every value is brought within sensible limits by <see cref="Clamped"/>, so a bad setting can never leave the camera unusable.</summary>
public sealed record CameraOptions
{
    public float SensitivityH { get; init; } = .003f;
    public float SensitivityV { get; init; } = .003f;
    public bool InvertH { get; init; }
    public bool InvertV { get; init; }
    /// <summary>1 is one normal wheel notch; the proportional zoom (free camera) uses it as a multiplier.</summary>
    public float ZoomSpeed { get; init; } = 1f;
    /// <summary>The wheel with CTRL held: how much of a normal notch it is.</summary>
    public float PrecisionZoomSpeed { get; init; } = .25f;
    /// <summary>How quickly the camera catches up with where it was asked to be (per second; higher is snappier).</summary>
    public float Smoothing { get; init; } = 14f;
    public float MinDistance { get; init; } = .2f;
    public float MaxDistance { get; init; } = 40f;
    /// <summary>85 degrees: nearly straight up or down, never over the top.</summary>
    public const float DefaultPitchLimit = 1.4835299f;
    public float MinPitch { get; init; } = -DefaultPitchLimit;
    public float MaxPitch { get; init; } = DefaultPitchLimit;
    public bool Collision { get; init; } = true;
    /// <summary>After CTRL is let go the camera stays where it is (otherwise it goes back behind the avatar).</summary>
    public bool KeepAfterCtrl { get; init; } = true;
    /// <summary>When the camera is left in a free view, it goes back behind the avatar by itself once the avatar moves.</summary>
    public bool RestoreAutomatically { get; init; } = true;
    /// <summary>When the editor of an attachment (or of the appearance) is closed the camera goes back to how it was; otherwise it stays where the editing left it.</summary>
    public bool RestoreAfterEdit { get; init; } = true;
    public bool AllowDetached { get; init; }
    /// <summary>The Second Life controls: ALT, CTRL+ALT and CTRL+ALT+SHIFT with the left button.</summary>
    public bool Classic { get; init; } = true;
    /// <summary>Holding CTRL and dragging with the left button orbits the camera.</summary>
    public bool CtrlSimplified { get; init; }
    /// <summary>A double click on an avatar or an object puts the camera on it (on the ground it still walks).</summary>
    public bool FocusOnDoubleClick { get; init; }
    /// <summary>The detached camera: how far from the avatar it may go (metres) and how fast it moves (metres per second).</summary>
    public float DetachedRange { get; init; } = 60f;
    public float DetachedSpeed { get; init; } = 8f;

    public CameraOptions Clamped()
    {
        float C(float v, float lo, float hi, float fallback) => float.IsFinite(v) ? Math.Clamp(v, lo, hi) : fallback;
        var min = C(MinDistance, .05f, 5f, .2f); var max = Math.Max(C(MaxDistance, 2f, 200f, 40f), min + 1f);
        var lowPitch = C(MinPitch, -1.55f, -.1f, -DefaultPitchLimit); var highPitch = C(MaxPitch, .1f, 1.55f, DefaultPitchLimit);
        return this with
        {
            SensitivityH = C(SensitivityH, .0003f, .02f, .003f), SensitivityV = C(SensitivityV, .0003f, .02f, .003f), ZoomSpeed = C(ZoomSpeed, .1f, 5f, 1f), PrecisionZoomSpeed = C(PrecisionZoomSpeed, .02f, 1f, .25f),
            Smoothing = C(Smoothing, 1f, 60f, 14f), DetachedRange = C(DetachedRange, 5f, 200f, 60f), DetachedSpeed = C(DetachedSpeed, 1f, 60f, 8f), MinDistance = min, MaxDistance = max, MinPitch = lowPitch, MaxPitch = highPitch
        };
    }
}

public enum CameraGesture { None, OrbitDolly, Orbit, Pan, CtrlOrbit }
public enum CameraWheel { Normal, Precision }

/// <summary>Which camera movement a mouse gesture is, from the keys held and the options: all in one place so the editors can ask the same question before they use the mouse themselves.</summary>
public static class CameraInputRules
{
    /// <summary>The gesture started by pressing the left button with these modifiers.</summary>
    public static CameraGesture Classify(bool ctrl, bool alt, bool shift, CameraOptions options)
    {
        if (options.Classic)
        {
            if (alt && ctrl && shift) return CameraGesture.Pan;
            if (alt && ctrl) return CameraGesture.Orbit;
            if (alt && !shift) return CameraGesture.OrbitDolly;
        }
        if (options.CtrlSimplified && ctrl && !alt) return shift ? CameraGesture.Pan : CameraGesture.CtrlOrbit;
        return CameraGesture.None;
    }

    public static CameraWheel ClassifyWheel(bool ctrl) => ctrl ? CameraWheel.Precision : CameraWheel.Normal;

    /// <summary>True when the left button with these keys belongs to the camera (so a tool must leave it alone).</summary>
    public static bool IsCameraGesture(bool ctrl, bool alt, bool shift, CameraOptions options) => Classify(ctrl, alt, shift, options) != CameraGesture.None;
}

/// <summary>Turns a mouse movement into a turn around the point looked at; keeps the angle where it cannot flip over.</summary>
public sealed class CameraOrbitController(CameraOptions options)
{
    public CameraOptions Options { get; set; } = options;
    public float Yaw { get; set; }
    public float Pitch { get; set; }

    /// <summary>Dragging right turns the view to the right; dragging down looks from higher up. Both can be inverted. Returns the new angles.</summary>
    public (float Yaw, float Pitch) Orbit(float dx, float dy)
    {
        Yaw -= dx * Options.SensitivityH * (Options.InvertH ? -1f : 1f);
        Pitch = ClampPitch(Pitch + dy * Options.SensitivityV * (Options.InvertV ? 1f : -1f));
        return (Yaw, Pitch);
    }

    /// <summary>Only the horizontal part (the vertical drag does something else).</summary>
    public float OrbitHorizontal(float dx) { Yaw -= dx * Options.SensitivityH * (Options.InvertH ? -1f : 1f); return Yaw; }

    public float ClampPitch(float pitch) => Math.Clamp(pitch, Options.MinPitch, Options.MaxPitch);

    /// <summary>The camera position for a focus point seen at the current angles.</summary>
    public Vector3 PositionFor(Vector3 focus, float distance)
    {
        var horizontal = MathF.Cos(Pitch) * distance;
        return focus + new Vector3(MathF.Sin(Yaw) * horizontal, -MathF.Sin(Pitch) * distance, MathF.Cos(Yaw) * horizontal);
    }
}

/// <summary>How far the camera is from what it looks at. Zoom is proportional (the same notch feels the same close up and far away); the precision wheel takes a smaller part of it.</summary>
public sealed class CameraZoomController(CameraOptions options)
{
    public CameraOptions Options { get; set; } = options;
    /// <summary>Where the camera is going.</summary>
    public float Target { get; private set; } = 5f;
    /// <summary>Where it is now (it glides towards the target).</summary>
    public float Current { get; private set; } = 5f;

    public void Set(float distance, bool immediate = false) { Target = Limit(distance); if (immediate) Current = Target; }

    private float Limit(float distance) => Math.Clamp(float.IsFinite(distance) ? distance : Target, Options.MinDistance, Options.MaxDistance);

    /// <summary>Wheel notches (positive: closer). With <paramref name="precision"/> a notch is a small part of a normal one.</summary>
    public float Zoom(float notches, bool precision)
    {
        var amount = notches * Options.ZoomSpeed * (precision ? Options.PrecisionZoomSpeed : 1f);
        Target = Limit(Target * MathF.Pow(.88f, amount)); return Target;
    }

    /// <summary>A vertical drag with ALT: dragging down brings the camera closer.</summary>
    public float Dolly(float dy) { Target = Limit(Target * MathF.Exp(-dy * .005f * Options.ZoomSpeed)); return Target; }

    /// <summary>One step of the glide; with no time passed nothing moves and a huge step lands exactly on the target.</summary>
    public float Update(float dt) { Current = Glide(Current, Target, dt, Options.Smoothing); return Current; }

    public static float Glide(float current, float target, float dt, float rate) => current + (target - current) * (1f - MathF.Exp(-Math.Max(rate, 0f) * Math.Max(dt, 0f)));
}

/// <summary>Slides the point looked at sideways and up, as the camera sees it; far away the same movement covers more ground.</summary>
public static class CameraPanController
{
    public static Vector3 Pan(Vector3 focus, Vector3 right, Vector3 up, float dx, float dy, float distance, float speed = 1f) => OrbitMath.Pan(focus, right, up, dx * speed, dy * speed, distance);
}

/// <summary>Where to look and how far to stand to see something whole.</summary>
public static class CameraFocusManager
{
    /// <summary>How far to stand so a sphere of this radius fills about two thirds of the view.</summary>
    public static float DistanceFor(float radius, float verticalFovDegrees, CameraOptions options) => Math.Clamp(Math.Max(radius, .005f) / MathF.Tan(Math.Max(.1f, verticalFovDegrees) * MathF.PI / 360f) * 1.5f, options.MinDistance, options.MaxDistance);

    /// <summary>The middle of a box.</summary>
    public static Vector3 Center(Vector3 min, Vector3 max) => (min + max) * .5f;

    /// <summary>The point looked at moves towards a new one without a jump.</summary>
    public static Vector3 Blend(Vector3 current, Vector3 target, float dt, float rate) { var k = 1f - MathF.Exp(-Math.Max(rate, 0f) * Math.Max(dt, 0f)); return current + (target - current) * k; }
}

/// <summary>Keeps the camera in front of walls: when something is in the way it stands closer, never farther than asked and never inside the thing it looks at.</summary>
public static class CameraCollisionHandler
{
    public static float Resolve(float desired, float? obstructionAt, float margin, float minimum) => obstructionAt is { } hit ? Math.Clamp(hit - margin, Math.Min(minimum, desired), desired) : desired;
}

/// <summary>The camera that is not tied to the avatar: how it moves and where it may not go (too far from the avatar, off the island, into the ground).</summary>
public static class CameraDetachedRules
{
    /// <summary>Where the camera is after a step. <paramref name="move"/> is (right, up, forward) with each part from -1 to 1.</summary>
    public static Vector3 Move(Vector3 position, Vector3 forward, Vector3 right, Vector3 move, float speed, float dt)
    {
        var direction = right * move.X + Vector3.UnitY * move.Y + forward * move.Z;
        if (direction.LengthSquared() > 1f) direction = Vector3.Normalize(direction);
        return position + direction * speed * Math.Max(dt, 0f);
    }

    /// <summary>The nearest place the camera is allowed to be: within the range of the avatar, inside the island (when its half size is known) and above the ground.</summary>
    public static Vector3 Constrain(Vector3 position, Vector3 avatar, float range, float islandHalfExtent, float groundHeight)
    {
        var flat = new Vector3(position.X - avatar.X, 0, position.Z - avatar.Z);
        if (flat.Length() > range) flat = Vector3.Normalize(flat) * range;
        var x = avatar.X + flat.X; var z = avatar.Z + flat.Z;
        if (islandHalfExtent > 2f) { x = Math.Clamp(x, -islandHalfExtent + 1f, islandHalfExtent - 1f); z = Math.Clamp(z, -islandHalfExtent + 1f, islandHalfExtent - 1f); }
        var y = Math.Clamp(position.Y, groundHeight + .3f, Math.Max(groundHeight + .3f, avatar.Y + range));
        return new Vector3(x, y, z);
    }
}
