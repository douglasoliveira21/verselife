namespace LifeVerse.Shared.Networking;

/// <summary>A non-player character a script made: it has a (negative) peer number so the clients draw it like any other avatar, with the same snapshots.</summary>
public sealed class NpcState
{
    public int PeerId { get; init; }
    public Guid Script { get; init; }
    public Guid Owner { get; init; }
    public string Name { get; set; } = "";
    public float X, Y, Z, Yaw;
    public NetworkMotionState Motion;
    public float? TargetX, TargetZ;
    public float Speed;
    /// <summary>A gesture (wave, dance) the NPC plays until this time.</summary>
    public double GestureUntil;
    public NetworkMotionState Gesture;
}

/// <summary>
/// The non-player characters of an island: made, moved and removed by scripts of the owner (the CREATE_NPCS permission), at most five a script and twenty an island. They walk in a straight line to where they are told
/// (no path-finding: they do not walk around obstacles), at a speed of up to 6 m/s, never into the sea or off the island, and stand on the ground. Pure logic, so it is tested without a running server.
/// </summary>
public sealed class NpcWorld
{
    public const int MaxPerIsland = 20, MaxPerScript = 5;
    public const float MinSpeed = .5f, MaxSpeed = 6f, DefaultSpeed = 1.8f, RunningFrom = 3f, ArriveWithin = .25f;

    private readonly Dictionary<int, NpcState> _npcs = [];
    private int _next = -1;
    private readonly Func<float, float, float> _height; private readonly Func<float> _seaLevel, _halfExtent;

    /// <summary>A new NPC to show, one that is gone (its peer number), and one that arrived where it was sent.</summary>
    public Action<NpcState>? Spawned { get; set; }
    public Action<int>? Despawned { get; set; }
    public Action<NpcState>? Arrived { get; set; }

    public NpcWorld(Func<float, float, float> height, Func<float> seaLevel, Func<float> halfExtent) { _height = height; _seaLevel = seaLevel; _halfExtent = halfExtent; }

    public IReadOnlyCollection<NpcState> All => _npcs.Values;
    public NpcState? Get(int id) => _npcs.GetValueOrDefault(id);
    public int CountOf(Guid script) => _npcs.Values.Count(n => n.Script == script);

    private bool Inside(float x, float z) => float.IsFinite(x) && float.IsFinite(z) && MathF.Abs(x) <= _halfExtent() && MathF.Abs(z) <= _halfExtent() && _height(x, z) >= _seaLevel() - .2f;

    /// <summary>Makes an NPC standing at (x, z). Zero when there is no room (the script or the island has too many) or the spot is in the sea or off the island.</summary>
    public int Create(Guid script, Guid owner, string name, float x, float z)
    {
        if (_npcs.Count >= MaxPerIsland || CountOf(script) >= MaxPerScript || !Inside(x, z)) return 0;
        var npc = new NpcState { PeerId = _next--, Script = script, Owner = owner, Name = name, X = x, Y = _height(x, z), Z = z };
        _npcs[npc.PeerId] = npc; Spawned?.Invoke(npc); return npc.PeerId;
    }

    /// <summary>Sends an NPC of that script walking to (x, z). False when it is not that script's, or the target is in the sea or off the island.</summary>
    public bool MoveTo(Guid script, int id, float x, float z, float speed)
    {
        if (!_npcs.TryGetValue(id, out var npc) || npc.Script != script || !Inside(x, z)) return false;
        npc.TargetX = x; npc.TargetZ = z; npc.Speed = float.IsFinite(speed) ? Math.Clamp(speed, MinSpeed, MaxSpeed) : DefaultSpeed; npc.Gesture = NetworkMotionState.Idle; return true;
    }

    public bool Stop(Guid script, int id)
    {
        if (!_npcs.TryGetValue(id, out var npc) || npc.Script != script) return false;
        npc.TargetX = npc.TargetZ = null; npc.Motion = NetworkMotionState.Idle; return true;
    }

    public bool Remove(Guid script, int id)
    {
        if (!_npcs.TryGetValue(id, out var npc) || npc.Script != script) return false;
        _npcs.Remove(id); Despawned?.Invoke(id); return true;
    }

    /// <summary>Takes away every NPC of a script (it ended, stopped or restarted).</summary>
    public int RemoveScript(Guid script)
    {
        var gone = _npcs.Values.Where(n => n.Script == script).Select(n => n.PeerId).ToList();
        foreach (var id in gone) { _npcs.Remove(id); Despawned?.Invoke(id); }
        return gone.Count;
    }

    public void RemoveAll() { foreach (var id in _npcs.Keys.ToList()) { _npcs.Remove(id); Despawned?.Invoke(id); } }

    public bool Animate(Guid script, int id, NetworkMotionState gesture, double seconds, double now)
    {
        if (!_npcs.TryGetValue(id, out var npc) || npc.Script != script || gesture is not (NetworkMotionState.Wave or NetworkMotionState.Dance)) return false;
        npc.TargetX = npc.TargetZ = null; npc.Gesture = gesture; npc.GestureUntil = now + Math.Clamp(seconds, 1, 30); npc.Motion = gesture; return true;
    }

    /// <summary>Moves the NPCs that walk: a step toward the target, facing it; stops (and says so) on arriving, at the sea or the edge.</summary>
    public void Tick(double now, float dt)
    {
        foreach (var npc in _npcs.Values.ToList())
        {
            if (npc.Gesture != NetworkMotionState.Idle) { if (now < npc.GestureUntil) continue; npc.Gesture = NetworkMotionState.Idle; npc.Motion = NetworkMotionState.Idle; }
            if (npc.TargetX is not { } tx || npc.TargetZ is not { } tz) { npc.Motion = NetworkMotionState.Idle; continue; }
            var dx = tx - npc.X; var dz = tz - npc.Z; var distance = MathF.Sqrt(dx * dx + dz * dz);
            if (distance <= ArriveWithin) { Finish(npc, tx, tz); continue; }
            var step = MathF.Min(npc.Speed * dt, distance); var nx = npc.X + dx / distance * step; var nz = npc.Z + dz / distance * step;
            npc.Yaw = MathF.Atan2(dx, dz);
            if (!Inside(nx, nz)) { npc.TargetX = npc.TargetZ = null; npc.Motion = NetworkMotionState.Idle; Arrived?.Invoke(npc); continue; }      // the sea or the edge: it stops where it is
            npc.X = nx; npc.Z = nz; npc.Y = _height(nx, nz); npc.Motion = npc.Speed >= RunningFrom ? NetworkMotionState.Run : NetworkMotionState.Walk;
            if (step >= distance) Finish(npc, tx, tz);
        }
    }

    private void Finish(NpcState npc, float x, float z)
    {
        npc.X = x; npc.Z = z; npc.Y = _height(x, z); npc.TargetX = npc.TargetZ = null; npc.Motion = NetworkMotionState.Idle; Arrived?.Invoke(npc);
    }

    /// <summary>The snapshots of the NPCs near a player (the same ones the players get).</summary>
    public IEnumerable<PlayerSnapshot> Snapshots(float x, float y, float z, float radius) => _npcs.Values.Where(n => (n.X - x) * (n.X - x) + (n.Y - y) * (n.Y - y) + (n.Z - z) * (n.Z - z) <= radius * radius)
        .Select(n => new PlayerSnapshot(n.PeerId, n.X, n.Y, n.Z, 0, 0, 0, n.Yaw, n.Motion, 0));
}
