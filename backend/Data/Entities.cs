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
    /// <summary>The picture shown on the profile: an image the person owns (shown to others through the profile, never by the asset itself).</summary>
    public Guid? ProfilePhotoAssetId { get; set; }
    /// <summary>"Vida real": what the person wants to say about themselves outside the game (optional).</summary>
    public string FirstLifeBio { get; set; } = string.Empty;
    public string Interests { get; set; } = string.Empty;
    /// <summary>standard or donor. Only the administration changes it; it never comes from the client.</summary>
    public string AccountType { get; set; } = "standard";
    public DateTimeOffset? DonorSince { get; set; }
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
    /// <summary>Which animation plays for which state when it is not the default: { "Walk": "builtin:happy", "Dance": "custom:{file}#{name}" }.</summary>
    public string AnimationOverridesJson { get; set; } = "{}";
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

/// <summary>An item a person brought to wear: a checked GLB (stored by hash) for one slot, either rigged clothing or a rigid piece fixed to a bone.</summary>
public sealed class AvatarWearable
{
    public Guid Id { get; set; }
    public Guid AvatarId { get; set; }
    public required string Name { get; set; }
    public required string Slot { get; set; }
    public string Layer { get; set; } = "base";
    /// <summary>clothing or attachment.</summary>
    public string Kind { get; set; } = "attachment";
    public required string ContentHash { get; set; }
    public required string StorageKey { get; set; }
    public int SizeBytes { get; set; }
    public int Triangles { get; set; }
    /// <summary>The asset of the inventory this item is: whoever holds an inventory item of this asset may wear the item (the maker, and whoever bought it or was given it).</summary>
    public Guid? AssetId { get; set; }
    /// <summary>The whole report of the check: it also holds what the item covers, whether it hides the hair and where a rigid piece goes.</summary>
    public string ReportJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>A file of animations a person uploaded (checked, stored by hash). Each animation in it can replace the default of a state.</summary>
public sealed class AvatarAnimationFile
{
    public Guid Id { get; set; }
    public Guid AvatarId { get; set; }
    public required string Name { get; set; }
    public required string ContentHash { get; set; }
    public required string StorageKey { get; set; }
    public int SizeBytes { get; set; }
    public string ReportJson { get; set; } = "{}";
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
    /// <summary>livre, moderado or adulto.</summary>
    public string ContentRating { get; set; } = "livre";
    public string Language { get; set; } = "pt-BR";
    public bool ShowInSearch { get; set; } = true;
    public bool ShowOnMap { get; set; } = true;
    /// <summary>Temporarily closed: only the owner and administrators can come in. Whether the island is public is kept, so opening it again brings it back as it was.</summary>
    public bool AccessClosed { get; set; }
    /// <summary>The look of the whole island (hour, weather, fog, light, sky) where no parcel sets its own, as clean ParcelEnvironment JSON. Kept apart from EnvironmentJson, which the island server rewrites when it saves.</summary>
    public string LookJson { get; set; } = "{}";
    /// <summary>Looks the owner saved by name, as a JSON array of {name, look}.</summary>
    public string LookPresetsJson { get; set; } = "[]";
    /// <summary>The music of the whole island where no parcel plays its own: {"musicUrl","volume","autoplay","name"}, or {} for none.</summary>
    public string MediaJson { get; set; } = "{}";
    public string EnvironmentJson { get; set; } = "{}";
    public string ResourceLimitsJson { get; set; } = "{}";
    public string RuntimeState { get; set; } = "suspended";
    /// <summary>The island every new avatar arrives on (at most one island is it; see WelcomeIsland).</summary>
    public bool IsWelcome { get; set; }
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
    /// <summary>After this moment the permission no longer counts (null: it does not expire).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>Where a person was last: the island and the spot, written by the island servers, so signing in again brings them back to the same place.</summary>
public sealed class PlayerLocation
{
    public Guid UserId { get; set; }
    public Guid IslandId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
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

/// <summary>The members of a group may enter (allow) or build (build) in a parcel: access given to a whole group instead of person by person.</summary>
public sealed class ParcelGroupAccess
{
    public Guid Id { get; set; }
    public Guid ParcelId { get; set; }
    public Guid GroupId { get; set; }
    /// <summary>allow or build.</summary>
    public required string Kind { get; set; }
    public Guid GrantedByUserId { get; set; }
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
    /// <summary>What a click on the object does: touch (default), sit, buy, pay, open, zoom, ignore or none.</summary>
    public string ClickAction { get; set; } = "touch";
    /// <summary>Whoever owned it before the current owner (set when it is sold or given away); the creator never changes.</summary>
    public Guid? LastOwnerUserId { get; set; }
    /// <summary>When a temporary object is removed by the server (set when it becomes temporary, cleared when it stops being one).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    /// <summary>What the owner may do with this object (LifeVerse.Shared.Items.ItemRights). Inherited from the things it is made of and from the package it was placed from; objects from before the rights keep everything.</summary>
    public int OwnerRights { get; set; } = LifeVerse.Shared.Items.ItemRights.All;
    /// <summary>What the next owner (somebody it is given or sold to) will be able to do.</summary>
    public int NextOwnerRights { get; set; } = LifeVerse.Shared.Items.ItemRights.All;
    /// <summary>Protected against accidental editing: it cannot be moved, changed, linked or deleted until it is unprotected (by the owner, the owner of the land or an island administrator).</summary>
    public bool EditLocked { get; set; }
    /// <summary>The physics settings the object had before an administrator froze it (it was physical, now it is static); null when it is not frozen. Releasing puts them back.</summary>
    public string? FrozenPhysicsJson { get; set; }
    /// <summary>A pivot of its own, in the object's own axes, measured from its centre (metres): what it turns and stretches around when the editor uses "pivot of the object".</summary>
    public float PivotX { get; set; }
    public float PivotY { get; set; }
    public float PivotZ { get; set; }
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
    /// <summary>What the creator says changed in this version (shown to the people who hold the item).</summary>
    public string Notes { get; set; } = string.Empty;
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
    // ---- rights (see LifeVerse.Shared.Items.ItemRights: Copy 1, Modify 2, Transfer 4, Resell 8). Items from before the rights existed keep everything.
    /// <summary>What the person holding it may do.</summary>
    public int OwnerRights { get; set; } = LifeVerse.Shared.Items.ItemRights.All;
    /// <summary>What the members of <see cref="GroupId"/> may do.</summary>
    public int GroupRights { get; set; }
    /// <summary>What whoever gets it next will be able to do.</summary>
    public int NextOwnerRights { get; set; } = LifeVerse.Shared.Items.ItemRights.All;
    /// <summary>The group the item is shared with (empty when it is not).</summary>
    public Guid? GroupId { get; set; }
    /// <summary>The item this one was copied from (to follow where copies come from).</summary>
    public Guid? CopiedFromItemId { get; set; }
    /// <summary>Whether people other than the holder can see who held it before.</summary>
    public bool ShowOwnerHistory { get; set; }
    /// <summary>active, or offered while a transfer waits for an answer (an offered item can be neither used nor offered again).</summary>
    public string Status { get; set; } = "active";
    /// <summary>Goes up with every change of owner or rights; a change made on an older value is refused, so two requests cannot both win.</summary>
    public long Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    /// <summary>When the item was last placed, worn or opened (the "recent" section).</summary>
    public DateTimeOffset? LastUsedAt { get; set; }
    /// <summary>Set when the item went to the bin together with its folder: restoring that folder brings it back.</summary>
    public Guid? DeletedWithFolderId { get; set; }
    /// <summary>Where the item came from when that matters to the person: "returned" (objects sent back to the inventory), "recovered" (deleted objects brought back) or "unpacked" (from a zip pack). Null for the rest.</summary>
    public string? Origin { get; set; }
    /// <summary>When the holder last looked at the versions of the asset: a version published after this (and after they got the item) is an update they have not seen.</summary>
    public DateTimeOffset? UpdateAckAt { get; set; }
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
    /// <summary>How many people the island server says are connected (sent with its heartbeat); used to keep a full island from taking more.</summary>
    public int PlayerCount { get; set; }
    /// <summary>The last report the server sent with its heartbeat (who is connected and how it is doing), as ServerReport JSON.</summary>
    public string ReportJson { get; set; } = "{}";
    public DateTimeOffset? ReportedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
}

/// <summary>
/// A person travelling from one island to another. The destination reserves a place (counted against its capacity), a short-lived single-use token lets the
/// person connect there, the destination server confirms the arrival, and only then is the previous island released. Until the arrival is confirmed the
/// person stays where they were. Status: prepared, connecting, arrived, cancelled, expired, failed.
/// </summary>
public sealed class TeleportTransfer
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public Guid? SourceIslandId { get; set; }
    public Guid DestinationIslandId { get; set; }
    public required string Status { get; set; }
    /// <summary>Chosen by the client for one trip: asking again with the same key gives the same transfer instead of a second one.</summary>
    public required string IdempotencyKey { get; set; }
    public float? TargetX { get; set; }
    public float? TargetZ { get; set; }
    /// <summary>The token issued for this transfer; it opens the destination once (<see cref="TokenUsedAt"/>) and only until <see cref="TokenExpiresAt"/>.</summary>
    public Guid? TokenId { get; set; }
    public DateTimeOffset? TokenExpiresAt { get; set; }
    public DateTimeOffset? TokenUsedAt { get; set; }
    public string? FailureCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>The reservation of the place at the destination ends here.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ArrivedAt { get; set; }
    /// <summary>The island server of the source has been told to let go of the person.</summary>
    public DateTimeOffset? SourceReleasedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}

/// <summary>A person banned from an island. Kept after it ends (lifted or expired) so the island has a history of who was banned, by whom and why.</summary>
public sealed class IslandBan
{
    public Guid Id { get; set; }
    public Guid IslandId { get; set; }
    public Guid UserId { get; set; }
    public Guid BannedByUserId { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>When the ban ends by itself (null: permanent).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
}

/// <summary>One thing somebody did to an island through the management window, kept as it was: who, what, to what, and the values before and after. Entries are only ever added.</summary>
public sealed class IslandAuditEntry
{
    public Guid Id { get; set; }
    public Guid IslandId { get; set; }
    public Guid ActorUserId { get; set; }
    /// <summary>geral, acesso, banimento, permissao or objeto.</summary>
    public required string Category { get; set; }
    public required string Action { get; set; }
    public required string Summary { get; set; }
    public string Resource { get; set; } = string.Empty;
    public string BeforeJson { get; set; } = "null";
    public string AfterJson { get; set; } = "null";
    /// <summary>Ties together the entries written by the same request.</summary>
    public Guid CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Something an administrator asked an island server to do (remove a person, tell everybody something). The server picks it up with its next heartbeat; one nobody picked up in time is dropped.</summary>
public sealed class IslandServerCommand
{
    public Guid Id { get; set; }
    public Guid IslandId { get; set; }
    public required string Kind { get; set; }
    public Guid? TargetUserId { get; set; }
    public string Text { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}

/// <summary>One sample of how an island server was doing (taken from its heartbeat, at most one a minute, kept for a day) for the charts of the Desempenho tab.</summary>
public sealed class IslandMetric
{
    public Guid Id { get; set; }
    public Guid IslandId { get; set; }
    public DateTimeOffset At { get; set; }
    public double Fps { get; set; }
    public double ProcessMs { get; set; }
    public double MemoryMb { get; set; }
    public double TickRate { get; set; }
    public int Players { get; set; }
}

/// <summary>A restart of an island that somebody asked for. Status: scheduled (waiting for its time), running (the server was told to stop and the island is being started again), completed, failed or cancelled.</summary>
public sealed class IslandRestart
{
    public Guid Id { get; set; }
    public Guid IslandId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset ScheduledFor { get; set; }
    public required string Status { get; set; }
    /// <summary>Seconds-before warnings already sent (JSON array of numbers), so each is sent once.</summary>
    public string WarnedJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Result { get; set; } = string.Empty;
}
