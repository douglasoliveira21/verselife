using System.Numerics;

namespace LifeVerse.Shared.Camera;

/// <summary>
/// What the person chose for the first person camera. Every value is brought within limits by <see cref="Clamped"/>, so a bad setting can never leave the camera shaking or lagging behind the mouse.
/// </summary>
public sealed record FirstPersonOptions
{
    /// <summary>A light up-and-down and side-to-side movement of the head while walking and running.</summary>
    public bool HeadBob { get; init; } = true;
    /// <summary>1 is the normal bob; 0.25 to 2.</summary>
    public float BobStrength { get; init; } = 1f;
    /// <summary>A small lean of the view when turning and when sidestepping.</summary>
    public bool Sway { get; init; } = true;
    public float SwayStrength { get; init; } = 1f;
    /// <summary>How much of the shaking that the animations of the skeleton put in the head is taken out: 0 follows the head exactly, 1 is the steadiest.</summary>
    public float Stabilization { get; init; } = .6f;
    /// <summary>How soft the turn of the view is: 0 follows the mouse at once, 1 is slow and floaty. The default is not noticeable as a delay.</summary>
    public float LookSmoothing { get; init; } = .35f;
    /// <summary>The camera does not go through walls and objects (also when the face is against one).</summary>
    public bool Collision { get; init; } = true;

    public const float MaxBob = 2f, MinBob = .25f;

    public FirstPersonOptions Clamped() => this with
    {
        BobStrength = Fin(BobStrength, MinBob, MaxBob, 1f), SwayStrength = Fin(SwayStrength, MinBob, MaxBob, 1f),
        Stabilization = Fin(Stabilization, 0f, 1f, .6f), LookSmoothing = Fin(LookSmoothing, 0f, 1f, .35f)
    };

    private static float Fin(float v, float min, float max, float fallback) => float.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

    /// <summary>How quickly the view catches up with the mouse, per second: very fast at 0 (no delay one can notice), gentle at 1.</summary>
    public float LookRate => 120f + (14f - 120f) * Math.Clamp(LookSmoothing, 0f, 1f);

    /// <summary>Time constants of the steadying of the head, in seconds: sideways and forward, and up and down (the up and down is slower, so standing up and sitting down glide).</summary>
    public float StabilizeSeconds => Math.Clamp(Stabilization, 0f, 1f) * .10f;
    public float StabilizeVerticalSeconds => Math.Clamp(Stabilization, 0f, 1f) * .18f;
}

/// <summary>The bob of the head: a step lifts and lowers it twice per cycle and sways it once; it grows with the speed, fades in when walking starts and out when it stops or the feet leave the ground.</summary>
public sealed class HeadBob
{
    private float _phase, _amount;

    /// <summary>The bob now, in metres, as the person sees it: X to the right, Y up.</summary>
    public Vector2 Offset { get; private set; }
    public float Amount => _amount;

    /// <summary>Metres of lift at a brisk walk (4.5 m/s) with strength 1.</summary>
    public const float LiftMetres = .016f, SwayMetres = .010f;

    public void Step(float dt, float speed, bool grounded, FirstPersonOptions options)
    {
        if (dt <= 0) return;
        var wanted = options.HeadBob && grounded && speed > .3f ? 1f : 0f;
        _amount += (wanted - _amount) * (1f - MathF.Exp(-(wanted > _amount ? 9f : 10f) * dt));
        if (_amount < .003f && wanted == 0f) { _amount = 0; Offset = Vector2.Zero; return; }
        var stepsPerSecond = Math.Clamp(speed * .55f, 1.2f, 3.4f);
        _phase = (_phase + MathF.Tau * stepsPerSecond * dt * .5f) % MathF.Tau;            // one cycle is two steps (left, right)
        var size = Math.Clamp(speed / 4.5f, 0f, 1.6f) * options.BobStrength * _amount;
        Offset = new Vector2(MathF.Sin(_phase) * SwayMetres * size, MathF.Sin(2f * _phase) * LiftMetres * size);
    }

    public void Reset() { _phase = 0; _amount = 0; Offset = Vector2.Zero; }
}

/// <summary>The lean of the view: it tilts a little against a turn and a sidestep, and comes back softly. Roll is in radians.</summary>
public sealed class CameraSway
{
    private float _roll, _velocity;
    public float Roll => _roll;

    /// <summary>Largest lean, in radians (about two degrees).</summary>
    public const float MaxRoll = .035f;

    /// <param name="yawRate">Radians per second the view is turning (positive to the left).</param>
    /// <param name="strafe">Sideways speed in m/s (positive to the right).</param>
    public void Step(float dt, float yawRate, float strafe, FirstPersonOptions options)
    {
        if (dt <= 0) return;
        var target = options.Sway ? Math.Clamp(-yawRate * .006f + -strafe * .004f, -MaxRoll, MaxRoll) * options.SwayStrength : 0f;
        target = Math.Clamp(target, -MaxRoll, MaxRoll);
        // a critically damped spring: no overshoot, no wobble
        const float omega = 16f; var x = _roll - target; var temp = (_velocity + omega * x) * dt; var exp = MathF.Exp(-omega * dt);
        _roll = target + (x + temp) * exp; _velocity = (_velocity - omega * temp) * exp;
    }

    public void Reset() { _roll = 0; _velocity = 0; }
}

/// <summary>Takes the shaking out of where the head is without delaying the person's own turning (that is not filtered at all, only the position of the eyes is).</summary>
public sealed class EyeStabilizer
{
    private Vector3 _position; private bool _started;
    /// <summary>A jump larger than this (a teleport, a respawn) is followed at once.</summary>
    public const float SnapDistance = 1.5f;

    public Vector3 Step(Vector3 target, float dt, float seconds, float verticalSeconds)
    {
        if (!_started || dt <= 0 || Vector3.Distance(_position, target) > SnapDistance) { _position = target; _started = true; return _position; }
        var h = seconds <= 1e-4f ? 1f : 1f - MathF.Exp(-dt / seconds); var v = verticalSeconds <= 1e-4f ? 1f : 1f - MathF.Exp(-dt / verticalSeconds);
        _position = new Vector3(_position.X + (target.X - _position.X) * h, _position.Y + (target.Y - _position.Y) * v, _position.Z + (target.Z - _position.Z) * h);
        return _position;
    }

    public void Reset() => _started = false;
}

/// <summary>Where the camera may be when something is in the way between the head and the eyes.</summary>
public static class EyeCollision
{
    /// <summary>Pulls the eyes back toward the head when a solid thing is hit by a ray from <paramref name="anchor"/> through <paramref name="eyes"/> and a little beyond; <paramref name="hitDistance"/> is null when nothing was hit.</summary>
    public static Vector3 Clip(Vector3 anchor, Vector3 eyes, float? hitDistance, float margin = .06f)
    {
        if (hitDistance is not { } hit) return eyes;
        var along = eyes - anchor; var length = along.Length(); if (length < 1e-5f) return eyes;
        var allowed = Math.Clamp(hit - margin, 0f, length);
        return anchor + along / length * allowed;
    }
}
