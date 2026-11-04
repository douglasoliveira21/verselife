using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Models;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// Animation Override: for each state the avatar can play something other than the default. Two kinds of replacement:
/// the alternatives the game carries (a happy or tired walk, a sprint, a spinning dance… the library), which work on every body, and the
/// animations of the person's own GLB files, which play on custom bodies (the clip is sampled and put on the bones in the model's own space).
/// Everyone who sees the avatar gets the same overrides, so the others see your walk too.
/// </summary>
public partial class AvatarRig
{
    /// <summary>Tests: answers the download of an animation file (id, hash) without a network; null means it failed.</summary>
    public static Func<Guid, string, Task<byte[]?>>? AnimationProvider { get; set; }

    private static readonly Dictionary<string, byte[]> AnimationBytes = [];
    private Dictionary<string, string> _overrides = [];
    private readonly Dictionary<string, SkeletonClip> _clips = [];
    private readonly Dictionary<string, string> _animationStatus = [];
    private int _animationToken;
    private double _clipTime;
    private AvatarMotionState _clipState = AvatarMotionState.Idle;

    /// <summary>What happened to each override: "ready", "loading" or the reason the default plays instead.</summary>
    public IReadOnlyDictionary<string, string> AnimationStatus => _animationStatus;
    public IReadOnlyDictionary<string, string> Overrides => _overrides;
    public AvatarMotionState CurrentState => _state;
    /// The node of the animation tree that is playing ("Walk", or "Walk@happy" when the library alternative was chosen).
    public string CurrentAnimationNode => _playback?.GetCurrentNode() ?? string.Empty;

    /// <summary>The node of the animation tree that plays for a state: the default, or the library alternative chosen for it.</summary>
    private string Effective(AvatarMotionState state) => _overrides.TryGetValue(state.ToString(), out var source) && source.StartsWith(AnimationStates.BuiltinPrefix, StringComparison.Ordinal) && AnimationStates.BuiltinFor(state.ToString()).Contains(source[AnimationStates.BuiltinPrefix.Length..])
        ? $"{state}@{source[AnimationStates.BuiltinPrefix.Length..]}" : StateName(state);

    /// <summary>The clip of the person's own file that plays for the current state, when there is one and the body can show it.</summary>
    private SkeletonClip? ActiveClip => CustomBodyActive && _clips.TryGetValue(_state.ToString(), out var clip) ? clip : null;

    private void StartAnimations(AvatarAppearanceData data)
    {
        var token = ++_animationToken;
        _overrides = data.AnimationOverrides; _clips.Clear(); _animationStatus.Clear(); _clipTime = 0;
        if (_playback is not null) _playback.Start(Effective(_state)); // the library alternative of the current state, if any
        var files = data.AnimationFiles;
        foreach (var (state, source) in _overrides)
        {
            if (source.StartsWith(AnimationStates.BuiltinPrefix, StringComparison.Ordinal)) { _animationStatus[state] = AnimationStates.BuiltinFor(state).Contains(source[AnimationStates.BuiltinPrefix.Length..]) ? "ready" : "Esta animação da biblioteca não existe."; continue; }
            if (!AnimationStates.TryParseCustom(source, out var fileId, out var name) || files.FirstOrDefault(f => f.Id == fileId) is not { } file) { _animationStatus[state] = "O arquivo desta animação não foi informado."; continue; }
            _animationStatus[state] = "loading"; _ = LoadClipAsync(state, file, name, token);
        }
    }

    private async Task LoadClipAsync(string state, AnimationFileInfo file, string name, int token)
    {
        byte[]? bytes;
        try { bytes = AnimationBytes.TryGetValue(file.Hash, out var known) ? known : await (AnimationProvider?.Invoke(file.Id, file.Hash) ?? DownloadAnimationAsync(file)); }
        catch (Exception) { bytes = null; }
        if (token != _animationToken || !IsInstanceValid(this)) return;
        if (bytes is null) { _animationStatus[state] = "Não foi possível baixar o arquivo desta animação."; return; }
        if (!HashMatches(bytes, file.Hash)) { _animationStatus[state] = "O arquivo da animação não confere com o que o servidor aprovou."; return; }
        if (AnimationBytes.Count > 30) AnimationBytes.Clear();
        AnimationBytes[file.Hash] = bytes;
        var clip = SkeletonClip.Read(bytes, name);
        if (clip is null) { _animationStatus[state] = $"A animação “{name}” não está no arquivo."; return; }
        _clips[state] = clip;
        _animationStatus[state] = CustomBodyActive ? "ready" : "Animação própria só aparece em corpo personalizado; o padrão toca no lugar.";
        if (_state.ToString() == state) _clipTime = 0;
    }

