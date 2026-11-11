using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace LifeVerse.Api.Tests;

/// <summary>Small but really valid files for the import tests: images, sounds and glTF models (static, rigged, animated), built byte by byte.</summary>
public static class TestFiles
{
    // ---- images ------------------------------------------------------------------------------------------------------------

    private static uint Crc(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data) { crc ^= b; for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1; }
        return ~crc;
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length); output.Write(length);
        var typed = new byte[4 + data.Length]; Encoding.ASCII.GetBytes(type).CopyTo(typed, 0); data.CopyTo(typed, 4); output.Write(typed);
        Span<byte> crc = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(typed)); output.Write(crc);
    }

    /// <summary>A real 8-bit RGBA PNG of one flat colour.</summary>
    public static byte[] Png(int width, int height)
    {
        using var output = new MemoryStream(); output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height); header[8] = 8; header[9] = 6;
        Chunk(output, "IHDR", header);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
            for (var y = 0; y < height; y++) { z.WriteByte(0); for (var x = 0; x < width; x++) z.Write([0x12, 0xF2, 0xE0, 0xFF]); }
        Chunk(output, "IDAT", raw.ToArray()); Chunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>A JPEG with the structure of a real one (start, size, scan, end); enough for the server checks.</summary>
    public static byte[] Jpeg(int width, int height, bool ended = true)
    {
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0 };
        bytes.AddRange([0xFF, 0xC0, 0, 17, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1]);
        bytes.AddRange([0xFF, 0xDA, 0, 12, 3, 1, 0, 2, 0x11, 3, 0x11, 0, 63, 0]); bytes.AddRange(Enumerable.Repeat((byte)0x55, 40));
        if (ended) bytes.AddRange([0xFF, 0xD9]);
        return [.. bytes];
    }

    private static byte[] Riff(string fourCc, byte[] payload)
    {
        var body = new List<byte>(); body.AddRange(Encoding.ASCII.GetBytes("WEBP")); body.AddRange(Encoding.ASCII.GetBytes(fourCc));
        var size = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)payload.Length); body.AddRange(size); body.AddRange(payload); if (payload.Length % 2 == 1) body.Add(0);
        var all = new List<byte>(Encoding.ASCII.GetBytes("RIFF")); var total = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(total, (uint)body.Count); all.AddRange(total); all.AddRange(body); return [.. all];
    }

    /// <summary>The headers of the three kinds of WebP: lossy (VP8), lossless (VP8L) and extended (VP8X).</summary>
    public static byte[] WebP(string kind, int width, int height)
    {
        switch (kind)
        {
            case "VP8 ": { var p = new byte[30]; p[3] = 0x9D; p[4] = 0x01; p[5] = 0x2A; BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6), (ushort)width); BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8), (ushort)height); return Riff("VP8 ", p); }
            case "VP8L": { var p = new byte[20]; p[0] = 0x2F; BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(1), (uint)((width - 1) | ((height - 1) << 14))); return Riff("VP8L", p); }
            default: { var p = new byte[18]; p[4] = (byte)(width - 1); p[5] = (byte)((width - 1) >> 8); p[6] = (byte)((width - 1) >> 16); p[7] = (byte)(height - 1); p[8] = (byte)((height - 1) >> 8); p[9] = (byte)((height - 1) >> 16); return Riff("VP8X", p); }
        }
    }

    // ---- sounds ------------------------------------------------------------------------------------------------------------

    /// <summary>A real WAV: <paramref name="format"/> 1 is integer PCM, 3 is float, 2 is ADPCM; 0xFFFE writes the extensible header some tools use for 24 bits.</summary>
    public static byte[] Wav(int format = 1, int channels = 1, int bits = 16, int rate = 22050, int frames = 2205)
    {
        var bytesPerFrame = channels * bits / 8; var data = new byte[frames * bytesPerFrame];
        for (var i = 0; i < frames; i++) for (var c = 0; c < channels; c++)
        {
            var sample = Math.Sin(i * 2 * Math.PI * 440 / rate) * .5; var at = i * bytesPerFrame + c * bits / 8;
            switch (bits)
            {
                case 8: data[at] = (byte)(128 + sample * 100); break;
                case 16: BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(at), (short)(sample * short.MaxValue)); break;
                case 24: { var v = (int)(sample * 8388607); data[at] = (byte)v; data[at + 1] = (byte)(v >> 8); data[at + 2] = (byte)(v >> 16); break; }
                case 32 when format == 3: BitConverter.TryWriteBytes(data.AsSpan(at), (float)sample); break;
                case 32: BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at), (int)(sample * int.MaxValue)); break;
            }
        }
        var extensible = format == 0xFFFE; var fmtSize = extensible ? 40 : 16;
        using var output = new MemoryStream(); using var w = new BinaryWriter(output);
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(4 + 8 + fmtSize + 8 + data.Length); w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(fmtSize); w.Write((short)format); w.Write((short)channels); w.Write(rate); w.Write(rate * bytesPerFrame); w.Write((short)bytesPerFrame); w.Write((short)bits);
        if (extensible) { w.Write((short)22); w.Write((short)bits); w.Write(channels == 1 ? 4 : 3); w.Write((short)1); w.Write(new byte[] { 0, 0, 0, 0, 0x10, 0, 0x80, 0, 0, 0xAA, 0, 0x38, 0x9B, 0x71 }); }
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data.Length); w.Write(data);
        return output.ToArray();
    }

    /// <summary>The first page of an Ogg stream with a Vorbis (or Opus) identification header, followed by filler; the structure the server checks.</summary>
    public static byte[] Ogg(bool opus = false)
    {
        var packet = opus ? Encoding.ASCII.GetBytes("OpusHead").Concat(new byte[11]).ToArray() : new byte[] { 0x01 }.Concat(Encoding.ASCII.GetBytes("vorbis")).Concat(new byte[23]).ToArray();
        var page = new List<byte>(Encoding.ASCII.GetBytes("OggS")) { 0, 2 }; page.AddRange(new byte[8]); page.AddRange([1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, (byte)packet.Length]); page.AddRange(packet); page.AddRange(new byte[200]);
        return [.. page];
    }

    /// <summary>An MP3 with an ID3 tag and a few Layer III frames (MPEG-1, 128 kbit/s, 44.1 kHz).</summary>
    public static byte[] Mp3(bool id3 = true)
    {
        var bytes = new List<byte>();
        if (id3) { bytes.AddRange("ID3"u8.ToArray()); bytes.AddRange([3, 0, 0, 0, 0, 0, 20]); bytes.AddRange(new byte[20]); }
        for (var frame = 0; frame < 4; frame++) { bytes.AddRange([0xFF, 0xFB, 0x90, 0x00]); bytes.AddRange(new byte[413]); }
        return [.. bytes];
    }

    // ---- models --------------------------------------------------------------------------------------------------------------

    public sealed record ModelOptions(bool Rigged = false, bool Animated = false, bool Mesh = true, bool CutOff = false, int Joints = 2);

    /// <summary>
    /// A GLB with real data: a triangle mesh, optionally bound to a skeleton (<c>Joints</c> bones, skin weights and inverse bind matrices) and optionally with one animation that turns the
    /// bones. <c>CutOff</c> removes the end of the binary part, as a broken export does.
    /// </summary>
    public static byte[] Glb(ModelOptions? options = null)
    {
        options ??= new ModelOptions();
        var bin = new List<byte>(); var views = new List<object>(); var accessors = new List<object>();
        int View(byte[] data, int? target = null) { while (bin.Count % 4 != 0) bin.Add(0); var offset = bin.Count; bin.AddRange(data); views.Add(target is null ? new { buffer = 0, byteOffset = offset, byteLength = data.Length } : new { buffer = 0, byteOffset = offset, byteLength = data.Length, target }); return views.Count - 1; }
        int Accessor(int view, int componentType, int count, string type, object? min = null, object? max = null) { accessors.Add(min is null ? new { bufferView = view, componentType, count, type } : new { bufferView = view, componentType, count, type, min, max }); return accessors.Count - 1; }
        static byte[] Floats(params float[] values) { var b = new byte[values.Length * 4]; for (var i = 0; i < values.Length; i++) BitConverter.TryWriteBytes(b.AsSpan(i * 4), values[i]); return b; }

        var nodes = new List<object>(); var meshes = new List<object>(); var skins = new List<object>(); var animations = new List<object>(); var primitive = new Dictionary<string, object>();
        var sceneNodes = new List<int>();
        if (options.Mesh)
        {
            var position = Accessor(View(Floats(0, 0, 0, 1, 0, 0, 0, 1, 0), 34962), 5126, 3, "VEC3", new[] { 0f, 0f, 0f }, new[] { 1f, 1f, 0f });
            var indices = Accessor(View([0, 0, 1, 0, 2, 0], 34963), 5123, 3, "SCALAR");
            var attributes = new Dictionary<string, object> { ["POSITION"] = position };
            if (options.Rigged)
            {
                var joints = new byte[12]; for (var v = 0; v < 3; v++) joints[v * 4] = (byte)(v % options.Joints);
                attributes["JOINTS_0"] = Accessor(View(joints, 34962), 5121, 3, "VEC4");
                attributes["WEIGHTS_0"] = Accessor(View(Floats(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0), 34962), 5126, 3, "VEC4");
            }
            primitive = new Dictionary<string, object> { ["attributes"] = attributes, ["indices"] = indices, ["material"] = 0 };
            meshes.Add(new { primitives = new[] { primitive } });
        }
        // node 0 is the mesh (or an empty root); the bones follow
        nodes.Add(options.Mesh ? (options.Rigged ? new { name = "Corpo", mesh = 0, skin = 0 } : new { name = "Corpo", mesh = 0 }) : (object)new { name = "Raiz" });
        sceneNodes.Add(0);
        var jointNodes = new List<int>();
        if (options.Rigged || options.Animated)
        {
            for (var j = 0; j < options.Joints; j++) { nodes.Add(new { name = "Osso" + j, translation = new[] { 0f, j * .5f, 0f } }); jointNodes.Add(nodes.Count - 1); }
            sceneNodes.AddRange(jointNodes.Take(1));
        }
        if (options.Rigged)
        {
            var matrices = new float[16 * options.Joints]; for (var j = 0; j < options.Joints; j++) { matrices[j * 16] = matrices[j * 16 + 5] = matrices[j * 16 + 10] = matrices[j * 16 + 15] = 1; matrices[j * 16 + 13] = -j * .5f; }
            skins.Add(new { inverseBindMatrices = Accessor(View(Floats(matrices)), 5126, options.Joints, "MAT4"), joints = jointNodes, skeleton = jointNodes[0] });
        }
        if (options.Animated)
        {
            var times = Accessor(View(Floats(0, .5f, 1)), 5126, 3, "SCALAR", new[] { 0f }, new[] { 1f });
            var rotations = Accessor(View(Floats(0, 0, 0, 1, 0, .38f, 0, .92f, 0, 0, 0, 1)), 5126, 3, "VEC4");
            animations.Add(new { name = "Girar", samplers = new[] { new { input = times, output = rotations, interpolation = "LINEAR" } }, channels = new[] { new { sampler = 0, target = new { node = jointNodes[0], path = "rotation" } } } });
        }
        while (bin.Count % 4 != 0) bin.Add(0);
        var document = new Dictionary<string, object?>
        {
            ["asset"] = new { version = "2.0", generator = "lifeverse-tests" }, ["scene"] = 0, ["scenes"] = new[] { new { nodes = sceneNodes } }, ["nodes"] = nodes,
            ["buffers"] = new[] { new { byteLength = bin.Count } }, ["bufferViews"] = views, ["accessors"] = accessors
        };
        if (options.Mesh) { document["meshes"] = meshes; document["materials"] = new[] { new { name = "Mat", pbrMetallicRoughness = new { baseColorFactor = new[] { .8f, .5f, .3f, 1f }, metallicFactor = 0f, roughnessFactor = .8f } } }; }
        if (skins.Count > 0) document["skins"] = skins;
        if (animations.Count > 0) document["animations"] = animations;
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document)); var padded = json.Concat(Enumerable.Repeat((byte)' ', (4 - json.Length % 4) % 4)).ToArray();
        var binary = options.CutOff ? bin.Take(bin.Count / 2).ToArray() : [.. bin];
        using var output = new MemoryStream(); using var w = new BinaryWriter(output);
        w.Write(0x46546C67); w.Write(2); w.Write(12 + 8 + padded.Length + 8 + binary.Length);
        w.Write(padded.Length); w.Write(0x4E4F534A); w.Write(padded); w.Write(binary.Length); w.Write(0x004E4942); w.Write(binary);
        return output.ToArray();
    }
}
