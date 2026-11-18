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

    /// <summary>Triangles of the custom body at full detail (0 for the procedural body; the planner counts that as its own minimum).</summary>
    public int BodyCost => _lastData is { CustomModelId: not null } data ? Math.Max(0, data.CustomModelTriangles) : 0;

    /// <summary>Triangles of this avatar when drawn in full: the custom body and every custom item.</summary>
    public int FullCost => BodyCost + (_lastData?.CustomWearables.Sum(w => Math.Max(0, w.Triangles)) ?? 0);

    /// <summary>Triangles when drawn reduced: the custom body with its geometry simplified (see <see cref="ModelLod"/>), no items.</summary>
    public int ReducedCost => AvatarDetailPlanner.ReducedTriangles(BodyCost);

    /// <summary>Sets how much of the avatar is drawn. Hidden stops drawing and animating without throwing anything away; the other levels rebuild only when what they need changed.</summary>
    public void SetDetail(AvatarDetail detail)
    {
        if (detail == _detail) return;
        var before = _detail; _detail = detail;
        var visible = detail != AvatarDetail.Hidden;
        Visible = visible; SetProcess(visible);
        if (_tree is not null && IsInstanceValid(_tree))
        {
            _tree.Active = visible;
            // turning the tree back on restarts it at "Idle": go to the state the avatar is in now, or it would stay still until the state changes
            if (visible && _playback is not null) _playback.Start(Effective(_state));
        }
        if (!visible || _lastData is null) return;
        var level = detail < AvatarDetail.Basic ? AvatarDetail.Basic : detail;
        if (level != _builtDetail || before == AvatarDetail.Hidden && level != _builtDetail) ApplyAppearance(_lastData);
    }

    /// <summary>The avatar is not rebuilt in a different way on every refresh: the level it was last built at.</summary>
    private void RememberBuild(AvatarAppearanceData data) { _lastData = data; _builtDetail = _detail < AvatarDetail.Basic ? AvatarDetail.Basic : _detail; }

    /// <summary>How often a Reduced avatar is animated: every frame (1), every other frame (2, the default) or every third (3). Configurações › Mundo e avatares › Qualidade das animações.</summary>
    public static int AnimationDivisor { get; set; } = 2;
    private bool Throttled => _detail == AvatarDetail.Reduced && AnimationDivisor > 1 && Engine.GetProcessFrames() % (ulong)AnimationDivisor != 0;
}
