using System.Security.Cryptography;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Models;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// A custom body: the GLB a person uploaded and the server checked. Everybody downloads the same file by its hash (and checks the hash), the
/// engine imports it, and it is scaled to the standard height and put on the ground. It has no animations of its own: the procedural body
/// keeps playing the game's animations out of sight, and every frame its joint rotations are copied onto the matching bones of the model
/// (matched by <see cref="BoneMapper"/>), so walking, sitting, dancing and the other states work for any humanoid skeleton. Until the file
/// arrives, and if it cannot be used, the procedural body is shown.
/// </summary>
public partial class AvatarRig
{
    /// <summary>Tests: answers the download of a model (id, hash) without a network; null means it failed.</summary>
    public static Func<Guid, string, Task<byte[]?>>? ModelProvider { get; set; }

    private static readonly Dictionary<string, string> ProxyPaths = new()
    {
        ["hips"] = "Hips", ["spine"] = "Hips/Torso",
        ["upper_arm_l"] = "Hips/Torso/LeftUpperArm", ["lower_arm_l"] = "Hips/Torso/LeftUpperArm/LeftLowerArm",
        ["upper_arm_r"] = "Hips/Torso/RightUpperArm", ["lower_arm_r"] = "Hips/Torso/RightUpperArm/RightLowerArm",
        ["upper_leg_l"] = "Hips/LeftUpperLeg", ["lower_leg_l"] = "Hips/LeftUpperLeg/LeftLowerLeg",
        ["upper_leg_r"] = "Hips/RightUpperLeg", ["lower_leg_r"] = "Hips/RightUpperLeg/RightLowerLeg"
    };

    private Node3D? _customBody;
    private Node? _customRoot;
    private Skeleton3D? _customSkeleton;
    private int _customToken;
    private (int Bone, Node3D Proxy)[] _retarget = [];
    private Quaternion[] _restGlobal = [], _globalNow = [];
    private bool[] _globalDone = [];
    private float _customScale = 1f;
    private readonly Dictionary<string, int> _standardBones = [];
    private bool _hipsLifted;
    private readonly Dictionary<int, Quaternion> _neutral = [];

    /// <summary>True while a custom body is on the avatar (the procedural body is hidden).</summary>
    public bool CustomBodyActive => _customBody is not null && IsInstanceValid(_customBody);
    /// <summary>What happened to the last custom body: empty when there is none, "loading", "ready" or the reason it was not used.</summary>
    public string CustomBodyStatus { get; private set; } = string.Empty;
    /// <summary>Which standard bones of the model follow the animations (for the editor and tests).</summary>
    public IReadOnlyCollection<string> RetargetedBones => [.. _retarget.Select(r => BoneMapper.Map(_customSkeleton!.GetBoneName(r.Bone)) ?? string.Empty)];
    public Node3D? CustomBodyNode => CustomBodyActive ? _customBody : null;

    private void StartCustomBody(AvatarAppearanceData data)
    {
        var token = ++_customToken;
        if (_customBody is not null && IsInstanceValid(_customBody)) _customBody.QueueFree();
        _customBody = null; _customRoot = null; _customSkeleton = null; _retarget = []; CustomBodyStatus = string.Empty;
        if (data.CustomModelId is not { } id || string.IsNullOrEmpty(data.CustomModelHash)) return;
        CustomBodyStatus = "loading"; _customScale = data.CustomModelScale > 0 ? data.CustomModelScale : 1f;
        _ = LoadCustomAsync(id, data.CustomModelHash!, token);
    }

    private async Task LoadCustomAsync(Guid id, string hash, int token)
    {
        byte[]? bytes;
        try { bytes = await (ModelProvider?.Invoke(id, hash) ?? DownloadModelAsync(id, hash)); }
        catch (Exception) { bytes = null; }
        if (token != _customToken || !IsInstanceValid(this)) return; // the avatar changed while it downloaded
        if (bytes is null) { CustomBodyStatus = "Não foi possível baixar o corpo personalizado."; return; }
        if (!HashMatches(bytes, hash)) { CustomBodyStatus = "O arquivo do corpo não confere com o que o servidor aprovou."; return; }
        AttachCustom(bytes);
    }

