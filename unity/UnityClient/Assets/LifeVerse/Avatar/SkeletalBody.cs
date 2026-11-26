using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Networking;
using UnityEngine;

namespace LifeVerse.Unity.Avatar
{
    /// <summary>
    /// A person's own body: a GLB with the standard humanoid skeleton, loaded with glTFast under the avatar, brought to 1.8 m and set on the ground. The game's clips
    /// (<see cref="DefaultClips"/>, the shared <see cref="SkeletonClip"/>) are sampled every frame: for each standard bone a rotation "how far it has turned from rest, in the space of
    /// the model" that is put on the bone whatever the model's own proportions and bone axes are, and moved over to Unity (glTFast mirrors x; the content is turned half a turn about
    /// the vertical to meet the game space) before use. States without a clip (sitting, waving, dancing, gestures) and the time before the clips are read use the procedural pose of
    /// the simple body, laid on the skeleton (arms are brought down from the T-pose). Switching clips crosses over for a fifth of a second. Not yet: custom items on the body, the
    /// animations of the person's own files, finger bones and the look of the eyes.
    /// </summary>
    public sealed class SkeletalBody : MonoBehaviour
    {
        private sealed class Bone { public string Name; public Transform T; public Bone Parent; public Quaternion Rest; public int Depth; public Vector3 RestLocal; }

        public const float Height = 1.8f, BlendSeconds = .2f;
        private readonly List<Bone> _bones = new List<Bone>(); private readonly Dictionary<string, Bone> _byName = new Dictionary<string, Bone>();
        private Transform _content; private Renderer[] _renderers = Array.Empty<Renderer>(); private float _unit = 1f;
        private SkeletonClip _current, _previous; private float _time, _previousTime, _blend = 1f;
        private readonly HandFingers _fingers = new HandFingers();
        /// <summary>The fingers of the body, when its hands have finger bones.</summary>
        public HandFingers Fingers => _fingers;
        /// <summary>Whether the hand of a side holds something (an object worn at its point), so the fingers close on it. Set by the avatar.</summary>
        public Func<char, bool> HoldsHand;
        public int BoneCount => _bones.Count;
        public bool HasBone(string standard) => _byName.ContainsKey(standard);
        public Transform BoneOf(string standard) => _byName.TryGetValue(standard, out var bone) ? bone.T : null;
        public string ClipName => _current?.Name ?? "";
        public bool UsingClip => _current != null;
        public float Scale => _unit;
        public Bounds RenderBounds { get; private set; }
        /// <summary>The loader's own animation clip (glTFast legacy clips), kept only so tests can compare the two ways of moving the bones.</summary>
        public AnimationClip[] LoaderClips { get; private set; }
        public Transform Content => _content;
        /// <summary>The person's own animations for some states (Idle, Walk…), in place of the game's (the platform's "animation override").</summary>
        public readonly Dictionary<string, SkeletonClip> Overrides = new Dictionary<string, SkeletonClip>();

        /// <summary>Loads a binary glTF as the body under <paramref name="parent"/>. Null when it cannot be read or has no humanoid skeleton.</summary>
        public static async Task<SkeletalBody> CreateAsync(Transform parent, byte[] glb, bool keepLoaderClips = false)
        {
            var gltf = new GLTFast.GltfImport();
            if (!await gltf.LoadGltfBinary(glb, new Uri("file:///body.glb"))) return null;
            return await CreateAsync(parent, gltf, keepLoaderClips);
        }

        /// <summary>Makes a body from a model that was already read (so many people can share one import).</summary>
        public static async Task<SkeletalBody> CreateAsync(Transform parent, GLTFast.GltfImport gltf, bool keepLoaderClips = false)
        {
            if (gltf == null) return null;
            var holder = new GameObject("Skeletal body"); holder.transform.SetParent(parent, false);
            var body = holder.AddComponent<SkeletalBody>();
            // a glTF is right-handed and glTFast mirrors x; the game space flips z instead: half a turn about the vertical gives exactly that (as for imported objects)
            var content = new GameObject("Content"); content.transform.SetParent(holder.transform, false); content.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            if (!await gltf.InstantiateMainSceneAsync(content.transform)) { Destroy(holder); return null; }
            if (keepLoaderClips) body.LoaderClips = gltf.GetAnimationClips();
            if (!body.Setup(content.transform)) { Destroy(holder); return null; }
            return body;
        }

