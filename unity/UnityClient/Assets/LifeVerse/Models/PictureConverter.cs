#nullable enable
using System;
using System.IO;
using System.IO.Compression;

namespace LifeVerse.Unity.Models;

/// <summary>
/// TGA and BMP pictures turned into PNG, for the textures of imported models (the game takes PNG, JPG and WebP, and old modelling programs write these two).
/// TGA: true colour (15, 16, 24 and 32 bits), grey and with a palette, plain or run-length packed, in any of the four directions. BMP: 8 bits with a palette, 24 and 32 bits,
/// uncompressed or with the usual bit masks, bottom-up or top-down. Anything else is refused.
/// </summary>
public static class PictureConverter
{
    public const int MaxSide = 8192, MaxPixels = 16 * 1024 * 1024;

    public static bool TryToPng(byte[] source, string extension, out byte[] png)
    {
        png = Array.Empty<byte>();
        try
        {
            var picture = extension.ToLowerInvariant() == ".bmp" ? ReadBmp(source) : ReadTga(source);
            if (picture is not { } p) return false;
            png = WritePng(p.Width, p.Height, p.Rgba); return true;
        }
        catch (Exception exception) when (exception is IndexOutOfRangeException or ArgumentException or OverflowException) { return false; }   // a cut or made-up file
    }

    private readonly struct Picture
    {
        public readonly int Width, Height; public readonly byte[] Rgba;   // top row first
        public Picture(int width, int height) { Width = width; Height = height; Rgba = new byte[width * height * 4]; }
    }

    private static bool SizeFits(int width, int height) => width > 0 && height > 0 && width <= MaxSide && height <= MaxSide && (long)width * height <= MaxPixels;
    private static int U16(byte[] b, int at) => b[at] | b[at + 1] << 8;
    private static int I32(byte[] b, int at) => b[at] | b[at + 1] << 8 | b[at + 2] << 16 | b[at + 3] << 24;

    // ---- TGA -----------------------------------------------------------------------------------------------------------------------------
    private static Picture? ReadTga(byte[] b)
    {
        if (b.Length < 18) return null;
        int idLength = b[0], mapType = b[1], type = b[2], mapFirst = U16(b, 3), mapCount = U16(b, 5), mapBits = b[7], width = U16(b, 12), height = U16(b, 14), bits = b[16], descriptor = b[17];
        var packed = type >= 8; var kind = packed ? type - 8 : type;
        if (kind is not (1 or 2 or 3) || !SizeFits(width, height) || mapType > 1) return null;
        if (kind == 1 && (mapType != 1 || bits != 8 || mapBits is not (15 or 16 or 24 or 32))) return null;
        if (kind == 2 && bits is not (15 or 16 or 24 or 32)) return null;
        if (kind == 3 && bits is not (8 or 16)) return null;
        var at = 18 + idLength;
        var mapSize = mapType == 1 ? mapCount * ((mapBits + 7) / 8) : 0; var map = at; at += mapSize; if (at > b.Length) return null;
        var size = (bits + 7) / 8; var picture = new Picture(width, height); var alphaBits = descriptor & 0x0F;
        var rightToLeft = (descriptor & 0x10) != 0; var topFirst = (descriptor & 0x20) != 0;

        void Colour(int from, int depth, int to)
        {
            if (depth is 15 or 16)
            {
                var v = U16(b, from); picture.Rgba[to] = (byte)(((v >> 10) & 31) * 255 / 31); picture.Rgba[to + 1] = (byte)(((v >> 5) & 31) * 255 / 31); picture.Rgba[to + 2] = (byte)((v & 31) * 255 / 31);
                picture.Rgba[to + 3] = depth == 16 && alphaBits > 0 && (v & 0x8000) == 0 ? (byte)0 : (byte)255;
            }
            else { picture.Rgba[to] = b[from + 2]; picture.Rgba[to + 1] = b[from + 1]; picture.Rgba[to + 2] = b[from]; picture.Rgba[to + 3] = depth == 32 ? b[from + 3] : (byte)255; }
        }

        void Pixel(int from, int index)
        {
            var x = index % width; var y = index / width; if (rightToLeft) x = width - 1 - x; if (!topFirst) y = height - 1 - y; var to = (y * width + x) * 4;
            if (kind == 2) Colour(from, bits, to);
            else if (kind == 3) { var grey = b[from]; picture.Rgba[to] = picture.Rgba[to + 1] = picture.Rgba[to + 2] = grey; picture.Rgba[to + 3] = bits == 16 ? b[from + 1] : (byte)255; }
            else
            {
                var entry = b[from] - mapFirst; if (entry < 0 || entry >= mapCount) throw new ArgumentException("palette");
                Colour(map + entry * ((mapBits + 7) / 8), mapBits, to);
            }
        }

        var total = width * height;
        if (!packed) { if (at + (long)total * size > b.Length) return null; for (var i = 0; i < total; i++) Pixel(at + i * size, i); }
        else
        {
            var i = 0;
            while (i < total)
            {
                var header = b[at++]; var run = (header & 0x7F) + 1;
                if ((header & 0x80) != 0) { for (var k = 0; k < run && i < total; k++) Pixel(at, i++); at += size; }
                else for (var k = 0; k < run && i < total; k++) { Pixel(at, i++); at += size; }
            }
        }
        // many programs write 32 bits with an empty alpha channel: all transparent means opaque
        if (bits == 32 || mapBits == 32) { var any = false; for (var i = 3; i < picture.Rgba.Length && !any; i += 4) any = picture.Rgba[i] != 0; if (!any) for (var i = 3; i < picture.Rgba.Length; i += 4) picture.Rgba[i] = 255; }
        return picture;
    }

