using System;
using System.Collections.Generic;
using LifeVerse.Shared.Networking;
using LifeVerse.Shared.Terrain;
using LifeVerse.Wire;
using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// The connection of a player to an island server (LifeVerse Wire 12) as far as the world goes: it signs in with the entry ticket and keeps the ground, the sea and
    /// the ocean in step with what the server says (the full map when joining, the edits, the undo patches, the sea level). When an edit does not reproduce the
    /// revision the server announced, the local copy has diverged and the whole map is asked for again (at most every two seconds, as the server allows).
    /// Players, objects, chat and the rest of the game messages come with the phases that own them.
    /// </summary>
    public sealed class WorldClient : MonoBehaviour
    {
        public TerrainView Terrain;
        public OceanView Ocean;
        public WorldObjects Objects;
        public IslandEnvironment Environment;   // when set, the island's saved look (hour, weather, fog) is read from the API on joining and applied
        public string Token = "";
        /// <summary>Gets the bytes of an asset of the island (a texture, a model, a custom body); set when joining through the REST door.</summary>
        public Func<Guid, System.Threading.Tasks.Task<byte[]>> AssetLoader;

        private WireClient _client;
        private float _lastResync = -10f;
        private readonly List<string> _log = new List<string>();

        public bool Connected => _client != null && _client.IsConnected;
        public bool Authenticated { get; private set; }
        public string RejectCode { get; private set; }
        public int ProtocolMismatch { get; private set; }
        public bool CanEditTerrain { get; private set; }
        public int ResyncsAsked { get; private set; }
        public int TerrainMessages { get; private set; }
        public string LastTerrainRejection { get; private set; }
        public string LastBuildRejection { get; private set; }
        public IReadOnlyList<string> Log => _log;
        /// <summary>Told when the server accepts a person: the number the server gave this player (their id in snapshots).</summary>
        public int SelfPeerId { get; private set; } = -1;

        // what the avatar side (LifeVerse.Unity.Avatar) listens to: people arriving and leaving, where everybody is, gestures
        public event Action<SpawnPlayerMessage> PlayerSpawned;
        public event Action<DespawnPlayerMessage> PlayerDespawned;
        public event Action<AppearanceChangedMessage> AppearanceChanged;
        public event Action<GestureChangedMessage> GestureChanged;
        public event Action<WorldSnapshot> SnapshotReceived;
        public event Action<ReceiveChatMessage> ChatReceived;
        public event Action<string> DisconnectedBy;
        public bool SendChat(string text) => Send(new SendChatMessage(text));
        public event Action<HeadLookChangedMessage> HeadLookChanged;
        /// <summary>A dialog or panel a script of an object asks the person to see (JSON "script_dialog" / "script_hud").</summary>
        public event Action<string> ScriptOperation;
        /// <summary>The answer to a script dialog or the click on a script panel (JSON); the island checks it was asked.</summary>
        public bool SendScriptResponse(string json) => json != null && json.Length <= 2048 && Send(new ScriptDialogResponseMessage(json));
        public event Action<VehicleSpawnedMessage> VehicleSpawned;
        public event Action<IReadOnlyList<LifeVerse.Shared.Vehicles.VehicleSnapshot>> VehicleSnapshotsReceived;
        public string LastVehicleRejection { get; private set; }
        public int VehicleSnapshotsSeen { get; private set; }
        public int Snapshots { get; private set; }

        /// <summary>One step of movement input for the server (game space: x right, z toward the viewer).</summary>
        public bool SendInput(uint sequence, float x, float z, InputFlags flags) => Send(new SubmitInputMessage(sequence, x, z, (byte)flags));

        public bool Connect(string host, int port)
        {
            _client = new WireClient();
            _client.MessageReceived += OnMessage;
            _client.Disconnected += reason => { _log.Add("disconnected: " + reason); DisconnectedBy?.Invoke(reason.ToString()); };
            return _client.Connect(host, port);
        }

        public bool Send(IWireMessage message) => _client != null && _client.Send(message);

        private void Update() => Poll();

        /// <summary>One turn of the network loop; the game calls it every frame, tests call it in a loop.</summary>
        public void Poll()
        {
            if (_client == null) return;
            _client.Poll();
            if (!_signedIn && _client.IsConnected) { _signedIn = true; _client.Send(new AuthenticateMessage(Token, WireProtocol.Version)); }
        }

        private bool _signedIn;

        private void OnMessage(IWireMessage message)
        {
            switch (message)
            {
                case AuthResultMessage result: Authenticated = result.Success; if (!result.Success) RejectCode = result.Code; break;
                case ProtocolRejectedMessage rejected: ProtocolMismatch = rejected.ServerVersion; break;
                case SpawnPlayerMessage spawn:
                    if (SelfPeerId < 0 && spawn.DisplayName == Token) SelfPeerId = spawn.PeerId;
                    PlayerSpawned?.Invoke(spawn); break;
                case DespawnPlayerMessage gone: PlayerDespawned?.Invoke(gone); break;
                case AppearanceChangedMessage changed: AppearanceChanged?.Invoke(changed); break;
                case GestureChangedMessage gesture: GestureChanged?.Invoke(gesture); break;
                case ReceiveChatMessage chat: ChatReceived?.Invoke(chat); break;
                case HeadLookChangedMessage look: HeadLookChanged?.Invoke(look); break;
                case VehicleSpawnedMessage car: VehicleSpawned?.Invoke(car); break;
                case VehicleRejectedMessage refused: LastVehicleRejection = refused.Code; break;
                case VehicleSnapshotsMessage cars:
                    if (VehicleCodec.TryDecode(cars.Payload, out _, out var vehicles)) { VehicleSnapshotsSeen++; VehicleSnapshotsReceived?.Invoke(vehicles); }
                    break;
                case SnapshotMessage snapshot:
                    if (SnapshotCodec.TryDecode(snapshot.Payload, out var world)) { Snapshots++; SnapshotReceived?.Invoke(world); }
                    break;
                case TerrainFullMessage full: TerrainMessages++; CanEditTerrain = full.CanEdit; Terrain.ApplyFull(full.Data); if (Ocean != null) Ocean.SeaLevel = Terrain.Map.SeaLevel; break;
                case TerrainEditedMessage edit:
                    TerrainMessages++;
                    if (!Terrain.ApplyEdit((TerrainTool)edit.Tool, edit.X, edit.Z, edit.Radius, edit.Strength, edit.Layer, edit.Revision)) Resync("edit");
                    break;
                case TerrainPatchedMessage patch: TerrainMessages++; if (!Terrain.ApplyPatch(patch.Patch, patch.Revision)) Resync("patch"); break;
                case SeaLevelChangedMessage sea:
                    TerrainMessages++;
                    if (!Terrain.ApplySeaLevel(sea.Level, sea.Revision)) Resync("sea"); else if (Ocean != null) Ocean.SeaLevel = Terrain.Map.SeaLevel;
                    break;
                case BuildOperationAppliedMessage applied:
                    // what a script asks of the person (a dialog, a panel) is for the interface, not for the objects of the world
                    if (applied.OperationJson.Contains("\"script_dialog\"") || applied.OperationJson.Contains("\"script_hud\"")) ScriptOperation?.Invoke(applied.OperationJson); else Objects?.ApplyOperation(applied.OperationJson);
                    break;
                case BuildOperationRejectedMessage refused: LastBuildRejection = refused.Code; break;
                case TerrainEditRejectedMessage rejection: LastTerrainRejection = rejection.Code; break;
            }
        }

        private void Resync(string reason)
        {
            _log.Add("resync: " + reason);
            if (Time.realtimeSinceStartup - _lastResync < 2.1f) return;
            _lastResync = Time.realtimeSinceStartup; ResyncsAsked++; _client.Send(new RequestTerrainMessage());
        }

        private void OnDestroy() { _client?.Dispose(); }
    }
}
