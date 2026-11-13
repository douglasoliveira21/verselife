using LifeVerse.Scripting;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>inventory.giveItem: only to an avatar who allowed GIVE_ITEMS, only items the object carries (not scripts or notecards), limited per minute, and the script hears how the delivery ended.</summary>
public sealed class LuaGiveItemTests
{
    private static readonly Guid Owner = Guid.NewGuid(), Ana = Guid.NewGuid(), Gift = Guid.NewGuid();

    private sealed class World : IScriptWorld
    {
        public double Now { get; set; } = 100; public Dictionary<Guid, ScriptObjectState> Objects { get; } = []; public List<string> Said { get; } = []; public List<(Guid Avatar, Guid Asset, string Name)> Started { get; } = []; public bool CanStart { get; set; } = true;
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
