using LifeVerse.Api.Modules.Assets;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class AssetValidationTests
{
    [Fact]
    public async Task Validator_AcceptsGlbHeaderAndRejectsEmbeddedScripts()
    {
        var glb = GltfLimitsTests.Glb(GltfLimitsTests.Model(indices: 300));
        var valid = await AssetValidator.InspectAsync("avatar.glb", "model/gltf-binary", glb, CancellationToken.None);
        Assert.True(valid.Valid, valid.Error);
        // a bare header is not a model: the JSON part with the meshes is required
        var headerOnly = new byte[16]; headerOnly[0] = (byte)'g'; headerOnly[1] = (byte)'l'; headerOnly[2] = (byte)'T'; headerOnly[3] = (byte)'F';
        Assert.False((await AssetValidator.InspectAsync("avatar.glb", "model/gltf-binary", headerOnly, CancellationToken.None)).Valid);
        // a model over the triangle limit is refused with a reason
        var heavy = await AssetValidator.InspectAsync("big.glb", "model/gltf-binary", GltfLimitsTests.Glb(GltfLimitsTests.Model(indices: (GltfLimits.MaxTriangles + 1) * 3)), CancellationToken.None);
        Assert.False(heavy.Valid); Assert.Contains("triangles", heavy.Error);
        var unsafeFile = System.Text.Encoding.UTF8.GetBytes("glTF script javascript");
        var rejected = await AssetValidator.InspectAsync("avatar.glb", "model/gltf-binary", unsafeFile, CancellationToken.None);
        Assert.False(rejected.Valid);
    }

    [Fact]
    public async Task Validator_RejectsUnsupportedAndOversizedImages()
    {
        var invalid = await AssetValidator.InspectAsync("texture.bmp", "image/bmp", [1, 2, 3], CancellationToken.None);
        Assert.False(invalid.Valid);
        var png = new byte[24]; png[0] = 137; png[1] = 80; png[19] = 0x20; png[23] = 0x20; // big-endian 32 x 32
        var accepted = await AssetValidator.InspectAsync("texture.png", "image/png", png, CancellationToken.None);
        Assert.True(accepted.Valid);
        Assert.Equal(32, accepted.Width);
    }

    [Fact]
    public async Task Validator_ExplainsThatStandaloneGltfCannotReferenceNeighbouringFiles()
    {
        var gltf = System.Text.Encoding.UTF8.GetBytes("""
            { "asset": { "version": "2.0" }, "buffers": [{ "uri": "mesh.bin", "byteLength": 12 }] }
            """);

        var result = await AssetValidator.InspectAsync("house.gltf", "model/gltf+json", gltf, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains("external files", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validator_AcceptsAUtf8BomInAValidGltfDocument()
    {
        var json = GltfLimitsTests.Model(indices: 3);
        var bytes = new byte[3 + System.Text.Encoding.UTF8.GetByteCount(json)];
        bytes[0] = 0xEF; bytes[1] = 0xBB; bytes[2] = 0xBF;
        System.Text.Encoding.UTF8.GetBytes(json, bytes.AsSpan(3));

        var result = await AssetValidator.InspectAsync("bom.gltf", "model/gltf+json", bytes, CancellationToken.None);

        Assert.True(result.Valid, result.Error);
    }

    [Fact]
    public async Task Validator_DoesNotConfuseMetadataUrlsWithExternalResources()
    {
        var json = GltfLimitsTests.Model(indices: 3).Replace("\"asset\":{\"version\":\"2.0\"}", "\"asset\":{\"version\":\"2.0\",\"generator\":\"https://example.test/exporter\"}");
        var result = await AssetValidator.InspectAsync("model.glb", "model/gltf-binary", GltfLimitsTests.Glb(json), CancellationToken.None);

        Assert.True(result.Valid, result.Error);
    }
}
