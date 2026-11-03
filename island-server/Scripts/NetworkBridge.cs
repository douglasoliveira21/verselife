using Godot;

namespace LifeVerse.IslandServer.Scripts;

public partial class NetworkBridge : Node
{
    private ServerMain Server => GetNode<ServerMain>("/root/Server");

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    public void Authenticate(string token, int protocolVersion) => Server.Authenticate(Multiplayer.GetRemoteSenderId(), token, protocolVersion);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    public void SubmitInput(long sequence, float x, float z, int flags) => Server.SubmitInput(Multiplayer.GetRemoteSenderId(), sequence, x, z, flags);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    public void SendChat(string message) => Server.SendChat(Multiplayer.GetRemoteSenderId(), message);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    public void SubmitBuildOperation(string operationJson) => Server.SubmitBuildOperation(Multiplayer.GetRemoteSenderId(), operationJson);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    public void SubmitTerrainEdit(int tool, float x, float z, float radius, float strength, int layer) => Server.SubmitTerrainEdit(Multiplayer.GetRemoteSenderId(), tool, x, z, radius, strength, layer);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    public void RequestTerrain() => Server.RequestTerrain(Multiplayer.GetRemoteSenderId());
[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]    public void TouchObject(string objectId) => Server.TouchObject(Multiplayer.GetRemoteSenderId(), objectId);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    public void SubmitSeaLevel(float level) => Server.SubmitSeaLevel(Multiplayer.GetRemoteSenderId(), level);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    public void UndoTerrain() => Server.UndoTerrain(Multiplayer.GetRemoteSenderId());

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    public void RedoTerrain() => Server.RedoTerrain(Multiplayer.GetRemoteSenderId());

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    public void SubmitTerrainImport(byte[] heights) => Server.SubmitTerrainImport(Multiplayer.GetRemoteSenderId(), heights);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void SeaLevelChanged(float level, long revision) { }
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void TerrainPatched(byte[] patch, long revision) { }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void TerrainFull(byte[] data, bool canEdit) { }
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void TerrainEdited(int tool, float x, float z, float radius, float strength, int layer, long revision) { }
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void TerrainEditRejected(string code) { }
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void AuthResult(bool success, string code, long serverTick) { }
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void SpawnPlayer(int peerId, string avatarId, string displayName, string appearanceJson, float x, float y, float z, int state) { }
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void DespawnPlayer(int peerId, bool suspended) { }
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)] public void Snapshot(byte[] payload) { }
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void ReceiveChat(int peerId, string displayName, string message, long unixMilliseconds) { }
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void ProtocolRejected(int serverVersion) { }
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void BuildOperationApplied(string operationJson) { }
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void BuildOperationRejected(string code) { }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void EnterVehicle(int vehicleId) => Server.EnterVehicle(Multiplayer.GetRemoteSenderId(), vehicleId);
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void ExitVehicle() => Server.ExitVehicle(Multiplayer.GetRemoteSenderId());
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)] public void SubmitVehicleInput(float throttle, float steer, bool brake) => Server.SubmitVehicleInput(Multiplayer.GetRemoteSenderId(), throttle, steer, brake);
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void VehicleSpawned(int vehicleId, string name, string type, float x, float y, float z, float yaw) { }
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)] public void VehicleSnapshots(byte[] payload) { }
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)] public void VehicleRejected(string code) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 1)] public void SubmitVoice(long sequence, byte[] data) => Server.SubmitVoice(Multiplayer.GetRemoteSenderId(), sequence, data);
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 2)] public void ReceiveVoice(int speakerPeer, long sequence, byte[] data) { }
}
