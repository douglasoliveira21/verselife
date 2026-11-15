using LifeVerse.Shared.Interaction;
using LifeVerse.Shared.Items;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Which slices of the right-click menu a person may use, and the reason written for each one they may not.</summary>
public sealed class ContextPermissionsTests
{
    private static readonly Guid Me = Guid.NewGuid(), Other = Guid.NewGuid();
    private static ObjectFacts Obj(Guid owner, int rights = ItemRights.All, string click = "touch", bool locked = false) => new(owner, owner, null, rights, click, locked);
    private static ItemFacts Item(string category = "models", int rights = ItemRights.All, bool trash = false, bool offered = false) => new(Me, rights, category, trash, offered);
    private static ContextActor Person => new(Me);

    [Fact]
    public void The_owner_can_edit_take_copy_and_delete()
    {
        var mine = Obj(Me);
        foreach (var c in new[] { Capability.Edit, Capability.Take, Capability.TakeCopy, Capability.Delete }) Assert.True(ContextPermissions.ForObject(c, Person, mine).Allowed, c.ToString());
    }

    [Fact]
    public void Someone_elses_object_offers_touch_and_inspect_only_with_reasons_for_the_rest()
    {
        var theirs = Obj(Other);
        Assert.True(ContextPermissions.ForObject(Capability.Touch, Person, theirs).Allowed);
        Assert.True(ContextPermissions.ForObject(Capability.Inspect, Person, theirs).Allowed);
        foreach (var c in new[] { Capability.Edit, Capability.Take, Capability.TakeCopy, Capability.Delete, Capability.Return })
        {
            var d = ContextPermissions.ForObject(c, Person, theirs);
            Assert.False(d.Allowed, c.ToString()); Assert.False(string.IsNullOrWhiteSpace(d.Reason), c + " has a reason");
        }
    }

    [Fact]
    public void Rights_and_protection_limit_the_owner()
    {
        Assert.False(ContextPermissions.ForObject(Capability.Edit, Person, Obj(Me, ItemRights.Copy)).Allowed);
        Assert.False(ContextPermissions.ForObject(Capability.TakeCopy, Person, Obj(Me, ItemRights.Modify)).Allowed);
        var locked = Obj(Me, locked: true);
        Assert.False(ContextPermissions.ForObject(Capability.Edit, Person, locked).Allowed);
        Assert.False(ContextPermissions.ForObject(Capability.Delete, Person, locked).Allowed);
        Assert.Contains("protegido", ContextPermissions.ForObject(Capability.Edit, Person, locked).Reason!);
    }

    [Fact]
    public void Parcel_owners_and_admins_can_return_other_peoples_objects_but_not_their_own()
    {
        var theirs = Obj(Other);
        Assert.True(ContextPermissions.ForObject(Capability.Return, new ContextActor(Me, IsParcelOwner: true), theirs).Allowed);
        Assert.True(ContextPermissions.ForObject(Capability.Return, new ContextActor(Me, IsAdmin: true), theirs).Allowed);
        Assert.False(ContextPermissions.ForObject(Capability.Return, Person, theirs).Allowed);
        Assert.False(ContextPermissions.ForObject(Capability.Return, new ContextActor(Me, IsAdmin: true), Obj(Me)).Allowed);
        Assert.False(ContextPermissions.ForObject(Capability.Edit, new ContextActor(Me, IsParcelOwner: true), theirs).Allowed);      // a parcel owner returns, never edits
        Assert.True(ContextPermissions.ForObject(Capability.Delete, new ContextActor(Me, IsAdmin: true), theirs).Allowed);
    }

    [Fact]
    public void An_unknown_user_never_owns_anything() => Assert.False(ContextPermissions.ForObject(Capability.Edit, new ContextActor(Guid.Empty), Obj(Guid.Empty)).Allowed);

    [Theory]
    [InlineData("buy", Capability.Buy, "Comprar")]
    [InlineData("pay", Capability.Pay, "Pagar")]
    [InlineData("open", Capability.Open, "Abrir")]
    [InlineData("touch", Capability.Touch, "Tocar")]
    [InlineData("none", Capability.Touch, "Tocar")]
    public void The_click_action_names_the_main_slice(string click, Capability capability, string label)
    {
        Assert.Equal(capability, ContextPermissions.PrimaryAction(click)); Assert.Equal(label, ContextPermissions.PrimaryLabel(click));
    }

    [Fact]
    public void Objects_that_ignore_clicks_cannot_be_touched() => Assert.False(ContextPermissions.ForObject(Capability.Touch, Person, Obj(Other, click: "ignore")).Allowed);

