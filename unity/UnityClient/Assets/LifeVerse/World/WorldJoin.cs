using System;
using System.Collections;
using LifeVerse.Shared.Networking;
using LifeVerse.Shared.Terrain;
using LifeVerse.Wire;
using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// Enters an island the way the Godot client does: ask the API for the entry ticket (waiting while the island's server starts), draw the saved terrain and the objects
    /// at once from the REST snapshot (so the island shows before the connection is up), then connect to the island server with the ticket; from then on the
    /// <see cref="WorldClient"/> keeps everything in step. <see cref="Status"/> says where it is, for a loading screen.
    /// </summary>
    public sealed class WorldJoin : MonoBehaviour
    {
        public WorldClient Client;
        public int MaxStartAttempts = 15;
        public float StartRetrySeconds = 4f;

        public string Status { get; private set; } = "idle";
        public string Failure { get; private set; }
        public bool Done { get; private set; }
        public bool SnapshotApplied { get; private set; }
        public int ObjectsLoaded { get; private set; }
        public int StartAttempts { get; private set; }
        public IslandLookService Look { get; private set; }
        /// <summary>The HTTP status behind a failure to enter (409: the person has no avatar yet), or 0.</summary>
        public int FailureStatus { get; private set; }

        /// <summary>Enters the island. With a <paramref name="ticket"/> (a trip between islands got it from the API already) the request for one is skipped.</summary>
        public IEnumerator Join(IslandEntryClient api, Guid islandId, bool connect = true, EntryResult ticket = null)
        {
            Done = false; Failure = null; StartAttempts = 0; FailureStatus = 0;
            EntryResult entry = ticket;
            while (ticket == null)
            {
                Status = StartAttempts == 0 ? "requesting_ticket" : $"waiting_island {StartAttempts}/{MaxStartAttempts}";
                var request = api.RequestEntryAsync(islandId); while (!request.IsCompleted) yield return null;
                entry = request.Result;
                if (entry.Ok) break;
                if (!entry.Starting) { FailureStatus = entry.Status; Fail(entry.Message); yield break; }
                if (++StartAttempts > MaxStartAttempts) { Fail("A ilha não iniciou a tempo. Tente de novo em instantes."); yield break; }
                var until = Time.realtimeSinceStartup + StartRetrySeconds; while (Time.realtimeSinceStartup < until) yield return null;
            }
            if (entry.ProtocolVersion != NetworkProtocol.Version) { Fail("Versão do protocolo incompatível."); yield break; }

            Status = "loading_world";
            var snapshot = api.GetSnapshotAsync(islandId); var objects = api.GetObjectsAsync(islandId);
            while (!snapshot.IsCompleted || !objects.IsCompleted) yield return null;
            if (snapshot.Result.Ok && Client.Terrain != null) { Client.Terrain.Apply(TerrainMap.FromJson(snapshot.Result.TerrainJson)); if (Client.Ocean != null) Client.Ocean.SeaLevel = Client.Terrain.Map.SeaLevel; SnapshotApplied = true; }
            // the files near the arrival point come first (kept on disk by hash); the far ones keep coming after the entry
            Status = "loading_assets"; if (!_pruned) { _pruned = true; AssetPreloader.Prune(CacheFolder); }
            Preload = new AssetPreloader(CacheFolder, asset => api.GetAssetAsync(islandId, asset));
            var manifest = api.ApiAsync("GET", AssetPreloader.ManifestPath(islandId, null, null)); while (!manifest.IsCompleted) yield return null;
            var essentials = Preload.Essentials(manifest.Result.Status, manifest.Result.Body);
            while (!essentials.IsCompleted) { Detail = AssetPreloader.Describe(Preload.DoneBytes, Preload.TotalBytes); yield return null; }
            Detail = null; if (!essentials.Result) { Fail(Preload.LastError); yield break; }
            var preload = Preload; Client.AssetLoader = asset => preload.Load(asset);
            if (Client.Objects != null) Client.Objects.AssetLoader = Client.AssetLoader;   // pictures and models come from the API on demand
            if (objects.Result != null && Client.Objects != null) ObjectsLoaded = Client.Objects.Load(objects.Result);
            if (Client.Environment != null)
            {
                Look = Client.Environment.GetComponent<IslandLookService>() ?? Client.Environment.gameObject.AddComponent<IslandLookService>();
                Look.Environment = Client.Environment; Look.Api = api; Look.Island = islandId;
                var refresh = Look.RefreshAsync(); while (!refresh.IsCompleted) yield return null;   // the saved hour and weather are there before the first frame of the island
            }

            if (!connect) { Status = "ready"; Done = true; yield break; }
            Status = "connecting"; Client.Token = entry.Token;
            if (!Client.Connect(entry.Host, entry.Port)) { Fail("Não foi possível abrir a conexão com a ilha."); yield break; }
            var limit = Time.realtimeSinceStartup + 15f;
            while (!Client.Authenticated && Client.RejectCode == null && Client.ProtocolMismatch == 0 && Time.realtimeSinceStartup < limit) yield return null;
            if (Client.ProtocolMismatch != 0) { Fail("Versão do protocolo incompatível."); yield break; }
            if (!Client.Authenticated) { Fail(Client.RejectCode != null ? "A ilha recusou a entrada: " + Client.RejectCode : "A ilha não respondeu a tempo."); yield break; }
            Status = "in_island"; Done = true; _ = Preload?.Rest();
        }

        /// <summary>The files of the island, near ones before the entry, the rest after it.</summary>
        public AssetPreloader Preload { get; private set; }
        /// <summary>What the current step is doing in words (the bytes of the files), or null.</summary>
        public string Detail { get; private set; }
        public static string CacheFolder => System.IO.Path.Combine(Application.persistentDataPath, "assets");
        private static bool _pruned;

        private void Fail(string message) { Failure = message; Status = "failed"; Done = true; }
    }
}
