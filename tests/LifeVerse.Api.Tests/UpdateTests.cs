using LifeVerse.Api.Modules.Platform;
using LifeVerse.Shared.Updates;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.6.0", 0, 6, 0)] [InlineData("1.2", 1, 2, 0)] [InlineData("v2.10.3", 2, 10, 3)] [InlineData("1.0.0-beta.1", 1, 0, 0)] [InlineData(" 3.4.5+build7 ", 3, 4, 5)]
    public void ParsesCommonVersionShapes(string text, int major, int minor, int patch)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        Assert.Equal(new ReleaseVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")] [InlineData("1")] [InlineData("a.b.c")] [InlineData("1.2.3.4")] [InlineData("1.-2.3")] [InlineData("latest")]
    public void RejectsAnythingElse(string? text) => Assert.False(ReleaseVersion.TryParse(text, out _));

    [Fact]
    public void ComparesNumericallyNotAsText()
    {
        Assert.True(new ReleaseVersion(0, 10, 0).CompareTo(new ReleaseVersion(0, 9, 9)) > 0);
        Assert.True(new ReleaseVersion(1, 0, 0).CompareTo(new ReleaseVersion(0, 99, 99)) > 0);
        Assert.Equal(0, new ReleaseVersion(1, 2, 3).CompareTo(new ReleaseVersion(1, 2, 3)));
    }

    [Theory]
    [InlineData("0.6.0", "0.6.0", "0.6.0", UpdateStatus.UpToDate)]
    [InlineData("0.6.0", "0.7.0", "0.6.0", UpdateStatus.Available)]
    [InlineData("0.5.0", "0.7.0", "0.6.0", UpdateStatus.Required)]
    [InlineData("0.9.0", "0.7.0", "0.6.0", UpdateStatus.UpToDate)]
    [InlineData("0.6.0", "", "", UpdateStatus.UpToDate)]
    [InlineData("0.6.0", "garbage", "also garbage", UpdateStatus.UpToDate)]
    [InlineData("dev", "9.9.9", "9.9.9", UpdateStatus.Unknown)]
    [InlineData(null, "1.0.0", "1.0.0", UpdateStatus.Unknown)]
    public void EvaluateDecidesWhatTheClientShouldDo(string? current, string? latest, string? minimum, UpdateStatus expected) =>
        Assert.Equal(expected, UpdateRules.Evaluate(current, latest, minimum));
}

public sealed class ReleaseInfoTests
{
    [Fact]
    public void ParsesTheApiAnswer()
    {
        var info = ReleaseInfo.TryParse("""{"latest":"0.7.0","minimum":"0.6.0","downloadUrl":"https://example.com/lifeverse.zip","notes":"Veículos"}""");
        Assert.Equal(new ReleaseInfo("0.7.0", "0.6.0", "https://example.com/lifeverse.zip", "Veículos"), info);
        Assert.True(info!.HasSafeDownloadLink);
    }

    [Theory]
    [InlineData("")] [InlineData("not json")] [InlineData("[]")] [InlineData("{}")] [InlineData("""{"latest":5}""")] [InlineData("""{"latest":"","minimum":""}""")]
    public void BrokenAnswersGiveNothing(string json) => Assert.Null(ReleaseInfo.TryParse(json));

    [Fact]
    public void OversizedAnswersAreIgnored() => Assert.Null(ReleaseInfo.TryParse("{\"latest\":\"1.0.0\",\"notes\":\"" + new string('x', 9000) + "\"}"));

    [Theory]
    [InlineData("https://example.com/a", true)] [InlineData("http://example.com/a", true)]
    [InlineData("", false)] [InlineData("file:///C:/Windows/System32/cmd.exe", false)] [InlineData("javascript:alert(1)", false)] [InlineData("not a url", false)] [InlineData("ftp://example.com/a", false)]
    public void OnlyWebLinksAreSafeToOpen(string url, bool safe) => Assert.Equal(safe, new ReleaseInfo("1.0.0", "1.0.0", url, "").HasSafeDownloadLink);
}

public sealed class ClientReleaseEndpointTests
{
    private static readonly ClientReleaseOptions Options = new() { Latest = " 0.7.0 ", Minimum = "0.6.0", DownloadUrl = "https://example.com/all", DownloadUrlWindows = "https://example.com/win.zip", DownloadUrlLinux = "https://example.com/linux.tar.gz", Notes = "n" };

    [Theory]
    [InlineData("windows", "https://example.com/win.zip")] [InlineData("Linux", "https://example.com/linux.tar.gz")] [InlineData("macos", "https://example.com/all")] [InlineData(null, "https://example.com/all")]
    public void PicksTheLinkForThePlatformElseTheGeneralOne(string? platform, string expected) => Assert.Equal(expected, ClientReleaseEndpoints.Describe(Options, platform).DownloadUrl);

    [Fact]
    public void TrimsVersionsAndCarriesNotes()
    {
        var info = ClientReleaseEndpoints.Describe(Options, "windows");
        Assert.Equal(("0.7.0", "0.6.0", "n"), (info.Latest, info.Minimum, info.Notes));
    }

    [Fact]
    public void NeverHandsOutAnUnsafeLink()
    {
        var bad = new ClientReleaseOptions { Latest = "1.0.0", DownloadUrl = "file:///etc/passwd", DownloadUrlWindows = "javascript:alert(1)" };
        Assert.Equal(string.Empty, ClientReleaseEndpoints.Describe(bad, "windows").DownloadUrl);
    }

    [Fact]
    public void LongNotesAreCut() => Assert.Equal(1000, ClientReleaseEndpoints.Describe(new ClientReleaseOptions { Latest = "1.0.0", Notes = new string('x', 5000) }, null).Notes.Length);

    [Fact]
    public void TheShippedConfigAnswersWithTheSameVersionAsTheClient()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var settings = File.ReadAllText(Path.Combine(root, "backend/appsettings.json"));
        var client = File.ReadAllText(Path.Combine(root, "client/Autoloads/AppManager.cs"));
        var clientVersion = System.Text.RegularExpressions.Regex.Match(client, "Version = \"([^\"]+)\"").Groups[1].Value;
        Assert.Contains($"\"Latest\": \"{clientVersion}\"", settings);
        Assert.Contains($"\"Minimum\": \"{clientVersion}\"", settings);
    }
}
