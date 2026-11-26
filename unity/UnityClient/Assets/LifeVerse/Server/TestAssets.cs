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
        /// <summary>The standard humanoid body of the game (Mixamo names) with the three bones of every finger added under each hand, so the fingers can be tried on a model read the real way.</summary>
        public static byte[] HumanoidWithFingers()
        {
            var glb = LifeVerse.Shared.Avatar.HumanoidTemplate.Build(new LifeVerse.Shared.Avatar.TemplateOptions { Naming = LifeVerse.Shared.Avatar.BoneNaming.Mixamo });
            var jsonLength = (int)BitConverter.ToUInt32(glb, 12); var json = System.Text.Encoding.UTF8.GetString(glb, 20, jsonLength).TrimEnd(); var root = System.Text.Json.Nodes.JsonNode.Parse(json); var nodes = (System.Text.Json.Nodes.JsonArray)root["nodes"];
            var names = new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" }; var spread = new[] { -.03f, .03f, .01f, -.01f, -.03f };
            foreach (var side in new[] { "Left", "Right" })
            {
                var handIndex = -1; for (var i = 0; i < nodes.Count; i++) if ((nodes[i]["name"]?.GetValue<string>() ?? "").EndsWith(side + "Hand")) handIndex = i; if (handIndex < 0) throw new InvalidOperationException("no " + side + "Hand in the template");
                for (var f = 0; f < 5; f++)
                {
                    var parent = handIndex;
                    for (var s = 1; s <= 3; s++)
                    {
                        var node = new System.Text.Json.Nodes.JsonObject { ["name"] = "mixamorig:" + side + "Hand" + names[f] + s, ["translation"] = new System.Text.Json.Nodes.JsonArray(s == 1 ? spread[f] : 0f, .04f, 0f) }; nodes.Add(node); var index = nodes.Count - 1;
                        var holder = nodes[parent]; var children = holder["children"] as System.Text.Json.Nodes.JsonArray ?? new System.Text.Json.Nodes.JsonArray(); if (holder["children"] == null) holder["children"] = children; children.Add(index); parent = index;
                    }
                }
            }
            var newJson = System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()); var padded = (newJson.Length + 3) / 4 * 4; Array.Resize(ref newJson, padded); for (var i = System.Text.Encoding.UTF8.GetByteCount(root.ToJsonString()); i < padded; i++) newJson[i] = 0x20;
            var rest = new byte[glb.Length - (20 + jsonLength)]; Array.Copy(glb, 20 + jsonLength, rest, 0, rest.Length);
            using var file = new MemoryStream(); using var output = new BinaryWriter(file); output.Write(0x46546C67u); output.Write(2u); output.Write((uint)(12 + 8 + newJson.Length + rest.Length)); output.Write((uint)newJson.Length); output.Write(0x4E4F534Au); output.Write(newJson); output.Write(rest); return file.ToArray();
        }

        /// <summary>A GLB with extras put in its asset (where worn items say their slot: {"lifeverse":{"slot":"hat","bone":"head"}}).</summary>
        public static byte[] WithAssetExtras(byte[] glb, string extrasJson)
        {
            var jsonLength = (int)BitConverter.ToUInt32(glb, 12); var root = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Encoding.UTF8.GetString(glb, 20, jsonLength).TrimEnd(' ', (char)0)).AsObject();
            var asset = root["asset"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject { ["version"] = "2.0" }; asset["extras"] = System.Text.Json.Nodes.JsonNode.Parse(extrasJson); root["asset"] = asset;
            var text = root.ToJsonString(); var newJson = System.Text.Encoding.UTF8.GetBytes(text); var padded = (newJson.Length + 3) / 4 * 4; var json = new byte[padded]; Array.Copy(newJson, json, newJson.Length); for (var i = newJson.Length; i < padded; i++) json[i] = 0x20;
            var rest = new byte[glb.Length - (20 + jsonLength)]; Array.Copy(glb, 20 + jsonLength, rest, 0, rest.Length);
            using var file = new System.IO.MemoryStream(); using var output = new System.IO.BinaryWriter(file); output.Write(0x46546C67u); output.Write(2u); output.Write((uint)(12 + 8 + json.Length + rest.Length)); output.Write((uint)json.Length); output.Write(0x4E4F534Au); output.Write(json); output.Write(rest); return file.ToArray();
        }

        /// <summary>The same box with one clip, "Spin": two seconds turning a quarter about the vertical.</summary>
        public static byte[] AnimatedGlb() => Glb(true);

        public static byte[] Glb(bool animated = false)
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
            if (animated) { writer.Write(0f); writer.Write(2f); foreach (var q in new float[] { 0, 0, 0, 1, 0, .70710678f, 0, .70710678f }) writer.Write(q); }
            var binary = bin.ToArray();
            var json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"mesh\":0}],\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"indices\":1}]}],"
                + "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":8,\"type\":\"VEC3\",\"min\":[-2,-1,-1],\"max\":[2,1,1]},{\"bufferView\":1,\"componentType\":5123,\"count\":36,\"type\":\"SCALAR\"}"
                + (animated ? ",{\"bufferView\":2,\"componentType\":5126,\"count\":2,\"type\":\"SCALAR\",\"min\":[0],\"max\":[2]},{\"bufferView\":3,\"componentType\":5126,\"count\":2,\"type\":\"VEC4\"}" : "") + "],"
                + "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":96,\"target\":34962},{\"buffer\":0,\"byteOffset\":96,\"byteLength\":72,\"target\":34963}"
                + (animated ? ",{\"buffer\":0,\"byteOffset\":168,\"byteLength\":8},{\"buffer\":0,\"byteOffset\":176,\"byteLength\":32}" : "") + "],\"buffers\":[{\"byteLength\":" + binary.Length + "}]"
                + (animated ? ",\"animations\":[{\"name\":\"Spin\",\"samplers\":[{\"input\":2,\"output\":3,\"interpolation\":\"LINEAR\"}],\"channels\":[{\"sampler\":0,\"target\":{\"node\":0,\"path\":\"rotation\"}}]}]" : "") + "}";
            var jsonBytes = Encoding.UTF8.GetBytes(json); var padded = (jsonBytes.Length + 3) / 4 * 4; Array.Resize(ref jsonBytes, padded); for (var i = Encoding.UTF8.GetByteCount(json); i < padded; i++) jsonBytes[i] = 0x20;
            using var file = new MemoryStream(); using var output = new BinaryWriter(file);
            output.Write(0x46546C67u); output.Write(2u); output.Write((uint)(12 + 8 + jsonBytes.Length + 8 + binary.Length));
            output.Write((uint)jsonBytes.Length); output.Write(0x4E4F534Au); output.Write(jsonBytes);
            output.Write((uint)binary.Length); output.Write(0x004E4942u); output.Write(binary);
            return file.ToArray();
        }
    }
}
