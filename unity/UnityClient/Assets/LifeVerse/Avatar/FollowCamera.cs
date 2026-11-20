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
            var looking = FirstPerson ? Cursor.lockState == CursorLockMode.Locked : mouse.rightButton.isPressed;
            if (looking) { var delta = mouse.delta.ReadValue(); Yaw += delta.x * Sensitivity; Pitch = Mathf.Clamp(Pitch - delta.y * Sensitivity * (InvertY ? -1f : 1f), FirstPerson ? -85f : -60f, FirstPerson ? 85f : 80f); }
            if (!FirstPerson) { var wheel = mouse.scroll.ReadValue().y; if (Mathf.Abs(wheel) > .01f) Distance = Mathf.Clamp(Distance - Mathf.Sign(wheel) * .6f, MinDistance, MaxDistance); }
            var keyboard = Keyboard.current; if (keyboard != null && keyboard.vKey.wasPressedThisFrame) Toggle();
        }

        private void Place(Vector3 gamePosition, float speed, bool grounded, bool snap, float strafe = 0f)
        {
            var camera = Camera != null ? Camera : UnityEngine.Camera.main; if (camera == null) return;
            var dt = snap ? 0f : Time.deltaTime;
            if (FirstPerson)
            {
                _bob.Step(dt, speed, grounded, Options); var yawRate = dt > 0f ? Mathf.DeltaAngle(_lastYaw, Yaw) * Mathf.Deg2Rad / dt : 0f; _lastYaw = Yaw;
                _sway.Step(dt, -yawRate, strafe, Options); Roll = _sway.Roll;
                var head = GameSpace.ToUnity(gamePosition) + Vector3.up * EyeHeight; var steady = _eyes.Step(new System.Numerics.Vector3(head.x, head.y, head.z), dt, Options.StabilizeSeconds, Options.StabilizeVerticalSeconds);
                var eye = new Vector3(steady.X, steady.Y, steady.Z); var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
                eye += rotation * Vector3.right * _bob.Offset.X + Vector3.up * _bob.Offset.Y; Focus = head;
                _position = eye; camera.transform.position = eye; camera.transform.rotation = rotation * Quaternion.Euler(0f, 0f, Mathf.Rad2Deg * Roll); return;
            }
            Focus = ZoomTarget ?? GameSpace.ToUnity(gamePosition) + Vector3.up * HeadHeight; Roll = 0f;
            var back = -(Quaternion.Euler(Pitch, Yaw, 0f) * Vector3.forward); var distance = ZoomTarget.HasValue ? ZoomDistance : Distance;
            if (Terrain != null && Terrain.Map != null && Terrain.Raycast(new Ray(Focus, back), Distance) is { } hit) distance = Mathf.Max(MinDistance * .5f, Vector3.Distance(Focus, hit) - .25f);
            var wanted = Focus + back * distance;
            if (Terrain != null && Terrain.Map != null) { var game = GameSpace.ToGame(wanted); var ground = Terrain.Map.SampleHeight(game.x, game.z) + ClearGround; if (wanted.y < ground && Terrain.Map.SeaLevel < ground) wanted.y = ground; }
            _position = snap ? wanted : Vector3.Lerp(_position, wanted, 1f - Mathf.Exp(-18f * Time.deltaTime));
            if (snap || Time.deltaTime <= 0f) _position = wanted;
            camera.transform.position = _position; camera.transform.rotation = Quaternion.LookRotation(Focus - _position, Vector3.up);
        }
    }
}