        private bool Setup(Transform content)
        {
            _content = content; _renderers = content.GetComponentsInChildren<Renderer>(true);
            if (_renderers.Length == 0) return false;
            // find the standard bones by name and remember how they rest
            foreach (var transform in content.GetComponentsInChildren<Transform>(true))
            {
                var standard = BoneMapper.Map(transform.name); if (standard == null || _byName.ContainsKey(standard)) continue;
                var bone = new Bone { Name = standard, T = transform, Rest = Quaternion.Inverse(content.rotation) * transform.rotation, RestLocal = transform.localPosition };
                _bones.Add(bone); _byName[standard] = bone;
            }
            foreach (var bone in _bones)
            {
                var up = bone.T.parent; while (up != null && up != content) { foreach (var other in _bones) if (other.T == up) { bone.Parent = other; break; } if (bone.Parent != null) break; up = up.parent; }
                var depth = 0; for (var p = bone.T; p != null && p != content; p = p.parent) depth++; bone.Depth = depth;
            }
            _bones.Sort((a, b) => a.Depth.CompareTo(b.Depth));
            foreach (var required in new[] { "hips", "head", "upper_arm_l", "upper_arm_r", "upper_leg_l", "upper_leg_r" }) if (!_byName.ContainsKey(required)) return false;
            // 1.8 m tall, feet on the ground, centred: measured at rest, before any pose
            var bounds = _renderers[0].bounds; foreach (var renderer in _renderers) bounds.Encapsulate(renderer.bounds);
            _unit = bounds.size.y > 1e-4f ? Height / bounds.size.y : 1f;
            transform.localScale = Vector3.one * _unit;
            bounds = _renderers[0].bounds; foreach (var renderer in _renderers) bounds.Encapsulate(renderer.bounds);
            _content.position += transform.position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);   // feet on the origin, centred
            foreach (var bone in _bones) bone.RestLocal = bone.T.localPosition;
            _fingers.Prepare(content.GetComponentsInChildren<Transform>(true), side => _byName.TryGetValue("hand_" + side, out var hand) ? hand.T : null);
            bounds = _renderers[0].bounds; foreach (var renderer in _renderers) bounds.Encapsulate(renderer.bounds); RenderBounds = bounds;
            foreach (var renderer in _renderers) { if (renderer is SkinnedMeshRenderer skinned) { skinned.updateWhenOffscreen = true; if (skinned.sharedMesh != null) for (var sub = 0; sub < skinned.sharedMesh.subMeshCount; sub++) Triangles += (int)(skinned.sharedMesh.GetIndexCount(sub) / 3); } else if (renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null) for (var sub = 0; sub < filter.sharedMesh.subMeshCount; sub++) Triangles += (int)(filter.sharedMesh.GetIndexCount(sub) / 3); }
            return true;
        }

        /// <summary>Shows or hides every part of the body.</summary>
        public void SetShown(bool shown) { foreach (var renderer in _renderers) if (renderer != null) renderer.enabled = shown; }

        /// <summary>The custom body keeps the person's own materials; the tints of the simple body do not apply to it.</summary>
        public void Tint(AvatarAppearance appearance) { }

        /// <summary>Triangles of the body (what a scene budgets).</summary>
        public int Triangles { get; private set; }

        /// <summary>Gives this person their own copy of the materials and tints the skin ones (names with "skin", "body", "face" or "head") to a colour.</summary>
        public int TintSkin(Color skin)
        {
            var tinted = 0;
            foreach (var renderer in _renderers)
            {
                var materials = renderer.materials;   // copies: other people sharing the import keep their own look
                foreach (var material in materials)
                {
                    var name = material.name.ToLowerInvariant(); if (!(name.Contains("skin") || name.Contains("body") || name.Contains("face") || name.Contains("head"))) continue;
                    foreach (var property in new[] { "baseColorFactor", "_BaseColor", "_Color" }) if (material.HasProperty(property)) { material.SetColor(property, skin); tinted++; break; }
                }
                renderer.materials = materials;
            }
            return tinted;
        }

        // ---- moving the bones ----

        private static Quaternion ToUnity(System.Numerics.Quaternion q) => new Quaternion(q.X, -q.Y, -q.Z, q.W);   // glTFast mirrors x, which keeps x and w and flips the other two

