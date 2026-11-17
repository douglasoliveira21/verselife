using LifeVerse.Scripting;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>inventory.giveItem: only to an avatar who allowed GIVE_ITEMS, only items the object carries (not scripts or notecards), limited per minute, and the script hears how the delivery ended.</summary>
public sealed class LuaGiveItemTests
{
    private static readonly Guid Owner = Guid.NewGuid(), Ana = Guid.NewGuid(), Gift = Guid.NewGuid();

    private sealed class World : IScriptWorld
    {
        public double Now { get; set; } = 100; public Dictionary<Guid, ScriptObjectState> Objects { get; } = []; public List<string> Said { get; } = []; public List<(Guid Avatar, Guid Asset, string Name)> Started { get; } = []; public bool CanStart { get; set; } = true; public List<int> Prices { get; } = [];
        public bool SetObjectPrice(ScriptDefinition script, int price) { Prices.Add(price); return true; }
        public List<(Guid Request, Guid Avatar, long Amount)> Payouts { get; } = [];
        public bool StartPayout(ScriptDefinition script, Guid requestId, Guid avatarId, long amount) { Payouts.Add((requestId, avatarId, amount)); return true; }
        public List<(int Default, int[] Quick)> PayPrices { get; } = [];
        public bool SetPayPrice(ScriptDefinition script, int defaultPrice, int[] quick) { PayPrices.Add((defaultPrice, quick)); return true; }
        public ScriptObjectState? GetObject(Guid id) => Objects.GetValueOrDefault(id);
        public void ApplyChange(ScriptDefinition script, ObjectChange change) { }
        public void Say(ScriptDefinition script, string text) => Said.Add(text);
        public string? AvatarName(Guid avatarId) => avatarId == Ana ? "Ana" : null;
        public IReadOnlyList<ScriptItem> GetContents(Guid objectId) => [new("Brinde", "objects", Gift), new("Código", "scripts", Guid.NewGuid()), new("Nota", "notecards", Guid.NewGuid()), new("Sem id", "objects", null)];
        public bool StartGiveItem(ScriptDefinition script, Guid avatarId, Guid assetId, string itemName) { if (CanStart) Started.Add((avatarId, assetId, itemName)); return CanStart; }
    }

    private static (ScriptHost Host, World World, Guid Object, List<ScriptReport> Reports) Rig(string source)
    {
        var world = new World(); var obj = Guid.NewGuid(); world.Objects[obj] = new ScriptObjectState(obj, Owner, "Máquina", new Vec3(0, 0, 0), default, new Vec3(1, 1, 1));
        var host = new ScriptHost(world, new ScriptHostOptions(MillisecondsPerEvent: 2000)); var reports = new List<ScriptReport>(); host.Report += reports.Add;
        host.Sync([new ScriptDefinition(Guid.NewGuid(), obj, Owner, "maquina", source, 1)]); host.Process(500); return (host, world, obj, reports);
    }

    private static void Touch(ScriptHost host, Guid obj) { host.Enqueue(new ScriptEvent(obj, "touch")); host.Process(500); }

    [Fact]
    public void ItGivesOnlyToAnAvatarWhoAllowed_AndOnlyWhatTheObjectCarries()
    {
        var (host, world, obj, reports) = Rig($"function on_touch() local ok = inventory.giveItem('{Ana}', 'brinde') chat.say(tostring(ok)) end");
        Touch(host, obj); Assert.Contains("GIVE_ITEMS", Assert.Single(reports).Message); Assert.Empty(world.Started);                                           // nobody allowed it yet
        host.Permissions.Grant(host.Scripts.Single().ScriptId, Ana, ScriptPermission.GiveItems); reports.Clear(); Touch(host, obj);
        Assert.Empty(reports); Assert.Equal(["true"], world.Said); Assert.Equal([(Ana, Gift, "Brinde")], world.Started);                                       // the name is looked up without caring for case
    }

