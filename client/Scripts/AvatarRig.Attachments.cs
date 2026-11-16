using Godot;
using LifeVerse.Client.Scripts.Attachments;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// Objects of the inventory worn on the body (see the server's attachments). Each one is a holder that follows a bone every frame, exactly as the rigid wearables do, with the placement the
/// server keeps: a position, rotation and scale relative to the attachment point, never to the world. The holders belong to the rig, so teleporting or changing animation moves the
/// bone and the object follows; nothing about the placement is touched by a change of animation. They are rebuilt when the list changes, when the look is applied again and when the custom body arrives.
/// </summary>
public partial class AvatarRig
{
    /// <summary>Tests: supplies the model of an asset without a network (see ModelCache.ParseInstance); null means it failed.</summary>
    public static Func<Guid, ModelInstance?>? AttachmentModelProvider { get; set; }

    private const float LargestAttachment = 3f;
    private sealed class Worn { public AttachmentData Data = null!; public Node3D Holder = null!; public Follower? Follower; public Vector3 Tilt, LastPosition; }

    private IReadOnlyList<AttachmentData> _attachData = [];
    private readonly Dictionary<Guid, Worn> _attached = [];
    private readonly Dictionary<Guid, string> _attachStatus = [];
    private int _attachToken;

    /// <summary>How many attachments are on the body right now.</summary>
    public int AttachmentsShown => _attached.Count(p => IsInstanceValid(p.Value.Holder));
    /// <summary>What happened to each attachment: "ready", "loading" or the reason it is not shown.</summary>
    public IReadOnlyDictionary<Guid, string> AttachmentStatus => _attachStatus;
    public Node3D? AttachmentHolder(Guid id) => _attached.TryGetValue(id, out var w) && IsInstanceValid(w.Holder) ? w.Holder : null;

    /// <summary>Sets what the avatar wears (only what is equipped is drawn) and draws it.</summary>
    public void ApplyAttachments(IReadOnlyList<AttachmentData> items) { _attachData = items; RebuildAttachments(); }

    private void FollowAttachments(double delta = 0) { foreach (var worn in _attached.Values) if (worn.Follower is { } f && IsInstanceValid(f.Item)) { Follow(f); if (delta > 0) Sway(worn, (float)delta); } }

    private void ClearAttachments()
    {
        foreach (var worn in _attached.Values) if (IsInstanceValid(worn.Holder)) worn.Holder.QueueFree();
        _attached.Clear(); _attachStatus.Clear(); _attachFit.Clear(); _attachTriangles = 0; _attachLights = 0; _attachParticles = 0;
    }

    private void RebuildAttachments()
    {
        var token = ++_attachToken; ClearAttachments();
        if (_detail < AvatarDetail.Full) return; // far avatars do not draw other people's objects
        foreach (var data in _attachData.Where(a => a.Equipped))
        {
            var point = AttachmentPoints.Find(data.Point);
            if (point is null) { _attachStatus[data.Id] = "Este ponto do corpo não existe mais."; continue; }
            if (point.IsHud) { _attachStatus[data.Id] = "HUD: aparece na tela, não no corpo."; continue; }
            _attachStatus[data.Id] = "loading";
            LoadAttachmentModel(data.AssetId, (node, real) => { if (token == _attachToken && IsInstanceValid(this)) PlaceAttachedObject(data, point, node, real); });
        }
    }

    private void LoadAttachmentModel(Guid asset, Action<Node3D?, Vector3> done)
    {
        if (AttachmentModelProvider is { } provider)
        {
            var found = provider(asset); done(found?.Node, found?.Real ?? Vector3.One); return;
        }
        for (Node? n = GetParent(); n is not null; n = n.GetParent())
            if (n.GetNodeOrNull<ModelCache>("ModelCache") is { } cache) { cache.Request(asset, m => done(m?.Node, m?.Real ?? Vector3.One)); return; }
        done(null, Vector3.One);
    }

    private static string[] BoneFallbacks(string bone) => bone switch { "chest" => ["chest", "spine"], "root" => ["root", "hips"], _ => [bone] };

