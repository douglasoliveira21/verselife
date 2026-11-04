using System.IO.Compression;
using System.Text.Json;
using LifeVerse.Shared.Land;
using LifeVerse.Shared.Terrain;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class TerrainBrushTests
{
    private static TerrainMap Flat() { var map = new TerrainMap(); return map; }
    private static float At(TerrainMap map, float worldX, float worldZ) => map.SampleHeight(worldX, worldZ);

    [Fact]
    public void TheBrushOptionsTravelInTheLayerNumberAndAPlainLayerStaysAPlainLayer()
    {
        for (var layer = 0; layer < TerrainMap.LayerCount; layer++)
        {
            Assert.True(BrushCodec.TryUnpack(layer, out var back, out var plain));
            Assert.Equal(layer, back); Assert.True(plain.IsDefault);
            Assert.Equal(layer, BrushCodec.Pack(layer, BrushOptions.Default));
        }
        var packed = BrushCodec.Pack(5, new BrushOptions(true, .6f, 12.345f));
        Assert.True(BrushCodec.TryUnpack(packed, out var layer5, out var brush));
        Assert.Equal(5, layer5); Assert.True(brush.Square); Assert.Equal(.6f, brush.Hardness, .04f); Assert.Equal(12.345f, brush.TargetHeight!.Value, .001f);
        Assert.False(BrushCodec.TryUnpack(-1, out _, out _)); Assert.False(BrushCodec.TryUnpack(TerrainMap.LayerCount, out _, out _)); Assert.False(BrushCodec.TryUnpack(1 << 27, out _, out _));
        Assert.True(BrushCodec.TryUnpack(BrushCodec.Pack(0, new BrushOptions(false, 0, TerrainMap.MinHeight)), out _, out var low)); Assert.Equal(TerrainMap.MinHeight, low.TargetHeight!.Value, .001f);
        Assert.True(BrushCodec.TryUnpack(BrushCodec.Pack(0, new BrushOptions(false, 0, TerrainMap.MaxHeight)), out _, out var high)); Assert.Equal(TerrainMap.MaxHeight, high.TargetHeight!.Value, .001f);
    }

    [Fact]
    public void ASquareBrushReachesTheCornersARoundOneDoesNot()
    {
        var round = Flat(); var square = Flat();
        round.Apply(TerrainTool.Raise, 0, 0, 8, 1, 0, new BrushOptions(false, 1f), null); square.Apply(TerrainTool.Raise, 0, 0, 8, 1, 0, new BrushOptions(true, 1f), null);
        Assert.Equal(0f, At(round, 7, 7), .001f); // the corner is outside the circle
        Assert.True(At(square, 7, 7) > .9f, $"{At(square, 7, 7)} {At(square, 8, 8)} {At(square, 6, 6)} {At(square, 6, 8)}"); // and inside the square
        Assert.Equal(At(round, 6, 0), At(square, 6, 0), .001f); // on the axes the two agree
        Assert.True(At(square, 10.5f, 0) < .01f); // beyond the radius nothing changes
    }

    [Fact]
    public void HardnessKeepsTheEffectUpToTheEdge()
    {
        var soft = Flat(); var hard = Flat();
        soft.Apply(TerrainTool.Raise, 0, 0, 10, 1, 0); hard.Apply(TerrainTool.Raise, 0, 0, 10, 1, 0, new BrushOptions(false, 1f), null);
        Assert.True(At(hard, 8, 0) > At(soft, 8, 0) + .3f);
        Assert.Equal(At(soft, 0, 0), At(hard, 0, 0), .001f); // the middle is the same either way
        var same = Flat(); same.Apply(TerrainTool.Raise, 0, 0, 10, 1, 0, new BrushOptions(false, 0f), null);
        Assert.Equal(soft.Serialize(), same.Serialize()); // no options means exactly the old brush
    }

    [Fact]
    public void LevelBringsTheGroundToTheChosenHeight()
    {
        var map = Flat();
        map.Apply(TerrainTool.Level, 0, 0, 10, 1, 0, new BrushOptions(false, 1f, 6f), null);
        Assert.Equal(6f, At(map, 0, 0), .001f); Assert.Equal(6f, At(map, 7, 3), .001f);
        map.Apply(TerrainTool.Level, 0, 0, 10, 1, 0, new BrushOptions(false, 1f, 100f), null); // clamped to the highest ground
        Assert.Equal(TerrainMap.MaxHeight, At(map, 0, 0), .001f);
        var half = Flat(); half.Apply(TerrainTool.Level, 0, 0, 10, .5f, 0, new BrushOptions(false, 1f, 4f), null);
        Assert.Equal(2f, At(half, 0, 0), .002f); // halfway there
        var around = Flat(); around.Apply(TerrainTool.Raise, 20, 0, 6, 2, 0);
        var peak = At(around, 20, 0);
        around.Apply(TerrainTool.Level, 20, 0, 8, 1, 0); // with no height given it levels to the ground under the brush, like Flatten
        Assert.True(At(around, 20, 0) <= peak + .001f);
    }

    [Fact]
    public void RestoreBringsBackTheOriginalGroundAndTextures()
    {
        var map = Flat(); var original = Flat().Serialize();
        for (var i = 0; i < 6; i++) { map.Apply(TerrainTool.Raise, 10, 10, 8, 1, 0); map.Apply(TerrainTool.Paint, 10, 10, 8, 1, 5); }
        Assert.NotEqual(original.Length > 0 ? 0f : 1f, At(map, 10, 10));
        Assert.True(At(map, 10, 10) > 1f); Assert.True(map.SplatAt(map.Cells / 2 + 5, map.Cells / 2 + 5, 5) > 200);
        map.Apply(TerrainTool.Restore, 10, 10, 12, 1, 0, new BrushOptions(false, 1f), null);
        var fresh = Flat();
        for (var x = 0; x <= map.Cells; x++) for (var z = 0; z <= map.Cells; z++) Assert.Equal(fresh.HeightAt(x, z), map.HeightAt(x, z), .002f);
        Assert.Equal(0, map.SplatAt(map.Cells / 2 + 5, map.Cells / 2 + 5, 5));
        Assert.Equal(255, Enumerable.Range(0, TerrainMap.LayerCount).Sum(l => map.SplatAt(map.Cells / 2 + 5, map.Cells / 2 + 5, l)));
        // a gentle restore only goes part of the way
        var gentle = Flat(); for (var i = 0; i < 4; i++) gentle.Apply(TerrainTool.Raise, 0, 0, 8, 1, 0);
        var raised = At(gentle, 0, 0); gentle.Apply(TerrainTool.Restore, 0, 0, 8, .5f, 0);
        Assert.True(At(gentle, 0, 0) < raised && At(gentle, 0, 0) > 0f);
        // and it can restore to another ground
        var reference = Flat(); reference.Apply(TerrainTool.Level, 0, 0, 12, 1, 0, new BrushOptions(false, 1f, 3f), null);
        var other = Flat(); other.Apply(TerrainTool.Restore, 0, 0, 12, 1, 0, new BrushOptions(false, 1f), null, reference);
        Assert.Equal(3f, At(other, 0, 0), .002f);
    }

    [Fact]
    public void ErosionWearsSteepGroundDownWithoutMakingOrLosingMaterial()
    {
        var map = Flat();
        for (var i = 0; i < 10; i++) map.Apply(TerrainTool.Raise, 0, 0, 5, 2, 0); // a sharp hill
        double Volume(TerrainMap m) { double sum = 0; for (var x = 1; x < m.Cells; x++) for (var z = 1; z < m.Cells; z++) sum += m.HeightAt(x, z); return sum; }
        var before = Volume(map); var peak = At(map, 0, 0);
        for (var i = 0; i < 40; i++) map.Apply(TerrainTool.Erode, 0, 0, 14, 1, 0, new BrushOptions(false, 1f), null);
        Assert.True(At(map, 0, 0) < peak - 1f, $"the peak came down: {peak} to {At(map, 0, 0)}");
        Assert.True(Math.Abs(Volume(map) - before) < 0.03 * map.Cells * map.Cells / 100, $"material is conserved: {Volume(map) - before}");
        var calm = Flat(); Assert.False(calm.Apply(TerrainTool.Erode, 0, 0, 10, 1, 0)); // flat ground has nothing to slide
        var again = Flat(); for (var i = 0; i < 10; i++) again.Apply(TerrainTool.Raise, 0, 0, 5, 2, 0); for (var i = 0; i < 40; i++) again.Apply(TerrainTool.Erode, 0, 0, 14, 1, 0, new BrushOptions(false, 1f), null);
        Assert.Equal(map.Serialize(), again.Serialize()); // the same edits always give the same ground
    }

    [Fact]
    public void OnlyTheVerticesThatPassTheTestChange()
    {
        var map = Flat(); var revision = map.Revision;
        bool Allowed(int x, int z) => x <= map.Cells / 2; // the west half of the island
        Assert.True(map.Apply(TerrainTool.Raise, 0, 0, 10, 1, 0, new BrushOptions(false, 1f), Allowed));
        Assert.Equal(revision + 1, map.Revision);
        Assert.True(At(map, -4, 0) > .9f); Assert.Equal(0f, At(map, 4, 0), .001f);
        var patch = map.Capture(0, 0, 10, false, Allowed);
        Assert.All(patch.Entries, e => Assert.True(e.Key % map.VertexCount <= map.Cells / 2));
        Assert.True(patch.Count < map.Capture(0, 0, 10).Count);
        Assert.False(map.Apply(TerrainTool.Raise, 40, 0, 4, 1, 0, new BrushOptions(false, 1f), Allowed), "a brush wholly outside the allowed ground does nothing");
        Assert.Equal(revision + 1, map.Revision);
        // the sea floor at the edge never changes, whatever the test says
        var edge = Flat(); edge.Apply(TerrainTool.Raise, -(edge.HalfExtent), 0, 8, 2, 0, new BrushOptions(false, 1f), (_, _) => true);
        for (var z = 0; z <= edge.Cells; z++) Assert.Equal(TerrainMap.MinHeight, edge.HeightAt(0, z));
    }

    [Fact]
    public void ThereAreEightTextureLayersAndPaintingAnyOfThemKeepsTheWeightsAt255()
    {
        Assert.Equal(8, TerrainMap.LayerCount);
        var map = Flat();
        for (var layer = 0; layer < TerrainMap.LayerCount; layer++)
        {
            map.Apply(TerrainTool.Paint, 0, 0, 6, .6f, layer);
            Assert.Equal(255, Enumerable.Range(0, TerrainMap.LayerCount).Sum(l => map.SplatAt(map.Cells / 2, map.Cells / 2, l)));
        }
        map.Apply(TerrainTool.Paint, 0, 0, 6, 1f, 7);
        Assert.True(map.SplatAt(map.Cells / 2, map.Cells / 2, 7) > 250);
        Assert.True(TerrainMap.TryDeserialize(map.Serialize(), out var copy));
        for (var layer = 0; layer < TerrainMap.LayerCount; layer++) Assert.Equal(map.SplatAt(map.Cells / 2, map.Cells / 2, layer), copy.SplatAt(map.Cells / 2, map.Cells / 2, layer));
    }

    [Fact]
    public void MapsSavedWithFourLayersStillLoad()
    {
        const int cells = 8; var vertices = (cells + 1) * (cells + 1);
        using var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, System.Text.Encoding.UTF8, true))
        {
            writer.Write(0x3254564Cu); writer.Write(cells); writer.Write(2f); writer.Write(7L); writer.Write(-.8f);
            for (var i = 0; i < vertices; i++) writer.Write((ushort)(8000 + 2000)); // 2 m above the lowest ground
            for (var i = 0; i < vertices; i++) { writer.Write((byte)100); writer.Write((byte)50); writer.Write((byte)100); writer.Write((byte)5); }
        }
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, true)) raw.WriteTo(deflate);
        Assert.True(TerrainMap.TryDeserialize(packed.ToArray(), out var map));
        Assert.Equal(cells, map.Cells); Assert.Equal(7, map.Revision);
        Assert.Equal(2f, map.HeightAt(3, 3), .001f);
        Assert.Equal(new[] { 100, 50, 100, 5, 0, 0, 0, 0 }, Enumerable.Range(0, 8).Select(l => (int)map.SplatAt(3, 3, l)));
        Assert.False(TerrainMap.TryDeserialize(packed.ToArray()[..^6], out _));
    }
}

