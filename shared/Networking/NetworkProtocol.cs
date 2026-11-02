namespace LifeVerse.Shared.Networking;

public static class NetworkProtocol
{
    public const int Version = 1;
    public const int MaxSnapshotBytes = 32 * 1024;
    public const int MaxPlayersPerSnapshot = 256;
    public const int MaxChatCharacters = 256;
    public const float PositionScale = 100f;
    public const float VelocityScale = 100f;
}

[Flags]
public enum InputFlags : byte
{
    None = 0,
    Run = 1,
    Jump = 2,
    Fly = 4,
    Descend = 8,
    Sit = 16,
    Wave = 32,
    Dance = 64
}

public enum NetworkMotionState : byte
{
    Idle,
    Walk,
    Run,
    Jump,
    Fall,
    Fly,
    Sit,
    Wave,
    Dance
}

public readonly record struct MovementInput(uint Sequence, float X, float Z, InputFlags Flags);

public readonly record struct PlayerSnapshot(
    int PeerId,
    float X,
    float Y,
    float Z,
    float VelocityX,
    float VelocityY,
    float VelocityZ,
    float Yaw,
    NetworkMotionState Motion,
    uint LastProcessedInput);

public sealed record WorldSnapshot(uint ServerTick, IReadOnlyList<PlayerSnapshot> Players);
