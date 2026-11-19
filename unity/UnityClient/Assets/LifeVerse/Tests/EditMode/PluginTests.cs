#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LifeVerse.Scripting;
using LifeVerse.Shared.Groups;
using LifeVerse.Shared.Loading;
using LifeVerse.Shared.Networking;
using LifeVerse.Shared.Terrain;
using LifeVerse.Shared.Vehicles;
using LifeVerse.Shared.Voice;
using NUnit.Framework;

namespace LifeVerse.Unity.Tests.EditMode
{
    /// <summary>
    /// Spike S1: o código de shared/ e scripting/, compilado para netstandard2.1 e carregado como plugin, faz o mesmo que na versão .NET 8.
    /// Cada teste é a versão Unity de um teste xUnit existente e passa por uma das partes que o build dos plugins reescreve ou por uma biblioteca de terceiros.
    /// </summary>
    public sealed class PluginTests
    {
        // ---- codecs de rede (BinaryPrimitives.*SingleLittleEndian, Enum.IsDefined reescritos) --------------------------------------------

        [Test]
        public void SnapshotCodec_RoundTripsQuantizedState()
        {
            var bytes = SnapshotCodec.Encode(42, new[] { new PlayerSnapshot(7, 12.345f, 1f, -2.5f, 3.2f, 0f, -1f, 1.2f, NetworkMotionState.Run, 99) });
            Assert.IsTrue(SnapshotCodec.TryDecode(bytes, out var snapshot));
            Assert.AreEqual(1, snapshot.Players.Count);
            var player = snapshot.Players[0];
            Assert.AreEqual(42u, snapshot.ServerTick);
            Assert.AreEqual(7, player.PeerId);
            Assert.AreEqual(12.345f, player.X, 0.01f);
            Assert.AreEqual(NetworkMotionState.Run, player.Motion);
            Assert.AreEqual(99u, player.LastProcessedInput);
        }

        [Test]
        public void SnapshotCodec_RejectsMalformedPayload()
        {
            Assert.IsFalse(SnapshotCodec.TryDecode(new byte[] { 1, 2, 3 }, out _));
            var valid = SnapshotCodec.Encode(1, Array.Empty<PlayerSnapshot>());
            Assert.IsFalse(SnapshotCodec.TryDecode(valid.Concat(new byte[] { 0 }).ToArray(), out _));
        }

        [Test]
        public void VehicleCodec_RoundTripsPositionAndSpeedExactly()
        {
            var vehicle = new VehicleSnapshot(5, 1.5f, 2.25f, -3.75f, 0.5f, 0.1f, 12.5f, 7);
            var bytes = VehicleCodec.Encode(9, new[] { vehicle });
            Assert.IsTrue(VehicleCodec.TryDecode(bytes, out var tick, out var vehicles));
            Assert.AreEqual(9u, tick);
            Assert.AreEqual(1, vehicles.Count);
            Assert.AreEqual(1.5f, vehicles[0].X); Assert.AreEqual(2.25f, vehicles[0].Y); Assert.AreEqual(-3.75f, vehicles[0].Z);
            Assert.AreEqual(12.5f, vehicles[0].Speed); Assert.AreEqual(7, vehicles[0].DriverPeer);
        }

        // ---- terreno (DeflateStream.ReadExactly, Array.Clear, Enum.IsDefined, System.Text.Json) -----------------------------------------

        [Test]
        public void TerrainMap_BinaryAndJsonRoundTrips_KeepSeaLevelAndHeights()
        {
            var map = new TerrainMap();
            Assert.IsTrue(map.SetSeaLevel(1.5f));
            map.Apply(TerrainTool.Raise, 10f, 10f, 6f, 1f);
            Assert.IsTrue(TerrainMap.TryDeserialize(map.Serialize(), out var loaded));
            Assert.AreEqual(map.SeaLevel, loaded.SeaLevel, 1e-4f);
            Assert.AreEqual(map.SampleHeight(10f, 10f), loaded.SampleHeight(10f, 10f), 1e-3f);
            var fromJson = TerrainMap.FromJson(map.ToJson());
            Assert.AreEqual(map.SampleHeight(10f, 10f), fromJson.SampleHeight(10f, 10f), 1e-3f);
        }

        // ---- hash e texto (SHA256.HashData, Convert.ToHexString, TrimEntries, ISet no lugar de IReadOnlySet) ------------------------------

        [Test]
        public void AssetPlanner_HashMatches_UsesSha256InHex()
        {
            var abc = Encoding.ASCII.GetBytes("abc");
            Assert.IsTrue(AssetPlanner.HashMatches("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", abc));
            Assert.IsTrue(AssetPlanner.HashMatches("  ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad ", abc));
            Assert.IsFalse(AssetPlanner.HashMatches("00", abc));
        }

        [Test]
        public void GroupRules_SplitPermissions_TrimsKeepsKnownOnlyAndDropsEmpties()
        {
            var set = GroupRules.SplitPermissions(" invite , kick ,, nonsense ,ban ");
            CollectionAssert.AreEquivalent(new[] { "invite", "kick", "ban" }, set.ToArray());
            Assert.AreEqual(0, GroupRules.SplitPermissions(null).Count);
        }

        // ---- voz (Concentus/Opus em C# puro) ---------------------------------------------------------------------------------------------

