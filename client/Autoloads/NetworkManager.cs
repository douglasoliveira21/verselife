using Godot;

namespace LifeVerse.Client.Autoloads;

public partial class NetworkManager : Node
{
    public bool IsConnectedToIsland => Multiplayer.HasMultiplayerPeer() && Multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

    public Error ConnectToIsland(string host, int port, string temporaryEntryToken)
    {
        if (string.IsNullOrWhiteSpace(temporaryEntryToken)) return Error.Unauthorized;
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateClient(host, port);
        if (error == Error.Ok) Multiplayer.MultiplayerPeer = peer;
        return error;
    }

    public void Disconnect() => Multiplayer.MultiplayerPeer = null;
}
