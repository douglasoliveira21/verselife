namespace LifeVerse.Wire;

internal delegate IWireMessage? ReadMessage(ref WireReader reader);

/// <summary>Resultado de decodificar um quadro: a mensagem, ou o motivo (nunca uma exceção).</summary>
public readonly record struct WireDecodeResult(IWireMessage? Message, string? Error)
{
    public bool Ok => Message != null;
}

/// <summary>
/// Quadro = [1 byte: tipo][campos]. Ids de 0x01 a 0x16 vão do cliente ao servidor e de 0x81 a 0x96 do servidor ao cliente.
/// Quem recebe só aceita a direção que lhe cabe, recusa bytes sobrando e nunca lança por conteúdo malformado.
/// </summary>
public static class WireCodec
{
    private sealed record Entry(byte Id, Type Type, WireDirection Direction, WireRoute Route, Action<WireWriter, IWireMessage> Write, ReadMessage Read);

    private static readonly Dictionary<byte, Entry> ById = new();
    private static readonly Dictionary<Type, Entry> ByType = new();

    /// <summary>Quantidade de mensagens definidas (44).</summary>
    public static int Count => ById.Count;

    /// <summary>Os tipos de mensagem de uma direção, com o id e a rota de cada um.</summary>
    public static IReadOnlyList<(byte Id, Type Type, WireRoute Route)> Messages(WireDirection direction) =>
        ById.Values.Where(e => e.Direction == direction).OrderBy(e => e.Id).Select(e => (e.Id, e.Type, e.Route)).ToList();

    private static void Define<T>(byte id, WireDirection direction, byte channel, WireDelivery delivery, Action<WireWriter, T> write, ReadMessage read) where T : IWireMessage
    {
        var entry = new Entry(id, typeof(T), direction, new WireRoute(channel, delivery), (w, m) => write(w, (T)m), read);
        ById.Add(id, entry); ByType.Add(typeof(T), entry);
    }

    private const WireDirection C2S = WireDirection.ClientToServer, S2C = WireDirection.ServerToClient;
    private const WireDelivery Rel = WireDelivery.Reliable, Seq = WireDelivery.Sequenced, Unrel = WireDelivery.Unreliable;

