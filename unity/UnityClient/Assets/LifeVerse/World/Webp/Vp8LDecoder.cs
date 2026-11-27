using System;
using System.Collections.Generic;

namespace LifeVerse.Unity.World.Webp
{
    /// <summary>The WebP lossless format (VP8L): Huffman coded pixels with copies and a colour cache, then up to four reversible transforms. Pixels are 0xAARRGGBB.</summary>
    internal sealed class Vp8LDecoder
    {
        private readonly byte[] _data; private readonly int _end; private int _next; private ulong _bits; private int _count, _past;

        private Vp8LDecoder(byte[] data, int at, int size) { _data = data; _next = at; _end = at + size; }

        // bits come least significant first
        private int Read(int n)
        {
            while (_count < n)
            {
                if (_next < _end) _bits |= (ulong)_data[_next++] << _count; else if (++_past > 8) throw new WebpException("the stream ended early");
                _count += 8;
            }
            var value = (int)(_bits & ((1UL << n) - 1)); _bits >>= n; _count -= n; return value;
        }

        internal static WebpPicture Decode(byte[] data, int at, int size)
        {
            if (size < 5 || data[at] != 0x2F) throw new WebpException("lossless signature");
            var decoder = new Vp8LDecoder(data, at + 1, size - 1);
            int width = decoder.Read(14) + 1, height = decoder.Read(14) + 1; decoder.Read(1); if (decoder.Read(3) != 0) throw new WebpException("lossless version");
            WebpDecoder.Check(width, height);
            var argb = decoder.Image(width, height);
            var pixels = new byte[width * height * 4];
            for (int i = 0, o = 0; i < argb.Length; i++, o += 4) { var p = argb[i]; pixels[o] = (byte)(p >> 16); pixels[o + 1] = (byte)(p >> 8); pixels[o + 2] = (byte)p; pixels[o + 3] = (byte)(p >> 24); }
            return new WebpPicture { Width = width, Height = height, Pixels = pixels };
        }

        /// <summary>The compressed transparency of a lossy picture: a lossless stream without its header; the values are in the green channel.</summary>
        internal static uint[] DecodeAlpha(byte[] data, int at, int size, int width, int height) => new Vp8LDecoder(data, at, size).Image(width, height);

        private sealed class Transform { public int Kind, Bits, Width, Height; public uint[] Data; }

        private uint[] Image(int width, int height)
        {
            var transforms = new List<Transform>(); var coded = width; var seen = 0;
            while (Read(1) == 1)
            {
                var t = new Transform { Kind = Read(2), Width = coded, Height = height };
                if ((seen & (1 << t.Kind)) != 0) throw new WebpException("a transform twice"); seen |= 1 << t.Kind;
                if (t.Kind == 0 || t.Kind == 1) { t.Bits = Read(3) + 2; t.Data = Stream(Sub(coded, t.Bits), Sub(height, t.Bits), false); }
                else if (t.Kind == 3)
                {
                    var colours = Read(8) + 1; var table = Stream(colours, 1, false);
                    for (var i = 1; i < colours; i++) table[i] = Add(table[i], table[i - 1]);   // the table is written as differences
                    t.Data = new uint[256]; Array.Copy(table, t.Data, colours);                 // an index past the table is transparent black
                    t.Bits = colours > 16 ? 0 : colours > 4 ? 1 : colours > 2 ? 2 : 3; coded = Sub(coded, t.Bits);
                }
                transforms.Add(t);
            }
            var pixels = Stream(coded, height, true);
            for (var i = transforms.Count - 1; i >= 0; i--) pixels = Undo(transforms[i], pixels);
            return pixels;
        }

        private static int Sub(int size, int bits) => (size + (1 << bits) - 1) >> bits;

        // ---- Huffman codes ----

        /// <summary>A canonical Huffman code: the short codes are looked up from the next 8 bits, the long ones bit by bit.</summary>
        private sealed class Code
        {
            public int Single = -1; public int[] Fast; public int[] Counts, Symbols;
        }

