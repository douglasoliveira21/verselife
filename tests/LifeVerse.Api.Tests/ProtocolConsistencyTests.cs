using System.Text.Json;
using LifeVerse.Shared.Networking;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>
/// The API stamps island entry tickets with IslandEntry:ProtocolVersion from appsettings.json, and the island server
/// checks them against NetworkProtocol.Version. If they drift, every player is rejected as "incompatible".
/// </summary>
public sealed class ProtocolConsistencyTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LifeVerse.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void ApiTicketProtocolVersion_MatchesTheSharedProtocol()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "appsettings.json")));
        var configured = document.RootElement.GetProperty("IslandEntry").GetProperty("ProtocolVersion").GetInt32();
        Assert.Equal(NetworkProtocol.Version, configured);
    }
}
