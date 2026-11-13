using LifeVerse.Shared.Networking;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The non-player characters of an island: limits, walking in a straight line, stopping at the sea and the edge, gestures, and who may command which.</summary>
public sealed class NpcWorldTests
{
    private static readonly Guid Script = Guid.NewGuid(), Other = Guid.NewGuid(), Owner = Guid.NewGuid();

    // flat land at height 1, except the sea west of x = -50 (height -5, sea level 0), on an island 100 m wide
    private static NpcWorld World() => new((x, _) => x < -50 ? -5f : 1f, () => 0f, () => 100f);

    [Fact]
    public void ANpcIsMadeStandingOnTheGround_WithNegativePeerNumbers_AndTheWorldIsTold()
    {
        var world = World(); var spawned = new List<NpcState>(); world.Spawned = spawned.Add;
        var a = world.Create(Script, Owner, "Guarda", 10, 20); var b = world.Create(Script, Owner, "Vigia", 12, 20);
        Assert.True(a < 0 && b < 0 && a != b); Assert.Equal([a, b], spawned.Select(n => n.PeerId));
        Assert.Equal((10f, 1f, 20f), (world.Get(a)!.X, world.Get(a)!.Y, world.Get(a)!.Z)); Assert.Equal(NetworkMotionState.Idle, world.Get(a)!.Motion);
    }

    [Fact]
    public void LimitsAndBadSpotsAreRefused()
    {
        var world = World();
        for (var i = 0; i < NpcWorld.MaxPerScript; i++) Assert.True(world.Create(Script, Owner, "n", i, 0) < 0);
        Assert.Equal(0, world.Create(Script, Owner, "sexto", 9, 0));                                                    // five a script
        for (var s = 0; world.All.Count < NpcWorld.MaxPerIsland; s++) Assert.True(world.Create(Guid.NewGuid(), Owner, "n", s, 5) < 0);
        Assert.Equal(0, world.Create(Guid.NewGuid(), Owner, "demais", 1, 9));                                           // twenty an island
        var fresh = World();
        Assert.Equal(0, fresh.Create(Script, Owner, "no mar", -80, 0)); Assert.Equal(0, fresh.Create(Script, Owner, "fora", 500, 0)); Assert.Equal(0, fresh.Create(Script, Owner, "nan", float.NaN, 0));
    }

    [Fact]
    public void ANpcWalksStraightToWhereItIsSent_FacesIt_AndSaysWhenItArrives()
    {
        var world = World(); var arrived = new List<int>(); world.Arrived = n => arrived.Add(n.PeerId);
        var id = world.Create(Script, Owner, "Guarda", 0, 0); Assert.True(world.MoveTo(Script, id, 10, 0, 2));
        world.Tick(1, 1); var npc = world.Get(id)!; Assert.Equal((2f, 0f, NetworkMotionState.Walk), (npc.X, npc.Z, npc.Motion)); Assert.InRange(npc.Yaw, MathF.PI / 2 - .01f, MathF.PI / 2 + .01f);   // east
        for (var t = 2; t < 10; t++) world.Tick(t, 1);
        Assert.Equal((10f, 0f, NetworkMotionState.Idle), (npc.X, npc.Z, npc.Motion)); Assert.Equal([id], arrived);
        world.Tick(11, 1); Assert.Equal([id], arrived);                                                                  // arriving is told once
    }

    [Fact]
    public void FastNpcsRun_SpeedIsClamped_AndOnlyTheScriptThatMadeItCommandsIt()
    {
        var world = World(); var id = world.Create(Script, Owner, "Corredor", 0, 0);
        Assert.True(world.MoveTo(Script, id, 50, 0, 100)); Assert.Equal(NpcWorld.MaxSpeed, world.Get(id)!.Speed); world.Tick(1, 1); Assert.Equal((NpcWorld.MaxSpeed, NetworkMotionState.Run), (world.Get(id)!.X, world.Get(id)!.Motion));
        world.MoveTo(Script, id, 50, 0, 0); Assert.Equal(NpcWorld.MinSpeed, world.Get(id)!.Speed);
        Assert.False(world.MoveTo(Other, id, 5, 5, 2)); Assert.False(world.Stop(Other, id)); Assert.False(world.Remove(Other, id)); Assert.False(world.Animate(Other, id, NetworkMotionState.Wave, 3, 0)); Assert.NotNull(world.Get(id));
        Assert.False(world.MoveTo(Script, id, -80, 0, 2)); Assert.False(world.MoveTo(Script, id, 500, 0, 2)); Assert.False(world.MoveTo(Script, 12345, 1, 1, 2));
    }

    [Fact]
    public void ANpcStopsAtTheSea_AndStopCancelsTheWalk()
    {
        var world = World(); var arrived = new List<NpcState>(); world.Arrived = arrived.Add;
        var id = world.Create(Script, Owner, "Pescador", -48, 0); Assert.True(world.MoveTo(Script, id, -40, 0, 2));
        Assert.True(world.Stop(Script, id)); world.Tick(1, 1); Assert.Equal(-48f, world.Get(id)!.X);
        // sent along a path the shore cuts: the target itself is dry, the walk is straight over water
        var other = World(); var swimmer = other.Create(Script, Owner, "Nadador", -49, 0); other.MoveTo(Script, swimmer, -49, 90, 3); other.Tick(1, 1); Assert.True(other.Get(swimmer)!.Z > 0);
    }

    [Fact]
    public void GesturesInterruptWalkingAndEnd()
    {
        var world = World(); var id = world.Create(Script, Owner, "Dançarina", 0, 0); world.MoveTo(Script, id, 30, 0, 2);
        Assert.True(world.Animate(Script, id, NetworkMotionState.Dance, 5, now: 10)); world.Tick(12, 1); Assert.Equal((0f, NetworkMotionState.Dance), (world.Get(id)!.X, world.Get(id)!.Motion));
        world.Tick(16, 1); Assert.Equal(NetworkMotionState.Idle, world.Get(id)!.Motion);
        Assert.False(world.Animate(Script, id, NetworkMotionState.Run, 5, 20));                                           // only gestures
    }

    [Fact]
    public void RemovingTellsTheWorld_AndTheScriptsNpcsGoTogether()
    {
        var world = World(); var gone = new List<int>(); world.Despawned = gone.Add;
        var a = world.Create(Script, Owner, "a", 0, 0); var b = world.Create(Script, Owner, "b", 1, 0); var c = world.Create(Other, Owner, "c", 2, 0);
        Assert.True(world.Remove(Script, a)); Assert.Equal(1, world.RemoveScript(Script)); Assert.Equal([a, b], gone); Assert.Equal([c], world.All.Select(n => n.PeerId));
        world.RemoveAll(); Assert.Empty(world.All); Assert.Equal([a, b, c], gone);
    }

    [Fact]
    public void OnlyNearbyNpcsAreInTheSnapshot()
    {
        var world = World(); var near = world.Create(Script, Owner, "perto", 5, 0); world.Create(Script, Owner, "longe", 90, 0);
        var snapshots = world.Snapshots(0, 1, 0, 30).ToList(); Assert.Equal([near], snapshots.Select(s => s.PeerId));
    }
}
