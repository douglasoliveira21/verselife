using LifeVerse.Shared.Camera;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LifeVerse.Unity.Avatar
{
    /// <summary>
    /// The camera of the person playing. Third person (the default): it orbits the head (hold the right mouse button and move to turn it, the wheel to come closer or go away),
    /// stays out of the ground (pulled in when the terrain is between it and the player, never under it) and looks at the head. First person (V): the eyes of the body, with the
    /// mouse turning the view (the cursor is locked), the shared head bob, steady eyes and a small lean when turning (the same <c>FirstPersonOptions</c> rules as the Godot
    /// client); the person's own body is hidden. <see cref="Yaw"/> is the heading the camera looks along (Unity degrees about the vertical): walking forward goes that way.
    /// The build camera, the free camera and the photo camera come later; so does the camera not going through built objects.
    /// </summary>
    public sealed class FollowCamera : MonoBehaviour
    {
        public Camera Camera; public TerrainView Terrain;
        public float Yaw, Pitch = 14f, Distance = 4.6f;
        public const float MinDistance = 1.2f, MaxDistance = 14f, HeadHeight = 1.55f, EyeHeight = 1.62f, ClearGround = .35f;
        /// <summary>When false the mouse and the wheel are ignored (a menu is open, or a test is driving it).</summary>
        public bool Interactive = true;
        /// <summary>Degrees of turn per pixel of mouse, and whether pushing the mouse up looks down (settings).</summary>
        public float Sensitivity = .15f; public bool InvertY;
        public bool FirstPerson { get; private set; }
        public FirstPersonOptions Options = new FirstPersonOptions();
        public Vector3 Focus { get; private set; }
        /// <summary>Looking at a point of the world (an object) instead of the person, from this far; cleared by moving or Esc.</summary>
        public Vector3? ZoomTarget; public float ZoomDistance = 3f;
        public float Roll { get; private set; }
        /// <summary>The choices of the settings (Câmera e visualização): the opening of the lens in each view, how much a wheel tick zooms, the head height and the shoulder offset of the third person view,
        /// how quickly it follows (higher is tighter), whether it is pulled in by the ground, and whether the wheel goes into first person at the closest distance and back out.</summary>
        public float FieldOfView = 75f, FirstPersonFieldOfView = 75f, ZoomStep = .6f, HeightOffset, Shoulder, Smoothing = 18f; public bool Collide = true, WheelToFirstPerson;
        /// <summary>The mirror of the avatar editor: the third person camera stands in front of the avatar and looks back at it.</summary>
        public bool Mirror;
        private Vector3 _position; private float _lastYaw;
        private readonly HeadBob _bob = new HeadBob(); private readonly CameraSway _sway = new CameraSway(); private readonly EyeStabilizer _eyes = new EyeStabilizer();

        public void Snap(Vector3 gamePosition) { Place(gamePosition, 0f, true, true); }
        public void Follow(Vector3 gamePosition, float speed = 0f, bool grounded = true, float strafe = 0f) { Place(gamePosition, speed, grounded, false, strafe); }

        /// <summary>Switches between first and third person (the cursor is locked in first person while the camera is interactive).</summary>
        /// <summary>"Colisão em primeira pessoa": a solid thing between the neck and the eyes (a little beyond them) pulls the eyes back, so the camera does not look through a wall.</summary>
        public static Vector3 KeepEyesOutOfWalls(Vector3 neck, Vector3 eyes, Quaternion look)
        {
            var ahead = eyes + look * Vector3.forward * .08f; var along = ahead - neck; var length = along.magnitude; if (length < 1e-4f) return eyes;
            if (!Physics.Raycast(neck, along / length, out var hit, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return eyes;
            var clipped = LifeVerse.Shared.Camera.EyeCollision.Clip(new System.Numerics.Vector3(neck.x, neck.y, neck.z), new System.Numerics.Vector3(eyes.x, eyes.y, eyes.z), hit.distance);
            return new Vector3(clipped.X, clipped.Y, clipped.Z);
        }

        private float _viewYaw, _viewPitch; private bool _viewStarted;

        public void SetFirstPerson(bool on)
        {
            if (FirstPerson == on) return;
            _viewStarted = false;
            FirstPerson = on; _bob.Reset(); _sway.Reset(); _eyes.Reset();
            if (Interactive) { Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None; }
            if (on) Pitch = Mathf.Clamp(Pitch, -80f, 80f);
        }

        public void Toggle() => SetFirstPerson(!FirstPerson);

        // ---- the detached camera (ALT + V) and the focus by double click ----

        /// <summary>The camera left the avatar (ALT + V): the walking keys move it through the world, the avatar stands still; Esc or ALT + V brings it back.</summary>
        public bool Detached { get; private set; }
        /// <summary>Where the avatar was when the camera left (the detached camera stays within <c>DetachedRange</c> of it).</summary>
        public Vector3 DetachedAnchor { get; private set; }
        private Vector3 _detachedAt;
        /// <summary>What a double click focuses on: a person or an object under the ray (never the ground). Set by the game.</summary>
        public System.Func<Ray, Vector3?> PickFocus;
        private float _lastClick = -10f; private Vector2 _lastClickAt;

        public void SetDetached(bool on)
        {
            if (on == Detached) return; if (on && !FreeOptions.AllowDetached) return;
            Detached = on; var camera = Camera != null ? Camera : UnityEngine.Camera.main;
            if (on) { _detachedAt = camera != null ? camera.transform.position : Focus; DetachedAnchor = Focus; if (FirstPerson) SetFirstPerson(false); }
        }

        /// <summary>One step of the detached camera: the keys move it (Shift three times faster) along where it looks, up with jump and down with descend, kept within the range of the avatar.</summary>
        public Vector3 MoveDetached(Vector3 input, bool fast, float dt)
        {
            var rotation = Quaternion.Euler(Pitch, Yaw, 0f); var move = rotation * new Vector3(input.x, 0f, input.z) + Vector3.up * input.y; if (move.sqrMagnitude > 1f) move.Normalize();
            var next = _detachedAt + move * FreeOptions.DetachedSpeed * (fast ? 3f : 1f) * dt; var offset = next - DetachedAnchor;
            if (offset.magnitude > FreeOptions.DetachedRange) next = DetachedAnchor + offset.normalized * FreeOptions.DetachedRange;
            return _detachedAt = next;
        }

        private void DetachedUpdate(Mouse mouse, Keyboard keyboard)
        {
            if (mouse.rightButton.isPressed) { var delta = mouse.delta.ReadValue(); Yaw += delta.x * Sensitivity; Pitch = Mathf.Clamp(Pitch - delta.y * Sensitivity * (InvertY ? -1f : 1f), -85f, 85f); }
            var input = new Vector3((KeyBindings.Held(GameAction.Right) ? 1f : 0f) - (KeyBindings.Held(GameAction.Left) ? 1f : 0f), (KeyBindings.Held(GameAction.Jump) ? 1f : 0f) - (KeyBindings.Held(GameAction.Descend) ? 1f : 0f), (KeyBindings.Held(GameAction.Forward) ? 1f : 0f) - (KeyBindings.Held(GameAction.Back) ? 1f : 0f));
            var at = MoveDetached(input, keyboard != null && keyboard.shiftKey.isPressed, Time.deltaTime);
            var camera = Camera != null ? Camera : UnityEngine.Camera.main; if (camera != null) { camera.transform.position = at; camera.transform.rotation = Quaternion.Euler(Pitch, Yaw, 0f); }
        }

        /// <summary>Whether a click is the second of a double click (within 0.35 s and 6 pixels of the first).</summary>
        public bool IsDoubleClick(float now, Vector2 at)
        {
            var twice = now - _lastClick <= .35f && (at - _lastClickAt).sqrMagnitude <= 36f; _lastClick = twice ? -10f : now; _lastClickAt = at; return twice;
        }

        private void LateUpdate()
        {
            if (!Interactive) return;
            var mouse = Mouse.current; if (mouse == null) return; var keys = Keyboard.current;
            if (keys != null && FreeOptions.AllowDetached && keys.altKey.isPressed && keys.vKey.wasPressedThisFrame) SetDetached(!Detached);
            if (Detached && keys != null && keys.escapeKey.wasPressedThisFrame) SetDetached(false);
            if (Detached) { DetachedUpdate(mouse, keys); return; }
            if (FreeOptions.FocusOnDoubleClick && mouse.leftButton.wasPressedThisFrame && Camera != null && !(keys != null && (keys.ctrlKey.isPressed || keys.altKey.isPressed || keys.shiftKey.isPressed)))
            {
                var at = mouse.position.ReadValue();
                if (IsDoubleClick(Time.unscaledTime, at) && !(OverInterface?.Invoke(at) ?? false) && PickFocus?.Invoke(Camera.ScreenPointToRay(at)) is { } target) { FocusAt(Camera.ScreenPointToRay(at)); ZoomTarget = target; ZoomDistance = Mathf.Clamp(Vector3.Distance(Camera.transform.position, target), 2.5f, 8f); return; }
            }
            if (!FirstPerson && FreeCamera(mouse)) return;
            var looking = FirstPerson ? Cursor.lockState == CursorLockMode.Locked : mouse.rightButton.isPressed;
            if (looking) { var delta = mouse.delta.ReadValue(); Yaw += delta.x * Sensitivity; Pitch = Mathf.Clamp(Pitch - delta.y * Sensitivity * (InvertY ? -1f : 1f), FirstPerson ? -85f : -60f, FirstPerson ? 85f : 80f); }
            var wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > .01f) Wheel(wheel);
            if (KeyBindings.Pressed(GameAction.FirstPerson)) Toggle();
        }

        /// <summary>The free camera of the Godot client (Second Life style), with the left button: ALT puts the focus on what was clicked and then turns around it (sideways) and comes closer or
        /// goes away (up and down); CTRL+ALT turns around it both ways; CTRL+ALT+SHIFT slides the focus. The camera stays there until the avatar moves or Esc (the game clears <see cref="ZoomTarget"/>).</summary>
        public LifeVerse.Shared.Camera.CameraOptions FreeOptions = new LifeVerse.Shared.Camera.CameraOptions();
        /// <summary>What is under a ray of the screen (an object or the ground), for the focus of the free camera; and whether the interface is under a point of the screen.</summary>
        public System.Func<Ray, Vector3?> PickPoint; public System.Func<Vector2, bool> OverInterface;
        public LifeVerse.Shared.Camera.CameraGesture Gesture { get; private set; }

        private bool FreeCamera(Mouse mouse)
        {
            var keyboard = Keyboard.current; var ctrl = keyboard != null && keyboard.ctrlKey.isPressed; var alt = keyboard != null && keyboard.altKey.isPressed; var shift = keyboard != null && keyboard.shiftKey.isPressed;
            if (mouse.leftButton.wasPressedThisFrame && Camera != null)
            {
                var gesture = LifeVerse.Shared.Camera.CameraInputRules.Classify(ctrl, alt, shift, FreeOptions); var at = mouse.position.ReadValue();
                if (gesture != LifeVerse.Shared.Camera.CameraGesture.None && !(OverInterface?.Invoke(at) ?? false)) { Gesture = gesture; if (gesture != LifeVerse.Shared.Camera.CameraGesture.Pan || !ZoomTarget.HasValue) FocusAt(Camera.ScreenPointToRay(at)); }
            }
            if (Gesture == LifeVerse.Shared.Camera.CameraGesture.None) return false;
            if (!mouse.leftButton.isPressed) { Gesture = LifeVerse.Shared.Camera.CameraGesture.None; return true; }
            var d = mouse.delta.ReadValue(); Drag(Gesture, d.x, d.y); return true;
        }

        /// <summary>Puts the focus of the free camera on what a ray hits (or 10 m along it), keeping the camera where it is.</summary>
        public void FocusAt(Ray ray)
        {
            var point = PickPoint?.Invoke(ray) ?? ray.GetPoint(10f); var from = Camera != null ? Camera.transform.position : point - Vector3.forward * 5f;
            ZoomTarget = point; ZoomDistance = Mathf.Clamp(Vector3.Distance(from, point), FreeOptions.MinDistance + .3f, FreeOptions.MaxDistance);
            var look = (point - from).normalized; if (look.sqrMagnitude > .0001f) { Yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg; Pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg, -80f, 80f); }
        }

        /// <summary>One drag of a free camera gesture (pixels): turn, come closer, slide.</summary>
        public void Drag(LifeVerse.Shared.Camera.CameraGesture gesture, float dx, float dy)
        {
            if (!ZoomTarget.HasValue) return;
            switch (gesture)
            {
                case LifeVerse.Shared.Camera.CameraGesture.OrbitDolly: Yaw += dx * Sensitivity; ZoomDistance = Mathf.Clamp(ZoomDistance * Mathf.Exp(-dy * .01f), FreeOptions.MinDistance + .3f, FreeOptions.MaxDistance); break;
                case LifeVerse.Shared.Camera.CameraGesture.Orbit: case LifeVerse.Shared.Camera.CameraGesture.CtrlOrbit: Yaw += dx * Sensitivity; Pitch = Mathf.Clamp(Pitch - dy * Sensitivity * (InvertY ? -1f : 1f), -80f, 80f); break;
                case LifeVerse.Shared.Camera.CameraGesture.Pan:
                {
                    var rotation = Quaternion.Euler(Pitch, Yaw, 0f); var k = ZoomDistance * .0015f; ZoomTarget = ZoomTarget.Value - rotation * Vector3.right * dx * k - rotation * Vector3.up * dy * k; break;
                }
            }
        }

        /// <summary>A tick of the wheel: closer or farther by the zoom step; at the closest, in (when the setting allows) goes to first person, and out of first person comes back.</summary>
        public void Wheel(float wheel)
        {
            if (!FirstPerson && ZoomTarget.HasValue) { var precise = Keyboard.current != null && Keyboard.current.ctrlKey.isPressed; ZoomDistance = Mathf.Clamp(ZoomDistance * (1f - Mathf.Sign(wheel) * (precise ? .04f : .15f)), FreeOptions.MinDistance + .3f, FreeOptions.MaxDistance); return; }
            if (FirstPerson) { if (WheelToFirstPerson && wheel < 0f) { SetFirstPerson(false); Distance = Mathf.Clamp(MinDistance + ZoomStep, MinDistance, MaxDistance); } return; }
            if (wheel > 0f && WheelToFirstPerson && Distance <= MinDistance + .01f) { SetFirstPerson(true); return; }
            Distance = Mathf.Clamp(Distance - Mathf.Sign(wheel) * Mathf.Max(.1f, ZoomStep), MinDistance, MaxDistance);
        }

        private void Place(Vector3 gamePosition, float speed, bool grounded, bool snap, float strafe = 0f)
        {
            var camera = Camera != null ? Camera : UnityEngine.Camera.main; if (camera == null) return;
            if (Detached && !snap) return;   // the detached camera moves by itself
            if (snap) Detached = false;
            var dt = snap ? 0f : Time.deltaTime;
            camera.fieldOfView = Mathf.Clamp(FirstPerson ? FirstPersonFieldOfView : FieldOfView, 30f, 120f);
            if (FirstPerson)
            {
                _bob.Step(dt, speed, grounded, Options); var yawRate = dt > 0f ? Mathf.DeltaAngle(_lastYaw, Yaw) * Mathf.Deg2Rad / dt : 0f; _lastYaw = Yaw;
                _sway.Step(dt, -yawRate, strafe, Options); Roll = _sway.Roll;
                var head = GameSpace.ToUnity(gamePosition) + Vector3.up * EyeHeight; var steady = _eyes.Step(new System.Numerics.Vector3(head.x, head.y, head.z), dt, Options.StabilizeSeconds, Options.StabilizeVerticalSeconds);
                // "Suavização do olhar": the view catches up with the mouse at the shared rate (instant at 0)
                var follow = dt <= 0f || !_viewStarted ? 1f : 1f - Mathf.Exp(-Options.LookRate * dt); _viewStarted = true;
                _viewYaw = Mathf.LerpAngle(_viewYaw, Yaw, follow); _viewPitch = Mathf.Lerp(_viewPitch, Pitch, follow);
                var eye = new Vector3(steady.X, steady.Y, steady.Z); var rotation = Quaternion.Euler(_viewPitch, _viewYaw, 0f);
                eye += rotation * Vector3.right * _bob.Offset.X + Vector3.up * _bob.Offset.Y; Focus = head;
                if (Options.Collision && Collide) eye = KeepEyesOutOfWalls(head - Vector3.up * .3f, eye, rotation);
                _position = eye; camera.transform.position = eye; camera.transform.rotation = rotation * Quaternion.Euler(0f, 0f, Mathf.Rad2Deg * Roll); return;
            }
            Focus = ZoomTarget ?? GameSpace.ToUnity(gamePosition) + Vector3.up * (HeadHeight + HeightOffset) + Quaternion.Euler(0f, Yaw, 0f) * Vector3.right * Shoulder; Roll = 0f;
            var back = -(Quaternion.Euler(Pitch, Yaw, 0f) * Vector3.forward); if (Mirror && !ZoomTarget.HasValue) back = new Vector3(-back.x, back.y, -back.z); var distance = ZoomTarget.HasValue ? ZoomDistance : Distance;
            if (Collide && Terrain != null && Terrain.Map != null && Terrain.Raycast(new Ray(Focus, back), distance) is { } hit) distance = Mathf.Max(MinDistance * .5f, Vector3.Distance(Focus, hit) - .25f);
            var wanted = Focus + back * distance;
            if (Terrain != null && Terrain.Map != null) { var game = GameSpace.ToGame(wanted); var ground = Terrain.Map.SampleHeight(game.x, game.z) + ClearGround; if (wanted.y < ground && Terrain.Map.SeaLevel < ground) wanted.y = ground; }
            _position = snap ? wanted : Vector3.Lerp(_position, wanted, 1f - Mathf.Exp(-Mathf.Max(1f, Smoothing) * Time.deltaTime));
            if (snap || Time.deltaTime <= 0f) _position = wanted;
            camera.transform.position = _position; camera.transform.rotation = Quaternion.LookRotation(Focus - _position, Vector3.up);
        }
    }
}
