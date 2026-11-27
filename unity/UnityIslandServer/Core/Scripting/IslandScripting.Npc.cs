using LifeVerse.Scripting;
using LifeVerse.Shared.Networking;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>The non-player characters scripts make: the world of NPCs lives in the shared library (so it is tested without a server) and the server shows them to the players like any other avatar. Here they are tied to the scripts that made them.</summary>
public sealed partial class IslandScripting
{
    private NpcWorld? _npcs;

    /// <summary>The NPCs of the island. Set by the server, which also draws them for the players; null in tests that have none.</summary>
    public NpcWorld? Npcs
    {
        get => _npcs;
        set { _npcs = value; if (value is not null) value.Arrived = n => _host.NpcArrived(n.Script, n.PeerId, new Vec3(n.X, n.Y, n.Z), n.Reached); }
    }

    public int CreateNpc(ScriptDefinition script, string name, float x, float z) => _npcs?.Create(script.Id, script.OwnerUserId, name, x, z) ?? 0;
    public bool MoveNpc(ScriptDefinition script, int npc, float x, float z, float speed) => _npcs?.MoveTo(script.Id, npc, x, z, speed) ?? false;
    public bool StopNpc(ScriptDefinition script, int npc) => _npcs?.Stop(script.Id, npc) ?? false;
    public bool NpcAnimate(ScriptDefinition script, int npc, string gesture, double seconds) => _npcs?.Animate(script.Id, npc, gesture == "dance" ? NetworkMotionState.Dance : NetworkMotionState.Wave, seconds, Now) ?? false;
    public Vec3? GetNpcPosition(int npc) => _npcs?.Get(npc) is { } n ? new Vec3(n.X, n.Y, n.Z) : null;
    public bool RemoveNpc(ScriptDefinition script, int npc) => _npcs?.Remove(script.Id, npc) ?? false;
    public void RemoveNpcsOf(ScriptDefinition script) => _npcs?.RemoveScript(script.Id);

    /// <summary>An NPC speaks in the local chat with its own name, heard by the players within 20 m of it.</summary>
    public bool NpcSay(ScriptDefinition script, int npc, string text)
    {
        if (_npcs?.Get(npc) is not { } n || n.Script != script.Id) return false;
        _say(n.Name, new Vec3(n.X, n.Y, n.Z), text, 20f); return true;
    }
}
