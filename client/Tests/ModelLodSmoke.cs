using System.Security.Cryptography;
using Godot;
using LifeVerse.Client.Models;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Tests;

/// <summary>Simplified geometry for the reduced detail: a dense smooth mesh keeps a fraction of its triangles and its shape, small bodies are left alone, full detail never simplifies.</summary>
public partial class ModelLodSmoke : Node
{
    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static ArrayMesh Sphere(int radial, int rings)
    {
        var sphere = new SphereMesh { RadialSegments = radial, Rings = rings }; var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, sphere.GetMeshArrays()); return mesh;
    }

    private static int Triangles(ArrayMesh mesh) => mesh.SurfaceGetArrayIndexLen(0) / 3;

    public override async void _Ready()
    {
        try
        {
            // ---- the simplifier itself
            var dense = Sphere(96, 48); var before = Triangles(dense);
            Require(before > 8_000, $"the test mesh is dense: {before}");
            var light = ModelLod.SimplifyMesh(dense, AvatarDetailPlanner.ReducedRatio, "sphere");
            Require(ModelLod.Available, "the engine has a mesh simplifier");
            var after = Triangles(light);
            Require(after < before * .6f && after > before * .1f, $"it keeps roughly the asked fraction: {before} -> {after}");
            var a = dense.GetAabb().Size; var b = light.GetAabb().Size;
            Require((a - b).Length() < a.Length() * .06f, $"the shape is kept: {a} / {b}");
            var arrays = light.SurfaceGetArrays(0); var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
            Require(arrays[(int)Mesh.ArrayType.Normal].AsVector3Array().Length == vertices && arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array().Length == vertices, "normals and texture coordinates are still there for every vertex");
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array(); Require(indices.All(i => i >= 0 && i < vertices) && indices.Length % 3 == 0, "every triangle points at real vertices");
            Require(Triangles(ModelLod.SimplifyMesh(dense, AvatarDetailPlanner.ReducedRatio, "sphere")) == after, "the same body simplifies to the same thing (cached)");
            var lighter = ModelLod.SimplifyMesh(dense, .1f, "sphere"); Require(Triangles(lighter) < after, $"a smaller fraction gives fewer triangles: {Triangles(lighter)} < {after}");

            // ---- a model: all of its meshes, only above the floor
            var root = new Node3D(); root.AddChild(new MeshInstance3D { Name = "A", Mesh = Sphere(96, 48) }); root.AddChild(new MeshInstance3D { Name = "B", Mesh = Sphere(40, 20) });
            var total = ModelLod.CountTriangles(root);
            var (was, now) = ModelLod.Simplify(root, AvatarDetailPlanner.ReducedRatio, "model");
            Require(was == total && now < was * .6f && now > 0, $"a model is simplified as a whole: {was} -> {now}");
            var small = new Node3D(); small.AddChild(new MeshInstance3D { Name = "Small", Mesh = Sphere(16, 8) });
            var (s0, s1) = ModelLod.Simplify(small, AvatarDetailPlanner.ReducedRatio, "small"); Require(s0 == s1 && s0 > 0 && s0 <= AvatarDetailPlanner.ReducedFloor, "a model under the floor is left as it is");
            var morph = new ArrayMesh(); morph.AddBlendShape("smile");
            var morphArrays = new SphereMesh { RadialSegments = 96, Rings = 48 }.GetMeshArrays(); var shape = new Godot.Collections.Array(); shape.Resize((int)Mesh.ArrayType.Max);
            shape[(int)Mesh.ArrayType.Vertex] = morphArrays[(int)Mesh.ArrayType.Vertex]; shape[(int)Mesh.ArrayType.Normal] = morphArrays[(int)Mesh.ArrayType.Normal];
            morph.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, morphArrays, [shape]);
            Require(ReferenceEquals(ModelLod.SimplifyMesh(morph, .35f, "morph"), morph), "a mesh with morph targets keeps every triangle");

            // ---- the avatar: reduced detail simplifies the custom body, full detail does not
            var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); AddChild(rig); await Frames(4);
            var body = HumanoidTemplate.Build(); ModelLod.FloorOverride = 50;
            AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(body);
            AvatarAppearanceData Look() => new()
            {
                DisplayName = "Teste", CustomModelId = Guid.NewGuid(), CustomModelHash = HashOf(body), CustomModelScale = 1.8f / 1.78f, CustomModelTriangles = 30_000,
                EquippedItems = [new() { Slot = "top", ItemKey = "tee_basic" }, new() { Slot = "bottom", ItemKey = "pants_basic" }, new() { Slot = "shoes", ItemKey = "sneakers_basic" }]
            };
            rig.SetDetail(AvatarDetail.Reduced); rig.ApplyAppearance(Look()); await Frames(8);
            Require(rig.CustomBodyActive && rig.CustomBodyStatus == "ready", $"the body is drawn at reduced detail: {rig.CustomBodyStatus}");
            Require(rig.CustomBodyFileTriangles > 0 && rig.CustomBodyTriangles <= rig.CustomBodyFileTriangles, $"never more triangles than the file has: {rig.CustomBodyTriangles} / {rig.CustomBodyFileTriangles}");
            rig.SetDetail(AvatarDetail.Full); await Frames(8);
            Require(rig.CustomBodyActive && rig.CustomBodyTriangles == rig.CustomBodyFileTriangles && rig.CustomBodyGeometry == "full", $"at full detail the body keeps every triangle: {rig.CustomBodyTriangles} / {rig.CustomBodyGeometry}");
            Require(rig.BodyCost == 30_000 && rig.ReducedCost == AvatarDetailPlanner.ReducedTriangles(30_000), "the budget counts the simplified body at reduced detail");
            ModelLod.FloorOverride = null; AvatarRig.ModelProvider = null;

            GD.Print("event=model_lod_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=model_lod_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