    private void PlaceAttachedObject(AttachmentData data, AttachmentPoint point, Node3D? model, Vector3 real)
    {
        if (model is null) { _attachStatus[data.Id] = "Não foi possível carregar o modelo deste objeto."; return; }
        if (BudgetProblem(model) is { } overBudget) { model.QueueFree(); _attachStatus[data.Id] = overBudget; return; }
        // models come normalised to one metre (or, from the test provider, in their own units): bring them back to their real size, within reason
        var longest = Mathf.Max(real.X, Mathf.Max(real.Y, real.Z)); var fit = longest > .0001f ? Mathf.Min(longest, LargestAttachment) : 1f;
        var holder = new Node3D { Name = "Attachment_" + point.Key }; AddChild(holder); holder.AddChild(model); LimitEffects(model); StartModelAnimations(model); if (!OwnAvatar) FadeWithDistance(model);
        var k = VisibleHeight / StandardHeight;
        var offset = Vec(point.Offset) + Vec(data.Position);
        var follower = new Follower { Item = holder, Offset = offset, ItemRot = EulerDegrees(data.Rotation), Scale = k, ScaleVec = Vec(data.Scale) * fit, RigRot = GlobalBasis.Orthonormalized() };
        string? problem = null;
        foreach (var bone in BoneFallbacks(point.Bone)) { problem = BindToBone(follower, bone); if (problem is null) break; }
        if (problem is not null) { holder.QueueFree(); _attachStatus[data.Id] = problem; return; }
        _attached[data.Id] = new Worn { Data = data, Holder = holder, Follower = follower }; _attachFit[data.Id] = fit; if (_soloAttachment is { } solo && solo != data.Id) holder.Visible = false;
        if (_previews.TryGetValue(data.Id, out var preview)) { follower.Offset = Vec(point.Offset) + preview.Position; follower.ItemRot = EulerDegrees([preview.Rotation.X, preview.Rotation.Y, preview.Rotation.Z]); follower.ScaleVec = preview.Scale * fit; }
        Follow(follower); _attached[data.Id].LastPosition = holder.GlobalPosition; _attachStatus[data.Id] = "ready";
    }
}

/// <summary>What the editor of placements needs from the body: where an object's point is and how it is turned, a live preview that is not saved, and finding an object under the cursor.</summary>
public partial class AvatarRig
{
    private readonly Dictionary<Guid, (Vector3 Position, Vector3 Rotation, Vector3 Scale)> _previews = [];
    private Guid? _soloAttachment;

    /// <summary>The axes of the attachment point in the world (the frame its position, rotation and scale are measured in) and the metres a unit of position stands for. Null when the object is not drawn.</summary>
    public (Basis Frame, float Metres)? AttachmentFrame(Guid id) => _attached.TryGetValue(id, out var w) && w.Follower is { } f && IsInstanceValid(w.Holder) ? (f.Frame, VisibleHeight / StandardHeight) : null;

    /// <summary>The place of an attachment in the world.</summary>
    public Vector3? AttachmentWorldPosition(Guid id) => AttachmentHolder(id)?.GlobalPosition;

    /// <summary>The real size (metres, longest side) of an attachment as drawn now.</summary>
    public float AttachmentSize(Guid id) { if (AttachmentHolder(id) is not { } h || h.GetChildOrNull<Node3D>(0) is not { } model || ModelCache.Bounds(model) is not { } b) return .2f; return b.Size.Length() * h.GlobalBasis.Scale.X; }

    /// <summary>Shows an attachment with another placement without saving anything (position in metres of the point's space, rotation in degrees, scale per axis). Null values go back to what is saved.</summary>
    public bool PreviewAttachment(Guid id, Vector3 position, Vector3 rotationDegrees, Vector3 scale)
    {
        if (!_attached.TryGetValue(id, out var w) || w.Follower is not { } f || !IsInstanceValid(w.Holder) || AttachmentPoints.Find(w.Data.Point) is not { } point) return false;
        f.Offset = Vec(point.Offset) + position; f.ItemRot = EulerDegrees([rotationDegrees.X, rotationDegrees.Y, rotationDegrees.Z]);
        f.ScaleVec = scale * (_attachFit.TryGetValue(id, out var fit) ? fit : 1f); Follow(f); return true;
    }

