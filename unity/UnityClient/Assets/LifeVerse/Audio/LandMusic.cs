using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LifeVerse.Shared.Land;
using LifeVerse.Unity.World;
using UnityEngine;

namespace LifeVerse.Unity.Audio
{
    /// <summary>
    /// The music of the parcel under the person: a file the owner points to. This computer downloads it, so the address is checked again here (https, no machine inside a network, mp3 or ogg, a size limit, no redirects), and the person
    /// can turn all parcel music off. Walking into another parcel changes the music; open ground has none. A parcel whose music is not set to "start by itself" stays quiet.
    /// It also keeps the parcels that switched voice off (<see cref="VoiceOffIn"/>), and asks every ten seconds whether the land changed.
    /// </summary>
    public sealed class LandMusic : MonoBehaviour
    {
        private sealed class Tune { public Guid Parcel; public string Url, Name; public float Volume = .6f; public bool Autoplay = true; }
        private readonly Dictionary<long, Tune> _cells = new Dictionary<long, Tune>(); private readonly Dictionary<string, byte[]> _cache = new Dictionary<string, byte[]>();
        private IslandEntryClient _api; private Func<Guid> _island; private Func<Vector3?> _where; private BusSource _bus; private float _since; private string _wanted = ""; private int _request; private bool _enabled = true;

        /// <summary>Gets the file of an address (tests replace it; by default <see cref="AudioDecode.Fetch"/>).</summary>
        public Func<string, Task<byte[]>> Fetcher { get; set; }
        public string Status { get; private set; } = ""; public bool StatusFailed { get; private set; }
        public string PlayingUrl => _bus != null && _bus.Source.isPlaying ? _wanted : "";
        public string WantedUrl => _wanted;
        public bool Playing => _bus != null && _bus.Source != null && _bus.Source.clip != null && _bus.Source.isPlaying;
        public AudioSource Source => _bus?.Source;
        public int Parcels => _cells.Values.Select(t => t.Parcel).Distinct().Count();
        public Guid CurrentParcel { get; private set; }
        public event Action Changed;

        public bool Enabled { get => _enabled; set { if (_enabled == value) return; _enabled = value; if (!value) Stop(); else Evaluate(true); Changed?.Invoke(); } }

        /// <summary>The look service that knows the music of the whole island (set by the game); it plays where no parcel has music of its own.</summary>
        public IslandLookService Island { get; set; }
        /// <summary>Finds the look service when the island has been entered after this was made.</summary>
        public Func<IslandLookService> IslandFinder { get; set; }
        private IslandLookService _watching;
        public bool IslandMusicPlaying => _wantedIsIsland && Playing;
        private bool _wantedIsIsland;

        public void Setup(IslandEntryClient api, Func<Guid> island, Func<Vector3?> where) { _api = api; _island = island; _where = where; }

        private readonly Dictionary<long, string> _noVoice = new Dictionary<long, string>();
        /// <summary>The name of the parcel under the person that has voice off, or null where voice works. The island server cuts the voice there by itself; this is what the client shows and why the microphone stays closed.</summary>
        public string VoiceOffIn { get; private set; }
        /// <summary>The version of the land last read (-1: none).</summary>
        public long Version { get; private set; } = -1;
        /// <summary>How often the version of the land is asked, in seconds (0: never).</summary>
        public float PollSeconds { get; set; } = 10f;
        private float _sincePoll; private bool _polling;

        /// <summary>Asks for the land version (a few bytes) and reads the whole land again only when it changed: rules, sales and music set by others show up.</summary>
        public void Poll() { if (_api == null || _polling) return; _polling = true; StartCoroutine(WaitVersion(_api.ApiAsync("GET", "/api/v1/worlds/islands/" + _island() + "/land/version", null))); }
        private System.Collections.IEnumerator WaitVersion(Task<(int Status, string Body)> task) { while (!task.IsCompleted) yield return null; _polling = false; if (!task.IsFaulted && task.Result.Status == 200 && VersionOf(task.Result.Body) is { } v && v != Version) Load(); }

        /// <summary>The "version" of a land answer, or null.</summary>
        public static long? VersionOf(string json)
        {
            try { using var d = JsonDocument.Parse(json); return d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : (long?)null; }
            catch (JsonException) { return null; }
        }

        private void Awake() { _bus = BusSource.Make(gameObject, Bus.Music, 1f, false, 30f, true); }

        public static string RefusalText(string code) { switch (code) { case "too_long": return "O endereço da música é longo demais."; case "not_https": return "A música precisa de um endereço https."; case "bad_host": return "Esse endereço não é de um site público."; case "bad_type": return "A música precisa ser um arquivo mp3 ou ogg."; default: return "Esse endereço não pode tocar."; } }

        /// <summary>Reads the parcels of the island: for each, its cells and the music its owner set.</summary>
        public void Load() { if (_api == null) return; var task = _api.ApiAsync("GET", "/api/v1/worlds/islands/" + _island() + "/land", null); StartCoroutine(Wait(task)); }
        private System.Collections.IEnumerator Wait(Task<(int Status, string Body)> task) { while (!task.IsCompleted) yield return null; if (!task.IsFaulted && task.Result.Status == 200) ReadLand(task.Result.Body); }

