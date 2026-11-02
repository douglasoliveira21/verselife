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
    public bool IsPublic { get; set; }
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
