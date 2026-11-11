using System.Text.Json;
using LifeVerse.Api.Modules.Assets;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>What the server accepts when somebody imports a picture, a sound or a model: every kind the game can use, and a clear refusal for the rest (cut-off files, files with another name, kinds nobody can play).</summary>
public sealed class AssetImportMatrixTests
{
    private static Task<AssetInspection> Inspect(string name, byte[] bytes, string? category = null) => AssetValidator.InspectAsync(name, string.Empty, bytes, CancellationToken.None, category);

    // ---- pictures ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task APngIsReadWithItsRealSize()
    {
        var png = await Inspect("textura.png", TestFiles.Png(64, 32));
        Assert.True(png.Valid, png.Error); Assert.Equal(("image/png", 64, 32), (png.MimeType, png.Width, png.Height));
    }

    [Fact]
    public async Task AJpegIsReadWithItsRealSizeInBothExtensions()
    {
        foreach (var name in new[] { "foto.jpg", "foto.JPEG" })
        { var jpeg = await Inspect(name, TestFiles.Jpeg(300, 200)); Assert.True(jpeg.Valid, jpeg.Error); Assert.Equal(("image/jpeg", 300, 200), (jpeg.MimeType, jpeg.Width, jpeg.Height)); }
    }

    [Theory]
    [InlineData("VP8 ")]
    [InlineData("VP8L")]
    [InlineData("VP8X")]
    public async Task EveryKindOfWebPIsAccepted(string kind)
    {
        var webp = await Inspect("imagem.webp", TestFiles.WebP(kind, 128, 96));
        Assert.True(webp.Valid, webp.Error); Assert.Equal(("image/webp", 128, 96), (webp.MimeType, webp.Width, webp.Height));
    }

    [Fact]
    public async Task APngWithACorruptBodyIsRefused()
    {
        var png = TestFiles.Png(32, 32);
        var idat = png.AsSpan().IndexOf("IDAT"u8);
        var broken = (byte[])png.Clone(); broken[idat + 8] ^= 0xFF; // flips a byte inside the pixel data: the chunk CRC no longer matches
        Assert.Contains("corrupt", (await Inspect("a.png", broken)).Error);
        Assert.True((await Inspect("a.png", png)).Valid);
    }

    [Fact]
    public async Task ImagesThatAreCutOffOrFalseAreRefusedWithAReason()
    {
        var png = TestFiles.Png(16, 16);
        Assert.Contains("cut off", (await Inspect("a.png", png[..(png.Length - 20)])).Error);
        Assert.False((await Inspect("a.png", TestFiles.Jpeg(16, 16))).Valid, "a JPEG named .png is not a PNG");
        Assert.False((await Inspect("a.jpg", png)).Valid, "a PNG named .jpg is not a JPEG");
        Assert.Contains("cut off", (await Inspect("a.jpg", TestFiles.Jpeg(16, 16, ended: false))).Error);
        Assert.False((await Inspect("a.webp", png)).Valid);
        Assert.False((await Inspect("a.png", new byte[100])).Valid);
        foreach (var name in new[] { "a.gif", "a.bmp", "a.tga", "a.svg", "a.psd", "a.exr", "a.hdr", "a.dds", "a.ktx2" }) Assert.False((await Inspect(name, TestFiles.Png(4, 4))).Valid, name + " is not offered");
        Assert.Contains("8192", (await Inspect("grande.png", TestFiles.Png(8193, 1))).Error);
        Assert.True((await Inspect("limite.png", TestFiles.Png(8192, 2))).Valid);
        Assert.False((await Inspect("zero.png", TestFiles.Png(0, 5))).Valid);
    }

    // ---- sounds ------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 1, 8)]
    [InlineData(1, 1, 16)]
    [InlineData(1, 2, 16)]
    [InlineData(1, 1, 24)]
    [InlineData(1, 2, 24)]
    [InlineData(1, 2, 32)]
    [InlineData(3, 1, 32)]
    [InlineData(3, 2, 32)]
    [InlineData(0xFFFE, 2, 24)]
    public async Task EveryPlainWavIsAccepted(int format, int channels, int bits)
    {
        var wav = await Inspect("som.wav", TestFiles.Wav(format, channels, bits));
        Assert.True(wav.Valid, $"{format}/{channels}/{bits}: {wav.Error}"); Assert.Equal("audio/wav", wav.MimeType);
    }

