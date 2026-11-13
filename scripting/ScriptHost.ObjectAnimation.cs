using MoonSharp.Interpreter;

namespace LifeVerse.Scripting;

/// <summary>Where a script left the animation of its object (so pausing can work out the point reached).</summary>
public sealed class ObjectAnimationState
{
    public string Clip = ""; public bool Playing, Loop; public double Speed = 1, Offset, StartedAt;
    public double PositionAt(double now) => Playing ? Offset + (now - StartedAt) * Speed : Offset;
}

public sealed partial class ScriptHost
{
    public const double MinAnimationSpeed = .1, MaxAnimationSpeed = 4, MaxAnimationOffset = 3600;

    /// <summary>
    /// animation.*: the mesh animation of the object itself (the "Animação de Mesh" of the Recursos tab), played from a clip the imported model has. It goes through the same change of the object as every other script change; the server
    /// stamps the start with its clock, so every player sees the same pose. A clip the model does not have is ignored by the clients. The server does not know the length of a clip, so there is no event when one ends.
    /// </summary>
    private void InstallObjectAnimationApi(Instance instance)
    {
        var script = instance.Sandbox.Script; var animation = new Table(script); script.Globals["animation"] = animation;
        DynValue Fn(Func<CallbackArguments, DynValue> body) => DynValue.NewCallback((_, args) => { instance.Metrics.ApiCalls++; return body(args); });
        ObjectChange Change() => instance.Pending ?? throw new ScriptRuntimeException("objects can only be changed while handling an event");
        void Write(ObjectAnimationState s) => Change().Material["animation"] = new Dictionary<string, object?> { ["clip"] = s.Clip, ["playing"] = s.Playing, ["loop"] = s.Loop, ["speed"] = s.Speed, ["offset"] = s.Offset };
        static double Clamp(double value, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : min;

        animation["play"] = Fn(args =>
        {
            var clip = args.Count > 0 && args[0].Type is DataType.String ? args[0].String.Trim() : "";
            if (clip.Length is 0 or > 64 || clip.Any(char.IsControl)) throw new ScriptRuntimeException("animation.play: the clip needs a name of 1 to 64 letters (see the clips of the model in the Recursos tab)");
            var state = new ObjectAnimationState { Clip = clip, Playing = true, StartedAt = world.Now };
            if (args.Count > 1 && args[1].Type == DataType.Table)
            {
                var o = args[1].Table;
                if (o.Get("loop") is { Type: DataType.Boolean } l) state.Loop = l.Boolean;
                if (o.Get("speed") is { Type: DataType.Number } s) state.Speed = Clamp(s.Number, MinAnimationSpeed, MaxAnimationSpeed);
                if (o.Get("offset") is { Type: DataType.Number } f) state.Offset = Clamp(f.Number, 0, MaxAnimationOffset);
            }
            instance.ObjectAnimation = state; Write(state); return DynValue.True;
        });
        animation["pause"] = Fn(_ =>
        {
            if (instance.ObjectAnimation is not { Playing: true } s) return DynValue.False;
            s.Offset = s.PositionAt(world.Now); s.Playing = false; Write(s); return DynValue.True;
        });
        animation["resume"] = Fn(_ =>
        {
            if (instance.ObjectAnimation is not { Playing: false } s || s.Clip.Length == 0) return DynValue.False;
            s.Playing = true; s.StartedAt = world.Now; Write(s); return DynValue.True;
        });
        animation["stop"] = Fn(_ =>
        {
            if (instance.ObjectAnimation is not { } s) return DynValue.False;
            s.Playing = false; s.Offset = 0; Write(s); return DynValue.True;      // back to the first pose, still
        });
        animation["setSpeed"] = Fn(args =>
        {
            if (instance.ObjectAnimation is not { } s) return DynValue.False;
            var now = world.Now; s.Offset = s.PositionAt(now); s.StartedAt = now; s.Speed = Clamp(Number(args, 0, 1), MinAnimationSpeed, MaxAnimationSpeed); Write(s); return DynValue.True;
        });
        animation["getState"] = Fn(_ =>
        {
            if (instance.ObjectAnimation is not { } s) return DynValue.Nil;
            var table = new Table(script); table["clip"] = s.Clip; table["playing"] = s.Playing; table["loop"] = s.Loop; table["speed"] = s.Speed; table["position"] = s.PositionAt(world.Now); return DynValue.NewTable(table);
        });
    }
}
