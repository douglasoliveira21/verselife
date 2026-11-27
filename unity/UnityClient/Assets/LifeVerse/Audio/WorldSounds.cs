using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LifeVerse.Unity.World;
using UnityEngine;

namespace LifeVerse.Unity.Audio
{
    /// <summary>
    /// The sounds that scripts of objects make, announced by the island: a sound file of the island played at an object ("sound", with its volume and whether it loops, and "sound_stop"), and sound from the internet ("media": an mp3 or ogg
    /// on an https address that is downloaded here, under the same rules as the island's, and played at the object so it fades with distance; the island repeats the message when the object moves or the volume changes, and says "off"
    /// when the script stops it). When a sound that does not loop ends, this client tells the island, which has to know.
    /// </summary>
    public sealed class WorldSounds : MonoBehaviour
    {
        public const int MaxSounds = 16, MaxStreams = 8, MaxDownloads = 3;
        private sealed class Played { public GameObject Host; public BusSource Bus; public float Started, Length; public bool Loop; public string Key, PlayId; public bool Media; public Guid Object; public bool Loading; }

        private WorldClient _client; private readonly Dictionary<Guid, Played> _sounds = new Dictionary<Guid, Played>(); private readonly Dictionary<string, Played> _streams = new Dictionary<string, Played>(); private readonly Dictionary<Guid, AudioClip> _clips = new Dictionary<Guid, AudioClip>(); private int _downloads;
        /// <summary>Gets the bytes of a sound file of the island by its identifier. Set by the game; without it island sounds are skipped.</summary>
        public Func<Guid, Task<byte[]>> AssetLoader { get; set; }
        /// <summary>Gets the bytes of a file from the internet. Tests replace it; by default it is <see cref="AudioDecode.Fetch"/> (https only).</summary>
        public Func<string, Task<byte[]>> MediaFetch { get; set; }
        public Action<string> Report { get; set; }
        /// <summary>A sound started playing: whether it is a stream of the internet, and where (Unity space). For the captions.</summary>
        public event Action<bool, Vector3> Playing;
        public int ActiveSounds => _sounds.Count(p => p.Value.Bus != null);
        public int ActiveStreams => _streams.Count(p => p.Value.Bus != null);
        public int Started { get; private set; }
        public int Refused { get; private set; }
        public AudioSource SoundOf(Guid id) => _sounds.TryGetValue(id, out var p) && p.Bus != null ? p.Bus.Source : null;
        public AudioSource StreamOf(string key) => _streams.TryGetValue(key, out var p) && p.Bus != null ? p.Bus.Source : null;
        public string StreamPlayId(string key) => _streams.TryGetValue(key, out var p) ? p.PlayId : "";

        public void Attach(WorldClient client)
        {
            if (_client != null) _client.BuildOperation -= OnOperation; _client = client; if (client != null) { client.BuildOperation += OnOperation; Report = json => client.SendScriptResponse(json); }
        }

        private void OnDestroy() { if (_client != null) _client.BuildOperation -= OnOperation; }

        public void Clear() { foreach (var p in _sounds.Values) Drop(p); foreach (var p in _streams.Values) Drop(p); _sounds.Clear(); _streams.Clear(); }
        private static void Drop(Played p) { if (p.Host != null) Destroy(p.Host); }

