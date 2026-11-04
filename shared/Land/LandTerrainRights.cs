using System.Text.Json;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.Shared.Land;

/// <summary>
/// Who may reshape the ground where, as the island server needs it: every parcel with the people who may edit its terrain (the owner, the
/// tenant and the builders list). Island administrators are not in it; they may edit anywhere. A ground vertex may change only when every
/// parcel cell around it belongs to a parcel its editor may change, so an edit can never reach across a boundary and move the edge of a
/// neighbour's land, and ground that belongs to no parcel is left to the administrators.
/// </summary>
public sealed class LandTerrainRights
{
    private readonly Dictionary<long, HashSet<Guid>> _editorsByCell = [];
    private readonly HashSet<Guid> _anyone = [];

    public static LandTerrainRights Empty { get; } = new();
    public int Cells => _editorsByCell.Count;

    /// <summary>Reads the "terrain" part of the API's answer: { parcels: [{ id, editors: [...], runs: [[cz, x0, x1]] }] }.</summary>
    public static LandTerrainRights Parse(JsonElement terrain)
    {
        var rights = new LandTerrainRights();
        if (terrain.ValueKind != JsonValueKind.Object || !terrain.TryGetProperty("parcels", out var parcels) || parcels.ValueKind != JsonValueKind.Array) return rights;
        foreach (var item in parcels.EnumerateArray())
        {
            try
            {
                var editors = new HashSet<Guid>();
                foreach (var editor in item.GetProperty("editors").EnumerateArray()) if (Guid.TryParse(editor.GetString(), out var id)) { editors.Add(id); rights._anyone.Add(id); }
                if (editors.Count == 0) { foreach (var cell in LandMath.FromRuns(item.GetProperty("runs").EnumerateArray().Select(r => r.EnumerateArray().Select(n => n.GetInt32()).ToArray()))) rights._editorsByCell[cell] = editors; continue; }
                foreach (var cell in LandMath.FromRuns(item.GetProperty("runs").EnumerateArray().Select(r => r.EnumerateArray().Select(n => n.GetInt32()).ToArray()))) rights._editorsByCell[cell] = editors;
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException) { /* a damaged entry is skipped */ }
        }
        return rights;
    }

    /// <summary>The rights of one person from the cells of the parcels they may edit (what the client knows about itself).</summary>
    public static LandTerrainRights FromCells(IEnumerable<IEnumerable<long>> parcels, Guid user)
    {
        var rights = new LandTerrainRights(); var me = new HashSet<Guid> { user };
        foreach (var cells in parcels) foreach (var cell in cells) { rights._editorsByCell[cell] = me; rights._anyone.Add(user); }
        return rights;
    }

    /// <summary>True when this person may edit the terrain of at least one parcel.</summary>
    public bool HasAny(Guid user) => _anyone.Contains(user);

    private bool CellAllows(Guid user, double x, double z) => _editorsByCell.TryGetValue(LandMath.Key(LandMath.CellOf(x), LandMath.CellOf(z)), out var editors) && editors.Contains(user);

    /// <summary>May this person change the ground at this world position? Every parcel cell that touches the point has to allow it.</summary>
    public bool Allows(Guid user, double worldX, double worldZ)
    {
        const double edge = 0.001;
        foreach (var x in new[] { worldX - edge, worldX + edge })
            foreach (var z in new[] { worldZ - edge, worldZ + edge })
                if (!CellAllows(user, x, z)) return false;
        return true;
    }

    /// <summary>The test the terrain brush uses: whether the vertex at grid position (x, z) may change.</summary>
    public Func<int, int, bool> MaskFor(Guid user, TerrainMap map)
    {
        var half = map.Cells / 2f; var size = map.CellSize;
        return (x, z) => Allows(user, (x - half) * size, (z - half) * size);
    }
}
