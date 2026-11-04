namespace LifeVerse.Api.Data;

/// <summary>
/// One line of the history of who has held an item: when it was made, bought, given, sold on, copied. Always kept; shown to other people only when the
/// item allows it (<see cref="InventoryItem.ShowOwnerHistory"/>). Not tied to the item by a foreign key, so it survives the item being removed.
/// </summary>
public sealed class ItemOwnerRecord
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public Guid AssetId { get; set; }
    /// <summary>Who holds it from this moment.</summary>
    public Guid UserId { get; set; }
    /// <summary>Who held it before (empty when it was just made).</summary>
    public Guid? FromUserId { get; set; }
    /// <summary>created, purchase, transfer, gift, copy, resale, admin, legacy.</summary>
    public required string Reason { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// The audit trail of everything done to the rights and the ownership of items, including what was refused. Only ever added to. Not tied to the item by
/// a foreign key, so the trail stays when an item or an account is gone.
/// </summary>
public sealed class ItemAuditEntry
{
    public Guid Id { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid ActorUserId { get; set; }
    /// <summary>rights_change, share_group, copy, transfer_offer, transfer_accept, transfer_reject, transfer_cancel, gift, purchase, resale, history_setting, admin, denied.</summary>
    public required string Action { get; set; }
    /// <summary>ok or denied.</summary>
    public required string Outcome { get; set; }
    public Guid? ItemId { get; set; }
    public Guid? AssetId { get; set; }
    public Guid? FromUserId { get; set; }
    public Guid? ToUserId { get; set; }
    /// <summary>Why it was refused, or the before and after of a change.</summary>
    public string Detail { get; set; } = string.Empty;
}

/// <summary>
/// An item offered by one person to another (a gift, or a transfer). The item stays with the giver, locked ("offered"), until the receiver answers; accepting
/// moves it in one step, so it never exists twice and never belongs to nobody. Offers that nobody answers expire and release the item.
/// </summary>
public sealed class ItemOffer
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public Guid AssetId { get; set; }
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    /// <summary>gift or transfer.</summary>
    public required string Kind { get; set; }
    public string Message { get; set; } = string.Empty;
    /// <summary>The giver keeps their own item and the receiver gets a copy (only for items that can be copied). The giver's item is not locked.</summary>
    public bool KeepCopy { get; set; }
    /// <summary>pending, accepted, rejected, cancelled or expired.</summary>
    public required string Status { get; set; }
    /// <summary>Chosen by the sender: sending the same request twice gives the same offer, not two.</summary>
    public required string IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
    /// <summary>With KeepCopy, the receiver's new item.</summary>
    public Guid? ResultItemId { get; set; }
    /// <summary>Goes up with every answer; two answers at the same moment cannot both be applied.</summary>
    public long Revision { get; set; }
}
