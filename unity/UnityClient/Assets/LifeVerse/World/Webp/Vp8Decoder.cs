using System;

namespace LifeVerse.Unity.World.Webp
{
    /// <summary>
    /// The lossy WebP format: one VP8 key frame (RFC 6386). Modes come from the first partition and coefficients from the others; every macroblock (16x16 of brightness,
    /// 8x8 of each colour) is predicted from its neighbours and corrected, the loop filter smooths the block edges, and the colours go to RGB as libwebp does it.
    /// </summary>
    internal sealed class Vp8Decoder
    {
        /// <summary>The boolean arithmetic decoder of the format.</summary>
        private sealed class Bools
        {
            private readonly byte[] _data; private readonly int _end; private int _next, _count; private uint _value, _range = 255;

            public Bools(byte[] data, int at, int size) { _data = data; _next = at; _end = at + size; _value = (uint)(Byte() << 8 | Byte()); }

            private int Byte() => _next < _end ? _data[_next++] : 0;   // past the end the stream reads as zeros

            public int Bit(int probability)
            {
                var split = 1 + (((_range - 1) * (uint)probability) >> 8); var big = split << 8; int bit;
                if (_value >= big) { bit = 1; _range -= split; _value -= big; } else { bit = 0; _range = split; }
                while (_range < 128) { _value <<= 1; _range <<= 1; if (++_count == 8) { _count = 0; _value |= (uint)Byte(); } }
                return bit;
            }

            public int Number(int bits) { var v = 0; while (bits-- > 0) v = v << 1 | Bit(128); return v; }
            /// <summary>A number with its sign after it.</summary>
            public int Signed(int bits) { var v = Number(bits); return Bit(128) == 1 ? -v : v; }
            /// <summary>A flag, then the signed number when the flag is set.</summary>
            public int Optional(int bits) => Bit(128) == 1 ? Signed(bits) : 0;
        }

        private struct Block { public byte Segment; public bool Skip, Split, Inner; public int Mode, ChromaMode; }

        // the 4x4 modes, in the order of libwebp's tables
        private const int DcMode = 0, TrueMotion = 1, Vertical = 2, Horizontal = 3;
        private static readonly int[] Bands = { 0, 1, 2, 3, 6, 4, 5, 6, 6, 6, 6, 6, 6, 6, 6, 7, 0 };
        private static readonly int[] ZigZag = { 0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15 };
        private static readonly int[][] Large = { new[] { 173, 148, 140 }, new[] { 176, 155, 140, 135 }, new[] { 180, 157, 141, 134, 130 }, new[] { 254, 254, 243, 230, 196, 177, 153, 140, 133, 130, 129 } };

        private readonly int _width, _height, _across, _down;
        private Bools _header; private Bools[] _tokens;
        private readonly byte[] _probabilities = (byte[])Vp8Tables.Coefficients.Clone();
        private bool _segments, _segmentMap, _absolute; private readonly int[] _segmentQuant = new int[4], _segmentLevel = new int[4]; private readonly int[] _segmentProbabilities = { 255, 255, 255 };
        private bool _simple, _adjust; private int _level, _sharpness, _referenceDelta, _modeDelta;
        private int _skipProbability = -1;
        private readonly int[][] _steps = new int[4][];   // per segment: y dc, y ac, y2 dc, y2 ac, chroma dc, chroma ac
        private byte[] _y, _u, _v; private int _yStride, _uvStride;

        private Vp8Decoder(int width, int height) { _width = width; _height = height; _across = (width + 15) >> 4; _down = (height + 15) >> 4; }

        internal static WebpPicture Decode(byte[] data, int at, int size)
        {
            if (size < 10) throw new WebpException("a short frame");
            var tag = data[at] | data[at + 1] << 8 | data[at + 2] << 16; var first = tag >> 5;
            if ((tag & 1) != 0 || data[at + 3] != 0x9D || data[at + 4] != 0x01 || data[at + 5] != 0x2A) throw new WebpException("not a key frame");
            int width = (data[at + 6] | data[at + 7] << 8) & 0x3FFF, height = (data[at + 8] | data[at + 9] << 8) & 0x3FFF; WebpDecoder.Check(width, height);
            if (first > size - 10) throw new WebpException("the first partition is cut off");
            var decoder = new Vp8Decoder(width, height); decoder.ReadHeader(data, at + 10, first, size - 10);
            var blocks = decoder.ReadMacroblocks(); decoder.Filter(blocks); return decoder.ToRgb();
        }

