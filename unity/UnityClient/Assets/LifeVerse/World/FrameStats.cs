using System;
using System.Collections.Generic;
using System.Linq;

namespace LifeVerse.Unity.World
{
    /// <summary>Small helpers to summarise frame times (the benchmark and the performance overlay read them).</summary>
    public static class FrameStats
    {
        /// <summary>Average of the slowest 1 % of the frames (at least one), in milliseconds; 0 for no frames.</summary>
        public static double WorstOnePercent(IList<double> frameMs)
        {
            if (frameMs == null || frameMs.Count == 0) return 0; var sorted = frameMs.OrderByDescending(x => x).ToList(); var take = Math.Max(1, sorted.Count / 100); return sorted.Take(take).Average();
        }
    }
}
