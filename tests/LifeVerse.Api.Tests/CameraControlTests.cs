using System.Numerics;
using LifeVerse.Shared.Camera;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The rules of the free camera: which gesture is which, how it orbits, zooms and pans, how far it may go and how it stays in front of walls.</summary>
public sealed class CameraControlTests
{
    private static readonly CameraOptions Defaults = new();

    [Theory]
    [InlineData(false, true, false, CameraGesture.OrbitDolly)]
    [InlineData(true, true, false, CameraGesture.Orbit)]
    [InlineData(true, true, true, CameraGesture.Pan)]
    [InlineData(false, true, true, CameraGesture.None)]
    [InlineData(true, false, false, CameraGesture.None)]
    [InlineData(false, false, false, CameraGesture.None)]
    [InlineData(false, false, true, CameraGesture.None)]
    public void TheClassicControlsAreTheSecondLifeOnes(bool ctrl, bool alt, bool shift, CameraGesture expected) => Assert.Equal(expected, CameraInputRules.Classify(ctrl, alt, shift, Defaults));

    [Fact]
    public void WithTheClassicControlsOffAltDoesNothing() => Assert.Equal(CameraGesture.None, CameraInputRules.Classify(false, true, false, Defaults with { Classic = false }));

    [Fact]
    public void TheSimpleControlMakesCtrlAloneOrbitAndCtrlShiftPan()
    {
        var simple = Defaults with { CtrlSimplified = true };
        Assert.Equal(CameraGesture.CtrlOrbit, CameraInputRules.Classify(true, false, false, simple)); Assert.Equal(CameraGesture.Pan, CameraInputRules.Classify(true, false, true, simple));
        Assert.Equal(CameraGesture.Orbit, CameraInputRules.Classify(true, true, false, simple)); // the classic ones are still there
        Assert.Equal(CameraGesture.None, CameraInputRules.Classify(true, false, false, Defaults)); // and off by default: CTRL alone is left to whatever else uses it
        Assert.Equal(CameraGesture.CtrlOrbit, CameraInputRules.Classify(true, false, false, simple with { Classic = false }));
        Assert.True(CameraInputRules.IsCameraGesture(true, true, false, Defaults)); Assert.False(CameraInputRules.IsCameraGesture(false, false, false, simple));
    }

    [Fact]
    public void CtrlWithTheWheelIsThePrecisionZoom() { Assert.Equal(CameraWheel.Precision, CameraInputRules.ClassifyWheel(true)); Assert.Equal(CameraWheel.Normal, CameraInputRules.ClassifyWheel(false)); }

    [Fact]
    public void OrbitingTurnsTheViewAndKeepsThePitchWithinTheLimits()
    {
        var orbit = new CameraOrbitController(Defaults);
        orbit.Orbit(100, 0); Assert.Equal(-.3f, orbit.Yaw, 4); orbit.Orbit(-200, 0); Assert.Equal(.3f, orbit.Yaw, 4);
        orbit.Orbit(0, 100); Assert.Equal(-.3f, orbit.Pitch, 4);
        orbit.Orbit(0, 100000); Assert.Equal(Defaults.MinPitch, orbit.Pitch); orbit.Orbit(0, -1000000); Assert.Equal(Defaults.MaxPitch, orbit.Pitch);
        orbit.Yaw = 0; orbit.Orbit(100000, 0); Assert.True(float.IsFinite(orbit.Yaw)); // a full turn is allowed, there is no limit horizontally
    }

    [Fact]
    public void InvertingFlipsTheDirectionOfTheAxis()
    {
        var normal = new CameraOrbitController(Defaults); normal.Orbit(50, 50); var flipped = new CameraOrbitController(Defaults with { InvertH = true, InvertV = true }); flipped.Orbit(50, 50);
        Assert.Equal(-normal.Yaw, flipped.Yaw, 5); Assert.Equal(-normal.Pitch, flipped.Pitch, 5);
        var onlyH = new CameraOrbitController(Defaults with { InvertH = true }); Assert.Equal(normal.Pitch, onlyH.Orbit(50, 50).Pitch, 5);
    }

    [Fact]
    public void SensitivityScalesTheMovement() { var slow = new CameraOrbitController(Defaults with { SensitivityH = .001f }); var fast = new CameraOrbitController(Defaults with { SensitivityH = .006f }); slow.Orbit(100, 0); fast.Orbit(100, 0); Assert.Equal(6f, fast.Yaw / slow.Yaw, 3); }

    [Fact]
    public void TheFocusPointStaysWhereItIsWhileTheCameraGoesAround()
    {
        var orbit = new CameraOrbitController(Defaults); var focus = new Vector3(3, 1.5f, -2);
        foreach (var (dx, dy) in new[] { (0f, 0f), (250f, 0f), (250f, 120f), (-700f, -300f), (5000f, 40f) })
        {
            orbit.Orbit(dx, dy); var at = orbit.PositionFor(focus, 2.5f);
            Assert.Equal(2.5f, Vector3.Distance(at, focus), 3); // always the same distance from the focus, on any side
        }
    }