    private static bool HashMatches(byte[] bytes, string hash) => string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), hash, StringComparison.OrdinalIgnoreCase);

    private async Task<byte[]?> DownloadModelAsync(Guid id, string hash)
    {
        var cachePath = $"user://avatar_models/{hash}.glb";
        if (Godot.FileAccess.FileExists(cachePath)) { var cached = Godot.FileAccess.GetFileAsBytes(cachePath); if (HashMatches(cached, hash)) return cached; }
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        if (app is null || session is null || !session.IsAuthenticated) return null;
        var http = new HttpRequest { Timeout = 30, BodySizeLimit = AvatarModelValidator.MaxBytes + 1024 }; AddChild(http);
        if (http.Request($"{app.ApiBaseUrl}/api/v1/avatars/models/{id}/file", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) { http.QueueFree(); return null; }
        var answer = await ToSignal(http, HttpRequest.SignalName.RequestCompleted);
        http.QueueFree();
        if ((long)answer[0] != (long)HttpRequest.Result.Success || (long)answer[1] != 200) return null;
        var body = (byte[])answer[3];
        if (HashMatches(body, hash)) { DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://avatar_models")); using var file = Godot.FileAccess.Open(cachePath, Godot.FileAccess.ModeFlags.Write); file?.StoreBuffer(body); }
        return body;
    }

    private static Skeleton3D? FindSkeleton(Node node)
    {
        if (node is Skeleton3D skeleton) return skeleton;
        foreach (var child in node.GetChildren()) if (FindSkeleton(child) is { } found) return found;
        return null;
    }

    private static IEnumerable<MeshInstance3D> Meshes(Node node)
    {
        if (node is MeshInstance3D mesh) yield return mesh;
        foreach (var child in node.GetChildren()) foreach (var found in Meshes(child)) yield return found;
    }

    private void AttachCustom(byte[] bytes)
    {
        var document = new GltfDocument(); var state = new GltfState();
        if (document.AppendFromBuffer(bytes, string.Empty, state) != Error.Ok) { CustomBodyStatus = "O engine não conseguiu ler o corpo personalizado."; return; }
        var scene = document.GenerateScene(state);
        if (scene is null || FindSkeleton(scene) is not { } skeleton) { scene?.QueueFree(); CustomBodyStatus = "O corpo personalizado não tem esqueleto."; return; }

        // a glTF model looks along +Z and this game looks along -Z, so the model is turned around; the animation copy below accounts for it
        var holder = new Node3D { Name = "CustomBody", Rotation = new Vector3(0, Mathf.Pi, 0), Scale = Vector3.One * _customScale };
        AddChild(holder); holder.AddChild(scene);
        var low = float.MaxValue;
        foreach (var mesh in Meshes(scene))
        {
            var box = mesh.GetAabb(); var toHolder = holder.GlobalTransform.AffineInverse() * mesh.GlobalTransform;
            for (var corner = 0; corner < 8; corner++) low = Mathf.Min(low, (toHolder * (box.Position + new Vector3((corner & 1) * box.Size.X, ((corner >> 1) & 1) * box.Size.Y, ((corner >> 2) & 1) * box.Size.Z))).Y);
        }
        holder.Position = new Vector3(0, low < float.MaxValue ? -low * _customScale : 0, 0);

        // which bones follow which animated part of the hidden procedural body
        var table = new List<(int, Node3D)>(); var used = new HashSet<string>();
        for (var bone = 0; bone < skeleton.GetBoneCount(); bone++)
            if (BoneMapper.Map(skeleton.GetBoneName(bone)) is { } standard && ProxyPaths.TryGetValue(standard, out var path) && used.Add(standard) && _visual.GetNodeOrNull<Node3D>(path) is { } proxy)
                table.Add((bone, proxy));
        if (table.Count == 0) { holder.QueueFree(); CustomBodyStatus = "Os ossos do corpo personalizado não foram reconhecidos."; return; }

        // the procedural parts keep animating but are no longer drawn
        if (_visual.GetNodeOrNull<Node3D>("Hips") is { } hips) foreach (var mesh in Meshes(hips)) mesh.Visible = false;
        _customBody = holder; _customRoot = scene; _customSkeleton = skeleton; _retarget = [.. table];
        _standardBones.Clear();
        for (var bone = 0; bone < skeleton.GetBoneCount(); bone++) if (BoneMapper.Map(skeleton.GetBoneName(bone)) is { } named) _standardBones.TryAdd(named, bone);
        _restGlobal = new Quaternion[skeleton.GetBoneCount()]; _globalNow = new Quaternion[skeleton.GetBoneCount()]; _globalDone = new bool[skeleton.GetBoneCount()];
        for (var bone = 0; bone < _restGlobal.Length; bone++) _restGlobal[bone] = skeleton.GetBoneGlobalRest(bone).Basis.Orthonormalized().GetRotationQuaternion();
        ComputeNeutralPose(skeleton, scene);
        CustomBodyStatus = "ready";
        RetargetCustom();
        PlaceWearables(); // pieces and garments that were waiting for the body
    }

    /// <summary>
    /// Models are usually modelled with the arms out (a T-pose) or half down (an A-pose), while the game's animations start from arms hanging down.
    /// For every arm bone this works out the rotation that brings it (and the forearm below it) to hang straight down; it is applied under every animation.
    /// </summary>
    private void ComputeNeutralPose(Skeleton3D skeleton, Node model)
    {
        _neutral.Clear();
        if (model is not Node3D root) return;
        var skeletonInModel = (root.GlobalBasis.Inverse() * skeleton.GlobalBasis).Orthonormalized().GetRotationQuaternion();
        var down = skeletonInModel.Inverse() * Vector3.Down;
        int Find(string standard) { for (var i = 0; i < skeleton.GetBoneCount(); i++) if (BoneMapper.Map(skeleton.GetBoneName(i)) == standard) return i; return -1; }
        foreach (var side in new[] { "l", "r" })
        {
            var upper = Find($"upper_arm_{side}"); var lower = Find($"lower_arm_{side}"); var hand = Find($"hand_{side}");
            if (upper < 0 || lower < 0 || hand < 0) continue;
            var upperDirection = skeleton.GetBoneGlobalRest(lower).Origin - skeleton.GetBoneGlobalRest(upper).Origin;
            var lowerDirection = skeleton.GetBoneGlobalRest(hand).Origin - skeleton.GetBoneGlobalRest(lower).Origin;
            if (upperDirection.LengthSquared() < 1e-10f || lowerDirection.LengthSquared() < 1e-10f) continue;
            var upperTurn = new Quaternion(upperDirection.Normalized(), down.Normalized());
            var lowerTurn = new Quaternion((upperTurn * lowerDirection).Normalized(), down.Normalized()) * upperTurn;
            _neutral[upper] = upperTurn; _neutral[lower] = lowerTurn;
        }
    }

    public override void _Process(double delta) { AdvanceClip(delta); RetargetCustom(); FollowWearables(); }

    /// <summary>Copies the animated rotations of the procedural body onto the bones of the custom model, in the model's own space.</summary>
    public void RetargetCustom()
    {
        if (_customSkeleton is null || !IsInstanceValid(_customSkeleton) || _customRoot is not Node3D root || _retarget.Length == 0) return;
        var skeleton = _customSkeleton;
        var flip = new Quaternion(Vector3.Up, Mathf.Pi);
        var skeletonInModel = (((Node3D)root).GlobalBasis.Inverse() * skeleton.GlobalBasis).Orthonormalized().GetRotationQuaternion();
        var delta = new Dictionary<int, Quaternion>(); var clipDeltas = ClipDeltas(skeleton, skeletonInModel, _standardBones, out var hipsLift);
        if (clipDeltas is not null) delta = clipDeltas; // the person's own animation drives the bones
        else foreach (var (bone, proxy) in _retarget)
        {
            var inRig = (GlobalBasis.Inverse() * proxy.GlobalBasis).Orthonormalized().GetRotationQuaternion();
            var inModel = flip.Inverse() * inRig * flip;
            delta[bone] = skeletonInModel.Inverse() * inModel * skeletonInModel;
            if (_neutral.TryGetValue(bone, out var neutral)) delta[bone] *= neutral; // first the arm is lowered from its modelled pose, then the animation moves it
        }
        if (_standardBones.TryGetValue("hips", out var hipsBone))
        {
            if (clipDeltas is not null) { skeleton.SetBonePosePosition(hipsBone, skeleton.GetBoneRest(hipsBone).Origin + Vector3.Up * hipsLift); _hipsLifted = true; }
            else if (_hipsLifted) { skeleton.SetBonePosePosition(hipsBone, skeleton.GetBoneRest(hipsBone).Origin); _hipsLifted = false; }
        }
        Array.Clear(_globalDone);
        for (var bone = 0; bone < _restGlobal.Length; bone++) GlobalOf(skeleton, bone, delta);
    }

    private Quaternion GlobalOf(Skeleton3D skeleton, int bone, Dictionary<int, Quaternion> delta)
    {
        if (_globalDone[bone]) return _globalNow[bone];
        var parent = skeleton.GetBoneParent(bone);
        var parentGlobal = parent < 0 ? Quaternion.Identity : GlobalOf(skeleton, parent, delta);
        var restLocal = skeleton.GetBoneRest(bone).Basis.Orthonormalized().GetRotationQuaternion();
        var global = delta.TryGetValue(bone, out var d) ? d * _restGlobal[bone] : parentGlobal * restLocal;
        skeleton.SetBonePoseRotation(bone, parentGlobal.Inverse() * global);
        _globalNow[bone] = global; _globalDone[bone] = true;
        return global;
    }
}
