namespace LifeVerse.Shared.Gameplay;

/// One number per joint. Angles are in radians and in natural terms, so they read the same for every limb:
/// thighs and shoulders swing forward when positive, knees and elbows flex (bend) when positive, ankles lift the toes
/// when positive, "outward" moves a limb away from the body, torso and hips lean forward when positive, the head nods down
/// when positive. Rolls and yaws lean or turn to the avatar's left when positive. The three hip offsets are in metres.
public enum PoseChannel
{
    HipsX, HipsY, HipsZ, HipsPitch, HipsRoll, HipsYaw,
    TorsoPitch, TorsoRoll, TorsoYaw,
    HeadPitch, HeadYaw, HeadRoll,
    LeftThigh, LeftKnee, LeftAnkle, RightThigh, RightKnee, RightAnkle,
    LeftShoulder, LeftOutward, LeftElbow, RightShoulder, RightOutward, RightElbow,
    Count
}

/// What the solver needs to know about the avatar this frame. `TurnRate` is positive when turning left (radians per second),
/// `LandingProgress` runs 0 to 1 over the crouch after a fall (0 when not landing).
public readonly record struct LocomotionInput(float Speed, float TurnRate, bool Airborne, float VerticalSpeed, float LandingProgress, float DeltaTime);

/// Turns speed into a believable body pose. The stride length follows the speed so the planted foot does not slide, the
/// legs swing opposite to each other and the arms opposite to the legs, the knees bend while a leg swings through, the
/// hips bob twice per stride and sway over the planted foot, the torso twists against the hips and leans into speed,
/// acceleration and turns, and the head stays level. Standing still the body breathes and shifts its weight.
/// All channels are smoothed so changing speed, jumping or landing never snaps.
public sealed class LocomotionSolver
{
    public const float WalkSpeedForRunBlend = 3.8f;
    public const float RunFullSpeed = 6.2f;
    private const float SmoothingRate = 26f;

    private readonly float[] _pose = new float[(int)PoseChannel.Count];
    private readonly float[] _target = new float[(int)PoseChannel.Count];
    private readonly float[] _gait = new float[(int)PoseChannel.Count];
    private float _time;
    private float _previousSpeed;
    private float _acceleration;

    /// Hip to ankle length in metres; the stride that avoids foot sliding depends on it.
    public float LegLength { get; set; } = .9f;
    /// Position in the stride, 0 to 2 pi. Public so tests and previews can freeze the walk at a chosen moment.
    public float Phase { get; set; }
    /// When set, the phase is not advanced by speed (previews).
    public bool FreezePhase { get; set; }

    public float this[PoseChannel channel] => _pose[(int)channel];
    public ReadOnlySpan<float> Pose => _pose;

    /// Stride length in metres for a speed: one full cycle (both legs).
    public static float StrideFor(float speed) => .35f + .37f * speed;

    public void Step(in LocomotionInput input)
    {
        var dt = Math.Clamp(float.IsFinite(input.DeltaTime) ? input.DeltaTime : .016f, .0005f, .1f);
        _time += dt;
        var speed = Math.Clamp(float.IsFinite(input.Speed) ? input.Speed : 0f, 0f, 12f);
        var turn = float.IsFinite(input.TurnRate) ? Math.Clamp(input.TurnRate, -8f, 8f) : 0f;
        _acceleration += ((speed - _previousSpeed) / dt - _acceleration) * (1f - MathF.Exp(-6f * dt));
        _previousSpeed = speed;

        Array.Clear(_target);
        Idle(_target);
        if (input.Airborne) Air(_target, speed, float.IsFinite(input.VerticalSpeed) ? input.VerticalSpeed : 0f);
        else
        {
            var stride = StrideFor(speed);
            if (!FreezePhase) Phase = Wrap(Phase + MathF.Tau * (speed / stride) * dt);
            Gait(_gait, speed, turn);
            var moving = Smooth(.12f, .8f, speed);
            for (var i = 0; i < _target.Length; i++) _target[i] += (_gait[i] - _target[i]) * moving;
        }
        if (input.LandingProgress > 0f) Land(_target, Math.Clamp(input.LandingProgress, 0f, 1f));
        var amount = 1f - MathF.Exp(-SmoothingRate * dt);
        for (var i = 0; i < _pose.Length; i++) _pose[i] += (_target[i] - _pose[i]) * amount;
    }

    /// Where the left foot is in front of the hip (metres), from the thigh and knee angles. Used to check for foot sliding.
    public float LeftFootForward() => FootForward(_pose[(int)PoseChannel.LeftThigh], _pose[(int)PoseChannel.LeftKnee]);
    public float RightFootForward() => FootForward(_pose[(int)PoseChannel.RightThigh], _pose[(int)PoseChannel.RightKnee]);
    private float FootForward(float thigh, float knee) => LegLength / 2f * (MathF.Sin(thigh) + MathF.Sin(thigh - knee));

