using Godot;
using LifeVerse.Client.Models;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// How much of this avatar is drawn and animated (see <see cref="AvatarDetailPlanner"/>). Nothing expensive is downloaded or built until the detail
/// asks for it, so a crowd far away costs a few procedural bodies, and coming closer brings in the custom body, then the custom items and animations.
/// The avatar of the player is always Full. Changing the detail rebuilds only what the new level needs.
/// </summary>
public partial class AvatarRig
{
    private AvatarDetail _detail = AvatarDetail.Full;
    private AvatarDetail _builtDetail = AvatarDetail.Full;
    private AvatarAppearanceData? _lastData;

    public AvatarDetail Detail => _detail;

    /// <summary>Triangles of this avatar when drawn in full: the custom body and every custom item (0 for the procedural body; the planner counts that as its own minimum).</summary>
    public int FullCost => ReducedCost + (_lastData?.CustomWearables.Sum(w => Math.Max(0, w.Triangles)) ?? 0);

    /// <summary>Triangles when drawn reduced: the custom body only.</summary>
    public int ReducedCost => _lastData is { CustomModelId: not null } data ? Math.Max(0, data.CustomModelTriangles) : 0;

    /// <summary>Sets how much of the avatar is drawn. Hidden stops drawing and animating without throwing anything away; the other levels rebuild only when what they need changed.</summary>
    public void SetDetail(AvatarDetail detail)
    {
        if (detail == _detail) return;
        var before = _detail; _detail = detail;
        var visible = detail != AvatarDetail.Hidden;
        Visible = visible; SetProcess(visible);
        if (_tree is not null && IsInstanceValid(_tree)) _tree.Active = visible;
        if (!visible || _lastData is null) return;
        var level = detail < AvatarDetail.Basic ? AvatarDetail.Basic : detail;
        if (level != _builtDetail || before == AvatarDetail.Hidden && level != _builtDetail) ApplyAppearance(_lastData);
    }

    /// <summary>The avatar is not rebuilt in a different way on every refresh: the level it was last built at.</summary>
    private void RememberBuild(AvatarAppearanceData data) { _lastData = data; _builtDetail = _detail < AvatarDetail.Basic ? AvatarDetail.Basic : _detail; }

    private bool Throttled => _detail == AvatarDetail.Reduced && (Engine.GetProcessFrames() & 1) == 1;
}
