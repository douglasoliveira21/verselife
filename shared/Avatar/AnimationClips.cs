using System.Numerics;
using System.Text.Json;

namespace LifeVerse.Shared.Avatar;

/// <summary>The states of the avatar an animation can replace, with the kind of animation each one needs.</summary>
public static class AnimationStates
{
    /// <summary>state → true when it plays over and over (a loop), false when it plays once.</summary>
    public static readonly IReadOnlyDictionary<string, bool> All = new Dictionary<string, bool>
    {
        ["Idle"] = true, ["Walk"] = true, ["Run"] = true, ["Jump"] = false, ["Fall"] = true, ["Landing"] = false, ["FlyIdle"] = true, ["FlyMove"] = true,
        ["Sit"] = true, ["SitFloor"] = true, ["StandUp"] = false, ["Swim"] = true, ["Dance"] = true,
        ["Wave"] = true, ["Applaud"] = true, ["Point"] = true, ["Greet"] = true, ["Laugh"] = true
    };

    /// <summary>The alternatives the game itself carries for some states (the "library"): state → names. The first animation of every state is the default and is not listed.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Builtin = new Dictionary<string, string[]>
    {
        ["Idle"] = ["relaxed", "alert"], ["Walk"] = ["happy", "tired"], ["Run"] = ["sprint"], ["Sit"] = ["relaxed"], ["Dance"] = ["groove", "spin"],
        ["Wave"] = ["big"], ["Greet"] = ["bow"], ["Laugh"] = ["doubled"]
    };

    public static string[] BuiltinFor(string state) => Builtin.TryGetValue(state, out var names) ? names : [];

    public const string BuiltinPrefix = "builtin:", CustomPrefix = "custom:";
    public const int MaxOverrides = 24;

    /// <summary>A source is "builtin:name" (an alternative of the game's own library) or "custom:{file id}#{animation name}".</summary>
    public static bool TryParseCustom(string? source, out Guid file, out string animation)
    {
        file = Guid.Empty; animation = string.Empty;
        if (source is null || !source.StartsWith(CustomPrefix, StringComparison.Ordinal)) return false;
        var rest = source[CustomPrefix.Length..]; var hash = rest.IndexOf('#');
        if (hash < 0 || !Guid.TryParse(rest.AsSpan(0, hash), out file)) return false;
        animation = rest[(hash + 1)..]; return animation.Length is > 0 and <= 80;
    }
}

public sealed record ClipInfo(string Name, float Duration, int Bones, int Keys, bool RootMotion, bool Loopable);

