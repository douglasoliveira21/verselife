using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using LifeVerse.Shared.Building;
using UnityEngine;

namespace LifeVerse.Unity.Models
{
    /// <summary>What an imported model is made of, read from the file before anything is uploaded: how big it is, its triangles, materials, pictures, parts and animations, and whether it is within the limits the platform keeps.</summary>
    public sealed class ModelReport
    {
        public bool Valid; public string Error; public Vector3 Size; public int Triangles, Materials, Textures, Parts, Animations; public long Bytes;
    }

    public static class ModelInspector
    {
        /// <summary>The JSON of a GLB (the first chunk), or null when the bytes are not a GLB.</summary>
        public static JsonObject JsonOf(byte[] glb)
        {
            if (glb == null || glb.Length < 20 || glb[0] != 'g' || glb[1] != 'l' || glb[2] != 'T' || glb[3] != 'F') return null;
            var length = BitConverter.ToInt32(glb, 12); if (length <= 0 || 20 + length > glb.Length || BitConverter.ToUInt32(glb, 16) != 0x4E4F534A) return null;
            try { return JsonNode.Parse(Encoding.UTF8.GetString(glb, 20, length)) as JsonObject; } catch (System.Text.Json.JsonException) { return null; }
        }

        public static ModelReport Inspect(byte[] glb)
        {
            var report = new ModelReport { Bytes = glb?.LongLength ?? 0 }; var root = JsonOf(glb);
            if (root == null || root["asset"]?["version"]?.GetValue<string>()?.StartsWith("2") != true) { report.Error = "O arquivo não é um modelo glTF/GLB 2.0 válido."; return report; }
            var meshes = root["meshes"] as JsonArray ?? new JsonArray(); var accessors = root["accessors"] as JsonArray ?? new JsonArray(); var nodes = root["nodes"] as JsonArray ?? new JsonArray();
            report.Materials = (root["materials"] as JsonArray)?.Count ?? 0; report.Textures = (root["images"] as JsonArray)?.Count ?? 0; report.Parts = nodes.Count; report.Animations = (root["animations"] as JsonArray)?.Count ?? 0;
            var triangles = new int[meshes.Count];
            for (var m = 0; m < meshes.Count; m++)
                foreach (var primitive in (meshes[m]?["primitives"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                {
                    var mode = primitive["mode"]?.GetValue<int>() ?? 4; if (mode != 4) continue;   // only triangle lists are counted (what the exporters write)
                    var count = Count(accessors, primitive["indices"]?.GetValue<int>() ?? -1); if (count < 0) count = Count(accessors, primitive["attributes"]?["POSITION"]?.GetValue<int>() ?? -1); if (count > 0) triangles[m] += count / 3;
                }
            // what each node shows is counted once per place it is used; the size is the box around every mesh seen through its node transforms
            var scenes = root["scenes"] as JsonArray; var start = new List<int>(); var scene = scenes != null && scenes.Count > 0 ? (scenes[root["scene"]?.GetValue<int>() ?? 0] as JsonObject) : null;
            if (scene?["nodes"] is JsonArray roots) foreach (var n in roots) start.Add(n.GetValue<int>()); else for (var i = 0; i < nodes.Count; i++) start.Add(i);
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var max = new Vector3(float.MinValue, float.MinValue, float.MinValue); var seen = 0;
            void Walk(int index, Matrix4x4 parent, int depth)
            {
                if (index < 0 || index >= nodes.Count || depth > 64 || ++seen > 20000) return; var node = nodes[index] as JsonObject; if (node == null) return; var world = parent * Local(node);
                if (node["mesh"] != null) { var meshIndex = node["mesh"].GetValue<int>(); if (meshIndex >= 0 && meshIndex < meshes.Count) { report.Triangles += triangles[meshIndex]; foreach (var primitive in (meshes[meshIndex]["primitives"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()) Grow(accessors, primitive["attributes"]?["POSITION"]?.GetValue<int>() ?? -1, world, ref min, ref max); } }
                if (node["children"] is JsonArray children) foreach (var child in children) Walk(child.GetValue<int>(), world, depth + 1);
            }
            foreach (var index in start) Walk(index, Matrix4x4.identity, 0);
            report.Size = min.x <= max.x ? max - min : Vector3.zero;
            report.Error = ModelLimits.Check(report.Triangles, report.Materials, report.Textures, report.Parts, report.Bytes); report.Valid = report.Error == null; return report;
        }

        private static int Count(JsonArray accessors, int index) => index >= 0 && index < accessors.Count ? accessors[index]?["count"]?.GetValue<int>() ?? -1 : -1;

        private static Matrix4x4 Local(JsonObject node)
        {
            if (node["matrix"] is JsonArray m && m.Count == 16) { var v = new float[16]; for (var i = 0; i < 16; i++) v[i] = (float)(m[i]?.GetValue<double>() ?? 0); var r = new Matrix4x4(); for (var i = 0; i < 16; i++) r[i % 4, i / 4] = v[i]; return r; }   // glTF writes the matrix by columns
            Vector3 T = Vector3.zero, S = Vector3.one; var Q = Quaternion.identity;
            if (node["translation"] is JsonArray t && t.Count == 3) T = new Vector3(F(t[0]), F(t[1]), F(t[2])); if (node["scale"] is JsonArray s && s.Count == 3) S = new Vector3(F(s[0]), F(s[1]), F(s[2])); if (node["rotation"] is JsonArray q && q.Count == 4) Q = new Quaternion(F(q[0]), F(q[1]), F(q[2]), F(q[3]));
            return Matrix4x4.TRS(T, Q, S);
        }

        private static float F(JsonNode n) => (float)(n?.GetValue<double>() ?? 0);

        private static void Grow(JsonArray accessors, int index, Matrix4x4 world, ref Vector3 min, ref Vector3 max)
        {
            if (index < 0 || index >= accessors.Count || !(accessors[index]?["min"] is JsonArray lo) || !(accessors[index]?["max"] is JsonArray hi) || lo.Count < 3 || hi.Count < 3) return;
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? F(lo[0]) : F(hi[0]), (i & 2) == 0 ? F(lo[1]) : F(hi[1]), (i & 4) == 0 ? F(lo[2]) : F(hi[2])); var p = world.MultiplyPoint3x4(corner); min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            }
        }

        /// <summary>A size for people ("1,2 × 0,8 × 2 m").</summary>
        public static string SizeText(Vector3 size) => size.x.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR")) + " × " + size.y.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR")) + " × " + size.z.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR")) + " m";
    }
}
