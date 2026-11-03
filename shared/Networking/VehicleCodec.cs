using System.Buffers.Binary;
using LifeVerse.Shared.Vehicles;

namespace LifeVerse.Shared.Networking;

/// Compact vehicle state for the unreliable snapshot channel. Positions stay full floats because islands can be
/// larger than the 16-bit range the player snapshots use.
public static class VehicleCodec
{
    private const int HeaderSize = 6;
    private const int VehicleSize = 28;

    public static byte[] Encode(uint serverTick, IReadOnlyCollection<VehicleSnapshot> vehicles)
    {
        if (vehicles.Count > NetworkProtocol.MaxVehicles) throw new ArgumentOutOfRangeException(nameof(vehicles));
        var data = new byte[HeaderSize + vehicles.Count * VehicleSize];
        var span = data.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, serverTick);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)vehicles.Count);
        var offset = HeaderSize;
        foreach (var v in vehicles)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span[offset..], v.Id); offset += 4;
            BinaryPrimitives.WriteSingleLittleEndian(span[offset..], v.X); offset += 4;
            BinaryPrimitives.WriteSingleLittleEndian(span[offset..], v.Y); offset += 4;
            BinaryPrimitives.WriteSingleLittleEndian(span[offset..], v.Z); offset += 4;
            BinaryPrimitives.WriteInt16LittleEndian(span[offset..], Angle(v.Yaw)); offset += 2;
            BinaryPrimitives.WriteInt16LittleEndian(span[offset..], Angle(v.Pitch)); offset += 2;
            BinaryPrimitives.WriteSingleLittleEndian(span[offset..], v.Speed); offset += 4;
            BinaryPrimitives.WriteInt32LittleEndian(span[offset..], v.DriverPeer); offset += 4;
        }
        return data;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, out uint serverTick, out IReadOnlyList<VehicleSnapshot> vehicles)
    {
        serverTick = 0; vehicles = [];
        if (data.Length < HeaderSize) return false;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        if (count > NetworkProtocol.MaxVehicles || data.Length != HeaderSize + count * VehicleSize) return false;
        var tick = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var list = new VehicleSnapshot[count]; var offset = HeaderSize;
        for (var i = 0; i < count; i++)
        {
            var id = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]); offset += 4;
            var x = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]); offset += 4;
            var y = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]); offset += 4;
            var z = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]); offset += 4;
            var yaw = Unangle(BinaryPrimitives.ReadInt16LittleEndian(data[offset..])); offset += 2;
            var pitch = Unangle(BinaryPrimitives.ReadInt16LittleEndian(data[offset..])); offset += 2;
            var speed = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]); offset += 4;
            var driver = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]); offset += 4;
            if (!float.IsFinite(x + y + z + speed)) return false;
            list[i] = new VehicleSnapshot(id, x, y, z, yaw, pitch, speed, driver);
        }
        serverTick = tick; vehicles = list;
        return true;
    }

    private static short Angle(float radians) => (short)Math.Clamp(MathF.Round(radians / MathF.PI * short.MaxValue), short.MinValue, short.MaxValue);
    private static float Unangle(short value) => value / (float)short.MaxValue * MathF.PI;
}