    [Fact]
    public void Item_actions_follow_type_and_rights()
    {
        Assert.True(ContextPermissions.ForItem(Capability.ItemPlace, Person, Item("models")).Allowed);
        Assert.False(ContextPermissions.ForItem(Capability.ItemPlace, Person, Item("textures")).Allowed);
        Assert.False(ContextPermissions.ForItem(Capability.ItemCopy, Person, Item(rights: ItemRights.Transfer)).Allowed);
        Assert.False(ContextPermissions.ForItem(Capability.ItemSend, Person, Item(rights: ItemRights.Copy)).Allowed);
        Assert.True(ContextPermissions.ForItem(Capability.ItemSend, Person, Item(rights: ItemRights.Transfer)).Allowed);
        Assert.False(ContextPermissions.ForItem(Capability.ItemWear, Person, Item("sounds")).Allowed);
    }

    [Fact]
    public void The_trash_only_restores_and_purges()
    {
        var trash = Item(trash: true);
        Assert.True(ContextPermissions.ForItem(Capability.ItemRestore, Person, trash).Allowed); Assert.True(ContextPermissions.ForItem(Capability.ItemPurge, Person, trash).Allowed);
        foreach (var c in new[] { Capability.ItemPlace, Capability.ItemCopy, Capability.ItemSend, Capability.ItemDelete, Capability.ItemRename }) Assert.False(ContextPermissions.ForItem(c, Person, trash).Allowed, c.ToString());
        Assert.False(ContextPermissions.ForItem(Capability.ItemRestore, Person, Item()).Allowed); Assert.False(ContextPermissions.ForItem(Capability.ItemPurge, Person, Item()).Allowed);
        Assert.True(ContextPermissions.ForItem(Capability.ItemRights, Person, trash).Allowed);
    }

    [Fact]
    public void An_offered_item_waits_for_the_answer() => Assert.False(ContextPermissions.ForItem(Capability.ItemSend, Person, Item(offered: true)).Allowed);

    [Fact]
    public void Someone_elses_item_is_off_limits() => Assert.False(ContextPermissions.ForItem(Capability.ItemRights, new ContextActor(Other), Item()).Allowed);
}

public sealed class ContextParcelPermissionsTests
{
    private static readonly ContextActor Person = new(Guid.NewGuid());
    private static ParcelFacts Parcel(string state = "owned", bool mine = false, bool terrain = false, string build = "owner") => new(true, state, mine, terrain, build);

    [Fact]
    public void Unknown_ground_only_offers_about()
    {
        Assert.True(ContextPermissions.ForParcel(Capability.AboutLand, Person, ParcelFacts.Unknown).Allowed);
        foreach (var c in new[] { Capability.TerrainEdit, Capability.BuyLand, Capability.RentLand, Capability.ManageLand, Capability.BuildHere })
            Assert.False(ContextPermissions.ForParcel(c, Person, ParcelFacts.Unknown).Allowed, c.ToString());
    }

    [Fact]
    public void Buying_and_renting_depend_on_the_state()
    {
        Assert.True(ContextPermissions.ForParcel(Capability.BuyLand, Person, Parcel("sale")).Allowed);
        Assert.False(ContextPermissions.ForParcel(Capability.BuyLand, Person, Parcel("owned")).Allowed);
        Assert.False(ContextPermissions.ForParcel(Capability.BuyLand, Person, Parcel("sale", mine: true)).Allowed);
        Assert.True(ContextPermissions.ForParcel(Capability.RentLand, Person, Parcel("rent")).Allowed);
        Assert.False(ContextPermissions.ForParcel(Capability.RentLand, Person, Parcel("sale")).Allowed);
    }

    [Fact]
    public void Only_the_holder_manages_and_terrain_follows_the_server_flag()
    {
        Assert.True(ContextPermissions.ForParcel(Capability.ManageLand, Person, Parcel("own", mine: true)).Allowed);
        Assert.False(ContextPermissions.ForParcel(Capability.ManageLand, Person, Parcel("public")).Allowed);
        Assert.True(ContextPermissions.ForParcel(Capability.TerrainEdit, Person, Parcel(terrain: true)).Allowed);
        Assert.False(ContextPermissions.ForParcel(Capability.TerrainEdit, Person, Parcel("own", mine: true)).Allowed);   // the flag comes from the server, not from owning
    }

    [Theory]
    [InlineData("owner", false, false)]
    [InlineData("owner", true, true)]
    [InlineData("everyone", false, true)]
    [InlineData("list", false, true)]
    [InlineData("none", false, false)]
    [InlineData("none", true, true)]
    public void Building_follows_the_build_mode(string mode, bool mine, bool allowed) => Assert.Equal(allowed, ContextPermissions.ForParcel(Capability.BuildHere, Person, Parcel(mine: mine, build: mode)).Allowed);

