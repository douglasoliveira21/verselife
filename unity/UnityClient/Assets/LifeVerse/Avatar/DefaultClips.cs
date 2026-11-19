using System.Collections.Generic;
using System.Threading.Tasks;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Networking;
using UnityEngine;

namespace LifeVerse.Unity.Avatar
{
    /// <summary>
    /// The animations the game itself carries for custom bodies: the same Mixamo clip files the Godot client uses (idle, walk, run, jump, fall, flying, swimming, typing), kept as
    /// text assets under Resources/Animations (the files are GLBs; the engine does not import them, the shared <see cref="SkeletonClip"/> reads and samples them). They are in place
    /// (the game moves the avatar) and the jump does not lift the hips. Reading the big file takes about half a second, so it happens on another thread when the first custom body
    /// appears; until it is done the custom body uses the procedural poses.
    /// </summary>
    public static class DefaultClips
    {
        /// <summary>State of the game → name of the clip in the files.</summary>
        public static readonly IReadOnlyDictionary<string, string> ClipOfState = new Dictionary<string, string>
        {
            ["Typing"] = "Typing", ["Idle"] = "Idle", ["Walk"] = "Walk", ["Run"] = "Run", ["Jump"] = "Jump", ["Fall"] = "Fall", ["FlyIdle"] = "FlyIdle", ["FlyMove"] = "FlyMove", ["Swim"] = "Swim", ["SwimIdle"] = "SwimIdle", ["WalkBack"] = "WalkBackwards", ["FallDown"] = "JumpDown"
        };

        private static readonly (string Resource, string Clip)[] Replacements = { ("Animations/idle_animation", "Idle"), ("Animations/jump_animation", "Jump"), ("Animations/jumpdown_animation", "JumpDown") };
        private static Dictionary<string, SkeletonClip> _clips; private static Task _loading; private static readonly object Gate = new object();
        /// <summary>When true custom bodies use only the procedural poses (to tell whether the clips matter for a slowdown).</summary>
        public static bool Disabled;

        public static bool Ready { get { lock (Gate) return _clips != null; } }
        public static int Count { get { lock (Gate) return _clips?.Count ?? 0; } }

        /// <summary>Reads the clip files (the bytes on this thread, the parsing on another). Safe to call again.</summary>
        public static Task Preload()
        {
            lock (Gate)
            {
                if (_clips != null) return Task.CompletedTask; if (_loading != null) return _loading;
                var main = Resources.Load<TextAsset>("Animations/default_avatar_animations"); var typing = Resources.Load<TextAsset>("Animations/typing_animation");
                var replacements = new List<(byte[] Bytes, string Clip)>(); foreach (var (resource, clip) in Replacements) if (Resources.Load<TextAsset>(resource) is { } asset) replacements.Add((asset.bytes, clip));
                var mainBytes = main != null ? main.bytes : null; var typingBytes = typing != null ? typing.bytes : null;
                return _loading = Task.Run(() =>
                {
                    var loaded = new Dictionary<string, SkeletonClip>();
                    if (mainBytes != null) foreach (var name in new HashSet<string>(ClipOfState.Values)) if (name != "Typing" && SkeletonClip.Read(mainBytes, name) is { } clip) loaded[name] = clip;
                    foreach (var (bytes, clip) in replacements) if (SkeletonClip.Read(bytes, clip) is { } replacement) loaded[clip] = replacement;
                    if (typingBytes != null && SkeletonClip.Read(typingBytes, "Typing") is { } typed) loaded["Typing"] = typed;
                    lock (Gate) _clips = loaded;
                });
            }
        }

        /// <summary>The clip for a state of the game, or null when there is none (or the files are still being read).</summary>
        public static SkeletonClip For(string state)
        {
            if (Disabled || state == null) return null;
            lock (Gate) { if (_clips == null) { Preload(); return null; } return ClipOfState.TryGetValue(state, out var name) && _clips.TryGetValue(name, out var clip) ? clip : null; }
        }

        /// <summary>The state name the clips are asked for, from what the person is doing; null for what has no clip (sitting, waving, dancing, gestures), which the procedural poses cover.</summary>
        public static string StateOf(NetworkMotionState motion, float speed, bool gesturing)
        {
            if (gesturing) return null;
            switch (motion)
            {
                case NetworkMotionState.Idle: return "Idle";
                case NetworkMotionState.Walk: return "Walk";
                case NetworkMotionState.Run: return "Run";
                case NetworkMotionState.Jump: return "Jump";
                case NetworkMotionState.Fall: return "Fall";
                case NetworkMotionState.Fly: return speed > .5f ? "FlyMove" : "FlyIdle";
                case NetworkMotionState.Swim: return speed > .3f ? "Swim" : "SwimIdle";
                case NetworkMotionState.Typing: return "Typing";
                default: return null;
            }
        }
    }
}
