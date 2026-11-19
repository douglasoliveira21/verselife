using System.Collections.Generic;
using LifeVerse.Shared.Networking;
using LifeVerse.Unity.World;
using LifeVerse.Wire;
using UnityEngine;

namespace LifeVerse.Unity.Avatar
{
    /// <summary>
    /// One <see cref="AvatarView"/> for every person the server tells about: it creates a body when someone spawns (inside the interest radius), moves it with the snapshots
    /// and removes it when they leave or go out of range. Gestures (wave, dance) arrive as motion states in the snapshots, so nothing else is needed for them here.
    /// The body of the local player is kept too (it is placed by <see cref="LocalPlayer"/>, not by the snapshots).
    /// </summary>
    public sealed class PlayerViews : MonoBehaviour
    {
        public WorldClient Client;
        private readonly Dictionary<int, AvatarView> _views = new Dictionary<int, AvatarView>();
        private readonly Dictionary<int, PlayerSnapshot> _latest = new Dictionary<int, PlayerSnapshot>();

        public int Count => _views.Count;
        public AvatarView ViewOf(int peerId) => _views.TryGetValue(peerId, out var view) ? view : null;
        public IReadOnlyCollection<int> Peers => _views.Keys;
        public bool TryGetSnapshot(int peerId, out PlayerSnapshot snapshot) => _latest.TryGetValue(peerId, out snapshot);

        public void Attach(WorldClient client)
        {
            Client = client;
            client.PlayerSpawned += OnSpawn; client.PlayerDespawned += OnDespawn; client.SnapshotReceived += OnSnapshot;
        }

        private static Color ColorOf(string name)
        {
            var hash = 17; foreach (var c in name) hash = hash * 31 + c; var hue = Mathf.Abs(hash % 360) / 360f; return Color.HSVToRGB(hue, .55f, .8f);
        }

        private void OnSpawn(SpawnPlayerMessage spawn)
        {
            if (_views.TryGetValue(spawn.PeerId, out var existing)) { existing.PlaceAt(new Vector3(spawn.X, spawn.Y, spawn.Z)); return; }
            var view = AvatarView.Create(spawn.PeerId, spawn.DisplayName, ColorOf(spawn.DisplayName)); view.transform.SetParent(transform, false);
            view.PlaceAt(new Vector3(spawn.X, spawn.Y, spawn.Z)); view.External = spawn.PeerId == Client.SelfPeerId && SelfIsExternal; _views[spawn.PeerId] = view;
        }

        /// <summary>True when the local player's prediction places its own body (set by <see cref="LocalPlayer"/>).</summary>
        public bool SelfIsExternal;

        private void OnDespawn(DespawnPlayerMessage gone)
        {
            if (!_views.TryGetValue(gone.PeerId, out var view)) return;
            Destroy(view.gameObject); _views.Remove(gone.PeerId); _latest.Remove(gone.PeerId);
        }

        private void OnSnapshot(WorldSnapshot snapshot)
        {
            foreach (var player in snapshot.Players)
            {
                _latest[player.PeerId] = player;
                if (!_views.TryGetValue(player.PeerId, out var view)) continue;
                if (player.PeerId == Client.SelfPeerId && view.External) continue;
                view.ApplySnapshot(player);
            }
        }

        private void OnDestroy() { if (Client != null) { Client.PlayerSpawned -= OnSpawn; Client.PlayerDespawned -= OnDespawn; Client.SnapshotReceived -= OnSnapshot; } }
    }
}
