using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Shared.Items;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Copying, giving and handing over items: the rules, and that nothing is ever duplicated or lost.</summary>
public sealed class ItemTransferTests : IDisposable
{
    private readonly string _store = Guid.NewGuid().ToString();
    private readonly LifeVerseDbContext _db;
    private readonly DateTimeOffset _now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private readonly User _ana, _bia, _cris;
    private int _key;

    public ItemTransferTests() { _db = Open(); _ana = NewUser("ana"); _bia = NewUser("bia"); _cris = NewUser("cris"); }
    public void Dispose() => _db.Dispose();
    private LifeVerseDbContext Open() => new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(_store).Options);
    private string Key() => "chave-" + (++_key).ToString("D6");

    private User NewUser(string username)
    {
        var u = new User { Id = Guid.NewGuid(), Username = username, NormalizedUsername = username.ToUpperInvariant(), Email = $"{username}@t.local", NormalizedEmail = $"{username}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now };
        _db.Users.Add(u); _db.SaveChanges(); return u;
    }

    private InventoryItem NewItem(User owner, int rights = 15, int next = 15, int group = 0, Guid? groupId = null)
    {
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner.Id, CreatorUserId = owner.Id, Name = "Peça", Category = "mesh", CreatedAt = _now, UpdatedAt = _now };
        var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = owner.Id, AssetId = asset.Id, DisplayName = "Peça", OwnerRights = rights, NextOwnerRights = next, GroupRights = group, GroupId = groupId, CreatedAt = _now, UpdatedAt = _now };
        _db.Assets.Add(asset); _db.InventoryItems.Add(item); _db.SaveChanges(); return item;
    }

    private Task<OfferResult> Offer(User from, InventoryItem item, User to, string kind = "gift", bool keep = false, string? key = null) => ItemTransfers.OfferAsync(_db, from.Id, item.Id, to.Id, kind, "olha isso", keep, key ?? Key(), _now, default);
    private async Task<InventoryItem> Fresh(InventoryItem item) => await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == item.Id);
    private Task<int> Held(Guid assetId) => _db.InventoryItems.CountAsync(i => i.AssetId == assetId && i.DeletedAt == null);

    // ---- copying ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ACopyNeedsTheCopyRightAndKeepsWhereItCameFrom()
    {
        var item = NewItem(_ana, rights: ItemRights.Modify | ItemRights.Transfer, next: ItemRights.Standard);
        var refused = await ItemTransfers.CopyAsync(_db, _ana.Id, item.Id, _now, default); Assert.False(refused.Ok); Assert.Equal("not_allowed", refused.Error); Assert.Equal(1, await Held(item.AssetId)); // a no-copy item cannot be copied by its own holder
        Assert.Contains(_db.ItemAudit, a => a.Action == "denied" && a.Detail.StartsWith("copy"));
        item.OwnerRights = ItemRights.Standard; await _db.SaveChangesAsync();
        var made = await ItemTransfers.CopyAsync(_db, _ana.Id, item.Id, _now, default); Assert.True(made.Ok); var copy = made.Item!;
        Assert.Equal((_ana.Id, item.Id, 2, ItemRights.Standard, 0), (copy.OwnerUserId, copy.CopiedFromItemId, await Held(item.AssetId), copy.NextOwnerRights, copy.GroupRights)); Assert.Contains(_db.ItemOwnerRecords, r => r.ItemId == copy.Id && r.Reason == "copy");
    }

    [Fact]
    public async Task NobodyCopiesAnItemThatIsNotTheirs()
    {
        var item = NewItem(_ana); var result = await ItemTransfers.CopyAsync(_db, _bia.Id, item.Id, _now, default); Assert.Equal("not_found", result.Error); Assert.Equal(1, await Held(item.AssetId));
        Assert.False((await ItemTransfers.CopyAsync(_db, _ana.Id, Guid.NewGuid(), _now, default)).Ok);
    }

    [Fact]
    public async Task AGroupMemberWithTheGroupCopyRightGetsACopyWithTheRightsOfANewOwner()
    {
        var group = new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, Name = "G", NameKey = "g", Privacy = "public", CreatedAt = _now, UpdatedAt = _now }; _db.SocialGroups.Add(group);
        _db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = _bia.Id, Role = "member", JoinedAt = _now }); await _db.SaveChangesAsync();
        var item = NewItem(_ana, rights: 15, next: ItemRights.Copy | ItemRights.Modify, group: ItemRights.Copy, groupId: group.Id);
        var made = await ItemTransfers.CopyAsync(_db, _bia.Id, item.Id, _now, default); Assert.True(made.Ok); Assert.Equal((_bia.Id, 3, 3), (made.Item!.OwnerUserId, made.Item.OwnerRights, made.Item.NextOwnerRights)); Assert.Equal(_ana.Id, (await Fresh(item)).OwnerUserId);
        Assert.False((await ItemTransfers.CopyAsync(_db, _cris.Id, item.Id, _now, default)).Ok);
    }

    // ---- offering -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyTheHolderWithTheTransferRightCanOfferAndTheItemIsLocked()
    {
        var item = NewItem(_ana, rights: ItemRights.Copy | ItemRights.Modify);
        Assert.Equal("not_allowed", (await Offer(_ana, item, _bia)).Error); Assert.Equal("not_found", (await Offer(_cris, item, _bia)).Error); // no Transfer; not theirs
        item.OwnerRights = ItemRights.Standard; await _db.SaveChangesAsync();
        var offer = await Offer(_ana, item, _bia); Assert.True(offer.Ok); Assert.Equal("pending", offer.Offer!.Status); Assert.Equal("offered", (await Fresh(item)).Status);
        Assert.Equal("item_busy", (await Offer(_ana, item, _cris)).Error); // already waiting for an answer
        Assert.Equal("item_busy", (await ItemRightsService.RequireAsync(_db, _ana.Id, item.Id, Right.Modify, "edit", _now, default)).Error); // locked for everything
        Assert.Contains(_db.ItemAudit, a => a.Action == "transfer_offer" && a.Outcome == "ok");
    }

    [Fact]
    public async Task AnOfferNeedsARealReachableRecipientAndASensibleRequest()
    {
        var item = NewItem(_ana);
        Assert.Equal("to_self", (await Offer(_ana, item, _ana)).Error); Assert.Equal("bad_kind", (await Offer(_ana, item, _bia, kind: "sell")).Error);
        Assert.Equal("bad_key", (await ItemTransfers.OfferAsync(_db, _ana.Id, item.Id, _bia.Id, "gift", null, false, "curta", _now, default)).Error);
        Assert.Equal("recipient_not_found", (await ItemTransfers.OfferAsync(_db, _ana.Id, item.Id, Guid.NewGuid(), "gift", null, false, Key(), _now, default)).Error);
        _cris.DeletedAt = _now; await _db.SaveChangesAsync(); Assert.Equal("recipient_not_found", (await Offer(_ana, item, _cris)).Error);
        _db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = _bia.Id, BlockedUserId = _ana.Id, CreatedAt = _now }); await _db.SaveChangesAsync(); Assert.Equal("recipient_not_found", (await Offer(_ana, item, _bia)).Error); // blocked: as if the person did not exist
        Assert.Equal("active", (await Fresh(item)).Status); Assert.Empty(_db.ItemOffers);
    }

    [Fact]
    public async Task SendingTheSameRequestTwiceIsTheSameOffer()
    {
        var item = NewItem(_ana); var first = await Offer(_ana, item, _bia, key: "mesma-chave-1"); var again = await Offer(_ana, item, _bia, key: "mesma-chave-1");
        Assert.True(first.Ok && again.Ok); Assert.Equal(first.Offer!.Id, again.Offer!.Id); Assert.Single(_db.ItemOffers);
        var other = NewItem(_ana); Assert.Equal("key_reused", (await Offer(_ana, other, _bia, key: "mesma-chave-1")).Error); // the same key for something else is not an answer to this
    }

    [Fact]
    public async Task OffersAreLimitedForTheSenderAndTheRecipient()
    {
        for (var i = 0; i < ItemTransfers.MaxOutgoing; i++) Assert.True((await Offer(_ana, NewItem(_ana), _bia)).Ok);
        Assert.Equal("too_many_offers", (await Offer(_ana, NewItem(_ana), _bia)).Error);
        for (var i = 0; i < ItemTransfers.MaxIncoming - ItemTransfers.MaxOutgoing; i++) { var from = NewUser("s" + i); Assert.True((await Offer(from, NewItem(from), _bia)).Ok); }
        var late = NewUser("tardio"); Assert.Equal("recipient_full", (await Offer(late, NewItem(late), _bia)).Error);
    }

    [Fact]
    public async Task KeepingACopyNeedsTheCopyRightAndDoesNotLockTheItem()
    {
        var noCopy = NewItem(_ana, rights: ItemRights.Modify | ItemRights.Transfer); Assert.Equal("keep_copy_needs_copy", (await Offer(_ana, noCopy, _bia, keep: true)).Error);
        var item = NewItem(_ana); Assert.True((await Offer(_ana, item, _bia, keep: true)).Ok); Assert.Equal("active", (await Fresh(item)).Status); Assert.True((await ItemRightsService.RequireAsync(_db, _ana.Id, item.Id, Right.Modify, "edit", _now, default)).Ok);
    }

    // ---- accepting ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AcceptingMovesTheSameItemAndGivesTheNextOwnerRights()
    {
        var item = NewItem(_ana, rights: 15, next: ItemRights.Modify | ItemRights.Transfer); item.FolderId = Guid.NewGuid(); item.IsFavorite = true; item.ShowOwnerHistory = true; await _db.SaveChangesAsync();
        var offer = (await Offer(_ana, item, _bia, kind: "transfer")).Offer!; var done = await ItemTransfers.AcceptAsync(_db, _bia.Id, offer.Id, _now, default);
        Assert.True(done.Ok); var moved = await Fresh(item);
        Assert.Equal((_bia.Id, 6, 0, 6, "active", null, false, false), (moved.OwnerUserId, moved.OwnerRights, moved.GroupRights, moved.NextOwnerRights, moved.Status, moved.FolderId, moved.IsFavorite, moved.ShowOwnerHistory));
        Assert.Equal(1, await Held(item.AssetId)); Assert.Equal(0, await _db.InventoryItems.CountAsync(i => i.OwnerUserId == _ana.Id)); // moved, not duplicated
        var step = await _db.ItemOwnerRecords.SingleAsync(r => r.ItemId == item.Id && r.Reason == "transfer"); Assert.Equal((_bia.Id, _ana.Id), (step.UserId, step.FromUserId));
        Assert.Contains(_db.ItemAudit, a => a.Action == "transfer_accept" && a.Outcome == "ok" && a.FromUserId == _ana.Id && a.ToUserId == _bia.Id);
        Assert.False(ItemRights.IsCopyable(moved.OwnerRights)); Assert.False((await ItemTransfers.CopyAsync(_db, _bia.Id, item.Id, _now, default)).Ok); // made no-copy for the next owner: the new holder cannot copy it
        Assert.Equal("not_found", (await ItemRightsService.RequireAsync(_db, _ana.Id, item.Id, Right.Modify, "edit", _now, default)).Error); // and the giver has nothing left
    }

    [Fact]
    public async Task AGiftIsRecordedAsAGiftAndAcceptingTwiceChangesNothingMore()
    {
        var item = NewItem(_ana); var offer = (await Offer(_ana, item, _bia)).Offer!;
        Assert.Equal("not_found", (await ItemTransfers.AcceptAsync(_db, _cris.Id, offer.Id, _now, default)).Error); Assert.Equal("not_found", (await ItemTransfers.AcceptAsync(_db, _ana.Id, offer.Id, _now, default)).Error); // only the receiver
        Assert.True((await ItemTransfers.AcceptAsync(_db, _bia.Id, offer.Id, _now, default)).Ok); Assert.True((await ItemTransfers.AcceptAsync(_db, _bia.Id, offer.Id, _now, default)).Ok);
        Assert.Single(_db.ItemOwnerRecords.Where(r => r.Reason == "gift")); Assert.Equal(1, await _db.ItemAudit.CountAsync(a => a.Action == "gift")); Assert.Equal(1, await Held(item.AssetId));
    }

    [Fact]
    public async Task KeepingACopyMakesExactlyOneCopyForTheReceiver()
    {
        var item = NewItem(_ana, next: ItemRights.Copy | ItemRights.Transfer); var offer = (await Offer(_ana, item, _bia, keep: true)).Offer!;
        var first = await ItemTransfers.AcceptAsync(_db, _bia.Id, offer.Id, _now, default); var second = await ItemTransfers.AcceptAsync(_db, _bia.Id, offer.Id, _now, default);
        Assert.True(first.Ok && second.Ok); Assert.Equal(first.Item!.Id, second.Item!.Id); Assert.Equal(2, await Held(item.AssetId));
        Assert.Equal((_ana.Id, 15), ((await Fresh(item)).OwnerUserId, (await Fresh(item)).OwnerRights)); Assert.Equal((_bia.Id, 5, item.Id), (first.Item.OwnerUserId, first.Item.OwnerRights, first.Item.CopiedFromItemId));
    }

    [Fact]
    public async Task AnOfferIsClosedWhenTheItemWasChangedOrRemovedMeanwhile()
    {
        var item = NewItem(_ana); var offer = (await Offer(_ana, item, _bia, keep: true)).Offer!; item.OwnerRights = ItemRights.Modify | ItemRights.Transfer; await _db.SaveChangesAsync(); // the giver gave up the Copy right after offering
        var result = await ItemTransfers.AcceptAsync(_db, _bia.Id, offer.Id, _now, default); Assert.Equal("item_unavailable", result.Error); Assert.Equal(1, await Held(item.AssetId)); Assert.Equal("cancelled", (await _db.ItemOffers.SingleAsync()).Status);
        var item2 = NewItem(_ana); var offer2 = (await Offer(_ana, item2, _bia)).Offer!; var tracked = await _db.InventoryItems.SingleAsync(i => i.Id == item2.Id); tracked.DeletedAt = _now; await _db.SaveChangesAsync(); // removed while waiting (by code that does not know about the lock)
        Assert.Equal("item_unavailable", (await ItemTransfers.AcceptAsync(_db, _bia.Id, offer2.Id, _now, default)).Error);
    }

    [Fact]
    public async Task RejectingAndCancellingReleaseTheItem()
    {
        var item = NewItem(_ana); var offer = (await Offer(_ana, item, _bia)).Offer!;
        Assert.Equal("not_found", (await ItemTransfers.RejectAsync(_db, _ana.Id, offer.Id, _now, default)).Error); Assert.Equal("not_found", (await ItemTransfers.CancelAsync(_db, _bia.Id, offer.Id, _now, default)).Error); // each side has its own answer
        Assert.True((await ItemTransfers.RejectAsync(_db, _bia.Id, offer.Id, _now, default)).Ok); var released = await Fresh(item); Assert.Equal(("active", _ana.Id), (released.Status, released.OwnerUserId));
        Assert.Equal("offer_closed", (await ItemTransfers.AcceptAsync(_db, _bia.Id, offer.Id, _now, default)).Error); Assert.True((await Offer(_ana, item, _cris)).Ok); // it can be offered again
        var again = (await _db.ItemOffers.SingleAsync(o => o.ToUserId == _cris.Id)); Assert.True((await ItemTransfers.CancelAsync(_db, _ana.Id, again.Id, _now, default)).Ok); Assert.Equal("active", (await Fresh(item)).Status);
        Assert.Equal("offer_closed", (await ItemTransfers.RejectAsync(_db, _cris.Id, again.Id, _now, default)).Error);
    }

    [Fact]
    public async Task OffersThatNobodyAnswersExpireAndReleaseTheirItems()
    {
        var item = NewItem(_ana); var offer = (await Offer(_ana, item, _bia)).Offer!; var later = _now + ItemTransfers.OfferLife + TimeSpan.FromMinutes(1);
        Assert.Equal("offer_expired", (await ItemTransfers.AcceptAsync(_db, _bia.Id, offer.Id, later, default)).Error); var item2 = NewItem(_ana); var offer2 = (await Offer(_ana, item2, _bia)).Offer!;
        Assert.Equal("active", (await Fresh(item)).Status); Assert.Equal(1, await ItemTransfers.ExpireAsync(_db, later, default)); Assert.Equal("active", (await Fresh(item2)).Status); Assert.All(_db.ItemOffers, o => Assert.Equal("expired", o.Status));
        Assert.Equal(_ana.Id, (await Fresh(item)).OwnerUserId);
    }

    [Fact]
    public async Task TheOfferListsShowOnlyWhatCanBeAnswered()
    {
        var a = NewItem(_ana); var b = NewItem(_ana); var oa = (await Offer(_ana, a, _bia)).Offer!; await Offer(_ana, b, _bia); await ItemTransfers.RejectAsync(_db, _bia.Id, oa.Id, _now, default);
        var incoming = await ItemTransfers.ListAsync(_db, _bia.Id, true, true, _now, default); Assert.Single(incoming); Assert.Equal(("ana", "bia"), (incoming[0].FromName, incoming[0].ToName));
        Assert.Equal(2, (await ItemTransfers.ListAsync(_db, _ana.Id, false, false, _now, default)).Count); Assert.Empty(await ItemTransfers.ListAsync(_db, _cris.Id, true, false, _now, default));
        Assert.Empty(await ItemTransfers.ListAsync(_db, _bia.Id, true, true, _now + ItemTransfers.OfferLife + TimeSpan.FromHours(1), default)); // expired ones are not shown as pending
    }

    // ---- exploits and races -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ARoundOfHandingAnItemBackAndForthNeverDuplicatesIt()
    {
        var item = NewItem(_ana); User holder = _ana, other = _bia;
        for (var round = 0; round < 6; round++)
        {
            var offer = (await Offer(holder, item, other, kind: "transfer")).Offer!; Assert.True((await ItemTransfers.AcceptAsync(_db, other.Id, offer.Id, _now, default)).Ok);
            Assert.Equal(1, await Held(item.AssetId)); (holder, other) = (other, holder);
        }
        Assert.Equal(6, await _db.ItemOwnerRecords.CountAsync(r => r.ItemId == item.Id && r.Reason == "transfer"));
    }

    [Fact]
    public async Task TheReceiverCannotReuseALockedItemOrActOnSomeoneElsesOffer()
    {
        var item = NewItem(_ana); var offer = (await Offer(_ana, item, _bia)).Offer!;
        Assert.Equal("not_found", (await ItemTransfers.CopyAsync(_db, _bia.Id, item.Id, _now, default)).Error); // the receiver does not hold it yet
        Assert.Equal("item_busy", (await ItemTransfers.CopyAsync(_db, _ana.Id, item.Id, _now, default)).Error); // and the giver cannot copy it while it is on offer
        Assert.Equal(1, await Held(item.AssetId)); Assert.Equal("not_found", (await ItemTransfers.CancelAsync(_db, _cris.Id, offer.Id, _now, default)).Error);
    }

    [Fact]
    public async Task ConcurrentAnswersApplyOnlyOnce()
    {
        // each request has its own context, as on the server; the in-memory store has no row locks, so this checks the version checks and the idempotent answers
        var item = NewItem(_ana); var offer = (await Offer(_ana, item, _bia, kind: "transfer")).Offer!; var results = new System.Collections.Concurrent.ConcurrentBag<OfferResult>();
        await Parallel.ForEachAsync(Enumerable.Range(0, 12), new ParallelOptions { MaxDegreeOfParallelism = 12 }, async (n, ct) =>
        {
            await using var own = Open();
            results.Add(n % 3 == 0 ? await ItemTransfers.CancelAsync(own, _ana.Id, offer.Id, _now, ct) : await ItemTransfers.AcceptAsync(own, _bia.Id, offer.Id, _now, ct));
        });
        var final = await Fresh(item); var state = (await _db.ItemOffers.AsNoTracking().SingleAsync()).Status;
        Assert.Equal(1, await Held(item.AssetId)); Assert.Equal("active", final.Status);
        Assert.True((state == "accepted" && final.OwnerUserId == _bia.Id) || (state == "cancelled" && final.OwnerUserId == _ana.Id), $"offer {state}, owner {final.OwnerUserId}"); // one outcome, and the item agrees with it
    }
}
