using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// The server, the shared rules and the content of the islands live in "game space", the right-handed space of the Godot client (x right, y up, z toward the viewer).
    /// Unity is left-handed, so the world is drawn with the z axis flipped: a place (x, y, z) of the game is (x, y, -z) in Unity. With that, a building, a sign or a model
    /// looks exactly as it did in Godot (nothing is mirrored), and meshes keep their winding. Everything the server says goes through here on its way to the screen;
    /// anything sent back to the server (where a person clicked, where they walk) goes through the inverse.
    /// </summary>
    public static class GameSpace
    {
        public static Vector3 ToUnity(float x, float y, float z) => new Vector3(x, y, -z);
        public static Vector3 ToUnity(Vector3 game) => new Vector3(game.x, game.y, -game.z);
        public static Vector3 ToGame(Vector3 unity) => new Vector3(unity.x, unity.y, -unity.z);

        /// <summary>The orientation of a rotation given as Euler angles in degrees in the order the game uses (Y, then X, then Z), as a Unity rotation of the flipped world.</summary>
        public static Quaternion RotationToUnity(float xDegrees, float yDegrees, float zDegrees) => QuaternionToUnity(Quaternion.Euler(xDegrees, yDegrees, zDegrees));

        /// <summary>A quaternion of game space (the numbers the server sends) as a Unity rotation: reflecting z turns (x, y, z, w) into (-x, -y, z, w).</summary>
        public static Quaternion QuaternionToUnity(Quaternion game) => new Quaternion(-game.x, -game.y, game.z, game.w);

        public static Quaternion QuaternionToGame(Quaternion unity) => QuaternionToUnity(unity); // a reflection is its own inverse
    }
}
