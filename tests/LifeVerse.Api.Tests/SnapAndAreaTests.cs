using System.Numerics;
using LifeVerse.Shared.Building;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Magnetic snap, selection by area and the advanced alignment, as pure arithmetic.</summary>
public sealed class SnapAndAreaTests
{
    private static readonly SnapSettings All = new();
    private static Bounds Box(float cx, float cy, float cz, float half = .5f) => new(new Vector3(cx - half, cy - half, cz - half), new Vector3(cx + half, cy + half, cz + half));
    private static void Near(float expected, float actual, float tolerance = 1e-4f) => Assert.True(MathF.Abs(expected - actual) < tolerance, $"expected {expected} but got {actual}");

    // ---- snap points -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ABoxHasCornersEdgeMiddlesAndFaceCentresAndSettingsChooseWhichToUse()
    {
        var all = SnapMath.Features(Vector3.Zero, Quaternion.Identity, new Vector3(1, 2, 3), All); Assert.Equal(26, all.Count); Assert.Equal(8, all.Count(p => p.Kind == SnapKind.Vertex)); Assert.Equal(12, all.Count(p => p.Kind == SnapKind.Edge)); Assert.Equal(6, all.Count(p => p.Kind == SnapKind.Face));
        Assert.Contains(all, p => p.Kind == SnapKind.Vertex && p.Position == new Vector3(1, 2, 3)); Assert.Contains(all, p => p.Kind == SnapKind.Edge && p.Position == new Vector3(0, 2, 3)); Assert.Contains(all, p => p.Kind == SnapKind.Face && p.Position == new Vector3(0, 0, 3));
        Assert.Equal(8, SnapMath.Features(Vector3.Zero, Quaternion.Identity, Vector3.One, new SnapSettings(Edges: false, Faces: false)).Count); Assert.Empty(SnapMath.Features(Vector3.Zero, Quaternion.Identity, Vector3.One, new SnapSettings(false, false, false)));
        var turned = SnapMath.Features(new Vector3(10, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), new Vector3(1, 1, 3), All); Assert.Contains(turned, p => p.Kind == SnapKind.Vertex && Vector3.Distance(p.Position, new Vector3(13, 1, 1)) < 1e-4f); // its long side now points along X
    }

    [Fact]
    public void ACornerIsPulledOntoACornerOfAnotherBoxWhenCloseToThePathOfTheDrag()
    {
        var dragged = SnapMath.Features(Vector3.Zero, Quaternion.Identity, new Vector3(.5f), All); var target = SnapMath.Features(new Vector3(6, 0, 0), Quaternion.Identity, new Vector3(.5f), All);
        var near = SnapMath.AlongAxis(dragged, target, Vector3.UnitX, 5.1f, .3f); Assert.NotNull(near); Near(5f, near!.Value.Amount); Assert.Equal(SnapKind.Vertex, near.Value.Kind); // face against face, corner on corner
        Assert.Null(SnapMath.AlongAxis(dragged, target, Vector3.UnitX, 3f, .3f)); // too far along the path
        Assert.Null(SnapMath.AlongAxis(dragged, SnapMath.Features(new Vector3(6, 2, 0), Quaternion.Identity, new Vector3(.5f), All), Vector3.UnitX, 5.1f, .3f)); // not close to the line it moves on
        Assert.Null(SnapMath.AlongAxis(dragged, [], Vector3.UnitX, 5, .3f)); Assert.Null(SnapMath.AlongAxis([], target, Vector3.UnitX, 5, .3f));
        var back = SnapMath.AlongAxis(dragged, SnapMath.Features(new Vector3(-6, 0, 0), Quaternion.Identity, new Vector3(.5f), All), -Vector3.UnitX, 5.1f, .3f); Near(5f, back!.Value.Amount); // the other way round (the amount is along the axis it is given)
    }

    [Fact]
    public void WhenOnlyEdgeMiddlesOrFaceCentresAreInUseThoseAreWhatSnaps()
    {
        var dragged = SnapMath.Features(Vector3.Zero, Quaternion.Identity, new Vector3(.5f), new SnapSettings(false, false, true)); var target = SnapMath.Features(new Vector3(6, 0, 0), Quaternion.Identity, new Vector3(.5f), new SnapSettings(false, false, true));
        var result = SnapMath.AlongAxis(dragged, target, Vector3.UnitX, 4.9f, .3f); Near(5f, result!.Value.Amount); Assert.Equal(SnapKind.Face, result.Value.Kind);
    }

    [Fact]
    public void AFaceSnapsToTouchTheFaceOfAnotherBoxButOnlyWhenTheyFaceEachOther()
    {
        var target = new[] { new Bounds(new Vector3(6, -.5f, -.5f), new Vector3(7, .5f, .5f)) };
        var touch = SnapMath.Contact(Box(0, 0, 0), target, 0, 5.3f, .3f); Assert.NotNull(touch); Near(5.5f, touch!.Value.Amount); Assert.Equal(SnapKind.Contact, touch.Value.Kind); // its right face on the target's left face
        Assert.Null(SnapMath.Contact(Box(0, 0, 0), target, 0, 3f, .3f)); // too far
        Assert.Null(SnapMath.Contact(Box(0, 0, 0), [new Bounds(new Vector3(6, 3, -.5f), new Vector3(7, 4, .5f))], 0, 5.3f, .3f)); // beside the path: it would never touch
        var flush = SnapMath.Contact(Box(0, 0, 0), [new Bounds(new Vector3(5.8f, -.5f, -.5f), new Vector3(6.8f, .5f, .5f))], 0, 6.2f, .3f); Assert.NotNull(flush); // flush with the far side: the right faces share a plane
        Near(6.3f, flush!.Value.Amount); var above = SnapMath.Contact(Box(0, 0, 0), [new Bounds(new Vector3(-.5f, 2, -.5f), new Vector3(.5f, 3, .5f))], 1, 1.4f, .3f); Near(1.5f, above!.Value.Amount); // stacking on the Y axis
    }

