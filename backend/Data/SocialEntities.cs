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
    /// <summary>What the group is about (see GroupRules.Categories).</summary>
    public string Category { get; set; } = "social";
    public string Language { get; set; } = "pt-BR";
    /// <summary>livre, moderado or adulto.</summary>
    public string ContentRating { get; set; } = "livre";
    /// <summary>The wide picture on top of the group (an https address), or empty.</summary>
    public string CoverUrl { get; set; } = string.Empty;
    /// <summary>The island where the group meets (null: none).</summary>
    public Guid? HeadquartersIslandId { get; set; }
    /// <summary>Coins a person pays to join an open group (0: free).</summary>
    public long JoinFee { get; set; }
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
    /// <summary>The member asked not to be told about announcements, events and chat of this group.</summary>
    public bool NotificationsMuted { get; set; }
    /// <summary>The member lets this group be listed on their profile (only matters for groups others may see).</summary>
    public bool ShowInProfile { get; set; } = true;
    /// <summary>The group the person wears now: its tag and the title of their cargo show beside their name. At most one group per person is active.</summary>
    public bool IsActive { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}

public sealed class GroupInvite
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid InvitedUserId { get; set; }
    public Guid InvitedByUserId { get; set; }
    public required string Status { get; set; }
    /// <summary>A few words from who invited.</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>The cargo the person gets when they accept (null: the lowest one).</summary>
    public Guid? RoleId { get; set; }
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

/// <summary>A marriage inside the game: asked by one person, accepted by the other, ended by either. Only one marriage at a time per person.</summary>
public sealed class Marriage
{
    public Guid Id { get; set; }
    public Guid ProposerUserId { get; set; }
    public Guid TargetUserId { get; set; }
    /// <summary>proposed, married, declined, cancelled, expired or ended.</summary>
    public required string Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
    public DateTimeOffset? MarriedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public Guid? EndedByUserId { get; set; }
}
