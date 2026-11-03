using System.Buffers.Binary;
using System.Text;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Shared.Building;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class PackageParityTests
{
    private static SourceObject Source(float x, Guid? group = null) => new("cube", "Parede", "", x, 1, 0, 0, 0, 0, 1, 2, 1, "{}", "{}", "{\"collision\":true}", "[]", group);

    [Fact]
    public void ApiReadsWhatTheClientWrites()
    {
        var package = BuildPackages.Create("Casa", [Source(0, Guid.NewGuid()), Source(3, Guid.NewGuid()), Source(6)]);
        var parsed = PackageDocument.Parse(BuildPackages.Serialize(package), out var error);
        Assert.Null(error); Assert.NotNull(parsed);
        Assert.Equal(package.Name, parsed!.Name); Assert.Equal(package.BaseOffset, parsed.BaseOffset);
        Assert.Equal(package.Objects.Select(o => (o.X, o.Group)), parsed.Objects.Select(o => (o.X, o.Group)));
        // and the client reads what the API stores
        Assert.NotNull(BuildPackages.Parse(PackageDocument.Serialize(parsed), out _));
    }

    [Theory]
    [InlineData("")] [InlineData("not json")] [InlineData("{}")]
    [InlineData("{\"format\":\"other\",\"version\":1,\"name\":\"x\",\"baseOffset\":0,\"objects\":[]}")]
    [InlineData("{\"format\":\"lifeverse.build\",\"version\":2,\"name\":\"x\",\"baseOffset\":0,\"objects\":[]}")]
    [InlineData("{\"format\":\"lifeverse.build\",\"version\":1,\"name\":\"x\",\"baseOffset\":0,\"objects\":[]}")]
    [InlineData("{\"format\":\"lifeverse.build\",\"version\":1,\"name\":\"\",\"baseOffset\":0,\"objects\":[{\"primitiveType\":\"cube\",\"name\":\"a\",\"description\":\"\",\"x\":0,\"y\":0,\"z\":0,\"rotationX\":0,\"rotationY\":0,\"rotationZ\":0,\"scaleX\":1,\"scaleY\":1,\"scaleZ\":1,\"materialJson\":\"{}\",\"geometryJson\":\"{}\",\"physicsJson\":\"{}\",\"contentJson\":\"[]\",\"group\":null}]}")]
    [InlineData("{\"format\":\"lifeverse.build\",\"version\":1,\"name\":\"x\",\"baseOffset\":0,\"objects\":[{\"primitiveType\":\"cube\",\"name\":\"a\",\"description\":\"\",\"x\":0,\"y\":0,\"z\":0,\"rotationX\":0,\"rotationY\":0,\"rotationZ\":0,\"scaleX\":500,\"scaleY\":1,\"scaleZ\":1,\"materialJson\":\"{}\",\"geometryJson\":\"{}\",\"physicsJson\":\"{}\",\"contentJson\":\"[]\",\"group\":null}]}")]
    public void BothSidesRejectTheSameInputs(string json)
    {
        Assert.Null(BuildPackages.Parse(json, out _));
        Assert.Null(PackageDocument.Parse(json, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}

public sealed class ContentAssetsTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();
    private static string Entry(Guid id, string name = "Script", string category = "scripts") => $"{{\"assetId\":\"{id}\",\"name\":\"{name}\",\"category\":\"{category}\"}}";

    [Fact]
    public void Settings_AcceptsAWellFormedList()
    {
        Assert.Null(ContentAssets.ValidateSettings($"[{Entry(A)},{Entry(B, "Som", "sounds")}]"));
        Assert.Null(ContentAssets.ValidateSettings("[]")); Assert.Null(ContentAssets.ValidateSettings(null)); Assert.Null(ContentAssets.ValidateSettings(""));
        Assert.Equal(new[] { A, B }, ContentAssets.AssetIds($"[{Entry(A)},{Entry(B)}]"));
    }

    [Theory]
    [InlineData("{}")] [InlineData("[1]")] [InlineData("[{\"name\":\"x\",\"category\":\"scripts\"}]")] [InlineData("[{\"assetId\":\"nope\",\"name\":\"x\",\"category\":\"scripts\"}]")]
    [InlineData("not json")]
    public void Settings_RejectsMalformedEntries(string json) => Assert.NotNull(ContentAssets.ValidateSettings(json));

    [Fact]
    public void Settings_RejectsDuplicatesUnknownCategoriesAndTooMany()
    {
        Assert.NotNull(ContentAssets.ValidateSettings($"[{Entry(A)},{Entry(A)}]"));
        Assert.NotNull(ContentAssets.ValidateSettings($"[{Entry(A, category: "malware")}]"));
        Assert.NotNull(ContentAssets.ValidateSettings($"[{Entry(A, name: new string('x', 121))}]"));
        Assert.NotNull(ContentAssets.ValidateSettings($"[{Entry(A, name: "  ")}]"));
        Assert.NotNull(ContentAssets.ValidateSettings("[" + string.Join(",", Enumerable.Range(0, ContentAssets.MaxEntries + 1).Select(_ => Entry(Guid.NewGuid()))) + "]"));
    }
}

public sealed class GltfLimitsTests
{
    /// A tiny but structurally real GLB: the header, one JSON chunk, no binary chunk.
    internal static byte[] Glb(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var padded = body.Concat(Enumerable.Repeat((byte)' ', (4 - body.Length % 4) % 4)).ToArray();
        var bytes = new byte[20 + padded.Length];
        "glTF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)padded.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0x4E4F534A);
        padded.CopyTo(bytes, 20);
        return bytes;
    }

    internal static string Model(int indices, int materials = 1, int textures = 0, int nodes = 1) =>
        $"{{\"asset\":{{\"version\":\"2.0\"}},\"accessors\":[{{\"count\":{indices}}},{{\"count\":100}}],\"meshes\":[{{\"primitives\":[{{\"attributes\":{{\"POSITION\":1}},\"indices\":0}}]}}]," +
        $"\"materials\":[{string.Join(",", Enumerable.Repeat("{}", materials))}],\"textures\":[{string.Join(",", Enumerable.Repeat("{}", textures))}],\"nodes\":[{string.Join(",", Enumerable.Repeat("{}", nodes))}]}}";

    [Fact]
    public void Inspect_CountsTrianglesMaterialsTexturesAndParts()
    {
        var stats = GltfLimits.Inspect(Glb(Model(indices: 3000, materials: 3, textures: 2, nodes: 4)), binary: true, out var error);
        Assert.Null(error); Assert.NotNull(stats);
        Assert.Equal(1000, stats!.Triangles); Assert.Equal(3, stats.Materials); Assert.Equal(2, stats.Textures); Assert.Equal(4, stats.Nodes); Assert.Equal(1, stats.Meshes);
        Assert.Null(GltfLimits.Check(stats));
    }

    [Fact]
    public void Inspect_FallsBackToVertexCountWithoutIndices()
    {
        var json = "{\"asset\":{\"version\":\"2.0\"},\"accessors\":[{\"count\":600}],\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0}}]}]}";
        Assert.Equal(200, GltfLimits.Inspect(Encoding.UTF8.GetBytes(json), binary: false, out _)!.Triangles);
    }

    [Fact]
    public void Check_RejectsModelsOverTheLimits()
    {
        Assert.NotNull(GltfLimits.Check(GltfLimits.Inspect(Glb(Model(indices: (GltfLimits.MaxTriangles + 1) * 3)), true, out _)!));
        Assert.NotNull(GltfLimits.Check(GltfLimits.Inspect(Glb(Model(3, materials: GltfLimits.MaxMaterials + 1)), true, out _)!));
        Assert.NotNull(GltfLimits.Check(GltfLimits.Inspect(Glb(Model(3, textures: GltfLimits.MaxTextures + 1)), true, out _)!));
        Assert.NotNull(GltfLimits.Check(GltfLimits.Inspect(Glb(Model(3, nodes: GltfLimits.MaxNodes + 1)), true, out _)!));
        Assert.NotNull(GltfLimits.Check(new GltfStats(0, 0, 0, 0, 0, 1, 0)));
    }

    [Fact]
    public void Inspect_RejectsDamagedOrForeignFiles()
    {
        Assert.Null(GltfLimits.Inspect([1, 2, 3], true, out var e1)); Assert.NotNull(e1);
        var truncated = Glb(Model(3)); Array.Resize(ref truncated, 30);
        Assert.Null(GltfLimits.Inspect(truncated, true, out var e2)); Assert.NotNull(e2);
        Assert.Null(GltfLimits.Inspect(Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"1.0\"}}"), false, out var e3)); Assert.NotNull(e3);
        Assert.Null(GltfLimits.Inspect(Encoding.UTF8.GetBytes("not json at all"), false, out var e4)); Assert.NotNull(e4);
    }
}

public sealed class ModelLimitsTests
{
    [Fact]
    public void ClientAndServerLimitsAreTheSame()
    {
        Assert.Equal(LifeVerse.Shared.Building.ModelLimits.MaxTriangles, GltfLimits.MaxTriangles);
        Assert.Equal(LifeVerse.Shared.Building.ModelLimits.MaxMaterials, GltfLimits.MaxMaterials);
        Assert.Equal(LifeVerse.Shared.Building.ModelLimits.MaxTextures, GltfLimits.MaxTextures);
        Assert.Equal(LifeVerse.Shared.Building.ModelLimits.MaxNodes, GltfLimits.MaxNodes);
    }

    [Fact]
    public void Check_ExplainsWhatToFix()
    {
        var limits = LifeVerse.Shared.Building.ModelLimits.Check;
        Assert.Null(limits(1000, 3, 2, 10, 1024));
        Assert.Contains("triângulos", limits(LifeVerse.Shared.Building.ModelLimits.MaxTriangles + 1, 1, 0, 1, 1024));
        Assert.Contains("materiais", limits(100, LifeVerse.Shared.Building.ModelLimits.MaxMaterials + 1, 0, 1, 1024));
        Assert.Contains("texturas", limits(100, 1, LifeVerse.Shared.Building.ModelLimits.MaxTextures + 1, 1, 1024));
        Assert.Contains("partes", limits(100, 1, 0, LifeVerse.Shared.Building.ModelLimits.MaxNodes + 1, 1024));
        Assert.Contains("MB", limits(100, 1, 0, 1, LifeVerse.Shared.Building.ModelLimits.MaxBytes + 1));
        Assert.NotNull(limits(0, 0, 0, 1, 1024));
    }
}
