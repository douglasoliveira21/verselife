using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Networking;
using LifeVerse.Unity.World;
using LifeVerse.Wire;
using UnityEngine;

namespace LifeVerse.Unity.Avatar
{
    /// <summary>
    /// One <see cref="AvatarView"/> for every person the server tells about: it creates a body when someone spawns (inside the interest radius), dresses it from the appearance the
    /// server sends (and again when it changes), moves it with the snapshots, leans the head as far as the person looks, plays the gestures others make, and removes it when they
    /// leave or go out of range. With a custom body (an asset id in the appearance) it fetches the GLB through <see cref="AssetLoader"/> and draws that skeleton instead.
    /// Twice a second the shared <see cref="AvatarDetailPlanner"/> decides how much of each is drawn from the distance and a triangle budget: the person's own is always whole, the
    /// ones far away are the simple body or not drawn at all. The body of the local player is kept too (placed by <see cref="LocalPlayer"/>).
    /// </summary>
    public sealed class PlayerViews : MonoBehaviour
    {
        public WorldClient Client;
        /// <summary>Gets the bytes of an asset of the island (a custom body). Set by <see cref="WorldJoin"/>; without it custom bodies are skipped and the simple body stays.</summary>
        public Func<Guid, Task<byte[]>> AssetLoader;
        public DetailBudget Budget = new DetailBudget();
        public Camera Camera;
        /// <summary>When false the detail is not planned by itself (tests call <see cref="PlanDetail"/>).</summary>
        public bool AutoPlan = true;
        private readonly Dictionary<int, AvatarView> _views = new Dictionary<int, AvatarView>();
        private readonly Dictionary<int, PlayerSnapshot> _latest = new Dictionary<int, PlayerSnapshot>();
        private readonly Dictionary<int, int> _bodyGeneration = new Dictionary<int, int>();
        private float _planClock;

        public int Count => _views.Count;
        public int CustomBodiesLoaded { get; private set; }
        public AvatarView ViewOf(int peerId) => _views.TryGetValue(peerId, out var view) ? view : null;
        public IReadOnlyCollection<int> Peers => _views.Keys;
        public bool TryGetSnapshot(int peerId, out PlayerSnapshot snapshot) => _latest.TryGetValue(peerId, out snapshot);

        public void Attach(WorldClient client)
        {
            Client = client;
            client.PlayerSpawned += OnSpawn; client.PlayerDespawned += OnDespawn; client.SnapshotReceived += OnSnapshot; client.GestureChanged += OnGesture;
            client.AppearanceChanged += OnAppearanceChanged; client.HeadLookChanged += OnHeadLook;
        }

        private void OnGesture(GestureChangedMessage gesture) { if (_views.TryGetValue(gesture.PeerId, out var view)) view.PlayGesture(gesture.GestureId, ExtraGestures.Seconds); }
        private void OnHeadLook(HeadLookChangedMessage look) { if (_views.TryGetValue(look.PeerId, out var view)) view.ApplyHeadLook(look.Pitch); }
        private void OnAppearanceChanged(AppearanceChangedMessage changed) => ApplyAppearance(changed.PeerId, changed.AppearanceJson);

        private static Color ColorOf(string name)
        {
            var hash = 17; foreach (var c in name) hash = hash * 31 + c; var hue = Mathf.Abs(hash % 360) / 360f; return Color.HSVToRGB(hue, .55f, .8f);
        }

        private void OnSpawn(SpawnPlayerMessage spawn)
        {
            if (_views.TryGetValue(spawn.PeerId, out var existing)) { existing.PlaceAt(new Vector3(spawn.X, spawn.Y, spawn.Z)); return; }
            var view = AvatarView.Create(spawn.PeerId, spawn.DisplayName, ColorOf(spawn.DisplayName)); view.transform.SetParent(transform, false);
            view.PlaceAt(new Vector3(spawn.X, spawn.Y, spawn.Z)); view.External = spawn.PeerId == Client.SelfPeerId && SelfIsExternal; _views[spawn.PeerId] = view;
            if (!string.IsNullOrWhiteSpace(spawn.AppearanceJson) && spawn.AppearanceJson != "{}") ApplyAppearance(spawn.PeerId, spawn.AppearanceJson);
        }

        /// <summary>True when the local player's prediction places its own body (set by <see cref="LocalPlayer"/>).</summary>
        public bool SelfIsExternal;