    [Fact]
    public void ZoomIsProportionalAndThePrecisionNotchIsSmaller()
    {
        var zoom = new CameraZoomController(Defaults); zoom.Set(10, true); zoom.Zoom(1, false); Assert.Equal(8.8f, zoom.Target, 3);
        var far = zoom.Target; zoom.Zoom(-1, false); Assert.Equal(far / .88f, zoom.Target, 3);
        zoom.Set(10, true); var precise = new CameraZoomController(Defaults); precise.Set(10, true); zoom.Zoom(1, false); precise.Zoom(1, true);
        Assert.True(10 - precise.Target < (10 - zoom.Target) / 3); Assert.True(precise.Target < 10);
        zoom.Set(.5f, true); zoom.Zoom(1, false); var closeStep = .5f - zoom.Target; zoom.Set(20, true); zoom.Zoom(1, false); Assert.True(20 - zoom.Target > closeStep * 10); // close up it moves by little, far away by a lot
    }

    [Fact]
    public void ZoomStaysBetweenTheLimits()
    {
        var zoom = new CameraZoomController(Defaults with { MinDistance = .3f, MaxDistance = 20 }); zoom.Set(5, true);
        for (var i = 0; i < 200; i++) zoom.Zoom(1, false); Assert.Equal(.3f, zoom.Target); for (var i = 0; i < 400; i++) zoom.Zoom(-1, false); Assert.Equal(20, zoom.Target);
        zoom.Set(float.NaN); Assert.Equal(20, zoom.Target); zoom.Set(-4); Assert.Equal(.3f, zoom.Target);
    }

    [Fact]
    public void DollyBringsTheCameraCloserWhenDraggingDown()
    {
        var zoom = new CameraZoomController(Defaults); zoom.Set(6, true); var closer = zoom.Dolly(100); Assert.True(closer < 6); var back = zoom.Dolly(-100); Assert.Equal(6, back, 3);
    }

    [Fact]
    public void TheCameraGlidesToItsTargetAndNeverOvershoots()
    {
        var zoom = new CameraZoomController(Defaults); zoom.Set(10, true); zoom.Set(2);
        var previous = zoom.Current; for (var i = 0; i < 30; i++) { var now = zoom.Update(1f / 60f); Assert.True(now <= previous && now >= 2); previous = now; }
        Assert.True(zoom.Current > 2 && zoom.Current < 10); for (var i = 0; i < 600; i++) zoom.Update(1f / 60f); Assert.Equal(2, zoom.Current, 3);
        Assert.Equal(7f, CameraZoomController.Glide(7, 1, 0, 12)); Assert.Equal(1f, CameraZoomController.Glide(7, 1, 1000, 12), 3);
    }

    [Fact]
    public void TheGlideTakesTheSameTimeAtAnyFrameRate()
    {
        float Run(float fps) { var z = new CameraZoomController(Defaults); z.Set(10, true); z.Set(2); for (var i = 0; i < fps / 2; i++) z.Update(1f / fps); return z.Current; }
        Assert.Equal(Run(30), Run(144), 2);
    }

    [Fact]
    public void PanningMovesTheFocusAlongTheScreenAndFartherAwayCoversMore()
    {
        var focus = Vector3.Zero; var right = Vector3.UnitX; var up = Vector3.UnitY;
        var near = CameraPanController.Pan(focus, right, up, 100, 0, 2); var far = CameraPanController.Pan(focus, right, up, 100, 0, 20);
        Assert.True(near.X < 0 && far.X < near.X); Assert.Equal(0, near.Y); Assert.Equal(10f, far.X / near.X, 3);
        Assert.True(CameraPanController.Pan(focus, right, up, 0, 100, 5).Y > 0); Assert.Equal(CameraPanController.Pan(focus, right, up, 100, 0, 5).X * 2, CameraPanController.Pan(focus, right, up, 100, 0, 5, 2).X, 5);
    }

    [Fact]
    public void FocusingOnSomethingSmallBringsTheCameraVeryClose()
    {
        var ring = CameraFocusManager.DistanceFor(.02f, 70, Defaults); var house = CameraFocusManager.DistanceFor(6, 70, Defaults);
        Assert.Equal(Defaults.MinDistance, ring); Assert.True(house > 8 && house <= Defaults.MaxDistance); Assert.Equal(new Vector3(1, 2, 3), CameraFocusManager.Center(new(0, 0, 0), new(2, 4, 6)));
    }

    [Fact]
    public void TheFocusGlidesToAnotherPointWithoutAJump()
    {
        var a = Vector3.Zero; var b = new Vector3(10, 0, 0); var one = CameraFocusManager.Blend(a, b, 1f / 60f, 12);
        Assert.True(one.X > 0 && one.X < 10); Assert.Equal(a, CameraFocusManager.Blend(a, b, 0, 12)); Assert.Equal(10f, CameraFocusManager.Blend(a, b, 100, 12).X, 3);
    }

