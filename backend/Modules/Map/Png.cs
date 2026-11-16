using System.IO.Compression;

namespace LifeVerse.Api.Modules.Map;

/// <summary>The smallest PNG writer that does the job: 8-bit RGB, no interlacing, one data chunk. Enough for the pictures of the map, with no image library.</summary>
public static class Png
{
    private static readonly uint[] Table = MakeTable();

    private static uint[] MakeTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++) { var c = n; for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; table[n] = c; }
        return table;
    }

    private static uint Crc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in type) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void Chunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length); output.Write(number);
        var name = System.Text.Encoding.ASCII.GetBytes(type); output.Write(name); output.Write(data);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, Crc(name, data)); output.Write(number);
    }

    /// <summary><paramref name="rgb"/> is width × height × 3 bytes, row by row.</summary>
    public static byte[] Encode(int width, int height, byte[] rgb)
    {
        if (width <= 0 || height <= 0 || rgb.Length != width * height * 3) throw new ArgumentException("The pixels do not fit the size.");
        using var output = new MemoryStream(); output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width); System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height); header[8] = 8; header[9] = 2; // 8 bits, RGB
        Chunk(output, "IHDR", header);
        using var raw = new MemoryStream(height * (width * 3 + 1));
        for (var y = 0; y < height; y++) { raw.WriteByte(0); raw.Write(rgb, y * width * 3, width * 3); } // filter "none" for every row
        using var packed = new MemoryStream(); using (var z = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw.GetBuffer(), 0, (int)raw.Length);
        Chunk(output, "IDAT", packed.ToArray()); Chunk(output, "IEND", []);
        return output.ToArray();
    }
}