        /// <summary>One operation of the island (public so tests feed the same JSON).</summary>
        public void OnOperation(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json); var root = document.RootElement; var operation = root.TryGetProperty("operation", out var op) ? op.GetString() : null;
                if (operation == "sound") OnSound(root); else if (operation == "sound_stop") { if (root.TryGetProperty("objectId", out var id) && Guid.TryParse(id.GetString(), out var objectId)) StopSound(objectId); } else if (operation == "media") OnMedia(root);
            }
            catch (Exception exception) when (exception is JsonException || exception is KeyNotFoundException || exception is InvalidOperationException || exception is FormatException) { }
        }

        private static double Number(JsonElement e, string name, double fallback, double min, double max) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && !double.IsNaN(n) && !double.IsInfinity(n) ? Math.Max(min, Math.Min(max, n)) : fallback;
        private static Vector3 PositionOf(JsonElement e) => GameSpace.ToUnity((float)Number(e, "x", 0, -10000, 10000), (float)Number(e, "y", 0, -10000, 10000), (float)Number(e, "z", 0, -10000, 10000));

        // ---- sound files of the island ----

        private void StopSound(Guid id) { if (_sounds.TryGetValue(id, out var p)) { Drop(p); _sounds.Remove(id); } }

        private async void OnSound(JsonElement root)
        {
            if (!root.TryGetProperty("assetId", out var a) || !Guid.TryParse(a.GetString(), out var asset)) return; var objectId = root.TryGetProperty("objectId", out var o) && Guid.TryParse(o.GetString(), out var oid) ? oid : Guid.NewGuid();
            var volume = (float)Number(root, "volume", 1, 0, 1); var loop = root.TryGetProperty("loop", out var l) && l.ValueKind == JsonValueKind.True; var position = PositionOf(root);
            if (_sounds.TryGetValue(objectId, out var running) && running.Bus != null && running.Loop) { running.Host.transform.position = position; running.Bus.Base = volume; return; }
            StopSound(objectId); if (_sounds.Count >= MaxSounds) { Refused++; return; }
            var claim = new Played { Object = objectId, Loop = loop, Loading = true }; _sounds[objectId] = claim;
            var clip = await ClipOf(asset); if (!_sounds.TryGetValue(objectId, out var now) || now != claim) return;   // stopped or replaced while it loaded
            if (clip == null) { _sounds.Remove(objectId); Refused++; return; }
            Begin(claim, clip, "Sound_" + objectId.ToString("N").Substring(0, 8), Bus.Effects, volume, position, 30f, loop);
        }

        private async Task<AudioClip> ClipOf(Guid asset)
        {
            if (_clips.TryGetValue(asset, out var known) && known != null) return known; if (AssetLoader == null) return null;
            var bytes = await AssetLoader(asset); if (bytes == null) return null; var clip = await AudioDecode.DecodeAsync(bytes, "island-" + asset.ToString("N").Substring(0, 8)); if (clip != null) { if (_clips.Count >= 24) _clips.Clear(); _clips[asset] = clip; }
            return clip;
        }

        private void Begin(Played p, AudioClip clip, string name, Bus bus, float volume, Vector3 position, float range, bool loop)
        {
            p.Host = new GameObject(name); p.Host.transform.SetParent(transform, false); p.Host.transform.position = position; p.Bus = BusSource.Make(p.Host, bus, volume, true, range, loop); p.Bus.Source.clip = clip; p.Bus.Source.Play();
            p.Started = Time.realtimeSinceStartup; p.Length = clip.length; p.Loop = loop; p.Loading = false; Started++; Playing?.Invoke(bus == Bus.Media, position);
        }

        // ---- sound from the internet ----

        private void StopStream(string key) { if (_streams.TryGetValue(key, out var p)) { Drop(p); _streams.Remove(key); } }

        private async void OnMedia(JsonElement root)
        {
            var key = root.TryGetProperty("key", out var k) ? k.GetString() ?? "" : ""; if (key.Length == 0 || key.Length > 40) return;
            if (!(root.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.True)) { StopStream(key); return; }
            var url = root.TryGetProperty("url", out var u) ? u.GetString() ?? "" : ""; var playId = root.TryGetProperty("playId", out var pid) ? pid.GetString() ?? "" : ""; if (playId.Length == 0 || playId.Length > 64 || AudioDecode.Refuse(url) != null) { Refused++; return; }
            var volume = (float)Number(root, "volume", .8, 0, 1); var range = (float)Number(root, "range", 25, 3, 60); var loop = root.TryGetProperty("loop", out var l) && l.ValueKind == JsonValueKind.True; var position = PositionOf(root);
            if (_streams.TryGetValue(key, out var current) && current.PlayId == playId) { if (current.Bus != null) { current.Host.transform.position = position; current.Bus.Base = volume; current.Bus.Source.maxDistance = range; } return; }   // the same play: it moved or changed volume
            StopStream(key); if (_streams.Count >= MaxStreams || _downloads >= MaxDownloads) { Refused++; return; }
            var claim = new Played { Key = key, PlayId = playId, Loop = loop, Media = true, Loading = true }; _streams[key] = claim; _downloads++;
            byte[] bytes; try { bytes = await (MediaFetch != null ? MediaFetch(url) : AudioDecode.Fetch(url)); } finally { _downloads--; }
            if (!_streams.TryGetValue(key, out var now) || now != claim) return;
            var clip = bytes == null ? null : await AudioDecode.DecodeAsync(bytes, "media-" + key); if (!_streams.TryGetValue(key, out now) || now != claim) { if (clip != null) Destroy(clip); return; }
            if (clip == null) { _streams.Remove(key); Refused++; return; }
            Begin(claim, clip, "Media_" + key, Bus.Media, volume, position, range, loop);
        }

        /// <summary>The sounds playing now: whether each is a stream, and where.</summary>
        public IEnumerable<(bool Media, Vector3 Position)> Audible() => _sounds.Values.Concat(_streams.Values).Where(p => p.Bus != null && p.Host != null).Select(p => (p.Media, p.Host.transform.position));

        private void Update()
        {
            var now = Time.realtimeSinceStartup;
            foreach (var pair in _sounds.Where(x => x.Value.Bus != null && !x.Value.Loop && now - x.Value.Started > x.Value.Length + .15f).ToList())
            { Drop(pair.Value); _sounds.Remove(pair.Key); Report?.Invoke(JsonSerializer.Serialize(new { sound_finished = true, objectId = pair.Key })); }
            foreach (var pair in _streams.Where(x => x.Value.Bus != null && !x.Value.Loop && now - x.Value.Started > x.Value.Length + .15f).ToList())
            { Drop(pair.Value); _streams.Remove(pair.Key); Report?.Invoke(JsonSerializer.Serialize(new { media_finished = true, key = pair.Key, playId = pair.Value.PlayId })); }
        }
    }
}
