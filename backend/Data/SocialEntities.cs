namespace LifeVerse.Api.Data;

public sealed class SocialMessage
{
    public Guid Id { get; set; }
    public required string Channel { get; set; }
    public Guid SenderUserId { get; set; }
    public Guid? RecipientUserId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? IslandId { get; set; }
    public required string Content { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class Friendship
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FriendUserId { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class UserBlock
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BlockedUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SocialGroup
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class GroupMember
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public required string Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}

public sealed class GroupInvite
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid InvitedUserId { get; set; }
    public Guid InvitedByUserId { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SocialReport
{
    public Guid Id { get; set; }
    public Guid ReporterUserId { get; set; }
    public Guid? ReportedUserId { get; set; }
    public Guid? MessageId { get; set; }
    public Guid? IslandId { get; set; }
    public required string Reason { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RadioStation
{
    public Guid Id { get; set; }
    public Guid IslandId { get; set; }
    public Guid OwnerUserId { get; set; }
    public required string Name { get; set; }
    public required string StreamUrl { get; set; }
    public bool IsPlaying { get; set; }
    public float Volume { get; set; } = 1f;
    public float Range { get; set; } = 30f;
    public DateTimeOffset UpdatedAt { get; set; }
}
