using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LifeVerse.Shared.Networking;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.UI;
using LifeVerse.Unity.World;
using Unity.Profiling;
using UnityEngine;

namespace LifeVerse.Unity.Server
{
    /// <summary>
    /// Measures the client under load, once per graphics preset: a crowded island (objects of every kind and walking avatars) seen from a fixed camera. For each preset it reports the frame time (average, the worst 1 %), the frames
    /// per second, what the game allocates per frame, how many garbage collections ran, the draw calls and triangles the renderer counted, and the time the CPU and the GPU spent on a frame when the engine can tell. It runs from the
    /// world self-check when started with <c>-lifeverse-benchmark REPORT.txt</c> (and a graphics device); the report is also printed as "event=benchmark" lines.
    /// </summary>
    public static class PerformanceBenchmark
    {
        public sealed class Result
        {
            public string Preset = ""; public int Frames; public double AverageMs, WorstOnePercentMs, MaxMs, Fps, AllocBytesPerFrame, DrawCalls, SetPassCalls, Triangles, CpuMs, GpuMs; public int Collections;
            public string Line() => string.Format(CultureInfo.InvariantCulture, "event=benchmark preset={0} frames={1} fps={2:F1} avg_ms={3:F2} worst1pct_ms={4:F2} max_ms={5:F2} alloc_kb_per_frame={6:F2} gc0={7} draw_calls={8:F0} setpass={9:F0} triangles={10:F0} cpu_ms={11:F2} gpu_ms={12:F2}",
                Preset, Frames, Fps, AverageMs, WorstOnePercentMs, MaxMs, AllocBytesPerFrame / 1024.0, Collections, DrawCalls, SetPassCalls, Triangles, CpuMs, GpuMs);
        }

        public static IEnumerator Run(Action<int> select, Action<float> perFrame, string reportPath, Func<IEnumerator> crowd, int presets = 4, float seconds = 4f, Action<string> log = null)
        {
            log = log ?? Console.WriteLine; var all = new List<Result>();
            yield return crowd();
            using (var drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count")) using (var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count")) using (var triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count"))
            {
                for (var preset = 0; preset < presets; preset++)   // presets: how many, counting from Alto when fewer than four (Alto is the default)
                {
                    select(presets >= 4 ? preset : 2); var settle = Time.realtimeSinceStartup + 1.5f; while (Time.realtimeSinceStartup < settle) { perFrame?.Invoke(Time.time); yield return null; }   // settle: shaders, streaming, the first frames after a change
                    var frames = new List<double>(); var allocStart = GC.GetAllocatedBytesForCurrentThread(); var gcStart = GC.CollectionCount(0); var draw = 0.0; var pass = 0.0; var tri = 0.0; var cpu = 0.0; var gpu = 0.0; var timed = 0; var end = Time.realtimeSinceStartup + seconds;
                    var timings = new FrameTiming[1];
                    while (Time.realtimeSinceStartup < end)
                    {
                        perFrame?.Invoke(Time.time); yield return null; frames.Add(Time.unscaledDeltaTime * 1000.0);
                        if (drawCalls.Valid) draw += drawCalls.LastValue; if (setPass.Valid) pass += setPass.LastValue; if (triangles.Valid) tri += triangles.LastValue;
                        FrameTimingManager.CaptureFrameTimings(); if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].cpuFrameTime > 0) { cpu += timings[0].cpuFrameTime; gpu += timings[0].gpuFrameTime; timed++; }
                    }
                    var n = Math.Max(1, frames.Count); var average = frames.Count == 0 ? 0 : frames.Average();
                    var result = new Result { Preset = GraphicsQuality.Of(presets >= 4 ? preset : 2).Name, Frames = frames.Count, AverageMs = average, Fps = average > 0 ? 1000.0 / average : 0, WorstOnePercentMs = FrameStats.WorstOnePercent(frames), MaxMs = frames.Count == 0 ? 0 : frames.Max(),
                        AllocBytesPerFrame = (GC.GetAllocatedBytesForCurrentThread() - allocStart) / (double)n, Collections = GC.CollectionCount(0) - gcStart, DrawCalls = draw / n, SetPassCalls = pass / n, Triangles = tri / n, CpuMs = timed > 0 ? cpu / timed : -1, GpuMs = timed > 0 ? gpu / timed : -1 };
                    all.Add(result); log(result.Line());
                }
            }
            if (!string.IsNullOrEmpty(reportPath)) { try { File.WriteAllText(reportPath, string.Join("\n", all.Select(r => r.Line())) + "\n", new UTF8Encoding(false)); } catch (IOException exception) { log("event=benchmark_note detail=" + exception.Message); } }
        }

        /// <summary>A crowded island: objects of every kind spread over a field, and walking avatars. Returns the avatars so the caller can remove them.</summary>
        public static IEnumerator Crowd(WorldObjects objects, List<AvatarView> avatars, int objectCount = 600, int avatarCount = 24)
        {
            var kinds = new[] { "cube", "sphere", "cylinder", "tree", "grass", "cube" }; var rng = new System.Random(12345);
            for (var i = 0; i < objectCount; i++)
            {
                var kind = kinds[i % kinds.Length]; var x = (float)(rng.NextDouble() * 240 - 120); var z = (float)(rng.NextDouble() * 240 - 120); var scale = kind == "tree" ? 4f : kind == "grass" ? 1.5f : 1f + (float)rng.NextDouble() * 2f;
                var json = "{\"operation\":\"create\",\"objectState\":{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"b" + i + "\",\"primitiveType\":\"" + kind + "\",\"materialJson\":\"{\\\"color\\\":\\\"#" + rng.Next(0x404040, 0xE0E0E0).ToString("X6") + "\\\"}\",\"positionX\":" + x.ToString(CultureInfo.InvariantCulture) + ",\"positionY\":6,\"positionZ\":" + z.ToString(CultureInfo.InvariantCulture)
                    + ",\"scaleX\":" + scale.ToString(CultureInfo.InvariantCulture) + ",\"scaleY\":" + scale.ToString(CultureInfo.InvariantCulture) + ",\"scaleZ\":" + scale.ToString(CultureInfo.InvariantCulture) + ",\"revision\":1}}";
                objects.ApplyOperation(json); if (i % 100 == 99) yield return null;
            }
            for (var i = 0; i < avatarCount; i++) { var view = AvatarView.Create(9000 + i, "Pessoa " + i, Color.HSVToRGB(i / (float)avatarCount, .5f, .8f)); view.PlaceAt(new Vector3((float)(rng.NextDouble() * 80 - 40), 6f, (float)(rng.NextDouble() * 80 - 40))); avatars.Add(view); }
            yield return null;
        }

        /// <summary>Moves the avatars in circles, as people walking, so the animation, the name tags and the body updates are in the measure.</summary>
        public static void Walk(List<AvatarView> avatars, float time)
        {
            for (var i = 0; i < avatars.Count; i++)
            {
                var a = time * .4f + i; var centre = new Vector3(Mathf.Sin(i * 1.7f) * 30f, 6f, Mathf.Cos(i * 1.3f) * 30f); var position = centre + new Vector3(Mathf.Cos(a) * 8f, 0f, Mathf.Sin(a) * 8f);
                avatars[i].SetPose(position, -a * Mathf.Rad2Deg, new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * 3f, NetworkMotionState.Walk);
            }
        }
    }
}
