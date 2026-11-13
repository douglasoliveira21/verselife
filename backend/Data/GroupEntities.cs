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
    /// <summary>The title shown above the name of a member who has this cargo and the group active (empty: the name of the cargo).</summary>
    public string Title { get; set; } = string.Empty;
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
    /// <summary>After this moment the announcement stops showing to members (null: never).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    /// <summary>An https link attached to the announcement (a picture, a document), or empty.</summary>
    public string AttachmentUrl { get; set; } = string.Empty;
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

/// <summary>A question the group votes on. Options are the answers (2 to 8); the criterion says what makes one of them win.</summary>
public sealed class GroupProposal
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid AuthorUserId { get; set; }
    public required string Title { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>The answers, as a JSON array of texts.</summary>
    public string OptionsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    /// <summary>Only members whose cargo ranks at least this high vote (0: every member).</summary>
    public int MinRank { get; set; }
    /// <summary>The share of the people who may vote that has to vote for the result to count (0 to 100).</summary>
    public int QuorumPercent { get; set; }
    /// <summary>majority (more than half of the votes), two_thirds, unanimous or plurality (the most voted wins).</summary>
    public string Criterion { get; set; } = "majority";
    /// <summary>open, approved (an option won), rejected (nobody reached the criterion or the quorum was missed) or cancelled.</summary>
    public string Status { get; set; } = "open";
    /// <summary>The winning option (approved only).</summary>
    public int? WinningOption { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    /// <summary>How many people could vote when it closed (kept so the quorum stays explainable later).</summary>
    public int EligibleAtClose { get; set; }
}

/// <summary>One person's vote on a proposal: one vote each, never changed.</summary>
public sealed class GroupVote
{
    public Guid Id { get; set; }
    public Guid ProposalId { get; set; }
    public Guid UserId { get; set; }
    public int Option { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
