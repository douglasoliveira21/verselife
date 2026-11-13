using MoonSharp.Interpreter;

namespace LifeVerse.Scripting;

/// <summary>An emitter of particles or a light that a script put on its object; <c>On</c> false takes it away.</summary>
public sealed record EffectSpec(int Id, string Kind, bool On, string Preset, double Rate, double Lifetime, double Size, double Speed, string Color, double Radius, double Energy, bool Flicker);

public sealed partial class ScriptHost
{
    public static readonly string[] ParticlePresets = ["smoke", "fire", "sparks", "rain", "snow", "explosion", "trail", "teleport", "dust"];
    public const int MaxEmittersPerScript = 4, MaxEmittersPerOwner = 12;
    private readonly Dictionary<Guid, Dictionary<int, EffectSpec>> _effects = [];

    private void InstallEffectsApi(Instance instance)
    {
        var script = instance.Sandbox.Script; var particles = new Table(script); script.Globals["particles"] = particles; var light = new Table(script); script.Globals["light"] = light;
        DynValue Fn(Func<CallbackArguments, DynValue> body) => DynValue.NewCallback((_, args) => { instance.Metrics.ApiCalls++; return body(args); });
        Dictionary<int, EffectSpec> Mine() => _effects.TryGetValue(instance.Definition.Id, out var list) ? list : _effects[instance.Definition.Id] = [];
        void Give(EffectSpec effect) { Mine()[effect.Id] = effect; world.SetEffect(instance.Definition, effect); }
        double Field(Table table, string name, double fallback, double min, double max) { var v = table.Get(name); if (v.Type == DataType.Nil) return fallback; var n = v.CastToNumber() ?? throw new ScriptRuntimeException($"{name} must be a number"); if (!double.IsFinite(n)) throw new ScriptRuntimeException($"{name} must be a finite number"); return Math.Clamp(n, min, max); }
        string ColorField(Table table, string name, string fallback)
        {
            var v = table.Get(name); if (v.Type == DataType.Nil) return fallback; if (v.Type == DataType.String) return ColorOf(v);
            if (v.Type == DataType.Table) return Rgb((float)(v.Table.Get(1).CastToNumber() ?? v.Table.Get("r").CastToNumber() ?? 1), (float)(v.Table.Get(2).CastToNumber() ?? v.Table.Get("g").CastToNumber() ?? 1), (float)(v.Table.Get(3).CastToNumber() ?? v.Table.Get("b").CastToNumber() ?? 1));
            throw new ScriptRuntimeException($"{name} must be '#RRGGBB' or a table {{r, g, b}} from 0 to 1");
        }
        particles["create"] = Fn(args =>
        {
            if (args.Count == 0 || args[0].Type != DataType.Table) throw new ScriptRuntimeException("particles.create expects a table {preset=, rate=, lifetime=, size=, color=, speed=}");
            var spec = args[0].Table; var presetValue = spec.Get("preset"); var preset = (presetValue.Type != DataType.Nil ? presetValue.CastToString() : spec.Get("texture").Type != DataType.Nil ? spec.Get("texture").CastToString() : "smoke")?.ToLowerInvariant() ?? "smoke";
            if (!ParticlePresets.Contains(preset)) throw new ScriptRuntimeException($"unknown preset '{preset}' (available: {string.Join(", ", ParticlePresets)})");
            if (Mine().Values.Count(e => e.Kind == "particles") >= MaxEmittersPerScript) throw new ScriptRuntimeException($"a script has at most {MaxEmittersPerScript} emitters; stop one with particles.stop(id)");
            if (_effects.Where(p => Find(p.Key)?.Definition.OwnerUserId == instance.Definition.OwnerUserId).Sum(p => p.Value.Values.Count(e => e.Kind == "particles")) >= MaxEmittersPerOwner) throw new ScriptRuntimeException($"a person has at most {MaxEmittersPerOwner} emitters on an island");
            var now = world.Now; while (instance.Pushes.Count > 0 && now - instance.Pushes.Peek() > 10) instance.Pushes.Dequeue(); if (instance.Pushes.Count >= 10) throw new ScriptRuntimeException("particles.create: at most 10 every 10 seconds"); instance.Pushes.Enqueue(now);
            var id = instance.NextEffectId++;
            Give(new EffectSpec(id, "particles", true, preset, Field(spec, "rate", 20, 1, 60), Field(spec, "lifetime", 2, .2, 6), Field(spec, "size", .4, .05, 2), Field(spec, "speed", 1.5, 0, 10), ColorField(spec, "color", "#FFFFFF"), 0, 0, false));
            return DynValue.NewNumber(id);
        });
        particles["stop"] = Fn(args => { var id = (int)Number(args, 0); if (!Mine().Remove(id, out var gone)) return DynValue.False; world.SetEffect(instance.Definition, gone with { On = false }); return DynValue.True; });
        particles["stopAll"] = Fn(_ => { var count = 0; foreach (var gone in Mine().Values.Where(e => e.Kind == "particles").ToList()) { Mine().Remove(gone.Id); world.SetEffect(instance.Definition, gone with { On = false }); count++; } return DynValue.NewNumber(count); });
        light["set"] = Fn(args =>
        {
            if (args.Count == 0 || args[0].Type != DataType.Table) throw new ScriptRuntimeException("light.set expects a table {color=, radius=, energy=, flicker=}");
            var spec = args[0].Table; var old = Mine().Values.FirstOrDefault(e => e.Kind == "light"); var id = old?.Id ?? instance.NextEffectId++;
            Give(new EffectSpec(id, "light", true, "", 0, 0, 0, 0, ColorField(spec, "color", "#FFFFFF"), Field(spec, "radius", 8, .5, 30), Field(spec, "energy", 1.5, 0, 8), spec.Get("flicker").CastToBool()));
            return DynValue.Nil;
        });
        light["off"] = Fn(_ => { if (Mine().Values.FirstOrDefault(e => e.Kind == "light") is { } gone) { Mine().Remove(gone.Id); world.SetEffect(instance.Definition, gone with { On = false }); } return DynValue.Nil; });
    }

    /// <summary>Takes down every emitter and light of a script that is going away.</summary>
    private void CloseEffects(Instance instance)
    {
        if (!_effects.Remove(instance.Definition.Id, out var list)) return;
        foreach (var effect in list.Values) world.SetEffect(instance.Definition, effect with { On = false });
    }

    /// <summary>The effects that are on now (for somebody who has just arrived).</summary>
    public IReadOnlyList<(ScriptDefinition Script, EffectSpec Effect)> ActiveEffects() => [.. _effects.Where(p => Find(p.Key) is not null).SelectMany(p => p.Value.Values.Select(e => (Find(p.Key)!.Definition, e)))];
}
