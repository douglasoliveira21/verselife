using LifeVerse.Shared.Terrain;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class TerrainEditTests
{
    [Fact]
    public void TerrainEdits_RaiseLowerFlattenAndSmoothWithinBounds()
    {
        var terrain = new TerrainEditBuffer(9, 9); terrain.Raise(4, 4, 3, 2); Assert.True(terrain.Heights[40] > 1.9f); terrain.Lower(4, 4, 3, 1); Assert.InRange(terrain.Heights[40], .9f, 1.1f); terrain.Flatten(4, 4, 3, 4); Assert.InRange(terrain.Heights[40], 3.9f, 4.1f); terrain.Smooth(4, 4, 3); Assert.Equal(81, terrain.Heights.Count);
    }
}