    [Theory]
    [InlineData("'Inexistente'", "does not carry")]
    [InlineData("'Código'", "cannot be given")]
    [InlineData("'Nota'", "cannot be given")]
    [InlineData("'Sem id'", "cannot be given")]
    [InlineData("42", "does not carry")]
    public void ItRefusesWhatCannotBeGiven(string item, string why)
    {
        var (host, world, obj, reports) = Rig($"function on_touch() inventory.giveItem('{Ana}', {item}) end");
        host.Permissions.Grant(host.Scripts.Single().ScriptId, Ana, ScriptPermission.GiveItems); Touch(host, obj);
        Assert.Contains(why, Assert.Single(reports).Message); Assert.Empty(world.Started);
    }

    [Fact]
    public void ABadAvatarIdIsAnError_AndAWorldThatCannotDeliverSaysFalse()
    {
        var (host, world, obj, reports) = Rig("function on_touch() inventory.giveItem('nao-e-id', 'Brinde') end"); Touch(host, obj); Assert.Single(reports);
        var (host2, world2, obj2, reports2) = Rig($"function on_touch() chat.say(tostring(inventory.giveItem('{Ana}', 'Brinde'))) end"); world2.CanStart = false;
        host2.Permissions.Grant(host2.Scripts.Single().ScriptId, Ana, ScriptPermission.GiveItems); Touch(host2, obj2); Assert.Equal(["false"], world2.Said); Assert.Empty(reports2);
    }

    [Fact]
    public void AScriptCannotGiveMoreThanSixAMinute_AndLaterItMay()
    {
        var (host, world, obj, reports) = Rig($"function on_touch() inventory.giveItem('{Ana}', 'Brinde') end");
        host.Permissions.Grant(host.Scripts.Single().ScriptId, Ana, ScriptPermission.GiveItems);
        for (var i = 0; i < ScriptHost.MaxGivesPerMinute; i++) Touch(host, obj); Assert.Empty(reports); Assert.Equal(ScriptHost.MaxGivesPerMinute, world.Started.Count);
        Touch(host, obj); Assert.Contains("at most", Assert.Single(reports).Message); Assert.Equal(ScriptHost.MaxGivesPerMinute, world.Started.Count);
        world.Now += 61; reports.Clear(); Touch(host, obj); Assert.Empty(reports); Assert.Equal(ScriptHost.MaxGivesPerMinute + 1, world.Started.Count);
    }


    [Fact]
    public void ThePriceOfTheObjectIsSetWithinLimits()
    {
        var (host, world, obj, reports) = Rig("function on_touch() chat.say(tostring(economy.setObjectPrice(150.9))) end");
        Touch(host, obj); Assert.Empty(reports); Assert.Equal(["true"], world.Said); Assert.Equal([150], world.Prices);                                   // whole lindens
        foreach (var bad in new[] { "-1", "10000001", "0/0" })
        {
            var (host2, world2, obj2, reports2) = Rig($"function on_touch() economy.setObjectPrice({bad}) end"); Touch(host2, obj2); Assert.Single(reports2); Assert.Empty(world2.Prices);
        }
        var (host3, world3, obj3, reports3) = Rig("function on_touch() economy.setObjectPrice(5) end");
        for (var i = 0; i < ScriptHost.MaxPriceChangesPerMinute; i++) Touch(host3, obj3); Assert.Empty(reports3); Touch(host3, obj3); Assert.Contains("at most", Assert.Single(reports3).Message);
        Assert.Equal(ScriptHost.MaxPriceChangesPerMinute, world3.Prices.Count);
    }

    [Fact]
    public void AScriptPaysOutOfTheOwnersWalletAndHearsTheResult()
    {
        var (host, world, obj, reports) = Rig($"function on_touch() chat.say(economy.pay('{Ana}', 50.7)) end function on_payout(e) chat.say('pago ' .. e.amount .. ' ' .. e.transaction_id) end function on_payout_failed(e) chat.say('falhou ' .. e.reason) end");
        Touch(host, obj); Assert.Empty(reports); var sent = Assert.Single(world.Payouts); Assert.Equal((Ana, 50L), (sent.Avatar, sent.Amount)); Assert.Equal([sent.Request.ToString()], world.Said);
        var script = host.Scripts.Single().ScriptId; var tx = Guid.NewGuid();
        host.CompletePayout(script, sent.Request, tx, null); host.CompletePayout(script, sent.Request, tx, null); host.CompletePayout(script, Guid.NewGuid(), tx, null); host.Process(500);          // a repeat and a made-up id do nothing
        Assert.Equal([sent.Request.ToString(), $"pago 50 {tx}"], world.Said);
        Touch(host, obj); var second = world.Payouts[1]; host.CompletePayout(script, second.Request, null, "not_authorized"); host.Process(500);
        Assert.Equal("falhou not_authorized", world.Said[^1]);
    }

