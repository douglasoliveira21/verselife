using System.Globalization;

namespace LifeVerse.Shared.Avatar;

/// <summary>
/// One place on the body (or on the screen) where an object can be attached. The <see cref="Key"/> and the <see cref="Id"/> never change once published: they are what is stored,
/// so an update that renames a point or moves its bone cannot make anybody lose an attachment.
/// </summary>
/// <param name="Id">Stable number (the Second Life numbers where the point exists there; ours start at 100).</param>
/// <param name="Key">Stable internal key, stored in the database and sent on the wire.</param>
/// <param name="Name">The name shown to people, in Portuguese.</param>
/// <param name="Region">head, torso, arms, legs, special or hud.</param>
/// <param name="Bone">The standard bone (see BoneMapper) the point follows; empty for a HUD point, which is anchored to the screen.</param>
/// <param name="Profile">The skeleton profile an avatar needs for the point to exist ("humanoid" is every avatar's).</param>
/// <param name="Offset">Where the point sits, in meters, in the bone's own space: the default the object is put at.</param>
/// <param name="SecondLife">The name the point has in Second Life, when it has one.</param>
public sealed record AttachmentPoint(int Id, string Key, string Name, string Region, string Bone, string Profile, float[] Offset, string? SecondLife = null)
{
    public bool IsHud => Region == AttachmentPoints.Hud;
}

/// <summary>The catalog of attachment points, the profiles that unlock the optional ones, the Second Life names and the rules a transform must follow.</summary>
public static class AttachmentPoints
{
    public const string Head = "head", Torso = "torso", Arms = "arms", Legs = "legs", Special = "special", Hud = "hud";
    public const string Humanoid = "humanoid", Fingers = "fingers", Tail = "tail", Wings = "wings";
    /// <summary>Goes up when a point changes where it sits on the skeleton; rows keep the version they were saved with so they can be migrated.</summary>
    public const int SkeletonProfileVersion = 1;

    public static readonly string[] Regions = [Head, Torso, Arms, Legs, Special, Hud];
    public static readonly string[] Profiles = [Humanoid, Fingers, Tail, Wings];

    private static AttachmentPoint P(int id, string key, string name, string region, string bone, float x, float y, float z, string? sl = null, string profile = Humanoid) => new(id, key, name, region, bone, profile, [x, y, z], sl);

