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
        public void SetFirstPerson(bool on)
        {
            if (FirstPerson == on) return;
            FirstPerson = on; _bob.Reset(); _sway.Reset(); _eyes.Reset();
            if (Interactive) { Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None; }
            if (on) Pitch = Mathf.Clamp(Pitch, -80f, 80f);
        }

        public void Toggle() => SetFirstPerson(!FirstPerson);

        private void LateUpdate()
        {
            if (!Interactive) return;
            var mouse = Mouse.current; if (mouse == null) return;
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
            var dt = snap ? 0f : Time.deltaTime;
            camera.fieldOfView = Mathf.Clamp(FirstPerson ? FirstPersonFieldOfView : FieldOfView, 30f, 120f);
            if (FirstPerson)
            {
                _bob.Step(dt, speed, grounded, Options); var yawRate = dt > 0f ? Mathf.DeltaAngle(_lastYaw, Yaw) * Mathf.Deg2Rad / dt : 0f; _lastYaw = Yaw;
                _sway.Step(dt, -yawRate, strafe, Options); Roll = _sway.Roll;
                var head = GameSpace.ToUnity(gamePosition) + Vector3.up * EyeHeight; var steady = _eyes.Step(new System.Numerics.Vector3(head.x, head.y, head.z), dt, Options.StabilizeSeconds, Options.StabilizeVerticalSeconds);
                var eye = new Vector3(steady.X, steady.Y, steady.Z); var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
                eye += rotation * Vector3.right * _bob.Offset.X + Vector3.up * _bob.Offset.Y; Focus = head;
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