    [Fact]
    public async Task WavFilesThePlayerCannotPlayAreRefusedWithAnExplanation()
    {
        Assert.Contains("PCM", (await Inspect("adpcm.wav", TestFiles.Wav(format: 2))).Error);
        Assert.Contains("mono or stereo", (await Inspect("surround.wav", TestFiles.Wav(channels: 6))).Error);
        Assert.Contains("sample rate", (await Inspect("lento.wav", TestFiles.Wav(rate: 1000))).Error);
        Assert.False((await Inspect("a.wav", [.. TestFiles.Wav()[..30]])).Valid, "no sound data");
        Assert.False((await Inspect("a.wav", TestFiles.Ogg())).Valid);
    }

    [Fact]
    public async Task OggVorbisIsAcceptedAndOpusIsRefusedWithTheFix()
    {
        var vorbis = await Inspect("musica.ogg", TestFiles.Ogg());
        Assert.True(vorbis.Valid, vorbis.Error); Assert.Equal("audio/ogg", vorbis.MimeType);
        var opus = await Inspect("voz.ogg", TestFiles.Ogg(opus: true));
        Assert.False(opus.Valid); Assert.Contains("Vorbis", opus.Error);
        Assert.False((await Inspect("a.ogg", TestFiles.Wav())).Valid);
        Assert.False((await Inspect("a.ogg", new byte[100])).Valid);
    }

    [Fact]
    public async Task Mp3IsAcceptedWithOrWithoutATag()
    {
        foreach (var id3 in new[] { true, false })
        { var mp3 = await Inspect("musica.mp3", TestFiles.Mp3(id3)); Assert.True(mp3.Valid, mp3.Error); Assert.Equal("audio/mpeg", mp3.MimeType); }
        Assert.False((await Inspect("a.mp3", new byte[2000])).Valid);
        Assert.False((await Inspect("a.mp3", TestFiles.Wav())).Valid);
        foreach (var name in new[] { "a.flac", "a.m4a", "a.aac", "a.opus", "a.wma", "a.aiff" }) Assert.False((await Inspect(name, TestFiles.Wav())).Valid, name + " is not offered");
    }

    // ---- models ------------------------------------------------------------------------------------------------------------

    private static JsonElement Metadata(AssetInspection inspection) => JsonDocument.Parse(inspection.MetadataJson).RootElement.Clone();

    [Fact]
    public async Task AStaticMeshIsAcceptedAndNotMarkedAsRigged()
    {
        var model = await Inspect("cadeira.glb", TestFiles.Glb(), "models");
        Assert.True(model.Valid, model.Error); Assert.Equal("model/gltf-binary", model.MimeType);
        var metadata = Metadata(model); Assert.False(metadata.GetProperty("rigged").GetBoolean()); Assert.Equal(0, metadata.GetProperty("animations").GetInt32()); Assert.Null(model.Category);
    }

    [Fact]
    public async Task ARiggedMeshIsAcceptedAndKnownAsRigged()
    {
        var model = await Inspect("personagem.glb", TestFiles.Glb(new(Rigged: true, Joints: 3)), "models");
        Assert.True(model.Valid, model.Error);
        var metadata = Metadata(model); Assert.True(metadata.GetProperty("rigged").GetBoolean()); Assert.Equal(3, metadata.GetProperty("joints").GetInt32());
    }

    [Fact]
    public async Task ARiggedAndAnimatedMeshKeepsItsAnimationCount()
    {
        var model = await Inspect("dancarino.glb", TestFiles.Glb(new(Rigged: true, Animated: true)), "models");
        Assert.True(model.Valid, model.Error); Assert.Equal(1, Metadata(model).GetProperty("animations").GetInt32()); Assert.True(Metadata(model).GetProperty("rigged").GetBoolean());
        var withoutRig = await Inspect("moinho.glb", TestFiles.Glb(new(Rigged: false, Animated: true)), "models");
        Assert.True(withoutRig.Valid, withoutRig.Error); Assert.Equal(1, Metadata(withoutRig).GetProperty("animations").GetInt32());
    }

