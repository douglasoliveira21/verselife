using LifeVerse.Api.Modules.Worlds;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class BuildValidationTests
{
    [Fact]
    public void Catalog_ContainsAllPhaseFivePrimitives()
    {
        var expected = new[] { "cube", "sphere", "cylinder", "cone", "plane", "prism", "tube", "ring", "pyramid" };
        Assert.Equal(expected.Length, BuildCatalog.PrimitiveTypes.Length);
        foreach (var primitive in expected) Assert.True(BuildCatalog.IsPrimitive(primitive));
        Assert.False(BuildCatalog.IsPrimitive("mesh_script"));
    }
}
