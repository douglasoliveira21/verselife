using Godot;

namespace LifeVerse.Client.Scripts;

public partial class BuildObjectView
{
    /// <summary>
    /// The tangents of a procedural face (four numbers per vertex), so a normal map lights the surface the right way after any change of shape: each triangle gives the direction
    /// in which U grows, the vertices share the sum of their triangles, and the result is made perpendicular to the normal with the sign of the bitangent in the fourth number.
    /// </summary>
    public static float[] TangentsOf(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] indices)
    {
        var tangent = new Vector3[vertices.Length]; var bitangent = new Vector3[vertices.Length];
        for (var t = 0; t + 2 < indices.Length; t += 3)
        {
            int a = indices[t], b = indices[t + 1], c = indices[t + 2];
            var e1 = vertices[b] - vertices[a]; var e2 = vertices[c] - vertices[a]; var d1 = uvs[b] - uvs[a]; var d2 = uvs[c] - uvs[a];
            var det = d1.X * d2.Y - d2.X * d1.Y; if (Mathf.Abs(det) < 1e-12f) continue;
            var inverse = 1f / det; var tan = (e1 * d2.Y - e2 * d1.Y) * inverse; var bit = (e2 * d1.X - e1 * d2.X) * inverse;
            foreach (var i in new[] { a, b, c }) { tangent[i] += tan; bitangent[i] += bit; }
        }
        var result = new float[vertices.Length * 4];
        for (var i = 0; i < vertices.Length; i++)
        {
            var n = normals[i]; var t = tangent[i] - n * n.Dot(tangent[i]);
            if (t.LengthSquared() < 1e-12f) t = Mathf.Abs(n.Y) < .9f ? Vector3.Up.Cross(n) : Vector3.Right.Cross(n);
            t = t.Normalized(); var w = n.Cross(t).Dot(bitangent[i]) < 0 ? -1f : 1f;
            result[i * 4] = t.X; result[i * 4 + 1] = t.Y; result[i * 4 + 2] = t.Z; result[i * 4 + 3] = w;
        }
        return result;
    }
}
