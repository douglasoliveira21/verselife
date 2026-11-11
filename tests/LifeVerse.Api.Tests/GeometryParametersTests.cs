using LifeVerse.Shared.Building;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class GeometryParametersTests
{
    [Fact]
    public void TheTwistOfTheBaseIsReadAndKeptInsideItsRange()
    {
        Assert.Equal(45f, PrimParams.Parse("{\"twistBegin\":45}").TwistBegin);
        Assert.Equal(180f, PrimParams.Parse("{\"twistBegin\":900}").TwistBegin);
        Assert.Equal(0f, PrimParams.Parse("{}").TwistBegin);
    }

    [Fact]
    public void ATwistThatStartsAtTheBaseTurnsTheWholeShapeInsteadOfBendingIt()
    {
        var plain = PrimMesh.Build("prism", new PrimParams());
        var spun = PrimMesh.Build("prism", new PrimParams(Twist: 90, TwistBegin: 90)); // the same turn at both ends: one piece, rotated
        var bent = PrimMesh.Build("prism", new PrimParams(Twist: 90));                 // turning from nothing at the base to 90 degrees at the top
        Assert.Equal(plain.TriangleCount, spun.TriangleCount);                          // a constant turn needs no extra rings of triangles
        Assert.True(bent.TriangleCount > plain.TriangleCount);
        Assert.NotEqual((plain.Min.Z, plain.Max.Z), (spun.Min.Z, spun.Max.Z));         // the triangle really was turned
        var halfway = PrimMesh.Build("prism", new PrimParams(Twist: 90, TwistBegin: -90));
        Assert.True(halfway.TriangleCount > spun.TriangleCount, "a turn from -90 to 90 degrees is a long twist: it needs the rings");
    }

    [Fact]
    public void EveryShapeBuildsWithEveryParameterAtItsLimits()
    {
        foreach (var type in new[] { "cube", "prism", "cylinder", "tube", "cone", "pyramid", "sphere", "hemisphere", "torus", "ring" })
            foreach (var p in new[] { new PrimParams(Hollow: .95f, TaperX: 1, TaperY: -1, Twist: 180, TwistBegin: -180, CutStart: .95f, CutEnd: 1, PathBegin: .5f, PathEnd: 1, ShearX: .5f, ShearZ: -.5f, HoleX: .95f, HoleY: .05f, RadiusOffset: .9f, Revolutions: 3, Skew: .95f), new PrimParams() })
            {
                var data = PrimMesh.Build(type, p);
                Assert.True(data.TriangleCount > 0, type);
                foreach (var face in data.Faces) { Assert.Equal(face.Positions.Length, face.Normals.Length); Assert.Equal(face.Positions.Length, face.Uvs.Length); Assert.All(face.Positions, v => Assert.True(float.IsFinite(v.X + v.Y + v.Z), type)); }
            }
    }
}
