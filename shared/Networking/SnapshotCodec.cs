using System.Buffers.Binary;

namespace LifeVerse.Shared.Networking;

public static class SnapshotCodec
{
    private const int HeaderSize = 6;
    private const int PlayerSize = 23;

    public static byte[] Encode(uint serverTick, IReadOnlyCollection<PlayerSnapshot> players)
    {
        if (players.Count > NetworkProtocol.MaxPlayersPerSnapshot) throw new ArgumentOutOfRangeException(nameof(players));
        var data = new byte[HeaderSize + players.Count * PlayerSize];
        var span = data.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, serverTick);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)players.Count);
        var offset = HeaderSize;
        foreach (var player in players)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span[offset..], player.PeerId); offset += 4;
            WriteScaled(span, ref offset, player.X, NetworkProtocol.PositionScale);
            WriteScaled(span, ref offset, player.Y, NetworkProtocol.PositionScale);
            WriteScaled(span, ref offset, player.Z, NetworkProtocol.PositionScale);
            WriteScaled(span, ref offset, player.VelocityX, NetworkProtocol.VelocityScale);
            WriteScaled(span, ref offset, player.VelocityY, NetworkProtocol.VelocityScale);
            WriteScaled(span, ref offset, player.VelocityZ, NetworkProtocol.VelocityScale);
            BinaryPrimitives.WriteInt16LittleEndian(span[offset..], QuantizeAngle(player.Yaw)); offset += 2;
            span[offset++] = (byte)player.Motion;
            BinaryPrimitives.WriteUInt32LittleEndian(span[offset..], player.LastProcessedInput); offset += 4;
        }
        return data;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, out WorldSnapshot snapshot)
    {
        snapshot = new WorldSnapshot(0, []);
        if (data.Length is < HeaderSize or > NetworkProtocol.MaxSnapshotBytes) return false;
        var tick = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        if (count > NetworkProtocol.MaxPlayersPerSnapshot || data.Length != HeaderSize + count * PlayerSize) return false;
        var players = new PlayerSnapshot[count];
        var offset = HeaderSize;
        for (var i = 0; i < count; i++)
        {
            var peer = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]); offset += 4;
            var x = ReadScaled(data, ref offset, NetworkProtocol.PositionScale);
            var y = ReadScaled(data, ref offset, NetworkProtocol.PositionScale);
            var z = ReadScaled(data, ref offset, NetworkProtocol.PositionScale);
            var vx = ReadScaled(data, ref offset, NetworkProtocol.VelocityScale);
            var vy = ReadScaled(data, ref offset, NetworkProtocol.VelocityScale);
            var vz = ReadScaled(data, ref offset, NetworkProtocol.VelocityScale);
            var yaw = DequantizeAngle(BinaryPrimitives.ReadInt16LittleEndian(data[offset..])); offset += 2;
            var motion = (NetworkMotionState)data[offset++];
            var ack = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]); offset += 4;
            if (!Enum.IsDefined(motion)) return false;
            players[i] = new PlayerSnapshot(peer, x, y, z, vx, vy, vz, yaw, motion, ack);
        }
        snapshot = new WorldSnapshot(tick, players);
        return true;
    }

    private static void WriteScaled(Span<byte> data, ref int offset, float value, float scale)
    {
        var quantized = (short)Math.Clamp(MathF.Round(value * scale), short.MinValue, short.MaxValue);
        BinaryPrimitives.WriteInt16LittleEndian(data[offset..], quantized); offset += 2;
    }

    private static float ReadScaled(ReadOnlySpan<byte> data, ref int offset, float scale)
    {
        var value = BinaryPrimitives.ReadInt16LittleEndian(data[offset..]); offset += 2;
        return value / scale;
    }

    private static short QuantizeAngle(float radians) => (short)Math.Clamp(MathF.Round(radians / MathF.PI * short.MaxValue), short.MinValue, short.MaxValue);
    private static float DequantizeAngle(short value) => value / (float)short.MaxValue * MathF.PI;
}
