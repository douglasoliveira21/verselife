using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Networking;
using LifeVerse.Unity.World;
using UnityEngine;

namespace LifeVerse.Unity.Avatar
{
    /// <summary>The angles of one instant of the body (degrees): how far each limb swings, the arms out to the side, how the body leans and how far it sinks (sitting).</summary>
    public struct PoseValues { public float LegL, LegR, ArmL, ArmR, ArmSideL, ArmSideR, Pitch, Drop; }

    /// <summary>
    /// A person in the world. By default a simple procedural body (torso, head, arms and legs hinged at the shoulders and hips) dressed from the appearance the server sends
    /// (skin, hair, beard, eyes, shirt, trousers and shoes tints, hat, glasses and backpack, and the height and shoulder sliders); with a custom GLB body the person's own
    /// skeleton is drawn instead (<see cref="SkeletalBody"/>) and the game's clips move it. The origin is at the feet, as in the game. Positions go through
    /// <see cref="GameSpace"/>; the body faces the way the server says (yaw = atan2(vx, -vz), the same angle as a Unity heading). A remote body follows the snapshots smoothly;
    /// the local player's is placed by <see cref="LocalPlayer"/>. The limbs swing by motion state and gesture; the head leans as far as the person looks (<see cref="HeadLook"/>);
    /// <see cref="Detail"/> says how much is drawn (hidden when far, the simple body when a little far).
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
        public AvatarAppearance Appearance { get; private set; } = new AvatarAppearance();
        public SkeletalBody Skeleton { get; private set; }
        public AvatarDetail Detail { get; private set; } = AvatarDetail.Full;
        /// <summary>The head leans up (positive) or down (negative) this far, radians, as the person looks (already within what a head can do).</summary>
        public float HeadPitch { get; private set; }

