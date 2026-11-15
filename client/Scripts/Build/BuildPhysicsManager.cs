using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Build;

/// <summary>
/// The basic physical states of an object, as four switches: protected against accidental editing (Travar), simulated by the island (Física), temporary (removed by the server
/// when its time is up) and ghost (no solid collision). Each is a real change stored with the object; the rules are the ones the server checks (<see cref="PhysicsRules"/>).
/// </summary>
public partial class BuildPhysicsManager : Node
{
    public sealed record Flags(bool? Locked, bool? Physical, bool? Temporary, bool? Ghost);

    private BuildNetworkSynchronizer _sync = null!;
    private BuildSelectionManager _selection = null!;

    public void Initialize(BuildNetworkSynchronizer sync, BuildSelectionManager selection) { _sync = sync; _selection = selection; }

    /// <summary>The state of the selection: true or false when every object agrees, null when they differ.</summary>
    public Flags? Read()
    {
        var states = _selection.Views().Select(v => v.State).ToList(); if (states.Count == 0) return null;
        bool? All(Func<BuildObjectState, bool> pick) { var first = pick(states[0]); return states.All(s => pick(s) == first) ? first : null; }
        return new Flags(All(s => s.EditLocked), All(s => PhysicsRules.Read(s.PhysicsJson).Physical), All(s => PhysicsRules.Read(s.PhysicsJson).Temporary), All(s => !PhysicsRules.Read(s.PhysicsJson).Collision));
    }

    /// <summary>Protects (or unprotects) the selection. Returns false when nothing was sent.</summary>
    public bool SetLocked(bool locked) => _selection.Count > 0 && _sync.Protect(_selection.Ids, locked);

    private (int Sent, string? Problem) Edit(Func<PhysicsSettings, (PhysicsSettings Settings, string? Problem)> change)
    {
        var sent = 0; string? problem = null; var states = _selection.Views().Select(v => v.State).ToList();
        // all or none: one object that cannot take the change stops it for everybody, so a selection is never half changed
        var plan = new List<BuildObjectState>();
        foreach (var state in states)
        {
            var (settings, why) = change(SettingsOf(state)); if (why is not null) { problem = why; break; }
            var after = BuildPersistenceManager.Clone(state); after.PhysicsJson = PhysicsRules.Write(state.PhysicsJson, settings);
            if (after.PhysicsJson != state.PhysicsJson) plan.Add(after);
        }
        if (problem is null) foreach (var after in plan) if (_sync.Update(after)) sent++;
        return (sent, problem);
    }

    /// <summary>The object's physics settings. An imported model with no shape of its own is exact (triangle mesh): reading it as a box and writing that back would turn a house into a solid block.</summary>
    public static PhysicsSettings SettingsOf(BuildObjectState state)
    {
        var read = PhysicsRules.Read(state.PhysicsJson);
        return state.PrimitiveType == "mesh" && !HasShape(state.PhysicsJson) ? read with { CollisionShape = "triangle" } : read;
    }

    private static bool HasShape(string json) { try { return !string.IsNullOrWhiteSpace(json) && System.Text.Json.Nodes.JsonNode.Parse(json) is System.Text.Json.Nodes.JsonObject o && o["shape"] is not null; } catch (System.Text.Json.JsonException) { return false; } }

    public (int Sent, string? Problem) SetPhysical(bool on) => Edit(s =>
    {
        if (!on) return (s with { Physical = false }, null);
        if (!s.Collision) return (s, "Um objeto físico precisa de colisão sólida: desligue Fantasma primeiro.");
        if (s.Trigger) return (s, "Um objeto que só dispara eventos não pode ser físico.");
        return (s with { Physical = true, CollisionShape = s.CollisionShape is "triangle" or "none" ? "box" : s.CollisionShape }, null);
    });

    public (int Sent, string? Problem) SetTemporary(bool on) => Edit(s => (s with { Temporary = on }, null));

    public (int Sent, string? Problem) SetGhost(bool on) => Edit(s => (s with { Collision = !on, Physical = on ? false : s.Physical }, null));
}
