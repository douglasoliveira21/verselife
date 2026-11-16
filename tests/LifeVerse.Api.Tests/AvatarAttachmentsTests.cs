using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Users;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Items;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The catalog of attachment points (stable ids, Second Life names, profiles) and the server rules for wearing objects: ownership, limits, add/replace, placement, rights and what happens when the item is gone.</summary>
public sealed class AvatarAttachmentsTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _me, _other;

    public AvatarAttachmentsTests() { (_me, _other) = (User("eu"), User("outro")); AvatarAttachments.Limits = AttachmentPoints.DefaultLimits; }
    public void Dispose() { AvatarAttachments.Limits = AttachmentPoints.DefaultLimits; _db.Dispose(); }

    private Guid User(string name)
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now, PresenceStatus = "online" });
        _db.Avatars.Add(new Avatar { Id = Guid.NewGuid(), UserId = id, DisplayName = name, BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); return id;
    }

    private Guid Item(Guid? owner = null, int rights = ItemRights.All, string status = "active", string assetStatus = "ready", string name = "Chapéu")
    {
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner ?? _me, CreatorUserId = owner ?? _me, Name = name, Category = "models", Status = assetStatus, CreatedAt = _now, UpdatedAt = _now };
        var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = owner ?? _me, AssetId = asset.Id, DisplayName = name, OwnerRights = rights, Status = status, CreatedAt = _now, UpdatedAt = _now };
        _db.Assets.Add(asset); _db.InventoryItems.Add(item); _db.SaveChanges(); return item.Id;
    }

    private async Task<Guid> Attach(Guid item, string point = "head_top", string? mode = null, float[]? pos = null, float[]? rot = null, float[]? scale = null, Guid? user = null)
    {
        var r = await AvatarAttachments.AttachAsync(_db, user ?? _me, new(item, point, mode, pos, rot, scale), _now, default); Assert.True(r.Ok, r.Error);
        return JsonSerializer.SerializeToElement(r.Data).GetProperty("id").GetGuid();
    }

    private Task<AttachmentResult> TryAttach(Guid item, string point = "head_top", string? mode = null, float[]? pos = null, Guid? user = null) => AvatarAttachments.AttachAsync(_db, user ?? _me, new(item, point, mode, pos, null, null), _now, default);

    // ---- catalog --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheCatalogHasStableUniqueIdsAndKeysAndEveryRegion()
    {
        var all = AttachmentPoints.Everything.ToList();
        Assert.Equal(all.Count, all.Select(p => p.Key).Distinct().Count()); Assert.Equal(all.Count, all.Select(p => p.Id).Distinct().Count());
        foreach (var region in AttachmentPoints.Regions) Assert.Contains(all, p => p.Region == region);
        Assert.All(all.Where(p => !p.IsHud), p => Assert.Contains(p.Bone, BoneMapper.Standard)); // every body point follows a bone the game really maps
        Assert.All(all.Where(p => p.IsHud), p => Assert.Equal(string.Empty, p.Bone));            // a HUD point is anchored to the screen, not to the skeleton
        Assert.All(all, p => Assert.Contains(p.Profile, AttachmentPoints.Profiles));
        Assert.Equal(2, AttachmentPoints.Find("skull")!.Id); Assert.Equal(6, AttachmentPoints.Find("hand_r")!.Id); Assert.Equal(9, AttachmentPoints.Find("back")!.Id); // ids that never change
    }

    [Theory]
    [InlineData("crown", false)] [InlineData("skull", true)] [InlineData("eye_l", true)] [InlineData("tail", false)] [InlineData("wing_l", false)] [InlineData("finger_index_r", false)] [InlineData("hud_top_left", true)]
    public void AnOptionalPointExistsOnlyOnASkeletonThatHasIt(string key, bool humanoid) => Assert.Equal(humanoid, AttachmentPoints.Resolve(key, null) is not null);

    [Fact]
    public void ProfilesUnlockTheOptionalPointsAndAMissingPointFallsBackToTheNearestOne()
    {
        Assert.NotNull(AttachmentPoints.Resolve("tail", [AttachmentPoints.Tail])); Assert.NotNull(AttachmentPoints.Resolve("finger_ring_l", [AttachmentPoints.Fingers]));
        Assert.Contains(AttachmentPoints.For([AttachmentPoints.Wings]), p => p.Key == "wing_r"); Assert.DoesNotContain(AttachmentPoints.For(null), p => p.Key == "wing_r");
        Assert.Equal("pelvis_back", AttachmentPoints.Fallback(AttachmentPoints.Find("tail")!, null)!.Key); Assert.Equal("back", AttachmentPoints.Fallback(AttachmentPoints.Find("wing_l")!, null)!.Key);
        Assert.Equal("hand_l", AttachmentPoints.Fallback(AttachmentPoints.Find("finger_thumb_l")!, null)!.Key); Assert.Null(AttachmentPoints.Fallback(AttachmentPoints.Find("skull")!, null));
    }

    [Theory]
    [InlineData("Skull", "skull")] [InlineData("chest", "chest")] [InlineData("Spine", "back")] [InlineData("Left Hand", "hand_l")] [InlineData("RIGHT FOOT", "foot_r")] [InlineData("HUD Top Left", "hud_top_left")]
    [InlineData("HUD Center 1", "hud_center")] [InlineData("Left_Upper_Arm", "upper_arm_l")] [InlineData("Stomach", "abdomen")] [InlineData("Mouth", "mouth")] [InlineData("5", "hand_l")]
    public void SecondLifePointsMapToOurs(string name, string key) => Assert.Equal(key, AttachmentPoints.FromSecondLife(name)?.Key);

    [Fact]
    public void ASecondLifeNameWeDoNotKnowMapsToNothing() { Assert.Null(AttachmentPoints.FromSecondLife("Left Wing")); Assert.Null(AttachmentPoints.FromSecondLife("")); Assert.Null(AttachmentPoints.FromSecondLife("999")); }

    [Fact]
    public void ATransformMustBeFiniteAndWithinTheLimits()
    {
        var l = AttachmentPoints.DefaultLimits;
        Assert.Null(AttachmentPoints.CheckTransform(l, [0, .1f, 0], [10, 20, 30], [1, 1, 1]));
        Assert.Equal("bad_position", AttachmentPoints.CheckTransform(l, [0, 99, 0], null, null)); Assert.Equal("bad_position", AttachmentPoints.CheckTransform(l, [float.NaN, 0, 0], null, null)); Assert.Equal("bad_position", AttachmentPoints.CheckTransform(l, [0, 0], null, null));
        Assert.Equal("bad_rotation", AttachmentPoints.CheckTransform(l, null, [float.PositiveInfinity, 0, 0], null));
        Assert.Equal("bad_scale", AttachmentPoints.CheckTransform(l, null, null, [0, 1, 1])); Assert.Equal("bad_scale", AttachmentPoints.CheckTransform(l, null, null, [1, 11, 1])); Assert.Equal("bad_scale", AttachmentPoints.CheckTransform(l, null, null, [-1, 1, 1]));
    }

    // ---- attaching ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnObjectOfTheInventoryIsWornAtAPointAndStaysWornWithItsPlacement()
    {
        var item = Item(); var id = await Attach(item, "hand_r", pos: [.01f, .02f, .03f]);
        var list = JsonSerializer.SerializeToElement(await AvatarAttachments.ListAsync(_db, _me, _now, default)).GetProperty("attachments");
        var a = Assert.Single(list.EnumerateArray()); Assert.Equal(id, a.GetProperty("id").GetGuid()); Assert.Equal("hand_r", a.GetProperty("point").GetString()); Assert.Equal("Mão direita", a.GetProperty("pointName").GetString());
        Assert.True(a.GetProperty("equipped").GetBoolean()); Assert.Equal(.02f, a.GetProperty("position")[1].GetSingle(), 4); Assert.Equal(1f, a.GetProperty("scale")[0].GetSingle()); Assert.Equal("Chapéu", a.GetProperty("name").GetString());
        Assert.Equal(AttachmentPoints.SkeletonProfileVersion, a.GetProperty("profileVersion").GetInt32());
    }

    [Fact]
    public async Task OnlyYourOwnReadyItemsCanBeWorn()
    {
        Assert.Equal("item_not_found", (await TryAttach(Item(_other))).Error); Assert.Equal("item_not_found", (await TryAttach(Guid.NewGuid())).Error);
        Assert.Equal("item_unavailable", (await TryAttach(Item(status: "offered"))).Error); Assert.Equal("asset_unavailable", (await TryAttach(Item(assetStatus: "processing"))).Error);
        Assert.Equal("unknown_point", (await TryAttach(Item(), "orelha")).Error); Assert.Equal("point_unavailable", (await TryAttach(Item(), "tail")).Error); Assert.Equal("bad_mode", (await TryAttach(Item(), mode: "xx")).Error);
        Assert.Empty(await _db.AvatarAttachments.ToListAsync());
    }

    [Fact]
    public async Task AWornItemIsNotWornTwice() { var item = Item(); await Attach(item); Assert.Equal("already_attached", (await TryAttach(item, "skull")).Error); Assert.Single(await _db.AvatarAttachments.ToListAsync()); }

    [Fact]
    public async Task AddKeepsWhatIsThereAndReplaceTakesItOff()
    {
        var hat = Item(); var hair = Item(name: "Cabelo"); var cap = Item(name: "Boné");
        await Attach(hat, "skull"); await Attach(hair, "skull"); // two objects at the same point
        Assert.Equal(2, await _db.AvatarAttachments.CountAsync(a => a.Equipped));
        await Attach(cap, "skull", "replace");
        var worn = await _db.AvatarAttachments.Where(a => a.Equipped).ToListAsync(); Assert.Equal(cap, Assert.Single(worn).InventoryItemId);
        Assert.Equal(2, await _db.AvatarAttachments.CountAsync(a => !a.Equipped)); // the replaced ones are kept, off the body
    }

    [Fact]
    public async Task ObjectsAtTheSamePointKeepTheirOwnOrder()
    {
        var a = await Attach(Item(), "wrist_l"); var b = await Attach(Item(), "wrist_l");
        Assert.True((await _db.AvatarAttachments.FindAsync(b))!.SortOrder > (await _db.AvatarAttachments.FindAsync(a))!.SortOrder);
    }

    [Fact]
    public async Task TheServerLimitsHowManyObjectsAreWorn()
    {
        AvatarAttachments.Limits = new(MaxTotal: 4, MaxPerPoint: 2, MaxHud: 1);
        await Attach(Item(), "skull"); await Attach(Item(), "skull"); Assert.Equal("too_many_at_point", (await TryAttach(Item(), "skull")).Error);
        await Attach(Item(), "hud_center"); Assert.Equal("too_many_huds", (await TryAttach(Item(), "hud_top")).Error);
        await Attach(Item(), "chest"); Assert.Equal("too_many_attachments", (await TryAttach(Item(), "neck")).Error);
        Assert.Equal(4, await _db.AvatarAttachments.CountAsync(a => a.Equipped));
        await Attach(Item(), "skull", "replace"); Assert.Equal(3, await _db.AvatarAttachments.CountAsync(a => a.Equipped)); // replacing at a full point still works
    }

    [Fact]
    public async Task APlacementIsCheckedAndNeedsTheRightToModify()
    {
        Assert.Equal("bad_position", (await TryAttach(Item(), pos: [0, 50, 0])).Error);
        Assert.Equal("no_modify_right", (await TryAttach(Item(rights: ItemRights.Copy | ItemRights.Transfer), pos: [0, .1f, 0])).Error);
        await Attach(Item(rights: ItemRights.Copy | ItemRights.Transfer), "chest"); // a plain attach of a no-modify item is fine
    }

    // ---- editing --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task APlacementIsChangedAndKeptAsEntered()
    {
        var id = await Attach(Item(), "back");
        var r = await AvatarAttachments.TransformAsync(_db, _me, id, new([.1f, -.2f, .3f], [15.5f, -90, 180], [2, 2, .5f]), _now, default); Assert.True(r.Ok, r.Error);
        var row = (await _db.AvatarAttachments.FindAsync(id))!; Assert.Equal(15.5, row.RotationX); Assert.Equal(-90, row.RotationY); Assert.Equal(.5, row.ScaleZ, 4); Assert.Equal(-.2, row.PositionY, 4); Assert.Equal(2, row.Revision);
        await AvatarAttachments.TransformAsync(_db, _me, id, new(null, [1, 2, 3], null), _now, default);
        row = (await _db.AvatarAttachments.FindAsync(id))!; Assert.Equal(1, row.RotationX); Assert.Equal(2, row.ScaleX); // what was not sent stays as it was
    }

    [Fact]
    public async Task EditingNeedsAnObjectYouMayModifyThatIsWornAndAValidPlacement()
    {
        var locked = await Attach(Item(rights: ItemRights.Copy | ItemRights.Transfer), "chest"); var ok = await Attach(Item(), "back");
        Assert.Equal("no_modify_right", (await AvatarAttachments.TransformAsync(_db, _me, locked, new([0, .1f, 0], null, null), _now, default)).Error);
        Assert.Equal("no_modify_right", (await AvatarAttachments.ResetAsync(_db, _me, locked, null, _now, default)).Error);
        Assert.Equal("not_found", (await AvatarAttachments.TransformAsync(_db, _other, ok, new([0, .1f, 0], null, null), _now, default)).Error); // somebody else's
        Assert.Equal("bad_scale", (await AvatarAttachments.TransformAsync(_db, _me, ok, new(null, null, [100, 1, 1]), _now, default)).Error);
        Assert.Equal("nothing_to_change", (await AvatarAttachments.TransformAsync(_db, _me, ok, new(null, null, null), _now, default)).Error);
        await AvatarAttachments.DetachAsync(_db, _me, ok, _now, default); Assert.Equal("not_attached", (await AvatarAttachments.TransformAsync(_db, _me, ok, new([0, .1f, 0], null, null), _now, default)).Error);
    }

    [Fact]
    public async Task ResetPutsBackJustThePartAsked()
    {
        var id = await Attach(Item(), "back"); await AvatarAttachments.TransformAsync(_db, _me, id, new([.1f, .1f, .1f], [10, 20, 30], [2, 2, 2]), _now, default);
        await AvatarAttachments.ResetAsync(_db, _me, id, "rotation", _now, default); var row = (await _db.AvatarAttachments.FindAsync(id))!; Assert.Equal(0, row.RotationY); Assert.Equal(2, row.ScaleY); Assert.Equal(.1, row.PositionZ, 4);
        await AvatarAttachments.ResetAsync(_db, _me, id, null, _now, default); row = (await _db.AvatarAttachments.FindAsync(id))!; Assert.Equal((0d, 1d), (row.PositionZ, row.ScaleY));
        Assert.Equal("bad_part", (await AvatarAttachments.ResetAsync(_db, _me, id, "tudo", _now, default)).Error);
    }

    [Fact]
    public async Task EveryChangeRaisesTheRevision()
    {
        var id = await Attach(Item(), "back"); var first = (await _db.AvatarAttachments.FindAsync(id))!.Revision;
        await AvatarAttachments.TransformAsync(_db, _me, id, new([0, .1f, 0], null, null), _now, default); Assert.True((await _db.AvatarAttachments.FindAsync(id))!.Revision > first);
    }

    // ---- moving and removing --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnObjectMovesToAnotherPointAndStartsFromThatPointsOwnPlacementEvenWithoutTheRightToModify()
    {
        var id = await Attach(Item(rights: ItemRights.Copy | ItemRights.Transfer), "chest");
        var r = await AvatarAttachments.MoveAsync(_db, _me, id, new("hand_l", null), _now, default); Assert.True(r.Ok, r.Error);
        var row = (await _db.AvatarAttachments.FindAsync(id))!; Assert.Equal("hand_l", row.AttachmentPoint); Assert.Equal((0d, 1d), (row.PositionX, row.ScaleX));
        Assert.Equal("unknown_point", (await AvatarAttachments.MoveAsync(_db, _me, id, new("nada", null), _now, default)).Error); Assert.Equal("point_unavailable", (await AvatarAttachments.MoveAsync(_db, _me, id, new("tail", null), _now, default)).Error);
    }

    [Fact]
    public async Task MovingRespectsTheLimitsAndReplaceAtTheTarget()
    {
        AvatarAttachments.Limits = AttachmentPoints.DefaultLimits with { MaxPerPoint = 1 };
        var a = await Attach(Item(), "skull"); var b = await Attach(Item(), "chin");
        Assert.Equal("too_many_at_point", (await AvatarAttachments.MoveAsync(_db, _me, b, new("skull", null), _now, default)).Error);
        Assert.True((await AvatarAttachments.MoveAsync(_db, _me, b, new("skull", "replace"), _now, default)).Ok); Assert.False((await _db.AvatarAttachments.FindAsync(a))!.Equipped);
    }

    [Fact]
    public async Task TakingOffKeepsTheItemAndTheSavedPlacementForTheNextTime()
    {
        var item = Item(); var id = await Attach(item, "back"); await AvatarAttachments.TransformAsync(_db, _me, id, new([0, .2f, 0], [0, 45, 0], [1.5f, 1.5f, 1.5f]), _now, default);
        Assert.True((await AvatarAttachments.DetachAsync(_db, _me, id, _now, default)).Ok); Assert.NotNull(await _db.InventoryItems.FindAsync(item)); Assert.False((await _db.AvatarAttachments.FindAsync(id))!.Equipped);
        Assert.True((await AvatarAttachments.DetachAsync(_db, _me, id, _now, default)).Ok); // taking off twice is harmless
        var again = await Attach(item, "back"); Assert.Equal(id, again); var row = (await _db.AvatarAttachments.FindAsync(id))!; Assert.Equal(45, row.RotationY); Assert.Equal(1.5, row.ScaleX);
        Assert.Equal("not_found", (await AvatarAttachments.DetachAsync(_db, _other, id, _now, default)).Error);
    }

    [Fact]
    public async Task TakeEverythingOffOrOnlyOnePoint()
    {
        await Attach(Item(), "skull"); await Attach(Item(), "skull"); await Attach(Item(), "chest");
        Assert.Equal("unknown_point", (await AvatarAttachments.DetachAllAsync(_db, _me, "nada", _now, default)).Error);
        await AvatarAttachments.DetachAllAsync(_db, _me, "skull", _now, default); Assert.Equal(1, await _db.AvatarAttachments.CountAsync(a => a.Equipped));
        await AvatarAttachments.DetachAllAsync(_db, _me, null, _now, default); Assert.Equal(0, await _db.AvatarAttachments.CountAsync(a => a.Equipped));
    }

    // ---- consistency ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task WhatLeftThePersonIsTakenOffWhenTheListIsRead()
    {
        var kept = Item(); var given = Item(); var offered = Item(); await Attach(kept, "skull"); await Attach(given, "chest"); await Attach(offered, "neck");
        (await _db.InventoryItems.FindAsync(given))!.OwnerUserId = _other; (await _db.InventoryItems.FindAsync(offered))!.Status = "offered"; await _db.SaveChangesAsync();
        var list = JsonSerializer.SerializeToElement(await AvatarAttachments.ListAsync(_db, _me, _now, default)).GetProperty("attachments"); Assert.Equal(kept, Assert.Single(list.EnumerateArray()).GetProperty("inventoryItemId").GetGuid());
    }

    [Fact]
    public async Task OthersSeeOnlyWhatIsWornAndNeverTheOtherAvatarsRows()
    {
        var mine = await Attach(Item(), "skull"); var off = await Attach(Item(), "chest"); await AvatarAttachments.DetachAsync(_db, _me, off, _now, default); await Attach(Item(_other), "skull", user: _other);
        var avatar = await _db.Avatars.SingleAsync(a => a.UserId == _me); var worn = await AvatarAttachments.WornAsync(_db, avatar.Id, default);
        Assert.Equal(mine, JsonSerializer.SerializeToElement(Assert.Single(worn)).GetProperty("id").GetGuid());
    }

    [Fact]
    public void TheCatalogEndpointDescribesEveryPointAndTheLimits()
    {
        var json = JsonSerializer.SerializeToElement(AvatarAttachments.Catalog());
        Assert.Equal(AttachmentPoints.For(null).Count(), json.GetProperty("points").GetArrayLength()); Assert.Equal(38, json.GetProperty("limits").GetProperty("total").GetInt32());
        Assert.Contains(json.GetProperty("points").EnumerateArray(), p => p.GetProperty("key").GetString() == "hand_r" && p.GetProperty("bone").GetString() == "hand_r" && p.GetProperty("secondLife").GetString() == "Right Hand");
    }
}

