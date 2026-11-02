using LifeVerse.Api.Modules.Assets;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class AssetValidationTests
{
    [Fact]
    public async Task Validator_AcceptsGlbHeaderAndRejectsEmbeddedScripts()
    {
        var glb = new byte[16]; glb[0] = (byte)'g'; glb[1] = (byte)'l'; glb[2] = (byte)'T'; glb[3] = (byte)'F';
        var valid = await AssetValidator.InspectAsync("avatar.glb", "model/gltf-binary", glb, CancellationToken.None);
        Assert.True(valid.Valid);
        var unsafeFile = System.Text.Encoding.UTF8.GetBytes("glTF script javascript");
        var rejected = await AssetValidator.InspectAsync("avatar.glb", "model/gltf-binary", unsafeFile, CancellationToken.None);
        Assert.False(rejected.Valid);
    }

    [Fact]
    public async Task Validator_RejectsUnsupportedAndOversizedImages()
    {
        var invalid = await AssetValidator.InspectAsync("texture.bmp", "image/bmp", [1, 2, 3], CancellationToken.None);
        Assert.False(invalid.Valid);
        var png = new byte[24]; png[0] = 137; png[1] = 80; png[16] = 0x20; png[20] = 0x20;
        var accepted = await AssetValidator.InspectAsync("texture.png", "image/png", png, CancellationToken.None);
        Assert.True(accepted.Valid);
        Assert.Equal(32, accepted.Width);
    }
}
