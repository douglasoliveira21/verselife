using System.Numerics;
using LifeVerse.Shared.Building;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The arithmetic of the editor's precise transforms, mirror and multiple duplication, without the engine.</summary>
public sealed class TransformMathTests
{
    private static readonly Quaternion QuarterY = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
    private static Pose At(float x, float y, float z, Quaternion? rotation = null, float scale = 1) => new(new Vector3(x, y, z), rotation ?? Quaternion.Identity, new Vector3(scale));

    private static void Near(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) => Assert.True(Vector3.Distance(expected, actual) < tolerance, $"expected {expected} but got {actual}");

    [Fact]
    public void AMoveGoesAlongTheWorldAxesOrAlongTheAxesOfTheReferenceObject()
    {
        Near(new Vector3(1, 2, 3), TransformMath.MoveBy(At(0, 0, 0), new Vector3(1, 2, 3), AxisSpace.World, QuarterY).Position);
        Near(new Vector3(0, 0, -1), TransformMath.MoveBy(At(0, 0, 0), new Vector3(1, 0, 0), AxisSpace.Local, QuarterY).Position); // its X points to the world's -Z
        Near(new Vector3(5, 1, 0), TransformMath.MoveBy(At(5, 0, 0), new Vector3(0, 1, 0), AxisSpace.Local, QuarterY).Position); // Y is the same in both
        Near(Vector3.Zero, TransformMath.FromWorld(TransformMath.ToWorld(Vector3.Zero, AxisSpace.Local, QuarterY), AxisSpace.Local, QuarterY));
        var back = TransformMath.FromWorld(TransformMath.ToWorld(new Vector3(1, 2, 3), AxisSpace.Local, QuarterY), AxisSpace.Local, QuarterY); Near(new Vector3(1, 2, 3), back);
    }

    [Fact]
    public void ATurnAroundAPivotMovesTheCentreAndTurnsTheObject()
    {
        var turned = TransformMath.RotateAbout(At(3, 0, 0), new Vector3(2, 0, 0), 1, 90, AxisSpace.World, Quaternion.Identity); Near(new Vector3(2, 0, -1), turned.Position); // one metre from the pivot, a quarter turn
        Assert.True(Quaternion.Dot(turned.Rotation, QuarterY) > .9999f);
        Near(new Vector3(1, 0, 0), TransformMath.RotateAbout(At(1, 0, 0), new Vector3(1, 0, 0), 1, 45, AxisSpace.World, Quaternion.Identity).Position); // the pivot itself does not move
        var local = TransformMath.RotateAbout(At(0, 0, 0), new Vector3(0, 0, 0), 0, 90, AxisSpace.Local, QuarterY); // about the frame's X = the world's -Z
        Assert.True(Quaternion.Dot(local.Rotation, Quaternion.CreateFromAxisAngle(-Vector3.UnitZ, MathF.PI / 2)) > .9999f);
        var full = TransformMath.RotateAbout(At(2, 1, 0), Vector3.Zero, 2, 360, AxisSpace.World, Quaternion.Identity); Near(new Vector3(2, 1, 0), full.Position);
    }

    [Fact]
    public void AStretchAroundAPivotMovesTheCentreAndGrowsTheSize()
    {
        Assert.True(TransformMath.TryScaleAbout(At(4, 0, 0), new Vector3(2, 0, 0), new Vector3(2, 2, 2), AxisSpace.World, Quaternion.Identity, out var uniform, out _)); Near(new Vector3(6, 0, 0), uniform.Position); Near(new Vector3(2), uniform.Scale);
        Assert.True(TransformMath.TryScaleAbout(At(3, 0, 0), Vector3.Zero, new Vector3(2, 1, 3), AxisSpace.World, Quaternion.Identity, out var axes, out _)); Near(new Vector3(6, 0, 0), axes.Position); Near(new Vector3(2, 1, 3), axes.Scale);
        // an object turned a quarter around Y has its own X pointing along the world's Z: stretching Z by 3 makes ITS X three times longer
        Assert.True(TransformMath.TryScaleAbout(At(0, 0, 0, QuarterY), Vector3.Zero, new Vector3(1, 1, 3), AxisSpace.World, Quaternion.Identity, out var swapped, out _)); Near(new Vector3(3, 1, 1), swapped.Scale);
        Assert.True(TransformMath.TryScaleAbout(At(0, 0, -1), Vector3.Zero, new Vector3(2, 1, 1), AxisSpace.Local, QuarterY, out var local, out _)); Near(new Vector3(0, 0, -2), local.Position); // on the frame's X axis
    }

