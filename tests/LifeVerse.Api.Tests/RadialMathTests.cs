using System.Numerics;
using LifeVerse.Shared.Gameplay;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class RadialMathTests
{
    private const float In = 80, Out = 190;

    [Theory]
    [InlineData(0f, -120f, 0)]    // straight up: the first slice
    [InlineData(120f, -10f, 2)]   // to the right: the third of eight
    [InlineData(0f, 120f, 4)]     // straight down
    [InlineData(-120f, 0f, 6)]    // to the left
    [InlineData(-90f, -90f, 7)]   // up and to the left
    [InlineData(90f, -90f, 1)]    // up and to the right
    public void SliceAt_ReadsTheDirectionClockwiseFromTheTop(float x, float y, int expected) => Assert.Equal(expected, RadialMath.SliceAt(new Vector2(x, y), 8, In, Out));

    [Fact]
    public void SliceAt_IsMinusOneInTheCentreAndOutsideTheRing()
    {
        Assert.Equal(-1, RadialMath.SliceAt(new Vector2(10, 10), 8, In, Out));
        Assert.Equal(-1, RadialMath.SliceAt(new Vector2(0, -250), 8, In, Out));
        Assert.Equal(-1, RadialMath.SliceAt(Vector2.Zero, 0, In, Out));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(5)] [InlineData(7)] [InlineData(8)]
    public void EverySliceIsFoundAtItsOwnLabel(int count)
    {
        for (var i = 0; i < count; i++)
            Assert.Equal(i, RadialMath.SliceAt(RadialMath.LabelPosition(i, count, 135), count, In, Out));
    }

    [Fact]
    public void ASingleSliceCoversTheWholeRing() =>
        Assert.All(new[] { new Vector2(0, -150), new Vector2(150, 0), new Vector2(0, 150), new Vector2(-150, 0) }, p => Assert.Equal(0, RadialMath.SliceAt(p, 1, In, Out)));

    [Fact]
    public void LabelPositions_StartAtTheTopAndGoClockwise()
    {
        var top = RadialMath.LabelPosition(0, 8, 100); var right = RadialMath.LabelPosition(2, 8, 100);
        Assert.Equal(0f, top.X, 3); Assert.Equal(-100f, top.Y, 3);
        Assert.Equal(100f, right.X, 3); Assert.Equal(0f, right.Y, 3);
    }

    [Fact]
    public void KeepOnScreen_PullsTheWholeCircleInsideTheWindow()
    {
        var screen = new Vector2(1600, 900);
        Assert.Equal(new Vector2(800, 450), RadialMath.KeepOnScreen(new Vector2(800, 450), 200, screen));
        var corner = RadialMath.KeepOnScreen(new Vector2(10, 10), 200, screen); Assert.Equal(new Vector2(208, 208), corner);
        var far = RadialMath.KeepOnScreen(new Vector2(1590, 890), 200, screen); Assert.Equal(new Vector2(1392, 692), far);
        Assert.Equal(new Vector2(208, 208), RadialMath.KeepOnScreen(new Vector2(500, 500), 200, new Vector2(300, 300))); // a window smaller than the circle
    }

    [Fact]
    public void Pages_SplitLongListsAndKeepEverything()
    {
        Assert.Single(RadialMath.Pages(Enumerable.Range(0, 8).ToList()));
        var pages = RadialMath.Pages(Enumerable.Range(0, 12).ToList());
        Assert.Equal(2, pages.Count); Assert.Equal(7, pages[0].Count); Assert.Equal(5, pages[1].Count);
        Assert.Equal(Enumerable.Range(0, 12), pages.SelectMany(x => x));
        var many = RadialMath.Pages(Enumerable.Range(0, 30).ToList());
        Assert.All(many, p => Assert.InRange(p.Count, 1, 8)); Assert.Equal(30, many.Sum(p => p.Count) );
    }

    [Fact]
    public void Keys_AndArrowStepping()
    {
        Assert.Equal(0, RadialMath.SliceForKey(1, 5)); Assert.Equal(4, RadialMath.SliceForKey(5, 5)); Assert.Equal(-1, RadialMath.SliceForKey(6, 5)); Assert.Equal(-1, RadialMath.SliceForKey(0, 5));
        Assert.Equal(0, RadialMath.Step(-1, 1, 6)); Assert.Equal(5, RadialMath.Step(-1, -1, 6));
        Assert.Equal(1, RadialMath.Step(0, 1, 6)); Assert.Equal(5, RadialMath.Step(0, -1, 6)); Assert.Equal(0, RadialMath.Step(5, 1, 6)); Assert.Equal(-1, RadialMath.Step(0, 1, 0));
    }
}

public sealed class RayMathTests
{
    [Fact]
    public void ARayThroughAnAvatarPassesAtDistanceZero()
    {
        var feet = new Vector3(0, 0, -10); var head = feet + new Vector3(0, 1.8f, 0);
        var distance = RayMath.DistanceToSegment(new Vector3(0, 1, 0), -Vector3.UnitZ, feet, head, 80, out var along);
        Assert.Equal(0f, distance, 3); Assert.Equal(10f, along, 3);
    }

    [Fact]
    public void ARayBesideOrBehindOrTooShortDoesNotMatch()
    {
        var feet = new Vector3(0, 0, -10); var head = feet + new Vector3(0, 1.8f, 0);
        Assert.True(RayMath.DistanceToSegment(new Vector3(2, 1, 0), -Vector3.UnitZ, feet, head, 80, out _) > 1.9f); // two metres to the side
        Assert.True(RayMath.DistanceToSegment(new Vector3(0, 1, 0), Vector3.UnitZ, feet, head, 80, out _) > 9f);    // looking the other way
        Assert.True(RayMath.DistanceToSegment(new Vector3(0, 1, 0), -Vector3.UnitZ, feet, head, 5, out _) > 4.9f);  // the ray stops short
        Assert.True(RayMath.DistanceToSegment(new Vector3(0, 3.5f, 0), -Vector3.UnitZ, feet, head, 80, out _) > 1.6f); // above the head
    }

    [Fact]
    public void ADownwardRayAtTheFeetMatchesToo() =>
        Assert.True(RayMath.DistanceToSegment(new Vector3(0, 5, -9), new Vector3(0, -1, -.1f), new Vector3(0, 0, -10), new Vector3(0, 1.8f, -10), 80, out _) < .6f);
}
