using Godot;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The body sliders of the editor (Altura, Ombros, Tronco, Braços, Pernas, Cabeça) on a custom body. The procedural body scales its parts; a model has
/// one skin, so here the same sliders scale the matching bones (along the limb for arms and legs, across for the torso, evenly for the head) and the
/// whole model (height, width). The feet are put back on the ground when the legs change length. "Quadril" has no equivalent on a skinned model and is not applied.
/// </summary>
public partial class AvatarRig
{
    private IReadOnlyDictionary<string, float>? _bodyShape;
    private float _customLow, _legExtra;

    /// <summary>Tests: the length added to the legs (model units) and the scale the custom body ended up with.</summary>
    public float LegExtraForTesting => _legExtra;

    private float Shape(string key) => _bodyShape is not null && _bodyShape.TryGetValue(key, out var value) ? value : 1f;

    /// <summary>The axis of a bone (0 x, 1 y, 2 z) that points to its child: the one the child's rest position lies along.</summary>
    private static int AlongAxis(Skeleton3D skeleton, int child)
    {
        var o = skeleton.GetBoneRest(child).Origin; var a = o.Abs();
        return a.X >= a.Y && a.X >= a.Z ? 0 : a.Y >= a.Z ? 1 : 2;
    }

    private static Vector3 AxisScale(int axis, float along, float across) => new(axis == 0 ? along : across, axis == 1 ? along : across, axis == 2 ? along : across);

    private void ScaleLimb(Skeleton3D skeleton, string bone, string child, float along, float across)
    {
        if (!_standardBones.TryGetValue(bone, out var index) || !_standardBones.TryGetValue(child, out var childIndex)) return;
        skeleton.SetBonePoseScale(index, AxisScale(AlongAxis(skeleton, childIndex), along, across));
    }

    /// <summary>Applies the body sliders to the custom body (called when it is attached).</summary>
    private void ApplyCustomShape()
    {
        if (_customBody is null || !IsInstanceValid(_customBody) || _customSkeleton is null) return;
        var skeleton = _customSkeleton;
        float legs = Shape("legs"), arms = Shape("arms"), torso = Shape("torso"), head = Shape("head"), height = Shape("height"), shoulder = Shape("shoulder");
        // how much the legs grow, in model units, so the feet can be put back on the ground
        _legExtra = 0;
        if (_standardBones.TryGetValue("upper_leg_l", out var upper) && _standardBones.TryGetValue("foot_l", out var foot))
        {
            var toHolder = _customBody.GlobalTransform.AffineInverse() * skeleton.GlobalTransform;
            _legExtra = (legs - 1f) * ((toHolder * skeleton.GetBoneGlobalRest(upper).Origin).Y - (toHolder * skeleton.GetBoneGlobalRest(foot).Origin).Y);
        }
        foreach (var side in new[] { "l", "r" })
        {
            ScaleLimb(skeleton, $"upper_leg_{side}", $"lower_leg_{side}", legs, 1f);
            ScaleLimb(skeleton, $"upper_arm_{side}", $"lower_arm_{side}", arms, 1f);
        }
        var above = _standardBones.ContainsKey("chest") ? "chest" : _standardBones.ContainsKey("neck") ? "neck" : "";
        if (above.Length > 0) ScaleLimb(skeleton, "spine", above, 1f, torso);
        if (_standardBones.TryGetValue("head", out var headBone)) skeleton.SetBonePoseScale(headBone, Vector3.One * head);
        var size = _customScale;
        _customBody.Scale = new Vector3(size * shoulder, size * height, size * shoulder);
        _customBody.Position = new Vector3(0, (-_customLow + _legExtra) * size * height, 0);
    }
}
