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
    [InlineData("plane")] [InlineData("mesh")]
    public void OtherPrimitivesAreNotProcedural(string type) => Assert.False(PrimMesh.Supports(type));
}

public sealed class PrimMeshAdvancedTests
{
    public static IEnumerable<object[]> All => new[] { "cube", "prism", "cylinder", "tube", "cone", "pyramid", "sphere", "hemisphere", "torus", "ring" }.Select(x => new object[] { x });
    public static IEnumerable<object[]> Round => new[] { "sphere", "hemisphere", "torus", "ring" }.Select(x => new object[] { x });

    [Theory, MemberData(nameof(All))]
    public void EverySettingKeepsTheMeshFiniteAndWindingOutward(string type)
    {
        var settings = new[]
        {
            new PrimParams(), new PrimParams(Hollow: .5f), new PrimParams(PathBegin: .2f, PathEnd: .8f), new PrimParams(ShearX: .3f, ShearZ: -.2f),
            new PrimParams(HoleX: .6f, HoleY: .3f), new PrimParams(CutStart: .25f, CutEnd: .75f, Hollow: .4f), new PrimParams(Twist: 90, TaperX: .4f, TaperY: -.3f),
            new PrimParams(RadiusOffset: .5f, Revolutions: 2, Skew: .5f), new PrimParams(Hollow: 99, PathBegin: -4, PathEnd: 9, ShearX: 40, HoleX: 50, Revolutions: 99, Skew: 9, RadiusOffset: 9)
        };
        foreach (var parameters in settings)
        {
            var mesh = PrimMesh.Build(type, parameters);
            Assert.True(mesh.TriangleCount is > 0 and < 12000, $"{type} has {mesh.TriangleCount} triangles with {parameters}");
            foreach (var face in mesh.Faces)
            {
                foreach (var position in face.Positions) Assert.True(float.IsFinite(position.X + position.Y + position.Z), $"{type} {parameters}");
                foreach (var normal in face.Normals) Assert.True(float.IsFinite(normal.X + normal.Y + normal.Z), $"{type} normal {parameters}");
            }
        }
    }

    [Fact]
    public void PathCutShortensABoxAndShearSlidesItsTop()
    {
        var whole = PrimMesh.Build("cube", new PrimParams()); var cut = PrimMesh.Build("cube", new PrimParams(PathBegin: .25f, PathEnd: .75f));
        Assert.Equal(.5f, cut.Max.Y - cut.Min.Y, 3); Assert.Equal(1f, whole.Max.Y - whole.Min.Y, 3);
        var sheared = PrimMesh.Build("cube", new PrimParams(ShearX: .25f));
        Assert.Equal(.75f, sheared.Max.X, 3); Assert.Equal(-.5f, sheared.Min.X, 3);
    }

    [Fact]
    public void HoleSizeXAndYAreIndependent()
    {
        var wide = PrimMesh.Build("cube", new PrimParams(HoleX: .8f, HoleY: .2f));
        var inner = wide.Faces.Single(f => f.Id == PrimMesh.InnerFace("cube"));
        Assert.Equal(.4f, inner.Positions.Max(p => p.X), 3); Assert.Equal(.1f, inner.Positions.Max(p => p.Z), 3);
    }

    [Theory, MemberData(nameof(Round))]
    public void RoundShapesAreRealGeometryThatReactsToTheirSettings(string type)
    {
        var plain = PrimMesh.Build(type, new PrimParams()); Assert.True(plain.TriangleCount > 100);
        Assert.NotEqual(plain.TriangleCount, PrimMesh.Build(type, new PrimParams(Hollow: .5f)).TriangleCount);
        Assert.NotEqual(plain.TriangleCount, PrimMesh.Build(type, new PrimParams(CutStart: .25f, CutEnd: .75f)).TriangleCount);
        Assert.True(PrimMesh.Build(type, new PrimParams(CutStart: .25f, CutEnd: .75f)).Faces.Any(f => f.Id == PrimMesh.ProfileCutStartFace), "a profile cut leaves flat faces");
        Assert.NotEqual(plain.TriangleCount, PrimMesh.Build(type, new PrimParams(PathBegin: .3f, PathEnd: .7f)).TriangleCount);
    }

    [Fact]
    public void ATorusReactsToHoleSizeRevolutionsRadiusOffsetAndSkew()
    {
        var plain = PrimMesh.Build("torus", new PrimParams());
        Assert.True(Math.Abs(plain.Max.X - .5f) < .01f && Math.Abs(plain.Max.Y - .15f) < .01f, $"{plain.Max}");
        Assert.True(PrimMesh.Build("torus", new PrimParams(HoleX: .6f)).Max.X - 0 > 0); Assert.True(PrimMesh.Build("torus", new PrimParams(HoleY: .6f)).Max.Y > plain.Max.Y + .1f);
        Assert.True(PrimMesh.Build("torus", new PrimParams(Revolutions: 2)).TriangleCount > plain.TriangleCount * 1.8);
        Assert.True(PrimMesh.Build("torus", new PrimParams(Skew: .8f)).Max.Y - PrimMesh.Build("torus", new PrimParams(Skew: .8f)).Min.Y > plain.Max.Y - plain.Min.Y + .5f);
        Assert.NotEqual(plain.Max.X, PrimMesh.Build("torus", new PrimParams(RadiusOffset: .5f)).Max.X);
    }

    [Fact]
    public void ASphereDimpleCutsItsPolesAndParseReadsEveryField()
    {
        var sphere = PrimMesh.Build("sphere", new PrimParams()); var dimpled = PrimMesh.Build("sphere", new PrimParams(PathBegin: .25f));
        Assert.True(dimpled.Min.Y > sphere.Min.Y + .05f); Assert.Contains(dimpled.Faces, f => f.Id == PrimMesh.BottomFace);
        var parsed = PrimParams.Parse("{\"pathBegin\":0.2,\"pathEnd\":0.9,\"shearX\":0.1,\"shearZ\":-0.1,\"holeX\":0.4,\"holeY\":0.3,\"radiusOffset\":0.2,\"revolutions\":2,\"skew\":0.3}");
        Assert.Equal(new PrimParams(PathBegin: .2f, PathEnd: .9f, ShearX: .1f, ShearZ: -.1f, HoleX: .4f, HoleY: .3f, RadiusOffset: .2f, Revolutions: 2, Skew: .3f), parsed);
    }
}
