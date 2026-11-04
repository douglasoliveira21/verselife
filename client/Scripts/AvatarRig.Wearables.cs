using System.Security.Cryptography;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Models;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// Items a person brought (checked by the server, downloaded by hash like custom bodies). A rigid piece (hat, glasses, jewellery, backpack) is
/// fixed to a bone: it follows the bone's movement from where the item says it sits, on the procedural body and on custom bodies alike.
/// Rigged clothing is bound to the skeleton of a custom body: its bones are matched to the avatar's bones by name (any naming convention), and
/// its skin is rebound to the avatar's own rest pose, so the garment bends with the body.
/// </summary>
public partial class AvatarRig
{
    /// <summary>Tests: answers the download of a wearable (id, hash) without a network; null means it failed.</summary>
    public static Func<Guid, string, Task<byte[]?>>? WearableProvider { get; set; }

    private const float StandardHeight = 1.78f, VisibleHeight = 1.8f;
    private static readonly Dictionary<string, byte[]> WearableBytes = [];

    private static readonly Dictionary<string, string> ProceduralBones = new()
    {
        ["hips"] = "Hips", ["spine"] = "Hips/Torso", ["chest"] = "Hips/Torso", ["neck"] = "Hips/Torso/Neck", ["head"] = "Hips/Torso/Neck/Head",
        ["upper_arm_l"] = "Hips/Torso/LeftUpperArm", ["lower_arm_l"] = "Hips/Torso/LeftUpperArm/LeftLowerArm", ["hand_l"] = "Hips/Torso/LeftUpperArm/LeftLowerArm/LeftHand",
        ["upper_arm_r"] = "Hips/Torso/RightUpperArm", ["lower_arm_r"] = "Hips/Torso/RightUpperArm/RightLowerArm", ["hand_r"] = "Hips/Torso/RightUpperArm/RightLowerArm/RightHand",
        ["upper_leg_l"] = "Hips/LeftUpperLeg", ["lower_leg_l"] = "Hips/LeftUpperLeg/LeftLowerLeg", ["foot_l"] = "Hips/LeftUpperLeg/LeftLowerLeg/LeftFoot",
        ["upper_leg_r"] = "Hips/RightUpperLeg", ["lower_leg_r"] = "Hips/RightUpperLeg/RightLowerLeg", ["foot_r"] = "Hips/RightUpperLeg/RightLowerLeg/RightFoot"
    };

    private sealed class Follower
    {
        public Node3D Item = null!; public Node3D? Proxy; public Skeleton3D? Skeleton; public int Bone;
        public Basis RestRot, RigRot, ItemRot; public Vector3 Corr, Offset; public float Scale;
    }

    private readonly Dictionary<Guid, byte[]> _fetched = [];
    private readonly List<Node> _placed = [];
    private readonly List<Follower> _followers = [];
    private IReadOnlyList<WornWearable> _worn = [];
    private int _wearToken;
    private readonly Dictionary<Guid, string> _wearStatus = [];

    /// <summary>How many custom items are on the avatar right now.</summary>
    public int WearablesShown => _placed.Count(n => IsInstanceValid(n));
    /// <summary>What happened to each custom item: "ready", "loading" or the reason it is not shown.</summary>
    public IReadOnlyDictionary<Guid, string> WearableStatus => _wearStatus;
    public IReadOnlyList<Node> PlacedWearables => [.. _placed.Where(IsInstanceValid)];

    private void StartWearables(AvatarAppearanceData data)
    {
        var token = ++_wearToken;
        ClearPlaced(); _fetched.Clear(); _wearStatus.Clear();
        _worn = _detail >= AvatarDetail.Full ? data.CustomWearables : []; // custom items are only fetched and drawn for avatars that are close and in budget
        foreach (var item in _worn) { _wearStatus[item.Id] = "loading"; _ = LoadWearableAsync(item, token); }
    }

    private async Task LoadWearableAsync(WornWearable item, int token)
    {
        byte[]? bytes;
        try { bytes = WearableBytes.TryGetValue(item.Hash, out var known) ? known : await (WearableProvider?.Invoke(item.Id, item.Hash) ?? DownloadWearableAsync(item)); }
        catch (Exception) { bytes = null; }
        if (token != _wearToken || !IsInstanceValid(this)) return;
        if (bytes is null) { _wearStatus[item.Id] = "Não foi possível baixar este item."; return; }
        if (!HashMatches(bytes, item.Hash)) { _wearStatus[item.Id] = "O arquivo do item não confere com o que o servidor aprovou."; return; }
        if (WearableBytes.Count > 40) WearableBytes.Clear();
        WearableBytes[item.Hash] = bytes; _fetched[item.Id] = bytes;
        PlaceWearables();
    }