    private readonly Dictionary<Guid, float> _attachFit = [];

    /// <summary>Shows only one attachment (and hides every other one), or all of them again (null). Only the view changes.</summary>
    public void SoloAttachment(Guid? id)
    {
        _soloAttachment = id;
        foreach (var (key, w) in _attached) if (IsInstanceValid(w.Holder)) w.Holder.Visible = id is null || id == key;
    }

    /// <summary>The attachment under a ray (from the camera), nearest first; the grab is generous for small things like rings and earrings.</summary>
    public Guid? PickAttachment(Vector3 origin, Vector3 direction)
    {
        Guid? best = null; var bestAlong = float.MaxValue;
        foreach (var (id, w) in _attached)
        {
            if (!IsInstanceValid(w.Holder) || !w.Holder.Visible || w.Holder.GetChildOrNull<Node3D>(0) is not { } model || ModelCache.Bounds(model) is not { } box) continue;
            var inverse = w.Holder.GlobalTransform.AffineInverse(); var o = inverse * origin; var d = (inverse.Basis * direction).Normalized();
            var scale = Mathf.Max(w.Holder.GlobalBasis.Scale.X, .0001f); var pad = Mathf.Max(.0f, .05f / scale); // at least a few centimetres to grab
            var grown = box.Grow(pad);
            if (SlabHit(grown, o, d) is { } near) { var along = origin.DistanceTo(w.Holder.GlobalTransform * (o + d * near)); if (along < bestAlong) { bestAlong = along; best = id; } }
        }
        return best;
    }
}

public partial class AvatarRig
{
    /// <summary>Where along a ray (a unit direction) it first meets a box, or null when it misses.</summary>
    public static float? SlabHit(Aabb box, Vector3 origin, Vector3 direction)
    {
        var near = float.NegativeInfinity; var far = float.PositiveInfinity;
        for (var axis = 0; axis < 3; axis++)
        {
            var o = origin[axis]; var d = direction[axis]; var lo = box.Position[axis]; var hi = lo + box.Size[axis];
            if (Mathf.Abs(d) < 1e-8f) { if (o < lo || o > hi) return null; continue; }
            var t1 = (lo - o) / d; var t2 = (hi - o) / d; if (t1 > t2) (t1, t2) = (t2, t1);
            near = Mathf.Max(near, t1); far = Mathf.Min(far, t2); if (near > far) return null;
        }
        return far < 0 ? null : Mathf.Max(near, 0);
    }
}

public partial class AvatarRig
{
    /// <summary>The live preview of another player dragging one of their objects: shown at once, kept until their look is applied again (the saved one, or the saved one coming back after a cancel).</summary>
    public void PreviewRemoteAttachment(Guid id, Vector3 position, Vector3 rotationDegrees, Vector3 scale)
    {
        if (AttachmentPoints.CheckTransform(AttachmentPoints.DefaultLimits, [position.X, position.Y, position.Z], [rotationDegrees.X, rotationDegrees.Y, rotationDegrees.Z], [scale.X, scale.Y, scale.Z]) is not null) return; // never trust the wire
        _previews[id] = (position, rotationDegrees, scale); PreviewAttachment(id, position, rotationDegrees, scale);
    }
}

/// <summary>What keeps other people's objects from costing the island its frame rate, objects that move on their own, and a light sway for tails and the like.</summary>
public partial class AvatarRig
{
    /// <summary>Triangles an avatar may carry in worn objects: more for our own (we look at it), less for everybody else.</summary>
    public const int OwnTriangleBudget = 200_000, OthersTriangleBudget = 40_000, LightsPerAvatar = 2, ParticleSystemsPerAvatar = 2;
    /// <summary>Metres beyond which the objects of other people are not drawn.</summary>
    public const float OthersRange = 45f;

    /// <summary>True for the avatar of the person playing (it has the bigger budget).</summary>
    public bool OwnAvatar { get; set; }
    private int _attachTriangles, _attachLights, _attachParticles;
    public int AttachmentTriangles => _attachTriangles;
    public int? TriangleBudgetForTesting { get; set; }

