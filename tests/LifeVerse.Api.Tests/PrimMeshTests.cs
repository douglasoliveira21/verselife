using System.Numerics;
using LifeVerse.Shared.Building;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class PrimMeshTests
{
    public static IEnumerable<object[]> Types => new[] { "cube", "prism", "cylinder", "tube", "cone", "pyramid" }.Select(x => new object[] { x });

    [Fact]
    public void Box_HasSixFacesTwelveTrianglesAndUnitBounds()
    {
        var mesh = PrimMesh.Build("cube", new PrimParams());
        Assert.True(mesh.Faces.Count == 6, string.Join(",", mesh.Faces.Select(f => f.Id)));
        Assert.Equal(16, mesh.TriangleCount); // 8 wall triangles and a fan of 4 on each cap
        Assert.Equal(new Vector3(-.5f), mesh.Min); Assert.Equal(new Vector3(.5f), mesh.Max);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, mesh.Faces.Select(x => x.Id));
    }

    [Theory, MemberData(nameof(Types))]
    public void EveryTriangleWindsClockwiseSeenFromOutside(string type)
    {
        foreach (var parameters in new[] { new PrimParams(), new PrimParams(Hollow: .5f), new PrimParams(Twist: 90, TaperX: .4f), new PrimParams(CutStart: .25f, CutEnd: .75f), new PrimParams(Hollow: .4f, CutStart: .1f, CutEnd: .6f, Twist: -45) })
            foreach (var face in PrimMesh.Build(type, parameters).Faces)
                for (var i = 0; i < face.Indices.Length; i += 3)
                {
                    int a = face.Indices[i], b = face.Indices[i + 1], c = face.Indices[i + 2];
                    var cross = Vector3.Cross(face.Positions[b] - face.Positions[a], face.Positions[c] - face.Positions[a]);
                    var normal = (face.Normals[a] + face.Normals[b] + face.Normals[c]);
                    Assert.True(cross.LengthSquared() > 0, $"{type} face {face.Id} has a flat triangle");
                    Assert.True(Vector3.Dot(cross, normal) < 0, $"{type} face {face.Id} winds the wrong way with {parameters}");
                }
    }

    [Theory, MemberData(nameof(Types))]
    public void ExtremeSettingsStayFiniteAndWithinBudget(string type)
    {
        var mesh = PrimMesh.Build(type, new PrimParams(Hollow: 99, TaperX: -50, TaperY: 50, Twist: 9999, CutStart: -3, CutEnd: 7));
        Assert.True(mesh.TriangleCount is > 0 and < 4000, $"{type} has {mesh.TriangleCount} triangles");
        foreach (var face in mesh.Faces)
        {
            Assert.All(face.Positions, x => Assert.True(float.IsFinite(x.X) && float.IsFinite(x.Y) && float.IsFinite(x.Z)));
            Assert.All(face.Normals, x => Assert.InRange(x.Length(), .99f, 1.01f));
            Assert.All(face.Indices, x => Assert.InRange(x, 0, face.Positions.Length - 1));
        }
    }

    [Fact]
    public void Hollow_AddsAnInnerWallAndRingCaps()
    {
        var solid = PrimMesh.Build("cube", new PrimParams());
        var hollow = PrimMesh.Build("cube", new PrimParams(Hollow: .5f));
        Assert.Contains(hollow.Faces, x => x.Id == PrimMesh.InnerFace("cube"));
        Assert.DoesNotContain(solid.Faces, x => x.Id == PrimMesh.InnerFace("cube"));
        Assert.True(hollow.TriangleCount > solid.TriangleCount);
        // the inner wall faces the hole: its normals point toward the axis
        var inner = hollow.Faces.Single(x => x.Id == PrimMesh.InnerFace("cube"));
        for (var i = 0; i < inner.Positions.Length; i++) Assert.True(Vector3.Dot(inner.Normals[i], new Vector3(inner.Positions[i].X, 0, inner.Positions[i].Z)) < 0);
    }

    [Fact]
    public void Taper_ShrinksTheTop()
    {
        var plain = PrimMesh.Build("cube", new PrimParams());
        var tapered = PrimMesh.Build("cube", new PrimParams(TaperX: .5f, TaperY: .5f));
        var top = tapered.Faces.Single(x => x.Id == PrimMesh.TopFace);
        Assert.Equal(.25f, top.Positions.Max(x => x.X), 3);
        Assert.Equal(.5f, plain.Faces.Single(x => x.Id == PrimMesh.TopFace).Positions.Max(x => x.X), 3);
    }

    [Fact]
    public void Cone_EndsInAPoint()
    {
        var cone = PrimMesh.Build("cone", new PrimParams());
        Assert.DoesNotContain(cone.Faces, x => x.Id == PrimMesh.TopFace);
        Assert.Contains(cone.Faces, x => x.Id == PrimMesh.BottomFace);
        Assert.Equal(.5f, cone.Max.Y, 3);
    }

    [Fact]
    public void PathCut_RemovesAWedgeAndAddsTwoCutFaces()
    {
        var half = PrimMesh.Build("cylinder", new PrimParams(CutStart: 0, CutEnd: .5f));
        Assert.Contains(half.Faces, x => x.Id == PrimMesh.CutStartFace("cylinder"));
        Assert.Contains(half.Faces, x => x.Id == PrimMesh.CutEndFace("cylinder"));
        Assert.True(half.Min.Z >= -1e-4f, "half a cylinder (angles 0 to 180) lies on one side of the X axis");
        Assert.Equal(.5f, half.Max.Z, 2);
    }

    [Fact]
    public void Twist_AddsLevelsAndRotatesTheTop()
    {
        var straight = PrimMesh.Build("cube", new PrimParams());
        var twisted = PrimMesh.Build("cube", new PrimParams(Twist: 45));
        Assert.True(twisted.TriangleCount > straight.TriangleCount * 2);
        var top = twisted.Faces.Single(x => x.Id == PrimMesh.TopFace);
        Assert.Contains(top.Positions, v => MathF.Abs(MathF.Abs(v.X) - .707f) < .01f && MathF.Abs(v.Z) < .01f); // after 45 degrees a corner sits on the axis
    }

    [Fact]
    public void Parse_ReadsAndClampsTheGeometryJson()
    {
        var parsed = PrimParams.Parse("{\"hollow\":0.3,\"taperX\":2,\"twist\":-400,\"cutStart\":0.9,\"cutEnd\":0.92}");
        Assert.Equal(.3f, parsed.Hollow, 3); Assert.Equal(1f, parsed.TaperX); Assert.Equal(-180f, parsed.Twist);
        Assert.True(parsed.CutEnd - parsed.CutStart >= .049f);
        Assert.Equal(new PrimParams(), PrimParams.Parse("not json"));
        Assert.Equal(new PrimParams(), PrimParams.Parse("{}"));
    }

    [Theory]
    [InlineData("sphere")] [InlineData("torus")] [InlineData("plane")]
    public void OtherPrimitivesAreNotProcedural(string type) => Assert.False(PrimMesh.Supports(type));
}