    [Fact]
    public async Task AFileWithOnlyMovementIsAnAnimationNoMatterHowItWasSent()
    {
        var clip = TestFiles.Glb(new(Mesh: false, Animated: true));
        foreach (var category in new[] { "models", "animations" })
        { var inspected = await Inspect("andar.glb", clip, category); Assert.True(inspected.Valid, $"{category}: {inspected.Error}"); Assert.Equal("animations", inspected.Category); Assert.True(Metadata(inspected).GetProperty("animationOnly").GetBoolean()); }
        var noMovement = await Inspect("estatico.glb", TestFiles.Glb(), "animations");
        Assert.False(noMovement.Valid); Assert.Contains("no animations", noMovement.Error);
        Assert.False((await Inspect("vazio.glb", TestFiles.Glb(new(Mesh: false)), "models")).Valid, "no mesh and no movement is nothing");
    }

    [Fact]
    public async Task AnAnimationSentAsAnAnimationMayAlsoCarryTheCharacter()
    {
        var inspected = await Inspect("mixamo.glb", TestFiles.Glb(new(Rigged: true, Animated: true)), "animations");
        Assert.True(inspected.Valid, inspected.Error);
    }

    [Fact]
    public async Task AModelWhoseDataIsCutOffIsRefusedBeforeItBreaksEveryonesGame()
    {
        var broken = await Inspect("quebrado.glb", TestFiles.Glb(new(Rigged: true, Animated: true, CutOff: true)), "models");
        Assert.False(broken.Valid); Assert.Contains("cut off", broken.Error);
        var full = TestFiles.Glb(); var truncatedFile = full[..(full.Length - 30)];
        Assert.False((await Inspect("curto.glb", truncatedFile, "models")).Valid, "a GLB shorter than its own header says");
    }

    [Fact]
    public async Task ASkeletonWithTooManyBonesIsRefused()
    {
        var heavy = await Inspect("polvo.glb", TestFiles.Glb(new(Rigged: true, Joints: GltfStructure.MaxJointsPerSkin + 1)), "models");
        Assert.False(heavy.Valid); Assert.Contains("bones", heavy.Error);
        Assert.True((await Inspect("grande.glb", TestFiles.Glb(new(Rigged: true, Joints: 200)), "models")).Valid);
    }

    [Fact]
    public async Task ModelsThatNeedOtherFilesOrAreInOtherFormatsAreRefused()
    {
        foreach (var name in new[] { "a.fbx", "a.obj", "a.dae", "a.blend", "a.3ds", "a.stl", "a.usdz" }) Assert.False((await Inspect(name, TestFiles.Glb())).Valid, name + " is not offered");
        Assert.False((await Inspect("a.glb", TestFiles.Png(4, 4))).Valid);
    }

    [Fact]
    public async Task AnythingElseIsRefusedWithTheListOfWhatIsAccepted()
    {
        var other = await Inspect("programa.exe", new byte[500]);
        Assert.False(other.Valid); Assert.Contains("MP3", other.Error); Assert.Contains("GLB", other.Error);
        Assert.False((await Inspect("a.zip", new byte[500])).Valid);
        Assert.False((await Inspect("a.png", [])).Valid);
    }
}

