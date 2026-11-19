using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace LifeVerse.Unity.Server
{
    /// <summary>Small files made by hand for checking the asset path (textures and imported models) without needing anything from the real API.</summary>
    public static class TestAssets
    {
        /// <summary>A PNG of one flat colour.</summary>
        public static byte[] Png(Color32 colour, int size = 8)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size]; for (var i = 0; i < pixels.Length; i++) pixels[i] = colour; texture.SetPixels32(pixels); texture.Apply();
            var bytes = texture.EncodeToPNG(); UnityEngine.Object.DestroyImmediate(texture); return bytes;
        }

        /// <summary>A binary glTF of a box 4 long (x), 2 high and 2 deep with no material, centred on its origin: the loader must shrink it to one metre on the long side.</summary>
        public static byte[] Glb()
        {
            var positions = new float[]
            {
                -2, -1, -1,  2, -1, -1,  2, 1, -1,  -2, 1, -1,
                -2, -1,  1,  2, -1,  1,  2, 1,  1,  -2, 1,  1,
            };
            var indices = new ushort[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5 };
            using var bin = new MemoryStream(); using var writer = new BinaryWriter(bin);
            foreach (var value in positions) writer.Write(value);
            foreach (var value in indices) writer.Write(value);
            var binary = bin.ToArray();
            var json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"mesh\":0}],\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"indices\":1}]}],"
                + "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":8,\"type\":\"VEC3\",\"min\":[-2,-1,-1],\"max\":[2,1,1]},{\"bufferView\":1,\"componentType\":5123,\"count\":36,\"type\":\"SCALAR\"}],"
                + "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":96,\"target\":34962},{\"buffer\":0,\"byteOffset\":96,\"byteLength\":72,\"target\":34963}],\"buffers\":[{\"byteLength\":" + binary.Length + "}]}";
            var jsonBytes = Encoding.UTF8.GetBytes(json); var padded = (jsonBytes.Length + 3) / 4 * 4; Array.Resize(ref jsonBytes, padded); for (var i = Encoding.UTF8.GetByteCount(json); i < padded; i++) jsonBytes[i] = 0x20;
            using var file = new MemoryStream(); using var output = new BinaryWriter(file);
            output.Write(0x46546C67u); output.Write(2u); output.Write((uint)(12 + 8 + jsonBytes.Length + 8 + binary.Length));
            output.Write((uint)jsonBytes.Length); output.Write(0x4E4F534Au); output.Write(jsonBytes);
            output.Write((uint)binary.Length); output.Write(0x004E4942u); output.Write(binary);
            return file.ToArray();
        }
    }
}
