using System.Text.Json.Nodes;
using Godot;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The "Recursos" of an object in the world: a reflection probe (the object mirrors its surroundings on shiny surfaces nearby) and the animation of a rigged mesh. Both come from the
/// material JSON the server stores, and both have a budget so a crowded island cannot overload anybody's graphics card.
/// </summary>
public partial class BuildObjectView
{
    /// <summary>Reflection probes render the surroundings: only the first few in the world are switched on.</summary>
    public partial class ObjectReflection : ReflectionProbe
    {
        public const int MaxProbes = 4;
        private static int _active;
        public override void _EnterTree() { _active++; Visible = _active <= MaxProbes; }
        public override void _ExitTree() { _active--; }
        public static int ActiveForTesting => _active;
    }

    /// <summary>Plays a clip of the model's own AnimationPlayer from the clock of the server, so everybody sees the same pose at the same time.</summary>
    public partial class ObjectAnimationDriver : Node
    {
        public AnimationPlayer? Player { get; set; }
        public Skeleton3D? Skeleton { get; set; }
        public string Clip { get; private set; } = string.Empty;
        public bool Playing { get; private set; }
        public double Position => Player is { } p && IsInstanceValid(p) ? p.CurrentAnimationPosition : 0;

        public IReadOnlyList<string> Clips => Player is null ? [] : [.. Player.GetAnimationList().Where(n => n != "RESET")];

        /// <summary>Starts, pauses or stops the clip as the stored animation says. A clip the model does not have is ignored.</summary>
        public void Apply(JsonObject? animation, double nowSeconds)
        {
            if (Player is null || !IsInstanceValid(Player)) return;
            var clip = animation?["clip"] is JsonValue c && c.TryGetValue<string>(out var name) ? name : string.Empty;
            if (animation is null || clip.Length == 0 || !Player.HasAnimation(clip)) { Player.Stop(); Clip = string.Empty; Playing = false; return; }
            double D(string key, double fallback) => animation[key] is JsonValue v && v.TryGetValue<double>(out var n) && double.IsFinite(n) ? n : fallback;
            var loop = animation["loop"] is JsonValue l && l.TryGetValue<bool>(out var looping) && looping; var playing = animation["playing"] is JsonValue p && p.TryGetValue<bool>(out var on) && on;
            var speed = Math.Clamp(D("speed", 1), .1, 4); var length = Player.GetAnimation(clip).Length;
            Player.GetAnimation(clip).LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
            var offset = D("offset", 0); var since = D("since", nowSeconds * 1000) / 1000.0;
            var at = playing ? offset + (nowSeconds - since) * speed : offset;
            at = loop && length > 0 ? ((at % length) + length) % length : Math.Clamp(at, 0, length);
            Clip = clip; Playing = playing;
            Player.SpeedScale = (float)speed; Player.Play(clip); Player.Seek(at, true);
            if (!playing) Player.Pause();
        }
    }

    private ObjectAnimationDriver? _animation;
    /// <summary>The imported model of this object has arrived (it may still have nothing to animate).</summary>
    public bool ModelLoaded { get; private set; }
    public ObjectAnimationDriver? AnimationDriver => _animation;
    /// <summary>The names of the clips of the imported model (empty while it loads or when it has none) and the bones of its skeleton.</summary>
    public IReadOnlyList<string> AnimationClips => _animation?.Clips ?? [];
    public int SkeletonBones => _animation?.Skeleton?.GetBoneCount() ?? 0;

    /// <summary>For tests: puts a model in the object as if it had been downloaded, with its light, reflection and animation.</summary>
    public void AttachModelForTesting(Node3D model) { AddChild(model); AddLightAndResources(ParseMaterial(State.MaterialJson), model); }

    private void AddResources(JsonObject material, Node? model)
    {
        if (material["reflection"] is JsonObject reflection)
        {
            float R(string key, float fallback) => reflection[key] is JsonValue v && v.TryGetValue<double>(out var n) && double.IsFinite(n) ? (float)n : fallback;
            var range = Mathf.Clamp(R("range", 8f), 2f, 30f);
            AddChild(new ObjectReflection { Name = "ObjectReflection", Size = new Vector3(range, range, range), Intensity = Mathf.Clamp(R("intensity", .6f), 0f, 1f), MaxDistance = range * 2f, UpdateMode = ReflectionProbe.UpdateModeEnum.Once, BoxProjection = false });
        }
        if (model is null) return;
        ModelLoaded = true;
        var player = model.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>().FirstOrDefault(); var skeleton = model.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().FirstOrDefault();
        if (player is null && skeleton is null) return;
        _animation = new ObjectAnimationDriver { Name = "Animation", Player = player, Skeleton = skeleton }; AddChild(_animation);
        _animation.Apply(material["animation"] as JsonObject, Time.GetUnixTimeFromSystem());
    }
}