/// <summary>Models exported by Godot itself (the files in tests/fixtures are written by ImportFormatsSmoke) and the humanoid of the game, through the same checks as a real upload; and any real files named in LIFEVERSE_IMPORT_SAMPLES.</summary>
public sealed class ExportedModelTests
{
    private static string Fixtures()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) { var candidate = Path.Combine(dir.FullName, "tests", "fixtures"); if (Directory.Exists(candidate)) return candidate; var direct = Path.Combine(dir.FullName, "fixtures"); if (Directory.Exists(direct) && dir.Name == "tests") return direct; }
        throw new DirectoryNotFoundException("tests/fixtures");
    }

    private static Task<AssetInspection> Inspect(string name, string category = "models") => AssetValidator.InspectAsync(name + ".glb", string.Empty, File.ReadAllBytes(Path.Combine(Fixtures(), name + ".glb")), CancellationToken.None, category);

    [Fact]
    public async Task AStaticMeshFromGodotIsAccepted()
    {
        var model = await Inspect("static"); Assert.True(model.Valid, model.Error);
        var metadata = System.Text.Json.JsonDocument.Parse(model.MetadataJson).RootElement; Assert.False(metadata.GetProperty("rigged").GetBoolean()); Assert.Equal(0, metadata.GetProperty("animations").GetInt32());
    }

    [Fact]
    public async Task ARiggedMeshFromGodotIsAcceptedAndKnownAsRigged()
    {
        var model = await Inspect("rigged"); Assert.True(model.Valid, model.Error);
        var metadata = System.Text.Json.JsonDocument.Parse(model.MetadataJson).RootElement; Assert.True(metadata.GetProperty("rigged").GetBoolean()); Assert.Equal(2, metadata.GetProperty("joints").GetInt32());
    }

    [Fact]
    public async Task ARiggedAnimatedMeshFromGodotKeepsItsClip()
    {
        var model = await Inspect("rigged-animated"); Assert.True(model.Valid, model.Error);
        var metadata = System.Text.Json.JsonDocument.Parse(model.MetadataJson).RootElement; Assert.Equal(1, metadata.GetProperty("animations").GetInt32()); Assert.True(metadata.GetProperty("rigged").GetBoolean());
        Assert.True((await Inspect("rigged-animated", "animations")).Valid, "the same file is also a good animation");
    }

    [Fact]
    public async Task AnAnimatedMeshWithoutASkeletonIsAccepted() => Assert.True((await Inspect("static-animated")).Valid);

    [Fact]
    public async Task AnAnimationOnlyFileFromGodotBecomesAnAnimation()
    {
        foreach (var category in new[] { "models", "animations" })
        { var clip = await Inspect("animation-only", category); Assert.True(clip.Valid, $"{category}: {clip.Error}"); Assert.Equal("animations", clip.Category); }
    }

    [Fact]
    public async Task TheHumanoidOfTheGameIsARiggedModel()
    {
        var humanoid = await AssetValidator.InspectAsync("humano.glb", string.Empty, LifeVerse.Shared.Avatar.HumanoidTemplate.Build(), CancellationToken.None, "models");
        Assert.True(humanoid.Valid, humanoid.Error); Assert.True(System.Text.Json.JsonDocument.Parse(humanoid.MetadataJson).RootElement.GetProperty("rigged").GetBoolean());
    }

    [Fact]
    public async Task RealFilesFromThisMachineAreAccepted()
    {
        var list = Environment.GetEnvironmentVariable("LIFEVERSE_IMPORT_SAMPLES"); if (string.IsNullOrWhiteSpace(list)) return;
        foreach (var path in list.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var category = Path.GetExtension(path).ToLowerInvariant() is ".glb" ? "models" : null;
            var inspected = await AssetValidator.InspectAsync(Path.GetFileName(path), string.Empty, File.ReadAllBytes(path), CancellationToken.None, category);
            Assert.True(inspected.Valid, $"{path}: {inspected.Error}");
        }
    }
}

public sealed class AssetSizeLimitTests
{
    [Fact]
    public async Task ASoundBiggerThanThePlayerCanFetchIsRefused()
    {
        var wav = TestFiles.Wav(frames: 10); var big = new byte[26 * 1024 * 1024]; wav.CopyTo(big, 0);
        var refused = await AssetValidator.InspectAsync("grande.wav", string.Empty, big, CancellationToken.None);
        Assert.False(refused.Valid); Assert.Contains("25 MB", refused.Error);
        Assert.True((await AssetValidator.InspectAsync("pequeno.wav", string.Empty, wav, CancellationToken.None)).Valid);
    }
}