        [Test]
        public void Voice_OpusRoundTrip_ReturnsAFrameWithEnergy()
        {
            var frame = new short[VoiceSettings.FrameSamples];
            for (var i = 0; i < frame.Length; i++) frame[i] = (short)(Math.Sin(i * 2 * Math.PI * 440 / VoiceSettings.SampleRate) * 8000);
            var encoder = new VoiceEncoder(); var decoder = new VoiceDecoder();
            byte[] packet = Array.Empty<byte>();
            for (var warmUp = 0; warmUp < 5; warmUp++) packet = encoder.Encode(frame);
            Assert.IsNotEmpty(packet); Assert.LessOrEqual(packet.Length, VoiceSettings.MaxPacketBytes);
            var decoded = decoder.Decode(packet);
            Assert.IsNotNull(decoded); Assert.AreEqual(VoiceSettings.FrameSamples, decoded!.Length);
            Assert.Greater(VoiceLevel.Of(decoded), 0.01f);
            Assert.IsNull(decoder.Decode(new byte[VoiceSettings.MaxPacketBytes + 1]));
        }

        // ---- NPCs (PriorityQueue do A*) ----------------------------------------------------------------------------------------------------

        [Test]
        public void NpcWorld_WalksAroundALake_UsingTheAStarQueue()
        {
            var script = Guid.NewGuid(); var owner = Guid.NewGuid();
            var lake = new NpcWorld((x, z) => (x - 10) * (x - 10) + z * z < 36 ? -5f : 1f, () => 0f, () => 200f);
            var arrived = new List<NpcState>(); lake.Arrived = arrived.Add;
            var id = lake.Create(script, owner, "Nadador", -2, 0);
            Assert.Less(id, 0);
            Assert.IsTrue(lake.MoveTo(script, id, 22, 0, 4));
            for (var t = 0.0; t < 30; t += 0.1) lake.Tick(t, 0.1f);
            Assert.AreEqual(1, arrived.Count);
            Assert.IsTrue(arrived[0].Reached);
            Assert.AreEqual(22f, lake.Get(id)!.X);
        }

        // ---- scripts Lua (MoonSharp, GeneratedRegex reescrito, temporizadores) ---------------------------------------------------

        [Test]
        public void ScriptCompiler_ReportsEndlessLoopsAndBadTimers()
        {
            var endless = ScriptCompiler.Compile("function on_start() while true do object.setName('x') end end");
            Assert.IsFalse(endless.Valid);
            Assert.IsTrue(endless.Diagnostics.Any(d => d.Severity == "error" && d.Message.Contains("nunca termina") && d.Message.Contains("timer.every")));
            var badTimer = ScriptCompiler.Compile("function on_start() timer.every(0.1, function() end) end");
            Assert.IsFalse(badTimer.Valid);
            Assert.IsTrue(ScriptCompiler.Compile("function on_start() timer.every(1, function() end) end").Valid);
            Assert.IsTrue(ScriptCompiler.Compile("function on_touch() while true do break end end").Valid);
        }

        [Test]
        public void ScriptHost_RepeatingTimerFiresEverySecond()
        {
            var world = new TestWorld(); var owner = Guid.NewGuid(); var objectId = Guid.NewGuid();
            world.Objects[objectId] = new ScriptObjectState(objectId, owner, "Porta", new Vec3(10, 1, 20), new Vec3(0, 90, 0), new Vec3(1, 2, 3));
            var host = new ScriptHost(world, new ScriptHostOptions(MillisecondsPerEvent: 2000));
            var definitions = new List<ScriptDefinition> { new(Guid.NewGuid(), objectId, owner, "loop", "n = 0 timer.every(1, function() n = n + 1 object.setRotation(0, n, 0) end)", 1) };
            host.Sync(definitions); host.Process(500);
            for (var i = 0; i < 20; i++) { world.Now += 0.25; host.Tick(world.Now); host.Process(500); }
            Assert.AreEqual(5, world.Changes.Count);
        }

        private sealed class TestWorld : IScriptWorld
        {
            public double Now { get; set; } = 100;
            public Dictionary<Guid, ScriptObjectState> Objects { get; } = new();
            public List<ObjectChange> Changes { get; } = new();
            public ScriptObjectState? GetObject(Guid id) => Objects.TryGetValue(id, out var state) ? state : null;
            public void ApplyChange(ScriptDefinition script, ObjectChange change) => Changes.Add(change);
            public void Say(ScriptDefinition script, string text) { }
            public void Chat(ScriptDefinition script, ChatKind kind, string text) { }
            public bool TellAvatar(ScriptDefinition script, Guid avatarId, string text) => true;
            public RegionInfo? GetRegion() => null;
            public bool StartHttp(ScriptDefinition script, Guid requestId, HttpSpec spec) => false;
            public bool ApplyPhysics(ScriptDefinition script, string action, Vec3 value) => false;
            public Vec3? GetVelocity(Guid objectId) => null;
            public bool StartPayment(ScriptDefinition script, Guid requestId, Guid avatarId, long amount) => false;
            public string? AvatarName(Guid avatarId) => "Ana";
            public bool PlayAnimation(ScriptDefinition script, Guid avatarId, string name, double seconds) => true;
            public void StopAnimation(ScriptDefinition script, Guid avatarId) { }
            public IReadOnlyList<ScriptItem> GetContents(Guid objectId) => Array.Empty<ScriptItem>();
            public IReadOnlyList<ScriptObjectState> GetLinkset(Guid objectId) => Objects.TryGetValue(objectId, out var one) ? new[] { one } : Array.Empty<ScriptObjectState>();
        }
    }
}
