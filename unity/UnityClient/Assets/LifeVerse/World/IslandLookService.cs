using System;
using System.Text.Json;
using System.Threading.Tasks;
using LifeVerse.Shared.Land;
using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// Keeps the look of the island up to date (hour, weather, fog, light and sky tint, set by the owner in the management window): it asks the API when the person enters
    /// an island and every half minute after, and gives it to <see cref="IslandEnvironment"/>, which fades it in. If the API cannot be reached the last look stays.
    /// </summary>
    public sealed class IslandLookService : MonoBehaviour
    {
        public const float RefreshSeconds = 30f;

        public IslandEnvironment Environment;
        public IslandEntryClient Api;
        public Guid Island;
        public ParcelEnvironment Look { get; private set; }
        public int Refreshes { get; private set; }
        /// <summary>The music of the whole island (what plays where no parcel sets its own): the address, the volume, whether it starts by itself and its name. Empty when none is set or the address is not allowed.</summary>
        public string MusicUrl { get; private set; } = ""; public float MusicVolume { get; private set; } = 1f; public bool MusicAutoplay { get; private set; } = true; public string MusicName { get; private set; } = "";
        public event Action MusicChanged;
        private float _since = RefreshSeconds; private bool _busy;

        private void Update()
        {
            if (Api == null || Island == Guid.Empty || _busy) return;
            _since += Time.deltaTime;
            if (_since >= RefreshSeconds) { _since = 0f; _ = RefreshAsync(); }
        }

        /// <summary>Reads the "media" part of the answer (musicUrl, volume, autoplay, name). An address the rules refuse (not https, a machine inside a network, not mp3 or ogg) counts as no music.</summary>
        public void ReadMusic(JsonElement root)
        {
            string url = ""; var volume = 1f; var autoplay = true; var name = "";
            if (root.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object && media.TryGetProperty("musicUrl", out var u) && u.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(u.GetString()) && LandMediaUrl.Check(u.GetString()) == null)
            {
                url = u.GetString(); if (media.TryGetProperty("volume", out var v) && v.TryGetSingle(out var vol)) volume = Mathf.Clamp01(vol); autoplay = !(media.TryGetProperty("autoplay", out var a) && a.ValueKind == JsonValueKind.False); if (media.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) name = n.GetString() ?? "";
            }
            if (url == MusicUrl && Mathf.Approximately(volume, MusicVolume) && autoplay == MusicAutoplay && name == MusicName) return;
            MusicUrl = url; MusicVolume = volume; MusicAutoplay = autoplay; MusicName = name; MusicChanged?.Invoke();
        }

        /// <summary>Asks the API for the look now. A failure keeps what was there.</summary>
        public async Task RefreshAsync()
        {
            if (Api == null || Island == Guid.Empty || _busy) return;
            _busy = true;
            try
            {
                var body = await Api.GetEnvironmentAsync(Island);
                if (body == null) return;
                using var document = JsonDocument.Parse(body);
                var look = document.RootElement.TryGetProperty("look", out var element) && element.ValueKind == JsonValueKind.Object ? ParcelEnvironment.Parse(element.GetRawText()) : new ParcelEnvironment();
                Look = look.IsEmpty ? null : look; Refreshes++; ReadMusic(document.RootElement);
                if (Environment != null) Environment.SetLook(Look);
            }
            catch (JsonException) { }
            finally { _busy = false; }
        }
    }
}
