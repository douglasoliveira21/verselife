using System.Text.Json;
using LifeVerse.Shared.Networking;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>
/// The API stamps island entry tickets with IslandEntry:ProtocolVersion from appsettings.json, while the client and
/// island server use NetworkProtocol.Version. If they drift, every player is rejected as "incompatible".
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

    [Fact]
    public void ClientAndIslandServerBridges_DeclareTheSameRpcMethods()
    {
        string Signatures(string relativePath) => string.Join("\n", System.Text.RegularExpressions.Regex
            .Matches(File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath)).Replace("\r", "").Replace("\n", " "), @"\[Rpc\([^\]]*\)\]\s*public void \w+\([^)]*\)")
            .Select(match => System.Text.RegularExpressions.Regex.Replace(match.Value, @"\s+", " ")).OrderBy(x => x, StringComparer.Ordinal));
        var client = Signatures("client/Autoloads/NetworkBridge.cs");
        Assert.NotEmpty(client);
        Assert.Equal(client, Signatures("island-server/Scripts/NetworkBridge.cs"));
    }

    [Fact]
    public void FlightStateChange_UsesReliableIdempotentRpc()
    {
        var client = File.ReadAllText(Path.Combine(RepositoryRoot(), "client", "Autoloads", "NetworkBridge.cs"));
        var server = File.ReadAllText(Path.Combine(RepositoryRoot(), "island-server", "Scripts", "NetworkBridge.cs"));
        const string reliable = "TransferMode = MultiplayerPeer.TransferModeEnum.Reliable";
        Assert.Matches($@"\[Rpc\([^\]]*{System.Text.RegularExpressions.Regex.Escape(reliable)}[^\]]*\)\]\s*public void SetFlight\(bool enabled\)", client);
        Assert.Matches($@"\[Rpc\([^\]]*{System.Text.RegularExpressions.Regex.Escape(reliable)}[^\]]*\)\]\s*public void SetFlight\(bool enabled\)", server);
    }
}
