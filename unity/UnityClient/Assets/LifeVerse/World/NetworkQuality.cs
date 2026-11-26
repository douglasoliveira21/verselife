using System;
using System.Collections.Generic;
using System.Linq;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// The quality of the connection to the island, from samples of the round trip taken once a second: the latest and the average of the last half minute, the jitter (how much one sample differs from the one
    /// before it, on average) and the packet loss the transport counts.
    /// </summary>
    public sealed class NetworkQuality
    {
        public const int Window = 30;
        private readonly Queue<int> _samples = new Queue<int>();
        public int Count => _samples.Count;
        public int Latest { get; private set; }
        public long LossPercent { get; private set; }
        public int Average => _samples.Count == 0 ? 0 : (int)Math.Round(_samples.Average());
        public int Jitter
        {
            get
            {
                if (_samples.Count < 2) return 0; var previous = -1; double sum = 0;
                foreach (var s in _samples) { if (previous >= 0) sum += Math.Abs(s - previous); previous = s; }
                return (int)Math.Round(sum / (_samples.Count - 1));
            }
        }

        public void Add(int roundTripMilliseconds, long lossPercent)
        {
            Latest = Math.Max(0, roundTripMilliseconds); LossPercent = Math.Max(0, Math.Min(100, lossPercent)); _samples.Enqueue(Latest); while (_samples.Count > Window) _samples.Dequeue();
        }

        public void Clear() { _samples.Clear(); Latest = 0; LossPercent = 0; }

        public string Describe() => _samples.Count == 0 ? "ping —" : "ping " + Latest + " ms (média " + Average + ")   jitter " + Jitter + " ms   perda " + LossPercent + "%";
    }
}
