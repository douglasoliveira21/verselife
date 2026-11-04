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
    public string? ClientMessageId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
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
    /// <summary>The name in lower case, so two groups cannot have the same name written differently.</summary>
    public string NameKey { get; set; } = string.Empty;
    /// <summary>A short tag of 2 to 5 letters or digits that goes before the name of members (optional, unique among groups).</summary>
    public string? Tag { get; set; }
    /// <summary>public (found in the search) or private (found only by invitation).</summary>
    public string Privacy { get; set; } = "public";
    /// <summary>open (anybody joins), request (the group approves) or invite (only by invitation).</summary>
    public string JoinPolicy { get; set; } = "request";
    public string AccentColor { get; set; } = "#12F2E0";
    public string Emblem { get; set; } = "people";
    public int MemberLimit { get; set; } = 500;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class GroupMember
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>The key of the role (owner, admin, moderator, member) or the name of a custom role: kept next to RoleId for what only needs to show it.</summary>
    public required string Role { get; set; }
    /// <summary>The cargo of the member. Members from before groups had cargos have none yet and count as their key says.</summary>
    public Guid? RoleId { get; set; }
    public DateTimeOffset? MutedUntil { get; set; }
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
