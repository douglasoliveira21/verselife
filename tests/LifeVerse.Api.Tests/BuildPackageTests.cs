using System.Numerics;
using LifeVerse.Shared.Building;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class BuildPackageTests
{
    private static SourceObject Wall(float x, float y, float z, Guid? group = null, float height = 2) =>
        new("cube", "Parede", "", x, y, z, 0, 15, 0, 1, height, 1, "{\"color\":\"#ffffff\"}", "{}", "{\"collision\":true}", "[]", group);

    [Fact]
    public void Create_StoresPositionsRelativeToTheMiddleAndTheLowestObject()
    {
        var package = BuildPackages.Create("Casa", [Wall(10, 5, 20), Wall(14, 5, 28), Wall(12, 3, 24)]);
        Assert.Equal("Casa", package.Name);
        Assert.Equal(BuildPackages.FormatName, package.Format);
        Assert.Equal(1f, package.BaseOffset); // the lowest object is 2 m tall, so its centre is 1 m above the ground
        Assert.Equal(-2f, package.Objects[0].X); Assert.Equal(2f, package.Objects[1].X);
        Assert.Equal(-4f, package.Objects[0].Z); Assert.Equal(4f, package.Objects[1].Z);
        Assert.Equal(2f, package.Objects[0].Y); Assert.Equal(0f, package.Objects[2].Y);
    }

    [Fact]
    public void Place_PutsTheLowestObjectOnTheGroundAtThePoint()
    {
        var package = BuildPackages.Create("Casa", [Wall(10, 5, 20), Wall(14, 5, 28), Wall(12, 3, 24)]);
        var placed = BuildPackages.Place(package, new Vector3(100, 7, -50));
        var lowest = placed.Single(p => p.Source.Y == 0);
        Assert.Equal(new Vector3(100, 8, -50), lowest.Position); // base 7 + half of 2 m
        Assert.Equal(new Vector3(98, 10, -54), placed[0].Position);
    }

    [Fact]
    public void Groups_BecomeNewGroupsWhenPlaced_AndStayTogether()
    {
        var old = Guid.NewGuid(); var other = Guid.NewGuid();
        var package = BuildPackages.Create("Sofá", [Wall(0, 1, 0, old), Wall(1, 1, 0, old), Wall(2, 1, 0, other), Wall(3, 1, 0)]);
        var first = BuildPackages.Place(package, Vector3.Zero); var second = BuildPackages.Place(package, Vector3.One);
        Assert.Equal(first[0].GroupId, first[1].GroupId); Assert.NotEqual(first[0].GroupId, first[2].GroupId); Assert.Null(first[3].GroupId);
        Assert.NotEqual(first[0].GroupId, second[0].GroupId); // every placing makes its own groups
        Assert.NotEqual(old, first[0].GroupId);
    }

    [Fact]
    public void Serialize_ThenParse_RoundTrips()
    {
        var package = BuildPackages.Create("Mesa", [Wall(1, 1, 1, Guid.NewGuid()), Wall(2, 1, 1)]);
        var parsed = BuildPackages.Parse(BuildPackages.Serialize(package), out var error);
        Assert.Null(error); Assert.NotNull(parsed);
        Assert.Equal(package.Name, parsed!.Name); Assert.Equal(package.Objects.Length, parsed.Objects.Length);
        Assert.Equal(package.Objects[0].MaterialJson, parsed.Objects[0].MaterialJson);
        Assert.Equal(package.Objects[0].Group, parsed.Objects[0].Group);
    }

    [Theory]
    [InlineData("")] [InlineData("not json")] [InlineData("{}")]
    [InlineData("{\"format\":\"other\",\"version\":1,\"name\":\"x\",\"baseOffset\":0,\"objects\":[]}")]
    [InlineData("{\"format\":\"lifeverse.build\",\"version\":2,\"name\":\"x\",\"baseOffset\":0,\"objects\":[]}")]
    [InlineData("{\"format\":\"lifeverse.build\",\"version\":1,\"name\":\"x\",\"baseOffset\":0,\"objects\":[]}")]
    public void Parse_RejectsWhatIsNotAConstruction(string json)
    {
        Assert.Null(BuildPackages.Parse(json, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Parse_RejectsBadNumbersSizesAndGroups()
    {
        PackageObject Make(float scale = 1, float x = 0, int? group = null) => new("cube", "a", "", x, 0, 0, 0, 0, 0, scale, 1, 1, "{}", "{}", "{}", "[]", group);
        BuildPackage Pack(params PackageObject[] objects) => new(BuildPackages.FormatName, 1, "p", 0, objects);
        Assert.Null(BuildPackages.Parse(BuildPackages.Serialize(Pack(Make(scale: 0))), out _));
        Assert.Null(BuildPackages.Parse(BuildPackages.Serialize(Pack(Make(scale: 100))), out _));
        Assert.Null(BuildPackages.Parse(BuildPackages.Serialize(Pack(Make(x: 99999))), out _));
        Assert.Null(BuildPackages.Parse(BuildPackages.Serialize(Pack(Make(group: -1))), out _));
        Assert.NotNull(BuildPackages.Parse(BuildPackages.Serialize(Pack(Make(group: 0))), out _));
    }

    [Fact]
    public void Limits_AreEnforced()
    {
        Assert.Throws<ArgumentException>(() => BuildPackages.Create("x", []));
        Assert.Throws<ArgumentException>(() => BuildPackages.Create("x", Enumerable.Range(0, BuildPackages.MaxObjects + 1).Select(i => Wall(i, 1, 0)).ToList()));
        var tooMany = new BuildPackage(BuildPackages.FormatName, 1, "p", 0, Enumerable.Range(0, BuildPackages.MaxObjects + 1).Select(i => new PackageObject("cube", "a", "", 0, 0, 0, 0, 0, 0, 1, 1, 1, "{}", "{}", "{}", "[]", null)).ToArray());
        Assert.Null(BuildPackages.Parse(BuildPackages.Serialize(tooMany), out _));
        Assert.Equal(160, BuildPackages.Create(new string('a', 400), [Wall(0, 1, 0)]).Name.Length);
        Assert.Equal("Construção", BuildPackages.Create("   ", [Wall(0, 1, 0)]).Name);
    }
}
