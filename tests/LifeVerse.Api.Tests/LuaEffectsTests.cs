using LifeVerse.Scripting;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Particles, lights and positional sounds that scripts make: parameters checked and clamped, limits per script and per person, and everything taken down when the script stops or goes away.</summary>
public sealed class LuaEffectsTests
{
    private sealed class World : IScriptWorld
    {
        public double Now { get; set; } = 100;
        public Dictionary<Guid, ScriptObjectState> Objects { get; } = [];
        public List<EffectSpec> Effects { get; } = [];
        public List<(string Name, float Volume, bool Loop, Vec3 At)> Sounds { get; } = [];
        public ScriptObjectState? GetObject(Guid id) => Objects.GetValueOrDefault(id);
        public void ApplyChange(ScriptDefinition script, ObjectChange change) { }
        public void Say(ScriptDefinition script, string text) { }
        public void SetEffect(ScriptDefinition script, EffectSpec effect) => Effects.Add(effect);
        public bool PlaySoundAt(ScriptDefinition script, string soundName, float volume, bool loop, Vec3 position) { Sounds.Add((soundName, volume, loop, position)); return soundName == "Alarme"; }
    }

    private static readonly Guid Owner = Guid.NewGuid();

    private sealed class Rig
    {
        public World World { get; } = new(); public ScriptHost Host { get; } public List<ScriptReport> Reports { get; } = []; public Guid Object { get; } = Guid.NewGuid(); public Guid Script { get; } = Guid.NewGuid();
        public Rig(string source, Guid? owner = null)
        {
            World.Objects[Object] = new ScriptObjectState(Object, owner ?? Owner, "Fogueira", default, default, new Vec3(1, 1, 1));
            Host = new ScriptHost(World, new ScriptHostOptions(MillisecondsPerEvent: 2000)); Host.Report += Reports.Add; Host.Sync([new ScriptDefinition(Script, Object, owner ?? Owner, "fogo", source, 1)]); Host.Process(500);
        }
        public void Touch() { Host.Enqueue(new ScriptEvent(Object, "touch")); Host.Process(500); }
    }

    [Fact]
    public void Particles_AreCreatedWithCheckedParameters_AndStopped()
    {
        var rig = new Rig("function on_touch() a = particles.create({preset = 'fire', rate = 30, lifetime = 1.5, size = 0.5, color = {1, 0.5, 0}, speed = 2}) b = particles.create({texture = 'smoke', rate = 9999, lifetime = 99, size = 99, speed = -4, color = '#112233'}) c = particles.create({}) end function on_message() chat_ok = particles.stop(a) and not particles.stop(a) end");
        rig.Touch(); Assert.Empty(rig.Reports); Assert.Equal(3, rig.World.Effects.Count);
        var fire = rig.World.Effects[0]; Assert.Equal(("particles", true, "fire", 30d, 1.5, .5, 2d, "#FF8000"), (fire.Kind, fire.On, fire.Preset, fire.Rate, fire.Lifetime, fire.Size, fire.Speed, fire.Color));
        var smoke = rig.World.Effects[1]; Assert.Equal(("smoke", 60d, 6d, 2d, 0d, "#112233"), (smoke.Preset, smoke.Rate, smoke.Lifetime, smoke.Size, smoke.Speed, smoke.Color));      // the example of the manual (texture =) works and numbers are clamped
        var plain = rig.World.Effects[2]; Assert.Equal(("smoke", 20d, 2d, "#FFFFFF"), (plain.Preset, plain.Rate, plain.Lifetime, plain.Color)); Assert.Equal([1, 2, 3], rig.World.Effects.Select(e => e.Id).ToArray());
        rig.Host.Enqueue(new ScriptEvent(rig.Object, "message")); rig.Host.Process(500); Assert.Equal((1, false), (rig.World.Effects[3].Id, rig.World.Effects[3].On)); Assert.Equal(4, rig.World.Effects.Count);     // stopped once; the second stop finds nothing
        Assert.Equal(2, rig.Host.ActiveEffects().Count);
    }

    [Theory]
    [InlineData("particles.create({preset = 'laser'})", "unknown preset")]
    [InlineData("particles.create(5)", "expects a table")]
    [InlineData("particles.create({rate = 'many'})", "rate must be a number")]
    [InlineData("particles.create({rate = 0/0})", "finite")]
    [InlineData("particles.create({color = 7})", "color must be")]
    [InlineData("particles.create({color = 'red'})", "colors must look like")]
    [InlineData("light.set(1)", "expects a table")]
    public void BadParticleAndLightCalls_AreRefusedWithAReason(string call, string why)
    {
        var rig = new Rig($"function on_touch() {call} end"); rig.Touch(); Assert.Contains(why, Assert.Single(rig.Reports).Message); Assert.Empty(rig.World.Effects);
    }

