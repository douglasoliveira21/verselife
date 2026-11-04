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