    // ---- selection by area -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void ARectangleOnTheScreenChoosesWhatIsInsideItOrWhatItCrosses()
    {
        var objects = new ScreenRect?[] { new(10, 10, 20, 20), new(90, 10, 130, 20), new(300, 300, 320, 320), null, new(0, 0, 400, 400) };
        var area = ScreenRect.Between(new Vector2(100, 100), new Vector2(0, 0)); // drawn from the bottom right to the top left: the same rectangle
        Assert.Equal([0], AreaSelect.InRectangle(objects, area, crossing: false)); Assert.Equal([0, 1, 4], AreaSelect.InRectangle(objects, area, crossing: true)); // behind the camera is never chosen
        Assert.Empty(AreaSelect.InRectangle([], area, false)); Assert.Equal((100f, 100f), (area.Width, area.Height));
        Assert.Null(AreaSelect.RectangleOf([new Vector2(1, 1), null])); var rect = AreaSelect.RectangleOf([new Vector2(5, 9), new Vector2(2, 20), new Vector2(7, 3)]); Assert.Equal(new ScreenRect(2, 3, 7, 20), rect); Assert.Null(AreaSelect.RectangleOf([]));
    }

    [Fact]
    public void AnAreaOfTheWorldChoosesWhatIsInsideOrTouching()
    {
        var objects = new[] { Box(0, 0, 0), Box(4.8f, 0, 0), Box(20, 0, 0), Box(0, 9, 0) }; var volume = AreaSelect.VolumeBetween(new Vector3(5, 5, 5), new Vector3(-5, -5, -5));
        Assert.Equal([0], AreaSelect.InVolume(objects, volume, touching: false)); Assert.Equal([0, 1], AreaSelect.InVolume(objects, volume, touching: true)); Assert.Equal(new Bounds(new Vector3(-5), new Vector3(5)), volume);
    }

    // ---- advanced alignment ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void AlignmentToOneChosenObjectLeavesThatObjectWhereItIs()
    {
        var boxes = new[] { Box(0, 0, 0), Box(5, 0, 0, 1f), Box(9, 0, 0, .25f) };
        Assert.Equal([5f, 0f, -4f], AlignMath.AlignTo(boxes, 1, AlignAxis.X, AlignEdge.Center)); Assert.Equal([4.5f, 0f, -4.75f], AlignMath.AlignTo(boxes, 1, AlignAxis.X, AlignEdge.Min).Select(x => MathF.Round(x, 3)).ToArray()); Assert.Equal(new float[3], AlignMath.AlignTo(boxes, 7, AlignAxis.X, AlignEdge.Min));
        Assert.Equal(0, AlignMath.PickReference(boxes, AlignAxis.X, "first")); Assert.Equal(2, AlignMath.PickReference(boxes, AlignAxis.X, "last")); Assert.Equal(1, AlignMath.PickReference(boxes, AlignAxis.X, "largest")); Assert.Equal(2, AlignMath.PickReference(boxes, AlignAxis.X, "smallest")); Assert.Equal(-1, AlignMath.PickReference(boxes, AlignAxis.X, "selection")); Assert.Equal(-1, AlignMath.PickReference([], AlignAxis.X, "largest"));
    }

    [Fact]
    public void ObjectsCanBeSpreadWithEqualCentresOrAFixedGap()
    {
        var boxes = new[] { Box(0, 0, 0), Box(1, 0, 0, 1f), Box(10, 0, 0, .25f) };
        var centres = AlignMath.DistributeCenters(boxes, AlignAxis.X); Assert.Equal([0f, 4f, 0f], centres); // the ones at the ends stay; the middle one goes to the middle
        var gap = AlignMath.DistributeWithGap(boxes, AlignAxis.X, 2f); var placed = Enumerable.Range(0, 3).Select(i => new Bounds(boxes[i].Min + new Vector3(gap[i], 0, 0), boxes[i].Max + new Vector3(gap[i], 0, 0))).OrderBy(b => b.Min.X).ToList();
        Near(2f, placed[1].Min.X - placed[0].Max.X); Near(2f, placed[2].Min.X - placed[1].Max.X); Assert.Equal(0f, gap[0]); // the first stays; two metres between neighbours
        Assert.Equal(new float[2], AlignMath.DistributeWithGap([Box(0, 0, 0), Box(3, 0, 0)], AlignAxis.X, 2.5f).Take(1).Concat([0f])); Assert.Equal(new float[2], AlignMath.DistributeCenters([Box(0, 0, 0), Box(3, 0, 0)], AlignAxis.X)); Assert.Empty(AlignMath.AlignTo([], 0, AlignAxis.X, AlignEdge.Min));
        var overlap = AlignMath.DistributeWithGap([Box(0, 0, 0), Box(5, 0, 0)], AlignAxis.X, -.25f); Near(-4.25f, overlap[1]); // a negative gap makes them overlap a little
    }
}
