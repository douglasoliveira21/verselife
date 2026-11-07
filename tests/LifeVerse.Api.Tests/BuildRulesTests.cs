using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class BuildRulesTests
{
    private static readonly Guid Owner = Guid.NewGuid(), ParcelOwner = Guid.NewGuid(), Visitor = Guid.NewGuid(), Other = Guid.NewGuid();
    private static ParcelFacts Parcel(bool publicBuild = false, Guid? owner = null) => new(Guid.NewGuid(), "Praça", owner ?? ParcelOwner, publicBuild, 100, -10, -10, 10, 10);

    [Fact]
    public void Parcel_ContainsItsRectangleWithMinInclusiveAndMaxExclusive()
    {
        var p = Parcel();
        Assert.True(p.Contains(-10, -10)); Assert.True(p.Contains(0, 0)); Assert.True(p.Contains(9.99, 9.99));
        Assert.False(p.Contains(10, 0)); Assert.False(p.Contains(0, 10)); Assert.False(p.Contains(-10.01, 0));
        Assert.Equal(2000, p.MaxCost);
    }

    [Fact]
    public void Parcel_IsReadFromTheStoredEntity()
    {
        var entity = new IslandParcel { Id = Guid.NewGuid(), IslandId = Guid.NewGuid(), OwnerUserId = ParcelOwner, Name = "Loja", BoundsJson = "{\"minX\":-5,\"minZ\":-6,\"maxX\":7,\"maxZ\":8}", PermissionsJson = "{\"enter\":true,\"build\":true}", MaxObjects = 50 };
        var facts = ParcelFacts.From(entity)!;
        Assert.True(facts.PublicBuild); Assert.True(facts.Contains(0, 0)); Assert.False(facts.Contains(8, 0)); Assert.Equal(50, facts.MaxObjects);
        entity.BoundsJson = "garbage"; Assert.Null(ParcelFacts.From(entity));
        entity.BoundsJson = "{\"minX\":0,\"minZ\":0,\"maxX\":1,\"maxZ\":1}"; entity.PermissionsJson = "broken"; Assert.False(ParcelFacts.From(entity)!.PublicBuild);
    }

    [Fact]
    public void ParcelAt_FindsTheParcelUnderAPoint()
    {
        var a = Parcel(); var b = new ParcelFacts(Guid.NewGuid(), "B", Other, false, 10, 20, 20, 30, 30);
        Assert.Same(a, BuildRules.ParcelAt([a, b], 0, 0)); Assert.Same(b, BuildRules.ParcelAt([a, b], 25, 25)); Assert.Null(BuildRules.ParcelAt([a, b], 15, 15));
    }

    [Theory]
    [InlineData(false, false, "forbidden")] // no parcel, no island permission
    [InlineData(false, true, null)]         // no parcel, island builder
    [InlineData(true, false, null)]         // island admin anywhere
    public void CanBuildAt_WithoutAParcel(bool admin, bool islandBuild, string? expected) =>
        Assert.Equal(expected, BuildRules.CanBuildAt(null, Visitor, admin, islandBuild));

    [Fact]
    public void CanBuildAt_InsideAParcel_FollowsTheParcelNotTheIsland()
    {
        Assert.Equal("parcel_forbidden", BuildRules.CanBuildAt(Parcel(), Visitor, false, hasIslandBuild: true)); // island builders do not override a private parcel
        Assert.Null(BuildRules.CanBuildAt(Parcel(), ParcelOwner, false, hasIslandBuild: false)); // the parcel owner needs no island permission
        Assert.Null(BuildRules.CanBuildAt(Parcel(publicBuild: true), Visitor, false, hasIslandBuild: false));
        Assert.Null(BuildRules.CanBuildAt(Parcel(), Visitor, isIslandAdmin: true, hasIslandBuild: true));
    }

    [Fact]
    public void CanEditObject_OwnerAdminOrParcelOwnerOnly()
    {
        Assert.Null(BuildRules.CanEditObject(Visitor, Visitor, false, null));
        Assert.Equal("not_owner", BuildRules.CanEditObject(Visitor, Other, false, null));
        Assert.Equal("not_owner", BuildRules.CanEditObject(Visitor, Other, false, Parcel(publicBuild: true))); // a public parcel lets people build, not edit each other's things
        Assert.Null(BuildRules.CanEditObject(ParcelOwner, Other, false, Parcel()));
        Assert.Null(BuildRules.CanEditObject(Owner, Other, true, null));
    }

    [Fact]
    public void CanReturn_MirrorsEditRights()
    {
        Assert.Null(BuildRules.CanReturn(ParcelOwner, Visitor, false, Parcel()));
        Assert.Null(BuildRules.CanReturn(Owner, Visitor, true, null));
        Assert.Null(BuildRules.CanReturn(Visitor, Visitor, false, null)); // taking your own
        Assert.Equal("not_allowed_to_return", BuildRules.CanReturn(Visitor, Other, false, Parcel()));
    }
}

