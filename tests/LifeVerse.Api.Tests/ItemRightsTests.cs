using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Shared.Items;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class ItemRightsModelTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(15, 15)]
    [InlineData(8, 0)] // resell without transfer does not exist
    [InlineData(12, 12)]
    [InlineData(3, 3)]
    [InlineData(255, 15)] // unknown bits are dropped
    [InlineData(-1, 15)]
    public void ASetOfRightsIsAlwaysCleaned(int input, int expected) => Assert.Equal(expected, ItemRights.Normalize(input));

    [Fact]
    public void RightsAreCheckedBitByBitAndResellNeedsTransfer()
    {
        Assert.True(ItemRights.Has(ItemRights.Standard, Right.Copy)); Assert.False(ItemRights.Has(ItemRights.Standard, Right.Resell)); Assert.False(ItemRights.Has(ItemRights.Copy | ItemRights.Resell, Right.Resell));
        Assert.False(ItemRights.Has(ItemRights.All, Right.None)); Assert.True(ItemRights.IsCopyable(ItemRights.All)); Assert.False(ItemRights.IsCopyable(ItemRights.Modify | ItemRights.Transfer));
    }

    [Fact]
    public void ASetCannotGiveMoreThanItHolds()
    {
        Assert.Equal(ItemRights.Copy, ItemRights.Clamp(ItemRights.All, ItemRights.Copy)); Assert.Equal(0, ItemRights.Clamp(ItemRights.Modify, ItemRights.Copy));
        Assert.True(ItemRights.IsWithin(ItemRights.Copy, ItemRights.Standard)); Assert.False(ItemRights.IsWithin(ItemRights.Resell | ItemRights.Transfer, ItemRights.Standard)); Assert.True(ItemRights.IsWithin(0, 0));
    }

    [Fact]
    public void ThePermissionsOfAThingMadeOfPartsAreTheStrictestOfTheParts()
    {
        Assert.Equal(ItemRights.All, ItemRights.Intersect([])); Assert.Equal(ItemRights.Modify | ItemRights.Transfer, ItemRights.Intersect([ItemRights.All, ItemRights.Modify | ItemRights.Transfer, ItemRights.Standard]));
        Assert.False(ItemRights.IsCopyable(ItemRights.Intersect([ItemRights.All, ItemRights.All, ItemRights.Modify | ItemRights.Transfer]))); // one part that cannot be copied
        var whole = ItemPermissions.Compose([new ItemPermissions(15, 3, 15), new ItemPermissions(7, 7, 6)]);
        Assert.Equal(new ItemPermissions(7, 3, 6), whole); Assert.Equal(ItemPermissions.Creator, ItemPermissions.Compose([]));
    }

    [Fact]
    public void GroupAndNextOwnerFitInsideTheOwner()
    {
        var cut = new ItemPermissions(ItemRights.Copy | ItemRights.Modify, ItemRights.All, ItemRights.Standard).Normalized();
        Assert.Equal(new ItemPermissions(3, 3, 3), cut); Assert.True(cut.IsValid); Assert.False(new ItemPermissions(3, 15, 3).IsValid); Assert.True(ItemPermissions.Creator.IsValid);
    }

    [Fact]
    public void TheNextOwnerHoldsTheNextOwnerSetAndTheGroupSharingIsGone()
    {
        var arrived = new ItemPermissions(ItemRights.All, ItemRights.Copy, ItemRights.Modify | ItemRights.Transfer).ForNextOwner();
        Assert.Equal(new ItemPermissions(6, 0, 6), arrived); Assert.False(ItemRights.IsCopyable(arrived.Owner)); // made no-copy for the next owner
    }

    [Fact]
    public void WhoHoldsWhatDependsOnBeingTheOwnerOrAGroupMember()
    {
        var p = new ItemPermissions(15, 3, 7); Assert.Equal(15, p.For(true, true)); Assert.Equal(3, p.For(false, true)); Assert.Equal(0, p.For(false, false));
    }

    [Fact]
    public void RightsAreWrittenAndReadAsJsonKeepingOtherKeys()
    {
        Assert.Equal(7, ItemRights.FromJson("{\"copy\":true,\"modify\":true,\"transfer\":true,\"use\":true}")); Assert.Equal(0, ItemRights.FromJson("{\"copy\":\"yes\"}")); Assert.Equal(7, ItemRights.FromJson("not json", 7)); Assert.Equal(15, ItemRights.FromJson(null, 15)); Assert.Equal(0, ItemRights.FromJson("[1]"));
        Assert.Equal(ItemRights.Standard, ItemRights.FromJson(ItemRights.ToJson(ItemRights.Standard)));
        var merged = ItemRights.MergeIntoJson("{\"use\":true,\"move\":false,\"copy\":true}", ItemRights.Modify); Assert.Contains("\"use\":true", merged); Assert.Contains("\"move\":false", merged); Assert.Contains("\"copy\":false", merged); Assert.Contains("\"modify\":true", merged);
        Assert.StartsWith("{", ItemRights.MergeIntoJson("garbage", 1));
    }

    [Fact]
    public void RightsAreDescribedForPeople()
    {
        Assert.Equal("Copiar · Modificar · Transferir · Revender", ItemRights.Describe(15)); Assert.Equal("Nenhum direito", ItemRights.Describe(0)); Assert.Equal("CMT-", ItemRights.Letters(7)); Assert.Equal("----", ItemRights.Letters(0)); Assert.Equal("C---", ItemRights.Letters(9));
    }

    [Theory]
    [InlineData("script", ContentKind.Script)]
    [InlineData("LUA", ContentKind.Script)]
    [InlineData("mesh", ContentKind.Model)]
    [InlineData("glb", ContentKind.Model)]
    [InlineData("texture", ContentKind.Texture)]
    [InlineData("animation", ContentKind.Animation)]
    [InlineData("whatever", ContentKind.Other)]
    [InlineData(null, ContentKind.Other)]
    public void TheKindOfContentFollowsTheCategory(string? category, ContentKind expected) => Assert.Equal(expected, ContentRules.FromCategory(category));

    [Fact]
    public void ScriptsNeedModifyToBeReadAndModelsTexturesAndAnimationsNeedCopyToBeTakenOut()
    {
        Assert.False(ContentRules.CanReadSource(ContentKind.Script, ItemRights.Copy | ItemRights.Transfer)); Assert.True(ContentRules.CanReadSource(ContentKind.Script, ItemRights.Modify)); Assert.True(ContentRules.CanReadSource(ContentKind.Texture, 0));
        foreach (var kind in new[] { ContentKind.Model, ContentKind.Texture, ContentKind.Animation }) { Assert.False(ContentRules.CanExport(kind, ItemRights.Modify | ItemRights.Transfer), kind.ToString()); Assert.True(ContentRules.CanExport(kind, ItemRights.Copy), kind.ToString()); }
        Assert.False(ContentRules.CanExport(ContentKind.Script, ItemRights.Copy)); Assert.True(ContentRules.CanExport(ContentKind.Script, ItemRights.Modify)); Assert.True(ContentRules.CanExport(ContentKind.Other, 0));
        foreach (var kind in new[] { ContentKind.Script, ContentKind.Model, ContentKind.Texture, ContentKind.Animation }) { Assert.False(ContentRules.CanEmbed(kind, ItemRights.Copy | ItemRights.Modify), kind.ToString()); Assert.True(ContentRules.CanEmbed(kind, ItemRights.Transfer), kind.ToString()); }
    }
}

