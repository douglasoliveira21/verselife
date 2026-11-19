using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace LifeVerse.Unity.Avatar
{
    /// <summary>
    /// The body that comes with the game: "the man of LifeVerse" (base model <c>lifeverse_man</c>), a GLB of about 50 MB that every client has, so nobody has to download it and everybody sees it.
    /// It is the same file the Godot client carries (client/Assets/avatars/lifeverse_man.bin); a build copies it to StreamingAssets/avatars (see the BuiltInBodiesCopy build step), and the
    /// game reads it from there once and keeps the parsed model: every person with this body is made from that one import (the materials are copied per person, so skin tints do not leak).
    /// </summary>
    public static class BuiltInBodies
    {
        public const string RelativePath = "avatars/lifeverse_man.bin";
        private static Task<GLTFast.GltfImport> _import;

        public static string Path => System.IO.Path.Combine(Application.streamingAssetsPath, RelativePath);
        public static bool Available => File.Exists(Path);

        /// <summary>Reads and parses the file (once; the answer is shared). Null when the file is not there or cannot be read.</summary>
        public static Task<GLTFast.GltfImport> ImportAsync()
        {
            if (_import != null) return _import;
            return _import = LoadAsync();
        }

        private static async Task<GLTFast.GltfImport> LoadAsync()
        {
            if (!Available) return null;
            try
            {
                var bytes = await Task.Run(() => File.ReadAllBytes(Path)); var import = new GLTFast.GltfImport();
                return await import.LoadGltfBinary(bytes, new Uri("file:///lifeverse_man.glb")) ? import : null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { Debug.LogWarning("[LifeVerse] corpo do jogo: " + exception.Message); return null; }
        }

        public static void Forget() { _import = null; }
    }
}