    [Fact]
    public void AStretchThatWouldShearATurnedObjectIsRefusedUnlessItIsUniform()
    {
        var off = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 30 * MathF.PI / 180);
        Assert.False(TransformMath.TryScaleAbout(At(0, 0, 0, off), Vector3.Zero, new Vector3(2, 1, 1), AxisSpace.World, Quaternion.Identity, out _, out var problem)); Assert.Equal("needs_alignment", problem);
        Assert.True(TransformMath.TryScaleAbout(At(0, 0, 0, off), Vector3.Zero, new Vector3(2, 2, 2), AxisSpace.World, Quaternion.Identity, out var ok, out _)); Near(new Vector3(2), ok.Scale);
        Assert.True(TransformMath.TryScaleAbout(At(0, 0, 0, off), Vector3.Zero, new Vector3(2, 1, 1), AxisSpace.Local, off, out _, out _)); // aligned to the frame it is stretched in
        foreach (var bad in new[] { new Vector3(0, 1, 1), new Vector3(-1, 1, 1), new Vector3(float.NaN, 1, 1), new Vector3(float.PositiveInfinity, 1, 1) }) { Assert.False(TransformMath.TryScaleAbout(At(0, 0, 0), Vector3.Zero, bad, AxisSpace.World, Quaternion.Identity, out _, out var why)); Assert.Equal("bad_factor", why); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AMirrorReflectsTheCentreAndNeverTurnsInsideOut(int axis)
    {
        var pose = At(3, 2, -1, Quaternion.CreateFromYawPitchRoll(.5f, .3f, .2f)); var pivot = new Vector3(1, 1, 1); var mirrored = TransformMath.Mirror(pose, pivot, axis);
        var expected = pose.Position - pivot; expected = axis switch { 0 => new Vector3(-expected.X, expected.Y, expected.Z), 1 => new Vector3(expected.X, -expected.Y, expected.Z), _ => new Vector3(expected.X, expected.Y, -expected.Z) }; Near(pivot + expected, mirrored.Position);
        Assert.True(Math.Abs(mirrored.Rotation.Length() - 1) < 1e-4f); Assert.Equal(pose.Scale, mirrored.Scale);
        var m2 = TransformMath.Mirror(mirrored, pivot, axis); Near(pose.Position, m2.Position); // mirrored twice, it is back
        foreach (var local in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ }) Assert.True(MathF.Abs(Vector3.Dot(Vector3.Transform(local, pose.Rotation), Vector3.Transform(local, m2.Rotation))) > .999f, "the same shape: each axis is the same up to a flip");
    }