        // ---- the header ----

        private static int Clip(int v, int max) => v < 0 ? 0 : v > max ? max : v;

        private void ReadHeader(byte[] data, int at, int first, int rest)
        {
            var b = _header = new Bools(data, at, first);
            b.Bit(128); b.Bit(128);   // colour space and clamping: the same in every WebP
            _segments = b.Bit(128) == 1;
            if (_segments)
            {
                _segmentMap = b.Bit(128) == 1;
                if (b.Bit(128) == 1)
                {
                    _absolute = b.Bit(128) == 1;
                    for (var i = 0; i < 4; i++) _segmentQuant[i] = b.Optional(7);
                    for (var i = 0; i < 4; i++) _segmentLevel[i] = b.Optional(6);
                }
                if (_segmentMap) for (var i = 0; i < 3; i++) _segmentProbabilities[i] = b.Bit(128) == 1 ? b.Number(8) : 255;
            }
            _simple = b.Bit(128) == 1; _level = b.Number(6); _sharpness = b.Number(3); _adjust = b.Bit(128) == 1;
            if (_adjust && b.Bit(128) == 1)
            {
                for (var i = 0; i < 4; i++) { var v = b.Optional(6); if (i == 0) _referenceDelta = v; }   // a still picture only has the intra frame
                for (var i = 0; i < 4; i++) { var v = b.Optional(6); if (i == 0) _modeDelta = v; }       // and, of the modes, the 4x4 one
            }
            var partitions = 1 << b.Number(2);
            var sizes = at + first; var start = sizes + 3 * (partitions - 1); var end = at + rest;
            if (start > end) throw new WebpException("the partitions are cut off");
            _tokens = new Bools[partitions];
            for (var i = 0; i < partitions; i++)
            {
                var length = i == partitions - 1 ? end - start : Math.Min(end - start, data[sizes + 3 * i] | data[sizes + 3 * i + 1] << 8 | data[sizes + 3 * i + 2] << 16);
                _tokens[i] = new Bools(data, start, length); start += length;
            }
            var quant = b.Number(7); int yDc = b.Optional(4), y2Dc = b.Optional(4), y2Ac = b.Optional(4), uvDc = b.Optional(4), uvAc = b.Optional(4);
            for (var s = 0; s < 4; s++)
            {
                var q = !_segments ? quant : _absolute ? _segmentQuant[s] : quant + _segmentQuant[s];
                _steps[s] = new[] { Vp8Tables.DcSteps[Clip(q + yDc, 127)], Vp8Tables.AcSteps[Clip(q, 127)], Vp8Tables.DcSteps[Clip(q + y2Dc, 127)] * 2, Math.Max(8, Vp8Tables.AcSteps[Clip(q + y2Ac, 127)] * 101581 >> 16), Vp8Tables.DcSteps[Clip(q + uvDc, 117)], (int)Vp8Tables.AcSteps[Clip(q + uvAc, 127)] };
            }
            b.Bit(128);   // whether the probabilities last past this frame
            for (var i = 0; i < _probabilities.Length; i++) if (b.Bit(Vp8Tables.CoefficientUpdates[i]) == 1) _probabilities[i] = (byte)b.Number(8);
            if (b.Bit(128) == 1) _skipProbability = b.Number(8);
        }

        // ---- the macroblocks ----