public sealed class AnimationReport
{
    public List<ModelIssue> Issues { get; } = [];
    public IEnumerable<ModelIssue> Errors => Issues.Where(i => i.IsError);
    public IEnumerable<ModelIssue> Warnings => Issues.Where(i => !i.IsError);
    public bool Ok => !Issues.Any(i => i.IsError);
    public List<ClipInfo> Clips { get; } = [];
    public int SizeBytes { get; set; }
    public void Error(string code, string message) => Issues.Add(new ModelIssue(code, message, true));
    public void Warn(string code, string message) => Issues.Add(new ModelIssue(code, message, false));
    public string ToJson() => JsonSerializer.Serialize(new { ok = Ok, sizeBytes = SizeBytes, clips = Clips.Select(c => new { name = c.Name, duration = c.Duration, bones = c.Bones, keys = c.Keys, rootMotion = c.RootMotion, loopable = c.Loopable }), issues = Issues.Select(i => new { code = i.Code, message = i.Message, error = i.IsError }) }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

/// <summary>
/// One animation of a glTF file, ready to be played on any humanoid. It keeps the skeleton of the file (parents and rest rotations) and the
/// rotation keys of the bones; sampling gives, for every standard bone, how far it has turned from its rest pose in the space of the model.
/// That is what is put on the bones of the avatar, so the avatar's own proportions and bone axes do not matter.
/// </summary>
public sealed class SkeletonClip
{
    private sealed record Track(float[] Times, Quaternion[] Values, bool Step);

    public string Name { get; }
    public float Duration { get; }
    private readonly int[] _parents; private readonly Quaternion[] _restLocal, _restGlobal;
    private readonly Dictionary<int, Track> _rotations; private readonly Track? _hips; private readonly Vector3[]? _hipsValues; private readonly Vector3 _hipsRest;
    private readonly Dictionary<string, int> _nodeOfBone;
    private readonly int[] _order;

    private SkeletonClip(string name, float duration, int[] parents, Quaternion[] restLocal, Dictionary<int, Track> rotations, Track? hips, Vector3[]? hipsValues, Vector3 hipsRest, Dictionary<string, int> nodeOfBone)
    {
        Name = name; Duration = duration; _parents = parents; _restLocal = restLocal; _rotations = rotations; _hips = hips; _hipsValues = hipsValues; _hipsRest = hipsRest; _nodeOfBone = nodeOfBone;
        _order = TopologicalOrder(parents);
        _restGlobal = new Quaternion[parents.Length];
        foreach (var node in _order) _restGlobal[node] = parents[node] < 0 ? restLocal[node] : Quaternion.Normalize(_restGlobal[parents[node]] * restLocal[node]);
    }

    private static int[] TopologicalOrder(int[] parents)
    {
        var order = new List<int>(); var seen = new bool[parents.Length];
        void Visit(int n) { if (seen[n]) return; seen[n] = true; if (parents[n] >= 0) Visit(parents[n]); order.Add(n); }
        for (var i = 0; i < parents.Length; i++) Visit(i);
        return [.. order];
    }

    public IReadOnlyCollection<string> Bones => _nodeOfBone.Keys;

    /// <summary>The bones that turn in this clip, by standard name.</summary>
    public IEnumerable<string> AnimatedBones => _nodeOfBone.Where(p => _rotations.ContainsKey(p.Value)).Select(p => p.Key);

    private static Quaternion SampleRotation(Track track, float time)
    {
        var times = track.Times;
        if (time <= times[0]) return track.Values[0];
        if (time >= times[^1]) return track.Values[^1];
        var i = Array.BinarySearch(times, time); if (i < 0) i = ~i - 1; i = Math.Clamp(i, 0, times.Length - 2);
        if (track.Step) return track.Values[i];
        var f = (time - times[i]) / Math.Max(times[i + 1] - times[i], 1e-6f);
        return Quaternion.Normalize(Quaternion.Slerp(track.Values[i], track.Values[i + 1], f));
    }

    /// <summary>For every standard bone: (the rotation it has now) × (its rest rotation) inverse, in the space of the model. Time wraps around when the clip loops.</summary>
    public Dictionary<string, Quaternion> SampleDeltas(float time, bool loop = true)
    {
        time = loop && Duration > 0 ? time % Duration : Math.Clamp(time, 0, Duration);
        var global = new Quaternion[_parents.Length];
        foreach (var node in _order)
        {
            var local = _rotations.TryGetValue(node, out var track) ? SampleRotation(track, time) : _restLocal[node];
            global[node] = _parents[node] < 0 ? local : Quaternion.Normalize(global[_parents[node]] * local);
        }
        var result = new Dictionary<string, Quaternion>();
        foreach (var (bone, node) in _nodeOfBone) result[bone] = Quaternion.Normalize(global[node] * Quaternion.Inverse(_restGlobal[node]));
        return result;
    }

    /// <summary>How far the hips are from where they rest, in the units of the file (the up-and-down bob of a step).</summary>
    public Vector3 SampleHipsOffset(float time, bool loop = true)
    {
        if (_hips is null || _hipsValues is null) return Vector3.Zero;
        time = loop && Duration > 0 ? time % Duration : Math.Clamp(time, 0, Duration);
        var times = _hips.Times;
        if (time <= times[0]) return _hipsValues[0] - _hipsRest; if (time >= times[^1]) return _hipsValues[^1] - _hipsRest;
        var i = Array.BinarySearch(times, time); if (i < 0) i = ~i - 1; i = Math.Clamp(i, 0, times.Length - 2);
        var f = (time - times[i]) / Math.Max(times[i + 1] - times[i], 1e-6f);
        return Vector3.Lerp(_hipsValues[i], _hipsValues[i + 1], _hips.Step ? 0 : f) - _hipsRest;
    }

    /// <summary>The names of the animations in a file (null when it cannot be read).</summary>
    public static IReadOnlyList<string>? Names(byte[] bytes)
    {
        using var gltf = GltfReader.Read(bytes, true, out _, out _); if (gltf is null) return null;
        return [.. gltf.Items("animations").Select((a, i) => a.TryGetProperty("name", out var n) && !string.IsNullOrWhiteSpace(n.GetString()) ? n.GetString()! : $"animation{i}")];
    }

    /// <summary>Reads one animation of a GLB by name (or the first when the name is empty). Null when it is not there or the file cannot be read.</summary>
    public static SkeletonClip? Read(byte[] bytes, string? name)
    {
        using var gltf = GltfReader.Read(bytes, true, out _, out _); if (gltf is null || gltf.Hierarchy() is not { } hierarchy) return null;
        var index = -1; var i = 0;
        foreach (var animation in gltf.Items("animations")) { var n = animation.TryGetProperty("name", out var nv) && !string.IsNullOrWhiteSpace(nv.GetString()) ? nv.GetString()! : $"animation{i}"; if (string.IsNullOrEmpty(name) || n == name) { index = i; break; } i++; }
        return index < 0 ? null : Build(gltf, index, hierarchy.Parents, out _, out _);
    }

    internal static SkeletonClip? Build(GltfReader gltf, int animationIndex, int[] parents, out List<ModelIssue> issues, out ClipInfo? info)
    {
        issues = []; info = null;
        var animation = gltf.At("animations", animationIndex)!.Value;
        var name = animation.TryGetProperty("name", out var nv) && !string.IsNullOrWhiteSpace(nv.GetString()) ? nv.GetString()! : $"animation{animationIndex}";
        var nodeCount = parents.Length;
        string NameOf(int node) => gltf.At("nodes", node)!.Value.TryGetProperty("name", out var n) ? n.GetString() ?? $"node{node}" : $"node{node}";
        var restLocal = new Quaternion[nodeCount]; var hipsRest = Vector3.Zero; var nodeOfBone = new Dictionary<string, int>();
        for (var node = 0; node < nodeCount; node++)
        {
            var matrix = gltf.LocalTransform(gltf.At("nodes", node)!.Value);
            restLocal[node] = Matrix4x4.Decompose(matrix, out _, out var rotation, out var translation) ? Quaternion.Normalize(rotation) : Quaternion.Identity;
            if (BoneMapper.Map(NameOf(node)) is { } bone && !nodeOfBone.ContainsKey(bone)) { nodeOfBone[bone] = node; if (bone == "hips") hipsRest = translation; }
        }
        var rotations = new Dictionary<int, Track>(); Track? hips = null; Vector3[]? hipsValues = null; var keys = 0; var ignored = 0; var rootMotion = false; var duration = 0f;
        if (!animation.TryGetProperty("samplers", out var samplers) || !animation.TryGetProperty("channels", out var channels)) return null;
        foreach (var channel in channels.EnumerateArray())
        {
            if (!channel.TryGetProperty("target", out var target) || !target.TryGetProperty("node", out var nodeValue) || !nodeValue.TryGetInt32(out var node) || node < 0 || node >= nodeCount) { ignored++; continue; }
            var path = target.TryGetProperty("path", out var p) ? p.GetString() : null;
            if (!channel.TryGetProperty("sampler", out var samplerValue) || !samplerValue.TryGetInt32(out var samplerIndex) || samplerIndex < 0 || samplerIndex >= samplers.GetArrayLength()) { ignored++; continue; }
            var sampler = samplers[samplerIndex];
            if (!sampler.TryGetProperty("input", out var input) || !sampler.TryGetProperty("output", out var output)) { ignored++; continue; }
            var interpolation = sampler.TryGetProperty("interpolation", out var ip) ? ip.GetString() ?? "LINEAR" : "LINEAR";
            var times = gltf.ReadFloats(input.GetInt32(), 100_000); var values = gltf.ReadFloats(output.GetInt32(), 400_000);
            if (times is null || values is null || times.Length < 1) { issues.Add(new ModelIssue("bad_track", $"A track of “{name}” cannot be read.", true)); continue; }
            var cubic = interpolation == "CUBICSPLINE"; var perKey = (path == "rotation" ? 4 : 3) * (cubic ? 3 : 1);
            if (values.Length != times.Length * perKey) { issues.Add(new ModelIssue("bad_track", $"A track of “{name}” has {values.Length} values for {times.Length} keys.", true)); continue; }
            for (var t = 0; t < times.Length; t++) if (!float.IsFinite(times[t]) || (t > 0 && times[t] <= times[t - 1])) { issues.Add(new ModelIssue("bad_times", $"The key times of a track of “{name}” are not increasing numbers.", true)); times = null; break; }
            if (times is null) continue;
            duration = Math.Max(duration, times[^1]); keys += times.Length;
            if (path == "rotation")
            {
                var list = new Quaternion[times.Length];
                for (var t = 0; t < times.Length; t++)
                {
                    var at = t * perKey + (cubic ? 4 : 0); var q = new Quaternion(values[at], values[at + 1], values[at + 2], values[at + 3]);
                    var length = q.Length(); if (!float.IsFinite(length) || length < .5f || length > 1.5f) { issues.Add(new ModelIssue("bad_rotation", $"A rotation in “{name}” is not a unit quaternion.", true)); list = []; break; }
                    list[t] = Quaternion.Normalize(q);
                }
                if (list.Length == 0) continue;
                if (BoneMapper.Map(NameOf(node)) is null) { ignored++; continue; }
                rotations[node] = new Track(times, list, interpolation == "STEP");
            }
            else if (path == "translation" && nodeOfBone.TryGetValue("hips", out var hipsNode) && node == hipsNode)
            {
                var list = new Vector3[times.Length];
                for (var t = 0; t < times.Length; t++) { var at = t * perKey + (cubic ? 3 : 0); list[t] = new Vector3(values[at], values[at + 1], values[at + 2]); }
                hips = new Track(times, [], interpolation == "STEP"); hipsValues = list;
                var horizontal = list.Select(v => new Vector2(v.X - hipsRest.X, v.Z - hipsRest.Z).Length()).DefaultIfEmpty(0).Max(); if (horizontal > .5f) rootMotion = true;
            }
            else ignored++;
        }
        if (ignored > 0) issues.Add(new ModelIssue("ignored_tracks", $"{ignored} track(s) of “{name}” are for bones the game does not use (fingers, toes…), or are scale/translation, and are ignored.", false));
        if (rotations.Count == 0) { info = new ClipInfo(name, duration, 0, keys, rootMotion, false); return null; }
        var loopable = rotations.Values.All(t => t.Values.Length < 2 || Quaternion.Dot(t.Values[0], t.Values[^1]) is > .995f or < -.995f);
        var clip = new SkeletonClip(name, duration, parents, restLocal, rotations, hips, hipsValues, hipsRest, nodeOfBone);
        info = new ClipInfo(name, duration, rotations.Keys.Count(n => BoneMapper.Map(NameOf(n)) is not null), keys, rootMotion, loopable);
        return clip;
    }
}

/// <summary>
/// Decides whether a GLB can supply animations to avatars: readable, with a humanoid skeleton the clips can be matched to (at least eight of the
/// standard bones are animated), sane durations and key counts, finite unit rotations and increasing times. Root motion and tracks of bones the
/// game does not use are reported but not refused: the game ignores them.
/// </summary>
public static class AnimationValidator
{
    public const int MaxBytes = 5 * 1024 * 1024, MaxClips = 20, MaxKeysPerClip = 60_000, MinBones = 8;
    public const float MinDuration = .2f, MaxDuration = 30f;

    public static AnimationReport Validate(byte[] bytes)
    {
        var report = new AnimationReport { SizeBytes = bytes.Length };
        if (bytes.Length == 0) { report.Error("empty", "The file is empty."); return report; }
        if (bytes.Length > MaxBytes) { report.Error("too_big", $"The file is larger than {MaxBytes / 1024 / 1024} MB."); return report; }
        using var gltf = GltfReader.Read(bytes, true, out var code, out var message);
        if (gltf is null) { report.Error(code, message); return report; }
        foreach (var image in gltf.Items("images")) if (image.TryGetProperty("uri", out var uri) && !(uri.GetString() ?? string.Empty).StartsWith("data:", StringComparison.OrdinalIgnoreCase)) { report.Error("external_reference", "A texture points to another file. Export a single .glb."); break; }
        var count = gltf.Count("animations");
        if (count == 0) { report.Error("no_animations", "The file has no animations."); return report; }
        if (count > MaxClips) { report.Error("too_many_clips", $"The file has {count} animations; the limit is {MaxClips}."); return report; }
        if (gltf.Hierarchy() is not { } hierarchy) { report.Error("bad_hierarchy", "The nodes of the file are not a valid tree."); return report; }
        var names = new HashSet<string>();
        for (var i = 0; i < count; i++)
        {
            var clip = SkeletonClip.Build(gltf, i, hierarchy.Parents, out var issues, out var info);
            foreach (var issue in issues) report.Issues.Add(issue);
            if (info is null) { if (!issues.Any(x => x.IsError)) report.Error("too_few_bones", $"“{gltf.At("animations", i)!.Value.GetProperty("name").GetString()}” does not animate any bone of the humanoid skeleton."); continue; }
            report.Clips.Add(info);
            if (!names.Add(info.Name)) report.Error("duplicate_name", $"Two animations are called “{info.Name}”.");
            if (clip is null) { report.Error("too_few_bones", $"“{info.Name}” does not animate any bone of the humanoid skeleton."); continue; }
            if (info.Duration < MinDuration || info.Duration > MaxDuration) report.Error("bad_duration", $"“{info.Name}” lasts {info.Duration:0.##} s; it must be between {MinDuration} and {MaxDuration} s.");
            if (info.Bones < MinBones) report.Error("too_few_bones", $"“{info.Name}” animates only {info.Bones} bones of the humanoid; at least {MinBones} are needed.");
            if (info.Keys > MaxKeysPerClip) report.Error("too_many_keys", $"“{info.Name}” has {info.Keys:N0} keys; the limit is {MaxKeysPerClip:N0}.");
            if (info.RootMotion) report.Warn("root_motion", $"“{info.Name}” moves the hips more than half a metre; the game plays it in place.");
            if (!info.Loopable) report.Warn("not_loopable", $"“{info.Name}” does not end where it starts, so it will jump when it loops.");
        }
        return report;
    }
}