public sealed class LandTerrainRightsTests
{
    private static readonly Guid Owner = Guid.NewGuid(), Builder = Guid.NewGuid(), Stranger = Guid.NewGuid();

    private static LandTerrainRights Rights(params (Guid[] Editors, int[][] Runs)[] parcels)
    {
        var json = JsonSerializer.Serialize(new { parcels = parcels.Select(p => new { id = Guid.NewGuid(), editors = p.Editors, runs = p.Runs }) });
        return LandTerrainRights.Parse(JsonDocument.Parse(json).RootElement);
    }

    [Fact]
    public void OnlyTheEditorsOfAParcelMayChangeItsGround()
    {
        var rights = Rights(([Owner, Builder], [[0, 0, 3], [1, 0, 3]])); // cells x 0..3, z 0..1: metres 0..16 by 0..8
        Assert.True(rights.HasAny(Owner)); Assert.True(rights.HasAny(Builder)); Assert.False(rights.HasAny(Stranger));
        Assert.True(rights.Allows(Owner, 6, 2)); Assert.True(rights.Allows(Builder, 6, 2)); Assert.False(rights.Allows(Stranger, 6, 2));
        Assert.False(rights.Allows(Owner, 20, 2)); // ground that belongs to no parcel is for administrators
        Assert.False(rights.Allows(Owner, 6, -2));
    }