        /// <summary>The short gestures the network carries besides wave and dance (the numbers are those of <c>ExtraGestures.States</c>).</summary>
        public enum Gesture { None, Applaud, Point, Greet, Laugh, SitFloor }
        public Gesture ActiveGesture { get; private set; }
        private float _gestureLeft;
        private TextMesh _tag;
        public bool Visible { get; private set; } = true;
        public Transform NameTag => _tag != null ? _tag.transform : null;
        public bool Speaking { get; private set; }
        /// <summary>Somebody is talking: their name is lit green while their voice is heard (or sent, for oneself).</summary>
        public void SetSpeaking(bool speaking) { if (Speaking == speaking) return; Speaking = speaking; if (_tag != null) _tag.color = speaking ? new Color(.2f, 1f, .6f) : Color.white; }

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
            if (visible) ApplyDetail();
        }

        private Transform _body, _head, _leftArm, _rightArm, _leftLeg, _rightLeg, _hairRoot, _extras;
        private Material _skin, _shirt, _trousers, _shoes, _hairMaterial;
        private Vector3 _targetGame, _velocityGame; private float _targetYaw, _sinceSnapshot; private bool _placed;
        private float _phase, _swing, _bodyPitch, _sitDrop;
        private readonly System.Collections.Generic.List<GameObject> _dressing = new System.Collections.Generic.List<GameObject>();

        public Transform LeftLeg => _leftLeg;
        public Transform RightArm => _rightArm;
        public Transform Torso => _body;
        public Transform Head => _head;
        public Vector3 TargetGame => _targetGame;
        public int Dressing => _dressing.Count;

        public static AvatarView Create(int peerId, string name, Color color)
        {
            var root = new GameObject("Avatar " + name); var view = root.AddComponent<AvatarView>(); view.PeerId = peerId; view.DisplayName = name; view.Build(color); root.AddComponent<AvatarSounds>(); return view;
        }

        private static Material Surface(Color color, float smoothness = .3f)
        {
            var prototype = Resources.Load<Material>("LifeVerse/ObjectMaterial"); var material = new Material(prototype != null ? prototype : new Material(Shader.Find("HDRP/Lit")));
            material.SetColor("_BaseColor", color); material.SetFloat("_SmoothnessRemapMin", smoothness); material.SetFloat("_SmoothnessRemapMax", smoothness); material.SetFloat("_MetallicRemapMax", 0f); return material;
        }

        private static Transform Part(Transform parent, PrimitiveType shape, string name, Vector3 centre, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(shape); part.name = name; var collider = part.GetComponent<Collider>(); if (collider != null) { if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider); }
            part.transform.SetParent(parent, false); part.transform.localPosition = centre; part.transform.localScale = scale; part.GetComponent<MeshRenderer>().sharedMaterial = material; return part.transform;
        }

        private Transform Limb(Transform parent, string name, Vector3 joint, float length, float thickness, Material material, Material tip = null)
        {
            var pivot = new GameObject(name).transform; pivot.SetParent(parent, false); pivot.localPosition = joint;
            Part(pivot, PrimitiveType.Capsule, name + " mesh", new Vector3(0, -length / 2f, 0), new Vector3(thickness, length / 2f, thickness), material);
            if (tip != null) Part(pivot, PrimitiveType.Cube, name + " tip", new Vector3(0, -length + (name.EndsWith("Leg") ? .035f : .02f), name.EndsWith("Leg") ? .04f : 0f), name.EndsWith("Leg") ? new Vector3(thickness * 1.1f, .07f, .26f) : new Vector3(thickness * 1.1f, .08f, thickness * 1.1f), tip);
            return pivot;
        }

        private void Build(Color color)
        {
            _skin = Surface(new Color(.86f, .68f, .55f)); _shirt = Surface(color); _trousers = Surface(color * .45f + new Color(.1f, .1f, .12f) * .5f); _shoes = Surface(new Color(.9f, .9f, .92f)); _hairMaterial = Surface(new Color(.17f, .1f, .08f), .5f);
            _body = new GameObject("Body").transform; _body.SetParent(transform, false);
            Part(_body, PrimitiveType.Cube, "Torso", new Vector3(0, 1.22f, 0), new Vector3(.42f, .56f, .22f), _shirt);
            _head = Part(_body, PrimitiveType.Sphere, "Head", new Vector3(0, 1.67f, 0), new Vector3(.26f, .28f, .26f), _skin);
            Part(_head, PrimitiveType.Cube, "Nose", new Vector3(0, -.05f, .5f), new Vector3(.12f, .14f, .25f), _skin);
            _leftArm = Limb(_body, "LeftArm", new Vector3(-.29f, 1.46f, 0), .56f, .09f, _shirt, _skin); _rightArm = Limb(_body, "RightArm", new Vector3(.29f, 1.46f, 0), .56f, .09f, _shirt, _skin);
            _leftLeg = Limb(_body, "LeftLeg", new Vector3(-.11f, .94f, 0), .94f, .11f, _trousers, _shoes); _rightLeg = Limb(_body, "RightLeg", new Vector3(.11f, .94f, 0), .94f, .11f, _trousers, _shoes);
            _extras = new GameObject("Dressing").transform; _extras.SetParent(_body, false);
            var tag = new GameObject("NameTag"); tag.transform.SetParent(transform, false); tag.transform.localPosition = new Vector3(0f, 2.05f, 0f);
            _tag = tag.AddComponent<TextMesh>(); _tag.text = DisplayName; _tag.fontSize = 64; _tag.characterSize = .035f; _tag.anchor = TextAnchor.LowerCenter; _tag.alignment = TextAlignment.Center; _tag.color = Color.white;
            _tag.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); tag.GetComponent<MeshRenderer>().sharedMaterial = _tag.font.material; tag.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ---- appearance ----

        /// <summary>Dresses the body as the server describes the person (skin, hair, beard, eyes, clothes' tints, hat, glasses, backpack, sliders) and, with a custom body, remembers which one.</summary>
        public void ApplyAppearance(AvatarAppearance appearance)
        {
            Appearance = appearance ?? new AvatarAppearance(); var a = Appearance;
            if (!string.IsNullOrEmpty(a.DisplayName) && a.DisplayName != "Resident") { DisplayName = a.DisplayName; if (_tag != null) _tag.text = DisplayName; }
            _skin.SetColor("_BaseColor", a.Skin); _shirt.SetColor("_BaseColor", a.Tint("top", new Color(.23f, .68f, .76f))); _trousers.SetColor("_BaseColor", a.Tint("bottom", new Color(.15f, .23f, .34f))); _shoes.SetColor("_BaseColor", a.Tint("shoes", new Color(.9f, .91f, .93f)));
            _hairMaterial.SetColor("_BaseColor", a.Hair);
            // the sliders: height scales the whole body, shoulders its width, arms and legs their length, the head its size
            var height = a.Height * (a.BaseModel == "human_b" ? 1.04f : 1f);
            _body.localScale = new Vector3(a.Shoulder * (a.BaseModel == "human_b" ? 1.06f : 1f), height, a.Shoulder * (a.BaseModel == "human_b" ? 1.06f : 1f));
            float Slider(string key) => a.Body.TryGetValue(key, out var v) ? v : 1f;
            _leftArm.localScale = _rightArm.localScale = new Vector3(1f, Slider("arms"), 1f); _leftLeg.localScale = _rightLeg.localScale = new Vector3(1f, Slider("legs"), 1f); _head.localScale = new Vector3(.26f, .28f, .26f) * Slider("head");
            // clothes the catalog draws differently: shorts and skirts show the legs, a dress covers them
            var bottom = a.Item("bottom", "pants_basic"); _leftLeg.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial = _rightLeg.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial = bottom == "pants_basic" ? _trousers : _skin;
            foreach (var piece in _dressing) if (piece != null) { if (Application.isPlaying) Destroy(piece); else DestroyImmediate(piece); }
            _dressing.Clear();
            AddHair(a); AddFace(a); AddWorn(a, bottom);
            if (Skeleton != null) Skeleton.Tint(a);
            ApplyDetail();
        }

        private GameObject Dress(Transform parent, PrimitiveType shape, string name, Vector3 centre, Vector3 scale, Material material)
        {
            var part = Part(parent, shape, name, centre, scale, material).gameObject; _dressing.Add(part); return part;
        }

        private void AddHair(AvatarAppearance a)
        {
            var hides = a.Item("hat", "none") == "beanie_basic"; if (a.HairStyle == "bald" || hides) return;
            switch (a.HairStyle)
            {
                case "short": Dress(_head, PrimitiveType.Sphere, "Hair", new Vector3(0, .12f, -.06f), new Vector3(1.08f, .8f, 1.06f), _hairMaterial); break;
                case "bob": Dress(_head, PrimitiveType.Sphere, "Hair", new Vector3(0, .0f, -.1f), new Vector3(1.2f, 1.15f, 1.2f), _hairMaterial); break;
                case "long": Dress(_head, PrimitiveType.Sphere, "Hair", new Vector3(0, .0f, -.1f), new Vector3(1.15f, 1.1f, 1.12f), _hairMaterial); Dress(_head, PrimitiveType.Cube, "Hair back", new Vector3(0, -.9f, -.55f), new Vector3(1.0f, 2.2f, .35f), _hairMaterial); break;
                case "ponytail": Dress(_head, PrimitiveType.Sphere, "Hair", new Vector3(0, .12f, -.06f), new Vector3(1.08f, .8f, 1.06f), _hairMaterial); Dress(_head, PrimitiveType.Capsule, "Ponytail", new Vector3(0, -.25f, -.7f), new Vector3(.3f, .9f, .3f), _hairMaterial); break;
                case "mohawk": Dress(_head, PrimitiveType.Cube, "Mohawk", new Vector3(0, .6f, -.05f), new Vector3(.18f, .5f, 1.1f), _hairMaterial); break;
            }
        }

        private void AddFace(AvatarAppearance a)
        {
            var eyes = Surface(a.Eyes, .8f); var spacing = a.Face.TryGetValue("eyeSpacing", out var sp) ? sp : 1f; var size = a.Face.TryGetValue("eyeScale", out var sc) ? sc : 1f;
            foreach (var side in new[] { -1f, 1f }) Dress(_head, PrimitiveType.Sphere, "Eye", new Vector3(side * .22f * spacing, .12f, .43f), Vector3.one * .16f * size, eyes);
            if (a.BeardStyle != "none")
            {
                var length = a.BeardStyle == "full" ? .5f : a.BeardStyle == "short" ? .3f : .12f; Dress(_head, PrimitiveType.Sphere, "Beard", new Vector3(0, -.3f - length * .2f, .18f), new Vector3(.8f, .35f + length, .7f), _hairMaterial);
            }
        }

        private void AddWorn(AvatarAppearance a, string bottom)
        {
            switch (a.Item("hat", "none"))
            {
                case "cap_basic": Dress(_head, PrimitiveType.Sphere, "Cap", new Vector3(0, .35f, 0), new Vector3(1.1f, .55f, 1.1f), Surface(a.Tint("hat", Color.gray))); Dress(_head, PrimitiveType.Cube, "Cap brim", new Vector3(0, .22f, .62f), new Vector3(.9f, .08f, .5f), Surface(a.Tint("hat", Color.gray))); break;
                case "hat_basic": Dress(_head, PrimitiveType.Cylinder, "Hat brim", new Vector3(0, .42f, 0), new Vector3(1.7f, .03f, 1.7f), Surface(a.Tint("hat", Color.gray))); Dress(_head, PrimitiveType.Cylinder, "Hat top", new Vector3(0, .62f, 0), new Vector3(.95f, .22f, .95f), Surface(a.Tint("hat", Color.gray))); break;
                case "beanie_basic": Dress(_head, PrimitiveType.Sphere, "Beanie", new Vector3(0, .25f, 0), new Vector3(1.12f, .85f, 1.12f), Surface(a.Tint("hat", Color.gray))); break;
            }
            if (a.Item("glasses", "none") != "none" || a.Item("accessory", "none").StartsWith("glasses"))
            {
                var frame = Surface(a.Tint("glasses", Color.black), .8f); foreach (var side in new[] { -1f, 1f }) Dress(_head, PrimitiveType.Cylinder, "Lens", new Vector3(side * .22f, .12f, .5f), new Vector3(.3f, .02f, .3f), frame).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            if (a.Item("back", "none") == "backpack_basic") Dress(_body, PrimitiveType.Cube, "Backpack", new Vector3(0, 1.22f, -.2f), new Vector3(.32f, .42f, .18f), Surface(a.Tint("back", new Color(.5f, .25f, .15f))));
            if (a.Item("neck", "none") == "scarf_basic") Dress(_body, PrimitiveType.Cylinder, "Scarf", new Vector3(0, 1.5f, 0), new Vector3(.24f, .05f, .24f), Surface(a.Tint("neck", Color.red)));
            if (a.Item("top", "tee_basic") == "dress_basic" || bottom == "skirt_basic") Dress(_body, PrimitiveType.Cylinder, "Skirt", new Vector3(0, .78f, 0), new Vector3(.45f, .16f, .3f), bottom == "skirt_basic" ? _trousers : _shirt);
            if (a.Item("jacket", "none") == "coat_long") Dress(_body, PrimitiveType.Cube, "Coat", new Vector3(0, .98f, 0), new Vector3(.46f, .95f, .26f), Surface(a.Tint("jacket", Color.gray)));
        }

        // ---- the custom body ----

        /// <summary>Takes the person's own skeleton in place of the procedural body (null goes back to the procedural one).</summary>
        public void UseSkeleton(SkeletalBody skeleton)
        {
            Skeleton = skeleton; ApplyDetail();
        }

        /// <summary>How much is drawn and moved: hidden, the simple body, or the custom body (Reduced and Full draw it; Full adds the custom clips and items).</summary>
        public void SetDetail(AvatarDetail detail) { Detail = detail; ApplyDetail(); }

        private void ApplyDetail()
        {
            if (_body == null) return;
            var shown = Visible && Detail != AvatarDetail.Hidden; var custom = Skeleton != null && Detail >= AvatarDetail.Reduced;
            foreach (var renderer in _body.GetComponentsInChildren<Renderer>(true)) renderer.enabled = shown && !custom;
            if (Skeleton != null) Skeleton.SetShown(shown && custom);
            if (_tag != null) _tag.GetComponent<MeshRenderer>().enabled = shown;
        }

        // ---- snapshots ----

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

        /// <summary>The head leans as far as the person looks (radians, up positive); a head never bends farther than <see cref="HeadLook.MaxPitch"/>.</summary>
        public void ApplyHeadLook(float pitch) { HeadPitch = HeadLook.Drawn(pitch); }

        private void ApplyTransform()
        {
            transform.position = GameSpace.ToUnity(GamePosition); transform.rotation = Quaternion.Euler(0f, Heading, 0f);
        }

        /// <summary>The name turns to face the camera and hides far away; it never shows on one's own body.</summary>
        private void LateUpdate()
        {
            if (_tag == null) return;
            var camera = Camera.main; var show = Visible && !External && camera != null && Detail != AvatarDetail.Hidden;
            if (show) { var away = camera.transform.position - _tag.transform.position; show = away.sqrMagnitude < 45f * 45f; if (show) _tag.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up); } // TextMesh is read from its back, so it looks at the camera, not away from it
            _tag.gameObject.SetActive(show);
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
            Speed = new Vector2(_velocityGame.x, _velocityGame.z).magnitude;
            if (Detail == AvatarDetail.Hidden) return;   // nobody sees it: not even posed
            Pose(delta);
        }

        private static float Ease(float current, float target, float delta, float rate = 12f) => Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * delta));

        /// <summary>The pose of a motion state (and of a gesture) at a point of the walk cycle. A positive turn about x swings a hanging limb backward and leans the body forward;
        /// negative swings it forward and up; about z a positive turn lifts the right arm out sideways.</summary>
        public static PoseValues Evaluate(NetworkMotionState motion, Gesture gesture, float phase, float swing, float speed)
        {
            var running = motion == NetworkMotionState.Run; var p = new PoseValues();
            p.LegL = Mathf.Sin(phase) * swing; p.LegR = -p.LegL; p.ArmL = -p.LegL * .9f; p.ArmR = p.LegL * .9f;
            switch (motion)
            {
                case NetworkMotionState.Idle: p.ArmL = p.ArmR = Mathf.Sin(phase * .6f) * 3f; p.LegL = p.LegR = 0f; p.ArmSideL = -4f; p.ArmSideR = 4f; break;
                case NetworkMotionState.Jump: p.LegL = -25f; p.LegR = 15f; p.ArmL = p.ArmR = -150f; p.ArmSideL = -10f; p.ArmSideR = 10f; break;
                case NetworkMotionState.Fall: p.LegL = -10f; p.LegR = 10f; p.ArmL = p.ArmR = -120f; p.ArmSideL = -35f; p.ArmSideR = 35f; break;
                case NetworkMotionState.Fly: p.Pitch = Mathf.Clamp01(speed / 3f) * 70f; p.LegL = 8f; p.LegR = -8f; p.ArmL = p.ArmR = 20f; break;
                case NetworkMotionState.Swim: p.Pitch = 75f; p.LegL = Mathf.Sin(phase * 2f) * 25f; p.LegR = -p.LegL; p.ArmL = -(Mathf.Sin(phase) * 70f + 120f); p.ArmR = -(-Mathf.Sin(phase) * 70f + 120f); break;
                case NetworkMotionState.Sit: p.LegL = p.LegR = -85f; p.ArmL = p.ArmR = -35f; p.Drop = .5f; break;
                case NetworkMotionState.Wave: p.LegL = p.LegR = 0f; p.ArmL = 0f; p.ArmR = 0f; p.ArmSideL = -4f; p.ArmSideR = 140f + Mathf.Sin(phase * 3f) * 22f; break;
                case NetworkMotionState.Dance: p.LegL = Mathf.Sin(phase) * 30f; p.LegR = -p.LegL; p.ArmL = -(120f + Mathf.Sin(phase) * 40f); p.ArmR = -(120f - Mathf.Sin(phase) * 40f); p.Drop = Mathf.Abs(Mathf.Sin(phase)) * .08f; break;
                case NetworkMotionState.Typing: p.LegL = p.LegR = 0f; p.ArmL = p.ArmR = -70f; p.ArmSideL = -6f; p.ArmSideR = 6f; break;
            }
            if (gesture != Gesture.None && motion != NetworkMotionState.Fly && motion != NetworkMotionState.Swim && motion != NetworkMotionState.Sit)
            {
                p = new PoseValues { ArmSideL = -4f, ArmSideR = 4f };
                switch (gesture)
                {
                    case Gesture.Applaud: p.ArmL = p.ArmR = -75f; p.ArmSideL = 12f + Mathf.Sin(phase * 6f) * 14f; p.ArmSideR = -p.ArmSideL; break;
                    case Gesture.Point: p.ArmR = -95f; break;
                    case Gesture.Greet: p.Pitch = 38f; p.ArmL = p.ArmR = 8f; break;
                    case Gesture.Laugh: p.Pitch = -12f; p.ArmL = p.ArmR = -50f; p.Drop = Mathf.Abs(Mathf.Sin(phase * 8f)) * .04f; break;
                    case Gesture.SitFloor: p.LegL = p.LegR = -85f; p.ArmL = p.ArmR = -35f; p.Drop = .5f; break;
                }
            }
            return p;
        }

        private void Pose(float delta)
        {
            var motion = Motion; var running = motion == NetworkMotionState.Run; var walking = motion == NetworkMotionState.Walk || running;
            if (Speed > .8f && ActiveGesture != Gesture.None) StopGesture();
            if (ActiveGesture != Gesture.None) { _gestureLeft -= delta; if (_gestureLeft <= 0f) ActiveGesture = Gesture.None; }
            _phase += delta * (walking ? Mathf.Clamp(Speed, 1f, 7f) * 1.9f : motion == NetworkMotionState.Swim ? 4f : motion == NetworkMotionState.Dance ? 6f : 1.6f);
            var amplitude = walking ? Mathf.Clamp01(Speed / 6.4f) * (running ? 62f : 42f) : 0f; _swing = Ease(_swing, amplitude, delta);
            var pose = Evaluate(motion, ActiveGesture, _phase, _swing, Speed);
            if (Skeleton != null && Detail >= AvatarDetail.Reduced) { Skeleton.Tick(delta, motion, ActiveGesture, Speed, _phase, pose, HeadPitch, Detail >= AvatarDetail.Full); return; }
            _bodyPitch = Ease(_bodyPitch, pose.Pitch, delta); _sitDrop = Ease(_sitDrop, pose.Drop, delta);
            _body.localRotation = Quaternion.Euler(_bodyPitch, 0f, 0f); _body.localPosition = new Vector3(0f, -_sitDrop, 0f);
            _head.localRotation = Quaternion.Euler(-HeadPitch * Mathf.Rad2Deg, 0f, 0f);
            _leftLeg.localRotation = Quaternion.Euler(pose.LegL, 0f, 0f); _rightLeg.localRotation = Quaternion.Euler(pose.LegR, 0f, 0f);
            _leftArm.localRotation = Quaternion.Euler(pose.ArmL, 0f, pose.ArmSideL); _rightArm.localRotation = Quaternion.Euler(pose.ArmR, 0f, pose.ArmSideR);
        }
    }
}
