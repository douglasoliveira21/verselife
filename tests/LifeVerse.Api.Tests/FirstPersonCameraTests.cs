using System.Numerics;
using LifeVerse.Shared.Camera;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class FirstPersonCameraTests
{
    private static readonly FirstPersonOptions On = new();

    [Fact]
    public void BadSettingsAreBroughtWithinLimits()
    {
        var bad = new FirstPersonOptions { BobStrength = 99, SwayStrength = -3, Stabilization = float.NaN, LookSmoothing = 7 }.Clamped();
        Assert.Equal(2f, bad.BobStrength); Assert.Equal(.25f, bad.SwayStrength); Assert.Equal(.6f, bad.Stabilization); Assert.Equal(1f, bad.LookSmoothing);
    }

    [Fact]
    public void TheDefaultTurnHasNoDelayOneCanNotice()
    {
        // at 60 frames per second the view covers most of the way to where the mouse asked in one frame
        var catchUpInOneFrame = 1f - MathF.Exp(-On.LookRate / 60f); Assert.True(catchUpInOneFrame > .7f, $"{catchUpInOneFrame}");
        Assert.True(new FirstPersonOptions { LookSmoothing = 0 }.LookRate > new FirstPersonOptions { LookSmoothing = 1 }.LookRate);
        Assert.True(new FirstPersonOptions { LookSmoothing = 1 }.LookRate >= 14f);
    }

    [Fact]
    public void WalkingBobsTheHeadAndStoppingFadesItOut()
    {
        var bob = new HeadBob(); var tallest = 0f;
        for (var i = 0; i < 240; i++) { bob.Step(1 / 60f, 4.5f, true, On); tallest = Math.Max(tallest, Math.Abs(bob.Offset.Y)); }
        Assert.InRange(tallest, .01f, .03f);                                          // centimetres, never a shake
        for (var i = 0; i < 240; i++) bob.Step(1 / 60f, 0f, true, On);
        Assert.Equal(Vector2.Zero, bob.Offset);
    }

    [Fact]
    public void RunningBobsMoreThanWalkingAndStrengthScalesIt()
    {
        float Peak(float speed, FirstPersonOptions o) { var b = new HeadBob(); var p = 0f; for (var i = 0; i < 300; i++) { b.Step(1 / 60f, speed, true, o); p = Math.Max(p, Math.Abs(b.Offset.Y)); } return p; }
        Assert.True(Peak(7f, On) > Peak(3f, On));
        Assert.True(Peak(4.5f, new FirstPersonOptions { BobStrength = 2 }) > Peak(4.5f, On) * 1.8f);
        Assert.True(Peak(4.5f, new FirstPersonOptions { BobStrength = .5f }) < Peak(4.5f, On) * .6f);
    }

    [Fact]
    public void TheBobCanBeSwitchedOffAndNeverHappensInTheAir()
    {
        var off = new HeadBob(); for (var i = 0; i < 120; i++) off.Step(1 / 60f, 5f, true, new FirstPersonOptions { HeadBob = false }); Assert.Equal(Vector2.Zero, off.Offset);
        var air = new HeadBob(); for (var i = 0; i < 120; i++) air.Step(1 / 60f, 5f, false, On); Assert.Equal(Vector2.Zero, air.Offset);
    }

    [Fact]
    public void TheViewLeansAgainstATurnWithoutOvershootingAndComesBack()
    {
        var sway = new CameraSway(); var biggest = 0f;
        for (var i = 0; i < 120; i++) { sway.Step(1 / 60f, 6f, 0, On); biggest = Math.Max(biggest, Math.Abs(sway.Roll)); }
        Assert.InRange(biggest, .005f, CameraSway.MaxRoll + .0001f); Assert.True(sway.Roll < 0);        // turning left tilts the view the other way
        for (var i = 0; i < 180; i++) sway.Step(1 / 60f, 0, 0, On); Assert.True(Math.Abs(sway.Roll) < .0005f);
        var none = new CameraSway(); for (var i = 0; i < 60; i++) none.Step(1 / 60f, 9f, 3f, new FirstPersonOptions { Sway = false }); Assert.Equal(0f, none.Roll, 4);
    }

    [Fact]
    public void TheStabilizerTakesOutQuickShakingButFollowsSlowMovement()
    {
        var steady = new EyeStabilizer(); var raw = new List<float>(); var filtered = new List<float>(); var dt = 1 / 60f;
        for (var i = 0; i < 300; i++)
        {
            var shake = MathF.Sin(i * 2.1f) * .02f; raw.Add(shake);                                      // a 5 Hz shake of two centimetres
            filtered.Add(steady.Step(new Vector3(0, 1.6f + shake, 0), dt, On.StabilizeSeconds, On.StabilizeVerticalSeconds).Y - 1.6f);
        }
        Assert.True(filtered.Skip(60).Max(x => Math.Abs(x)) < raw.Skip(60).Max(x => Math.Abs(x)) * .5f, "the shake is cut at least in half");
        var slow = new EyeStabilizer(); var last = Vector3.Zero; var lag = 0f;
        for (var i = 0; i < 120; i++) { var y = 1.6f - i / 120f * .5f; last = slow.Step(new Vector3(0, y, 0), dt, On.StabilizeSeconds, On.StabilizeVerticalSeconds); lag = Math.Max(lag, last.Y - y); }
        Assert.True(lag < .12f, $"sitting down is followed closely: {lag}");
    }

    [Fact]
    public void NoStabilizationFollowsTheHeadExactlyAndATeleportIsFollowedAtOnce()
    {
        var exact = new EyeStabilizer(); exact.Step(Vector3.Zero, 1 / 60f, 0, 0); Assert.Equal(new Vector3(.3f, 1, 0), exact.Step(new Vector3(.3f, 1, 0), 1 / 60f, 0, 0));
        var far = new EyeStabilizer(); far.Step(Vector3.Zero, 1 / 60f, .1f, .2f); Assert.Equal(new Vector3(40, 3, 40), far.Step(new Vector3(40, 3, 40), 1 / 60f, .1f, .2f));
    }

    [Fact]
    public void TheEyesStopShortOfAWall()
    {
        var anchor = new Vector3(0, 1.6f, 0); var eyes = new Vector3(0, 1.7f, -.1f);
        Assert.Equal(eyes, EyeCollision.Clip(anchor, eyes, null));
        var clipped = EyeCollision.Clip(anchor, eyes, .08f); Assert.True((clipped - anchor).Length() < (eyes - anchor).Length());
        Assert.Equal(anchor, EyeCollision.Clip(anchor, eyes, .03f));                                      // a wall right at the head: the eyes are at the head, never behind it
        Assert.Equal(eyes, EyeCollision.Clip(anchor, eyes, 5f));
    }
}