        /// <summary>One frame: choose the clip for what the person is doing, cross over when it changed, and put the rotations on the bones.</summary>
        public void Tick(float delta, NetworkMotionState motion, AvatarView.Gesture gesture, float speed, float phase, PoseValues pose, float headPitch, bool fullClips)
        {
            if (_content == null) return;
            var state = DefaultClips.StateOf(motion, speed, gesture != AvatarView.Gesture.None); var wanted = state != null && Overrides.TryGetValue(state, out var own) && own != null ? own : DefaultClips.For(state);   // the person's own clip for this state, else the game's
            if (wanted != _current) { _previous = _current; _previousTime = _time; _current = wanted; _time = 0f; _blend = _previous == null && _current == null ? 1f : 0f; }
            _time += delta; _previousTime += delta; _blend = Mathf.Min(1f, _blend + delta / BlendSeconds);
            Dictionary<string, Quaternion> deltas;
            Vector3 hipsOffset = Vector3.zero;
            if (_current != null)
            {
                var now = _current.SampleDeltas(_time); deltas = new Dictionary<string, Quaternion>(now.Count);
                Dictionary<string, System.Numerics.Quaternion> before = _blend < 1f && _previous != null ? _previous.SampleDeltas(_previousTime) : null;
                foreach (var pair in now)
                {
                    var q = ToUnity(pair.Value);
                    if (before != null && before.TryGetValue(pair.Key, out var b)) q = Quaternion.Slerp(ToUnity(b), q, Mathf.SmoothStep(0f, 1f, _blend));
                    deltas[pair.Key] = q;
                }
                var offset = _current.SampleHipsOffset(_time); hipsOffset = new Vector3(0f, offset.Y, 0f);
            }
            else { deltas = Procedural(pose, headPitch); hipsOffset = new Vector3(0f, -pose.Drop / Mathf.Max(_unit, 1e-4f), 0f); }
            Apply(deltas, hipsOffset); _gesture = gesture; _motion = motion; _fingers.Apply(side => HandShape.For(_motion, _gesture, side, HoldsHand != null && HoldsHand(side)), delta);
        }

        private AvatarView.Gesture _gesture; private NetworkMotionState _motion;

        /// <summary>The pose of the simple body as rotations in the space of the content: the arms are brought down from the T-pose first.</summary>
        private Dictionary<string, Quaternion> Procedural(PoseValues p, float headPitch)
        {
            Quaternion X(float degrees) => Quaternion.AngleAxis(degrees, Vector3.right); Quaternion Z(float degrees) => Quaternion.AngleAxis(degrees, Vector3.forward);
            var local = new Dictionary<string, Quaternion>
            {
                ["hips"] = X(p.Pitch), ["head"] = X(-headPitch * Mathf.Rad2Deg),
                ["upper_arm_l"] = X(p.ArmL) * Z(p.ArmSideL) * Z(82f), ["upper_arm_r"] = X(p.ArmR) * Z(p.ArmSideR) * Z(-82f),
                ["upper_leg_l"] = X(p.LegL), ["upper_leg_r"] = X(p.LegR),
            };
            // global = parent's global * own local; the bones with no pose of their own carry their parent's
            var global = new Dictionary<string, Quaternion>(); var result = new Dictionary<string, Quaternion>();
            foreach (var bone in _bones)
            {
                var parent = bone.Parent != null && global.TryGetValue(bone.Parent.Name, out var g) ? g : Quaternion.identity;
                var own = local.TryGetValue(bone.Name, out var l) ? l : Quaternion.identity; var total = parent * own; global[bone.Name] = total; result[bone.Name] = total;
            }
            return result;
        }

        private void Apply(Dictionary<string, Quaternion> deltas, Vector3 hipsOffset)
        {
            var frame = _content.rotation;
            foreach (var bone in _bones)
                if (deltas.TryGetValue(bone.Name, out var delta)) bone.T.rotation = frame * (delta * bone.Rest);
            if (_byName.TryGetValue("hips", out var hips) && hips.T.parent != null)
                hips.T.localPosition = hips.RestLocal + hips.T.parent.InverseTransformVector(_content.up * hipsOffset.y * _unit);
        }

        /// <summary>Puts the pose of a clip at a time on the bones now (tests compare it with the loader's own playback).</summary>
        public void ApplyClipAt(SkeletonClip clip, float time)
        {
            var now = clip.SampleDeltas(time); var deltas = new Dictionary<string, Quaternion>(now.Count); foreach (var pair in now) deltas[pair.Key] = ToUnity(pair.Value);
            Apply(deltas, Vector3.zero);
        }

        /// <summary>The rotation of a standard bone in the space of the content (tests).</summary>
        public Quaternion ContentRotationOf(string standard) => Quaternion.Inverse(_content.rotation) * _byName[standard].T.rotation;
    }
}
