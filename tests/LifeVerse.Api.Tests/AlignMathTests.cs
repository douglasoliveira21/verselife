using System.Numerics;
using LifeVerse.Shared.Building;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class AlignMathTests
{
    private static Bounds Box(float x, float width) => new(new Vector3(x - width / 2, 0, 0), new Vector3(x + width / 2, 1, 1));

    [Fact]
    public void Align_Min_LinesUpTheLowSides()
    {
        var moves = AlignMath.Align([Box(0, 2), Box(5, 4), Box(9, 1)], AlignAxis.X, AlignEdge.Min);
        Assert.Equal(0f, moves[0], 3);
        Assert.Equal(-1f - 3f, moves[1], 3);     // low side of the second box is 3, moves to -1
        Assert.Equal(-1f - 8.5f, moves[2], 3);   // low side of the third box is 8.5
    }

    [Fact]
    public void Align_Max_And_Center()
    {
        var boxes = new[] { Box(0, 2), Box(5, 4), Box(9, 1) };
        var max = AlignMath.Align(boxes, AlignAxis.X, AlignEdge.Max);
        Assert.Equal(new[] { 9.5f - 1f, 9.5f - 7f, 0f }, max);
        var center = AlignMath.Align(boxes, AlignAxis.X, AlignEdge.Center);
        var target = (-1f + 9.5f) / 2f;
        Assert.Equal(new[] { target - 0f, target - 5f, target - 9f }, center);
    }

    [Fact]
    public void Align_AfterApplyingTheMoves_TheEdgesCoincide()
    {
        var boxes = new[] { Box(2, 2), Box(-3, 5), Box(8, 1) };
        var moves = AlignMath.Align(boxes, AlignAxis.X, AlignEdge.Max);
        var edges = boxes.Select((b, i) => b.Max.X + moves[i]).ToArray();
        Assert.All(edges, e => Assert.Equal(edges[0], e, 3));
    }

    [Fact]
    public void Distribute_MakesTheGapsEqualAndKeepsTheOuterOnesStill()
    {
        var boxes = new[] { Box(0, 2), Box(2.5f, 2), Box(10, 2), Box(7, 2) }; // unsorted on purpose
        var moves = AlignMath.Distribute(boxes, AlignAxis.X);
        Assert.Equal(0f, moves[0]); Assert.Equal(0f, moves[2]);
        var placed = boxes.Select((b, i) => new Bounds(b.Min + new Vector3(moves[i], 0, 0), b.Max + new Vector3(moves[i], 0, 0))).OrderBy(b => b.Min.X).ToArray();
        var gaps = Enumerable.Range(0, 3).Select(i => placed[i + 1].Min.X - placed[i].Max.X).ToArray();
        Assert.All(gaps, g => Assert.Equal(gaps[0], g, 3));
        Assert.True(gaps[0] > 0);
    }

    [Fact]
    public void Distribute_NeedsThreeObjects() => Assert.All(AlignMath.Distribute([Box(0, 1), Box(5, 1)], AlignAxis.X), x => Assert.Equal(0f, x));

    [Fact]
    public void RestOnGround_MovesTheLowestPointToTheGround()
    {
        var boxes = new[] { new Bounds(new Vector3(0, 2, 0), new Vector3(1, 3, 1)), new Bounds(new Vector3(0, 1.5f, 0), new Vector3(1, 4, 1)) };
        Assert.Equal(-.5f, AlignMath.RestOnGround(boxes, 1f), 3);
        Assert.Equal(0f, AlignMath.RestOnGround([], 1f));
    }

    [Theory]
    [InlineData(1.3f, .5f, 1.5f)] [InlineData(-0.3f, .5f, -.5f)] [InlineData(7.77f, 0f, 7.77f)] [InlineData(2.25f, .5f, 2.5f)]
    public void SnapToGrid_RoundsToTheNearestStep(float value, float size, float expected) => Assert.Equal(expected, AlignMath.SnapToGrid(value, size), 3);
}