public sealed class BuildCostTests
{
    [Fact]
    public void SimpleShapesAreCheap_ComplexOnesCostMore()
    {
        var box = BuildCost.Of("cube", "{}", "{}", "{\"collision\":true}", "[]");
        var twisted = BuildCost.Of("cube", "{\"twist\":120,\"hollow\":0.5}", "{}", "{\"collision\":true}", "[]");
        var sphere = BuildCost.Of("sphere", "{}", "{}", "{\"collision\":true}", "[]");
        Assert.InRange(box, 1, 5); Assert.True(twisted > box * 3, $"{twisted} vs {box}"); Assert.True(sphere > twisted);
    }

    [Fact]
    public void GhostObjectsHaveNoPhysicsCost() =>
        Assert.Equal(BuildCost.Of("cube", "{}", "{}", "{\"collision\":true}", "[]") - 1, BuildCost.Of("cube", "{}", "{}", "{\"collision\":false}", "[]"));

    [Fact]
    public void CollisionProfilesAffectBothAuthorityAndIslandBudget()
    {
        Assert.False(LifeVerse.Shared.Networking.ObstacleField.Blocks("{\"shape\":\"none\"}"));
        // The island simulation has no downloaded triangle mesh yet. It must not replace a
        // house's doorways with one solid bounding box; client-side Godot collision remains exact.
        Assert.False(LifeVerse.Shared.Networking.ObstacleField.Blocks("{\"shape\":\"triangle\"}"));
        var box = BuildCost.Of("mesh", "{}", "{}", "{\"shape\":\"box\"}", "[]");
        Assert.True(BuildCost.Of("mesh", "{}", "{}", "{\"shape\":\"convex\"}", "[]") > box);
        Assert.True(BuildCost.Of("mesh", "{}", "{}", "{\"shape\":\"triangle\"}", "[]") > box);
    }

    [Fact]
    public void TexturesLightsFacesAndContentsAddUp()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var plain = BuildCost.Of("cube", "{}", "{}", "{}", "[]");
        var textured = BuildCost.Of("cube", "{}", $"{{\"texture\":\"{a}\",\"normalMap\":\"{b}\"}}", "{}", "[]");
        Assert.Equal(plain + 6, textured);
        Assert.Equal(plain + 5, BuildCost.Of("cube", "{}", "{\"light\":{\"energy\":1,\"range\":4}}", "{}", "[]"));
        Assert.Equal(plain + 2, BuildCost.Of("cube", "{}", "{\"faces\":[{\"index\":0},{\"index\":1}]}", "{}", "[]"));
        Assert.Equal(plain + 1, BuildCost.Of("cube", "{}", "{}", "{}", $"[{{\"assetId\":\"{a}\",\"name\":\"x\",\"category\":\"sounds\"}}]"));
    }

    [Fact]
    public void CostIsCappedAndToleratesBadJson()
    {
        var many = string.Join(",", Enumerable.Range(0, 8).Select(_ => $"\"x\":\"{Guid.NewGuid()}\""));
        Assert.True(BuildCost.Of("mesh", "garbage", "not json", "{", "[") <= BuildCost.MaxObjectCost);
        Assert.True(BuildCost.Of("sphere", "{}", "{\"faces\":[" + string.Join(",", Enumerable.Repeat("{\"index\":0}", 32)) + "]}", "{}", "[]") <= BuildCost.MaxObjectCost);
        Assert.True(BuildCost.Triangles("unknown", null) > 0);
        _ = many;
    }

    [Fact]
    public void Limit_ReadsTheIslandSettingOrTheDefault()
    {
        Assert.Equal(5000, BuildCost.Limit("{\"cost\":5000}", "cost", 1));
        Assert.Equal(7, BuildCost.Limit("{}", "cost", 7)); Assert.Equal(7, BuildCost.Limit("nope", "cost", 7)); Assert.Equal(7, BuildCost.Limit(null, "cost", 7));
        Assert.Equal(1, BuildCost.Limit("{\"cost\":-9}", "cost", 7));
    }
}

public sealed class PrimitiveCatalogParityTests
{
    [Fact]
    public void IslandServerAndApiAcceptTheSameShapes() =>
        Assert.Equal(BuildCatalog.PrimitiveTypes.OrderBy(x => x), LifeVerse.Shared.Building.PrimitiveCatalog.Types.OrderBy(x => x));

    [Fact]
    public void EveryShapeTheEditorOffersIsAccepted()
    {
        foreach (var type in new[] { "cube", "sphere", "cylinder", "prism", "torus", "tube", "ring", "pyramid", "cone", "hemisphere", "plane" })
        {
            Assert.True(LifeVerse.Shared.Building.PrimitiveCatalog.IsKnown(type), type);
            Assert.True(BuildCatalog.IsPrimitive(type), type);
        }
    }
}
