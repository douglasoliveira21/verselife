namespace LifeVerse.Api.Data;

/// <summary>A cargo of a group: a name, a rank (who may act on whom) and the permissions it allows. The four starting roles are "system" roles with a key.</summary>
public sealed class GroupRole
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public required string Name { get; set; }
    /// <summary>owner, admin, moderator or member for the starting roles; empty for roles the group made.</summary>
    public string SystemKey { get; set; } = string.Empty;
    public int Rank { get; set; }
    /// <summary>The permissions, as names separated by commas.</summary>
    public string Permissions { get; set; } = string.Empty;
    public string Color { get; set; } = "#A9B3BD";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Somebody kept out of a group, for a time or for good.</summary>
public sealed class GroupBan
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public Guid BannedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>A person's request to join a group that decides who comes in.</summary>
public sealed class GroupJoinRequest
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public string Message { get; set; } = string.Empty;
    /// <summary>pending, approved, denied or cancelled.</summary>
    public required string Status { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}

/// <summary>One thing done in the administration of a group: who did it, what and to whom. Entries are only ever added.</summary>
public sealed class GroupAuditEntry
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid ActorUserId { get; set; }
    public required string Action { get; set; }
    public Guid? TargetUserId { get; set; }
    public string DetailJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A notice from the leaders to every member. Pinned ones stay on top.</summary>
public sealed class GroupAnnouncement
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid AuthorUserId { get; set; }
    public required string Title { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool Pinned { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Something a group wants a member to know, kept until it is read (a push only reaches people who are connected).</summary>
public sealed class GroupNotification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid GroupId { get; set; }
    /// <summary>group_announcement, group_event or group_event_cancelled.</summary>
    public required string Type { get; set; }
    public required string Title { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>The chest of a group: the coins its members gave and what its shops earned. The balance only moves together with a line in <see cref="GroupLedgerEntry"/>.</summary>
public sealed class GroupTreasury
{
    public Guid GroupId { get; set; }
    /// <summary>Coins in the chest. It is a concurrency token: two operations on the same balance at once cannot both win.</summary>
    public long Balance { get; set; }
    public long TotalIn { get; set; }
    public long TotalOut { get; set; }
    /// <summary>While frozen only the leader can take coins out (set by the leader when something looks wrong).</summary>
    public bool Frozen { get; set; }
    public Guid? FrozenByUserId { get; set; }
    public DateTimeOffset? FrozenAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One movement of the chest, kept forever: positive coins came in (contribution, sale), negative went out (withdrawal).</summary>
public sealed class GroupLedgerEntry
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    /// <summary>contribution, sale or withdrawal.</summary>
    public required string Type { get; set; }
    public long Amount { get; set; }
    public long BalanceAfter { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? TargetUserId { get; set; }
    public string Note { get; set; } = string.Empty;
    /// <summary>What it refers to: the order of a sale.</summary>
    public Guid? RefId { get; set; }
    public Guid? LedgerTransactionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
