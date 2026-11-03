using System.IO.Compression;

namespace LifeVerse.Shared.Terrain;

/// <summary>
/// A set of vertices with the height and texture weights they had (or should get). Used for undo/redo: capturing a
/// region before an edit gives the patch that undoes it, and restoring a patch returns the patch that redoes it.
/// </summary>
public sealed class TerrainPatch
{
    private readonly Dictionary<int, (float Height, byte[] Splat)> _entries = [];

    public int Count => _entries.Count;
    public IEnumerable<KeyValuePair<int, (float Height, byte[] Splat)>> Entries => _entries;

    public void Add(int index, float height, byte[] splat) => _entries.TryAdd(index, (height, splat));

    /// <summary>Adds the other patch's vertices, keeping the values already here (the oldest state wins).</summary>
    public void MergeKeepingOldest(TerrainPatch other)
    {
        foreach (var entry in other._entries) _entries.TryAdd(entry.Key, entry.Value);
    }

    public byte[] Serialize()
    {
        using var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, System.Text.Encoding.UTF8, true))
        {
            writer.Write(_entries.Count);
            foreach (var (index, (height, splat)) in _entries) { writer.Write(index); writer.Write(height); writer.Write(splat); }
        }
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.Fastest, true)) raw.WriteTo(deflate);
        return packed.ToArray();
    }

    public static bool TryDeserialize(byte[] data, int vertexCount, out TerrainPatch patch)
    {
        patch = new TerrainPatch();
        if (data.Length is < 4 or > 4 * 1024 * 1024) return false;
        try
        {
            using var deflate = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress);
            using var reader = new BinaryReader(deflate);
            var count = reader.ReadInt32();
            if (count < 0 || count > vertexCount) return false;
            var loaded = new TerrainPatch();
            for (var i = 0; i < count; i++)
            {
                var index = reader.ReadInt32(); var height = reader.ReadSingle(); var splat = reader.ReadBytes(TerrainMap.LayerCount);
                if (index < 0 || index >= vertexCount || splat.Length != TerrainMap.LayerCount || !float.IsFinite(height)) return false;
                loaded._entries[index] = (height, splat);
            }
            patch = loaded;
            return true;
        }
        catch (Exception exception) when (exception is EndOfStreamException or InvalidDataException or IOException) { return false; }
    }
}
