using System.Buffers.Binary;
using System.Text;

namespace LifeVerse.Api.Modules.Assets;

/// <summary>
/// Structural checks of the files people import, done without decoding them: an image has a real header, size and ending, a sound is a kind of sound the game can play, and
/// nothing is accepted just because its name ends in the right letters. What passes here must also load in the client.
/// </summary>
public static class AssetFormats
{
    public const int MaxImageSide = 8192;

    // ---- images ------------------------------------------------------------------------------------------------------------

    /// <summary>Width and height of a PNG, JPEG or WebP, or null with the reason the file is not one the game can show.</summary>
    public static (int Width, int Height)? Image(string extension, byte[] b, out string? error)
    {
        error = null;
        (int Width, int Height)? size = extension switch { ".png" => Png(b, out error), ".jpg" or ".jpeg" => Jpeg(b, out error), ".webp" => WebP(b, out error), _ => null };
        if (size is null) { error ??= "Invalid or unsupported image header."; return null; }
        if (size.Value.Width < 1 || size.Value.Height < 1) { error = "The image has no pixels."; return null; }
        return size;
    }

    private static (int, int)? Png(byte[] b, out string? error)
    {
        error = null;
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (b.Length < 33 || !b.AsSpan(0, 8).SequenceEqual(signature)) { error = "This is not a PNG image (the header is wrong)."; return null; }
        if (BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(8)) != 13 || Encoding.ASCII.GetString(b, 12, 4) != "IHDR") { error = "The PNG is damaged (no image header)."; return null; }
        if (b.AsSpan(33).IndexOf("IEND"u8) < 0) { error = "The PNG is cut off (it has no ending). Export it again."; return null; }
        var width = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(16)); var height = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(20));
        error = PngBody(b, width, height);
        return error is null ? (width, height) : null;
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n => { var c = (uint)n; for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; return c; }).ToArray();
    private static uint Crc32(ReadOnlySpan<byte> data) { var c = 0xFFFFFFFFu; foreach (var x in data) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8); return c ^ 0xFFFFFFFFu; }

    /// <summary>Walks the chunks (checking each CRC) and inflates the pixel data to see that it is whole; null when the body is sound.</summary>
    private static string? PngBody(byte[] b, int width, int height)
    {
        const string damaged = "The PNG is damaged (the picture data is corrupt). Export it again.";
        if (width < 1 || height < 1 || width > MaxImageSide || height > MaxImageSide) return null; // size limits are reported by the caller
        var colorType = b[25]; var depth = b[24]; var interlace = b[28];
        using var pixels = new MemoryStream();
        var position = 8; var ended = false;
        while (position + 12 <= b.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(position));
            if (length > b.Length - position - 12) return damaged;
            var type = b.AsSpan(position + 4, 4);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(position + 8 + (int)length));
            if (Crc32(b.AsSpan(position + 4, 4 + (int)length)) != crc) return damaged;
            if (type.SequenceEqual("IDAT"u8)) pixels.Write(b, position + 8, (int)length);
            else if (type.SequenceEqual("IEND"u8)) { ended = true; break; }
            position += 12 + (int)length;
        }
        if (!ended || pixels.Length == 0) return damaged;
        try
        {
            pixels.Position = 0; long total = 0; var buffer = new byte[65536];
            using var inflate = new System.IO.Compression.ZLibStream(pixels, System.IO.Compression.CompressionMode.Decompress);
            int read; while ((read = inflate.Read(buffer, 0, buffer.Length)) > 0) { total += read; if (total > 1L << 31) return damaged; }
            var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
            if (channels == 0) return damaged;
            if (interlace == 0)
            {
                var rowBytes = ((long)width * channels * depth + 7) / 8;
                if (total != height * (rowBytes + 1)) return damaged;
            }
            return null;
        }
        catch (InvalidDataException) { return damaged; }
    }

    private static (int, int)? Jpeg(byte[] b, out string? error)
    {
        error = null;
        if (b.Length < 20 || b[0] != 0xFF || b[1] != 0xD8) { error = "This is not a JPEG image (the header is wrong)."; return null; }
        (int, int)? found = null;
        for (var offset = 2; offset + 9 < b.Length;)
        {
            if (b[offset] != 0xFF) { offset++; continue; }
            var marker = b[offset + 1]; offset += 2;
            if (marker is 0xD8 or 0xD9 or 0x01 or (>= 0xD0 and <= 0xD7) or 0xFF) { if (marker == 0xFF) offset--; continue; }
            if (offset + 2 > b.Length) break;
            var length = (b[offset] << 8) | b[offset + 1]; if (length < 2 || offset + length > b.Length) break;
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC)) { found = ((b[offset + 5] << 8) | b[offset + 6], (b[offset + 3] << 8) | b[offset + 4]); break; }
            offset += length;
        }
        if (found is null) { error = "The JPEG is damaged (no image size found)."; return null; }
        var scan = false; for (var i = 2; i + 1 < b.Length; i++) if (b[i] == 0xFF && b[i + 1] == 0xDA) { scan = true; break; }
        if (!scan) { error = "The JPEG is damaged (it has no picture data)."; return null; }
        var tail = b.AsSpan(Math.Max(0, b.Length - 64));
        var ended = false; for (var i = 0; i + 1 < tail.Length; i++) if (tail[i] == 0xFF && tail[i + 1] == 0xD9) { ended = true; break; }
        if (!ended) { error = "The JPEG is cut off (it has no ending). Export it again."; return null; }
        return found;
    }

    private static (int, int)? WebP(byte[] b, out string? error)
    {
        error = null;
        if (b.Length < 30 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WEBP") { error = "This is not a WebP image (the header is wrong)."; return null; }
        var riff = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4));
        if (riff + 8L > b.Length + 1) { error = "The WebP is cut off. Export it again."; return null; }
        switch (Encoding.ASCII.GetString(b, 12, 4))
        {
            case "VP8X": return (1 + b[24] + (b[25] << 8) + (b[26] << 16), 1 + b[27] + (b[28] << 8) + (b[29] << 16));
            case "VP8L": // lossless: signature 0x2F, then 14 bits of width-1 and 14 bits of height-1
                if (b[20] != 0x2F) { error = "The WebP is damaged (lossless header)."; return null; }
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(21));
                return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
            case "VP8 ": // lossy: a 3 byte frame tag, the start code 9D 01 2A and then 14 bits each of width and height
                if (b[23] != 0x9D || b[24] != 0x01 || b[25] != 0x2A) { error = "The WebP is damaged (lossy header)."; return null; }
                return (BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(26)) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(28)) & 0x3FFF);
            default: error = "This kind of WebP is not supported."; return null;
        }
    }

    // ---- sounds ------------------------------------------------------------------------------------------------------------------

    public sealed record SoundInfo(string Mime, string Format);

    /// <summary>The kind of sound (WAV PCM or float, Ogg Vorbis or MP3), or null with a message about what to export instead.</summary>
    public static SoundInfo? Sound(string extension, byte[] b, out string? error)
    {
        error = null;
        switch (extension)
        {
            case ".wav": return Wav(b, out error) ? new("audio/wav", "wav") : null;
            case ".ogg": return Ogg(b, out error) ? new("audio/ogg", "ogg") : null;
            case ".mp3": return Mp3(b, out error) ? new("audio/mpeg", "mp3") : null;
            default: error = "Sounds must be WAV, OGG (Vorbis) or MP3."; return null;
        }
    }

    private static bool Wav(byte[] b, out string? error)
    {
        error = null;
        if (b.Length < 44 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE") { error = "Invalid WAV header."; return false; }
        int format = 0, channels = 0, rate = 0, bits = 0; var data = -1L;
        for (var position = 12; position + 8 <= b.Length;)
        {
            var id = Encoding.ASCII.GetString(b, position, 4); var size = (long)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(position + 4)); var body = position + 8;
            if (id == "fmt " && size >= 16 && body + 16 <= b.Length)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(body)); channels = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(body + 2)); rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(body + 4)); bits = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(body + 14));
                if (format == 0xFFFE && size >= 26 && body + 26 <= b.Length) format = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(body + 24)); // WAVE_FORMAT_EXTENSIBLE: the real format is in the sub-format
            }
            else if (id == "data") data = Math.Min(size, b.Length - body);
            position = (int)Math.Min(int.MaxValue, body + size + (size & 1));
        }
        if (format == 0) { error = "The WAV has no format description."; return false; }
        if (data <= 0) { error = "The WAV has no sound data."; return false; }
        if (format is not (1 or 3)) { error = "Only uncompressed WAV (PCM) is supported. Export it again as PCM or use OGG."; return false; }
        if (channels is < 1 or > 2) { error = "Use a mono or stereo sound."; return false; }
        if (bits is not (8 or 16 or 24 or 32)) { error = "Use 8, 16, 24 or 32 bits per sample."; return false; }
        if (format == 3 && bits != 32) { error = "Float WAV must be 32 bits."; return false; }
        if (rate is < 4000 or > 192000) { error = "The sample rate must be between 4000 and 192000 Hz."; return false; }
        return true;
    }

    private static bool Ogg(byte[] b, out string? error)
    {
        error = null;
        if (b.Length < 58 || Encoding.ASCII.GetString(b, 0, 4) != "OggS") { error = "Invalid OGG header."; return false; }
        var segments = b[26]; var start = 27 + segments;
        if (start + 8 > b.Length) { error = "The OGG is damaged."; return false; }
        if (b[start] == 0x01 && Encoding.ASCII.GetString(b, start + 1, 6) == "vorbis") return true;
        if (Encoding.ASCII.GetString(b, start, 8) == "OpusHead") { error = "Ogg Opus is not supported. Export the sound as Ogg Vorbis (or WAV / MP3)."; return false; }
        error = "This OGG is not Ogg Vorbis, the only kind the game plays."; return false;
    }

    private static bool Mp3(byte[] b, out string? error)
    {
        error = null;
        var offset = 0;
        if (b.Length > 10 && b[0] == 'I' && b[1] == 'D' && b[2] == '3') offset = 10 + ((b[6] & 0x7F) << 21 | (b[7] & 0x7F) << 14 | (b[8] & 0x7F) << 7 | (b[9] & 0x7F));
        for (var i = offset; i + 4 <= b.Length && i < offset + 8192; i++)
        {
            if (b[i] != 0xFF || (b[i + 1] & 0xE0) != 0xE0) continue;
            var version = (b[i + 1] >> 3) & 3; var layer = (b[i + 1] >> 1) & 3; var bitrate = b[i + 2] >> 4; var sample = (b[i + 2] >> 2) & 3;
            if (version != 1 && layer != 0 && bitrate is not (0 or 15) && sample != 3 && layer == 1) return true; // Layer III, a real bitrate and sample rate
        }
        error = "Invalid MP3 file (no MP3 audio frame found)."; return false;
    }
}
