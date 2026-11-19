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

        public IEnumerator Join(IslandEntryClient api, Guid islandId, bool connect = true)
        {
            Done = false; Failure = null; StartAttempts = 0;
            EntryResult entry = null;
            while (true)
            {
                Status = StartAttempts == 0 ? "requesting_ticket" : $"waiting_island {StartAttempts}/{MaxStartAttempts}";
                var request = api.RequestEntryAsync(islandId); while (!request.IsCompleted) yield return null;
                entry = request.Result;
                if (entry.Ok) break;
                if (!entry.Starting) { Fail(entry.Message); yield break; }
                if (++StartAttempts > MaxStartAttempts) { Fail("A ilha não iniciou a tempo. Tente de novo em instantes."); yield break; }
                var until = Time.realtimeSinceStartup + StartRetrySeconds; while (Time.realtimeSinceStartup < until) yield return null;
            }
            if (entry.ProtocolVersion != NetworkProtocol.Version) { Fail("Versão do protocolo incompatível."); yield break; }

            Status = "loading_world";
            var snapshot = api.GetSnapshotAsync(islandId); var objects = api.GetObjectsAsync(islandId);
            while (!snapshot.IsCompleted || !objects.IsCompleted) yield return null;
            if (snapshot.Result.Ok && Client.Terrain != null) { Client.Terrain.Apply(TerrainMap.FromJson(snapshot.Result.TerrainJson)); if (Client.Ocean != null) Client.Ocean.SeaLevel = Client.Terrain.Map.SeaLevel; SnapshotApplied = true; }
            if (objects.Result != null && Client.Objects != null) ObjectsLoaded = Client.Objects.Load(objects.Result);

            if (!connect) { Status = "ready"; Done = true; yield break; }
            Status = "connecting"; Client.Token = entry.Token;
            if (!Client.Connect(entry.Host, entry.Port)) { Fail("Não foi possível abrir a conexão com a ilha."); yield break; }
            var limit = Time.realtimeSinceStartup + 15f;
            while (!Client.Authenticated && Client.RejectCode == null && Client.ProtocolMismatch == 0 && Time.realtimeSinceStartup < limit) yield return null;
            if (Client.ProtocolMismatch != 0) { Fail("Versão do protocolo incompatível."); yield break; }
            if (!Client.Authenticated) { Fail(Client.RejectCode != null ? "A ilha recusou a entrada: " + Client.RejectCode : "A ilha não respondeu a tempo."); yield break; }
            Status = "in_island"; Done = true;
        }

        private void Fail(string message) { Failure = message; Status = "failed"; Done = true; }
    }
}
