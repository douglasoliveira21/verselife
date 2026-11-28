using UnityEngine;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// What the person reads and types follows Second Life: X east, Y north and Z up, rotations as llEuler2Rot takes them (a turn around Z, then Y, then X of the world). The island keeps
    /// the numbers the way it always did (Y up, −Z north); only the screen changes. The change of base is (x, y, z) → (x, −z, y), which is a proper rotation, so a quaternion changes the
    /// same way as a point.
    /// </summary>
    public static class BuildAxes
    {
        public static Vector3 ToViewerPosition(Vector3 game) => new Vector3(game.x, Clean(-game.z), game.y);
        public static Vector3 ToGamePosition(Vector3 viewer) => new Vector3(viewer.x, viewer.z, Clean(-viewer.y));
        /// <summary>Sizes along the object's own sides (no signs: only which side is which).</summary>
        public static Vector3 ToViewerSize(Vector3 game) => new Vector3(game.x, game.z, game.y);
        public static Vector3 ToGameSize(Vector3 viewer) => new Vector3(viewer.x, viewer.z, viewer.y);

        /// <summary>The island's rotation (degrees, the order of Quaternion.Euler) as the three angles the viewer shows, from 0 to 360.</summary>
        public static Vector3 ToViewerRotation(Vector3 gameDegrees)
        {
            var q = Quaternion.Euler(gameDegrees.x, gameDegrees.y, gameDegrees.z); var viewer = new Quaternion(q.x, -q.z, q.y, q.w);
            // viewer = Rx(roll) · Ry(pitch) · Rz(yaw)
            var m = Matrix4x4.Rotate(viewer); var pitch = Mathf.Asin(Mathf.Clamp(m.m02, -1f, 1f)); float roll, yaw;
            if (Mathf.Abs(m.m02) < .999999f) { roll = Mathf.Atan2(-m.m12, m.m22); yaw = Mathf.Atan2(-m.m01, m.m00); }
            else { roll = Mathf.Atan2(m.m21, m.m11); yaw = 0f; }   // looking straight along Z: only the sum of the other two is known
            return new Vector3(Turn(roll * Mathf.Rad2Deg), Turn(pitch * Mathf.Rad2Deg), Turn(yaw * Mathf.Rad2Deg));
        }

        /// <summary>The viewer's three angles back as the island keeps them, from −180 to 180.</summary>
        public static Vector3 ToGameRotation(Vector3 viewerDegrees)
        {
            var s = Quaternion.AngleAxis(viewerDegrees.x, Vector3.right) * Quaternion.AngleAxis(viewerDegrees.y, Vector3.up) * Quaternion.AngleAxis(viewerDegrees.z, Vector3.forward);
            var game = new Quaternion(s.x, s.z, -s.y, s.w).eulerAngles; return new Vector3(Wrap(game.x), Wrap(game.y), Wrap(game.z));
        }

        private static float Clean(float v) => v == 0f ? 0f : v;   // no "-0" on screen
        private static float Turn(float degrees) { var d = Mathf.Repeat(Mathf.Round(degrees * 1000f) / 1000f, 360f); return d > 359.999f ? 0f : d; }
        private static float Wrap(float degrees) { var d = Mathf.Repeat(degrees + 180f, 360f) - 180f; return Mathf.Abs(d) < .0005f ? 0f : Mathf.Round(d * 1000f) / 1000f; }
    }
}