    [Fact]
    public void Emitters_AreLimitedPerScript_PerPerson_AndPerMoment()
    {
        var rig = new Rig("function on_touch() for i = 1, 5 do particles.create({}) end end"); rig.Touch(); Assert.Contains("at most 4 emitters", Assert.Single(rig.Reports).Message); Assert.Equal(4, rig.World.Effects.Count);
        var shared = new World(); var host = new ScriptHost(shared, new ScriptHostOptions(MillisecondsPerEvent: 2000)); var reports = new List<ScriptReport>(); host.Report += reports.Add; var objects = new List<Guid>();
        for (var i = 0; i < 4; i++) { var id = Guid.NewGuid(); objects.Add(id); shared.Objects[id] = new ScriptObjectState(id, Owner, "Fogo" + i, default, default, new Vec3(1, 1, 1)); host.Sync(objects.Select((o, n) => new ScriptDefinition(Guid.NewGuid(), o, Owner, "s" + n, "function on_touch() particles.create({}) particles.create({}) particles.create({}) particles.create({}) end", 1)).ToList()); }
        foreach (var id in objects) { host.Enqueue(new ScriptEvent(id, "touch")); host.Process(500); shared.Now += 11; }
        Assert.Equal(ScriptHost.MaxEmittersPerOwner, shared.Effects.Count(e => e.On)); Assert.Contains(reports, r => r.Message.Contains("at most 12 emitters"));        // one person cannot cover the island
        var burst = new Rig("function on_touch() for i = 1, 3 do particles.create({}) particles.stopAll() end end"); burst.Touch(); burst.Touch(); burst.Touch(); burst.Touch(); Assert.Contains(burst.Reports, r => r.Message.Contains("at most 10 every 10 seconds"));
    }

    [Fact]
    public void ALight_IsOnePerScript_AndChangesInPlace()
    {
        var rig = new Rig("function on_touch() light.set({color = '#FFC46B', radius = 99, energy = 99, flicker = true}) end function on_message() light.set({radius = 3}) end function on_enable() light.off() light.off() end");
        rig.Touch(); var first = Assert.Single(rig.World.Effects); Assert.Equal(("light", "#FFC46B", 30d, 8d, true), (first.Kind, first.Color, first.Radius, first.Energy, first.Flicker));
        rig.Host.Enqueue(new ScriptEvent(rig.Object, "message")); rig.Host.Process(500); Assert.Equal(2, rig.World.Effects.Count); Assert.Equal((first.Id, 3d), (rig.World.Effects[1].Id, rig.World.Effects[1].Radius));      // same light, new numbers
        Assert.Single(rig.Host.ActiveEffects());
        rig.Host.SetEnabled(rig.Script, false); rig.Host.SetEnabled(rig.Script, true); rig.Host.Process(500); Assert.False(rig.World.Effects.Last().On); Assert.Empty(rig.Host.ActiveEffects());
    }

    [Fact]
    public void EverythingIsTakenDown_WhenTheScriptStopsIsEditedOrGoesAway()
    {
        string Source(string tag) => $"function on_touch() particles.create({{}}) light.set({{}}) end -- {tag}";
        var rig = new Rig(Source("v1")); rig.Touch(); Assert.Equal(2, rig.World.Effects.Count(e => e.On));
        rig.Host.Stop(rig.Script); Assert.Equal(2, rig.World.Effects.Count(e => !e.On)); Assert.Empty(rig.Host.ActiveEffects());
        rig.Host.Restart(rig.Script); rig.Host.Process(500); rig.Touch(); Assert.Equal(2, rig.Host.ActiveEffects().Count);
        rig.Host.Sync([new ScriptDefinition(rig.Script, rig.Object, Owner, "fogo", Source("v2"), 2)]); Assert.Empty(rig.Host.ActiveEffects()); Assert.Equal(4, rig.World.Effects.Count(e => !e.On));         // an edited script starts clean
        rig.Touch(); Assert.Equal(2, rig.Host.ActiveEffects().Count); rig.Host.Sync([]); Assert.Empty(rig.Host.ActiveEffects()); Assert.Equal(6, rig.World.Effects.Count(e => !e.On));                           // removed: nothing stays on
    }

    [Fact]
    public void PositionalSound_PlaysOnlyWhatTheObjectCarries_WithinLimits()
    {
        var rig = new Rig("function on_touch() ok = audio.playAtPosition('Alarme', 10, 1, 20, 5, true) no = audio.playAtPosition('Outro', 99999, 0, 0) chat_ok = tostring(ok) .. tostring(no) end");
        rig.Touch(); Assert.Empty(rig.Reports); Assert.Equal(("Alarme", 1f, true, new Vec3(10, 1, 20)), rig.World.Sounds[0]); Assert.Equal(5000f, rig.World.Sounds[1].At.X);                      // volume and distance are clamped
        var spam = new Rig("function on_touch() for i = 1, 8 do audio.playAtPosition('Alarme', 0, 0, 0) end end"); spam.Touch(); Assert.Contains("at most 5 calls", Assert.Single(spam.Reports).Message); Assert.Equal(5, spam.World.Sounds.Count);
    }
}
