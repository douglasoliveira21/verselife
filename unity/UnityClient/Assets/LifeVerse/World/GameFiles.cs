using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// "Verificar arquivos do jogo": the build writes a list of its files with their SHA-256 (<see cref="Manifest"/>), and the game checks them against it; the files it downloaded
    /// are kept under their hash, so the damaged ones are found the same way and deleted (they are downloaded again when needed).
    /// </summary>
    public static class GameFiles
    {
        public const string Manifest = "integrity.json";

        private static string HashOf(string path)
        {
            using var sha = SHA256.Create(); using var stream = File.OpenRead(path); return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static string Relative(string folder, string path) => path.Substring(Path.GetFullPath(folder).TrimEnd('\\', '/').Length + 1).Replace('\\', '/');

        /// <summary>Writes the list of the files of a build; returns how many it holds.</summary>
        public static int Write(string folder)
        {
            var files = Directory.GetFiles(Path.GetFullPath(folder), "*", SearchOption.AllDirectories).Select(p => (Full: p, Name: Relative(folder, p))).Where(f => f.Name != Manifest).OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
            var list = files.ToDictionary(f => f.Name, f => HashOf(f.Full));
            File.WriteAllText(Path.Combine(folder, Manifest), JsonSerializer.Serialize(list, new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() })); return list.Count;
        }

        /// <summary>Checks a folder against its list: how many files were checked and which are missing or changed. Null when there is no list (or it cannot be read).</summary>
        public static (int Checked, List<string> Bad)? Verify(string folder)
        {
            var path = Path.Combine(folder, Manifest); if (!File.Exists(path)) return null;
            var list = new List<(string Name, string Hash)>();
            try { using var d = JsonDocument.Parse(File.ReadAllText(path)); foreach (var p in d.RootElement.EnumerateObject()) list.Add((p.Name, p.Value.GetString() ?? "")); }
            catch (Exception e) when (e is JsonException || e is IOException || e is InvalidOperationException) { return null; }
            var bad = new List<string>();
            foreach (var (name, hash) in list)
            {
                if (name.Contains("..") || Path.IsPathRooted(name)) { bad.Add(name); continue; }
                var file = Path.Combine(folder, name);
                try { if (!File.Exists(file) || !string.Equals(HashOf(file), hash, StringComparison.OrdinalIgnoreCase)) bad.Add(name); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { bad.Add(name); }
            }
            return (list.Count, bad);
        }

        /// <summary>Checks the downloaded files (each named by its hash) and deletes the damaged ones: how many were checked and how many went.</summary>
        public static (int Checked, int Removed) VerifyCache(string folder)
        {
            if (!Directory.Exists(folder)) return (0, 0); int seen = 0, removed = 0;
            foreach (var file in Directory.GetFiles(folder, "*.bin"))
            {
                seen++;
                try { if (!string.Equals(HashOf(file), Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)) { File.Delete(file); removed++; } }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
            return (seen, removed);
        }

        /// <summary>What the check found, in words.</summary>
        public static string Words((int Checked, List<string> Bad)? game, (int Checked, int Removed) cache)
        {
            var first = game is { } g ? g.Bad.Count == 0 ? g.Checked + " arquivos do jogo conferidos, todos íntegros." : g.Bad.Count + " arquivo(s) do jogo com problema (" + string.Join(", ", g.Bad.Take(3)) + (g.Bad.Count > 3 ? "…" : "") + "): instale o jogo de novo."
                : "Esta instalação não tem a lista de arquivos do jogo para conferir.";
            return first + " Downloads guardados: " + cache.Checked + " conferidos" + (cache.Removed > 0 ? ", " + cache.Removed + " estragado(s) apagado(s)." : ".");
        }
    }
}
