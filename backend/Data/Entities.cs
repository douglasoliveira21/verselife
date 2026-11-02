namespace LifeVerse.Api.Data;

public sealed class User
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string NormalizedUsername { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string PasswordHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Avatar? Avatar { get; set; }
    public ICollection<Session> Sessions { get; set; } = [];
}

public sealed class Session
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid FamilyId { get; set; }
    public required string RefreshTokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedBySessionId { get; set; }
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }
}

public sealed class Avatar
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public required string DisplayName { get; set; }
    public required string BaseModel { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public AvatarAppearance? Appearance { get; set; }
    public ICollection<EquippedItem> EquippedItems { get; set; } = [];
}

public sealed class AvatarAppearance
{
    public Guid Id { get; set; }
    public Guid AvatarId { get; set; }
    public Avatar Avatar { get; set; } = null!;
    public required string SkinColor { get; set; }
    public required string HairColor { get; set; }
    public required string HairStyle { get; set; }
    public string BodySettingsJson { get; set; } = "{}";
    public float CameraSensitivity { get; set; } = 0.003f;
    public bool InvertCameraY { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EquippedItem
{
    public Guid Id { get; set; }
    public Guid AvatarId { get; set; }
    public Avatar Avatar { get; set; } = null!;
    public required string Slot { get; set; }
    public required string ItemKey { get; set; }
    public required string TintColor { get; set; }
    public DateTimeOffset EquippedAt { get; set; }
}

public sealed class Island
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public string Category { get; set; } = "social";
    public bool IsPublic { get; set; }
    public int Size { get; set; } = 256;
    public int MaxPlayers { get; set; } = 64;
    public float SpawnX { get; set; }
    public float SpawnY { get; set; } = 1f;
    public float SpawnZ { get; set; }
    public string EnvironmentJson { get; set; } = "{}";
    public string ResourceLimitsJson { get; set; } = "{}";
    public string RuntimeState { get; set; } = "suspended";
    public long SnapshotVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class IslandPermission
{
    public Guid Id { get; set; }
    public Guid IslandId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? GroupId { get; set; }
    public required string Permission { get; set; }
    public required string PrincipalType { get; set; }
    public Guid GrantedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class IslandSnapshot
{
    public Guid Id { get; set; }
    public Guid IslandId { get; set; }
    public long Version { get; set; }
    public required string TerrainJson { get; set; }
    public required string EnvironmentJson { get; set; }
    public required string IntegrityHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class IslandChange
{
    public Guid Id { get; set; }
    public Guid IslandId { get; set; }
    public long Version { get; set; }
    public Guid ActorUserId { get; set; }
    public required string Operation { get; set; }
    public required string PayloadJson { get; set; }
    public required string IntegrityHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BuildObject
{
    public Guid Id { get; set; }
    public Guid IslandId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid? GroupId { get; set; }
    public required string PrimitiveType { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float RotationX { get; set; }
    public float RotationY { get; set; }
    public float RotationZ { get; set; }
    public float ScaleX { get; set; } = 1;
    public float ScaleY { get; set; } = 1;
    public float ScaleZ { get; set; } = 1;
    public string MaterialJson { get; set; } = "{}";
    public string PhysicsJson { get; set; } = "{}";
    public int Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class BuildOperation
{
    public Guid Id { get; set; }
    public Guid IslandId { get; set; }
    public Guid? ObjectId { get; set; }
    public Guid ActorUserId { get; set; }
    public required string Operation { get; set; }
    public required string PayloadJson { get; set; }
    public int Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ServerInstance
{
    public Guid Id { get; set; }
    public Guid IslandId { get; set; }
    public required string Hostname { get; set; }
    public int Port { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastHeartbeatAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
}
