using LifeVerse.Shared.Networking;
using LifeVerse.Unity.World;
using UnityEngine;

namespace LifeVerse.Unity.Avatar
{
    /// <summary>
    /// A person in the world, drawn as a simple procedural body (torso, head with a nose to show where they face, two arms and two legs hinged at the shoulders and hips).
    /// The origin is at the feet, as in the game. Positions and velocities arrive in game space and go through <see cref="GameSpace"/>; the body faces the way the server
    /// says (yaw = atan2(vx, -vz) in game space, which is the same angle as a Unity heading about the vertical). A remote body follows the snapshots smoothly (it is moved a
    /// little toward where the last snapshot says it is by now); the local player's body is placed directly by <see cref="LocalPlayer"/>. The limbs swing by motion state
    /// (walk, run, jump, fall, fly, swim, sit, wave, dance, typing). The shared animation clips and the custom GLB bodies come in later increments.
    /// </summary>
    public sealed class AvatarView : MonoBehaviour
    {
        public int PeerId; public string DisplayName = "";
        /// <summary>When true something else (the local player's prediction) places the body.</summary>
        public bool External;
        public NetworkMotionState Motion { get; private set; }
        public Vector3 GamePosition { get; private set; }
        public float Speed { get; private set; }
        public float Heading { get; private set; }   // degrees about the vertical, the Unity heading of the body

        /// <summary>The short gestures the network carries besides wave and dance (the numbers are those of <c>ExtraGestures.States</c>).</summary>
        public enum Gesture { None, Applaud, Point, Greet, Laugh, SitFloor }
        public Gesture ActiveGesture { get; private set; }
        private float _gestureLeft;
        private TextMesh _tag;
        public bool Visible { get; private set; } = true;
        public Transform NameTag => _tag != null ? _tag.transform : null;

        /// <summary>Plays one of the extra gestures for a few seconds (the island told everybody who can see this person).</summary>
        public void PlayGesture(int id, float seconds = 2.5f)
        {
            if (id < 0 || id >= System.Enum.GetValues(typeof(Gesture)).Length - 1) return;
            ActiveGesture = (Gesture)(id + 1); _gestureLeft = seconds;
        }

        public void StopGesture() { ActiveGesture = Gesture.None; _gestureLeft = 0f; }

        /// <summary>Shows or hides the body and the name (the local player's body is hidden in first person).</summary>
        public void SetVisible(bool visible)
        {
            Visible = visible; foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = visible;
        }

        private Transform _body, _head, _leftArm, _rightArm, _leftLeg, _rightLeg;
        private Vector3 _targetGame, _velocityGame; private float _targetYaw, _sinceSnapshot; private bool _placed;
        private float _phase, _swing, _bodyPitch, _sitDrop;

        public Transform LeftLeg => _leftLeg;
        public Transform RightArm => _rightArm;
        public Transform Torso => _body;
        public Vector3 TargetGame => _targetGame;

        public static AvatarView Create(int peerId, string name, Color color)
        {
            var root = new GameObject("Avatar " + name); var view = root.AddComponent<AvatarView>(); view.PeerId = peerId; view.DisplayName = name; view.Build(color); return view;
        }

        private static Material Surface(Color color)
        {
            var prototype = Resources.Load<Material>("LifeVerse/ObjectMaterial"); var material = new Material(prototype != null ? prototype : new Material(Shader.Find("HDRP/Lit")));
            material.SetColor("_BaseColor", color); material.SetFloat("_SmoothnessRemapMin", .3f); material.SetFloat("_SmoothnessRemapMax", .3f); material.SetFloat("_MetallicRemapMax", 0f); return material;
        }

        private static Transform Part(Transform parent, PrimitiveType shape, string name, Vector3 centre, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(shape); part.name = name; var collider = part.GetComponent<Collider>(); if (collider != null) { if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider); }
            part.transform.SetParent(parent, false); part.transform.localPosition = centre; part.transform.localScale = scale; part.GetComponent<MeshRenderer>().sharedMaterial = material; return part.transform;
        }

        private Transform Limb(Transform parent, string name, Vector3 joint, float length, float thickness, Material material)
        {
            var pivot = new GameObject(name).transform; pivot.SetParent(parent, false); pivot.localPosition = joint;
            Part(pivot, PrimitiveType.Capsule, name + " mesh", new Vector3(0, -length / 2f, 0), new Vector3(thickness, length / 2f, thickness), material); return pivot;
        }