/// <summary>What other players and the island servers get: the attachments inside the appearance, who may fetch their files, and the rules for the live preview of an edit.</summary>
public sealed class AttachmentsReplicationTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private Guid _me, _other; private Avatar _avatar = null!;

    public AttachmentsReplicationTests()
    {
        _me = Add("eu"); _other = Add("outro"); _avatar = _db.Avatars.Include(a => a.EquippedItems).Single(a => a.UserId == _me); AvatarAttachments.Limits = AttachmentPoints.DefaultLimits;
    }
    public void Dispose() => _db.Dispose();

    private Guid Add(string name)
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now, PresenceStatus = "online" });
        _db.Avatars.Add(new Avatar { Id = Guid.NewGuid(), UserId = id, DisplayName = name, BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); return id;
    }

    private (Guid Item, Guid Asset) Item(Guid owner, string name = "Chapéu")
    {
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner, CreatorUserId = owner, Name = name, Category = "models", CreatedAt = _now, UpdatedAt = _now };
        var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = owner, AssetId = asset.Id, DisplayName = name, CreatedAt = _now, UpdatedAt = _now };
        _db.Assets.Add(asset); _db.InventoryItems.Add(item); _db.SaveChanges(); return (item.Id, asset.Id);
    }

    private async Task<(Guid Row, Guid Asset)> Wear(string point = "skull")
    {
        var (item, asset) = Item(_me); var r = await AvatarAttachments.AttachAsync(_db, _me, new(item, point, null, null, null, null), _now, default); Assert.True(r.Ok, r.Error);
        return (JsonSerializer.SerializeToElement(r.Data).GetProperty("id").GetGuid(), asset);
    }

    [Fact]
    public async Task TheAppearanceCarriesWhatIsWornAndOnlyThat()
    {
        var (worn, _) = await Wear("skull"); var (off, _) = await Wear("chest"); await AvatarAttachments.DetachAsync(_db, _me, off, _now, default);
        var json = await AvatarAppearanceJson.BuildAsync(_db, _avatar, default); using var doc = JsonDocument.Parse(json);
        using var list = JsonDocument.Parse(doc.RootElement.GetProperty("bodyAttachmentsJson").GetString()!);
        Assert.Equal(worn, Assert.Single(list.RootElement.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal([worn], AttachmentLive.WornIds(json).ToArray()); // the island server finds the same ids
    }

    [Fact]
    public async Task WhatLeftThePersonIsNoLongerShownToOthers()
    {
        var (row, _) = await Wear(); var attachment = await _db.AvatarAttachments.FindAsync(row); (await _db.InventoryItems.FindAsync(attachment!.InventoryItemId))!.OwnerUserId = _other; await _db.SaveChangesAsync();
        Assert.Empty(await AvatarAttachments.WornAsync(_db, _avatar.Id, default)); Assert.Empty(AttachmentLive.WornIds(await AvatarAppearanceJson.BuildAsync(_db, _avatar, default)));
    }

    [Fact]
    public async Task AnAssetWornOnABodyCanBeFetchedByEveryoneOnTheIsland() { var (_, asset) = await Wear(); Assert.True(await AvatarAttachments.AssetIsWornAsync(_db, asset, default)); Assert.False(await AvatarAttachments.AssetIsWornAsync(_db, Item(_me).Asset, default)); }

    [Fact]
    public async Task AnAssetNoLongerWornIsNotHandedOut() { var (row, asset) = await Wear(); await AvatarAttachments.DetachAsync(_db, _me, row, _now, default); Assert.False(await AvatarAttachments.AssetIsWornAsync(_db, asset, default)); }

    [Fact]
    public void TheLivePreviewIsLimitedInRateAndInValues()
    {
        var t = _now; Assert.True(AttachmentLive.MayRelay(null, t)); Assert.False(AttachmentLive.MayRelay(t, t.AddMilliseconds(20))); Assert.True(AttachmentLive.MayRelay(t, t.AddMilliseconds(61)));
        Assert.Null(AttachmentLive.Check([0, .1f, 0, 10, 20, 30, 1, 1, 1]));
        Assert.Equal("bad_values", AttachmentLive.Check([0, 0, 0])); Assert.Equal("bad_values", AttachmentLive.Check(null));
        Assert.Equal("bad_position", AttachmentLive.Check([0, 99, 0, 0, 0, 0, 1, 1, 1])); Assert.Equal("bad_position", AttachmentLive.Check([float.NaN, 0, 0, 0, 0, 0, 1, 1, 1]));
        Assert.Equal("bad_scale", AttachmentLive.Check([0, 0, 0, 0, 0, 0, 1, 50, 1])); Assert.Equal("bad_rotation", AttachmentLive.Check([0, 0, 0, float.PositiveInfinity, 0, 0, 1, 1, 1]));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("{}")] [InlineData("not json")] [InlineData("{\"bodyAttachmentsJson\":3}")] [InlineData("{\"bodyAttachmentsJson\":\"nope\"}")]
    public void AnAppearanceWithoutValidAttachmentsHasNoIds(string? json) => Assert.Empty(AttachmentLive.WornIds(json));
}

/// <summary>Outfits that remember the objects worn, bodies with a tail or wings, and a change of body that moves objects without losing the placement.</summary>
public sealed class AttachmentOutfitsTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static JsonElement Web(object? data) => JsonSerializer.SerializeToElement(data, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private readonly Guid _me = Guid.NewGuid(), _friend = Guid.NewGuid();

    public AttachmentOutfitsTests() { Seed(_me, "Marina"); Seed(_friend, "Rafa"); AvatarAttachments.Limits = AttachmentPoints.DefaultLimits; }
    public void Dispose() { AvatarAttachments.Limits = AttachmentPoints.DefaultLimits; _db.Dispose(); }

    private void Seed(Guid user, string name)
    {
        _db.Users.Add(new User { Id = user, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now, PresenceStatus = "online" });
        var avatar = new Avatar { Id = Guid.NewGuid(), UserId = user, DisplayName = name, BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now };
        avatar.Appearance = new AvatarAppearance { Id = Guid.NewGuid(), AvatarId = avatar.Id, SkinColor = "#C98F65", HairColor = "#2B1B14", HairStyle = "short", CreatedAt = _now, UpdatedAt = _now };
        foreach (var (slot, key) in new[] { ("top", "tee_basic"), ("bottom", "pants_basic"), ("shoes", "sneakers_basic") }) avatar.EquippedItems.Add(new EquippedItem { Id = Guid.NewGuid(), AvatarId = avatar.Id, Slot = slot, ItemKey = key, TintColor = "#FFFFFF", EquippedAt = _now });
        _db.Avatars.Add(avatar); _db.SaveChanges();
    }

    private Guid Item(Guid owner, int rights = ItemRights.All, string name = "Chapéu")
    {
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner, CreatorUserId = owner, Name = name, Category = "models", CreatedAt = _now, UpdatedAt = _now };
        var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = owner, AssetId = asset.Id, DisplayName = name, OwnerRights = rights, CreatedAt = _now, UpdatedAt = _now };
        _db.Assets.Add(asset); _db.InventoryItems.Add(item); _db.SaveChanges(); return item.Id;
    }

    private async Task<Guid> Wear(Guid item, string point, float[]? pos = null, float[]? rot = null, float[]? scale = null)
    {
        var r = await AvatarAttachments.AttachAsync(_db, _me, new(item, point, null, pos, rot, scale), _now, default); Assert.True(r.Ok, r.Error);
        return JsonSerializer.SerializeToElement(r.Data).GetProperty("id").GetGuid();
    }

    private Avatar Mine(Guid? user = null) { _db.ChangeTracker.Clear(); return _db.Avatars.Include(a => a.Appearance).Include(a => a.EquippedItems).Single(a => a.UserId == (user ?? _me)); }

    private async Task<AvatarModel> Body(params string[] profiles)
    {
        var avatar = Mine(); var model = new AvatarModel { Id = Guid.NewGuid(), AvatarId = avatar.Id, Name = "Corpo", ContentHash = Guid.NewGuid().ToString("N"), StorageKey = "k", ReportJson = JsonSerializer.Serialize(new { profiles }), CreatedAt = _now };
        _db.AvatarModels.Add(model); (await _db.Avatars.FindAsync(avatar.Id))!.CustomModelId = model.Id; await _db.SaveChangesAsync(); return model;
    }

    // ---- profiles -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ProfilesAreFoundFromTheNamesOfTheBones()
    {
        Assert.Equal([], AttachmentProfiles.Detect(["Hips", "Spine", "Head", "Hand_L"]));
        Assert.Equal(["tail"], AttachmentProfiles.Detect(["Hips", "Tail_01", "Tail_02"]));
        Assert.Equal(["wings"], AttachmentProfiles.Detect(["Wing_L", "Wing_R"])); Assert.DoesNotContain("wings", AttachmentProfiles.Detect(["Wing_L"]));
        Assert.Contains("fingers", AttachmentProfiles.Detect(["Thumb1_L", "Index1_L", "Middle1_L", "Ring1_L", "Pinky1_L"])); Assert.DoesNotContain("fingers", AttachmentProfiles.Detect(["Index1_L"]));
        Assert.Equal(["tail", "wings"], AttachmentProfiles.FromReport("{\"profiles\":[\"tail\",\"wings\",\"nonsense\"]}")); Assert.Empty(AttachmentProfiles.FromReport("not json")); Assert.Empty(AttachmentProfiles.FromReport("{}"));
    }

    [Fact]
    public async Task ATailPointExistsOnlyOnABodyThatHasATail()
    {
        var item = Item(_me); var refused = await AvatarAttachments.AttachAsync(_db, _me, new(item, "tail", null, null, null, null), _now, default); Assert.Equal("point_unavailable", refused.Error);
        await Body("tail"); await Wear(item, "tail");
        var list = JsonSerializer.SerializeToElement(await AvatarAttachments.ListAsync(_db, _me, _now, default)); Assert.Contains(list.GetProperty("profiles").EnumerateArray(), p => p.GetString() == "tail");
    }

    [Fact]
    public async Task ChangingToABodyWithoutTheTailMovesTheObjectAndKeepsItsPlacementThenItGoesBack()
    {
        await Body("tail"); var id = await Wear(Item(_me), "tail", [.01f, .02f, .03f], [0, 45, 0], [2, 2, 2]);
        _db.ChangeTracker.Clear(); (await _db.Avatars.SingleAsync(a => a.UserId == _me)).CustomModelId = null; await _db.SaveChangesAsync();
        var shown = JsonSerializer.SerializeToElement(await AvatarAttachments.ListAsync(_db, _me, _now, default)).GetProperty("attachments")[0];
        Assert.Equal("pelvis_back", shown.GetProperty("point").GetString()); Assert.Equal("tail", shown.GetProperty("originalPoint").GetString()); Assert.Equal("Cauda", shown.GetProperty("originalPointName").GetString());
        Assert.Equal(45, shown.GetProperty("rotation")[1].GetDouble()); Assert.Equal(2, shown.GetProperty("scale")[0].GetSingle()); Assert.Equal(.02f, shown.GetProperty("position")[1].GetSingle(), 4); // the placement is untouched
        await Body("tail"); var back = JsonSerializer.SerializeToElement(await AvatarAttachments.ListAsync(_db, _me, _now, default)).GetProperty("attachments")[0];
        Assert.Equal("tail", back.GetProperty("point").GetString()); Assert.Equal(JsonValueKind.Null, back.GetProperty("originalPoint").ValueKind); Assert.Equal(id, back.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task OthersSeeTheObjectOnTheNearestPointWhileTheBodyLacksTheOne()
    {
        await Body("wings"); await Wear(Item(_me), "wing_l"); _db.ChangeTracker.Clear(); (await _db.Avatars.SingleAsync(a => a.UserId == _me)).CustomModelId = null; await _db.SaveChangesAsync();
        var worn = await AvatarAttachments.WornAsync(_db, Mine().Id, default); Assert.Equal("back", JsonSerializer.SerializeToElement(Assert.Single(worn)).GetProperty("point").GetString());
    }

    // ---- sway ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheSwayIsKeptWithinZeroAndOneAndNeedsTheRightToModify()
    {
        var id = await Wear(Item(_me), "chest"); var locked = await Wear(Item(_me, ItemRights.Copy | ItemRights.Transfer), "back");
        var ok = await AvatarAttachments.TransformAsync(_db, _me, id, new(null, null, null, .6f), _now, default); Assert.True(ok.Ok, ok.Error);
        Assert.Equal(.6f, JsonSerializer.SerializeToElement(ok.Data).GetProperty("sway").GetSingle(), 3);
        Assert.Equal("bad_sway", (await AvatarAttachments.TransformAsync(_db, _me, id, new(null, null, null, 2f), _now, default)).Error); Assert.Equal("bad_sway", (await AvatarAttachments.TransformAsync(_db, _me, id, new(null, null, null, float.NaN), _now, default)).Error);
        Assert.Equal("no_modify_right", (await AvatarAttachments.TransformAsync(_db, _me, locked, new(null, null, null, .5f), _now, default)).Error);
        await AvatarAttachments.TransformAsync(_db, _me, id, new([0, .1f, 0], null, null), _now, default); Assert.Equal(.6f, JsonSerializer.SerializeToElement((await AvatarAttachments.TransformAsync(_db, _me, id, new(null, [0, 5, 0], null), _now, default)).Data).GetProperty("sway").GetSingle(), 3); // other edits keep it
    }

    // ---- outfits ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnOutfitRemembersTheObjectsAndWhereTheySit()
    {
        var hat = Item(_me); var watch = Item(_me, name: "Relógio"); await Wear(hat, "head_top", [0, .02f, 0], [0, 30, 0], [1.5f, 1.5f, 1.5f]); await Wear(watch, "wrist_l");
        var saved = await AvatarLooks.SaveCurrentAsync(_db, _me, "Festa", _now, default); Assert.True(saved.Ok, saved.Error); var lookId = Web(saved.Data).GetProperty("id").GetGuid();
        Assert.Equal(2, Web(saved.Data).GetProperty("bodyAttachments").GetInt32());
        await AvatarAttachments.DetachAllAsync(_db, _me, null, _now, default); var changed = await AvatarAttachments.TransformAsync(_db, _me, (await _db.AvatarAttachments.FirstAsync()).Id, new([0, 0, 0], null, null), _now, default); Assert.Equal("not_attached", changed.Error);
        var applied = await AvatarLooks.ApplyAsync(_db, _me, lookId, _now, default); Assert.True(applied.Ok, applied.Error);
        var rows = await _db.AvatarAttachments.Where(a => a.Equipped).OrderBy(a => a.AttachmentPoint).ToListAsync(); Assert.Equal(["head_top", "wrist_l"], rows.Select(r => r.AttachmentPoint));
        var back = rows.Single(r => r.AttachmentPoint == "head_top"); Assert.Equal(30, back.RotationY); Assert.Equal(1.5, back.ScaleX); Assert.Equal(.02, back.PositionY, 4);
    }

    [Fact]
    public async Task PuttingAnOutfitOnTakesOffWhatIsWornNow()
    {
        var a = Item(_me); await Wear(a, "skull"); var saved = await AvatarLooks.SaveCurrentAsync(_db, _me, "Só chapéu", _now, default); var lookId = Web(saved.Data).GetProperty("id").GetGuid();
        var b = Item(_me, name: "Colar"); await Wear(b, "neck");
        Assert.True((await AvatarLooks.ApplyAsync(_db, _me, lookId, _now, default)).Ok); Assert.Equal(["skull"], (await _db.AvatarAttachments.Where(x => x.Equipped).ToListAsync()).Select(x => x.AttachmentPoint));
    }

    [Fact]
    public async Task WhatIsGoneFromTheInventoryIsLeftOutAndTold()
    {
        var kept = Item(_me); var gone = Item(_me, name: "Perdido"); await Wear(kept, "skull"); await Wear(gone, "chest");
        var lookId = Web((await AvatarLooks.SaveCurrentAsync(_db, _me, "Visual", _now, default)).Data).GetProperty("id").GetGuid();
        (await _db.InventoryItems.FindAsync(gone))!.OwnerUserId = _friend; await _db.SaveChangesAsync();
        var applied = await AvatarLooks.ApplyAsync(_db, _me, lookId, _now, default); Assert.True(applied.Ok, applied.Error);
        Assert.Equal(["skull"], (await _db.AvatarAttachments.Where(x => x.Equipped && x.AvatarId == Mine().Id).ToListAsync()).Select(x => x.AttachmentPoint));
        Assert.Contains("attachment:", JsonSerializer.Serialize(applied.Data));
    }

    [Fact]
    public async Task AnOutfitOnABodyWithoutTheTailPutsItOnTheNearestPointAndAnItemYouCannotModifyKeepsDefaults()
    {
        var tailBody = await Body("tail"); var tail = Item(_me, name: "Cauda"); var locked = Item(_me, ItemRights.Copy | ItemRights.Transfer, "Broche"); await Wear(tail, "tail", [0, .1f, 0]); await Wear(locked, "chest");
        var lookId = Web((await AvatarLooks.SaveCurrentAsync(_db, _me, "Cauda", _now, default)).Data).GetProperty("id").GetGuid();
        _db.ChangeTracker.Clear(); (await _db.Avatars.SingleAsync(a => a.UserId == _me)).CustomModelId = null; (await _db.AvatarModels.FindAsync(tailBody.Id))!.DeletedAt = _now; await _db.SaveChangesAsync(); // the body is gone too
        Assert.True((await AvatarLooks.ApplyAsync(_db, _me, lookId, _now, default)).Ok);
        var row = await _db.AvatarAttachments.SingleAsync(a => a.InventoryItemId == tail && a.Equipped); Assert.Equal("pelvis_back", row.AttachmentPoint); Assert.Equal("tail", AvatarAttachments.OriginalPoint(row)); Assert.Equal(.1, row.PositionY, 4);
    }

    [Fact]
    public async Task ACodeSharedWithSomebodyElseNeverBringsTheSendersObjects()
    {
        await Wear(Item(_me), "skull"); var saved = await AvatarLooks.SaveCurrentAsync(_db, _me, "Meu visual", _now, default); var id = Web(saved.Data).GetProperty("id").GetGuid();
        var shared = await AvatarLooks.ShareAsync(_db, _me, id, true, _now, default); var code = Web(shared.Data).GetProperty("shareCode").GetString();
        var imported = await AvatarLooks.ImportAsync(_db, _friend, code, null, _now, default); Assert.True(imported.Ok, imported.Error);
        Assert.Equal(0, Web(imported.Data).GetProperty("bodyAttachments").GetInt32());
    }

    [Fact]
    public async Task AnOlderOutfitWithoutObjectsLeavesWhatIsWornAsItIs()
    {
        await Wear(Item(_me), "skull"); var old = new AvatarLook { Id = Guid.NewGuid(), AvatarId = Mine().Id, Name = "Antigo", SnapshotJson = JsonSerializer.Serialize(AvatarLooks.Snapshot(Mine()), new JsonSerializerOptions(JsonSerializerDefaults.Web)), CreatedAt = _now, UpdatedAt = _now };
        _db.AvatarLooks.Add(old); await _db.SaveChangesAsync();
        Assert.True((await AvatarLooks.ApplyAsync(_db, _me, old.Id, _now, default)).Ok); Assert.Equal(1, await _db.AvatarAttachments.CountAsync(a => a.Equipped));
    }

    [Fact]
    public async Task AnOutfitNeverBreaksTheLimits()
    {
        AvatarAttachments.Limits = AttachmentPoints.DefaultLimits with { MaxPerPoint = 2 };
        var ids = new[] { Item(_me), Item(_me), Item(_me) }; await Wear(ids[0], "skull"); await Wear(ids[1], "skull");
        var lookId = Web((await AvatarLooks.SaveCurrentAsync(_db, _me, "Cheio", _now, default)).Data).GetProperty("id").GetGuid();
        var snapshot = JsonSerializer.Deserialize<LookSnapshot>((await _db.AvatarLooks.FindAsync(lookId))!.SnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var padded = snapshot with { BodyAttachments = [.. snapshot.BodyAttachments!, new LookAttachment(ids[2], "skull", [0, 0, 0], [0, 0, 0], [1, 1, 1])] }; (await _db.AvatarLooks.FindAsync(lookId))!.SnapshotJson = JsonSerializer.Serialize(padded, new JsonSerializerOptions(JsonSerializerDefaults.Web)); await _db.SaveChangesAsync();
        Assert.True((await AvatarLooks.ApplyAsync(_db, _me, lookId, _now, default)).Ok); Assert.Equal(2, await _db.AvatarAttachments.CountAsync(a => a.Equipped));
    }
}
