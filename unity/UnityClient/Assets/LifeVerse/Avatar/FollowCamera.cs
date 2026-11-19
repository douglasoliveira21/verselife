using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LifeVerse.Unity.Avatar
{
    /// <summary>
    /// The third-person camera: it orbits the player's head (hold the right mouse button and move to turn it, the wheel to come closer or go away), stays out of the ground
    /// (it is pulled in when the terrain is between it and the player and never goes under it) and looks at the head. <see cref="Yaw"/> is the heading the camera looks
    /// along (Unity degrees about the vertical): walking forward goes that way. First person, the build camera and the photo camera come later.
    /// </summary>
    public sealed class FollowCamera : MonoBehaviour
    {
        public Camera Camera; public TerrainView Terrain;
        public float Yaw, Pitch = 14f, Distance = 4.6f;
        public const float MinDistance = 1.2f, MaxDistance = 14f, HeadHeight = 1.55f, ClearGround = .35f;
        /// <summary>When false the mouse and the wheel are ignored (a menu is open, or a test is driving it).</summary>
        public bool Interactive = true;
        public Vector3 Focus { get; private set; }
        private Vector3 _position;

        public void Snap(Vector3 gamePosition) { Place(gamePosition, true); }
        public void Follow(Vector3 gamePosition) { Place(gamePosition, false); }

        private void LateUpdate()
        {
            if (!Interactive) return;
            var mouse = Mouse.current; if (mouse == null) return;
            if (mouse.rightButton.isPressed) { var delta = mouse.delta.ReadValue(); Yaw += delta.x * .15f; Pitch = Mathf.Clamp(Pitch - delta.y * .15f, -60f, 80f); }
            var wheel = mouse.scroll.ReadValue().y; if (Mathf.Abs(wheel) > .01f) Distance = Mathf.Clamp(Distance - Mathf.Sign(wheel) * .6f, MinDistance, MaxDistance);
        }

        private void Place(Vector3 gamePosition, bool snap)
        {
            var camera = Camera != null ? Camera : UnityEngine.Camera.main; if (camera == null) return;
            Focus = GameSpace.ToUnity(gamePosition) + Vector3.up * HeadHeight;
            var back = -(Quaternion.Euler(Pitch, Yaw, 0f) * Vector3.forward); var distance = Distance;
            if (Terrain != null && Terrain.Map != null && Terrain.Raycast(new Ray(Focus, back), Distance) is { } hit) distance = Mathf.Max(MinDistance * .5f, Vector3.Distance(Focus, hit) - .25f);
            var wanted = Focus + back * distance;
            if (Terrain != null && Terrain.Map != null) { var game = GameSpace.ToGame(wanted); var ground = Terrain.Map.SampleHeight(game.x, game.z) + ClearGround; if (wanted.y < ground && Terrain.Map.SeaLevel < ground) wanted.y = ground; }
            _position = snap ? wanted : Vector3.Lerp(_position, wanted, 1f - Mathf.Exp(-18f * Time.deltaTime));
            if (snap || Time.deltaTime <= 0f) _position = wanted;
            camera.transform.position = _position; camera.transform.rotation = Quaternion.LookRotation(Focus - _position, Vector3.up);
        }
    }
}
