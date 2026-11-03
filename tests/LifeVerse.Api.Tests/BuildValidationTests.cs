using LifeVerse.Api.Modules.Worlds;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class BuildValidationTests
{
    [Fact]
    public void Catalog_ContainsAllPhaseFivePrimitives()
    {
        var expected = new[] { "cube", "sphere", "cylinder", "cone", "plane", "prism", "tube", "ring", "pyramid" };
        // The nine primitives plus "mesh", which shows an imported 3D model.
        Assert.Equal(expected.Length + 1, BuildCatalog.PrimitiveTypes.Length);
        Assert.True(BuildCatalog.IsPrimitive("mesh"));
        foreach (var primitive in expected) Assert.True(BuildCatalog.IsPrimitive(primitive));
        Assert.False(BuildCatalog.IsPrimitive("mesh_script"));
    }
}