    static WireCodec()
    {
        // ---- cliente → servidor -------------------------------------------------------------------------------------------------------
        Define<AuthenticateMessage>(0x01, C2S, 0, Rel, (w, m) => { w.String(m.Token); w.Int(m.ProtocolVersion); },
            (ref WireReader r) => { var token = r.String(WireLimits.Token); var version = r.Int(); return new AuthenticateMessage(token, version); });
        Define<SendChatMessage>(0x02, C2S, 0, Rel, (w, m) => w.String(m.Message),
            (ref WireReader r) => new SendChatMessage(r.String(WireLimits.Chat)));
        Define<SetFlightMessage>(0x03, C2S, 0, Rel, (w, m) => w.Bool(m.Enabled),
            (ref WireReader r) => new SetFlightMessage(r.Bool()));
        Define<SubmitBuildOperationMessage>(0x04, C2S, 0, Rel, (w, m) => w.String(m.OperationJson),
            (ref WireReader r) => new SubmitBuildOperationMessage(r.String(WireLimits.OperationJson)));
        Define<SubmitTerrainEditMessage>(0x05, C2S, 0, Rel, (w, m) => { w.Int(m.Tool); w.Float(m.X); w.Float(m.Z); w.Float(m.Radius); w.Float(m.Strength); w.Int(m.Layer); },
            (ref WireReader r) => { var tool = r.Int(); var x = r.Float(); var z = r.Float(); var radius = r.Float(); var strength = r.Float(); var layer = r.Int(); return new SubmitTerrainEditMessage(tool, x, z, radius, strength, layer); });
        Define<SubmitTerrainImportMessage>(0x06, C2S, 0, Rel, (w, m) => w.Bytes(m.Heights),
            (ref WireReader r) => new SubmitTerrainImportMessage(r.Bytes(WireLimits.TerrainImport)));
        Define<SubmitSeaLevelMessage>(0x07, C2S, 0, Rel, (w, m) => w.Float(m.Level),
            (ref WireReader r) => new SubmitSeaLevelMessage(r.Float()));
        Define<UndoTerrainMessage>(0x08, C2S, 0, Rel, (w, m) => { }, (ref WireReader r) => new UndoTerrainMessage());
        Define<RedoTerrainMessage>(0x09, C2S, 0, Rel, (w, m) => { }, (ref WireReader r) => new RedoTerrainMessage());
        Define<RequestTerrainMessage>(0x0A, C2S, 0, Rel, (w, m) => { }, (ref WireReader r) => new RequestTerrainMessage());
        Define<RefreshAppearanceMessage>(0x0B, C2S, 0, Rel, (w, m) => { }, (ref WireReader r) => new RefreshAppearanceMessage());
        Define<AttachmentLiveMessage>(0x0C, C2S, 0, Rel, (w, m) => { w.String(m.Id); w.Floats(m.Values); },
            (ref WireReader r) => { var id = r.String(WireLimits.Id); var values = r.Floats(WireLimits.AttachmentValues); return new AttachmentLiveMessage(id, values); });
        Define<RequestTeleportMessage>(0x0D, C2S, 0, Rel, (w, m) => { w.String(m.ParcelId); w.Float(m.X); w.Float(m.Z); },
            (ref WireReader r) => { var parcel = r.String(WireLimits.Id); var x = r.Float(); var z = r.Float(); return new RequestTeleportMessage(parcel, x, z); });
        Define<TouchObjectMessage>(0x0E, C2S, 0, Rel, (w, m) => w.String(m.ObjectId),
            (ref WireReader r) => new TouchObjectMessage(r.String(WireLimits.Id)));
        Define<ScriptDialogResponseMessage>(0x0F, C2S, 0, Rel, (w, m) => w.String(m.Json),
            (ref WireReader r) => new ScriptDialogResponseMessage(r.String(WireLimits.DialogJson)));
        Define<EnterVehicleMessage>(0x10, C2S, 0, Rel, (w, m) => w.Int(m.VehicleId),
            (ref WireReader r) => new EnterVehicleMessage(r.Int()));
        Define<ExitVehicleMessage>(0x11, C2S, 0, Rel, (w, m) => { }, (ref WireReader r) => new ExitVehicleMessage());
        Define<GestureMessage>(0x12, C2S, 0, Rel, (w, m) => w.Int(m.GestureId),
            (ref WireReader r) => new GestureMessage(r.Int()));
        Define<SubmitInputMessage>(0x13, C2S, 1, Seq, (w, m) => { w.UInt(m.Sequence); w.Float(m.X); w.Float(m.Z); w.Byte(m.Flags); },
            (ref WireReader r) => { var sequence = r.UInt(); var x = r.Float(); var z = r.Float(); var flags = r.Byte(); return new SubmitInputMessage(sequence, x, z, flags); });
        Define<SubmitVehicleInputMessage>(0x14, C2S, 1, Seq, (w, m) => { w.Float(m.Throttle); w.Float(m.Steer); w.Bool(m.Brake); },
            (ref WireReader r) => { var throttle = r.Float(); var steer = r.Float(); var brake = r.Bool(); return new SubmitVehicleInputMessage(throttle, steer, brake); });
        Define<HeadLookMessage>(0x15, C2S, 1, Seq, (w, m) => w.Float(m.Pitch),
            (ref WireReader r) => new HeadLookMessage(r.Float()));
        Define<SubmitVoiceMessage>(0x16, C2S, 1, Unrel, (w, m) => { w.UInt(m.Sequence); w.Bytes(m.Data); },
            (ref WireReader r) => { var sequence = r.UInt(); var data = r.Bytes(WireLimits.Voice); return new SubmitVoiceMessage(sequence, data); });

        // ---- servidor → cliente -------------------------------------------------------------------------------------------------------
        // Os snapshots vão sem ordem garantida: um snapshot grande são vários pacotes do mesmo tick, e a entrega "sequenced" jogaria fora o que chegasse depois do irmão.
        // Quem recebe descarta o que for de um tick mais velho que o último visto (ver SnapshotOrder).
        Define<AuthResultMessage>(0x81, S2C, 0, Rel, (w, m) => { w.Bool(m.Success); w.String(m.Code); w.UInt(m.ServerTick); },
            (ref WireReader r) => { var success = r.Bool(); var code = r.String(WireLimits.Code); var tick = r.UInt(); return new AuthResultMessage(success, code, tick); });
        Define<ProtocolRejectedMessage>(0x82, S2C, 0, Rel, (w, m) => w.Int(m.ServerVersion),
            (ref WireReader r) => new ProtocolRejectedMessage(r.Int()));
        Define<SpawnPlayerMessage>(0x83, S2C, 0, Rel, (w, m) => { w.Int(m.PeerId); w.String(m.AvatarId); w.String(m.DisplayName); w.String(m.AppearanceJson); w.Float(m.X); w.Float(m.Y); w.Float(m.Z); w.Int(m.State); },
            (ref WireReader r) => { var peer = r.Int(); var avatar = r.String(WireLimits.Id); var name = r.String(WireLimits.DisplayName); var look = r.String(WireLimits.AppearanceJson); var x = r.Float(); var y = r.Float(); var z = r.Float(); var state = r.Int(); return new SpawnPlayerMessage(peer, avatar, name, look, x, y, z, state); });
        Define<DespawnPlayerMessage>(0x84, S2C, 0, Rel, (w, m) => { w.Int(m.PeerId); w.Bool(m.Suspended); },
            (ref WireReader r) => { var peer = r.Int(); var suspended = r.Bool(); return new DespawnPlayerMessage(peer, suspended); });
        Define<AppearanceChangedMessage>(0x85, S2C, 0, Rel, (w, m) => { w.Int(m.PeerId); w.String(m.DisplayName); w.String(m.AppearanceJson); },
            (ref WireReader r) => { var peer = r.Int(); var name = r.String(WireLimits.DisplayName); var look = r.String(WireLimits.AppearanceJson); return new AppearanceChangedMessage(peer, name, look); });
        Define<AttachmentLiveChangedMessage>(0x86, S2C, 0, Rel, (w, m) => { w.Int(m.PeerId); w.String(m.Id); w.Floats(m.Values); },
            (ref WireReader r) => { var peer = r.Int(); var id = r.String(WireLimits.Id); var values = r.Floats(WireLimits.AttachmentValues); return new AttachmentLiveChangedMessage(peer, id, values); });
        Define<ReceiveChatMessage>(0x87, S2C, 0, Rel, (w, m) => { w.Int(m.PeerId); w.String(m.DisplayName); w.String(m.Message); w.Long(m.UnixMilliseconds); },
            (ref WireReader r) => { var peer = r.Int(); var name = r.String(WireLimits.DisplayName); var text = r.String(WireLimits.Chat); var at = r.Long(); return new ReceiveChatMessage(peer, name, text, at); });
        Define<BuildOperationAppliedMessage>(0x88, S2C, 0, Rel, (w, m) => w.String(m.OperationJson),
            (ref WireReader r) => new BuildOperationAppliedMessage(r.String(WireLimits.OperationJson)));
        Define<BuildOperationRejectedMessage>(0x89, S2C, 0, Rel, (w, m) => w.String(m.Code),
            (ref WireReader r) => new BuildOperationRejectedMessage(r.String(WireLimits.Code)));
        Define<TerrainFullMessage>(0x8A, S2C, 0, Rel, (w, m) => { w.Bytes(m.Data); w.Bool(m.CanEdit); },
            (ref WireReader r) => { var data = r.Bytes(WireLimits.TerrainFull); var canEdit = r.Bool(); return new TerrainFullMessage(data, canEdit); });
        Define<TerrainPatchedMessage>(0x8B, S2C, 0, Rel, (w, m) => { w.Bytes(m.Patch); w.Long(m.Revision); },
            (ref WireReader r) => { var patch = r.Bytes(WireLimits.TerrainPatch); var revision = r.Long(); return new TerrainPatchedMessage(patch, revision); });
        Define<TerrainEditedMessage>(0x8C, S2C, 0, Rel, (w, m) => { w.Int(m.Tool); w.Float(m.X); w.Float(m.Z); w.Float(m.Radius); w.Float(m.Strength); w.Int(m.Layer); w.Long(m.Revision); },
            (ref WireReader r) => { var tool = r.Int(); var x = r.Float(); var z = r.Float(); var radius = r.Float(); var strength = r.Float(); var layer = r.Int(); var revision = r.Long(); return new TerrainEditedMessage(tool, x, z, radius, strength, layer, revision); });
        Define<TerrainEditRejectedMessage>(0x8D, S2C, 0, Rel, (w, m) => w.String(m.Code),
            (ref WireReader r) => new TerrainEditRejectedMessage(r.String(WireLimits.Code)));
        Define<SeaLevelChangedMessage>(0x8E, S2C, 0, Rel, (w, m) => { w.Float(m.Level); w.Long(m.Revision); },
            (ref WireReader r) => { var level = r.Float(); var revision = r.Long(); return new SeaLevelChangedMessage(level, revision); });
        Define<TeleportResultMessage>(0x8F, S2C, 0, Rel, (w, m) => { w.Bool(m.Ok); w.String(m.Code); w.Float(m.X); w.Float(m.Z); w.Float(m.Yaw); },
            (ref WireReader r) => { var ok = r.Bool(); var code = r.String(WireLimits.Code); var x = r.Float(); var z = r.Float(); var yaw = r.Float(); return new TeleportResultMessage(ok, code, x, z, yaw); });
        Define<VehicleSpawnedMessage>(0x90, S2C, 0, Rel, (w, m) => { w.Int(m.VehicleId); w.String(m.Name); w.String(m.Type); w.Float(m.X); w.Float(m.Y); w.Float(m.Z); w.Float(m.Yaw); },
            (ref WireReader r) => { var id = r.Int(); var name = r.String(WireLimits.VehicleName); var type = r.String(WireLimits.VehicleType); var x = r.Float(); var y = r.Float(); var z = r.Float(); var yaw = r.Float(); return new VehicleSpawnedMessage(id, name, type, x, y, z, yaw); });
        Define<VehicleRejectedMessage>(0x91, S2C, 0, Rel, (w, m) => w.String(m.Code),
            (ref WireReader r) => new VehicleRejectedMessage(r.String(WireLimits.Code)));
        Define<GestureChangedMessage>(0x92, S2C, 0, Rel, (w, m) => { w.Int(m.PeerId); w.Int(m.GestureId); },
            (ref WireReader r) => { var peer = r.Int(); var gesture = r.Int(); return new GestureChangedMessage(peer, gesture); });
        Define<SnapshotMessage>(0x93, S2C, 2, Unrel, (w, m) => w.Bytes(m.Payload),
            (ref WireReader r) => new SnapshotMessage(r.Bytes(WireLimits.Snapshot)));
        Define<VehicleSnapshotsMessage>(0x94, S2C, 2, Unrel, (w, m) => w.Bytes(m.Payload),
            (ref WireReader r) => new VehicleSnapshotsMessage(r.Bytes(WireLimits.Snapshot)));
        Define<HeadLookChangedMessage>(0x95, S2C, 2, Seq, (w, m) => { w.Int(m.PeerId); w.Float(m.Pitch); },
            (ref WireReader r) => { var peer = r.Int(); var pitch = r.Float(); return new HeadLookChangedMessage(peer, pitch); });
        Define<ReceiveVoiceMessage>(0x96, S2C, 2, Unrel, (w, m) => { w.Int(m.SpeakerPeer); w.UInt(m.Sequence); w.Bytes(m.Data); },
            (ref WireReader r) => { var speaker = r.Int(); var sequence = r.UInt(); var data = r.Bytes(WireLimits.Voice); return new ReceiveVoiceMessage(speaker, sequence, data); });
    }

