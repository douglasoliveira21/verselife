using LifeVerse.Scripting;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>animation.*: the mesh animation of the object itself, written as the material's "animation" the rest of the game already understands (clip, playing, loop, speed, offset), with the point reached kept by the script.</summary>
public sealed class LuaObjectAnimationTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private sealed class World : IScriptWorld
    {
        public double Now { get; set; } = 100; public Dictionary<Guid, ScriptObjectState> Objects { get; } = []; public List<string> Said { get; } = []; public List<Dictionary<string, object?>> Written { get; } = [];
        public ScriptObjectState? GetObject(Guid id) => Objects.GetValueOrDefault(id);
        public void ApplyChange(ScriptDefinition script, ObjectChange change) { if (change.Material.TryGetValue("animation", out var a) && a is Dictionary<string, object?> d) Written.Add(d); }
        public void Say(ScriptDefinition script, string text) => Said.Add(text);
    }

    private static (ScriptHost Host, World World, Guid Object, List<ScriptReport> Reports) Rig(string source)
    {
        var world = new World(); var obj = Guid.NewGuid(); world.Objects[obj] = new ScriptObjectState(obj, Owner, "Porta", new Vec3(0, 0, 0), default, new Vec3(1, 1, 1));
        var host = new ScriptHost(world, new ScriptHostOptions(MillisecondsPerEvent: 2000)); var reports = new List<ScriptReport>(); host.Report += reports.Add;
        host.Sync([new ScriptDefinition(Guid.NewGuid(), obj, Owner, "porta", source, 1)]); host.Process(500); return (host, world, obj, reports);
    }

    private static void Touch(ScriptHost host, Guid obj) { host.Enqueue(new ScriptEvent(obj, "touch")); host.Process(500); }

    [Fact]
    public void PlayWritesTheAnimationTheGameAlreadyUnderstands_WithLimits()
    {
        var (host, world, obj, reports) = Rig("function on_touch() animation.play('Abrir', {loop = true, speed = 99, offset = -5}) end");
        Touch(host, obj); Assert.Empty(reports); var a = Assert.Single(world.Written);
        Assert.Equal(("Abrir", true, true, ScriptHost.MaxAnimationSpeed, 0d), (a["clip"], a["playing"], a["loop"], a["speed"], a["offset"]));
        var (host2, world2, obj2, _) = Rig("function on_touch() animation.play('Fechar') end"); Touch(host2, obj2);
        var b = Assert.Single(world2.Written); Assert.Equal((false, 1d), (b["loop"], b["speed"]));
    }

    [Theory]
    [InlineData("animation.play('')")]
    [InlineData("animation.play()")]
    [InlineData("animation.play(42)")]
    [InlineData("animation.play(string.rep('x', 65))")]
    public void ABadClipNameIsAnError(string call)
    {
        var (host, world, obj, reports) = Rig($"function on_touch() {call} end"); Touch(host, obj); Assert.Single(reports); Assert.Empty(world.Written);
    }

    [Fact]
    public void PauseKeepsThePointReached_ResumeContinuesFromIt_StopGoesBackToTheStart()
    {
        var (host, world, obj, reports) = Rig("local step = 0 function on_touch() step = step + 1 if step == 1 then animation.play('Girar', {speed = 2}) elseif step == 2 then chat.say(tostring(animation.pause())) elseif step == 3 then chat.say(tostring(animation.resume())) elseif step == 4 then chat.say(tostring(animation.stop())) end end");
        Touch(host, obj); world.Now += 3; Touch(host, obj);                                                                                   // 3 s at speed 2
        Assert.Equal((false, 6d), (world.Written[1]["playing"], world.Written[1]["offset"]));
        world.Now += 50; Touch(host, obj); Assert.Equal((true, 6d), (world.Written[2]["playing"], world.Written[2]["offset"]));                // paused time does not count
        world.Now += 1; Touch(host, obj); Assert.Equal((false, 0d), (world.Written[3]["playing"], world.Written[3]["offset"]));
        Assert.Empty(reports); Assert.Equal(["true", "true", "true"], world.Said);
    }

    [Fact]
    public void SetSpeedKeepsThePointAndGetStateTellsWhatTheScriptLeft()
    {
        var (host, world, obj, _) = Rig("local step = 0 function on_touch() step = step + 1 if step == 1 then animation.play('Girar') elseif step == 2 then animation.setSpeed(3) local s = animation.getState() chat.say(s.clip .. ' ' .. tostring(s.playing) .. ' ' .. s.speed .. ' ' .. s.position) end end");
        Touch(host, obj); world.Now += 2; Touch(host, obj);
        Assert.Equal((2d, 3d), (world.Written[1]["offset"], world.Written[1]["speed"])); Assert.Equal(["Girar true 3 2"], world.Said);
    }

    [Fact]
    public void WithoutAnAnimationTheOtherCallsAnswerFalseOrNothing()
    {
        var (host, world, obj, reports) = Rig("function on_touch() chat.say(tostring(animation.pause()) .. tostring(animation.resume()) .. tostring(animation.stop()) .. tostring(animation.setSpeed(2)) .. tostring(animation.getState())) end");
        Touch(host, obj); Assert.Empty(reports); Assert.Equal(["falsefalsefalsefalsenil"], world.Said); Assert.Empty(world.Written);
    }
}

public sealed class LuaObjectAnimationApiTests
{
    [Fact]
    public void WhatTheScriptWritesIsAcceptedByTheMaterialRules_AndStampedByTheServer()
    {
        var written = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?> { ["animation"] = new Dictionary<string, object?> { ["clip"] = "Abrir", ["playing"] = true, ["loop"] = false, ["speed"] = 1.5, ["offset"] = 0.0 } });
        Assert.Null(LifeVerse.Api.Modules.Assets.MaterialAssets.ValidateSettings(written));
        var item = new LifeVerse.Api.Data.BuildObject { Id = Guid.NewGuid(), Name = "Porta", PrimitiveType = "cube", MaterialJson = written }; var now = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        LifeVerse.Api.Modules.Worlds.ObjectAnimation.Stamp(item, "{}", now);
        Assert.Contains($"\"since\":{now.ToUnixTimeMilliseconds()}", item.MaterialJson);                                      // every player works out the same pose from the server's clock
        var tooFast = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?> { ["animation"] = new Dictionary<string, object?> { ["clip"] = "x", ["playing"] = true, ["speed"] = 9.0 } });
        Assert.NotNull(LifeVerse.Api.Modules.Assets.MaterialAssets.ValidateSettings(tooFast));                                  // and the rules still hold for anything else
    }
}
