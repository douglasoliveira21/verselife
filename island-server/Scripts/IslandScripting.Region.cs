using LifeVerse.Scripting;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>Changes of the island as a whole that scripts may care about (the sea level, the shape of the terrain): announced to the scripts that handle on_region_changed, at most once every two seconds however many edits there were.</summary>
public sealed partial class IslandScripting
{
    public const double RegionChangeEvery = 2;
    private readonly List<string> _pendingRegion = [];
    private double _lastRegionEvent = double.NegativeInfinity;

    /// <summary>The current sea level, from the terrain. Set by the server.</summary>
    public Func<float>? SeaLevelOf { get; set; }

    /// <summary>Something about the island changed (kind: "sea_level" or "terrain"). The scripts hear it on the next tick that is at least two seconds after the last announcement.</summary>
    public void RegionChanged(string kind) { if (!_pendingRegion.Contains(kind)) _pendingRegion.Add(kind); }

    private void FlushRegionChanges(double now)
    {
        if (_pendingRegion.Count == 0 || now - _lastRegionEvent < RegionChangeEvery) return;
        _lastRegionEvent = now; var kinds = _pendingRegion.ToList(); _pendingRegion.Clear();
        _host.RegionChanged(kinds, SeaLevelOf?.Invoke() ?? 0f);
    }
}