    private async Task<byte[]?> DownloadWearableAsync(WornWearable item)
    {
        var cachePath = $"user://avatar_models/{item.Hash}.glb";
        if (Godot.FileAccess.FileExists(cachePath)) { var cached = Godot.FileAccess.GetFileAsBytes(cachePath); if (HashMatches(cached, item.Hash)) return cached; }
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        if (app is null || session is null || !session.IsAuthenticated) return null;
        var http = new HttpRequest { Timeout = 30, BodySizeLimit = WearableValidator.MaxBytes + 1024 }; AddChild(http);
        if (http.Request($"{app.ApiBaseUrl}/api/v1/avatars/wearables/{item.Id}/file", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) { http.QueueFree(); return null; }
        var answer = await ToSignal(http, HttpRequest.SignalName.RequestCompleted);
        http.QueueFree();
        if ((long)answer[0] != (long)HttpRequest.Result.Success || (long)answer[1] != 200) return null;
        var body = (byte[])answer[3];
        if (HashMatches(body, item.Hash)) { DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://avatar_models")); using var file = Godot.FileAccess.Open(cachePath, Godot.FileAccess.ModeFlags.Write); file?.StoreBuffer(body); }
        return body;
    }

    private void ClearPlaced()
    {
        foreach (var node in _placed) if (IsInstanceValid(node)) node.QueueFree();
        _placed.Clear(); _followers.Clear();
    }

    /// <summary>Puts every item whose file has arrived on the avatar, as the body now is (called when a file arrives and when the custom body changes).</summary>
    private void PlaceWearables()
    {
        ClearPlaced();
        foreach (var item in _worn)
        {
            if (!_fetched.TryGetValue(item.Id, out var bytes)) continue;
            try
            {
                var placed = item.Kind == "clothing" ? PlaceClothing(item, bytes) : PlaceAttachment(item, bytes);
                _wearStatus[item.Id] = placed ?? "ready";
            }
            catch (Exception exception) when (exception is InvalidCastException or NullReferenceException or ArgumentException) { _wearStatus[item.Id] = "Este item não pôde ser desenhado."; }
        }
    }

    private static Node? Instantiate(byte[] bytes)
    {
        var document = new GltfDocument(); var state = new GltfState();
        return document.AppendFromBuffer(bytes, string.Empty, state) == Error.Ok ? document.GenerateScene(state) : null;
    }

    private static Basis EulerDegrees(float[] values) => values.Length == 3 ? Basis.FromEuler(new Vector3(Mathf.DegToRad(values[0]), Mathf.DegToRad(values[1]), Mathf.DegToRad(values[2]))) : Basis.Identity;
    private static Vector3 Vec(float[] values) => values.Length == 3 ? new Vector3(values[0], values[1], values[2]) : Vector3.Zero;

    /// <summary>Fixes a rigid piece to a bone. Null when placed, otherwise why not.</summary>
    private string? PlaceAttachment(WornWearable item, byte[] bytes)
    {
        var bone = string.IsNullOrEmpty(item.AttachBone) ? "head" : item.AttachBone;
        var scene = Instantiate(bytes) as Node3D; if (scene is null) return "O engine não conseguiu ler este item.";
        var holder = new Node3D { Name = "Wearable_" + item.Slot }; AddChild(holder); holder.AddChild(scene); _placed.Add(holder);
        var k = VisibleHeight / StandardHeight;
        var follower = new Follower { Item = holder, Offset = Vec(item.Offset), ItemRot = EulerDegrees(item.Rotation), Scale = k * (item.Scale > 0 ? item.Scale : 1f), RigRot = GlobalBasis.Orthonormalized() };
        var flip = Basis.FromEuler(new Vector3(0, Mathf.Pi, 0));
        if (CustomBodyActive && _customSkeleton is { } skeleton)
        {
            var index = -1; for (var i = 0; i < skeleton.GetBoneCount() && index < 0; i++) if (BoneMapper.Map(skeleton.GetBoneName(i)) == bone) index = i;
            if (index < 0) { holder.QueueFree(); _placed.Remove(holder); return $"O corpo personalizado não tem o osso “{bone}”."; }
            follower.Skeleton = skeleton; follower.Bone = index; follower.RestRot = (skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(index)).Basis.Orthonormalized();
        }
        else
        {
            if (!ProceduralBones.TryGetValue(bone, out var path) || _visual.GetNodeOrNull<Node3D>(path) is not { } proxy) { holder.QueueFree(); _placed.Remove(holder); return $"O corpo padrão não tem o osso “{bone}”."; }
            // the procedural parts are not where the standard bones are (the head node is the middle of the head), so the difference is kept
            var standard = Vec(WearableValidator.ReferencePosition(bone)) * k;
            var target = GlobalPosition + follower.RigRot * flip * standard;
            follower.Proxy = proxy; follower.RestRot = proxy.GlobalBasis.Orthonormalized(); follower.Corr = target - proxy.GlobalPosition;
        }
        _followers.Add(follower); Follow(follower);
        return null;
    }

    private void FollowWearables() { foreach (var follower in _followers) if (IsInstanceValid(follower.Item)) Follow(follower); }

    private static void Follow(Follower f)
    {
        Vector3 origin; Basis rot;
        if (f.Proxy is { } proxy && IsInstanceValid(proxy)) { origin = proxy.GlobalPosition; rot = proxy.GlobalBasis.Orthonormalized(); }
        else if (f.Skeleton is { } skeleton && IsInstanceValid(skeleton)) { var world = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(f.Bone); origin = world.Origin; rot = world.Basis.Orthonormalized(); }
        else return;
        var flip = Basis.FromEuler(new Vector3(0, Mathf.Pi, 0));
        var delta = rot * f.RestRot.Inverse();
        var k = VisibleHeight / StandardHeight;
        var basis = (delta * f.RigRot * flip * f.ItemRot).Scaled(Vector3.One * f.Scale);
        f.Item.GlobalTransform = new Transform3D(basis, origin + delta * (f.Corr + f.RigRot * flip * f.Offset * k));
    }

    /// <summary>
    /// Binds a rigged garment to the skeleton of the custom body. Each bone of the garment is matched to the bone of the avatar that plays the same
    /// part (by name, any convention); the skin then uses the avatar's own rest pose, so what the garment shows at rest is where it was made and
    /// it follows the bones from there. Null when placed, otherwise why not.
    /// </summary>
    private string? PlaceClothing(WornWearable item, byte[] bytes)
    {
        if (!CustomBodyActive || _customSkeleton is not { } avatarSkeleton || _customRoot is not Node3D customRoot) return "Roupa com esqueleto só funciona em um corpo personalizado.";
        var scene = Instantiate(bytes); if (scene is null) return "O engine não conseguiu ler esta roupa.";
        var avatarBones = new Dictionary<string, int>();
        for (var i = 0; i < avatarSkeleton.GetBoneCount(); i++) if (BoneMapper.Map(avatarSkeleton.GetBoneName(i)) is { } standard) avatarBones.TryAdd(standard, i);
        var meshes = Meshes(scene).Where(m => m.Skin is not null).ToList();
        if (meshes.Count == 0) { scene.QueueFree(); return "Esta roupa não tem malha ligada ao esqueleto."; }
        var shown = 0;
        foreach (var mesh in meshes)
        {
            var skin = new Skin(); var bound = 0;
            for (var bind = 0; bind < mesh.Skin!.GetBindCount(); bind++)
            {
                var name = mesh.Skin.GetBindName(bind); if (string.IsNullOrEmpty(name) && mesh.Skeleton is { IsEmpty: false } && mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton) is { } own) name = own.GetBoneName(mesh.Skin.GetBindBone(bind));
                if (BoneMapper.Map(name) is { } standard && avatarBones.TryGetValue(standard, out var index)) { skin.AddNamedBind(avatarSkeleton.GetBoneName(index), avatarSkeleton.GetBoneGlobalRest(index).AffineInverse()); bound++; }
                else skin.AddNamedBind(avatarSkeleton.GetBoneName(avatarBones.GetValueOrDefault("hips", 0)), avatarSkeleton.GetBoneGlobalRest(avatarBones.GetValueOrDefault("hips", 0)).AffineInverse()); // a bone the avatar lacks: follow the hips instead of tearing
            }
            if (bound == 0) continue;
            var copy = new MeshInstance3D { Name = "Cloth_" + item.Slot, Mesh = mesh.Mesh, Skin = skin };
            avatarSkeleton.AddChild(copy); copy.Skeleton = new NodePath("..");
            // the garment was made in the same space as the model, whatever the armature does to the skeleton
            copy.Transform = avatarSkeleton.GlobalTransform.AffineInverse() * customRoot.GlobalTransform;
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++) if (mesh.GetActiveMaterial(surface) is { } material) copy.SetSurfaceOverrideMaterial(surface, material);
            _placed.Add(copy); shown++;
        }
        scene.QueueFree();
        return shown > 0 ? null : "Os ossos desta roupa não foram reconhecidos no corpo.";
    }
}