    /// <summary>The triangles of a model.</summary>
    public static int Triangles(Node node)
    {
        var total = node is MeshInstance3D { Mesh: not null } mesh ? mesh.Mesh.GetSurfaceCount() == 0 ? 0 : mesh.Mesh.GetFaces().Length / 3 : 0;
        foreach (var child in node.GetChildren()) total += Triangles(child);
        return total;
    }

    private string? BudgetProblem(Node3D model)
    {
        var triangles = Triangles(model); var budget = TriangleBudgetForTesting ?? (OwnAvatar ? OwnTriangleBudget : OthersTriangleBudget);
        if (_attachTriangles + triangles > budget) return $"Acima do limite de desempenho: {triangles:N0} triângulos (o corpo já leva {_attachTriangles:N0} de {budget:N0}).";
        _attachTriangles += triangles; return null;
    }

    /// <summary>Keeps the lights and particle systems of an object within what one avatar may have; the rest are removed from the copy on screen.</summary>
    private void LimitEffects(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Light3D) { if (_attachLights >= LightsPerAvatar) { child.QueueFree(); continue; } _attachLights++; }
            else if (child is GpuParticles3D or CpuParticles3D) { if (_attachParticles >= ParticleSystemsPerAvatar) { child.QueueFree(); continue; } _attachParticles++; }
            LimitEffects(child);
        }
    }

    private static void FadeWithDistance(Node node)
    {
        if (node is GeometryInstance3D g) { g.VisibilityRangeEnd = OthersRange; g.VisibilityRangeEndMargin = 5; g.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self; }
        foreach (var child in node.GetChildren()) FadeWithDistance(child);
    }

    /// <summary>Plays the animation inside a model (a flapping wing, a spinning ring): the first one that is not the rest pose, looping. Objects without animations are left alone.</summary>
    public static void StartModelAnimations(Node model)
    {
        foreach (var child in model.GetChildren())
        {
            if (child is AnimationPlayer player)
            {
                var name = player.GetAnimationList().FirstOrDefault(n => n != "RESET");
                if (name is not null && player.GetAnimation(name) is { } animation) { animation.LoopMode = Animation.LoopModeEnum.Linear; player.Play(name); }
            }
            StartModelAnimations(child);
        }
    }

    private readonly Dictionary<Guid, float> _swayOverride = [];
    /// <summary>The sway of an object while it is being edited (null: what is saved).</summary>
    public void SetAttachmentSway(Guid id, float? sway) { if (sway is { } s) _swayOverride[id] = s; else _swayOverride.Remove(id); }
    /// <summary>How much of the sway shows at the moment (tests).</summary>
    public float AttachmentSwayAngle(Guid id) => _attached.TryGetValue(id, out var w) ? w.Tilt.Length() : 0;

    private void Sway(Worn worn, float delta)
    {
        var amount = _swayOverride.TryGetValue(worn.Data.Id, out var edited) ? edited : worn.Data.Sway;
        if (!IsInstanceValid(worn.Holder) || worn.Holder.GetChildOrNull<Node3D>(0) is not { } model) return;
        var position = worn.Holder.GlobalPosition; var velocity = (position - worn.LastPosition) / delta; worn.LastPosition = position;
        if (amount <= 0.001f) { if (worn.Tilt != Vector3.Zero) { worn.Tilt = Vector3.Zero; model.Basis = Basis.Identity.Scaled(model.Basis.Scale); } return; }
        if (velocity.Length() > 12f) velocity = velocity.Normalized() * 12f; // a teleport is not a swing
        var local = worn.Holder.GlobalBasis.Orthonormalized().Inverse() * velocity;
        var target = new Vector3(Mathf.Clamp(local.Z * .12f * amount, -.5f, .5f), 0, Mathf.Clamp(-local.X * .12f * amount + local.Y * .05f * amount, -.5f, .5f));
        worn.Tilt = worn.Tilt.Lerp(target, 1f - Mathf.Exp(-9f * delta));
        model.Basis = Basis.FromEuler(worn.Tilt).Scaled(model.Basis.Scale);
    }
}
