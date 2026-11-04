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
    public string VoiceSettingsJson { get; set; } = "{\"pushToTalk\":true,\"voiceActivation\":false,\"sensitivity\":0.5,\"outputVolume\":1}";
    public string Bio { get; set; } = string.Empty;
    public string? ProfilePhotoUrl { get; set; }
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
    /// <summary>The checked GLB the avatar wears as its body; null: the procedural body.</summary>
    public Guid? CustomModelId { get; set; }
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

/// <summary>A body model a person uploaded: the checked GLB (stored by its hash), the report of the check and the map of its bones to the standard skeleton.</summary>
public sealed class AvatarModel
{
    public Guid Id { get; set; }
    public Guid AvatarId { get; set; }
    public required string Name { get; set; }
    public required string ContentHash { get; set; }
    public required string StorageKey { get; set; }
    public int SizeBytes { get; set; }
    public int Triangles { get; set; }
    public int Joints { get; set; }
    public float Height { get; set; }
    public float ScaleToStandard { get; set; } = 1f;
    public string ReportJson { get; set; } = "{}";
    public string BoneMapJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>A saved look: a named snapshot of the whole appearance that can be put on again, duplicated and shared with a code.</summary>
public sealed class AvatarLook
{
    public Guid Id { get; set; }
    public Guid AvatarId { get; set; }
    public required string Name { get; set; }
    public required string SnapshotJson { get; set; }
    /// <summary>When set, anybody with the code can copy this look.</summary>
    public string? ShareCode { get; set; }
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

    // ---- for sale (null price: not for sale)
    public long? SalePrice { get; set; }
    /// <summary>When set, only this person may buy.</summary>
    public Guid? SaleBuyerUserId { get; set; }
    public string SaleDescription { get; set; } = string.Empty;
    /// <summary>Whether the seller's objects on the parcel go to the buyer with it.</summary>
    public bool SaleIncludesObjects { get; set; } = true;

    // ---- offered for rent (null price: not offered)
    public long? RentPrice { get; set; }
    public int RentPeriodDays { get; set; } = 30;
    public int RentMinPeriods { get; set; } = 1;
    public int RentMaxPeriods { get; set; } = 12;
    public long RentDeposit { get; set; }
    public int RentGraceDays { get; set; } = 3;
    public bool RentAutoRenewAllowed { get; set; } = true;

    /// <summary>Who may walk in: public (everyone), list (the owner, the tenant and the people on the access list) or private (the owner and the tenant).</summary>
    public string AccessMode { get; set; } = "public";

    /// <summary>Who may build: owner (the owner and tenant), list (also the people on the builders list), everyone, or nobody (only island administrators).</summary>
    public string BuildMode { get; set; } = "owner";
    /// <summary>Objects of people who may not build here are sent back after this many minutes; null: never.</summary>
    public int? AutoReturnMinutes { get; set; }

    /// <summary>Which way people face when they arrive at SpawnX/SpawnZ, in degrees.</summary>
    public float ArrivalYaw { get; set; }
    /// <summary>free (people land where they ask), forced (always at the arrival point) or disabled (teleporting here is refused).</summary>
    public string TeleportMode { get; set; } = "free";
    /// <summary>The parcel's music: { musicUrl, volume, autoplay, name } or {}.</summary>
    public string MediaJson { get; set; } = "{}";
}

/// <summary>One line of a parcel's access list: a person allowed in (kind allow) or kept out (kind ban), for ever or until a time.</summary>
public sealed class ParcelAccess
{
    public Guid Id { get; set; }
    public Guid ParcelId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>allow or ban (who may enter) or build (who may build, when the parcel's build mode is "list").</summary>
    public required string Kind { get; set; }
    public Guid GrantedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>A rental of a parcel: the tenant may build in it while the contract is active. Status: active, grace (overdue, no building) or ended.</summary>
public sealed class ParcelRental
{
    public Guid Id { get; set; }
    public Guid ParcelId { get; set; }
    public Guid LandlordUserId { get; set; }
    public Guid TenantUserId { get; set; }
    public long PricePerPeriod { get; set; }
    public int PeriodDays { get; set; }
    public long DepositCoins { get; set; }
    public int GraceDays { get; set; }
    public int Periods { get; set; }
    public bool AutoRenew { get; set; }
    public required string Status { get; set; }
    public string? EndReason { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? GraceUntil { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>What happened to a parcel in money terms: sales, rent payments, deposits and refunds.</summary>
public sealed class ParcelTransaction
{
    public Guid Id { get; set; }
    public Guid ParcelId { get; set; }
    public required string Type { get; set; }
    public Guid? FromUserId { get; set; }
    public Guid? ToUserId { get; set; }
    public long Amount { get; set; }
    public string DataJson { get; set; } = "{}";
    public Guid? LedgerTransactionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One 4 x 4 m cell of the territorial grid and the parcel that owns it. The key (island, cx, cz) makes overlap impossible.</summary>
public sealed class ParcelCell
{
    public Guid IslandId { get; set; }
    public int Cx { get; set; }
    public int Cz { get; set; }
    public Guid ParcelId { get; set; }
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

public sealed class Vehicle
{
    public Guid Id { get; set; }
    public Guid IslandId { get; set; }
    public Guid OwnerUserId { get; set; }
    public required string Name { get; set; }
    public required string VehicleType { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; } = 1f;
    public float PositionZ { get; set; }
    public float RotationY { get; set; }
    public float Speed { get; set; }
    public float MaxSpeed { get; set; } = 20f;
    public float Mass { get; set; } = 1000f;
    public bool EngineOn { get; set; }
    public int Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class VehicleSeat
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public required string SeatType { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float OffsetZ { get; set; }
    public Guid? OccupantUserId { get; set; }
    public DateTimeOffset? OccupiedAt { get; set; }
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
    /// <summary>What this object costs the island's and parcel's budget (see BuildCost).</summary>
    public int Cost { get; set; }
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
