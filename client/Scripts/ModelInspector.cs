using Godot;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Scripts;

/// What the import window shows about a model before it is uploaded: its real size, triangles, materials, textures and parts,
/// whether it fits the limits, and a ready-to-show copy of the scene for the preview.
public sealed record ModelReport(bool Valid, string? Error, Vector3 Size, int Triangles, int Materials, int Textures, int Parts, int Animations, Node3D? Scene);

public static class ModelInspector
{
    public static ModelReport Inspect(byte[] bytes)
    {
        ModelReport Fail(string error) => new(false, error, Vector3.Zero, 0, 0, 0, 0, 0, null);
        if (bytes.Length == 0) return Fail("O arquivo está vazio.");
        if (bytes.Length > ModelLimits.MaxBytes) return Fail(ModelLimits.Check(1, 0, 0, 0, bytes.Length)!);
        var document = new GltfDocument(); var state = new GltfState();
        // no base path: a model can only use what it carries inside, never files next to it
        if (document.AppendFromBuffer(bytes, string.Empty, state) != Error.Ok) return Fail("O arquivo não é um modelo glTF/GLB válido.");
        if (document.GenerateScene(state) is not Node3D scene) return Fail("O modelo não tem uma cena para mostrar.");

        var triangles = 0; var parts = 0;
        var materials = new HashSet<Material>(); var textures = new HashSet<Texture2D>();
        foreach (var node in All(scene))
        {
            parts++;
            if (node is not MeshInstance3D { Mesh: { } mesh }) continue;
            triangles += mesh.GetFaces().Length / 3;
            for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.SurfaceGetMaterial(surface) is not { } material) continue;
                materials.Add(material);
                if (material is BaseMaterial3D standard)
                    foreach (var slot in new[] { BaseMaterial3D.TextureParam.Albedo, BaseMaterial3D.TextureParam.Normal, BaseMaterial3D.TextureParam.Metallic, BaseMaterial3D.TextureParam.Roughness, BaseMaterial3D.TextureParam.Emission, BaseMaterial3D.TextureParam.AmbientOcclusion })
                        if (standard.GetTexture(slot) is { } texture) textures.Add(texture);
            }
        }
        var animations = scene.GetChildren().OfType<AnimationPlayer>().Sum(x => x.GetAnimationList().Length);
        var bounds = ModelCache.Bounds(scene);
        if (bounds is not { } box || box.Size.LengthSquared() < 1e-8f) { scene.Free(); return Fail("O modelo não tem forma visível."); }
        var error = ModelLimits.Check(triangles, materials.Count, textures.Count, parts, bytes.Length);
        return new ModelReport(error is null, error, box.Size, triangles, materials.Count, textures.Count, parts, animations, scene);
    }

    private static IEnumerable<Node> All(Node root)
    {
        yield return root;
        foreach (var child in root.GetChildren()) foreach (var node in All(child)) yield return node;
    }
}