    [Fact]
    public void AMirrorOfATurnedBoxIsTheBoxTurnedTheOtherWay()
    {
        var turned = At(2, 0, 0, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 30 * MathF.PI / 180)); var mirrored = TransformMath.Mirror(turned, Vector3.Zero, 0);
        Near(new Vector3(-2, 0, 0), mirrored.Position); Assert.True(Quaternion.Dot(mirrored.Rotation, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -30 * MathF.PI / 180)) > .9999f);
        Assert.True(Quaternion.Dot(TransformMath.Mirror(At(1, 0, 0), Vector3.Zero, 0).Rotation, Quaternion.Identity) > .9999f); // an unturned box mirrored is an unturned box
    }

    [Fact]
    public void AnObjectWithItsOwnPivotTurnsAroundThatPoint()
    {
        var pose = At(10, 0, 0, QuarterY); Near(new Vector3(10, 0, -1), TransformMath.PivotOf(pose, new Vector3(1, 0, 0))); // the offset turns with the object
        Near(pose.Position, TransformMath.PivotOf(pose, Vector3.Zero));
    }

    // ---- multiple duplication -------------------------------------------------------------------------------------------------------

    [Fact]
    public void ALineOfCopiesStepsTheSameWayEachTimeAndCanTurnAndGrowAsItGoes()
    {
        var plain = ArrayMath.Copies(new ArraySpec(ArrayKind.Line, 3, 1, 1, new Vector3(2, 0, 0), Vector3.Zero, Vector3.One), Vector3.Zero, AxisSpace.World, Quaternion.Identity);
        Assert.Equal([2f, 4f, 6f], plain.Select(c => c(At(0, 0, 0)).Position.X)); Assert.All(plain, c => Near(Vector3.One, c(At(0, 0, 0)).Scale));
        var growing = ArrayMath.Copies(new ArraySpec(ArrayKind.Line, 3, 1, 1, Vector3.Zero, Vector3.Zero, new Vector3(1.5f)), Vector3.Zero, AxisSpace.World, Quaternion.Identity);
        Assert.Equal([1.5f, 2.25f, 3.375f], growing.Select(c => MathF.Round(c(At(0, 0, 0)).Scale.X, 4))); // each one 50 % bigger than the one before
        var turning = ArrayMath.Copies(new ArraySpec(ArrayKind.Line, 2, 1, 1, Vector3.Zero, new Vector3(0, 90, 0), Vector3.One), Vector3.Zero, AxisSpace.World, Quaternion.Identity);
        Near(new Vector3(0, 0, -1), turning[0](At(1, 0, 0)).Position); Near(new Vector3(-1, 0, 0), turning[1](At(1, 0, 0)).Position); // a quarter turn more each time, around the pivot
        var local = ArrayMath.Copies(new ArraySpec(ArrayKind.Line, 2, 1, 1, new Vector3(1, 0, 0), Vector3.Zero, Vector3.One), Vector3.Zero, AxisSpace.Local, QuarterY); Near(new Vector3(0, 0, -2), local[1](At(0, 0, 0)).Position);
    }

    [Fact]
    public void AGridHasOneCopyForEveryPlaceExceptTheOriginal()
    {
        var copies = ArrayMath.Copies(new ArraySpec(ArrayKind.Grid, 2, 2, 2, new Vector3(1, 2, 3), Vector3.Zero, Vector3.One), Vector3.Zero, AxisSpace.World, Quaternion.Identity);
        Assert.Equal(7, copies.Count); var places = copies.Select(c => c(At(0, 0, 0)).Position).ToHashSet(); Assert.Equal(7, places.Count); Assert.DoesNotContain(Vector3.Zero, places); Assert.Contains(new Vector3(1, 2, 3), places); Assert.Contains(new Vector3(0, 0, 3), places);
        Assert.Equal(5, ArrayMath.Copies(new ArraySpec(ArrayKind.Grid, 3, 2, 1, Vector3.One, Vector3.Zero, Vector3.One), Vector3.Zero, AxisSpace.World, Quaternion.Identity).Count);
    }

    [Fact]
    public void ACircleSpacesTheObjectsEvenlyAroundThePivot()
    {
        var circle = ArrayMath.Copies(new ArraySpec(ArrayKind.Circle, 4, 1, 1, Vector3.Zero, Vector3.Zero, Vector3.One, CircleAxis: 1), Vector3.Zero, AxisSpace.World, Quaternion.Identity); Assert.Equal(3, circle.Count); // four in all, the original is one
        Near(new Vector3(0, 0, -1), circle[0](At(1, 0, 0)).Position); Near(new Vector3(-1, 0, 0), circle[1](At(1, 0, 0)).Position); Near(new Vector3(0, 0, 1), circle[2](At(1, 0, 0)).Position);
        Assert.True(Quaternion.Dot(circle[0](At(1, 0, 0)).Rotation, QuarterY) > .9999f); // facing the centre as it goes round
        var still = ArrayMath.Copies(new ArraySpec(ArrayKind.Circle, 4, 1, 1, Vector3.Zero, Vector3.Zero, Vector3.One, FaceCenter: false), Vector3.Zero, AxisSpace.World, Quaternion.Identity); Assert.True(Quaternion.Dot(still[0](At(1, 0, 0)).Rotation, Quaternion.Identity) > .9999f);
        var offCentre = ArrayMath.Copies(new ArraySpec(ArrayKind.Circle, 2, 1, 1, Vector3.Zero, Vector3.Zero, Vector3.One), new Vector3(5, 0, 0), AxisSpace.World, Quaternion.Identity); Near(new Vector3(9, 0, 0), offCentre[0](At(1, 0, 0)).Position); // half a turn around (5,0,0)
    }

    [Theory]
    [InlineData(ArrayKind.Line, 0, 1, 1)]
    [InlineData(ArrayKind.Line, 201, 1, 1)]
    [InlineData(ArrayKind.Line, -3, 1, 1)]
    [InlineData(ArrayKind.Grid, 1, 1, 1)]
    [InlineData(ArrayKind.Grid, 0, 5, 5)]
    [InlineData(ArrayKind.Grid, 10, 10, 10)]
    [InlineData(ArrayKind.Circle, 1, 1, 1)]
    [InlineData(ArrayKind.Circle, 202, 1, 1)]
    public void NumbersThatAskForNothingOrTooMuchMakeNoCopies(ArrayKind kind, int x, int y, int z)
    {
        var spec = new ArraySpec(kind, x, y, z, Vector3.One, Vector3.Zero, Vector3.One); Assert.Equal(-1, spec.CopyCount); Assert.Empty(ArrayMath.Copies(spec, Vector3.Zero, AxisSpace.World, Quaternion.Identity));
        Assert.Equal(ArraySpec.MaxCopies, new ArraySpec(ArrayKind.Line, ArraySpec.MaxCopies, 1, 1, Vector3.One, Vector3.Zero, Vector3.One).CopyCount);
    }
}