    private async Task<byte[]?> DownloadAnimationAsync(AnimationFileInfo file)
    {
        var cachePath = $"user://avatar_models/{file.Hash}.glb";
        if (Godot.FileAccess.FileExists(cachePath)) { var cached = Godot.FileAccess.GetFileAsBytes(cachePath); if (HashMatches(cached, file.Hash)) return cached; }
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        if (app is null || session is null || !session.IsAuthenticated) return null;
        var http = new HttpRequest { Timeout = 30, BodySizeLimit = AnimationValidator.MaxBytes + 1024 }; AddChild(http);
        if (http.Request($"{app.ApiBaseUrl}/api/v1/avatars/animations/{file.Id}/file", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) { http.QueueFree(); return null; }
        var answer = await ToSignal(http, HttpRequest.SignalName.RequestCompleted);
        http.QueueFree();
        if ((long)answer[0] != (long)HttpRequest.Result.Success || (long)answer[1] != 200) return null;
        var body = (byte[])answer[3];
        if (HashMatches(body, file.Hash)) { DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://avatar_models")); using var f = Godot.FileAccess.Open(cachePath, Godot.FileAccess.ModeFlags.Write); f?.StoreBuffer(body); }
        return body;
    }

    /// <summary>Plays a state right now (used by the editor's preview of the library and of the person's own animations).</summary>
    public void Preview(AvatarMotionState state) { _clipTime = 0; SetMotionState(state, true); }

    /// <summary>The clip's pose for now, in the space of the skeleton of the custom body: the delta of each bone and the bob of the hips. Null when no clip plays.</summary>
    private Dictionary<int, Quaternion>? ClipDeltas(Skeleton3D skeleton, Quaternion skeletonInModel, Dictionary<string, int> bones, out float hipsLift)
    {
        hipsLift = 0;
        if (ActiveClip is not { } clip) { _clipState = _state; return null; }
        if (_clipState != _state) { _clipState = _state; _clipTime = 0; }
        var loop = AnimationStates.All.TryGetValue(_state.ToString(), out var loops) && loops;
        var sample = clip.SampleDeltas((float)_clipTime, loop); var result = new Dictionary<int, Quaternion>();
        foreach (var (standard, rotation) in sample)
        {
            if (!bones.TryGetValue(standard, out var bone)) continue;
            var inModel = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
            result[bone] = skeletonInModel.Inverse() * inModel * skeletonInModel; // the clip and the avatar are both glTF models, so no turning around is needed here
        }
        hipsLift = clip.SampleHipsOffset((float)_clipTime, loop).Y;
        return result.Count > 0 ? result : null;
    }

    private void AdvanceClip(double delta) { if (ActiveClip is not null) _clipTime += delta; }

    // ---- the library ----------------------------------------------------------------------------------------------------------

    /// <summary>The alternatives of the library, as animations named "State@name"; they are nodes of the animation tree like the defaults.</summary>
    private static void AddAlternativeAnimations(AnimationLibrary library)
    {
        library.AddAnimation("Idle@relaxed", Clip(2.6, true, ("Visual/Hips/Torso:rotation", new[] { new Godot.Vector3(0, 0, .04f), new Godot.Vector3(0, 0, -.04f), new Godot.Vector3(0, 0, .04f) }), ("Visual/Hips/Torso/LeftUpperArm:rotation", new[] { new Godot.Vector3(0, 0, .08f), new Godot.Vector3(0, 0, .02f), new Godot.Vector3(0, 0, .08f) }), ("Visual/Hips/Torso/RightUpperArm:rotation", new[] { new Godot.Vector3(0, 0, -.08f), new Godot.Vector3(0, 0, -.02f), new Godot.Vector3(0, 0, -.08f) })));
        library.AddAnimation("Idle@alert", Clip(1.4, true, ("Visual/Hips/Torso:rotation", new[] { V(.06f), V(.1f), V(.06f) }), ("Visual/Hips/Torso/LeftUpperArm/LeftLowerArm:rotation", new[] { V(-.6f), V(-.7f), V(-.6f) }), ("Visual/Hips/Torso/RightUpperArm/RightLowerArm:rotation", new[] { V(-.6f), V(-.7f), V(-.6f) })));
        library.AddAnimation("Walk@happy", Clip(.9, true,
            ("Visual/Hips/LeftUpperLeg:rotation", new[] { V(.55f), V(-.55f), V(.55f) }), ("Visual/Hips/RightUpperLeg:rotation", new[] { V(-.55f), V(.55f), V(-.55f) }),
            ("Visual/Hips/Torso/LeftUpperArm:rotation", new[] { V(-.7f), V(.7f), V(-.7f) }), ("Visual/Hips/Torso/RightUpperArm:rotation", new[] { V(.7f), V(-.7f), V(.7f) }),
            ("Visual/Hips/Torso:rotation", new[] { new Godot.Vector3(0, 0, .08f), new Godot.Vector3(0, 0, -.08f), new Godot.Vector3(0, 0, .08f) })));
        library.AddAnimation("Walk@tired", Clip(1.5, true,
            ("Visual/Hips/LeftUpperLeg:rotation", new[] { V(.3f), V(-.3f), V(.3f) }), ("Visual/Hips/RightUpperLeg:rotation", new[] { V(-.3f), V(.3f), V(-.3f) }),
            ("Visual/Hips/Torso/LeftUpperArm:rotation", new[] { V(-.15f), V(.15f), V(-.15f) }), ("Visual/Hips/Torso/RightUpperArm:rotation", new[] { V(.15f), V(-.15f), V(.15f) }), ("Visual/Hips/Torso:rotation", new[] { V(.25f), V(.2f), V(.25f) })));
        library.AddAnimation("Run@sprint", Clip(.5, true,
            ("Visual/Hips/LeftUpperLeg:rotation", new[] { V(.95f), V(-.95f), V(.95f) }), ("Visual/Hips/RightUpperLeg:rotation", new[] { V(-.95f), V(.95f), V(-.95f) }),
            ("Visual/Hips/Torso/LeftUpperArm:rotation", new[] { V(-1f), V(1f), V(-1f) }), ("Visual/Hips/Torso/RightUpperArm:rotation", new[] { V(1f), V(-1f), V(1f) }), ("Visual/Hips/Torso:rotation", new[] { V(-.3f), V(-.3f), V(-.3f) })));
        library.AddAnimation("Sit@relaxed", Pose(.5, ("Visual/Hips/LeftUpperLeg:rotation", V(-1.45f)), ("Visual/Hips/RightUpperLeg:rotation", V(-1.45f)), ("Visual/Hips/LeftUpperLeg/LeftLowerLeg:rotation", V(1.45f)), ("Visual/Hips/RightUpperLeg/RightLowerLeg:rotation", V(1.45f)), ("Visual/Hips/Torso:rotation", V(.2f)), ("Visual/Hips/Torso/LeftUpperArm:rotation", V(-.5f)), ("Visual/Hips/Torso/RightUpperArm:rotation", V(-.5f))));
        library.AddAnimation("Dance@groove", Clip(.8, true, ("Visual/Hips:rotation", new[] { new Godot.Vector3(0, 0, -.25f), new Godot.Vector3(0, 0, .25f), new Godot.Vector3(0, 0, -.25f) }), ("Visual/Hips/Torso/LeftUpperArm:rotation", new[] { V(-2.6f), V(-.8f), V(-2.6f) }), ("Visual/Hips/Torso/RightUpperArm:rotation", new[] { V(-.8f), V(-2.6f), V(-.8f) }), ("Visual/Hips/LeftUpperLeg:rotation", new[] { V(.3f), V(-.3f), V(.3f) }), ("Visual/Hips/RightUpperLeg:rotation", new[] { V(-.3f), V(.3f), V(-.3f) })));
        library.AddAnimation("Dance@spin", Clip(1.6, true, ("Visual/Hips:rotation", new[] { new Godot.Vector3(0, 0, 0), new Godot.Vector3(0, Mathf.Pi, 0), new Godot.Vector3(0, Mathf.Tau, 0) }), ("Visual/Hips/Torso/LeftUpperArm:rotation", new Godot.Vector3[] { new(0, 0, -2.4f), new(0, 0, -2.4f), new(0, 0, -2.4f) }), ("Visual/Hips/Torso/RightUpperArm:rotation", new Godot.Vector3[] { new(0, 0, 2.4f), new(0, 0, 2.4f), new(0, 0, 2.4f) })));
        library.AddAnimation("Wave@big", Clip(.7, true, ("Visual/Hips/Torso/RightUpperArm:rotation", new Godot.Vector3[] { new(0, 0, 2.6f), new(0, 0, 2.6f), new(0, 0, 2.6f) }), ("Visual/Hips/Torso/RightUpperArm/RightLowerArm:rotation", new[] { V(-1.5f), V(-.2f), V(-1.5f) }), ("Visual/Hips/Torso:rotation", new[] { new Godot.Vector3(0, 0, .06f), new Godot.Vector3(0, 0, -.06f), new Godot.Vector3(0, 0, .06f) })));
        library.AddAnimation("Greet@bow", Clip(1.6, true, ("Visual/Hips/Torso:rotation", new[] { V(0), V(.95f), V(0) }), ("Visual/Hips/Torso/LeftUpperArm:rotation", new[] { V(0), V(.3f), V(0) }), ("Visual/Hips/Torso/RightUpperArm:rotation", new[] { V(0), V(.3f), V(0) })));
        library.AddAnimation("Laugh@doubled", Clip(.5, true, ("Visual/Hips/Torso:rotation", new[] { V(.35f), V(.2f), V(.35f) }), ("Visual/Hips/Torso/LeftUpperArm:rotation", new[] { V(-.3f), V(-.5f), V(-.3f) }), ("Visual/Hips/Torso/RightUpperArm:rotation", new[] { V(-.3f), V(-.5f), V(-.3f) })));
    }

    /// <summary>The names of the animation nodes of the library alternatives ("Walk@happy"…).</summary>
    private static IEnumerable<string> AlternativeNames() => AnimationStates.Builtin.SelectMany(p => p.Value.Select(n => $"{p.Key}@{n}"));
}
