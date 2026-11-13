using System.Text.Json;
using LifeVerse.Scripting;
using LifeVerse.Shared.Land;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>What scripts may know about the land (public data only) and the avatars walking from one parcel to another.</summary>
public sealed class LuaLandTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly ParcelData Garden = new(Guid.NewGuid(), "Jardim", Owner, "public", "owner", false), Shop = new(Guid.NewGuid(), "Loja", Guid.NewGuid(), "list", "everyone", true);

    private sealed class World : IScriptWorld
    {
        public double Now { get; set; } = 100; public Dictionary<Guid, ScriptObjectState> Objects { get; } = []; public List<string> Said { get; } = [];
        public ScriptObjectState? GetObject(Guid id) => Objects.GetValueOrDefault(id);
        public void ApplyChange(ScriptDefinition script, ObjectChange change) { }
        public void Say(ScriptDefinition script, string text) => Said.Add(text);
        public ParcelData? GetParcelAt(double x, double z) => x < 0 ? null : x < 50 ? Garden : Shop;     // west of 0 is nobody's land
    }

    private static (ScriptHost Host, World World, Guid Object, List<ScriptReport> Reports) Rig(string source, double x)
    {
        var world = new World(); var obj = Guid.NewGuid(); world.Objects[obj] = new ScriptObjectState(obj, Owner, "Placa", new Vec3((float)x, 0, 0), default, new Vec3(1, 1, 1));
        var host = new ScriptHost(world, new ScriptHostOptions(MillisecondsPerEvent: 2000)); var reports = new List<ScriptReport>(); host.Report += reports.Add;
        host.Sync([new ScriptDefinition(Guid.NewGuid(), obj, Owner, "placa", source, 1)]); host.Process(500); return (host, world, obj, reports);
    }

    [Fact]
    public void Parcel_AnswersOnlyWithPublicData()
    {
        var (host, world, obj, reports) = Rig("function on_touch() local p = parcel.getPermissions() chat.say(parcel.getName() .. '|' .. parcel.getOwner() .. '|' .. parcel.getId() .. '|' .. object.getParcel() .. '|' .. p.access .. p.build .. tostring(p.forSale)) local q = parcel.getAt(60, 5) chat.say(q.name .. q.access .. tostring(q.forSale) .. tostring(parcel.getAt(-10, 0))) end", 10);
        host.Enqueue(new ScriptEvent(obj, "touch")); host.Process(500); Assert.Empty(reports);
        Assert.Equal([$"Jardim|{Owner}|{Garden.Id}|{Garden.Id}|publicownerfalse", "Lojalisttruenil"], world.Said);
        var (host2, world2, obj2, _) = Rig("function on_touch() chat.say(tostring(parcel.getName()) .. tostring(parcel.getId()) .. tostring(parcel.getPermissions()) .. tostring(object.getParcel())) end", -20);
        host2.Enqueue(new ScriptEvent(obj2, "touch")); host2.Process(500); Assert.Equal("nilnilnilnil", world2.Said.Single());                                                       // on nobody's land there is nothing to say
    }

    [Fact]
    public void ParcelEvents_ReachOnlyObjectsStandingOnThatParcel()
    {
        const string Source = "function on_parcel_enter(e) chat.say('entrou ' .. e.avatar.name .. ' em ' .. e.parcel.name .. ' ' .. e.parcel.access) end function on_parcel_leave(e) chat.say('saiu ' .. e.avatar.name .. ' de ' .. e.parcel.name) end";
        var (host, world, obj, _) = Rig(Source, 10);                                                       // a sign in the garden
        var shop = Guid.NewGuid(); world.Objects[shop] = new ScriptObjectState(shop, Owner, "Placa 2", new Vec3(70, 0, 0), default, new Vec3(1, 1, 1)); host.Sync([.. host.Scripts.Select(s => new ScriptDefinition(s.ScriptId, s.ObjectId, Owner, s.Name, Source, 1)), new ScriptDefinition(Guid.NewGuid(), shop, Owner, "placa2", Source, 1)]); host.Process(500);
        var ana = Guid.NewGuid();
        host.AvatarMovedParcels(ana, "Ana", null, Garden); host.Process(500); Assert.Equal(["entrou Ana em Jardim public"], world.Said);                                    // arriving in the garden: only the garden's sign hears
        host.AvatarMovedParcels(ana, "Ana", Garden, Garden); host.Process(500); Assert.Single(world.Said);                                                                  // staying put is no event
        host.AvatarMovedParcels(ana, "Ana", Garden, Shop); host.Process(500); Assert.Equal(["entrou Ana em Jardim public", "saiu Ana de Jardim", "entrou Ana em Loja list"], world.Said.ToArray());   // each sign hears about its own parcel
        host.AvatarMovedParcels(ana, "Ana", Shop, null); host.Process(500); Assert.Equal("saiu Ana de Loja", world.Said.Last());                                           // walking off the land is leaving
        host.AvatarMovedParcels(ana, "Ana", null, null); host.Process(500); Assert.Equal(4, world.Said.Count);
    }

    [Fact]
    public void LandInfo_ReadsWhatTheApiSends_AndSkipsDamagedEntries()
    {
        var id = Guid.NewGuid(); var owner = Guid.NewGuid();
        var info = LandInfo.Parse(JsonDocument.Parse($"{{\"parcels\":[{{\"id\":\"{id}\",\"name\":\"Praça\",\"owner\":\"{owner}\",\"group\":null,\"access\":\"list\",\"build\":\"everyone\",\"forSale\":true,\"runs\":[[0,0,2]]}},{{\"broken\":true}},{{\"id\":\"not-a-guid\",\"owner\":\"{owner}\",\"runs\":[[0,5,6]]}}]}}").RootElement);
        Assert.Equal(1, info.Parcels); var at = info.At(5, 1)!; Assert.Equal((id, "Praça", owner, "list", "everyone", true), (at.Id, at.Name, at.Owner, at.Access, at.Build, at.ForSale));
        Assert.Null(info.At(100, 100)); Assert.Equal(id, info.At(10, 2)!.Id); Assert.Null(info.At(30, 1));                                                                  // cells 0..2 of row 0 (4 m each)
        Assert.Equal(0, LandInfo.Parse(default).Parcels); Assert.Null(LandInfo.Empty.At(0, 0));
    }
}
