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
        private float _since = RefreshSeconds; private bool _busy;

        private void Update()
        {
            if (Api == null || Island == Guid.Empty || _busy) return;
            _since += Time.deltaTime;
            if (_since >= RefreshSeconds) { _since = 0f; _ = RefreshAsync(); }
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
                Look = look.IsEmpty ? null : look; Refreshes++;
                if (Environment != null) Environment.SetLook(Look);
            }
            catch (JsonException) { }
            finally { _busy = false; }
        }
    }
}
