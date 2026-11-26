using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LifeVerse.Shared.Loading;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// Gets the files an island needs (3D models and pictures) before the person enters and keeps them on disk, as the Godot client's <c>AssetLoadingService</c>: the API lists them
    /// (<c>assets/manifest</c>: id, hash, size, how far from the arrival point each is used); the ones near the arrival point (the shared <see cref="AssetPlanner"/>) load before the entry with
    /// progress in bytes and the rest keep loading after it. Everything is kept by content hash: a file already on disk that still hashes right is not downloaded again, a damaged one is
    /// replaced, and a download with the wrong hash is refused and tried once more. Afterwards <see cref="Load"/> answers from the disk first, then from the API.
    /// </summary>
    public sealed class AssetPreloader
    {
        public const int Parallel = 3;
        public const long MaxCacheBytes = 512L * 1024 * 1024;
        /// <summary>"Tamanho do cache de recursos": what the folder of downloaded files may hold (the game sets it from the settings).</summary>
        public static long LimitBytes = 2048L * 1024 * 1024;
        private readonly Dictionary<Guid, AssetEntry> _known = new Dictionary<Guid, AssetEntry>();
        private readonly Func<Guid, Task<byte[]>> _download; private readonly string _folder;
        public long DoneBytes { get; private set; }
        public long TotalBytes { get; private set; }
        public int CacheHits { get; private set; }
        public int Downloads { get; private set; }
        public int Refused { get; private set; }
        public string LastError { get; private set; }
        public bool BackgroundRunning { get; private set; }
        public AssetPlanResult Plan { get; private set; }

        public AssetPreloader(string folder, Func<Guid, Task<byte[]>> download) { _folder = folder; _download = download; }

        /// <summary>The island's list: <c>{"assets":[{"assetId","kind","hash","size","mime","distance"}]}</c>.</summary>
        public static List<AssetEntry> ParseManifest(string json)
        {
            var list = new List<AssetEntry>();
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return list;
            foreach (var item in assets.EnumerateArray())
            {
                if (!item.TryGetProperty("assetId", out var id) || !Guid.TryParse(id.GetString(), out var asset)) continue;
                string S(string n) => item.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var size = item.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0; var distance = item.TryGetProperty("distance", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
                list.Add(new AssetEntry(asset, S("kind") == "model" ? AssetKind.Model : AssetKind.Texture, S("hash"), size, S("mime"), distance));
            }
            return list;
        }

        /// <summary>The query of the manifest for an arrival point ("" when there is none).</summary>
        public static string ManifestPath(Guid island, float? x, float? z) => "/api/v1/worlds/islands/" + island + "/assets/manifest" + (x is { } px && z is { } pz ? "?x=" + px.ToString(System.Globalization.CultureInfo.InvariantCulture) + "&z=" + pz.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");

        private string PathFor(string hash) => Path.Combine(_folder, hash.ToLowerInvariant() + ".bin");

        /// <summary>The bytes kept for a hash, when they are there and still right (a damaged file is deleted).</summary>
        public byte[] TryRead(string hash)
        {
            if (string.IsNullOrEmpty(hash)) return null; var path = PathFor(hash);
            try
            {
                if (!File.Exists(path)) return null; var bytes = File.ReadAllBytes(path);
                if (bytes.Length > 0 && AssetPlanner.HashMatches(hash, bytes)) { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); return bytes; }
                File.Delete(path);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            return null;
        }

        /// <summary>Keeps bytes under their hash; a half-written file never has the final name.</summary>
        public void Write(string hash, byte[] bytes)
        {
            try { Directory.CreateDirectory(_folder); var path = PathFor(hash); var temp = path + ".part"; File.WriteAllBytes(temp, bytes); if (File.Exists(path)) File.Delete(path); File.Move(temp, path); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        /// <summary>Deletes the oldest files when the folder is over <paramref name="limit"/> (down to 80 %).</summary>
        public static int Prune(string folder, long limit = MaxCacheBytes)
        {
            var removed = 0;
            try
            {
                if (!Directory.Exists(folder)) return 0; var files = new DirectoryInfo(folder).GetFiles("*.bin").ToList(); var total = files.Sum(f => f.Length); if (total <= limit) return 0;
                foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc)) { total -= file.Length; file.Delete(); removed++; if (total <= limit * .8) break; }
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            return removed;
        }

        /// <summary>Reads the list and loads what is near the arrival point. False only when an essential file could not be had; without a list (an older server) it answers true and nothing is preloaded.</summary>
        public async Task<bool> Essentials(int status, string manifest)
        {
            _known.Clear(); DoneBytes = 0; TotalBytes = 0; LastError = null; Plan = null;
            if (status == 404) return true;
            List<AssetEntry> entries;
            try { entries = status >= 200 && status < 300 ? ParseManifest(manifest) : null; } catch (JsonException) { entries = null; }
            if (entries == null) { LastError = "Não foi possível obter a lista de recursos da ilha."; return true; }   // the island can still be entered: the files load when an object needs them
            foreach (var entry in entries) _known[entry.AssetId] = entry;
            Plan = AssetPlanner.Plan(entries); TotalBytes = Plan.EssentialBytes;
            return await Run(Plan.Essential, true);
        }

        /// <summary>The far files, after the entry (one that fails is fetched when an object needs it).</summary>
        public async Task Rest() { if (Plan == null || Plan.Deferred.Count == 0) return; BackgroundRunning = true; try { await Run(Plan.Deferred, false); } finally { BackgroundRunning = false; } }

        private async Task<bool> Run(IReadOnlyList<AssetEntry> entries, bool essential)
        {
            var next = 0; var failed = false;
            async Task Worker()
            {
                while (!failed)
                {
                    int index; lock (_known) index = next++; if (index >= entries.Count) return;
                    var entry = entries[index]; var bytes = await Ensure(entry);
                    if (bytes == null && essential) { failed = true; LastError = "Não foi possível baixar " + (entry.Kind == AssetKind.Model ? "um modelo 3D" : "uma imagem") + " da ilha (" + entry.AssetId.ToString().Substring(0, 8) + ")."; return; }
                    if (essential) lock (_known) DoneBytes += entry.Size;
                }
            }
            await Task.WhenAll(Enumerable.Range(0, Parallel).Select(_ => Worker()));
            return !failed;
        }

        private async Task<byte[]> Ensure(AssetEntry entry)
        {
            var kept = TryRead(entry.Hash); if (kept != null) { CacheHits++; return kept; }
            for (var attempt = 0; attempt < 2; attempt++)
            {
                byte[] bytes; try { bytes = await _download(entry.AssetId); } catch (Exception exception) when (!(exception is OutOfMemoryException)) { bytes = null; }
                if (bytes == null || bytes.Length == 0) continue;
                if (!string.IsNullOrEmpty(entry.Hash) && !AssetPlanner.HashMatches(entry.Hash, bytes)) { Refused++; continue; }
                Downloads++; if (!string.IsNullOrEmpty(entry.Hash)) Write(entry.Hash, bytes); return bytes;
            }
            return null;
        }

        /// <summary>The loader the world uses after the entry: a file the island announced comes from the disk when it is there and right; otherwise from the API (and is kept when it is right).</summary>
        public async Task<byte[]> Load(Guid asset)
        {
            if (_known.TryGetValue(asset, out var entry))
            {
                var kept = TryRead(entry.Hash); if (kept != null) { CacheHits++; return kept; }
                var bytes = await _download(asset); if (bytes != null && !string.IsNullOrEmpty(entry.Hash) && AssetPlanner.HashMatches(entry.Hash, bytes)) Write(entry.Hash, bytes); return bytes;
            }
            return await _download(asset);
        }

        /// <summary>The words for the loading screen ("Baixando os recursos da ilha… 3,2 de 10 MB").</summary>
        public static string Describe(long done, long total) => total <= 0 ? "Conferindo os recursos da ilha…" : "Baixando os recursos da ilha… " + (done / 1048576d).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + " de " + (total / 1048576d).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + " MB";
    }
}
