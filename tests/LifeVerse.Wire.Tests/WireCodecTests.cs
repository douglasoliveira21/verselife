using LifeVerse.Shared.Networking;
using LifeVerse.Shared.Voice;
using LifeVerse.Wire;
using Xunit;

namespace LifeVerse.Wire.Tests;

/// <summary>O formato de quadro das 44 mensagens: ida e volta, rotas iguais às do protocolo 11, limites e mensagens malformadas.</summary>
public sealed class WireCodecTests
{
    internal static byte[] Bytes(int length, int seed = 1) { var data = new byte[length]; new Random(seed).NextBytes(data); return data; }

    /// <summary>Uma instância de cada uma das 44 mensagens, com valores que usam todos os campos.</summary>
    internal static List<IWireMessage> Samples() =>
    [
        new AuthenticateMessage("ticket.assinado", 12), new SendChatMessage("Olá, mundo! ✓"), new SetFlightMessage(true), new SubmitBuildOperationMessage("{\"op\":\"move\"}"),
        new SubmitTerrainEditMessage(2, 10.5f, -3.25f, 6f, .8f, 3), new SubmitTerrainImportMessage(Bytes(500)), new SubmitSeaLevelMessage(1.5f), new UndoTerrainMessage(), new RedoTerrainMessage(),
        new RequestTerrainMessage(), new RefreshAppearanceMessage(), new AttachmentLiveMessage("a1", [1f, 2f, 3f, 0f, 0f, 0f, 1f, 1f, 1f]), new RequestTeleportMessage("parcela-7", 12f, 34f),
        new TouchObjectMessage("obj-1"), new ScriptDialogResponseMessage("{\"ok\":true}"), new EnterVehicleMessage(4), new ExitVehicleMessage(), new GestureMessage(2),
        new SubmitInputMessage(123456u, .5f, -1f, 0b0010_0101), new SubmitVehicleInputMessage(1f, -.5f, true), new HeadLookMessage(.3f), new SubmitVoiceMessage(88u, Bytes(120)),

        new AuthResultMessage(true, "ok", 4_000_000_000u), new ProtocolRejectedMessage(12),
        new SpawnPlayerMessage(7, "avatar-guid", "Douglas", "{\"hair\":\"curto\"}", 1f, 2f, 3f, 5), new DespawnPlayerMessage(7, true), new AppearanceChangedMessage(7, "Douglas", "{\"a\":1}"),
        new AttachmentLiveChangedMessage(7, "a1", [1f, 2f]), new ReceiveChatMessage(7, "Douglas", "oi", 1_700_000_000_000L), new BuildOperationAppliedMessage("{\"ok\":1}"),
        new BuildOperationRejectedMessage("not_owner"), new TerrainFullMessage(Bytes(2000, 2), true), new TerrainPatchedMessage(Bytes(300, 3), 42L),
        new TerrainEditedMessage(1, 5f, 6f, 4f, .5f, 2, 99L), new TerrainEditRejectedMessage("rate"), new SeaLevelChangedMessage(-.8f, 5L), new TeleportResultMessage(true, "ok", 10f, 20f, 1.57f),
        new VehicleSpawnedMessage(3, "Carro", "car", 1f, 2f, 3f, .5f), new VehicleRejectedMessage("occupied"), new GestureChangedMessage(7, 3), new SnapshotMessage(Bytes(900, 4)),
        new VehicleSnapshotsMessage(Bytes(200, 5)), new HeadLookChangedMessage(7, -.4f), new ReceiveVoiceMessage(7, 90u, Bytes(100, 6)),
    ];

    private static WireDirection DirectionOf(IWireMessage message) =>
        WireCodec.Messages(WireDirection.ClientToServer).Any(m => m.Type == message.GetType()) ? WireDirection.ClientToServer : WireDirection.ServerToClient;

    [Fact]
    public void EveryMessageRoundTripsToTheSameBytes()
    {
        foreach (var sample in Samples())
        {
            var bytes = WireCodec.Encode(sample);
            var decoded = WireCodec.Decode(bytes, DirectionOf(sample));
            Assert.True(decoded.Ok, $"{sample.GetType().Name}: {decoded.Error}");
            Assert.Equal(sample.GetType(), decoded.Message!.GetType());
            Assert.Equal(bytes, WireCodec.Encode(decoded.Message));
        }
    }

    [Fact]
    public void ScalarMessagesComeBackEqual()
    {
        foreach (var sample in new IWireMessage[] { new AuthenticateMessage("t", 12), new SubmitInputMessage(9u, 1.5f, -2f, 7), new SpawnPlayerMessage(7, "a", "n", "{}", 1f, 2f, 3f, 4), new TerrainEditedMessage(1, 2f, 3f, 4f, 5f, 6, 7L), new ReceiveChatMessage(1, "a", "b", 5L) })
            Assert.Equal(sample, WireCodec.Decode(WireCodec.Encode(sample), DirectionOf(sample)).Message);
    }

