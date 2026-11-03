using System.Numerics;
using LifeVerse.Shared.Building;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class OrbitMathTests
{
    [Fact]
    public void Position_IsAtTheRequestedDistanceInTheRequestedDirection()
    {
        var target = new Vector3(3, 2, -4);
        foreach (var (yaw, pitch, distance) in new[] { (0f, 0f, 5f), (1.2f, -.4f, 12f), (-2.5f, .8f, 1f) })
            Assert.Equal(distance, Vector3.Distance(OrbitMath.Position(target, yaw, pitch, distance), target), 3);
        // yaw 0, no pitch: straight behind (+Z) at the same height; looking down puts the camera above
        Assert.Equal(new Vector3(3, 2, 1), OrbitMath.Position(target, 0, 0, 5));
        Assert.True(OrbitMath.Position(target, 0, -.7f, 5).Y > target.Y);
        Assert.True(OrbitMath.Position(target, 0, .7f, 5).Y < target.Y);
        Assert.Equal(1f, OrbitMath.Position(Vector3.Zero, MathF.PI / 2, 0, 1).X, 3);
    }

    [Fact]
    public void Pitch_IsClampedSoTheCameraNeverFlips() =>
        Assert.Equal(OrbitMath.Position(Vector3.Zero, 0, OrbitMath.MaxPitch, 4), OrbitMath.Position(Vector3.Zero, 0, 9f, 4));

    [Fact]
    public void FocusDistance_GrowsWithTheObjectAndStaysInRange()
    {
        Assert.True(OrbitMath.FocusDistance(10, 70) > OrbitMath.FocusDistance(1, 70));
        Assert.True(OrbitMath.FocusDistance(1, 30) > OrbitMath.FocusDistance(1, 90)); // a narrow lens has to stand farther back
        Assert.Equal(OrbitMath.MinDistance, OrbitMath.FocusDistance(0, 179));
        Assert.Equal(OrbitMath.MaxDistance, OrbitMath.FocusDistance(100000, 40));
    }

    [Fact]
    public void Zoom_StepsByAFractionAndIsClamped()
    {
        Assert.True(OrbitMath.Zoom(10, 1) < 10 && OrbitMath.Zoom(10, -1) > 10);
        Assert.Equal(OrbitMath.Zoom(10, 1) / 10, OrbitMath.Zoom(100, 1) / 100, 4); // the same feel at every distance
        Assert.Equal(OrbitMath.MinDistance, OrbitMath.Zoom(1, 100)); Assert.Equal(OrbitMath.MaxDistance, OrbitMath.Zoom(100, -100));
    }

    [Fact]
    public void Pan_MovesTheTargetAlongTheCameraPlaneMoreWhenFarAway()
    {
        var near = OrbitMath.Pan(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 100, 0, 5); var far = OrbitMath.Pan(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 100, 0, 50);
        Assert.True(near.X < 0 && far.X < near.X); // dragging right pulls the scene right, so the target moves left
        Assert.Equal(0, near.Y); Assert.Equal(0, near.Z);
        Assert.True(OrbitMath.Pan(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0, 100, 5).Y > 0);
    }
}

public sealed class BuildTutorialTests
{
    [Fact]
    public void StepsFinishOnlyInOrder()
    {
        var tutorial = new BuildTutorial();
        Assert.True(tutorial.Active); Assert.Equal(TutorialStep.Create, tutorial.Current);
        Assert.False(tutorial.Notify(TutorialStep.Move)); // too early
        Assert.True(tutorial.Notify(TutorialStep.Create)); Assert.Equal(TutorialStep.Move, tutorial.Current);
        Assert.False(tutorial.Notify(TutorialStep.Create)); // already done
        foreach (var step in new[] { TutorialStep.Move, TutorialStep.Resize, TutorialStep.Rotate, TutorialStep.Texture, TutorialStep.Duplicate, TutorialStep.Link, TutorialStep.Save })
            Assert.True(tutorial.Notify(step), step.ToString());
        Assert.True(tutorial.Finished); Assert.False(tutorial.Active); Assert.False(tutorial.Notify(TutorialStep.Create));
    }

    [Fact]
    public void CanBeSkippedAndRestarted()
    {
        var tutorial = new BuildTutorial();
        tutorial.Notify(TutorialStep.Create); tutorial.Skip();
        Assert.False(tutorial.Active); Assert.False(tutorial.Notify(TutorialStep.Move));
        tutorial.Restart();
        Assert.True(tutorial.Active); Assert.Equal(TutorialStep.Create, tutorial.Current);
    }

    [Fact]
    public void EveryStepHasPortugueseTextWithItsNumber()
    {
        for (var i = 0; i < BuildTutorial.Steps; i++) Assert.StartsWith($"{i + 1}/8", BuildTutorial.Text((TutorialStep)i));
        Assert.Contains("concluído", BuildTutorial.Text(TutorialStep.Done));
        Assert.Equal(BuildTutorial.Steps, (int)TutorialStep.Done);
    }
}