    /// <summary>O canal e o modo de entrega da mensagem.</summary>
    public static WireRoute RouteOf(IWireMessage message) => EntryOf(message).Route;

    private static Entry EntryOf(IWireMessage message) =>
        ByType.TryGetValue(message.GetType(), out var entry) ? entry : throw new ArgumentException("Mensagem sem definição no protocolo: " + message.GetType().Name);

    public static byte[] Encode(IWireMessage message)
    {
        var entry = EntryOf(message);
        var writer = new WireWriter();
        writer.Byte(entry.Id);
        entry.Write(writer, message);
        if (writer.Length > WireProtocol.MaxFrameBytes) throw new InvalidOperationException("A mensagem passa do tamanho máximo de um quadro: " + entry.Type.Name);
        return writer.ToArray();
    }

    /// <summary>Decodifica um quadro recebido. <paramref name="expected"/> é a direção que o receptor aceita.</summary>
    public static WireDecodeResult Decode(ReadOnlySpan<byte> frame, WireDirection expected)
    {
        if (frame.Length == 0) return new WireDecodeResult(null, "empty");
        if (frame.Length > WireProtocol.MaxFrameBytes) return new WireDecodeResult(null, "too_large");
        if (!ById.TryGetValue(frame[0], out var entry)) return new WireDecodeResult(null, "unknown_type");
        if (entry.Direction != expected) return new WireDecodeResult(null, "wrong_direction");
        var reader = new WireReader(frame.Slice(1));
        var message = entry.Read(ref reader);
        if (reader.Failed || message == null) return new WireDecodeResult(null, "malformed");
        if (reader.Remaining != 0) return new WireDecodeResult(null, "trailing_bytes");
        return new WireDecodeResult(message, null);
    }
}
