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
    /// <summary>Where the walk is going in the end, and the points to pass on the way (turns around things; just the end when the way is clear).</summary>
    public float? TargetX, TargetZ;
    public List<(float X, float Z)> Path { get; } = [];
    public int Replans;
    public float Speed;
    /// <summary>A gesture (wave, dance) the NPC plays until this time.</summary>
    public double GestureUntil;
    public NetworkMotionState Gesture;
    /// <summary>Set when a walk ends: true when the NPC got where it was sent, false when it was stopped by the sea, the edge or something it could not go around.</summary>
    public bool Reached;
}

/// <summary>
/// The non-player characters of an island: made, moved and removed by scripts of the owner (the CREATE_NPCS permission), at most five a script and twenty an island. They walk to where they are told at a speed of up to 6 m/s, going AROUND
/// the solid things in the way (a path found over a grid of one metre, then straightened), never into the sea or off the island, and stand on the ground. If a thing appears in the way while they walk they look for another way, a few times.
/// A place they cannot reach (walled in, or too far to search) refuses the order. Pure logic, so it is tested without a running server.
/// </summary>
public sealed class NpcWorld
{
    public const int MaxPerIsland = 20, MaxPerScript = 5;
    public const float MinSpeed = .5f, MaxSpeed = 6f, DefaultSpeed = 1.8f, RunningFrom = 3f, ArriveWithin = .25f;
    public const float Cell = 1f, SampleEvery = .4f;
    public const int MaxSearchCells = 5000, MaxWindowCells = 140, MaxReplans = 5;

    private readonly Dictionary<int, NpcState> _npcs = [];
    private int _next = -1;
    private readonly Func<float, float, float> _height; private readonly Func<float> _seaLevel, _halfExtent;
    private readonly Func<float, float, bool>? _blocked;

    /// <summary>A new NPC to show, one that is gone (its peer number), and one whose walk ended (see <see cref="NpcState.Reached"/>).</summary>
    public Action<NpcState>? Spawned { get; set; }
    public Action<int>? Despawned { get; set; }
    public Action<NpcState>? Arrived { get; set; }

    /// <param name="blocked">True where an avatar cannot stand because something solid is there (null: nothing is in the way).</param>
    public NpcWorld(Func<float, float, float> height, Func<float> seaLevel, Func<float> halfExtent, Func<float, float, bool>? blocked = null) { _height = height; _seaLevel = seaLevel; _halfExtent = halfExtent; _blocked = blocked; }

    public IReadOnlyCollection<NpcState> All => _npcs.Values;
    public NpcState? Get(int id) => _npcs.GetValueOrDefault(id);
    public int CountOf(Guid script) => _npcs.Values.Count(n => n.Script == script);

    private bool Dry(float x, float z) => float.IsFinite(x) && float.IsFinite(z) && MathF.Abs(x) <= _halfExtent() && MathF.Abs(z) <= _halfExtent() && _height(x, z) >= _seaLevel() - .2f;
    private bool Free(float x, float z) => Dry(x, z) && !(_blocked?.Invoke(x, z) ?? false);

    /// <summary>Makes an NPC standing at (x, z). Zero when there is no room (the script or the island has too many) or the spot is in the sea or off the island.</summary>
    public int Create(Guid script, Guid owner, string name, float x, float z)
    {
        if (_npcs.Count >= MaxPerIsland || CountOf(script) >= MaxPerScript || !Dry(x, z)) return 0;
        var npc = new NpcState { PeerId = _next--, Script = script, Owner = owner, Name = name, X = x, Y = _height(x, z), Z = z };
        _npcs[npc.PeerId] = npc; Spawned?.Invoke(npc); return npc.PeerId;
    }

    /// <summary>Sends an NPC of that script walking to (x, z), around what is in the way. False when it is not that script's, the target is in the sea, off the island or inside something, or no way was found.</summary>
    public bool MoveTo(Guid script, int id, float x, float z, float speed)
    {
        if (!_npcs.TryGetValue(id, out var npc) || npc.Script != script || !Free(x, z)) return false;
        var path = FindPath(npc.X, npc.Z, x, z); if (path is null) return false;
        npc.TargetX = x; npc.TargetZ = z; npc.Path.Clear(); npc.Path.AddRange(path); npc.Replans = 0;
        npc.Speed = float.IsFinite(speed) ? Math.Clamp(speed, MinSpeed, MaxSpeed) : DefaultSpeed; npc.Gesture = NetworkMotionState.Idle; return true;
    }