        private Block[] ReadMacroblocks()
        {
            _yStride = _across * 16; _uvStride = _across * 8;
            _y = new byte[_yStride * _down * 16]; _u = new byte[_uvStride * _down * 8]; _v = new byte[_uvStride * _down * 8];
            var blocks = new Block[_across * _down];
            var topModes = new int[_across * 4]; var leftModes = new int[4]; var modes = new int[16];
            // whether the blocks above and on the left had coefficients: 4 of brightness, 2 + 2 of colour, 1 of the brightness averages
            var topUsed = new bool[_across, 9]; var leftUsed = new bool[9];
            var coefficients = new short[25 * 16];
            for (var my = 0; my < _down; my++)
            {
                Array.Clear(leftModes, 0, 4); Array.Clear(leftUsed, 0, 9);
                var tokens = _tokens[my & (_tokens.Length - 1)];
                for (var mx = 0; mx < _across; mx++)
                {
                    var block = new Block(); var h = _header;
                    if (_segmentMap) block.Segment = (byte)(h.Bit(_segmentProbabilities[0]) == 0 ? h.Bit(_segmentProbabilities[1]) : 2 + h.Bit(_segmentProbabilities[2]));
                    block.Skip = _skipProbability >= 0 && h.Bit(_skipProbability) == 1;
                    block.Split = h.Bit(145) == 0;
                    if (!block.Split)
                    {
                        block.Mode = h.Bit(156) == 1 ? (h.Bit(128) == 1 ? TrueMotion : Horizontal) : (h.Bit(163) == 1 ? Vertical : DcMode);
                        for (var i = 0; i < 4; i++) { topModes[mx * 4 + i] = block.Mode; leftModes[i] = block.Mode; }
                    }
                    else
                        for (var y = 0; y < 4; y++)
                        {
                            var mode = leftModes[y];
                            for (var x = 0; x < 4; x++) { mode = BlockMode(h, (topModes[mx * 4 + x] * 10 + mode) * 9); topModes[mx * 4 + x] = mode; modes[y * 4 + x] = mode; }
                            leftModes[y] = mode;
                        }
                    block.ChromaMode = h.Bit(142) == 0 ? DcMode : h.Bit(114) == 0 ? Vertical : h.Bit(183) == 1 ? TrueMotion : Horizontal;

                    Array.Clear(coefficients, 0, coefficients.Length); var any = false;
                    if (!block.Skip) any = Residuals(tokens, ref block, mx, topUsed, leftUsed, coefficients);
                    else
                    {
                        for (var i = 0; i < 8; i++) { topUsed[mx, i] = false; leftUsed[i] = false; }
                        if (!block.Split) { topUsed[mx, 8] = false; leftUsed[8] = false; }
                    }
                    block.Inner = block.Split || any;
                    Rebuild(block, modes, coefficients, mx, my);
                    blocks[my * _across + mx] = block;
                }
            }
            return blocks;
        }

        private static int BlockMode(Bools h, int at)
        {
            var p = Vp8Tables.BlockModes;
            if (h.Bit(p[at]) == 0) return 0;
            if (h.Bit(p[at + 1]) == 0) return 1;
            if (h.Bit(p[at + 2]) == 0) return 2;
            if (h.Bit(p[at + 3]) == 0) return h.Bit(p[at + 4]) == 0 ? 3 : h.Bit(p[at + 5]) == 0 ? 4 : 5;
            if (h.Bit(p[at + 6]) == 0) return 6;
            if (h.Bit(p[at + 7]) == 0) return 7;
            return h.Bit(p[at + 8]) == 0 ? 8 : 9;
        }

        /// <summary>Reads the coefficients of one 4x4 block from position <paramref name="n"/> on; gives the position after the last one.</summary>
        private int Coefficients(Bools t, int type, int context, int n, int dcStep, int acStep, short[] into, int at)
        {
            var p = _probabilities; var row = (type * 8 + Bands[n]) * 33 + context * 11;
            for (; n < 16; n++)
            {
                if (t.Bit(p[row]) == 0) return n;                       // nothing more
                while (t.Bit(p[row + 1]) == 0)                          // zeros
                {
                    if (++n == 16) return 16;
                    row = (type * 8 + Bands[n]) * 33;
                }
                var next = (type * 8 + Bands[n + 1]) * 33; int value;
                if (t.Bit(p[row + 2]) == 0) { value = 1; row = next + 11; }
                else
                {
                    if (t.Bit(p[row + 3]) == 0) value = t.Bit(p[row + 4]) == 0 ? 2 : 3 + t.Bit(p[row + 5]);
                    else if (t.Bit(p[row + 6]) == 0)
                    {
                        if (t.Bit(p[row + 7]) == 0) value = 5 + t.Bit(159);
                        else { value = 7 + 2 * t.Bit(165); value += t.Bit(145); }
                    }
                    else
                    {
                        var high = t.Bit(p[row + 8]); var low = t.Bit(p[row + 9 + high]); var category = 2 * high + low; value = 0;
                        foreach (var probability in Large[category]) value += value + t.Bit(probability);
                        value += 3 + (8 << category);
                    }
                    row = next + 22;
                }
                if (t.Bit(128) == 1) value = -value;
                into[at + ZigZag[n]] = (short)(value * (n > 0 ? acStep : dcStep));
            }
            return 16;
        }