    // ---- standing --------------------------------------------------------------------------------------------------

    private void Idle(float[] t)
    {
        var breath = MathF.Sin(MathF.Tau * _time / 3.8f);
        var shift = MathF.Sin(MathF.Tau * _time / 8.5f);
        t[(int)PoseChannel.TorsoPitch] = .014f * breath + .01f;
        t[(int)PoseChannel.HipsX] = .012f * shift;
        t[(int)PoseChannel.HipsRoll] = .022f * shift;
        t[(int)PoseChannel.TorsoRoll] = -.015f * shift;
        t[(int)PoseChannel.HeadYaw] = .06f * MathF.Sin(_time * .45f) + .03f * MathF.Sin(_time * 1.1f + 1f);
        t[(int)PoseChannel.HeadPitch] = .02f * MathF.Sin(_time * .37f + 2f) - .015f * breath;
        t[(int)PoseChannel.LeftThigh] = .012f * shift; t[(int)PoseChannel.RightThigh] = -.012f * shift;
        t[(int)PoseChannel.LeftKnee] = .035f + .02f * MathF.Max(0f, -shift); t[(int)PoseChannel.RightKnee] = .035f + .02f * MathF.Max(0f, shift);
        t[(int)PoseChannel.LeftAnkle] = .02f; t[(int)PoseChannel.RightAnkle] = .02f;
        t[(int)PoseChannel.LeftShoulder] = .05f + .012f * breath; t[(int)PoseChannel.RightShoulder] = .05f + .012f * breath;
        t[(int)PoseChannel.LeftOutward] = .07f + .01f * breath; t[(int)PoseChannel.RightOutward] = .07f + .01f * breath;
        t[(int)PoseChannel.LeftElbow] = .16f + .02f * breath; t[(int)PoseChannel.RightElbow] = .16f + .02f * breath;
    }

    // ---- walking and running ---------------------------------------------------------------------------------------

    /// The full-strength gait for this speed; the caller blends it in as the avatar starts moving.
    private void Gait(float[] g, float speed, float turn)
    {
        Array.Clear(g);
        var run = Smooth(WalkSpeedForRunBlend, RunFullSpeed, speed);
        var stride = StrideFor(speed);
        // Half the stride is covered while a foot is planted: its thigh sweeps from +a to -a, so 2 L sin(a) = stride / 2.
        var amplitude = MathF.Asin(Math.Clamp(stride / (4f * LegLength), 0f, .95f));
        var s = MathF.Sin(Phase); var c = MathF.Cos(Phase);
        var swingLeft = MathF.Max(0f, c); var swingRight = MathF.Max(0f, -c);
        var loadLeft = MathF.Max(0f, s); var loadRight = MathF.Max(0f, -s);

        var thighLeft = amplitude * s + .05f * run; var thighRight = -amplitude * s + .05f * run;
        var swingKnee = .55f + 1.15f * run;
        var kneeLeft = .05f + swingKnee * MathF.Pow(swingLeft, 1.2f) + (.06f + .12f * run) * Smooth(0f, 1f, loadLeft);
        var kneeRight = .05f + swingKnee * MathF.Pow(swingRight, 1.2f) + (.06f + .12f * run) * Smooth(0f, 1f, loadRight);
        // A planted foot stays flat on the ground; a swinging one lifts its toes a little ready for the heel strike.
        var plantedLeft = Smooth(.25f, -.25f, c); var plantedRight = Smooth(.25f, -.25f, -c);
        g[(int)PoseChannel.LeftThigh] = thighLeft; g[(int)PoseChannel.RightThigh] = thighRight;
        g[(int)PoseChannel.LeftKnee] = kneeLeft; g[(int)PoseChannel.RightKnee] = kneeRight;
        g[(int)PoseChannel.LeftAnkle] = Math.Clamp((kneeLeft - thighLeft) * plantedLeft + .25f * (1f - plantedLeft), -.7f, .7f);
        g[(int)PoseChannel.RightAnkle] = Math.Clamp((kneeRight - thighRight) * plantedRight + .25f * (1f - plantedRight), -.7f, .7f);

        // Hips: lowest when both legs are spread (twice per stride), sway over the planted foot, drop on the swinging side.
        var bob = LegLength * (.03f + .035f * run);
        g[(int)PoseChannel.HipsY] = -bob * (1f - MathF.Cos(2f * Phase)) / 2f;
        g[(int)PoseChannel.HipsX] = .022f * (1f - .5f * run) * c;
        g[(int)PoseChannel.HipsRoll] = .04f * c;
        g[(int)PoseChannel.HipsYaw] = -(.09f + .07f * run) * s;
        g[(int)PoseChannel.TorsoYaw] = (.11f + .1f * run) * s;

        // Arms swing against the legs and bend more as they come forward.
        var armSwing = .35f + .55f * run;
        var armBias = .04f + .1f * run;
        g[(int)PoseChannel.LeftShoulder] = -armSwing * s + armBias; g[(int)PoseChannel.RightShoulder] = armSwing * s + armBias;
        var elbowBase = .2f + .75f * run; var elbowSwing = .25f + .25f * run;
        g[(int)PoseChannel.LeftElbow] = elbowBase + elbowSwing * MathF.Max(0f, -s);
        g[(int)PoseChannel.RightElbow] = elbowBase + elbowSwing * MathF.Max(0f, s);
        g[(int)PoseChannel.LeftOutward] = .07f + .08f * run; g[(int)PoseChannel.RightOutward] = .07f + .08f * run;

        // Lean into speed and acceleration, keep the head level, lead and bank into turns.
        var lean = .014f * speed + .1f * run + Math.Clamp(_acceleration * .012f, -.1f, .16f);
        g[(int)PoseChannel.TorsoPitch] = lean * .7f; g[(int)PoseChannel.HipsPitch] = lean * .3f;
        g[(int)PoseChannel.HeadPitch] = -lean * .8f + .02f * MathF.Sin(2f * Phase);
        g[(int)PoseChannel.HeadYaw] = Math.Clamp(turn * .15f, -.4f, .4f);
        g[(int)PoseChannel.TorsoRoll] = Math.Clamp(turn * speed * .01f, -.2f, .2f);
    }

