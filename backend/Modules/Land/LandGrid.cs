namespace LifeVerse.Api.Modules.Land;

/// <summary>
/// The territorial grid: the island is cut into square cells of 4 x 4 metres and a parcel is a set of cells that touch each other
/// along their sides. A parcel can be a rectangle or any other connected shape; two parcels can never own the same cell.
/// Cell (cx, cz) covers x from cx * 4 to (cx + 1) * 4 and z from cz * 4 to (cz + 1) * 4.
/// </summary>
public static class LandGrid
{
    public const int CellSize = 4;
    public const int CellArea = CellSize * CellSize;
    public const int MaxCellsPerParcel = 16384;

    /// <summary>One number for a cell, so sets of cells are cheap to keep and compare.</summary>
    public static long Key(int cx, int cz) => ((long)cx << 32) | (uint)cz;
    public static (int Cx, int Cz) Unkey(long key) => ((int)(key >> 32), (int)(uint)key);

    public static int CellOf(double metres) => (int)Math.Floor(metres / CellSize);

    /// <summary>The cells that lie completely inside an island of this size (the island goes from -size/2 to +size/2 on both axes).</summary>
    public static bool InsideIsland(int cx, int cz, int islandSize)
    {
        var half = islandSize / 2d;
        return cx * (double)CellSize >= -half && (cx + 1) * (double)CellSize <= half && cz * (double)CellSize >= -half && (cz + 1) * (double)CellSize <= half;
    }

    /// <summary>The cells whose centre lies inside the rectangle (min inclusive, max exclusive).</summary>
    public static HashSet<long> CellsInRect(double minX, double minZ, double maxX, double maxZ)
    {
        var cells = new HashSet<long>();
        if (!(minX < maxX && minZ < maxZ)) return cells;
        for (var cx = CellOf(minX); cx <= CellOf(maxX); cx++)
            for (var cz = CellOf(minZ); cz <= CellOf(maxZ); cz++)
            {
                var centreX = (cx + .5) * CellSize; var centreZ = (cz + .5) * CellSize;
                if (centreX >= minX && centreX < maxX && centreZ >= minZ && centreZ < maxZ) cells.Add(Key(cx, cz));
                if (cells.Count > MaxCellsPerParcel) return cells;
            }
        return cells;
    }

    /// <summary>True when every cell can be reached from every other by stepping over shared sides (corners alone do not connect).</summary>
    public static bool IsContiguous(IReadOnlyCollection<long> cells)
    {
        if (cells.Count == 0) return false;
        var all = cells as HashSet<long> ?? [.. cells];
        var seen = new HashSet<long>(); var queue = new Queue<long>();
        var first = all.First(); seen.Add(first); queue.Enqueue(first);
        while (queue.Count > 0)
        {
            var (cx, cz) = Unkey(queue.Dequeue());
            foreach (var next in new[] { Key(cx + 1, cz), Key(cx - 1, cz), Key(cx, cz + 1), Key(cx, cz - 1) })
                if (all.Contains(next) && seen.Add(next)) queue.Enqueue(next);
        }
        return seen.Count == all.Count;
    }

    /// <summary>The smallest rectangle, in metres, that holds all the cells.</summary>
    public static (double MinX, double MinZ, double MaxX, double MaxZ) Bounds(IEnumerable<long> cells)
    {
        int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
        foreach (var key in cells) { var (cx, cz) = Unkey(key); minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx); minZ = Math.Min(minZ, cz); maxZ = Math.Max(maxZ, cz); }
        return (minX * (double)CellSize, minZ * (double)CellSize, (maxX + 1) * (double)CellSize, (maxZ + 1) * (double)CellSize);
    }

    /// <summary>Rows of neighbouring cells as [cz, firstCx, lastCx]: the compact way cells travel to the client.</summary>
    public static int[][] Runs(IEnumerable<long> cells)
    {
        var rows = cells.Select(Unkey).GroupBy(c => c.Cz).OrderBy(g => g.Key);
        var runs = new List<int[]>();
        foreach (var row in rows)
        {
            var xs = row.Select(c => c.Cx).OrderBy(x => x).ToList();
            var start = xs[0]; var previous = xs[0];
            for (var i = 1; i < xs.Count; i++)
            {
                if (xs[i] == previous + 1) { previous = xs[i]; continue; }
                runs.Add([row.Key, start, previous]); start = previous = xs[i];
            }
            runs.Add([row.Key, start, previous]);
        }
        return [.. runs];
    }

    public static HashSet<long> FromRuns(IEnumerable<int[]> runs)
    {
        var cells = new HashSet<long>();
        foreach (var run in runs) for (var cx = run[1]; cx <= run[2]; cx++) { cells.Add(Key(cx, run[0])); if (cells.Count > MaxCellsPerParcel) return cells; }
        return cells;
    }

    public static double AreaOf(int cellCount) => (double)cellCount * CellArea;
}
