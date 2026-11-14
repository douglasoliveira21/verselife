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

/// <summary>NPCs go around what is in the way, look for another way when something appears, and refuse a place they cannot reach.</summary>
public sealed class NpcWorldPathTests
{
    private static readonly Guid Script = Guid.NewGuid(), Owner = Guid.NewGuid();
    private static float Flat(float x, float z) => 1f;

    // a wall along x = 10 from z = -10 to z = 10 (blocks points within 0.7 of it), on land everywhere
    private static bool Wall(float x, float z) => MathF.Abs(x - 10) < .7f && z is >= -10 and <= 10;

    private static NpcWorld World(Func<float, float, bool>? blocked) => new(Flat, () => -100f, () => 200f, blocked);

    private static (NpcWorld World, int Id, List<NpcState> Ended) Rig(Func<float, float, bool>? blocked, float x = 0, float z = 0)
    {
        var world = World(blocked); var ended = new List<NpcState>(); world.Arrived = ended.Add; var id = world.Create(Script, Owner, "Guarda", x, z); return (world, id, ended);
    }

    private static void Walk(NpcWorld world, double seconds, float dt = .1f) { for (var t = 0.0; t < seconds; t += dt) world.Tick(t, dt); }

    [Fact]
    public void WithNothingInTheWayThePathIsStraight()
    {
        var (world, id, ended) = Rig(Wall); Assert.Equal([(0f, 30f)], world.FindPath(0, 0, 0, 30)!.Select(p => (p.X, p.Z)));
        Assert.True(world.MoveTo(Script, id, 0, 30, 3)); Walk(world, 20); Assert.True(ended.Single().Reached); Assert.Equal((0f, 30f), (world.Get(id)!.X, world.Get(id)!.Z));
    }

    [Fact]
    public void AWallIsGoneAround_AndTheNpcArrives()
    {
        var (world, id, ended) = Rig(Wall, 0, 0); var path = world.FindPath(0, 0, 20, 0)!;
        Assert.True(path.Count >= 2 && path.Any(p => MathF.Abs(p.Z) > 10), "the way turns at the end of the wall");                      // it has to pass beyond z = ±10
        Assert.True(world.MoveTo(Script, id, 20, 0, 5)); var maxCrossing = 0f;
        for (var t = 0.0; t < 60 && ended.Count == 0; t += .1) { world.Tick(t, .1f); var n = world.Get(id)!; Assert.False(Wall(n.X, n.Z), $"walked into the wall at {n.X},{n.Z}"); maxCrossing = MathF.Max(maxCrossing, MathF.Abs(n.Z)); }
        Assert.True(ended.Single().Reached); Assert.Equal((20f, 0f), (world.Get(id)!.X, world.Get(id)!.Z)); Assert.True(maxCrossing > 10);
    }

    [Fact]
    public void ATargetThatCannotBeReachedRefusesTheOrder()
    {
        // a closed box around the target: a ring of walls from 18 to 22 in both axes
        static bool Box(float x, float z) { var ax = MathF.Abs(x - 30); var az = MathF.Abs(z); var inside = ax < 6 && az < 6; return inside && !(ax < 3 && az < 3); }
        var (world, id, _) = Rig(Box); Assert.False(world.MoveTo(Script, id, 30, 0, 3)); Assert.Null(world.Get(id)!.TargetX);                // walled in
        Assert.True(world.MoveTo(Script, id, 20, 0, 3));
        Assert.False(World(Wall).MoveTo(Script, 12345, 5, 5, 3));
        var inside = Rig(Wall); Assert.False(inside.World.MoveTo(Script, inside.Id, 10, 0, 3));                                              // the target is inside the wall
    }

    [Fact]
    public void TooFarToSearchIsRefusedWhenTheWayIsNotStraight()
    {
        var (world, id, _) = Rig((x, z) => MathF.Abs(x - 100) < .7f && z is >= -400 and <= 400, 0, 0); Assert.False(world.MoveTo(Script, id, 190, 0, 3));
        var (free, freeId, _) = Rig(null, 0, 0); Assert.True(free.MoveTo(Script, freeId, 190, 0, 3));                                          // a clear line needs no search, however long
    }

