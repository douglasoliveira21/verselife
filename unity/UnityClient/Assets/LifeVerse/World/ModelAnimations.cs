using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// The clips a model brings with it (glTFast makes them legacy clips on an Animation component). A worn object plays its first clip in a loop; an object of the world plays the clip its "animation" setting names
    /// (<c>{ clip, loop, playing, speed, offset, since }</c>, with "since" in milliseconds of the server's clock) so everybody sees the same pose at the same time.
    /// </summary>
    public static class ModelAnimations
    {
        public static UnityEngine.Animation PlayerOf(GameObject root) => root == null ? null : root.GetComponentInChildren<UnityEngine.Animation>(true);

        public static IReadOnlyList<string> Clips(GameObject root)
        {
            var player = PlayerOf(root); if (player == null) return Array.Empty<string>();
            var names = new List<string>(); foreach (AnimationState state in player) names.Add(state.name); return names;
        }

        /// <summary>Starts the first clip in a loop (what a worn object does). False when the model has none.</summary>
        public static bool StartFirst(GameObject root)
        {
            var player = PlayerOf(root); var clips = Clips(root); if (player == null || clips.Count == 0) return false;
            var state = player[clips[0]]; state.wrapMode = WrapMode.Loop; state.speed = 1f; player.Play(clips[0]); return true;
        }

        /// <summary>The settings of the animation inside a material setting, or null when there is none.</summary>
        public static JsonObject Settings(string materialJson)
        {
            try { return JsonNode.Parse(string.IsNullOrWhiteSpace(materialJson) ? "{}" : materialJson) is JsonObject root ? root["animation"] as JsonObject : null; }
            catch (JsonException) { return null; }
        }

        /// <summary>The material setting without its animation: a change of the animation must not rebuild the object.</summary>
        public static string WithoutAnimation(string materialJson)
        {
            try { if (JsonNode.Parse(string.IsNullOrWhiteSpace(materialJson) ? "{}" : materialJson) is JsonObject root && root.ContainsKey("animation")) { root.Remove("animation"); return root.ToJsonString(); } }
            catch (JsonException) { }
            return materialJson;
        }

        /// <summary>The position in a clip: from the offset when paused, running on from the start time at the chosen speed otherwise; a loop wraps, a single play stops at the end.</summary>
        public static double PositionAt(double offset, double sinceSeconds, double nowSeconds, double speed, bool playing, bool loop, double length)
        {
            var at = playing ? offset + (nowSeconds - sinceSeconds) * speed : offset;
            return loop && length > 0 ? ((at % length) + length) % length : Math.Max(0, Math.Min(at, length));
        }

        /// <summary>Starts, pauses or stops the clip as the stored animation says. A clip the model does not have is ignored. Returns the clip that now plays (empty when none).</summary>
        public static string Apply(GameObject root, JsonObject animation, double nowUnixSeconds)
        {
            var player = PlayerOf(root); if (player == null) return "";
            double D(string key, double fallback) => animation != null && animation[key] is JsonValue v && v.TryGetValue<double>(out var n) && !double.IsNaN(n) && !double.IsInfinity(n) ? n : fallback;
            var clip = animation != null && animation["clip"] is JsonValue c && c.TryGetValue<string>(out var name) ? name : "";
            if (animation == null || clip.Length == 0 || player[clip] == null) { player.Stop(); return ""; }
            var loop = animation["loop"] is JsonValue l && l.TryGetValue<bool>(out var looping) && looping; var playing = animation["playing"] is JsonValue p && p.TryGetValue<bool>(out var on) && on;
            var speed = Math.Max(.1, Math.Min(D("speed", 1), 4)); var state = player[clip]; var since = D("since", nowUnixSeconds * 1000) / 1000.0;
            state.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever; state.time = (float)PositionAt(D("offset", 0), since, nowUnixSeconds, speed, playing, loop, state.length);
            state.speed = playing ? (float)speed : 0f; player.Play(clip); if (!playing) player.Sample();
            return clip;
        }
    }
}