    [Fact]
    public void Every_refusal_has_a_reason()
    {
        foreach (var c in new[] { Capability.TerrainEdit, Capability.BuyLand, Capability.RentLand, Capability.ManageLand, Capability.BuildHere })
        {
            var d = ContextPermissions.ForParcel(c, Person, Parcel("public", build: "none"));
            if (!d.Allowed) Assert.False(string.IsNullOrWhiteSpace(d.Reason), c.ToString());
        }
    }
}

public sealed class ContextPersonPermissionsTests
{
    private static readonly ContextActor Me = new(Guid.NewGuid());
    private static Decision D(Capability c, string relation = "none", bool self = false, ContextActor? actor = null) => ContextPermissions.ForPerson(c, actor ?? Me, new PersonFacts(relation, self));

    [Theory]
    [InlineData("none", true, false, false)]
    [InlineData("", true, false, false)]
    [InlineData("friend", false, false, true)]
    [InlineData("pending_out", false, false, false)]
    [InlineData("pending_in", false, true, false)]
    public void Friendship_follows_the_relation(string relation, bool add, bool accept, bool remove)
    {
        Assert.Equal(add, D(Capability.PersonAddFriend, relation).Allowed);
        Assert.Equal(accept, D(Capability.PersonAcceptFriend, relation).Allowed);
        Assert.Equal(remove, D(Capability.PersonRemoveFriend, relation).Allowed);
    }

    [Fact]
    public void Every_refusal_says_why()
    {
        foreach (var c in new[] { Capability.PersonAddFriend, Capability.PersonAcceptFriend, Capability.PersonRemoveFriend })
            foreach (var relation in new[] { "none", "friend", "pending_out", "pending_in" })
            { var d = D(c, relation); if (!d.Allowed) Assert.False(string.IsNullOrWhiteSpace(d.Reason), $"{c}/{relation}"); }
    }

    [Fact]
    public void Nothing_social_applies_to_yourself()
    {
        foreach (var c in new[] { Capability.PersonMessage, Capability.PersonPay, Capability.PersonAddFriend, Capability.PersonBlock, Capability.PersonReport, Capability.PersonHide, Capability.PersonMute })
            Assert.False(D(c, self: true).Allowed, c.ToString());
    }

    [Fact]
    public void Signed_out_people_can_only_use_the_local_choices()
    {
        var anonymous = new ContextActor(Guid.Empty);
        foreach (var c in new[] { Capability.PersonMessage, Capability.PersonPay, Capability.PersonAddFriend, Capability.PersonBlock, Capability.PersonReport }) Assert.False(D(c, actor: anonymous).Allowed, c.ToString());
        Assert.True(D(Capability.PersonHide, actor: anonymous).Allowed); Assert.True(D(Capability.PersonMute, actor: anonymous).Allowed);
    }
}

public sealed class ContextModerationPermissionsTests
{
    private static readonly ContextActor Me = new(Guid.NewGuid());
    private static Decision D(Capability c, bool admin = false, bool onParcel = true, bool onMine = false, ContextActor? actor = null) => ContextPermissions.ForPerson(c, actor ?? Me, new PersonFacts("none", false, false, false, admin, onParcel, onMine));

    [Fact]
    public void Parcel_owners_and_admins_can_eject_and_ban_on_the_parcel()
    {
        foreach (var c in new[] { Capability.PersonEjectParcel, Capability.PersonBanParcel })
        {
            Assert.True(D(c, onMine: true).Allowed, c.ToString()); Assert.True(D(c, admin: true).Allowed, c.ToString());
            var refused = D(c); Assert.False(refused.Allowed); Assert.False(string.IsNullOrWhiteSpace(refused.Reason));
        }
    }

    [Fact]
    public void Nobody_is_ejected_from_nowhere() => Assert.False(D(Capability.PersonEjectParcel, onParcel: false, onMine: true).Allowed);

    [Fact]
    public void Only_island_admins_can_kick_from_the_region()
    {
        Assert.True(D(Capability.PersonKickRegion, admin: true).Allowed);
        Assert.False(D(Capability.PersonKickRegion, onMine: true).Allowed);
    }

    [Fact]
    public void Signed_out_people_moderate_nothing()
    {
        var nobody = new ContextActor(Guid.Empty);
        foreach (var c in new[] { Capability.PersonEjectParcel, Capability.PersonBanParcel, Capability.PersonKickRegion }) Assert.False(D(c, true, true, true, nobody).Allowed, c.ToString());
    }
}