    [Fact]
    public void ASeaIsGoneAroundToo()
    {
        var lake = new NpcWorld((x, z) => (x - 10) * (x - 10) + z * z < 36 ? -5f : 1f, () => 0f, () => 200f); var id = lake.Create(Script, Owner, "Nadador", -2, 0); var ended = new List<NpcState>(); lake.Arrived = ended.Add;
        Assert.True(lake.MoveTo(Script, id, 22, 0, 4)); Walk(lake, 30); Assert.True(ended.Single().Reached); Assert.Equal(22f, lake.Get(id)!.X);
    }

    [Fact]
    public void SomethingThatAppearsInTheWayMakesItLookForAnotherWay_AFewTimesOnly()
    {
        var wall = false; var (world, id, ended) = Rig((x, z) => wall && Wall(x, z)); Assert.True(world.MoveTo(Script, id, 20, 0, 4)); Walk(world, 1);
        wall = true; for (var t = 1.0; t < 60 && ended.Count == 0; t += .1) { world.Tick(t, .1f); Assert.False(Wall(world.Get(id)!.X, world.Get(id)!.Z)); }
        Assert.True(ended.Single().Reached);                                                                                                  // it went around the wall that was not there when it set out
        var trapped = false; var (world2, id2, ended2) = Rig((x, z) => trapped && MathF.Abs(x - 3) < 50 && MathF.Abs(x - 3) > .5); world2.MoveTo(Script, id2, 20, 0, 4); trapped = true; Walk(world2, 5);
        Assert.False(ended2.Single().Reached); Assert.Null(world2.Get(id2)!.TargetX);                                                          // it gave up and says so
    }
}

public sealed class ObstacleFieldWalkingTests
{
    [Fact]
    public void WallsBlockAvatarsButLowLedgesAndFarAwayDoNot()
    {
        var field = new ObstacleField(); var wall = ObstacleField.For("cube", new System.Numerics.Vector3(10, 1.5f, 0), System.Numerics.Vector3.Zero, new System.Numerics.Vector3(1, 3, 20));   // 1 m thick, 3 m high, 20 m long, feet of the avatar at 0
        field.Set(Guid.NewGuid(), wall); var ledge = ObstacleField.For("cube", new System.Numerics.Vector3(30, .15f, 0), System.Numerics.Vector3.Zero, new System.Numerics.Vector3(4, .3f, 4)); field.Set(Guid.NewGuid(), ledge);
        Assert.True(field.Blocked(10, 0, 0)); Assert.True(field.Blocked(9.2f, 0, 5));                                                  // in and against the wall
        Assert.False(field.Blocked(5, 0, 0)); Assert.False(field.Blocked(10, 0, 40));                                                 // away from it, and beyond its end
        Assert.False(field.Blocked(30, 0, 0));                                                                                          // a ledge lower than a step is walked over
        Assert.False(new ObstacleField().Blocked(1, 0, 1));
    }

    [Fact]
    public void ANpcWorldOnARealFieldWalksAroundAWall()
    {
        var field = new ObstacleField(); field.Set(Guid.NewGuid(), ObstacleField.For("cube", new System.Numerics.Vector3(10, 1.5f, 0), System.Numerics.Vector3.Zero, new System.Numerics.Vector3(1, 3, 20)));
        var world = new NpcWorld((_, _) => 0f, () => -50f, () => 200f, (x, z) => field.Blocked(x, 0, z)); var script = Guid.NewGuid(); var id = world.Create(script, Guid.NewGuid(), "G", 0, 0); var ended = new List<NpcState>(); world.Arrived = ended.Add;
        Assert.True(world.MoveTo(script, id, 20, 0, 5)); for (var t = 0.0; t < 40 && ended.Count == 0; t += .1) world.Tick(t, .1f);
        Assert.True(ended.Single().Reached); Assert.Equal(20f, world.Get(id)!.X);
    }
}

