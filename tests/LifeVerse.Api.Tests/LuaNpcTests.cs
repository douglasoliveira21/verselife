using LifeVerse.Scripting;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>npc.*: only with CREATE_NPCS, only the script's own NPCs, limits, and the NPCs go when the script ends, stops or restarts.</summary>
public sealed class LuaNpcTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private sealed class World : IScriptWorld
    {
        public double Now { get; set; } = 100; public Dictionary<Guid, ScriptObjectState> Objects { get; } = []; public List<string> Said { get; } = [];
        public List<string> Calls { get; } = []; public int Next = -1; public Dictionary<int, Vec3> Npcs { get; } = []; public int Room = int.MaxValue;
        public ScriptObjectState? GetObject(Guid id) => Objects.GetValueOrDefault(id);
        public void ApplyChange(ScriptDefinition script, ObjectChange change) { }
        public void Say(ScriptDefinition script, string text) => Said.Add(text);
        public int CreateNpc(ScriptDefinition script, string name, float x, float z) { if (Npcs.Count >= Room) return 0; var id = Next--; Npcs[id] = new Vec3(x, 1, z); Calls.Add($"create {name} {x} {z}"); return id; }
        public bool MoveNpc(ScriptDefinition script, int npc, float x, float z, float speed) { Calls.Add($"move {npc} {x} {z} {speed}"); return Npcs.ContainsKey(npc); }
        public bool StopNpc(ScriptDefinition script, int npc) { Calls.Add($"stop {npc}"); return Npcs.ContainsKey(npc); }
        public bool NpcSay(ScriptDefinition script, int npc, string text) { Calls.Add($"say {npc} {text}"); return true; }
        public bool NpcAnimate(ScriptDefinition script, int npc, string gesture, double seconds) { Calls.Add($"animate {npc} {gesture} {seconds}"); return true; }
        public Vec3? GetNpcPosition(int npc) => Npcs.TryGetValue(npc, out var at) ? at : null;
        public bool RemoveNpc(ScriptDefinition script, int npc) { Calls.Add($"remove {npc}"); return Npcs.Remove(npc); }
        public void RemoveNpcsOf(ScriptDefinition script) { Calls.Add("remove-all"); Npcs.Clear(); }
    }

    private static (ScriptHost Host, World World, Guid Object, Guid Script, List<ScriptReport> Reports) Rig(string source, bool allowed = true)
    {
        var world = new World(); var obj = Guid.NewGuid(); world.Objects[obj] = new ScriptObjectState(obj, Owner, "Posto", new Vec3(0, 0, 0), default, new Vec3(1, 1, 1));
        var host = new ScriptHost(world, new ScriptHostOptions(MillisecondsPerEvent: 2000)); var reports = new List<ScriptReport>(); host.Report += reports.Add;
        var script = Guid.NewGuid(); host.Sync([new ScriptDefinition(script, obj, Owner, "posto", source, 1)]); if (allowed) host.Permissions.Grant(script, Guid.Empty, ScriptPermission.CreateNpcs); host.Process(500); return (host, world, obj, script, reports);
    }

    private static void Touch(ScriptHost host, Guid obj) { host.Enqueue(new ScriptEvent(obj, "touch")); host.Process(500); }

    [Fact]
    public void NothingWorksWithoutTheOwnersPermission()
    {
        var (host, world, obj, _, reports) = Rig("function on_touch() npc.create('Guarda', 1, 2) end", allowed: false);
        Touch(host, obj); Assert.Contains("CREATE_NPCS", Assert.Single(reports).Message); Assert.Empty(world.Calls);
    }

    [Fact]
    public void AScriptMakesWalksAndCommandsItsNpcs()
    {
        var (host, world, obj, _, reports) = Rig("function on_touch() local g = npc.create('Guarda', 10, 20) npc.moveTo(g, 30, 20, 2) npc.say(g, 'Alto lá!') npc.playAnimation(g, 'wave', 3) local p = npc.getPosition(g) chat.say(g .. ' ' .. p.x .. ' ' .. #npc.list()) npc.stop(g) npc.remove(g) end");
        Touch(host, obj); Assert.Empty(reports);
        Assert.Equal(["create Guarda 10 20", "move -1 30 20 2", "say -1 Alto lá!", "animate -1 wave 3", "stop -1", "remove -1"], world.Calls); Assert.Equal(["-1 10 1"], world.Said);
    }

    [Fact]
    public void ANpcOfAnotherScriptOrAMadeUpNumberIsRefused()
    {
        var (host, world, obj, _, reports) = Rig("function on_touch() npc.moveTo(-77, 1, 1) end"); world.Npcs[-77] = new Vec3(0, 0, 0);
        Touch(host, obj); Assert.Contains("not an NPC of this script", Assert.Single(reports).Message); Assert.Empty(world.Calls);
    }

    [Fact]
    public void WhenThereIsNoRoomTheAnswerIsNil_AndBadInputIsAnError()
    {
        var (host, world, obj, _, reports) = Rig("function on_touch() chat.say(tostring(npc.create('x', 1, 1))) end"); world.Room = 0; Touch(host, obj); Assert.Equal(["nil"], world.Said); Assert.Empty(reports);
        foreach (var bad in new[] { "npc.create('', 1, 1)", "npc.create('x', 99999, 1)", "npc.create('x', 0/0, 1)" })
        {
            var (host2, world2, obj2, _, reports2) = Rig($"function on_touch() {bad} end"); Touch(host2, obj2); Assert.Single(reports2); Assert.Empty(world2.Calls);
        }
        var (host3, world3, obj3, _, _) = Rig("function on_touch() npc.create(string.rep('n', 100), 1, 1) end"); Touch(host3, obj3); Assert.Equal(["create " + new string('n', ScriptHost.MaxNpcNameLength) + " 1 1"], world3.Calls);
    }

    [Fact]
    public void ScriptsCannotFloodCreationOrSpeech()
    {
        var (host, world, obj, _, reports) = Rig("function on_touch() npc.create('n', 1, 1) end");
        for (var i = 0; i < ScriptHost.MaxNpcCreatesPerMinute; i++) Touch(host, obj); Assert.Empty(reports); Touch(host, obj); Assert.Contains("at most", Assert.Single(reports).Message);
        var (host2, world2, obj2, _, reports2) = Rig("function on_touch() local g = npc.create('n', 1, 1) for i = 1, 6 do npc.say(g, 'oi') end end"); Touch(host2, obj2);
        Assert.Contains("at most", Assert.Single(reports2).Message); Assert.Equal(ScriptHost.MaxNpcSaysPer10Seconds, world2.Calls.Count(c => c.StartsWith("say", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheNpcsGoWhenTheScriptStopsOrRestarts_AndTheScriptHearsWhenOneArrives()
    {
        var (host, world, obj, script, reports) = Rig("function on_touch() npc.create('Guarda', 1, 1) end function on_npc_arrived(e) chat.say('chegou ' .. e.npc .. ' ' .. e.position.x) end");
        Touch(host, obj); Assert.Single(world.Npcs);
        host.NpcArrived(script, -1, new Vec3(5, 1, 6)); host.NpcArrived(script, -9, new Vec3(0, 0, 0)); host.Process(500); Assert.Equal(["chegou -1 5"], world.Said);          // only its own NPCs
        host.Stop(script); Assert.Empty(world.Npcs); Assert.Contains("remove-all", world.Calls);
        host.Restart(script); host.Process(500); Touch(host, obj); Assert.Single(world.Npcs); world.Calls.Clear(); host.Restart(script); Assert.Empty(world.Npcs); Assert.Contains("remove-all", world.Calls);
        Assert.Empty(reports);
    }
}