        private void Build(Color color)
        {
            var skin = Surface(new Color(.86f, .68f, .55f)); var shirt = Surface(color); var trousers = Surface(color * .45f + new Color(.1f, .1f, .12f) * .5f);
            _body = new GameObject("Body").transform; _body.SetParent(transform, false);
            Part(_body, PrimitiveType.Cube, "Torso", new Vector3(0, 1.22f, 0), new Vector3(.42f, .56f, .22f), shirt);
            _head = Part(_body, PrimitiveType.Sphere, "Head", new Vector3(0, 1.67f, 0), new Vector3(.26f, .28f, .26f), skin);
            Part(_head, PrimitiveType.Cube, "Nose", new Vector3(0, -.05f, .5f), new Vector3(.12f, .14f, .25f), skin);
            _leftArm = Limb(_body, "LeftArm", new Vector3(-.29f, 1.46f, 0), .56f, .09f, shirt); _rightArm = Limb(_body, "RightArm", new Vector3(.29f, 1.46f, 0), .56f, .09f, shirt);
            _leftLeg = Limb(_body, "LeftLeg", new Vector3(-.11f, .94f, 0), .94f, .11f, trousers); _rightLeg = Limb(_body, "RightLeg", new Vector3(.11f, .94f, 0), .94f, .11f, trousers);
            var tag = new GameObject("NameTag"); tag.transform.SetParent(transform, false); tag.transform.localPosition = new Vector3(0f, 2.05f, 0f);
            _tag = tag.AddComponent<TextMesh>(); _tag.text = DisplayName; _tag.fontSize = 64; _tag.characterSize = .035f; _tag.anchor = TextAnchor.LowerCenter; _tag.alignment = TextAlignment.Center; _tag.color = Color.white;
            _tag.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); tag.GetComponent<MeshRenderer>().sharedMaterial = _tag.font.material; tag.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>The name turns to face the camera and hides far away; it never shows on one's own body.</summary>
        private void LateUpdate()
        {
            if (_tag == null) return;
            var camera = Camera.main; var show = Visible && !External && camera != null;
            if (show) { var away = camera.transform.position - _tag.transform.position; show = away.sqrMagnitude < 45f * 45f; if (show) _tag.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up); } // TextMesh is read from its back, so it looks at the camera, not away from it
            _tag.gameObject.SetActive(show);
        }

        /// <summary>A snapshot of the server: where this person is (game space), how fast, which way and what they are doing.</summary>
        public void ApplySnapshot(PlayerSnapshot snapshot)
        {
            _targetGame = new Vector3(snapshot.X, snapshot.Y, snapshot.Z); _velocityGame = new Vector3(snapshot.VelocityX, snapshot.VelocityY, snapshot.VelocityZ);
            _targetYaw = snapshot.Yaw * Mathf.Rad2Deg; _sinceSnapshot = 0f; Motion = snapshot.Motion;
            if (!_placed) { _placed = true; GamePosition = _targetGame; Heading = _targetYaw; ApplyTransform(); }
        }

        /// <summary>Where this person starts (the spawn message), before the first snapshot.</summary>
        public void PlaceAt(Vector3 game) { _targetGame = game; GamePosition = game; _placed = true; ApplyTransform(); }

        /// <summary>The local player places the body: position, heading (degrees), velocity and motion, all from the prediction.</summary>
        public void SetPose(Vector3 game, float headingDegrees, Vector3 velocityGame, NetworkMotionState motion)
        {
            GamePosition = game; _targetGame = game; Heading = headingDegrees; _velocityGame = velocityGame; Motion = motion; ApplyTransform();
        }

        private void ApplyTransform()
        {
            transform.position = GameSpace.ToUnity(GamePosition); transform.rotation = Quaternion.Euler(0f, Heading, 0f);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One frame of smoothing and posing (also called by tests with a chosen step).</summary>
        public void Tick(float delta)
        {
            if (!External && _placed)
            {
                _sinceSnapshot += delta;
                var goal = _targetGame + _velocityGame * Mathf.Min(_sinceSnapshot, .2f);
                if ((goal - GamePosition).sqrMagnitude > 25f) GamePosition = goal; else GamePosition = Vector3.Lerp(GamePosition, goal, 1f - Mathf.Exp(-14f * delta));
                if (new Vector2(_velocityGame.x, _velocityGame.z).sqrMagnitude > .04f) Heading = Mathf.LerpAngle(Heading, _targetYaw, 1f - Mathf.Exp(-12f * delta));
                ApplyTransform();
            }
            Speed = new Vector2(_velocityGame.x, _velocityGame.z).magnitude; Pose(delta);
        }

        private static float Ease(float current, float target, float delta, float rate = 12f) => Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * delta));

        private void Pose(float delta)
        {
            var motion = Motion; var running = motion == NetworkMotionState.Run; var walking = motion == NetworkMotionState.Walk || running;
            _phase += delta * (walking ? Mathf.Clamp(Speed, 1f, 7f) * 1.9f : motion == NetworkMotionState.Swim ? 4f : motion == NetworkMotionState.Dance ? 6f : 1.6f);
            var amplitude = walking ? Mathf.Clamp01(Speed / 6.4f) * (running ? 62f : 42f) : 0f; _swing = Ease(_swing, amplitude, delta);
            float legL = Mathf.Sin(_phase) * _swing, legR = -legL, armL = -legL * .9f, armR = legL * .9f; float armSideL = 0f, armSideR = 0f, pitch = 0f, drop = 0f;
            switch (motion)
            {
                // a positive turn about x swings a hanging limb backward (and leans the body forward); negative swings it forward and up; about z a positive turn lifts the right arm out sideways
                case NetworkMotionState.Idle: armL = armR = Mathf.Sin(_phase * .6f) * 3f; legL = legR = 0f; armSideL = -4f; armSideR = 4f; break;
                case NetworkMotionState.Jump: legL = -25f; legR = 15f; armL = armR = -150f; armSideL = -10f; armSideR = 10f; break;
                case NetworkMotionState.Fall: legL = -10f; legR = 10f; armL = armR = -120f; armSideL = -35f; armSideR = 35f; break;
                case NetworkMotionState.Fly: pitch = Mathf.Clamp01(Speed / 3f) * 70f; legL = 8f; legR = -8f; armL = armR = 20f; break;
                case NetworkMotionState.Swim: pitch = 75f; legL = Mathf.Sin(_phase * 2f) * 25f; legR = -legL; armL = -(Mathf.Sin(_phase) * 70f + 120f); armR = -(-Mathf.Sin(_phase) * 70f + 120f); break;
                case NetworkMotionState.Sit: legL = legR = -85f; armL = armR = -35f; drop = .5f; break;
                case NetworkMotionState.Wave: legL = legR = 0f; armL = 0f; armR = 0f; armSideL = -4f; armSideR = 140f + Mathf.Sin(_phase * 3f) * 22f; break;
                case NetworkMotionState.Dance: legL = Mathf.Sin(_phase) * 30f; legR = -legL; armL = -(120f + Mathf.Sin(_phase) * 40f); armR = -(120f - Mathf.Sin(_phase) * 40f); drop = Mathf.Abs(Mathf.Sin(_phase)) * .08f; break;
                case NetworkMotionState.Typing: legL = legR = 0f; armL = armR = -70f; armSideL = armSideR = 6f; break;
            }
            if (Speed > .8f && ActiveGesture != Gesture.None) StopGesture();
            if (ActiveGesture != Gesture.None && motion !=NetworkMotionState.Fly && motion != NetworkMotionState.Swim && motion != NetworkMotionState.Sit)
            {
                _gestureLeft -= delta; if (_gestureLeft <= 0f) ActiveGesture = Gesture.None;
                legL = legR = 0f; armSideL = -4f; armSideR = 4f; armL = armR = 0f; pitch = 0f; drop = 0f;
                switch (ActiveGesture)
                {
                    case Gesture.Applaud: armL = armR = -75f; armSideL = 12f + Mathf.Sin(_phase * 6f) * 14f; armSideR = -armSideL; break;
                    case Gesture.Point: armR = -95f; armSideR = 4f; break;
                    case Gesture.Greet: pitch = 38f; armL = armR = 8f; break;
                    case Gesture.Laugh: pitch = -12f; armL = armR = -50f; drop = Mathf.Abs(Mathf.Sin(_phase * 8f)) * .04f; break;
                    case Gesture.SitFloor: legL = legR = -85f; armL = armR = -35f; drop = .5f; break;
                }
            }
            _bodyPitch = Ease(_bodyPitch, pitch, delta); _sitDrop = Ease(_sitDrop, drop, delta);
            _body.localRotation = Quaternion.Euler(_bodyPitch, 0f, 0f); _body.localPosition = new Vector3(0f, -_sitDrop, 0f);
            _leftLeg.localRotation = Quaternion.Euler(legL, 0f, 0f); _rightLeg.localRotation = Quaternion.Euler(legR, 0f, 0f);
            _leftArm.localRotation = Quaternion.Euler(armL, 0f, armSideL); _rightArm.localRotation = Quaternion.Euler(armR, 0f, armSideR);
        }
    }
}