public sealed class BackwardsWalkTests
{
    [Theory]
    [InlineData(LifeVerse.Shared.Gameplay.AvatarMotionState.Walk, true, LifeVerse.Shared.Gameplay.AvatarMotionState.WalkBack)]
    [InlineData(LifeVerse.Shared.Gameplay.AvatarMotionState.Run, true, LifeVerse.Shared.Gameplay.AvatarMotionState.WalkBack)]
    [InlineData(LifeVerse.Shared.Gameplay.AvatarMotionState.Walk, false, LifeVerse.Shared.Gameplay.AvatarMotionState.Walk)]
    [InlineData(LifeVerse.Shared.Gameplay.AvatarMotionState.Idle, true, LifeVerse.Shared.Gameplay.AvatarMotionState.Idle)]
    [InlineData(LifeVerse.Shared.Gameplay.AvatarMotionState.Jump, true, LifeVerse.Shared.Gameplay.AvatarMotionState.Jump)]
    [InlineData(LifeVerse.Shared.Gameplay.AvatarMotionState.Fall, true, LifeVerse.Shared.Gameplay.AvatarMotionState.Fall)]
    [InlineData(LifeVerse.Shared.Gameplay.AvatarMotionState.FlyMove, true, LifeVerse.Shared.Gameplay.AvatarMotionState.FlyMove)]
    [InlineData(LifeVerse.Shared.Gameplay.AvatarMotionState.Sit, true, LifeVerse.Shared.Gameplay.AvatarMotionState.Sit)]
    public void OnlyWalkingAndRunningWhilePullingBackBecomeWalkingBackwards(LifeVerse.Shared.Gameplay.AvatarMotionState state, bool back, LifeVerse.Shared.Gameplay.AvatarMotionState expected) => Assert.Equal(expected, LifeVerse.Shared.Gameplay.BackwardsWalk.Apply(state, back));

    [Fact]
    public void WalkingBackwardsIsNotAStateTheEditorOrTheNetworkKnows()
    {
        Assert.False(LifeVerse.Shared.Avatar.AnimationStates.All.ContainsKey("WalkBack")); Assert.False(LifeVerse.Shared.Avatar.AnimationStates.All.ContainsKey("FlatImpact"));                                                // no override for it, and the server accepts none
        Assert.Equal(17, (int)LifeVerse.Shared.Gameplay.AvatarMotionState.SitFloor); Assert.Equal(18, (int)LifeVerse.Shared.Gameplay.AvatarMotionState.WalkBack); Assert.Equal(19, (int)LifeVerse.Shared.Gameplay.AvatarMotionState.FlatImpact); Assert.Equal(20, (int)LifeVerse.Shared.Gameplay.AvatarMotionState.SwimIdle);   // added at the end: no number moved
    }
}

public sealed class WaterStillTests
{
    [Theory]
    [InlineData(LifeVerse.Shared.Gameplay.AvatarMotionState.Swim, false, LifeVerse.Shared.Gameplay.AvatarMotionState.SwimIdle)]
    [InlineData(LifeVerse.Shared.Gameplay.AvatarMotionState.Swim, true, LifeVerse.Shared.Gameplay.AvatarMotionState.Swim)]
    [InlineData(LifeVerse.Shared.Gameplay.AvatarMotionState.Idle, false, LifeVerse.Shared.Gameplay.AvatarMotionState.Idle)]
    [InlineData(LifeVerse.Shared.Gameplay.AvatarMotionState.FlyIdle, false, LifeVerse.Shared.Gameplay.AvatarMotionState.FlyIdle)]
    [InlineData(LifeVerse.Shared.Gameplay.AvatarMotionState.Walk, false, LifeVerse.Shared.Gameplay.AvatarMotionState.Walk)]
    public void OnlySwimmingWithoutMovingIsTreadingWater(LifeVerse.Shared.Gameplay.AvatarMotionState state, bool moving, LifeVerse.Shared.Gameplay.AvatarMotionState expected) => Assert.Equal(expected, LifeVerse.Shared.Gameplay.WaterStill.Apply(state, moving));
}