    [Fact]
    public void TheProtocolHas44MessagesAndTheSampleCoversEveryOne()
    {
        Assert.Equal(44, WireCodec.Count);
        Assert.Equal(22, WireCodec.Messages(WireDirection.ClientToServer).Count);
        Assert.Equal(22, WireCodec.Messages(WireDirection.ServerToClient).Count);
        var registered = WireCodec.Messages(WireDirection.ClientToServer).Concat(WireCodec.Messages(WireDirection.ServerToClient)).Select(m => m.Type).ToHashSet();
        Assert.Equal(registered, Samples().Select(s => s.GetType()).ToHashSet());
        Assert.Equal(44, Samples().Count);
    }

    [Fact]
    public void ChannelsAndModesAreTheOnesOfTheGodotProtocol()
    {
        // 18 mensagens confiáveis no canal 0 em cada direção; as outras 4 nos canais 1 (cliente) e 2 (servidor): do cliente, 3 sequenciadas e a voz;
        // do servidor, só o olhar é sequenciado (os snapshots vão sem ordem, porque um snapshot grande são vários pacotes, e quem recebe confere o tick)
        foreach (var (direction, stateChannel, sequenced) in new[] { (WireDirection.ClientToServer, (byte)1, 3), (WireDirection.ServerToClient, (byte)2, 1) })
        {
            var routes = WireCodec.Messages(direction).Select(m => m.Route).ToList();
            Assert.Equal(18, routes.Count(r => r is { Channel: 0, Delivery: WireDelivery.Reliable }));
            Assert.Equal(sequenced, routes.Count(r => r.Channel == stateChannel && r.Delivery == WireDelivery.Sequenced));
            Assert.Equal(4 - sequenced, routes.Count(r => r.Channel == stateChannel && r.Delivery == WireDelivery.Unreliable));
        }
        Assert.Equal(new WireRoute(1, WireDelivery.Sequenced), WireCodec.RouteOf(new SubmitInputMessage(1, 0, 0, 0)));
        Assert.Equal(new WireRoute(2, WireDelivery.Unreliable), WireCodec.RouteOf(new SnapshotMessage([])));
        Assert.Equal(new WireRoute(1, WireDelivery.Unreliable), WireCodec.RouteOf(new SubmitVoiceMessage(1, [])));
        Assert.Equal(new WireRoute(2, WireDelivery.Unreliable), WireCodec.RouteOf(new ReceiveVoiceMessage(1, 1, [])));
        Assert.Equal(new WireRoute(0, WireDelivery.Reliable), WireCodec.RouteOf(new AuthenticateMessage("t", 12)));
    }

    [Fact]
    public void LateSnapshotsAreDroppedAndTheOtherPacketsOfTheSameTickAreKept()
    {
        var order = new SnapshotOrder();
        Assert.True(order.Accept(10)); Assert.True(order.Accept(10)); Assert.True(order.Accept(13)); Assert.False(order.Accept(10)); Assert.False(order.Accept(12)); Assert.True(order.Accept(13));
        var wrap = new SnapshotOrder(); Assert.True(wrap.Accept(uint.MaxValue - 1)); Assert.True(wrap.Accept(2)); Assert.False(wrap.Accept(uint.MaxValue));
    }

    [Fact]
    public void LimitsAndVersionFollowTheGodotProtocol()
    {
        Assert.Equal(NetworkProtocol.MaxSnapshotBytes, WireLimits.Snapshot);
        Assert.Equal(NetworkProtocol.MaxTerrainImportBytes, WireLimits.TerrainImport);
        Assert.Equal(VoiceSettings.MaxPacketBytes, WireLimits.Voice);
        Assert.True(WireLimits.Chat >= NetworkProtocol.MaxChatCharacters * 4);
        Assert.Equal(NetworkProtocol.Version + 1, WireProtocol.Version);
    }

    [Fact]
    public void TheSnapshotPayloadOfTheGodotCodecTravelsUnchanged()
    {
        var payload = SnapshotCodec.Encode(42, [new PlayerSnapshot(7, 12.345f, 1f, -2.5f, 3.2f, 0f, -1f, 1.2f, NetworkMotionState.Run, 99)]);
        var decoded = Assert.IsType<SnapshotMessage>(WireCodec.Decode(WireCodec.Encode(new SnapshotMessage(payload)), WireDirection.ServerToClient).Message);
        Assert.Equal(payload, decoded.Payload);
        Assert.True(SnapshotCodec.TryDecode(decoded.Payload, out var snapshot));
        Assert.Equal(NetworkMotionState.Run, snapshot.Players.Single().Motion);
    }

    [Fact]
    public void EmptyUnknownAndWrongDirectionFramesAreRefused()
    {
        Assert.Equal("empty", WireCodec.Decode([], WireDirection.ClientToServer).Error);
        Assert.Equal("unknown_type", WireCodec.Decode([0x7F], WireDirection.ClientToServer).Error);
        Assert.Equal("unknown_type", WireCodec.Decode([0x00], WireDirection.ServerToClient).Error);
        Assert.Equal("wrong_direction", WireCodec.Decode(WireCodec.Encode(new SendChatMessage("oi")), WireDirection.ServerToClient).Error);
        Assert.Equal("wrong_direction", WireCodec.Decode(WireCodec.Encode(new SnapshotMessage([1])), WireDirection.ClientToServer).Error);
    }

