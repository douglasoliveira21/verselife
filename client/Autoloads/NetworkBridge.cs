using Godot;

namespace LifeVerse.Client.Autoloads;

public partial class NetworkBridge : Node
{
    private NetworkManager Manager => GetNode<NetworkManager>("/root/NetworkManager");
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void Authenticate(string token, int protocolVersion) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)] public void SubmitInput(long sequence, float x, float z, int flags) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void SendChat(string message) { }
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void AuthResult(bool success, string code, long serverTick) => Manager.OnAuthenticationResult(success, code, serverTick);
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void SpawnPlayer(int peerId, string avatarId, string displayName, string appearanceJson, float x, float y, float z, int state) => Manager.OnPlayerSpawned(peerId, avatarId, displayName, appearanceJson, new Vector3(x, y, z), state);
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void DespawnPlayer(int peerId, bool suspended) => Manager.OnPlayerDespawned(peerId, suspended);
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)] public void Snapshot(byte[] payload) => Manager.OnSnapshot(payload);
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void ReceiveChat(int peerId, string displayName, string message, long unixMilliseconds) => Manager.OnChatReceived(peerId, displayName, message, unixMilliseconds);
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void ProtocolRejected(int serverVersion) => Manager.OnProtocolRejected(serverVersion);
}
