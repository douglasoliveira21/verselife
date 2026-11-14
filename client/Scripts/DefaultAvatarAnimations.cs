using Godot;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The animations the game itself carries for custom bodies: a file of Mixamo clips (idle, walk, run, jump, fall, flying…) kept as a resource (<c>Assets/animations/default_avatar_animations.bin</c>, a GLB: it is not imported by the engine, the
/// bytes are read and sampled by the same code that plays the animations of the person's own files). A person's own animation for a state always wins; where there is none, a custom body plays these instead of copying the procedural doll's
/// movements. The clips are in place (the game moves the avatar) and the jump does not lift the hips (the physics already does).
/// </summary>
public static class DefaultAvatarAnimations
{
    public const string Path = "res://Assets/animations/default_avatar_animations.bin";
    /// <summary>Set the environment variable LIFEVERSE_NO_DEFAULT_ANIMATIONS=1 to play without the game's own clips (to tell whether they matter for a slowdown).</summary>
    public static bool Disabled { get; } = System.Environment.GetEnvironmentVariable("LIFEVERSE_NO_DEFAULT_ANIMATIONS") == "1";

    /// <summary>The state of the game → the name of the clip in the file. The flying clip serves both flying states; the file also carries "WalkBackwards", "FallFlat" and "DynamicPose", which no state of the game asks for yet.</summary>
    public static readonly IReadOnlyDictionary<string, string> ClipOfState = new Dictionary<string, string>
    {
        ["Idle"] = "Idle", ["Walk"] = "Walk", ["Run"] = "Run", ["Jump"] = "Jump", ["Fall"] = "Fall", ["FlyIdle"] = "FlyIdle", ["FlyMove"] = "FlyMove", ["Swim"] = "Swim", ["SwimIdle"] = "SwimIdle", ["WalkBack"] = "WalkBackwards", ["FlatImpact"] = "FallFlat", ["FallDown"] = "JumpDown"
    };

    private static Dictionary<string, SkeletonClip>? _clips;
    private static readonly object Gate = new(); private static bool _loading;
    /// <summary>Tests: bytes to use instead of the resource (null reads the resource). With bytes given, everything loads at once.</summary>
    public static byte[]? BytesForTesting { get; set; }

    public static void Reset() { lock (Gate) { _clips = null; _loading = false; } }

    private static Dictionary<string, SkeletonClip> Parse()
    {
        var loaded = new Dictionary<string, SkeletonClip>();
        var bytes = BytesForTesting ?? (Godot.FileAccess.FileExists(Path) ? Godot.FileAccess.GetFileAsBytes(Path) : null);
        if (bytes is { Length: > 0 }) foreach (var name in ClipOfState.Values.Distinct()) if (SkeletonClip.Read(bytes, name) is { } clip) loaded[name] = clip;
        return loaded;
    }

    private static Dictionary<string, SkeletonClip> LoadNow() { lock (Gate) return _clips ??= Parse(); }

    /// <summary>Reading the file takes about half a second, which would be a stutter in the middle of a walk: the game starts it in the background when the first avatar appears, and until it is done a custom body just uses the game's own animations.</summary>
    public static void Preload()
    {
        lock (Gate) { if (_clips is not null || _loading) return; _loading = true; }
        if (BytesForTesting is not null) { LoadNow(); return; }
        Task.Run(() => { var parsed = Parse(); lock (Gate) { _clips ??= parsed; _loading = false; } });
    }

    /// <summary>The clip the game plays for a state of a custom body, or null when it has none for that state (or the file is still being read).</summary>
    public static SkeletonClip? For(string state)
    {
        if (!ClipOfState.TryGetValue(state, out var name) || Disabled) return null;
        Dictionary<string, SkeletonClip>? ready; lock (Gate) ready = _clips;
        if (ready is null) { if (BytesForTesting is not null) ready = LoadNow(); else { Preload(); return null; } }
        return ready.TryGetValue(name, out var clip) ? clip : null;
    }

    /// <summary>How many clips the file gave (reads it now if needed; for tests).</summary>
    public static int Count => LoadNow().Count;
}