/// <summary>The service that decides who holds which rights over an item, how they change, and the history and audit trail.</summary>
public sealed class ItemRightsServiceTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private readonly User _creator, _holder, _other;

    public ItemRightsServiceTests() { _creator = NewUser("criador"); _holder = NewUser("dono"); _other = NewUser("outro"); }
    public void Dispose() => _db.Dispose();

    private User NewUser(string username)
    {
        var u = new User { Id = Guid.NewGuid(), Username = username, NormalizedUsername = username.ToUpperInvariant(), Email = $"{username}@t.local", NormalizedEmail = $"{username}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now };
        _db.Users.Add(u); _db.SaveChanges(); return u;
    }

    private InventoryItem NewItem(User owner, int ownerRights = 15, int group = 0, int next = 15, string category = "mesh", User? madeBy = null, Guid? groupId = null)
    {
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = (madeBy ?? owner).Id, CreatorUserId = (madeBy ?? owner).Id, Name = "Coisa", Category = category, CreatedAt = _now, UpdatedAt = _now };
        var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = owner.Id, AssetId = asset.Id, DisplayName = "Coisa", OwnerRights = ownerRights, GroupRights = group, NextOwnerRights = next, GroupId = groupId, CreatedAt = _now, UpdatedAt = _now };
        _db.Assets.Add(asset); _db.InventoryItems.Add(item); _db.SaveChanges(); return item;
    }

    private SocialGroup NewGroup(params User[] members)
    {
        var g = new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = members[0].Id, Name = "Grupo " + Guid.NewGuid().ToString("N")[..4], NameKey = Guid.NewGuid().ToString("N")[..8], Privacy = "public", CreatedAt = _now, UpdatedAt = _now };
        _db.SocialGroups.Add(g); foreach (var m in members) _db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = g.Id, UserId = m.Id, Role = "member", JoinedAt = _now }); _db.SaveChanges(); return g;
    }

    private Task<int> Held(InventoryItem item, User who) => ItemRightsService.HeldAsync(_db, item, who.Id, default);

    // ---- who holds what -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheOwnerHoldsTheOwnerSetAndNobodyElseHoldsAnything()
    {
        var item = NewItem(_holder, ownerRights: 7);
        Assert.Equal(7, await Held(item, _holder)); Assert.Equal(0, await Held(item, _other)); Assert.Equal(0, await Held(item, _creator));
        Assert.True(await ItemRightsService.CanAsync(_db, item, _holder.Id, Right.Copy, default)); Assert.False(await ItemRightsService.CanAsync(_db, item, _holder.Id, Right.Resell, default)); Assert.False(await ItemRightsService.CanAsync(_db, item, _other.Id, Right.Copy, default));
    }

    [Fact]
    public async Task AMemberOfTheSharedGroupHoldsTheGroupSetButOnlyInsideTheOwners()
    {
        var group = NewGroup(_holder, _other); var item = NewItem(_holder, ownerRights: 3, group: 15, groupId: group.Id); // a group set above the owner's is cut to it
        Assert.Equal(3, await Held(item, _other)); item.GroupRights = ItemRights.Modify; Assert.Equal(2, await Held(item, _other));
        var stranger = NewUser("estranho"); Assert.Equal(0, await Held(item, stranger)); item.GroupId = null; Assert.Equal(0, await Held(item, _other)); // not shared any more
    }

    [Fact]
    public async Task RemovedAndOfferedItemsCanBeUsedByNobody()
    {
        var item = NewItem(_holder); item.Status = "offered"; Assert.Equal(0, await Held(item, _holder)); item.Status = "active"; item.DeletedAt = _now; Assert.Equal(0, await Held(item, _holder));
    }

    [Fact]
    public async Task RequireAllowsWhatIsHeldAndRefusesTheRestLeavingAMarkInTheAudit()
    {
        var item = NewItem(_holder, ownerRights: ItemRights.Modify);
        Assert.True((await ItemRightsService.RequireAsync(_db, _holder.Id, item.Id, Right.Modify, "edit", _now, default)).Ok);
        var denied = await ItemRightsService.RequireAsync(_db, _holder.Id, item.Id, Right.Copy, "copy", _now, default); Assert.False(denied.Ok); Assert.Equal("not_allowed", denied.Error); Assert.Null(denied.Item);
        var mark = await _db.ItemAudit.SingleAsync(); Assert.Equal(("denied", "denied", _holder.Id, item.Id), (mark.Action, mark.Outcome, mark.ActorUserId, mark.ItemId)); Assert.Contains("copy", mark.Detail);
        var stranger = await ItemRightsService.RequireAsync(_db, _other.Id, item.Id, Right.Modify, "edit", _now, default); Assert.Equal("not_found", stranger.Error); // what is not theirs does not exist for them
        Assert.Equal("not_found", (await ItemRightsService.RequireAsync(_db, _holder.Id, Guid.NewGuid(), Right.Modify, "edit", _now, default)).Error); Assert.Single(_db.ItemAudit);
        item = await _db.InventoryItems.SingleAsync(); item.Status = "offered"; await _db.SaveChangesAsync();
        Assert.Equal("item_busy", (await ItemRightsService.RequireAsync(_db, _holder.Id, item.Id, Right.Modify, "edit", _now, default)).Error);
    }

    // ---- reading ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheViewShowsTheOwnerEverythingAndAGroupMemberOnlyTheirOwnSet()
    {
        var group = NewGroup(_holder, _other); var item = NewItem(_holder, ownerRights: 15, group: 3, next: 7, category: "script", groupId: group.Id);
        var mine = (await ItemRightsService.ViewAsync(_db, _holder.Id, item.Id, default))!; Assert.True(mine.IsOwner); Assert.Equal(new ItemPermissions(15, 3, 7), mine.Permissions); Assert.Equal("CMTR", mine.Letters); Assert.Equal("script", mine.Content); Assert.True(mine.Copyable);
        var theirs = (await ItemRightsService.ViewAsync(_db, _other.Id, item.Id, default))!; Assert.False(theirs.IsOwner); Assert.Equal(3, theirs.YourRights); Assert.Equal(new ItemPermissions(3, 0, 0), theirs.Permissions); // what the owner passes on is not theirs to see
        Assert.Null(await ItemRightsService.ViewAsync(_db, NewUser("estranho").Id, item.Id, default)); Assert.Null(await ItemRightsService.ViewAsync(_db, _holder.Id, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task AThingMadeOfSeveralItemsHasTheStrictestRights()
    {
        var a = NewItem(_holder, 15, next: 15); var b = NewItem(_holder, 7, next: 6); var c = NewItem(_holder, 15, next: 15);
        var whole = await ItemRightsService.ComposeAsync(_db, [a.Id, b.Id, c.Id, b.Id], default); Assert.Equal(new ItemPermissions(7, 0, 6), whole);
    }

    // ---- changing -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheHolderCanGiveUpRightsAndWhatTheyPassOnFollows()
    {
        var item = NewItem(_holder, madeBy: _creator); item.OwnerRights = 7; item.NextOwnerRights = 7; await _db.SaveChangesAsync();
        var result = await ItemRightsService.SetPermissionsAsync(_db, _holder.Id, item.Id, new ItemPermissions(ItemRights.Copy | ItemRights.Modify, 0, ItemRights.Copy), null, _now, default); Assert.True(result.Ok);
        item = await _db.InventoryItems.SingleAsync(); Assert.Equal((3, 0, 1, 1L), (item.OwnerRights, item.GroupRights, item.NextOwnerRights, item.Revision));
        var audit = await _db.ItemAudit.SingleAsync(); Assert.Equal(("rights_change", "ok"), (audit.Action, audit.Outcome)); Assert.Contains("CMT-/----/CMT- -> CM--/----/C---", audit.Detail);
    }

    [Fact]
    public async Task OnlyTheHolderChangesTheRightsAndNobodyRaisesWhatTheyDoNotHold()
    {
        var item = NewItem(_holder, ownerRights: 3, next: 3, madeBy: _creator);
        Assert.Equal("not_found", (await ItemRightsService.SetPermissionsAsync(_db, _other.Id, item.Id, ItemPermissions.Creator, null, _now, default)).Error);
        Assert.Equal("not_found", (await ItemRightsService.SetPermissionsAsync(_db, _creator.Id, item.Id, ItemPermissions.Creator, null, _now, default)).Error); // the maker no longer holds it
        Assert.Equal("cannot_raise_rights", (await ItemRightsService.SetPermissionsAsync(_db, _holder.Id, item.Id, new ItemPermissions(15, 0, 15), null, _now, default)).Error);
        Assert.Equal("cannot_raise_rights", (await ItemRightsService.SetPermissionsAsync(_db, _holder.Id, item.Id, new ItemPermissions(3 | 4, 0, 0), null, _now, default)).Error);
        var stored = await _db.InventoryItems.AsNoTracking().SingleAsync(); Assert.Equal((3, 3, 0L), (stored.OwnerRights, stored.NextOwnerRights, stored.Revision));
        Assert.Equal(2, await _db.ItemAudit.CountAsync(a => a.Outcome == "denied")); // the two attempts by the holder are in the trail
    }

    [Fact]
    public async Task TheMakerWhoStillHoldsTheItemMayRestoreRights()
    {
        var item = NewItem(_creator, ownerRights: 1, next: 1);
        Assert.True((await ItemRightsService.SetPermissionsAsync(_db, _creator.Id, item.Id, new ItemPermissions(15, 0, 7), null, _now, default)).Ok);
        var stored = await _db.InventoryItems.AsNoTracking().SingleAsync(); Assert.Equal((15, 7), (stored.OwnerRights, stored.NextOwnerRights));
    }

    [Fact]
    public async Task TheGroupAndNextOwnerSetsMustFitInsideTheHoldersNewSet()
    {
        var item = NewItem(_holder); var group = NewGroup(_holder); item.GroupId = group.Id; await _db.SaveChangesAsync();
        Assert.Equal("exceeds_owner", (await ItemRightsService.SetPermissionsAsync(_db, _holder.Id, item.Id, new ItemPermissions(3, 15, 3), null, _now, default)).Error);
        Assert.Equal("exceeds_owner", (await ItemRightsService.SetPermissionsAsync(_db, _holder.Id, item.Id, new ItemPermissions(3, 0, 7), null, _now, default)).Error);
        Assert.Equal("exceeds_owner", (await ItemRightsService.SetPermissionsAsync(_db, _holder.Id, item.Id, new ItemPermissions(3, 0, 1 << 10), null, _now, default)).Error); // unknown bits are not accepted
        Assert.True((await ItemRightsService.SetPermissionsAsync(_db, _holder.Id, item.Id, new ItemPermissions(3, 2, 1), null, _now, default)).Ok);
    }

    [Fact]
    public async Task AChangeMadeOnAnOutOfDateViewIsRefused()
    {
        var item = NewItem(_holder);
        Assert.True((await ItemRightsService.SetPermissionsAsync(_db, _holder.Id, item.Id, new ItemPermissions(7, 0, 7), 0, _now, default)).Ok);
        Assert.Equal("stale", (await ItemRightsService.SetPermissionsAsync(_db, _holder.Id, item.Id, new ItemPermissions(3, 0, 3), 0, _now, default)).Error); // asked on revision 0, it is 1 now
        Assert.Equal(7, (await _db.InventoryItems.AsNoTracking().SingleAsync()).OwnerRights);
        Assert.True((await ItemRightsService.SetPermissionsAsync(_db, _holder.Id, item.Id, new ItemPermissions(3, 0, 3), 1, _now, default)).Ok);
    }

    [Fact]
    public async Task AnItemWaitingForAnAnswerCannotBeChanged()
    {
        var item = NewItem(_holder); item.Status = "offered"; await _db.SaveChangesAsync();
        Assert.Equal("item_busy", (await ItemRightsService.SetPermissionsAsync(_db, _holder.Id, item.Id, new ItemPermissions(7, 0, 7), null, _now, default)).Error);
        Assert.Equal("item_busy", (await ItemRightsService.ShareWithGroupAsync(_db, _holder.Id, item.Id, null, _now, default)).Error);
    }

    [Fact]
    public async Task SharingWithAGroupNeedsMembershipAndStoppingClearsTheGroupSet()
    {
        var mine = NewGroup(_holder); var notMine = NewGroup(_other); var item = NewItem(_holder);
        Assert.Equal("not_in_group", (await ItemRightsService.ShareWithGroupAsync(_db, _holder.Id, item.Id, notMine.Id, _now, default)).Error); Assert.Equal("not_in_group", (await ItemRightsService.ShareWithGroupAsync(_db, _holder.Id, item.Id, Guid.NewGuid(), _now, default)).Error);
        Assert.True((await ItemRightsService.ShareWithGroupAsync(_db, _holder.Id, item.Id, mine.Id, _now, default)).Ok);
        Assert.True((await ItemRightsService.SetPermissionsAsync(_db, _holder.Id, item.Id, new ItemPermissions(15, 3, 15), null, _now, default)).Ok); Assert.Equal(3, (await _db.InventoryItems.AsNoTracking().SingleAsync()).GroupRights);
        Assert.True((await ItemRightsService.ShareWithGroupAsync(_db, _holder.Id, item.Id, null, _now, default)).Ok); var stored = await _db.InventoryItems.AsNoTracking().SingleAsync(); Assert.Equal((null, 0), (stored.GroupId, stored.GroupRights));
        Assert.True((await ItemRightsService.SetPermissionsAsync(_db, _holder.Id, item.Id, new ItemPermissions(15, 3, 15), null, _now, default)).Ok); Assert.Equal(0, (await _db.InventoryItems.AsNoTracking().SingleAsync()).GroupRights); // no group, so no group set
        Assert.Equal("not_found", (await ItemRightsService.ShareWithGroupAsync(_db, _other.Id, item.Id, notMine.Id, _now, default)).Error);
    }

    // ---- history ------------------------------------------------------------------------------------------------------------------

    private async Task<InventoryItem> ItemWithThreeHolders()
    {
        var middle = NewUser("meio"); var item = NewItem(_holder, madeBy: _creator);
        ItemRightsService.RecordOwner(_db, item, _creator.Id, null, "created", _now.AddDays(-3)); ItemRightsService.RecordOwner(_db, item, middle.Id, _creator.Id, "purchase", _now.AddDays(-2)); ItemRightsService.RecordOwner(_db, item, _holder.Id, middle.Id, "transfer", _now.AddDays(-1));
        await _db.SaveChangesAsync(); return item;
    }

    [Fact]
    public async Task TheHolderSeesTheWholeHistoryInOrder()
    {
        var item = await ItemWithThreeHolders(); var lines = (await ItemRightsService.HistoryAsync(_db, _holder.Id, item.Id, default))!;
        Assert.Equal(["created", "purchase", "transfer"], lines.Select(l => l.Reason)); Assert.Equal(["criador", "meio", "dono"], lines.Select(l => l.Holder));
    }

    [Fact]
    public async Task OthersSeeTheHistoryOnlyWhenAllowedAndTheNamesInBetweenStayHidden()
    {
        var item = await ItemWithThreeHolders(); var group = NewGroup(_holder, _other); item = await _db.InventoryItems.SingleAsync(); item.GroupId = group.Id; item.GroupRights = 1; await _db.SaveChangesAsync();
        Assert.Null(await ItemRightsService.HistoryAsync(_db, _other.Id, item.Id, default)); // not allowed by default
        Assert.True((await ItemRightsService.SetHistoryVisibilityAsync(_db, _holder.Id, item.Id, true, _now, default)).Ok);
        var seen = (await ItemRightsService.HistoryAsync(_db, _other.Id, item.Id, default))!; Assert.Equal(["criador", "proprietário anterior", "dono"], seen.Select(l => l.Holder)); Assert.Equal([null, "criador", "proprietário anterior"], seen.Select(l => l.From));
        Assert.Null(await ItemRightsService.HistoryAsync(_db, NewUser("estranho").Id, item.Id, default)); // someone with no link to the item never sees it
        Assert.Equal("not_found", (await ItemRightsService.SetHistoryVisibilityAsync(_db, _other.Id, item.Id, false, _now, default)).Error);
    }
}