        private bool Residuals(Bools t, ref Block block, int mx, bool[,] top, bool[] left, short[] into)
        {
            var steps = _steps[block.Segment]; var any = false; var first = 0; var type = 3;
            if (!block.Split)
            {
                // the 16 averages come first, as a block of their own, and are spread back with the Walsh-Hadamard transform
                var averages = 24 * 16; var used = Coefficients(t, 1, (top[mx, 8] ? 1 : 0) + (left[8] ? 1 : 0), 0, steps[2], steps[3], into, averages);
                top[mx, 8] = left[8] = used > 0; if (used > 0) Walsh(into, averages);
                first = 1; type = 0;
            }
            for (var y = 0; y < 4; y++)
                for (var x = 0; x < 4; x++)
                {
                    var used = Coefficients(t, type, (top[mx, x] ? 1 : 0) + (left[y] ? 1 : 0), first, steps[0], steps[1], into, (y * 4 + x) * 16);
                    top[mx, x] = left[y] = used > first; any |= used > first;
                }
            if (!block.Split) for (var i = 0; i < 16; i++) any |= into[i * 16] != 0;   // an average spread back also counts as something to smooth
            for (var plane = 0; plane < 2; plane++)
                for (var y = 0; y < 2; y++)
                    for (var x = 0; x < 2; x++)
                    {
                        var column = 4 + plane * 2 + x; var line = 4 + plane * 2 + y;
                        var used = Coefficients(t, 2, (top[mx, column] ? 1 : 0) + (left[line] ? 1 : 0), 0, steps[4], steps[5], into, (16 + plane * 4 + y * 2 + x) * 16);
                        top[mx, column] = left[line] = used > 0; any |= used > 0;
                    }
            return any;
        }

        private static void Walsh(short[] c, int from)
        {
            var t = new int[16];
            for (var i = 0; i < 4; i++)
            {
                int a0 = c[from + i] + c[from + 12 + i], a1 = c[from + 4 + i] + c[from + 8 + i], a2 = c[from + 4 + i] - c[from + 8 + i], a3 = c[from + i] - c[from + 12 + i];
                t[i] = a0 + a1; t[8 + i] = a0 - a1; t[4 + i] = a3 + a2; t[12 + i] = a3 - a2;
            }
            for (var i = 0; i < 4; i++)
            {
                int dc = t[i * 4] + 3, a0 = dc + t[i * 4 + 3], a1 = t[i * 4 + 1] + t[i * 4 + 2], a2 = t[i * 4 + 1] - t[i * 4 + 2], a3 = dc - t[i * 4 + 3];
                c[(4 * i) * 16] = (short)((a0 + a1) >> 3); c[(4 * i + 1) * 16] = (short)((a3 + a2) >> 3); c[(4 * i + 2) * 16] = (short)((a0 - a1) >> 3); c[(4 * i + 3) * 16] = (short)((a3 - a2) >> 3);
            }
        }

        // ---- prediction and the inverse transform ----