    [Fact]
    public void EveryTruncationIsRefusedWithoutThrowing()
    {
        foreach (var sample in Samples())
        {
            var bytes = WireCodec.Encode(sample); var direction = DirectionOf(sample);
            for (var length = 0; length < bytes.Length; length++)
            {
                if (length == 1 && bytes.Length == 1) continue;
                var result = WireCodec.Decode(bytes.AsSpan(0, length), direction);
                Assert.False(result.Ok, $"{sample.GetType().Name} cortada em {length} de {bytes.Length}");
            }
        }
    }

    [Fact]
    public void BytesLeftOverAreRefused()
    {
        foreach (var sample in Samples())
            Assert.Equal("trailing_bytes", WireCodec.Decode([.. WireCodec.Encode(sample), 0], DirectionOf(sample)).Error);
    }

    [Fact]
    public void RandomFramesNeverThrow()
    {
        var random = new Random(20261118); var accepted = 0;
        foreach (var direction in new[] { WireDirection.ClientToServer, WireDirection.ServerToClient })
            for (var i = 0; i < 30_000; i++)
            {
                var frame = new byte[random.Next(0, 80)]; random.NextBytes(frame);
                if (frame.Length > 0 && random.Next(3) > 0) frame[0] = (byte)(direction == WireDirection.ClientToServer ? random.Next(1, 0x17) : random.Next(0x81, 0x97));
                var result = WireCodec.Decode(frame, direction);
                if (result.Ok) { accepted++; Assert.True(WireCodec.Decode(WireCodec.Encode(result.Message!), direction).Ok); }
            }
        Assert.True(accepted > 0, "o fuzz também precisa exercitar o caminho que aceita");
    }

    [Fact]
    public void FieldsAboveTheirLimitsAreRefused()
    {
        void Refused(IWireMessage message) => Assert.Equal("malformed", WireCodec.Decode(WireCodec.Encode(message), DirectionOf(message)).Error);
        Refused(new AuthenticateMessage(new string('x', WireLimits.Token + 1), 12));
        Refused(new SendChatMessage(new string('x', WireLimits.Chat + 1)));
        Refused(new TouchObjectMessage(new string('x', WireLimits.Id + 1)));
        Refused(new SubmitVoiceMessage(1, new byte[WireLimits.Voice + 1]));
        Refused(new AttachmentLiveMessage("a", new float[WireLimits.AttachmentValues + 1]));
        Refused(new SnapshotMessage(new byte[WireLimits.Snapshot + 1]));
        Refused(new SubmitTerrainImportMessage(new byte[WireLimits.TerrainImport + 1]));
        Assert.True(WireCodec.Decode(WireCodec.Encode(new SnapshotMessage(new byte[WireLimits.Snapshot])), WireDirection.ServerToClient).Ok);
        Assert.True(WireCodec.Decode(WireCodec.Encode(new SubmitVoiceMessage(1, new byte[WireLimits.Voice])), WireDirection.ClientToServer).Ok);
    }

    [Fact]
    public void NonFiniteFloatsAreRefused()
    {
        foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.False(WireCodec.Decode(WireCodec.Encode(new SubmitInputMessage(1, bad, 0, 0)), WireDirection.ClientToServer).Ok);
            Assert.False(WireCodec.Decode(WireCodec.Encode(new SubmitSeaLevelMessage(bad)), WireDirection.ClientToServer).Ok);
            Assert.False(WireCodec.Decode(WireCodec.Encode(new HeadLookMessage(bad)), WireDirection.ClientToServer).Ok);
            Assert.False(WireCodec.Decode(WireCodec.Encode(new AttachmentLiveMessage("a", [1f, bad])), WireDirection.ClientToServer).Ok);
        }
    }

    [Fact]
    public void InvalidUtf8BadBooleansAndOverlongNumbersAreRefused()
    {
        Assert.Equal("malformed", WireCodec.Decode([0x02, 2, 0xC3, 0x28], WireDirection.ClientToServer).Error);          // UTF-8 inválido
        Assert.Equal("malformed", WireCodec.Decode([0x03, 2], WireDirection.ClientToServer).Error);                        // booleano que não é 0 nem 1
        Assert.Equal("malformed", WireCodec.Decode([0x02, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F], WireDirection.ClientToServer).Error);   // número variável longo demais
        Assert.Equal("malformed", WireCodec.Decode([0x02, 0x80, 0x80, 0x80, 0x80, 0x10], WireDirection.ClientToServer).Error);          // 5º byte além de 32 bits
        Assert.True(WireCodec.Decode([0x02, 0], WireDirection.ClientToServer).Ok);                                         // chat vazio é um quadro válido
    }

    [Fact]
    public void EncodingAFrameLargerThanTheLimitThrows()
    {
        Assert.Throws<InvalidOperationException>(() => WireCodec.Encode(new TerrainFullMessage(new byte[WireProtocol.MaxFrameBytes], true)));
        Assert.Equal("too_large", WireCodec.Decode(new byte[WireProtocol.MaxFrameBytes + 1], WireDirection.ServerToClient).Error);
    }
}
