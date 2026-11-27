using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// "Limite de download": the files the game downloads are read no faster than this, all downloads together sharing it. Reading slowly makes the network slow down too
    /// (the connection stops taking data). 0 reads as fast as it comes.
    /// </summary>
    public static class DownloadThrottle
    {
        public const int Chunk = 16 * 1024;
        /// <summary>Bytes a second for all downloads together; 0 for no limit.</summary>
        public static long BytesPerSecond;
        private static readonly object Gate = new object();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static double _free;   // the moment (seconds) from which the line is free again

        /// <summary>How long to wait after reading <paramref name="count"/> bytes at <paramref name="now"/>, given when the line is free; moves that moment on.</summary>
        public static double Delay(int count, long limit, double now, ref double free)
        {
            if (limit <= 0) return 0;
            free = Math.Max(free, now) + count / (double)limit; return Math.Max(0, free - now);
        }

        public static async Task<byte[]> ReadAsync(Stream stream, CancellationToken ct = default)
        {
            using var all = new MemoryStream(); var buffer = new byte[Chunk]; int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                all.Write(buffer, 0, read); double wait;
                lock (Gate) wait = Delay(read, Interlocked.Read(ref BytesPerSecond), Clock.Elapsed.TotalSeconds, ref _free);
                if (wait > .001) await Task.Delay(TimeSpan.FromSeconds(wait), ct).ConfigureAwait(false);
            }
            return all.ToArray();
        }
    }
}