    [Fact]
    public void WallsPullTheCameraCloserButNeverPastTheFocus()
    {
        Assert.Equal(5f, CameraCollisionHandler.Resolve(5, null, .15f, .3f)); Assert.Equal(2.85f, CameraCollisionHandler.Resolve(5, 3f, .15f, .3f), 4);
        Assert.Equal(.3f, CameraCollisionHandler.Resolve(5, .1f, .15f, .3f)); Assert.Equal(1f, CameraCollisionHandler.Resolve(1, 3f, .15f, 2f)); // never farther than asked, even when the minimum is bigger
    }

    [Fact]
    public void BadSettingsAreBroughtWithinLimits()
    {
        var bad = new CameraOptions { SensitivityH = 99, SensitivityV = float.NaN, ZoomSpeed = -3, PrecisionZoomSpeed = 50, Smoothing = 0, MinDistance = 9, MaxDistance = 3, MinPitch = -5, MaxPitch = 9 }.Clamped();
        Assert.True(bad.SensitivityH <= .02f && bad.SensitivityV == .003f && bad.ZoomSpeed >= .1f && bad.PrecisionZoomSpeed <= 1 && bad.Smoothing >= 1);
        Assert.True(bad.MaxDistance > bad.MinDistance && bad.MinPitch >= -1.55f && bad.MaxPitch <= 1.55f);
        Assert.Equal(new CameraOptions().SensitivityH, new CameraOptions().Clamped().SensitivityH);
    }
}

/// <summary>The detached camera: how it moves, where it may not go, and the options of this stage.</summary>
public sealed class CameraDetachedTests
{
    [Fact]
    public void ItMovesAlongTheViewAndAtTheSameSpeedInEveryDirection()
    {
        var fwd = Vector3.Normalize(new Vector3(1, 0, -1)); var right = Vector3.Normalize(new Vector3(1, 0, 1));
        var ahead = CameraDetachedRules.Move(Vector3.Zero, fwd, right, new Vector3(0, 0, 1), 8, .5f); Assert.Equal(4f, ahead.Length(), 4); Assert.True(Vector3.Dot(ahead, fwd) > 3.9f);
        var diagonal = CameraDetachedRules.Move(Vector3.Zero, fwd, right, new Vector3(1, 1, 1), 8, 1); Assert.Equal(8f, diagonal.Length(), 4); // diagonals are not faster
        Assert.Equal(Vector3.Zero, CameraDetachedRules.Move(Vector3.Zero, fwd, right, new Vector3(1, 1, 1), 8, 0)); Assert.Equal(Vector3.UnitY * 4, CameraDetachedRules.Move(Vector3.Zero, fwd, right, new Vector3(0, 1, 0), 8, .5f));
    }

    [Fact]
    public void ItCannotGoFarFromTheAvatarOffTheIslandOrIntoTheGround()
    {
        var avatar = new Vector3(10, 2, 10);
        var far = CameraDetachedRules.Constrain(new Vector3(500, 5, 10), avatar, 60, 256, 0); Assert.Equal(70f, far.X, 3); Assert.Equal(10f, far.Z, 3);
        var edge = CameraDetachedRules.Constrain(new Vector3(255, 5, 10), new Vector3(240, 2, 10), 60, 256, 0); Assert.Equal(255f, edge.X, 3); Assert.Equal(255f, CameraDetachedRules.Constrain(new Vector3(400, 5, 10), new Vector3(240, 2, 10), 300, 256, 0).X, 3);
        Assert.Equal(1.3f, CameraDetachedRules.Constrain(new Vector3(10, -9, 10), avatar, 60, 256, 1f).Y, 3); Assert.Equal(62f, CameraDetachedRules.Constrain(new Vector3(10, 900, 10), avatar, 60, 256, 0).Y, 3);
        Assert.Equal(new Vector3(12, 3, 11), CameraDetachedRules.Constrain(new Vector3(12, 3, 11), avatar, 60, 256, 0)); // a good place is left as it is
        Assert.Equal(new Vector3(300, 3, 11).X, CameraDetachedRules.Constrain(new Vector3(300, 3, 11), new Vector3(290, 2, 10), 60, 0, 0).X, 3); // unknown island size: no limit but the range
    }

    [Fact]
    public void TheNewOptionsAreKeptWithinLimits()
    {
        var o = new CameraOptions { DetachedRange = 9999, DetachedSpeed = 0 }.Clamped(); Assert.Equal(200, o.DetachedRange); Assert.Equal(1, o.DetachedSpeed); Assert.False(new CameraOptions().FocusOnDoubleClick); Assert.Equal(60, new CameraOptions { DetachedRange = float.NaN }.Clamped().DetachedRange);
    }
}
