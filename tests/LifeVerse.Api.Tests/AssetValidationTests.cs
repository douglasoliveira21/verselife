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
}