        public void ReadLand(string json)
        {
            _cells.Clear(); _noVoice.Clear(); Version = VersionOf(json) ?? -1;
            try
            {
                using var document = JsonDocument.Parse(json); if (!document.RootElement.TryGetProperty("parcels", out var list) || list.ValueKind != JsonValueKind.Array) return;
                foreach (var item in list.EnumerateArray())
                {
                    if (!item.TryGetProperty("runs", out var runs)) continue;
                    if (item.TryGetProperty("allowVoice", out var voice) && voice.ValueKind == JsonValueKind.False)
                    {
                        var parcel = item.TryGetProperty("name", out var pn) && pn.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(pn.GetString()) ? pn.GetString() : "este terreno";
                        foreach (var cell in LandMath.FromRuns(runs.EnumerateArray().Select(r => r.EnumerateArray().Select(x => x.GetInt32()).ToArray()))) _noVoice[cell] = parcel;
                    }
                    if (!item.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.String) continue;
                    using var m = JsonDocument.Parse(string.IsNullOrWhiteSpace(media.GetString()) ? "{}" : media.GetString()); var root = m.RootElement; if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("musicUrl", out var url) || url.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(url.GetString())) continue;
                    var tune = new Tune { Parcel = item.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var g) ? g : Guid.Empty, Url = url.GetString(), Volume = root.TryGetProperty("volume", out var v) && v.TryGetDouble(out var vol) ? (float)Math.Max(0, Math.Min(1, vol)) : .6f, Autoplay = !(root.TryGetProperty("autoplay", out var a) && a.ValueKind == JsonValueKind.False), Name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : "" };
                    foreach (var cell in LandMath.FromRuns(runs.EnumerateArray().Select(r => r.EnumerateArray().Select(x => x.GetInt32()).ToArray()))) _cells[cell] = tune;
                }
            }
            catch (Exception exception) when (exception is JsonException || exception is KeyNotFoundException || exception is InvalidOperationException || exception is FormatException) { _cells.Clear(); _noVoice.Clear(); }
            Evaluate(false);   // the land is read again whenever it changes: the same tune goes on playing, only another one starts over
        }

        private void Update()
        {
            if (PollSeconds > 0f) { _sincePoll += Time.unscaledDeltaTime; if (_sincePoll >= PollSeconds) { _sincePoll = 0f; Poll(); } }
            _since += Time.unscaledDeltaTime; if (_since < .25f) return; _since = 0f; Evaluate(false);
        }

        /// <summary>Looks at where the person stands and plays what that parcel plays (force: look again even in the same parcel).</summary>
        public void Evaluate(bool force)
        {
            var at = _where?.Invoke(); Tune tune = null; string quiet = null; if (at.HasValue) { var key = LandMath.Key(LandMath.CellOf(at.Value.x), LandMath.CellOf(at.Value.z)); _cells.TryGetValue(key, out tune); _noVoice.TryGetValue(key, out quiet); }
            VoiceOffIn = quiet;
            CurrentParcel = tune?.Parcel ?? Guid.Empty;
            var look = Island != null ? Island : IslandFinder?.Invoke(); if (look != _watching) { if (_watching != null) _watching.MusicChanged -= OnIslandMusic; _watching = look; if (_watching != null) _watching.MusicChanged += OnIslandMusic; }
            // the parcel's own music, or else the music of the whole island; a parcel with its own music that is set not to start by itself stays quiet (it does not fall back)
            string url = ""; var volume = .6f; var name = ""; var autoplay = true; var island = false;
            if (tune != null) { url = tune.Url; volume = tune.Volume; name = tune.Name; autoplay = tune.Autoplay; }
            else if (look != null && look.MusicUrl.Length > 0) { url = look.MusicUrl; volume = look.MusicVolume; name = look.MusicName; autoplay = look.MusicAutoplay; island = true; }
            var wanted = _enabled && autoplay ? url : ""; _wantedIsIsland = island && wanted.Length > 0;
            if (!force && wanted == _wanted) { if (wanted.Length > 0 && _bus != null) _bus.Base = volume; return; }
            if (wanted.Length == 0) { Stop(); return; } Play(url, volume, island && name.Length == 0 ? "música da ilha" : name);
        }

        /// <summary>Plays an address for the person who is testing it in the management window; <see cref="EndPreview"/> goes back to what the place plays.</summary>
        public void Preview(string url, float volume) => Play(url, volume, "");
        public void EndPreview() { Stop(); Evaluate(true); }

        public void Stop() { _request++; _wanted = ""; if (_bus != null && _bus.Source != null) { _bus.Source.Stop(); _bus.Source.clip = null; } Report(_enabled ? "" : "A música dos terrenos está desligada.", false); }
        private void Report(string text, bool failed) { Status = text; StatusFailed = failed; Changed?.Invoke(); }

        private async void Play(string url, float volume, string name)
        {
            var refusal = AudioDecode.Refuse(url); if (refusal != null) { Stop(); Report(RefusalText(refusal), true); return; }
            var request = ++_request; _wanted = url; Report("Baixando a música…", false);
            byte[] bytes; if (!_cache.TryGetValue(url, out bytes)) { bytes = await (Fetcher != null ? Fetcher(url) : AudioDecode.Fetch(url)); if (bytes != null) { if (_cache.Count >= 3) _cache.Clear(); _cache[url] = bytes; } }
            if (request != _request || this == null) return;
            var clip = bytes == null ? null : await AudioDecode.DecodeAsync(bytes, "land-music"); if (request != _request || this == null) return;
            if (clip == null) { _wanted = ""; Report("Não foi possível tocar a música deste terreno.", true); return; }
            _bus.Base = volume; _bus.Source.clip = clip; _bus.Source.loop = true; _bus.Source.Play(); Report(name.Length > 0 ? "Tocando: " + name : "Tocando a música do terreno.", false);
        }

        private void OnIslandMusic() => Evaluate(true);
        private void OnDestroy() { if (_watching != null) _watching.MusicChanged -= OnIslandMusic; }

        public void ClearCache() => _cache.Clear();
    }
}