        /// <summary>Dresses one person from the JSON the server sent and starts loading their custom body when they have one.</summary>
        public void ApplyAppearance(int peerId, string json)
        {
            if (!_views.TryGetValue(peerId, out var view)) return;
            var appearance = AvatarAppearance.Parse(json); view.ApplyAppearance(appearance);
            var generation = _bodyGeneration.TryGetValue(peerId, out var g) ? g + 1 : 1; _bodyGeneration[peerId] = generation;
            if (appearance.CustomModelId is { } model && (AssetLoader ?? Client?.AssetLoader) != null) _ = LoadBodyAsync(view, model, generation);
            else if (appearance.BaseModel == LifeVerse.Shared.Avatar.AvatarRules.DefaultMan && BuiltInBodies.Available) _ = LoadBuiltInAsync(view, appearance, generation);
            else if (view.Skeleton != null) { Destroy(view.Skeleton.gameObject); view.UseSkeleton(null); }
        }

        /// <summary>The man of LifeVerse, from the file that comes with the game: one import shared by everybody, a body per person, the skin in their colour.</summary>
        private async Task LoadBuiltInAsync(AvatarView view, AvatarAppearance appearance, int generation)
        {
            try
            {
                _ = DefaultClips.Preload(); var import = await BuiltInBodies.ImportAsync(); if (import == null || view == null || _bodyGeneration[view.PeerId] != generation) return;
                var skeleton = await SkeletalBody.CreateAsync(view.transform, import); if (skeleton == null) return;
                if (view == null || _bodyGeneration[view.PeerId] != generation) { Destroy(skeleton.gameObject); return; }
                skeleton.TintSkin(appearance.Skin); appearance.CustomModelTriangles = skeleton.Triangles;
                if (view.Skeleton != null) Destroy(view.Skeleton.gameObject); view.UseSkeleton(skeleton); CustomBodiesLoaded++;
            }
            catch (Exception exception) { Debug.LogWarning("[LifeVerse] corpo do jogo: " + exception.Message); }
        }

        private async Task LoadBodyAsync(AvatarView view, Guid model, int generation)
        {
            try
            {
                var bytes = await (AssetLoader ?? Client.AssetLoader)(model); if (bytes == null || view == null || _bodyGeneration[view.PeerId] != generation) return;
                _ = DefaultClips.Preload();
                var skeleton = await SkeletalBody.CreateAsync(view.transform, bytes);
                if (skeleton == null) return;
                if (view == null || _bodyGeneration[view.PeerId] != generation) { Destroy(skeleton.gameObject); return; }
                if (view.Skeleton != null) Destroy(view.Skeleton.gameObject);
                view.UseSkeleton(skeleton); CustomBodiesLoaded++;
            }
            catch (Exception exception) { Debug.LogWarning("[LifeVerse] corpo personalizado: " + exception.Message); }
        }

        private void OnDespawn(DespawnPlayerMessage gone)
        {
            if (!_views.TryGetValue(gone.PeerId, out var view)) return;
            Destroy(view.gameObject); _views.Remove(gone.PeerId); _latest.Remove(gone.PeerId); _bodyGeneration.Remove(gone.PeerId);
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

        private void Update()
        {
            if (!AutoPlan) return; _planClock += Time.deltaTime; if (_planClock < .5f) return; _planClock = 0f;
            PlanDetail(Camera != null ? Camera : Camera.main);
        }

        /// <summary>Decides how much of each body is drawn (the shared planner: distance, whether it is in front of the camera, a budget of triangles and of full bodies).</summary>
        public void PlanDetail(Camera camera)
        {
            if (camera == null || Client == null) return;
            var candidates = new List<DetailCandidate>(_views.Count); var eye = camera.transform.position; var forward = camera.transform.forward;
            foreach (var pair in _views)
            {
                var view = pair.Value; var offset = view.transform.position - eye; var self = pair.Key == Client.SelfPeerId;
                var cost = view.Appearance.CustomModelTriangles > 0 ? view.Appearance.CustomModelTriangles : Budget.BasicCost;
                candidates.Add(new DetailCandidate(pair.Key, offset.magnitude, Vector3.Dot(forward, offset) > -1f, cost, Mathf.Min(cost, Budget.BasicCost * 4), self, view.Detail));
            }
            var plan = AvatarDetailPlanner.Plan(candidates, Budget);
            foreach (var pair in plan) if (_views.TryGetValue(pair.Key, out var view) && view.Detail != pair.Value) view.SetDetail(pair.Value);
        }

        private void OnDestroy()
        {
            if (Client == null) return;
            Client.PlayerSpawned -= OnSpawn; Client.PlayerDespawned -= OnDespawn; Client.SnapshotReceived -= OnSnapshot; Client.GestureChanged -= OnGesture; Client.AppearanceChanged -= OnAppearanceChanged; Client.HeadLookChanged -= OnHeadLook;
        }
    }
}
