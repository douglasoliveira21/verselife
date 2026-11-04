using Godot;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// Simplification of the geometry of a custom body (a level of detail made of fewer triangles). The engine's own simplifier (the one the editor uses to
/// make LODs when it imports a model) is run on every surface of a model, keeping its vertices, so skinning and textures stay as they are and only
/// the index buffer gets shorter. The result is cached by model and surface, so a crowd of the same body simplifies it once. Where the engine has no
/// simplifier, or a surface cannot be simplified, that surface is left as it was: more triangles, never a broken body.
/// </summary>
public static class ModelLod
{
    /// <summary>Tests: below how many triangles a model is left alone (null: <see cref="LifeVerse.Shared.Avatar.AvatarDetailPlanner.ReducedFloor"/>).</summary>
    public static int? FloorOverride { get; set; }

    private static readonly Dictionary<string, int[]> Cache = [];

    /// <summary>False once a surface was big enough to simplify and the engine gave no simplified version at all (no simplifier in this build).</summary>
    public static bool Available { get; private set; } = true;

    private static IEnumerable<MeshInstance3D> Meshes(Node node)
    {
        if (node is MeshInstance3D mesh) yield return mesh;
        foreach (var child in node.GetChildren()) foreach (var found in Meshes(child)) yield return found;
    }

    private static int Triangles(ArrayMesh mesh, int surface)
    {
        if (mesh.SurfaceGetPrimitiveType(surface) != Mesh.PrimitiveType.Triangles) return 0;
        var indices = mesh.SurfaceGetArrayIndexLen(surface);
        return (indices > 0 ? indices : mesh.SurfaceGetArrayLen(surface)) / 3;
    }

    /// <summary>The triangles drawn by every mesh under a node.</summary>
    public static int CountTriangles(Node root)
    {
        var total = 0;
        foreach (var instance in Meshes(root))
            if (instance.Mesh is ArrayMesh array) for (var surface = 0; surface < array.GetSurfaceCount(); surface++) total += Triangles(array, surface);
            else if (instance.Mesh is { } other) total += other.GetFaces().Length / 3;
        return total;
    }

    /// <summary>
    /// Simplifies a surface: keeps the triangles the engine's simplifier leaves for a target (a fraction of the original). Returns the original indices when
    /// nothing smaller can be made.
    /// </summary>
    public static int[] SimplifyIndices(Godot.Collections.Array arrays, Material? material, float ratio, string key)
    {
        var original = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        if (original.Length < 9) return original;
        var cacheKey = $"{key}|{ratio:0.###}";
        if (Cache.TryGetValue(cacheKey, out var known)) return known;
        var target = Math.Max(3, (int)(original.Length * ratio) / 3 * 3);
        var best = original;
        try
        {
            var importer = new ImporterMesh();
            importer.AddSurface(Mesh.PrimitiveType.Triangles, arrays, null, null, material);
            importer.GenerateLods(60f, 25f, []);
            var lods = importer.GetSurfaceLodCount(0);
            if (lods == 0) Available = false;
            for (var lod = 0; lod < lods; lod++)
            {
                var candidate = importer.GetSurfaceLodIndices(0, lod);
                if (candidate.Length >= 3 && candidate.Length < best.Length && (best.Length == original.Length || Math.Abs(candidate.Length - target) < Math.Abs(best.Length - target))) best = candidate;
            }
            if (lods > 0) Available = true;
        }
        catch (Exception exception) when (exception is InvalidCastException or ArgumentException or NullReferenceException) { best = original; }
        if (Cache.Count > 2000) Cache.Clear();
        Cache[cacheKey] = best;
        return best;
    }

    /// <summary>A copy of a mesh with every surface simplified to roughly the given fraction of its triangles (a mesh with morph targets is returned as it is).</summary>
    public static ArrayMesh SimplifyMesh(ArrayMesh source, float ratio, string key)
    {
        if (source.GetBlendShapeCount() > 0) return source;
        var rebuilt = new ArrayMesh();
        for (var surface = 0; surface < source.GetSurfaceCount(); surface++)
        {
            var arrays = source.SurfaceGetArrays(surface); var material = source.SurfaceGetMaterial(surface);
            if (source.SurfaceGetPrimitiveType(surface) == Mesh.PrimitiveType.Triangles && source.SurfaceGetArrayIndexLen(surface) > 0) arrays[(int)Mesh.ArrayType.Index] = SimplifyIndices(arrays, material, ratio, $"{key}|{surface}");
            rebuilt.AddSurfaceFromArrays(source.SurfaceGetPrimitiveType(surface), arrays);
            var added = rebuilt.GetSurfaceCount() - 1; rebuilt.SurfaceSetMaterial(added, material); rebuilt.SurfaceSetName(added, source.SurfaceGetName(surface));
        }
        return rebuilt;
    }

    /// <summary>
    /// Replaces the meshes of a model with simplified ones, unless the whole model is small already. Returns the triangles before and after.
    /// </summary>
    public static (int Before, int After) Simplify(Node root, float ratio, string key)
    {
        var before = CountTriangles(root);
        if (before <= (FloorOverride ?? LifeVerse.Shared.Avatar.AvatarDetailPlanner.ReducedFloor)) return (before, before);
        var number = 0;
        foreach (var instance in Meshes(root))
            if (instance.Mesh is ArrayMesh mesh) instance.Mesh = SimplifyMesh(mesh, ratio, $"{key}|{instance.Name}|{number++}");
        return (before, CountTriangles(root));
    }
}
