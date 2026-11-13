using MoonSharp.Interpreter;

namespace LifeVerse.Scripting;

public sealed partial class ScriptHost
{
    public const int MaxNpcNameLength = 40, MaxNpcSaysPer10Seconds = 5, MaxNpcCreatesPerMinute = 10, MaxNpcMovesPerSecond = 10;
    public const float MaxNpcDistance = 5000f;

    /// <summary>npc.*: non-player characters of the island, made by a script whose owner gave it CREATE_NPCS (editor, tab Permissões). An NPC belongs to the script that made it and goes when the script ends, stops or restarts.</summary>
    private void InstallNpcApi(Instance instance)
    {
        var script = instance.Sandbox.Script; var npc = new Table(script); script.Globals["npc"] = npc;
        DynValue Fn(Func<CallbackArguments, DynValue> body) => DynValue.NewCallback((_, args) =>
        {
            instance.Metrics.ApiCalls++;
            if (!_permissions.Has(instance.Definition.Id, Guid.Empty, ScriptPermission.CreateNpcs)) throw new ScriptRuntimeException("npc needs the CREATE_NPCS permission: the owner of the object gives it in the editor (tab Permissões)");
            return body(args);
        });
        int Mine(CallbackArguments args) { var id = (int)Math.Floor(Number(args, 0)); if (!instance.Npcs.Contains(id)) throw new ScriptRuntimeException("that is not an NPC of this script (see npc.list)"); return id; }
        float Coordinate(CallbackArguments args, int index) { var value = Number(args, index); if (MathF.Abs(value) > MaxNpcDistance) throw new ScriptRuntimeException($"coordinates are within {MaxNpcDistance:N0} m of the origin"); return value; }

        npc["create"] = Fn(args =>
        {
            var name = Plain(args, 0, "NPC name").Trim(); if (name.Length > MaxNpcNameLength) name = name[..MaxNpcNameLength];
            var x = Coordinate(args, 1); var z = Coordinate(args, 2);
            var now = world.Now; while (instance.NpcCreates.Count > 0 && now - instance.NpcCreates.Peek() > 60) instance.NpcCreates.Dequeue();
            if (instance.NpcCreates.Count >= MaxNpcCreatesPerMinute) throw new ScriptRuntimeException($"npc.create: at most {MaxNpcCreatesPerMinute} a minute"); instance.NpcCreates.Enqueue(now);
            var id = world.CreateNpc(instance.Definition, name, x, z);
            if (id == 0) return DynValue.Nil;       // no room (five a script, twenty an island) or the spot is the sea or off the island
            instance.Npcs.Add(id); return DynValue.NewNumber(id);
        });
        npc["moveTo"] = Fn(args =>
        {
            var now = world.Now; while (instance.NpcMoves.Count > 0 && now - instance.NpcMoves.Peek() > 1) instance.NpcMoves.Dequeue();
            if (instance.NpcMoves.Count >= MaxNpcMovesPerSecond) throw new ScriptRuntimeException($"npc.moveTo: at most {MaxNpcMovesPerSecond} a second (each one looks for a way around things)"); instance.NpcMoves.Enqueue(now);
            return DynValue.NewBoolean(world.MoveNpc(instance.Definition, Mine(args), Coordinate(args, 1), Coordinate(args, 2), Number(args, 3, 1.8f)));
        });
        npc["stop"] = Fn(args => DynValue.NewBoolean(world.StopNpc(instance.Definition, Mine(args))));
        npc["say"] = Fn(args =>
        {
            var id = Mine(args); var text = Plain(args, 1, "message"); if (text.Length > 500) text = text[..500];
            var now = world.Now; while (instance.NpcSays.Count > 0 && now - instance.NpcSays.Peek() > 10) instance.NpcSays.Dequeue();
            if (instance.NpcSays.Count >= MaxNpcSaysPer10Seconds) throw new ScriptRuntimeException($"npc.say: at most {MaxNpcSaysPer10Seconds} messages every 10 seconds"); instance.NpcSays.Enqueue(now);
            return DynValue.NewBoolean(world.NpcSay(instance.Definition, id, text));
        });
        npc["playAnimation"] = Fn(args =>
        {
            var id = Mine(args); var name = Plain(args, 1, "animation name").ToLowerInvariant();
            if (!Gestures.Contains(name)) throw new ScriptRuntimeException($"unknown animation '{name}' (available: {string.Join(", ", Gestures)})");
            return DynValue.NewBoolean(world.NpcAnimate(instance.Definition, id, name, Math.Clamp(Number(args, 2, 4), 1, 30)));
        });
        npc["getPosition"] = Fn(args => world.GetNpcPosition(Mine(args)) is { } at ? FromVec(script, at) : DynValue.Nil);
        npc["remove"] = Fn(args => { var id = Mine(args); var ok = world.RemoveNpc(instance.Definition, id); instance.Npcs.Remove(id); return DynValue.NewBoolean(ok); });
        npc["list"] = Fn(_ => { var table = new Table(script); var index = 1; foreach (var id in instance.Npcs.Order()) table[index++] = id; return DynValue.NewTable(table); });
    }

    /// <summary>The NPCs of a script go with it (it ended, stopped, restarted or was suspended).</summary>
    private void CloseNpcs(Instance instance)
    {
        if (instance.Npcs.Count == 0) return;
        world.RemoveNpcsOf(instance.Definition); instance.Npcs.Clear();
    }

    /// <summary>An NPC of a script arrived where it was sent (or stopped at the sea or the edge): the script hears <c>on_npc_arrived</c>.</summary>
    public void NpcArrived(Guid scriptId, int npcId, Vec3 at, bool arrived = true)
    {
        if (Find(scriptId) is not { } instance || !instance.Npcs.Contains(npcId)) return;
        Enqueue(new ScriptEvent(instance.Definition.ObjectId, "npc_arrived", new Dictionary<string, object?> { ["npc"] = npcId, ["arrived"] = arrived, ["position"] = new Dictionary<string, object?> { ["x"] = at.X, ["y"] = at.Y, ["z"] = at.Z } }, scriptId));
    }
}
