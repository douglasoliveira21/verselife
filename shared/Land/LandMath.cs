namespace LifeVerse.Shared.Land;

/// <summary>A straight piece of the border of some cells, in cell corners (multiply by <see cref="LandMath.CellSize"/> for metres).</summary>
public readonly record struct LandEdge(int X0, int Z0, int X1, int Z1);

/// <summary>
/// The territorial grid as the client needs it: the island is cut into 4 x 4 m cells and a parcel is a set of connected cells. The server
/// (backend LandGrid) has the same numbers; LandParityTests keeps the two from drifting apart.
/// </summary>
public static class LandMath
{
    public const int CellSize = 4;
    public const int MaxCells = 16384;

    public static long Key(int cx, int cz) => ((long)cx << 32) | (uint)cz;
    public static (int Cx, int Cz) Unkey(long key) => ((int)(key >> 32), (int)(uint)key);
    public static int CellOf(double metres) => (int)Math.Floor(metres / CellSize);

    public static bool InsideIsland(int cx, int cz, int islandSize)
    {
        var half = islandSize / 2d;
        return cx * (double)CellSize >= -half && (cx + 1) * (double)CellSize <= half && cz * (double)CellSize >= -half && (cz + 1) * (double)CellSize <= half;
    }

    /// <summary>Every cell touched by the rectangle between two points (the area a person drags over the ground).</summary>
    public static HashSet<long> Between(double ax, double az, double bx, double bz)
    {
        var cells = new HashSet<long>();
        int x0 = CellOf(Math.Min(ax, bx)), x1 = CellOf(Math.Max(ax, bx)), z0 = CellOf(Math.Min(az, bz)), z1 = CellOf(Math.Max(az, bz));
        if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > MaxCells) return cells;
        for (var cx = x0; cx <= x1; cx++) for (var cz = z0; cz <= z1; cz++) cells.Add(Key(cx, cz));
        return cells;
    }

    /// <summary>Cells as rows [cz, firstCx, lastCx], the way the server sends them.</summary>
    public static HashSet<long> FromRuns(IEnumerable<int[]> runs)
    {
        var cells = new HashSet<long>();
        foreach (var run in runs)
        {
            if (run.Length != 3) continue;
            for (var cx = run[1]; cx <= run[2]; cx++) { cells.Add(Key(cx, run[0])); if (cells.Count > MaxCells) return cells; }
        }
        return cells;
    }

    /// <summary>The outline of a set of cells, with straight neighbouring pieces joined into one edge.</summary>
    public static List<LandEdge> Boundary(IReadOnlySet<long> cells)
    {
        var horizontal = new Dictionary<int, List<int>>(); var vertical = new Dictionary<int, List<int>>();
        void Add(Dictionary<int, List<int>> map, int line, int at) { if (!map.TryGetValue(line, out var list)) map[line] = list = []; list.Add(at); }
        foreach (var key in cells)
        {
            var (cx, cz) = Unkey(key);
            if (!cells.Contains(Key(cx, cz - 1))) Add(horizontal, cz, cx);
            if (!cells.Contains(Key(cx, cz + 1))) Add(horizontal, cz + 1, cx);
            if (!cells.Contains(Key(cx - 1, cz))) Add(vertical, cx, cz);
            if (!cells.Contains(Key(cx + 1, cz))) Add(vertical, cx + 1, cz);
        }
        var edges = new List<LandEdge>();
        void Merge(Dictionary<int, List<int>> map, bool isHorizontal)
        {
            foreach (var (line, positions) in map)
            {
                positions.Sort();
                var start = positions[0]; var previous = positions[0];
                for (var i = 1; i <= positions.Count; i++)
                {
                    if (i < positions.Count && positions[i] == previous + 1) { previous = positions[i]; continue; }
                    edges.Add(isHorizontal ? new LandEdge(start, line, previous + 1, line) : new LandEdge(line, start, line, previous + 1));
                    if (i < positions.Count) start = previous = positions[i];
                }
            }
        }
        Merge(horizontal, true); Merge(vertical, false);
        return edges;
    }

    /// <summary>The smallest block of cells that holds all of them: (minCx, minCz, maxCx, maxCz), or null for none.</summary>
    public static (int MinCx, int MinCz, int MaxCx, int MaxCz)? Extent(IEnumerable<long> cells)
    {
        int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue; var any = false;
        foreach (var key in cells) { any = true; var (cx, cz) = Unkey(key); minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx); minZ = Math.Min(minZ, cz); maxZ = Math.Max(maxZ, cz); }
        return any ? (minX, minZ, maxX, maxZ) : null;
    }

    public static double AreaOf(int cellCount) => (double)cellCount * CellSize * CellSize;
}