    public bool Stop(Guid script, int id)
    {
        if (!_npcs.TryGetValue(id, out var npc) || npc.Script != script) return false;
        npc.TargetX = npc.TargetZ = null; npc.Path.Clear(); npc.Motion = NetworkMotionState.Idle; return true;
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
        npc.TargetX = npc.TargetZ = null; npc.Path.Clear(); npc.Gesture = gesture; npc.GestureUntil = now + Math.Clamp(seconds, 1, 30); npc.Motion = gesture; return true;
    }

    // ---- finding the way ----------------------------------------------------------------------------------------------------------

    /// <summary>Whether the straight line between two points can be walked: every sample of it is dry and clear.</summary>
    private bool ClearLine(float x0, float z0, float x1, float z1)
    {
        var dx = x1 - x0; var dz = z1 - z0; var length = MathF.Sqrt(dx * dx + dz * dz); var steps = Math.Max(1, (int)MathF.Ceiling(length / SampleEvery));
        for (var i = 1; i <= steps; i++) { var t = (float)i / steps; if (!Free(x0 + dx * t, z0 + dz * t)) return false; }
        return true;
    }

    /// <summary>The points to walk through to get from one place to another: just the end when the straight line is clear, otherwise the corners of a path found with A* over a one-metre grid (null when there is none, or the search gave up).</summary>
    public List<(float X, float Z)>? FindPath(float fromX, float fromZ, float toX, float toZ)
    {
        if (ClearLine(fromX, fromZ, toX, toZ)) return [(toX, toZ)];
        var margin = 14; var minX = (int)MathF.Floor(MathF.Min(fromX, toX)) - margin; var maxX = (int)MathF.Ceiling(MathF.Max(fromX, toX)) + margin; var minZ = (int)MathF.Floor(MathF.Min(fromZ, toZ)) - margin; var maxZ = (int)MathF.Ceiling(MathF.Max(fromZ, toZ)) + margin;
        if (maxX - minX > MaxWindowCells || maxZ - minZ > MaxWindowCells) return null;                      // too far to search
        int Id(int cx, int cz) => (cz - minZ) * (maxX - minX + 1) + (cx - minX);
        (int X, int Z) CellOf(float x, float z) => ((int)MathF.Floor(x / Cell), (int)MathF.Floor(z / Cell));
        var start = CellOf(fromX, fromZ); var goal = CellOf(toX, toZ);
        var walkable = new Dictionary<int, bool>();
        bool Walk(int cx, int cz) { if (cx < minX || cx > maxX || cz < minZ || cz > maxZ) return false; var id = Id(cx, cz); if (!walkable.TryGetValue(id, out var ok)) walkable[id] = ok = Free((cx + .5f) * Cell, (cz + .5f) * Cell); return ok; }
        var open = new PriorityQueue<(int X, int Z), float>(); var cost = new Dictionary<int, float> { [Id(start.X, start.Z)] = 0 }; var cameFrom = new Dictionary<int, (int X, int Z)>(); var closed = new HashSet<int>();
        open.Enqueue(start, 0); var expanded = 0;
        while (open.TryDequeue(out var current, out _))
        {
            var currentId = Id(current.X, current.Z); if (!closed.Add(currentId)) continue;
            if (current == goal) { return Simplify(Rebuild(cameFrom, current, start), fromX, fromZ, toX, toZ); }
            if (++expanded > MaxSearchCells) return null;
            for (var ox = -1; ox <= 1; ox++) for (var oz = -1; oz <= 1; oz++)
            {
                if (ox == 0 && oz == 0) continue; var nx = current.X + ox; var nz = current.Z + oz;
                var isGoal = nx == goal.X && nz == goal.Z; var isStart = nx == start.X && nz == start.Z;
                if (!isGoal && !isStart && !Walk(nx, nz)) continue;
                if (ox != 0 && oz != 0 && (!Walk(current.X + ox, current.Z) || !Walk(current.X, current.Z + oz))) continue;     // no cutting a corner of a wall
                var next = cost[currentId] + (ox != 0 && oz != 0 ? 1.4142f : 1f); var nextId = Id(nx, nz);
                if (cost.TryGetValue(nextId, out var known) && known <= next) continue;
                cost[nextId] = next; cameFrom[nextId] = current;
                var h = MathF.Sqrt((goal.X - nx) * (goal.X - nx) + (goal.Z - nz) * (goal.Z - nz)); open.Enqueue((nx, nz), next + h);
            }
        }
        return null;

        List<(int X, int Z)> Rebuild(Dictionary<int, (int X, int Z)> from, (int X, int Z) end, (int X, int Z) begin)
        {
            var cells = new List<(int X, int Z)> { end }; var at = end;
            while (at != begin && from.TryGetValue(Id(at.X, at.Z), out var previous)) { cells.Add(previous); at = previous; }
            cells.Reverse(); return cells;
        }
    }