        private static byte Pixel(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

        private static void AddBlock(short[] c, int from, byte[] plane, int at, int stride)
        {
            var t = new int[16];
            for (var i = 0; i < 4; i++)
            {
                int a = c[from + i] + c[from + 8 + i], b = c[from + i] - c[from + 8 + i];
                int low = c[from + 4 + i], high = c[from + 12 + i];
                int cc = (low * 35468 >> 16) - ((high * 20091 >> 16) + high), d = ((low * 20091 >> 16) + low) + (high * 35468 >> 16);
                t[i * 4] = a + d; t[i * 4 + 1] = b + cc; t[i * 4 + 2] = b - cc; t[i * 4 + 3] = a - d;
            }
            for (var y = 0; y < 4; y++)
            {
                int dc = t[y] + 4, a = dc + t[8 + y], b = dc - t[8 + y];
                int low = t[4 + y], high = t[12 + y];
                int cc = (low * 35468 >> 16) - ((high * 20091 >> 16) + high), d = ((low * 20091 >> 16) + low) + (high * 35468 >> 16);
                var o = at + y * stride;
                plane[o] = Pixel(plane[o] + ((a + d) >> 3)); plane[o + 1] = Pixel(plane[o + 1] + ((b + cc) >> 3)); plane[o + 2] = Pixel(plane[o + 2] + ((b - cc) >> 3)); plane[o + 3] = Pixel(plane[o + 3] + ((a - d) >> 3));
            }
        }

        /// <summary>A whole block of brightness (16) or colour (8) guessed from the row above and the column on the left.</summary>
        private static void PredictWhole(byte[] plane, int stride, int x, int y, int size, int mode)
        {
            var at = y * stride + x; var above = y > 0; var beside = x > 0;
            int Top(int i) => above ? plane[at - stride + i] : 127;
            int Left(int j) => beside ? plane[at + j * stride - 1] : 129;
            var corner = !above ? 127 : !beside ? 129 : plane[at - stride - 1];
            var dc = 128;
            if (mode == DcMode && (above || beside))
            {
                var sum = 0; var shift = size == 16 ? 4 : 3;
                if (above) for (var i = 0; i < size; i++) sum += Top(i);
                if (beside) for (var j = 0; j < size; j++) sum += Left(j);
                if (above && beside) shift++;
                dc = (sum + (1 << (shift - 1))) >> shift;
            }
            for (var j = 0; j < size; j++)
                for (var i = 0; i < size; i++)
                    plane[at + j * stride + i] = mode == DcMode ? (byte)dc : mode == Vertical ? (byte)Top(i) : mode == Horizontal ? (byte)Left(j) : Pixel(Left(j) + Top(i) - corner);
        }

        private readonly int[] _edge = new int[13];   // 0..3 the left column from the top, 4 the corner, 5..12 the eight above

        private void PredictSmall(int x, int y, int mode, int topRightRow)
        {
            var plane = _y; var stride = _yStride; var at = y * stride + x; var e = _edge;
            for (var j = 0; j < 4; j++) e[j] = x > 0 ? plane[at + j * stride - 1] : 129;
            e[4] = y == 0 ? 127 : x == 0 ? 129 : plane[at - stride - 1];
            for (var i = 0; i < 4; i++) e[5 + i] = y == 0 ? 127 : plane[at - stride + i];
            // the four past the block: from the row above it, except on the right column of the macroblock, which looks above the whole macroblock
            var rightColumn = (x & 15) == 12; var row = rightColumn ? topRightRow : y - 1;
            for (var i = 4; i < 8; i++)
            {
                if (row < 0) e[5 + i] = 127;
                else if (x + i >= stride) e[5 + i] = plane[row * stride + stride - 1];
                else e[5 + i] = plane[row * stride + x + i];
            }
            int I = e[0], J = e[1], K = e[2], L = e[3], X = e[4], A = e[5], B = e[6], C = e[7], D = e[8], E = e[9], F = e[10], G = e[11], H = e[12];
            void Set(int px, int py, int v) => plane[at + py * stride + px] = (byte)v;
            int Two(int a, int b) => (a + b + 1) >> 1;
            int Three(int a, int b, int c) => (a + 2 * b + c + 2) >> 2;
            switch (mode)
            {
                case 0: { var dc = (I + J + K + L + A + B + C + D + 4) >> 3; for (var j = 0; j < 4; j++) for (var i = 0; i < 4; i++) Set(i, j, dc); break; }
                case 1: for (var j = 0; j < 4; j++) for (var i = 0; i < 4; i++) Set(i, j, Pixel(e[j] + e[5 + i] - X)); break;
                case 2: for (var i = 0; i < 4; i++) { var v = Three(e[4 + i], e[5 + i], e[6 + i]); for (var j = 0; j < 4; j++) Set(i, j, v); } break;
                case 3:
                {
                    int r0 = Three(X, I, J), r1 = Three(I, J, K), r2 = Three(J, K, L), r3 = Three(K, L, L);
                    for (var i = 0; i < 4; i++) { Set(i, 0, r0); Set(i, 1, r1); Set(i, 2, r2); Set(i, 3, r3); }
                    break;
                }
                case 4:   // down and to the right
                    Set(0, 3, Three(J, K, L)); Set(1, 3, Three(I, J, K)); Set(0, 2, Three(I, J, K));
                    Set(2, 3, Three(X, I, J)); Set(1, 2, Three(X, I, J)); Set(0, 1, Three(X, I, J));
                    Set(3, 3, Three(A, X, I)); Set(2, 2, Three(A, X, I)); Set(1, 1, Three(A, X, I)); Set(0, 0, Three(A, X, I));
                    Set(3, 2, Three(B, A, X)); Set(2, 1, Three(B, A, X)); Set(1, 0, Three(B, A, X));
                    Set(3, 1, Three(C, B, A)); Set(2, 0, Three(C, B, A)); Set(3, 0, Three(D, C, B));
                    break;
                case 5:   // vertical, leaning right
                    Set(0, 0, Two(X, A)); Set(1, 2, Two(X, A)); Set(1, 0, Two(A, B)); Set(2, 2, Two(A, B)); Set(2, 0, Two(B, C)); Set(3, 2, Two(B, C)); Set(3, 0, Two(C, D));
                    Set(0, 3, Three(K, J, I)); Set(0, 2, Three(J, I, X)); Set(0, 1, Three(I, X, A)); Set(1, 3, Three(I, X, A)); Set(1, 1, Three(X, A, B)); Set(2, 3, Three(X, A, B));
                    Set(2, 1, Three(A, B, C)); Set(3, 3, Three(A, B, C)); Set(3, 1, Three(B, C, D));
                    break;
                case 6:   // down and to the left
                    Set(0, 0, Three(A, B, C)); Set(1, 0, Three(B, C, D)); Set(0, 1, Three(B, C, D)); Set(2, 0, Three(C, D, E)); Set(1, 1, Three(C, D, E)); Set(0, 2, Three(C, D, E));
                    Set(3, 0, Three(D, E, F)); Set(2, 1, Three(D, E, F)); Set(1, 2, Three(D, E, F)); Set(0, 3, Three(D, E, F));
                    Set(3, 1, Three(E, F, G)); Set(2, 2, Three(E, F, G)); Set(1, 3, Three(E, F, G)); Set(3, 2, Three(F, G, H)); Set(2, 3, Three(F, G, H)); Set(3, 3, Three(G, H, H));
                    break;
                case 7:   // vertical, leaning left
                    Set(0, 0, Two(A, B)); Set(1, 0, Two(B, C)); Set(0, 2, Two(B, C)); Set(2, 0, Two(C, D)); Set(1, 2, Two(C, D)); Set(3, 0, Two(D, E)); Set(2, 2, Two(D, E));
                    Set(0, 1, Three(A, B, C)); Set(1, 1, Three(B, C, D)); Set(0, 3, Three(B, C, D)); Set(2, 1, Three(C, D, E)); Set(1, 3, Three(C, D, E)); Set(3, 1, Three(D, E, F)); Set(2, 3, Three(D, E, F));
                    Set(3, 2, Three(E, F, G)); Set(3, 3, Three(F, G, H));
                    break;
                case 8:   // horizontal, leaning down
                    Set(0, 0, Two(I, X)); Set(2, 1, Two(I, X)); Set(0, 1, Two(J, I)); Set(2, 2, Two(J, I)); Set(0, 2, Two(K, J)); Set(2, 3, Two(K, J)); Set(0, 3, Two(L, K));
                    Set(3, 0, Three(A, B, C)); Set(2, 0, Three(X, A, B)); Set(1, 0, Three(I, X, A)); Set(3, 1, Three(I, X, A)); Set(1, 1, Three(J, I, X)); Set(3, 2, Three(J, I, X));
                    Set(1, 2, Three(K, J, I)); Set(3, 3, Three(K, J, I)); Set(1, 3, Three(L, K, J));
                    break;
                default:  // horizontal, leaning up
                    Set(0, 0, Two(I, J)); Set(2, 0, Two(J, K)); Set(0, 1, Two(J, K)); Set(2, 1, Two(K, L)); Set(0, 2, Two(K, L)); Set(1, 0, Three(I, J, K)); Set(3, 0, Three(J, K, L)); Set(1, 1, Three(J, K, L));
                    Set(3, 1, Three(K, L, L)); Set(1, 2, Three(K, L, L)); Set(3, 2, L); Set(2, 2, L); Set(0, 3, L); Set(1, 3, L); Set(2, 3, L); Set(3, 3, L);
                    break;
            }
        }

        private void Rebuild(Block block, int[] modes, short[] coefficients, int mx, int my)
        {
            int x = mx * 16, y = my * 16;
            if (!block.Split)
            {
                PredictWhole(_y, _yStride, x, y, 16, block.Mode);
                for (var i = 0; i < 16; i++) AddBlock(coefficients, i * 16, _y, (y + (i >> 2) * 4) * _yStride + x + (i & 3) * 4, _yStride);
            }
            else
                for (var i = 0; i < 16; i++)
                {
                    int bx = x + (i & 3) * 4, by = y + (i >> 2) * 4;
                    PredictSmall(bx, by, modes[i], y - 1);
                    AddBlock(coefficients, i * 16, _y, by * _yStride + bx, _yStride);
                }
            for (var plane = 0; plane < 2; plane++)
            {
                var target = plane == 0 ? _u : _v; PredictWhole(target, _uvStride, mx * 8, my * 8, 8, block.ChromaMode);
                for (var i = 0; i < 4; i++) AddBlock(coefficients, (16 + plane * 4 + i) * 16, target, (my * 8 + (i >> 1) * 4) * _uvStride + mx * 8 + (i & 1) * 4, _uvStride);
            }
        }

        // ---- the loop filter ----

        private static int Signed(int v) => v < -128 ? -128 : v > 127 ? 127 : v;
        private static int Small(int v) => v < -16 ? -16 : v > 15 ? 15 : v;

        /// <summary>Smooths one edge: <paramref name="step"/> crosses it and <paramref name="along"/> walks it. Kind 0 is the simple filter, 1 an edge between macroblocks, 2 an edge inside one.</summary>
        private static void Edge(byte[] p, int at, int step, int along, int count, int limit, int interior, int high, int kind)
        {
            var most = 2 * limit + 1;
            for (var i = 0; i < count; i++, at += along)
            {
                int p1 = p[at - 2 * step], p0 = p[at - step], q0 = p[at], q1 = p[at + step];
                if (4 * Math.Abs(p0 - q0) + Math.Abs(p1 - q1) > most) continue;
                if (kind == 0) { Two(p, at, step, p1, p0, q0, q1); continue; }
                int p3 = p[at - 4 * step], p2 = p[at - 3 * step], q2 = p[at + 2 * step], q3 = p[at + 3 * step];
                if (Math.Abs(p3 - p2) > interior || Math.Abs(p2 - p1) > interior || Math.Abs(p1 - p0) > interior || Math.Abs(q3 - q2) > interior || Math.Abs(q2 - q1) > interior || Math.Abs(q1 - q0) > interior) continue;
                if (Math.Abs(p1 - p0) > high || Math.Abs(q1 - q0) > high) { Two(p, at, step, p1, p0, q0, q1); continue; }
                if (kind == 1)
                {
                    var a = Signed(3 * (q0 - p0) + Signed(p1 - q1)); int a1 = (27 * a + 63) >> 7, a2 = (18 * a + 63) >> 7, a3 = (9 * a + 63) >> 7;
                    p[at - 3 * step] = Pixel(p2 + a3); p[at - 2 * step] = Pixel(p1 + a2); p[at - step] = Pixel(p0 + a1); p[at] = Pixel(q0 - a1); p[at + step] = Pixel(q1 - a2); p[at + 2 * step] = Pixel(q2 - a3);
                }
                else
                {
                    var a = 3 * (q0 - p0); int a1 = Small((a + 4) >> 3), a2 = Small((a + 3) >> 3), a3 = (a1 + 1) >> 1;
                    p[at - 2 * step] = Pixel(p1 + a3); p[at - step] = Pixel(p0 + a2); p[at] = Pixel(q0 - a1); p[at + step] = Pixel(q1 - a3);
                }
            }
        }

        private static void Two(byte[] p, int at, int step, int p1, int p0, int q0, int q1)
        {
            var a = 3 * (q0 - p0) + Signed(p1 - q1); int a1 = Small((a + 4) >> 3), a2 = Small((a + 3) >> 3);
            p[at - step] = Pixel(p0 + a2); p[at] = Pixel(q0 - a1);
        }

        private void Filter(Block[] blocks)
        {
            if (_level == 0) return;   // the frame's level switches the filter on, whatever the segments say
            for (var my = 0; my < _down; my++)
                for (var mx = 0; mx < _across; mx++)
                {
                    var block = blocks[my * _across + mx];
                    var level = !_segments ? _level : _absolute ? _segmentLevel[block.Segment] : _level + _segmentLevel[block.Segment];
                    level = Clip(level, 63);
                    if (_adjust) level = Clip(level + _referenceDelta + (block.Split ? _modeDelta : 0), 63);
                    if (level == 0) continue;
                    var interior = level;
                    if (_sharpness > 0) { interior >>= _sharpness > 4 ? 2 : 1; interior = Math.Min(interior, 9 - _sharpness); }
                    interior = Math.Max(1, interior);
                    var limit = 2 * level + interior; var high = level >= 40 ? 2 : level >= 15 ? 1 : 0;
                    var y = my * 16 * _yStride + mx * 16; var c = my * 8 * _uvStride + mx * 8; var edge = _simple ? 0 : 1; var inside = _simple ? 0 : 2;
                    // the left edge, the edges inside from left to right, the top edge, the edges inside from top to bottom
                    if (mx > 0) { Edge(_y, y, 1, _yStride, 16, limit + 4, interior, high, edge); if (!_simple) { Edge(_u, c, 1, _uvStride, 8, limit + 4, interior, high, edge); Edge(_v, c, 1, _uvStride, 8, limit + 4, interior, high, edge); } }
                    if (block.Inner) { for (var k = 4; k < 16; k += 4) Edge(_y, y + k, 1, _yStride, 16, limit, interior, high, inside); if (!_simple) { Edge(_u, c + 4, 1, _uvStride, 8, limit, interior, high, inside); Edge(_v, c + 4, 1, _uvStride, 8, limit, interior, high, inside); } }
                    if (my > 0) { Edge(_y, y, _yStride, 1, 16, limit + 4, interior, high, edge); if (!_simple) { Edge(_u, c, _uvStride, 1, 8, limit + 4, interior, high, edge); Edge(_v, c, _uvStride, 1, 8, limit + 4, interior, high, edge); } }
                    if (block.Inner) { for (var k = 4; k < 16; k += 4) Edge(_y, y + k * _yStride, _yStride, 1, 16, limit, interior, high, inside); if (!_simple) { Edge(_u, c + 4 * _uvStride, _uvStride, 1, 8, limit, interior, high, inside); Edge(_v, c + 4 * _uvStride, _uvStride, 1, 8, limit, interior, high, inside); } }
                }
        }

        // ---- to RGB ----

        private static int High(int v, int k) => (v * k) >> 8;
        private static byte Clip6(int v) => (byte)((v & ~16383) == 0 ? v >> 6 : v < 0 ? 0 : 255);

        private WebpPicture ToRgb()
        {
            var rgba = new byte[_width * _height * 4]; var chromaHeight = (_height + 1) >> 1; var chromaWidth = (_width + 1) >> 1;
            var u = new int[_width]; var v = new int[_width];
            for (var y = 0; y < _height; y++)
            {
                // each row of pixels lies a quarter of the way from one row of colour to another (libwebp's "fancy" upsampling)
                var near = y >> 1; var far = (y & 1) == 1 ? near + 1 : near - 1;
                if (far < 0 || far >= chromaHeight) far = near;
                Upsample(_u, near * _uvStride, far * _uvStride, chromaWidth, u); Upsample(_v, near * _uvStride, far * _uvStride, chromaWidth, v);
                for (var x = 0; x < _width; x++)
                {
                    var l = High(_y[y * _yStride + x], 19077); var o = (y * _width + x) * 4;
                    rgba[o] = Clip6(l + High(v[x], 26149) - 14234);
                    rgba[o + 1] = Clip6(l - High(u[x], 6419) - High(v[x], 13320) + 8708);
                    rgba[o + 2] = Clip6(l + High(u[x], 33050) - 17685);
                    rgba[o + 3] = 255;
                }
            }
            return new WebpPicture { Width = _width, Height = _height, Pixels = rgba };
        }

        private void Upsample(byte[] plane, int near, int far, int count, int[] row)
        {
            row[0] = (3 * plane[near] + plane[far] + 2) >> 2;
            for (var x = 1; x <= (_width - 1) >> 1; x++)
            {
                int a = plane[near + x - 1], b = plane[near + x], c = plane[far + x - 1], d = plane[far + x];
                var sum = a + b + c + d + 8;
                var first = (((sum + 2 * (b + c)) >> 3) + a) >> 1; var second = (((sum + 2 * (a + d)) >> 3) + b) >> 1;
                row[2 * x - 1] = first; row[2 * x] = second;
            }
            if ((_width & 1) == 0) row[_width - 1] = (3 * plane[near + count - 1] + plane[far + count - 1] + 2) >> 2;
        }
    }
}