    // ---- in the air ------------------------------------------------------------------------------------------------

    private void Air(float[] t, float speed, float verticalSpeed)
    {
        var a = Math.Clamp(verticalSpeed / 5.2f, -1f, 1f);
        var up = MathF.Max(0f, a);
        t[(int)PoseChannel.LeftThigh] = .60f * up + .25f * (1f - up); t[(int)PoseChannel.RightThigh] = -.10f * up + .05f * (1f - up);
        t[(int)PoseChannel.LeftKnee] = .85f * up + .45f * (1f - up); t[(int)PoseChannel.RightKnee] = .55f * up + .35f * (1f - up);
        t[(int)PoseChannel.LeftAnkle] = -.25f; t[(int)PoseChannel.RightAnkle] = -.25f;
        t[(int)PoseChannel.LeftShoulder] = .9f * up + .25f * (1f - up); t[(int)PoseChannel.RightShoulder] = .9f * up + .25f * (1f - up);
        t[(int)PoseChannel.LeftOutward] = .35f * up + .85f * (1f - up); t[(int)PoseChannel.RightOutward] = .35f * up + .85f * (1f - up);
        t[(int)PoseChannel.LeftElbow] = .4f; t[(int)PoseChannel.RightElbow] = .3f;
        t[(int)PoseChannel.TorsoPitch] = .04f + .1f * MathF.Min(1f, speed / 6f);
        t[(int)PoseChannel.HeadPitch] = -.1f;
    }

    /// Crouch after landing: hips drop, knees and hips fold, arms come forward for balance, and it rises again.
    private void Land(float[] t, float progress)
    {
        var crouch = MathF.Sin(MathF.PI * progress);
        t[(int)PoseChannel.HipsY] -= .22f * (LegLength / .9f) * crouch;
        t[(int)PoseChannel.LeftThigh] += .55f * crouch; t[(int)PoseChannel.RightThigh] += .55f * crouch;
        t[(int)PoseChannel.LeftKnee] += 1.1f * crouch; t[(int)PoseChannel.RightKnee] += 1.1f * crouch;
        t[(int)PoseChannel.LeftAnkle] += .55f * crouch; t[(int)PoseChannel.RightAnkle] += .55f * crouch;
        t[(int)PoseChannel.TorsoPitch] += .22f * crouch;
        t[(int)PoseChannel.LeftShoulder] += .35f * crouch; t[(int)PoseChannel.RightShoulder] += .35f * crouch;
        t[(int)PoseChannel.HeadPitch] -= .15f * crouch;
    }

    private static float Smooth(float from, float to, float x)
    {
        var t = Math.Clamp((x - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Wrap(float angle) { while (angle >= MathF.Tau) angle -= MathF.Tau; while (angle < 0) angle += MathF.Tau; return angle; }
}