        private int Symbol(Code code)
        {
            if (code.Single >= 0) return code.Single;
            while (_count < 15) { if (_next < _end) _bits |= (ulong)_data[_next++] << _count; else if (++_past > 64) throw new WebpException("the stream ended early"); _count += 8; }
            var entry = code.Fast[(int)(_bits & 255)];
            if (entry != 0) { var length = entry & 15; _bits >>= length; _count -= length; return entry >> 4; }
            int value = 0, first = 0, index = 0;
            for (var length = 1; length <= 15; length++)
            {
                value |= Read(1); var count = code.Counts[length];
                if (value - first < count) return code.Symbols[index + value - first];
                index += count; first = (first + count) << 1; value <<= 1;
            }
            throw new WebpException("a code that means nothing");
        }

        private static Code Build(int[] lengths)
        {
            var code = new Code { Counts = new int[16] }; var used = 0; var last = 0;
            for (var s = 0; s < lengths.Length; s++) if (lengths[s] != 0) { code.Counts[lengths[s]]++; used++; last = s; }
            if (used <= 1) { code.Single = last; return code; }   // one symbol costs no bits
            var offsets = new int[16]; var left = 1;
            for (var length = 1; length <= 15; length++) { left = (left << 1) - code.Counts[length]; if (left < 0) throw new WebpException("too many codes"); offsets[length] = offsets[length - 1] + code.Counts[length - 1]; }
            if (left != 0) throw new WebpException("an incomplete code");
            code.Symbols = new int[used]; for (var s = 0; s < lengths.Length; s++) if (lengths[s] != 0) code.Symbols[offsets[lengths[s]]++] = s;
            code.Fast = new int[256]; int next = 0, at = 0;
            for (var length = 1; length <= 8; length++)
            {
                for (var i = 0; i < code.Counts[length]; i++, next++, at++)
                {
                    var reversed = 0; for (var b = 0; b < length; b++) reversed |= ((next >> b) & 1) << (length - 1 - b);   // the stream gives the first bit of a code first
                    for (var fill = reversed; fill < 256; fill += 1 << length) code.Fast[fill] = code.Symbols[at] << 4 | length;
                }
                next <<= 1;
            }
            return code;
        }

