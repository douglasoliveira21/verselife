using LifeVerse.Api.Modules.Assets;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class MaterialAssetsTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

    [Fact]
    public void TextureIds_ReadsObjectAndFaceLevelImages()
    {
        var json = $"{{\"texture\":\"{A}\",\"faces\":[{{\"index\":0,\"texture\":\"{B}\",\"normalMap\":\"{C}\"}},{{\"index\":1,\"color\":\"#fff\"}}]}}";
        Assert.Equal(new[] { A, B, C }.OrderBy(x => x), MaterialAssets.TextureIds(json).OrderBy(x => x));
        Assert.Empty(MaterialAssets.TextureIds("{}")); Assert.Empty(MaterialAssets.TextureIds("not json")); Assert.Empty(MaterialAssets.TextureIds(null));
    }

    [Fact]
    public void Settings_AcceptsAFullyStyledMaterial()
    {
        var json = $"{{\"color\":\"#ffffff\",\"texture\":\"{A}\",\"uvRepeatX\":4,\"uvRepeatY\":2,\"uvOffsetX\":0.5,\"uvRotation\":90,\"emission\":\"#ff0000\",\"emissionStrength\":2,\"roughness\":0.3,\"light\":{{\"color\":\"#ffcc88\",\"energy\":2,\"range\":8}},\"builtin\":\"bricks\"}}";
        Assert.Null(MaterialAssets.ValidateSettings(json));
        Assert.Null(MaterialAssets.ValidateSettings("{}")); Assert.Null(MaterialAssets.ValidateSettings(null));
    }

    [Theory]
    [InlineData("{\"uvRepeatX\":0}")] [InlineData("{\"uvRepeatY\":1000}")] [InlineData("{\"uvRotation\":720}")] [InlineData("{\"emissionStrength\":99}")]
    [InlineData("{\"roughness\":2}")] [InlineData("{\"uvOffsetX\":\"a\"}")] [InlineData("{\"builtin\":\"../etc/passwd\"}")] [InlineData("{\"texture\":\"not-a-guid\"}")]
    [InlineData("{\"light\":{\"energy\":100,\"range\":5}}")] [InlineData("{\"light\":{\"energy\":1,\"range\":900}}")]
    [InlineData("{\"faces\":[{\"index\":0,\"uvRepeatX\":-3}]}")] [InlineData("[1,2]x")]
    public void Settings_RejectsOutOfRangeOrMalformedValues(string json) => Assert.NotNull(MaterialAssets.ValidateSettings(json));

    [Fact]
    public void Settings_LimitsTexturesAndFaces()
    {
        var many = string.Join(",", Enumerable.Range(0, MaterialAssets.MaxTexturesPerObject + 1).Select(_ => $"{{\"index\":0,\"texture\":\"{Guid.NewGuid()}\"}}"));
        Assert.NotNull(MaterialAssets.ValidateSettings($"{{\"faces\":[{many}]}}"));
        var faces = string.Join(",", Enumerable.Range(0, 33).Select(i => $"{{\"index\":{i}}}"));
        Assert.NotNull(MaterialAssets.ValidateSettings($"{{\"faces\":[{faces}]}}"));
    }

    [Theory]
    [InlineData("image/png", true)] [InlineData("image/jpeg", true)] [InlineData("image/webp", true)] [InlineData("image/svg+xml", false)] [InlineData("model/gltf-binary", false)] [InlineData(null, false)]
    public void OnlyRasterImagesCanBeTextures(string? mime, bool expected) => Assert.Equal(expected, MaterialAssets.IsImage(mime));
}