    public static readonly IReadOnlyList<AttachmentPoint> All =
    [
        // ---- cabeça e rosto
        P(2, "skull", "Crânio", Head, "head", 0, .10f, 0, "Skull"),
        P(101, "head_top", "Topo da cabeça", Head, "head", 0, .17f, 0),
        P(102, "head_center", "Centro da cabeça", Head, "head", 0, .09f, 0),
        P(103, "face", "Rosto", Head, "head", 0, .08f, .10f),
        P(104, "forehead", "Testa", Head, "head", 0, .14f, .09f),
        P(15, "eye_l", "Olho esquerdo", Head, "head", .032f, .10f, .09f, "Left Eye"),
        P(16, "eye_r", "Olho direito", Head, "head", -.032f, .10f, .09f, "Right Eye"),
        P(13, "ear_l", "Orelha esquerda", Head, "head", .085f, .08f, 0, "Left Ear"),
        P(14, "ear_r", "Orelha direita", Head, "head", -.085f, .08f, 0, "Right Ear"),
        P(17, "nose", "Nariz", Head, "head", 0, .07f, .11f, "Nose"),
        P(11, "mouth", "Boca", Head, "head", 0, .035f, .10f, "Mouth"),
        P(12, "chin", "Queixo", Head, "head", 0, .0f, .09f, "Chin"),
        // ---- tronco
        P(39, "neck", "Pescoço", Torso, "neck", 0, .05f, 0, "Neck"),
        P(1, "chest", "Peito", Torso, "chest", 0, .05f, .09f, "Chest"),
        P(29, "pec_l", "Peitoral esquerdo", Torso, "chest", .09f, .06f, .09f, "Left Pec"),
        P(30, "pec_r", "Peitoral direito", Torso, "chest", -.09f, .06f, .09f, "Right Pec"),
        P(105, "spine_upper", "Coluna superior", Torso, "chest", 0, .10f, -.04f),
        P(106, "spine_lower", "Coluna inferior", Torso, "spine", 0, .05f, -.04f),
        P(9, "back", "Costas", Torso, "chest", 0, .02f, -.10f, "Spine"),
        P(3, "shoulder_l", "Ombro esquerdo", Torso, "upper_arm_l", .0f, .0f, 0, "Left Shoulder"),
        P(4, "shoulder_r", "Ombro direito", Torso, "upper_arm_r", .0f, .0f, 0, "Right Shoulder"),
        P(28, "abdomen", "Abdômen", Torso, "spine", 0, .05f, .09f, "Stomach"),
        P(10, "pelvis", "Pelve", Torso, "hips", 0, 0, 0, "Pelvis"),
        // ---- braços e mãos
        P(20, "upper_arm_l", "Braço superior esquerdo", Arms, "upper_arm_l", .12f, 0, 0, "Left Upper Arm"),
        P(18, "upper_arm_r", "Braço superior direito", Arms, "upper_arm_r", -.12f, 0, 0, "Right Upper Arm"),
        P(21, "lower_arm_l", "Antebraço esquerdo", Arms, "lower_arm_l", .12f, 0, 0, "Left Lower Arm"),
        P(19, "lower_arm_r", "Antebraço direito", Arms, "lower_arm_r", -.12f, 0, 0, "Right Lower Arm"),
        P(107, "elbow_l", "Cotovelo esquerdo", Arms, "lower_arm_l", 0, 0, 0),
        P(108, "elbow_r", "Cotovelo direito", Arms, "lower_arm_r", 0, 0, 0),
        P(109, "wrist_l", "Pulso esquerdo", Arms, "hand_l", 0, 0, 0),
        P(110, "wrist_r", "Pulso direito", Arms, "hand_r", 0, 0, 0),
        P(5, "hand_l", "Mão esquerda", Arms, "hand_l", .05f, 0, 0, "Left Hand"),
        P(6, "hand_r", "Mão direita", Arms, "hand_r", -.05f, 0, 0, "Right Hand"),
        // ---- pernas e pés
        P(26, "thigh_l", "Coxa esquerda", Legs, "upper_leg_l", 0, -.20f, 0, "Left Upper Leg"),
        P(23, "thigh_r", "Coxa direita", Legs, "upper_leg_r", 0, -.20f, 0, "Right Upper Leg"),
        P(111, "knee_l", "Joelho esquerdo", Legs, "lower_leg_l", 0, 0, .03f),
        P(112, "knee_r", "Joelho direito", Legs, "lower_leg_r", 0, 0, .03f),
        P(27, "calf_l", "Panturrilha esquerda", Legs, "lower_leg_l", 0, -.18f, 0, "Left Lower Leg"),
        P(24, "calf_r", "Panturrilha direita", Legs, "lower_leg_r", 0, -.18f, 0, "Right Lower Leg"),
        P(113, "ankle_l", "Tornozelo esquerdo", Legs, "foot_l", 0, 0, 0),
        P(114, "ankle_r", "Tornozelo direito", Legs, "foot_r", 0, 0, 0),
        P(7, "foot_l", "Pé esquerdo", Legs, "foot_l", 0, -.04f, .06f, "Left Foot"),
        P(8, "foot_r", "Pé direito", Legs, "foot_r", 0, -.04f, .06f, "Right Foot"),
        // ---- pontos especiais
        P(40, "avatar_center", "Centro do avatar", Special, "hips", 0, .10f, 0, "Avatar Center"),
        P(115, "root", "Root do avatar", Special, "root", 0, 0, 0),
        P(116, "pelvis_front", "Pelve frontal", Special, "hips", 0, 0, .12f),
        P(117, "pelvis_back", "Pelve traseira", Special, "hips", 0, 0, -.12f),
        P(118, "vfx", "Ponto de efeito visual", Special, "hips", 0, .90f, 0),
        P(119, "tail", "Cauda", Special, "hips", 0, -.05f, -.12f, null, Tail),
        P(120, "wing_l", "Asa esquerda", Special, "chest", .08f, .05f, -.10f, null, Wings),
        P(121, "wing_r", "Asa direita", Special, "chest", -.08f, .05f, -.10f, null, Wings),
        // ---- HUD (ancorados na tela, sem osso)
        P(35, "hud_center", "HUD · Centro", Hud, "", 0, 0, 0, "HUD Center 1"),
        P(31, "hud_center2", "HUD · Centro 2", Hud, "", 0, 0, 0, "HUD Center 2"),
        P(34, "hud_top_left", "HUD · Superior esquerdo", Hud, "", 0, 0, 0, "HUD Top Left"),
        P(33, "hud_top", "HUD · Superior", Hud, "", 0, 0, 0, "HUD Top"),
        P(32, "hud_top_right", "HUD · Superior direito", Hud, "", 0, 0, 0, "HUD Top Right"),
        P(36, "hud_bottom_left", "HUD · Inferior esquerdo", Hud, "", 0, 0, 0, "HUD Bottom Left"),
        P(37, "hud_bottom", "HUD · Inferior", Hud, "", 0, 0, 0, "HUD Bottom"),
        P(38, "hud_bottom_right", "HUD · Inferior direito", Hud, "", 0, 0, 0, "HUD Bottom Right"),
    ];

    /// <summary>The finger points: only on avatars whose skeleton has the finger bones (the "fingers" profile). They follow the hand until the client finds a finger bone.</summary>
    public static readonly IReadOnlyList<AttachmentPoint> FingerPoints = BuildFingers();