        private static readonly int[] CodeLengthOrder = { 17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

        private Code ReadCode(int alphabet)
        {
            var lengths = new int[alphabet];
            if (Read(1) == 1)
            {
                var two = Read(1) == 1; var first = Read(Read(1) == 1 ? 8 : 1); if (first >= alphabet) throw new WebpException("symbol"); lengths[first] = 1;
                if (two) { var second = Read(8); if (second >= alphabet) throw new WebpException("symbol"); lengths[second] = 1; }
                return Build(lengths);
            }
            var meta = new int[19]; var given = Read(4) + 4; for (var i = 0; i < given; i++) meta[CodeLengthOrder[i]] = Read(3);
            var reader = Build(meta); var budget = alphabet;
            if (Read(1) == 1) { var bits = 2 + 2 * Read(3); budget = 2 + Read(bits); if (budget > alphabet) throw new WebpException("too many lengths"); }
            var previous = 8; var symbol = 0;
            while (symbol < alphabet && budget-- > 0)
            {
                var value = Symbol(reader);
                if (value < 16) { lengths[symbol++] = value; if (value != 0) previous = value; continue; }
                var repeat = value == 16 ? 3 + Read(2) : value == 17 ? 3 + Read(3) : 11 + Read(7);
                if (symbol + repeat > alphabet) throw new WebpException("a repeat past the end");
                for (; repeat > 0; repeat--) lengths[symbol++] = value == 16 ? previous : 0;
            }
            return Build(lengths);
        }

        // ---- the pixels ----

        private int Prefixed(int symbol)
        {
            if (symbol < 4) return symbol + 1;
            var extra = (symbol - 2) >> 1; return ((2 + (symbol & 1)) << extra) + Read(extra) + 1;
        }

        private uint[] Stream(int width, int height, bool main)
        {
            var cacheBits = 0; if (Read(1) == 1) { cacheBits = Read(4); if (cacheBits < 1 || cacheBits > 11) throw new WebpException("cache size"); }
            var groupBits = 0; uint[] groupOf = null; var groups = 1; var groupWidth = 0;
            if (main && Read(1) == 1)
            {
                groupBits = Read(3) + 2; groupWidth = Sub(width, groupBits); groupOf = Stream(groupWidth, Sub(height, groupBits), false);
                for (var i = 0; i < groupOf.Length; i++) { groupOf[i] = (groupOf[i] >> 8) & 0xFFFF; if (groupOf[i] + 1 > groups) groups = (int)groupOf[i] + 1; }
            }
            var codes = new Code[groups][]; var cacheSize = cacheBits > 0 ? 1 << cacheBits : 0;
            for (var g = 0; g < groups; g++) codes[g] = new[] { ReadCode(256 + 24 + cacheSize), ReadCode(256), ReadCode(256), ReadCode(256), ReadCode(40) };
            var pixels = new uint[checked(width * height)]; var cache = new uint[cacheSize]; var at = 0; var cached = 0; var group = codes[0];
            while (at < pixels.Length)
            {
                if (groupOf != null) group = codes[groupOf[(at / width >> groupBits) * groupWidth + (at % width >> groupBits)]];
                var green = Symbol(group[0]);
                if (green < 256)
                {
                    var red = Symbol(group[1]); var blue = Symbol(group[2]); var alpha = Symbol(group[3]);
                    pixels[at++] = (uint)alpha << 24 | (uint)red << 16 | (uint)green << 8 | (uint)blue;
                }
                else if (green < 280)
                {
                    var length = Prefixed(green - 256); var distance = Prefixed(Symbol(group[4]));
                    if (distance > 120) distance -= 120; else { var near = Vp8Tables.DistanceMap[distance - 1]; distance = Math.Max(1, (near >> 4) * width + 8 - (near & 15)); }
                    if (distance > at || length > pixels.Length - at) throw new WebpException("a copy from outside the picture");
                    for (; length > 0; length--, at++) pixels[at] = pixels[at - distance];
                }
                else
                {
                    var key = green - 280; if (key >= cacheSize) throw new WebpException("cache index");
                    for (; cached < at; cached++) cache[(int)(unchecked(0x1E35A7BDu * pixels[cached]) >> (32 - cacheBits))] = pixels[cached];
                    pixels[at++] = cache[key];
                }
            }
            return pixels;
        }

        // ---- undoing the transforms ----

        private static uint Add(uint a, uint b)
        {
            var low = (a & 0x00FF00FFu) + (b & 0x00FF00FFu); var high = (a & 0xFF00FF00u) >> 8; high += (b & 0xFF00FF00u) >> 8;
            return (low & 0x00FF00FFu) | ((high & 0x00FF00FFu) << 8);
        }

        private static uint Average(uint a, uint b) => (((a ^ b) & 0xFEFEFEFEu) >> 1) + (a & b);
        private static int Channel(uint p, int shift) => (int)(p >> shift) & 255;
        private static int Byte(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

        private static uint Select(uint left, uint top, uint corner)
        {
            var difference = 0;
            for (var s = 0; s < 32; s += 8) difference += Math.Abs(Channel(top, s) - Channel(corner, s)) - Math.Abs(Channel(left, s) - Channel(corner, s));
            // difference = how far the guess (left + top - corner) is from the left, minus how far it is from the top
            return difference < 0 ? left : top;
        }

        private static uint Clamped(uint a, uint b, uint c) { uint r = 0; for (var s = 0; s < 32; s += 8) r |= (uint)Byte(Channel(a, s) + Channel(b, s) - Channel(c, s)) << s; return r; }
        private static uint ClampedHalf(uint a, uint b) { uint r = 0; for (var s = 0; s < 32; s += 8) r |= (uint)Byte(Channel(a, s) + (Channel(a, s) - Channel(b, s)) / 2) << s; return r; }

        private static uint[] Undo(Transform t, uint[] pixels)
        {
            int width = t.Width, height = t.Height;
            switch (t.Kind)
            {
                case 0:   // each pixel was written as the difference from a guess made of its neighbours
                {
                    var across = Sub(width, t.Bits);
                    for (var y = 0; y < height; y++)
                        for (var x = 0; x < width; x++)
                        {
                            var i = y * width + x; uint guess;
                            if (y == 0) guess = x == 0 ? 0xFF000000u : pixels[i - 1];
                            else if (x == 0) guess = pixels[i - width];
                            else
                            {
                                uint left = pixels[i - 1], top = pixels[i - width], corner = pixels[i - width - 1], right = pixels[i - width + 1];   // past the last column, the first pixel of this row
                                switch ((t.Data[(y >> t.Bits) * across + (x >> t.Bits)] >> 8) & 15)
                                {
                                    case 1: guess = left; break;
                                    case 2: guess = top; break;
                                    case 3: guess = right; break;
                                    case 4: guess = corner; break;
                                    case 5: guess = Average(Average(left, right), top); break;
                                    case 6: guess = Average(left, corner); break;
                                    case 7: guess = Average(left, top); break;
                                    case 8: guess = Average(corner, top); break;
                                    case 9: guess = Average(top, right); break;
                                    case 10: guess = Average(Average(left, corner), Average(top, right)); break;
                                    case 11: guess = Select(left, top, corner); break;
                                    case 12: guess = Clamped(left, top, corner); break;
                                    case 13: guess = ClampedHalf(Average(left, top), corner); break;
                                    default: guess = 0xFF000000u; break;
                                }
                            }
                            pixels[i] = Add(pixels[i], guess);
                        }
                    return pixels;
                }
                case 1:   // red and blue were written less what green (and red) already said about them
                {
                    var across = Sub(width, t.Bits);
                    for (var y = 0; y < height; y++)
                        for (var x = 0; x < width; x++)
                        {
                            var i = y * width + x; var m = t.Data[(y >> t.Bits) * across + (x >> t.Bits)]; var p = pixels[i];
                            var green = (sbyte)(p >> 8); var red = (int)(p >> 16) & 255; var blue = (int)p & 255;
                            red = (red + (((sbyte)m * green) >> 5)) & 255;
                            blue = (blue + (((sbyte)(m >> 8) * green) >> 5) + (((sbyte)(m >> 16) * (sbyte)red) >> 5)) & 255;
                            pixels[i] = (p & 0xFF00FF00u) | (uint)red << 16 | (uint)blue;
                        }
                    return pixels;
                }
                case 2:   // green was taken from red and blue
                    for (var i = 0; i < pixels.Length; i++) { var p = pixels[i]; var g = (p >> 8) & 255; pixels[i] = (p & 0xFF00FF00u) | ((((p & 0x00FF00FFu) + (g << 16 | g)) & 0x00FF00FFu)); }
                    return pixels;
                default:  // a table of colours; with few colours several indices share a green byte
                {
                    var packed = Sub(width, t.Bits); var perByte = 1 << t.Bits; var bits = 8 >> t.Bits; var mask = (1 << bits) - 1;
                    var result = new uint[width * height];
                    for (var y = 0; y < height; y++)
                        for (var x = 0; x < width; x++)
                        {
                            var green = (int)(pixels[y * packed + (x >> t.Bits)] >> 8) & 255;
                            result[y * width + x] = t.Data[(green >> ((x & (perByte - 1)) * bits)) & mask];
                        }
                    return result;
                }
            }
        }
    }
}