    [Fact]
    public void ThePayoutsOfAScriptAreLimited()
    {
        foreach (var bad in new[] { "0", "-5", "100001" }) { var (h, w, o, r) = Rig($"function on_touch() economy.pay('{Ana}', {bad}) end"); Touch(h, o); Assert.Single(r); Assert.Empty(w.Payouts); }
        var (host, world, obj, reports) = Rig($"function on_touch() economy.pay('{Ana}', 1) end");
        for (var i = 0; i < ScriptHost.MaxPayoutsWaiting; i++) Touch(host, obj); Assert.Empty(reports); Touch(host, obj); Assert.Contains("waiting", Assert.Single(reports).Message); Assert.Equal(ScriptHost.MaxPayoutsWaiting, world.Payouts.Count);
    }

    [Fact]
    public void WhatAPayClickOffersIsSetWithinLimits()
    {
        var (host, world, obj, reports) = Rig("function on_touch() chat.say(tostring(economy.setPayPrice(25.9, {10, 25, 50, 100}))) end");
        Touch(host, obj); Assert.Empty(reports); Assert.Equal(["true"], world.Said); var set = Assert.Single(world.PayPrices); Assert.Equal(25, set.Default); Assert.Equal([10, 25, 50, 100], set.Quick);
        foreach (var bad in new[] { "-1, {}", "10000001, {}", "5, {1,2,3,4,5}", "5, {0}", "5, {\"a\"}" })
        {
            var (host2, world2, obj2, reports2) = Rig($"function on_touch() economy.setPayPrice({bad}) end"); Touch(host2, obj2); Assert.Single(reports2); Assert.Empty(world2.PayPrices);
        }
        var (host3, world3, obj3, reports3) = Rig("function on_touch() economy.setPayPrice() end"); Touch(host3, obj3); Assert.Empty(reports3); var cleared = Assert.Single(world3.PayPrices); Assert.Equal(0, cleared.Default); Assert.Empty(cleared.Quick);      // no arguments clears it
        var (host4, world4, obj4, reports4) = Rig("function on_touch() economy.setPayPrice(5, {}) end");
        for (var i = 0; i < ScriptHost.MaxPriceChangesPerMinute; i++) Touch(host4, obj4); Assert.Empty(reports4); Touch(host4, obj4); Assert.Contains("at most", Assert.Single(reports4).Message);
    }

    [Fact]
    public void ThePurchaseOfTheObjectReachesItsScripts()
    {
        var (host, world, obj, reports) = Rig("function on_purchase(e) chat.say(e.avatar.name .. ' pagou ' .. e.price .. ' ' .. e.sale_type) end");
        host.Enqueue(new ScriptEvent(obj, "purchase", new Dictionary<string, object?> { ["price"] = 250L, ["sale_type"] = "copy", ["avatar"] = new Dictionary<string, object?> { ["id"] = Ana, ["name"] = "Ana" } })); host.Process(500);
        Assert.Empty(reports); Assert.Equal(["Ana pagou 250 copy"], world.Said);
    }
    [Fact]
    public void TheScriptHearsHowTheDeliveryEnded()
    {
        var (host, world, obj, reports) = Rig("function on_item_given(e) chat.say('deu ' .. e.item .. ' ' .. e.avatar.name) end function on_item_give_failed(e) chat.say('falhou ' .. e.reason .. ' ' .. e.item) end");
        var script = host.Scripts.Single().ScriptId;
        host.CompleteGiveItem(script, Ana, "Brinde", null); host.CompleteGiveItem(script, Ana, "Raro", "not_copyable"); host.CompleteGiveItem(Guid.NewGuid(), Ana, "x", null); host.Process(500);
        Assert.Empty(reports); Assert.Equal(["deu Brinde Ana", "falhou not_copyable Raro"], world.Said);
    }
}
