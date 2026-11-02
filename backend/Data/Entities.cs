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
    public string Status { get; set; } = "active";
    public string PresenceStatus { get; set; } = "offline";
    public DateTimeOffset PresenceUpdatedAt { get; set; }
    public string PrivacyJson { get; set; } = "{\"presence\":\"friends\"}";
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
    public string EyeColor { get; set; } = "#4A3326";
    public string BeardStyle { get; set; } = "none";
    public string BodySettingsJson { get; set; } = "{}";
    public string FaceSettingsJson { get; set; } = "{}";
    public string OutfitsJson { get; set; } = "[]";
    public string AttachmentsJson { get; set; } = "[]";
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

public sealed class IslandParcel
{
    public Guid Id { get; set; }
    public Guid IslandId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid? GroupId { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public required string BoundsJson { get; set; }
    public double Area { get; set; }
    public int MaxObjects { get; set; } = 1000;
    public string PermissionsJson { get; set; } = "{\"enter\":true,\"build\":false,\"run_scripts\":false,\"rezzing\":false}";
    public string EnvironmentJson { get; set; } = "{}";
    public float SpawnX { get; set; }
    public float SpawnY { get; set; } = 1f;
    public float SpawnZ { get; set; }
    public int Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class ParcelChange
{
    public Guid Id { get; set; }
    public Guid ParcelId { get; set; }
    public Guid ActorUserId { get; set; }
    public required string Operation { get; set; }
    public required string PayloadJson { get; set; }
    public int Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BuildObject
{
    public Guid Id { get; set; }
    public Guid IslandId { get; set; }
    public Guid CreatorUserId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? ParentObjectId { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
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
    public string GeometryJson { get; set; } = "{}";
    public string PhysicsJson { get; set; } = "{}";
    public string PermissionsJson { get; set; } = "{\"modify\":true,\"copy\":true,\"transfer\":false,\"use\":true,\"move\":true,\"delete\":true,\"return\":true}";
    public string ContentJson { get; set; } = "[]";
    public string OwnerHistoryJson { get; set; } = "[]";
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

public sealed class Asset
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid CreatorUserId { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public string LicenseJson { get; set; } = "{}";
    public string Status { get; set; } = "ready";
    public int CurrentVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class AssetVersion
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public Asset Asset { get; set; } = null!;
    public int Version { get; set; }
    public required string ContentHash { get; set; }
    public required string StorageKey { get; set; }
    public string? ThumbnailKey { get; set; }
    public required string MimeType { get; set; }
    public long ByteSize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? DurationSeconds { get; set; }
    public string ValidationJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class InventoryFolder
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid? ParentId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class InventoryItem
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid AssetId { get; set; }
    public Guid? FolderId { get; set; }
    public required string DisplayName { get; set; }
    public bool IsFavorite { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class AssetPermission
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? GroupId { get; set; }
    public required string PrincipalType { get; set; }
    public required string Flags { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class WorldAssetInstance
{
    public Guid Id { get; set; }
    public Guid IslandId { get; set; }
    public Guid AssetId { get; set; }
    public Guid OwnerUserId { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float RotationX { get; set; }
    public float RotationY { get; set; }
    public float RotationZ { get; set; }
    public int Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class ObjectScript
{
    public Guid Id { get; set; }
    public Guid ObjectId { get; set; }
    public Guid OwnerUserId { get; set; }
    public required string Name { get; set; }
    public required string Source { get; set; }
    public int Version { get; set; } = 1;
    public string Status { get; set; } = "active";
    public int InstructionBudget { get; set; } = 10000;
    public int MemoryLimitKb { get; set; } = 256;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ScriptState
{
    public Guid Id { get; set; }
    public Guid ScriptId { get; set; }
    public required string Key { get; set; }
    public required string ValueJson { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ScriptExecution
{
    public Guid Id { get; set; }
    public Guid ScriptId { get; set; }
    public required string EventName { get; set; }
    public required string Status { get; set; }
    public int InstructionCount { get; set; }
    public string Error { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class HudDefinition
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public required string Name { get; set; }
    public required string LayoutJson { get; set; }
    public Guid? ScriptId { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
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
