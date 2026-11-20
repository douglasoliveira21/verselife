using System;
using System.Collections.Generic;
using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// Makes a lighter copy of a mesh for far away: the vertices are gathered in the cells of a regular grid (the vertices of one cell become one, at their average), triangles that collapse are dropped and the grid is made coarser until
    /// the copy has about the wanted share of the triangles. Keeps the sub-meshes (so the materials still fit), the normals and the texture coordinates. Meant for something seen small; up close the full mesh is used.
    /// </summary>
    public static class MeshSimplifier
    {
        /// <summary>The meshes smaller than this are not worth a lighter copy.</summary>
        public const int MinTriangles = 3000;

        public static int TriangleCount(Mesh mesh) { var total = 0; for (var s = 0; s < mesh.subMeshCount; s++) total += (int)(mesh.GetIndexCount(s) / 3); return total; }

        /// <summary>A lighter copy with about <paramref name="ratio"/> of the triangles (0.05 to 1), or null when the mesh is too small, not readable or cannot be reduced.</summary>
        public static Mesh Simplify(Mesh source, float ratio)
        {
            if (source == null || !source.isReadable || ratio >= .98f) return null; ratio = Mathf.Clamp(ratio, .05f, .98f);
            var before = TriangleCount(source); if (before < MinTriangles) return null;
            var vertices = source.vertices; var normals = source.normals; var uvs = source.uv; var hasNormals = normals.Length == vertices.Length; var hasUv = uvs.Length == vertices.Length;
            var triangles = new int[source.subMeshCount][]; for (var s = 0; s < triangles.Length; s++) triangles[s] = source.GetTriangles(s);
            var size = source.bounds.size; var longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z)); if (longest < 1e-5f) return null;
            var target = before * ratio; Plan best = null;
            for (var resolution = 256f; resolution >= 4f; resolution *= .8f)
            {
                var plan = Cluster(vertices, hasNormals ? normals : null, hasUv ? uvs : null, triangles, source.bounds.min, longest / resolution); best = plan; if (plan.Triangles <= target) break;
            }
            if (best == null || best.Triangles >= before * .95f || best.Triangles == 0) return null;
            var count = best.Positions.Count; var outV = new Vector3[count]; var outN = hasNormals ? new Vector3[count] : null; var outU = hasUv ? new Vector2[count] : null;
            for (var i = 0; i < count; i++)
            {
                var weight = best.Weights[i]; outV[i] = best.Positions[i] / weight;
                if (hasNormals) outN[i] = (best.Normals[i] / weight).normalized;
                if (hasUv) outU[i] = best.Uvs[i] / weight;
            }
            var mesh = new Mesh { name = source.name + " (far)", indexFormat = count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.vertices = outV; if (hasNormals) mesh.normals = outN; if (hasUv) mesh.uv = outU; mesh.subMeshCount = triangles.Length;
            for (var s = 0; s < triangles.Length; s++) mesh.SetTriangles(best.Indices[s], s, false);
            mesh.RecalculateBounds(); if (!hasNormals) mesh.RecalculateNormals(); mesh.RecalculateTangents(); return mesh;
        }

        private sealed class Plan { public List<Vector3> Positions = new List<Vector3>(), Normals = new List<Vector3>(); public List<Vector2> Uvs = new List<Vector2>(); public List<float> Weights = new List<float>(); public List<int>[] Indices; public int Triangles; }

        private static Plan Cluster(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[][] triangles, Vector3 origin, float cell)
        {
            var plan = new Plan { Indices = new List<int>[triangles.Length] }; var map = new int[vertices.Length]; var cells = new Dictionary<long, int>();
            for (var i = 0; i < vertices.Length; i++)
            {
                var p = (vertices[i] - origin) / cell; long x = (long)Mathf.Floor(p.x), y = (long)Mathf.Floor(p.y), z = (long)Mathf.Floor(p.z); var key = (x & 0x1FFFFF) | ((y & 0x1FFFFF) << 21) | ((z & 0x1FFFFF) << 42);
                if (!cells.TryGetValue(key, out var slot)) { slot = plan.Positions.Count; cells[key] = slot; plan.Positions.Add(Vector3.zero); plan.Normals.Add(Vector3.zero); plan.Uvs.Add(Vector2.zero); plan.Weights.Add(0f); }
                map[i] = slot; plan.Positions[slot] += vertices[i]; plan.Weights[slot] += 1f; if (normals != null) plan.Normals[slot] += normals[i]; if (uvs != null) plan.Uvs[slot] += uvs[i];
            }
            for (var s = 0; s < triangles.Length; s++)
            {
                var list = new List<int>(); plan.Indices[s] = list; var t = triangles[s]; var seen = new HashSet<long>();
                for (var i = 0; i + 2 < t.Length; i += 3)
                {
                    int a = map[t[i]], b = map[t[i + 1]], c = map[t[i + 2]]; if (a == b || b == c || a == c) continue;
                    var low = Math.Min(a, Math.Min(b, c)); var high = Math.Max(a, Math.Max(b, c)); var mid = a + b + c - low - high; var id = ((long)low << 40) ^ ((long)mid << 20) ^ high; if (!seen.Add(id)) continue;   // the same corner points twice: drawn once
                    list.Add(a); list.Add(b); list.Add(c); plan.Triangles++;
                }
            }
            return plan;
        }

    }
}
