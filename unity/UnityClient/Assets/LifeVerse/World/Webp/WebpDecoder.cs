using System;

namespace LifeVerse.Unity.World.Webp
{
    /// <summary>A decoded picture: RGBA, four bytes a pixel, the first row at the top.</summary>
    public sealed class WebpPicture
    {
        public int Width, Height;
        public byte[] Pixels;
    }

    /// <summary>
    /// Reads WebP pictures in plain C#, with no native library (Unity reads only PNG and JPEG): lossy (VP8), lossless (VP8L), lossy with transparency (VP8X + ALPH), and the
    /// first frame of an animation. A damaged or oversized file gives null, never an exception: the bytes come from other players.
    /// The pixels are the ones libwebp gives, checked against Godot by the EditMode tests (tests/fixtures/webp).
    /// </summary>
    public static class WebpDecoder
    {
        /// <summary>The largest picture that is decoded (width times height).</summary>
        public const int MaxPixels = 4096 * 4096;

        public static bool IsWebp(byte[] data) => data != null && data.Length >= 16 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' && data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P';

        public static WebpPicture Decode(byte[] data)
        {
            if (!IsWebp(data)) return null;
            try { return Chunks(data, 12, data.Length, 0, 0); }
            catch (Exception exception) when (exception is IndexOutOfRangeException || exception is ArgumentException || exception is WebpException || exception is OverflowException || exception is OutOfMemoryException) { return null; }
        }

        private static int U24(byte[] d, int at) => d[at] | d[at + 1] << 8 | d[at + 2] << 16;

        private static WebpPicture Chunks(byte[] data, int at, int end, int canvasWidth, int canvasHeight)
        {
            int alphaAt = -1, alphaSize = 0;
            while (at + 8 <= end)
            {
                var kind = "" + (char)data[at] + (char)data[at + 1] + (char)data[at + 2] + (char)data[at + 3];
                var size = BitConverter.ToInt32(data, at + 4); var body = at + 8;
                if (size < 0 || size > end - body) throw new WebpException("a chunk is cut off");
                switch (kind)
                {
                    case "VP8X": canvasWidth = 1 + U24(data, body + 4); canvasHeight = 1 + U24(data, body + 7); Check(canvasWidth, canvasHeight); break;
                    case "ALPH": alphaAt = body; alphaSize = size; break;
                    case "ANMF": if (size < 16) throw new WebpException("a frame is cut off"); return Chunks(data, body + 16, body + size, 0, 0);   // the first frame of an animation
                    case "VP8 ":
                    {
                        var picture = Vp8Decoder.Decode(data, body, size);
                        if (alphaAt >= 0) AlphaPlane.Apply(data, alphaAt, alphaSize, picture);
                        return picture;
                    }
                    case "VP8L": return Vp8LDecoder.Decode(data, body, size);
                }
                at = body + size + (size & 1);
            }
            throw new WebpException("no picture inside");
        }

        internal static void Check(int width, int height)
        {
            if (width <= 0 || height <= 0 || (long)width * height > MaxPixels) throw new WebpException("size " + width + "x" + height);
        }
    }

    internal sealed class WebpException : Exception { public WebpException(string message) : base(message) { } }

    /// <summary>The transparency plane that goes with a lossy picture (the ALPH chunk): raw or compressed as a lossless picture, then undone from its prediction.</summary>
    internal static class AlphaPlane
    {
        internal static void Apply(byte[] data, int at, int size, WebpPicture picture)
        {
            if (size < 1) throw new WebpException("empty alpha");
            int header = data[at], method = header & 3, filter = (header >> 2) & 3; int width = picture.Width, height = picture.Height; var plane = new byte[width * height];
            if (method == 0) { if (size - 1 < plane.Length) throw new WebpException("alpha is cut off"); Array.Copy(data, at + 1, plane, 0, plane.Length); }
            else if (method == 1) { var green = Vp8LDecoder.DecodeAlpha(data, at + 1, size - 1, width, height); for (var i = 0; i < plane.Length; i++) plane[i] = (byte)(green[i] >> 8); }
            else throw new WebpException("alpha compression " + method);
            if (filter != 0)
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var i = y * width + x; int guess;
                        if (x == 0 && y == 0) guess = 0;
                        else if (y == 0) guess = plane[i - 1];
                        else if (x == 0) guess = plane[i - width];
                        else if (filter == 1) guess = plane[i - 1];
                        else if (filter == 2) guess = plane[i - width];
                        else guess = Math.Max(0, Math.Min(255, plane[i - 1] + plane[i - width] - plane[i - width - 1]));
                        plane[i] = (byte)(plane[i] + guess);
                    }
            for (var i = 0; i < plane.Length; i++) picture.Pixels[i * 4 + 3] = plane[i];
        }
    }
}
