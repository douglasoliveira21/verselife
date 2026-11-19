namespace LifeVerse.Wire;

// As 44 mensagens do jogo: 22 do cliente para o servidor e 22 do servidor para o cliente. Mesmos nomes e a mesma ordem de argumentos dos 44 RPCs do
// protocolo 11 (ver docs/unity-migration/MIGRATION_AUDIT.md, seção 6.1). Dois tipos ficaram mais justos porque o valor sempre coube neles:
// sequências e o tick do servidor são uint (eram long) e as flags de entrada são byte (InputFlags já é um byte).

// ---- cliente → servidor ----------------------------------------------------------------------------------------------------------------

public sealed record AuthenticateMessage(string Token, int ProtocolVersion) : IWireMessage;
public sealed record SendChatMessage(string Message) : IWireMessage;
public sealed record SetFlightMessage(bool Enabled) : IWireMessage;
public sealed record SubmitBuildOperationMessage(string OperationJson) : IWireMessage;
public sealed record SubmitTerrainEditMessage(int Tool, float X, float Z, float Radius, float Strength, int Layer) : IWireMessage;
public sealed record SubmitTerrainImportMessage(byte[] Heights) : IWireMessage;
public sealed record SubmitSeaLevelMessage(float Level) : IWireMessage;
public sealed record UndoTerrainMessage : IWireMessage;
public sealed record RedoTerrainMessage : IWireMessage;
public sealed record RequestTerrainMessage : IWireMessage;
public sealed record RefreshAppearanceMessage : IWireMessage;
public sealed record AttachmentLiveMessage(string Id, float[] Values) : IWireMessage;
public sealed record RequestTeleportMessage(string ParcelId, float X, float Z) : IWireMessage;
public sealed record TouchObjectMessage(string ObjectId) : IWireMessage;
public sealed record ScriptDialogResponseMessage(string Json) : IWireMessage;
public sealed record EnterVehicleMessage(int VehicleId) : IWireMessage;
public sealed record ExitVehicleMessage : IWireMessage;
public sealed record GestureMessage(int GestureId) : IWireMessage;
public sealed record SubmitInputMessage(uint Sequence, float X, float Z, byte Flags) : IWireMessage;
public sealed record SubmitVehicleInputMessage(float Throttle, float Steer, bool Brake) : IWireMessage;
public sealed record HeadLookMessage(float Pitch) : IWireMessage;
public sealed record SubmitVoiceMessage(uint Sequence, byte[] Data) : IWireMessage;

// ---- servidor → cliente ----------------------------------------------------------------------------------------------------------------

public sealed record AuthResultMessage(bool Success, string Code, uint ServerTick) : IWireMessage;
public sealed record ProtocolRejectedMessage(int ServerVersion) : IWireMessage;
public sealed record SpawnPlayerMessage(int PeerId, string AvatarId, string DisplayName, string AppearanceJson, float X, float Y, float Z, int State) : IWireMessage;
public sealed record DespawnPlayerMessage(int PeerId, bool Suspended) : IWireMessage;
public sealed record AppearanceChangedMessage(int PeerId, string DisplayName, string AppearanceJson) : IWireMessage;
public sealed record AttachmentLiveChangedMessage(int PeerId, string Id, float[] Values) : IWireMessage;
public sealed record ReceiveChatMessage(int PeerId, string DisplayName, string Message, long UnixMilliseconds) : IWireMessage;
public sealed record BuildOperationAppliedMessage(string OperationJson) : IWireMessage;
public sealed record BuildOperationRejectedMessage(string Code) : IWireMessage;
public sealed record TerrainFullMessage(byte[] Data, bool CanEdit) : IWireMessage;
public sealed record TerrainPatchedMessage(byte[] Patch, long Revision) : IWireMessage;
public sealed record TerrainEditedMessage(int Tool, float X, float Z, float Radius, float Strength, int Layer, long Revision) : IWireMessage;
public sealed record TerrainEditRejectedMessage(string Code) : IWireMessage;
public sealed record SeaLevelChangedMessage(float Level, long Revision) : IWireMessage;
public sealed record TeleportResultMessage(bool Ok, string Code, float X, float Z, float Yaw) : IWireMessage;
public sealed record VehicleSpawnedMessage(int VehicleId, string Name, string Type, float X, float Y, float Z, float Yaw) : IWireMessage;
public sealed record VehicleRejectedMessage(string Code) : IWireMessage;
public sealed record GestureChangedMessage(int PeerId, int GestureId) : IWireMessage;
public sealed record SnapshotMessage(byte[] Payload) : IWireMessage;
public sealed record VehicleSnapshotsMessage(byte[] Payload) : IWireMessage;
public sealed record HeadLookChangedMessage(int PeerId, float Pitch) : IWireMessage;
public sealed record ReceiveVoiceMessage(int SpeakerPeer, uint Sequence, byte[] Data) : IWireMessage;