    /// <summary>Straightens a path of cells: from each point goes as far along it as a straight, clear line allows, so the NPC cuts across open ground and only turns at the corners of things. Ends exactly at the target.</summary>
    private List<(float X, float Z)> Simplify(List<(int X, int Z)> cells, float fromX, float fromZ, float toX, float toZ)
    {
        var points = cells.Skip(1).Select(c => ((c.X + .5f) * Cell, (c.Z + .5f) * Cell)).ToList(); if (points.Count > 0) points[^1] = (toX, toZ); else points.Add((toX, toZ));
        var result = new List<(float X, float Z)>(); var (ax, az) = (fromX, fromZ); var index = 0;
        while (index < points.Count)
        {
            var far = index; for (var i = points.Count - 1; i > index; i--) if (ClearLine(ax, az, points[i].Item1, points[i].Item2)) { far = i; break; }
            result.Add(points[far]); (ax, az) = points[far]; index = far + 1;
        }
        return result;
    }

    // ---- walking ------------------------------------------------------------------------------------------------------------------

    /// <summary>Moves the NPCs that walk: a step toward the next point of the path, facing it; looks for another way when something is in the way; stops (and says so) on arriving, at the sea or the edge.</summary>
    public void Tick(double now, float dt)
    {
        foreach (var npc in _npcs.Values.ToList())
        {
            if (npc.Gesture != NetworkMotionState.Idle) { if (now < npc.GestureUntil) continue; npc.Gesture = NetworkMotionState.Idle; npc.Motion = NetworkMotionState.Idle; }
            if (npc.TargetX is not { } tx || npc.TargetZ is not { } tz) { npc.Motion = NetworkMotionState.Idle; continue; }
            var (px, pz) = npc.Path.Count > 0 ? npc.Path[0] : (tx, tz);
            var dx = px - npc.X; var dz = pz - npc.Z; var distance = MathF.Sqrt(dx * dx + dz * dz);
            if (distance <= ArriveWithin)
            {
                if (npc.Path.Count > 1) { npc.Path.RemoveAt(0); continue; }
                Finish(npc, tx, tz); continue;
            }
            var step = MathF.Min(npc.Speed * dt, distance); var nx = npc.X + dx / distance * step; var nz = npc.Z + dz / distance * step;
            npc.Yaw = MathF.Atan2(dx, dz);
            if (!Dry(nx, nz)) { Halt(npc, false); continue; }                                  // the sea or the edge: it stops where it is
            if (_blocked?.Invoke(nx, nz) ?? false)
            {
                // something is in the way that was not there when the way was planned: look for another one, a few times
                var path = npc.Replans < MaxReplans ? FindPath(npc.X, npc.Z, tx, tz) : null; npc.Replans++;
                if (path is null) { Halt(npc, false); continue; }
                npc.Path.Clear(); npc.Path.AddRange(path); continue;
            }
            npc.X = nx; npc.Z = nz; npc.Y = _height(nx, nz); npc.Motion = npc.Speed >= RunningFrom ? NetworkMotionState.Run : NetworkMotionState.Walk;
            if (step >= distance && npc.Path.Count <= 1) Finish(npc, tx, tz);
        }
    }

    private void Halt(NpcState npc, bool reached) { npc.TargetX = npc.TargetZ = null; npc.Path.Clear(); npc.Motion = NetworkMotionState.Idle; npc.Reached = reached; Arrived?.Invoke(npc); }

    private void Finish(NpcState npc, float x, float z) { npc.X = x; npc.Z = z; npc.Y = _height(x, z); Halt(npc, true); }

    /// <summary>The snapshots of the NPCs near a player (the same ones the players get).</summary>
    public IEnumerable<PlayerSnapshot> Snapshots(float x, float y, float z, float radius) => _npcs.Values.Where(n => (n.X - x) * (n.X - x) + (n.Y - y) * (n.Y - y) + (n.Z - z) * (n.Z - z) <= radius * radius)
        .Select(n => new PlayerSnapshot(n.PeerId, n.X, n.Y, n.Z, 0, 0, 0, n.Yaw, n.Motion, 0));
}