    [Fact]
    public void AVertexOnABoundaryNeedsRightsOnBothSides()
    {
        var mine = Rights(([Owner], [[0, 0, 1]]), ([Stranger], [[0, 2, 3]])); // two neighbouring parcels along x = 8
        Assert.True(mine.Allows(Owner, 6, 2)); Assert.False(mine.Allows(Owner, 8, 2), "a point on the line between the two parcels moves land of the neighbour");
        Assert.False(mine.Allows(Owner, 10, 2)); Assert.True(mine.Allows(Stranger, 10, 2));
        Assert.True(mine.Allows(Owner, 4, 2)); // the edge of the island's cell grid inside the parcel is fine
        var joined = Rights(([Owner], [[0, 0, 1]]), ([Owner], [[0, 2, 3]]));
        Assert.True(joined.Allows(Owner, 8, 2), "when both sides are theirs the boundary may move");
    }

    [Fact]
    public void ParcelsNobodyMayEditAreClosedToEveryone()
    {
        var closed = Rights((Array.Empty<Guid>(), [[0, 0, 3]]));
        Assert.False(closed.Allows(Owner, 2, 2)); Assert.False(closed.HasAny(Owner)); Assert.Equal(4, closed.Cells);
        Assert.Equal(0, LandTerrainRights.Parse(JsonDocument.Parse("[]").RootElement).Cells);
        Assert.Equal(0, LandTerrainRights.Parse(JsonDocument.Parse("{\"parcels\":[{\"id\":1},{\"editors\":[\"x\"]}]}").RootElement).Cells);
    }

    [Fact]
    public void TheMaskFollowsTheMapsGrid()
    {
        var map = new TerrainMap(); // 128 cells of 2 m, centred on the origin
        var rights = Rights(([Owner], [[0, 0, 3], [1, 0, 3], [2, 0, 3], [3, 0, 3]])); // 16 x 16 m with its corner at the origin
        var mask = rights.MaskFor(Owner, map);
        Assert.True(mask(map.Cells / 2 + 3, map.Cells / 2 + 3)); // 6 m, 6 m
        Assert.False(mask(map.Cells / 2 - 3, map.Cells / 2 + 3));
        Assert.False(mask(map.Cells / 2 + 8, map.Cells / 2 + 3)); // 16 m is the boundary
        var edit = new TerrainMap(); edit.Apply(TerrainTool.Raise, 8, 8, 24, 1, 0, new BrushOptions(false, 1f), mask);
        Assert.True(edit.SampleHeight(6, 6) > .9f); Assert.Equal(0f, edit.SampleHeight(-6, 6), .001f); Assert.Equal(0f, edit.SampleHeight(20, 6), .001f); Assert.Equal(0f, edit.SampleHeight(16, 6), .001f);
    }
}