    private static List<AttachmentPoint> BuildFingers()
    {
        var list = new List<AttachmentPoint>(); var id = 130;
        foreach (var (side, sideName, bone) in new[] { ("l", "esquerda", "hand_l"), ("r", "direita", "hand_r") })
            foreach (var (finger, fingerName) in new[] { ("thumb", "Polegar"), ("index", "Indicador"), ("middle", "Médio"), ("ring", "Anelar"), ("pinky", "Mindinho") })
                list.Add(P(id++, $"finger_{finger}_{side}", $"{fingerName} {sideName}", Arms, bone, 0, 0, 0, null, Fingers));
        return list;
    }

    private static readonly Dictionary<string, AttachmentPoint> ByKey = All.Concat(FingerPoints).ToDictionary(p => p.Key);
    private static readonly Dictionary<int, AttachmentPoint> ById = All.Concat(FingerPoints).ToDictionary(p => p.Id);
    private static readonly Dictionary<string, AttachmentPoint> BySecondLife = All.Where(p => p.SecondLife is not null).ToDictionary(p => Normalize(p.SecondLife!));

    /// <summary>Every point, including the optional ones.</summary>
    public static IEnumerable<AttachmentPoint> Everything => ByKey.Values;

    public static AttachmentPoint? Find(string? key) => key is not null && ByKey.TryGetValue(key.Trim().ToLowerInvariant(), out var p) ? p : null;
    public static AttachmentPoint? FindById(int id) => ById.GetValueOrDefault(id);

    /// <summary>The point a Second Life name ("Left Hand", "HUD Top Left", "Skull"…) or number stands for.</summary>
    public static AttachmentPoint? FromSecondLife(string? name) => !string.IsNullOrWhiteSpace(name) && BySecondLife.TryGetValue(Normalize(name), out var p) ? p : int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? All.FirstOrDefault(x => x.Id == n && x.SecondLife is not null) : null;

    private static string Normalize(string name) => name.Trim().Replace("_", " ").ToLowerInvariant();

    /// <summary>The points an avatar of these skeleton profiles has: the humanoid ones always, the others when the avatar's skeleton offers them.</summary>
    public static IEnumerable<AttachmentPoint> For(IEnumerable<string>? profiles)
    {
        var set = new HashSet<string>(profiles ?? [], StringComparer.OrdinalIgnoreCase) { Humanoid };
        return ByKey.Values.Where(p => set.Contains(p.Profile)).OrderBy(p => Array.IndexOf(Regions, p.Region)).ThenBy(p => p.Id);
    }

    /// <summary>The point when the avatar can have it, otherwise null.</summary>
    public static AttachmentPoint? Resolve(string? key, IEnumerable<string>? profiles) => Find(key) is { } p && (p.Profile == Humanoid || (profiles ?? []).Contains(p.Profile, StringComparer.OrdinalIgnoreCase)) ? p : null;

    /// <summary>
    /// The nearest point an avatar does have when the one asked for is missing from its skeleton (a tail on a body without a tail goes to the pelvis…): the way a new body takes over the attachments of an old one
    /// without touching the saved placement. Null when the point is fine as it is.
    /// </summary>
    public static AttachmentPoint? Fallback(AttachmentPoint point, IEnumerable<string>? profiles)
    {
        if (Resolve(point.Key, profiles) is not null) return null;
        return point.Profile switch
        {
            Tail => Find("pelvis_back"),
            Wings => Find("back"),
            Fingers => Find(point.Key.EndsWith("_l", StringComparison.Ordinal) ? "hand_l" : "hand_r"),
            _ => Find("avatar_center")
        };
    }

    // ---- the limits of a transform -------------------------------------------------------------------------------------------------------

    /// <summary>What the server allows: how many things a body wears, how many at a point, how far from the point an object may sit and how big or small it may be.</summary>
    public sealed record Limits(int MaxTotal = 38, int MaxPerPoint = 8, int MaxHud = 8, float MaxOffset = 5f, float MinScale = .01f, float MaxScale = 10f);

    public static readonly Limits DefaultLimits = new();

    /// <summary>Null when the placement is acceptable, otherwise the code of what is wrong.</summary>
    public static string? CheckTransform(Limits limits, float[]? position, float[]? rotation, float[]? scale)
    {
        if (position is not null && (position.Length != 3 || position.Any(v => !float.IsFinite(v) || Math.Abs(v) > limits.MaxOffset))) return "bad_position";
        if (rotation is not null && (rotation.Length != 3 || rotation.Any(v => !float.IsFinite(v) || Math.Abs(v) > 36000f))) return "bad_rotation";
        if (scale is not null && (scale.Length != 3 || scale.Any(v => !float.IsFinite(v) || v < limits.MinScale || v > limits.MaxScale))) return "bad_scale";
        return null;
    }
}