    // ---- BMP -----------------------------------------------------------------------------------------------------------------------------
    private static Picture? ReadBmp(byte[] b)
    {
        if (b.Length < 54 || b[0] != 'B' || b[1] != 'M') return null;
        int data = I32(b, 10), header = I32(b, 14), width = I32(b, 18), height = I32(b, 22), bits = U16(b, 28), compression = I32(b, 30), used = I32(b, 46);
        var topFirst = height < 0; height = Math.Abs(height);
        if (header < 40 || !SizeFits(width, height) || bits is not (8 or 24 or 32) || compression is not (0 or 3) || compression == 3 && bits != 32) return null;
        uint red = 0x00FF0000, green = 0x0000FF00, blue = 0x000000FF, alpha = 0;
        if (compression == 3) { red = (uint)I32(b, 54); green = (uint)I32(b, 58); blue = (uint)I32(b, 62); if (header >= 56) alpha = (uint)I32(b, 66); }
        else if (bits == 32) alpha = 0xFF000000;
        var palette = 14 + header; var colours = bits == 8 ? (used > 0 && used <= 256 ? used : 256) : 0;
        var stride = (width * bits + 31) / 32 * 4; if (data < 0 || data + (long)stride * height > b.Length) return null;
        var picture = new Picture(width, height);
        static byte Part(uint value, uint mask, byte missing)
        {
            if (mask == 0) return missing; var shift = 0; while (((mask >> shift) & 1) == 0) shift++; var max = mask >> shift; return (byte)(((value & mask) >> shift) * 255UL / max);
        }
        for (var y = 0; y < height; y++)
        {
            var row = data + (topFirst ? y : height - 1 - y) * stride;
            for (var x = 0; x < width; x++)
            {
                var to = (y * width + x) * 4;
                if (bits == 32) { var v = (uint)I32(b, row + x * 4); picture.Rgba[to] = Part(v, red, 0); picture.Rgba[to + 1] = Part(v, green, 0); picture.Rgba[to + 2] = Part(v, blue, 0); picture.Rgba[to + 3] = Part(v, alpha, 255); }
                else
                {
                    var from = row + x * 3;
                    if (bits == 8) { var entry = b[row + x]; if (entry >= colours) return null; from = palette + entry * 4; }
                    picture.Rgba[to] = b[from + 2]; picture.Rgba[to + 1] = b[from + 1]; picture.Rgba[to + 2] = b[from]; picture.Rgba[to + 3] = 255;
                }
            }
        }
        if (alpha != 0) { var any = false; for (var i = 3; i < picture.Rgba.Length && !any; i += 4) any = picture.Rgba[i] != 0; if (!any) for (var i = 3; i < picture.Rgba.Length; i += 4) picture.Rgba[i] = 255; }
        return picture;
    }

    // ---- PNG -----------------------------------------------------------------------------------------------------------------------------
    private static byte[] WritePng(int width, int height, byte[] rgba)
    {
        var raw = new byte[(width * 4 + 1) * height];
        for (var y = 0; y < height; y++) Buffer.BlockCopy(rgba, y * width * 4, raw, y * (width * 4 + 1) + 1, width * 4);   // each row starts with its filter (none)
        byte[] packed;
        using (var output = new MemoryStream())
        {
            output.WriteByte(0x78); output.WriteByte(0x9C);   // the zlib wrapper around the deflate stream
            using (var deflate = new DeflateStream(output, System.IO.Compression.CompressionLevel.Fastest, true)) deflate.Write(raw, 0, raw.Length);
            uint a = 1, s = 0; foreach (var value in raw) { a = (a + value) % 65521; s = (s + a) % 65521; }
            var check = s << 16 | a; output.WriteByte((byte)(check >> 24)); output.WriteByte((byte)(check >> 16)); output.WriteByte((byte)(check >> 8)); output.WriteByte((byte)check);
            packed = output.ToArray();
        }
        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
        var head = new byte[13]; Big(head, 0, (uint)width); Big(head, 4, (uint)height); head[8] = 8; head[9] = 6;
        Chunk(png, "IHDR", head); Chunk(png, "IDAT", packed); Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void Big(byte[] to, int at, uint value) { to[at] = (byte)(value >> 24); to[at + 1] = (byte)(value >> 16); to[at + 2] = (byte)(value >> 8); to[at + 3] = (byte)value; }

    private static void Chunk(Stream to, string kind, byte[] data)
    {
        var length = new byte[4]; Big(length, 0, (uint)data.Length); to.Write(length, 0, 4);
        var body = new byte[4 + data.Length]; for (var i = 0; i < 4; i++) body[i] = (byte)kind[i]; Buffer.BlockCopy(data, 0, body, 4, data.Length); to.Write(body, 0, body.Length);
        var crc = 0xFFFFFFFFu; foreach (var value in body) { crc ^= value; for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1; }
        var tail = new byte[4]; Big(tail, 0, ~crc); to.Write(tail, 0, 4);
    }
}
