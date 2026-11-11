using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Build;

/// <summary>What the person can create: the primitives of the creation grid, the state a new object starts with and the see-through copy that follows the cursor.</summary>
public static class BuildPrimitiveFactory
{
    public sealed record Entry(string Type, string Label, string Hint);

    /// <summary>The grid of the "Criar objeto" screen, in the order the person sees it. Every entry is a shape the server accepts (<see cref="PrimitiveCatalog"/>).</summary>
    public static readonly IReadOnlyList<Entry> Entries =
    [
        new("cube", "Caixa", "Caixa com 4 lados"), new("sphere", "Esfera", "Esfera"), new("cylinder", "Cilindro", "Cilindro redondo"), new("prism", "Prisma", "Prisma triangular"),
        new("torus", "Toroide", "Rosca (toroide)"), new("tube", "Tubo", "Tubo oco"), new("ring", "Anel", "Anel fino"), new("cone", "Cone", "Cone"),
        new("pyramid", "Pirâmide", "Pirâmide de base quadrada"), new("hemisphere", "Semiesfera", "Metade de uma esfera"),
        new("tree", "Árvore", "Árvore: só o tronco bloqueia"), new("grass", "Grama", "Tufo de grama: dá para atravessar")
    ];

    public static string LabelOf(string type) => Entries.FirstOrDefault(e => e.Type.Equals(type, StringComparison.OrdinalIgnoreCase))?.Label ?? (type.Equals("mesh", StringComparison.OrdinalIgnoreCase) ? "Modelo 3D" : type);

    public const string DefaultMaterial = "{\"shading\":\"blinnphong\",\"builtin\":\"plywood\",\"color\":\"#FFFFFF\",\"glossiness\":0.4}";
    public const string PreviewLook = "{\"color\":\"#7FE8FF\",\"transparency\":0.4,\"emission\":\"#12F2E0\",\"emissionStrength\":0.7}";

    /// <summary>The state a new object of that shape asks the server for; the server gives it its identity, owner and revision.</summary>
    public static BuildObjectState NewState(string type, Vector3 position) => new()
    {
        Id = Guid.NewGuid(), PrimitiveType = type, Name = LabelOf(type), PositionX = position.X, PositionY = position.Y, PositionZ = position.Z,
        ScaleX = 1, ScaleY = 1, ScaleZ = 1, MaterialJson = DefaultMaterial, GeometryJson = "{}", PhysicsJson = "{\"collision\":true}", ContentJson = "[]"
    };

    /// <summary>A translucent copy of the shape for the placement mode: it is never sent to the server and has no collision, so it cannot be hit by the ray that places it.</summary>
    public static BuildObjectView CreatePreview(Node parent, string type)
    {
        var view = new BuildObjectView(); parent.AddChild(view);
        view.Apply(new BuildObjectState { Id = Guid.NewGuid(), PrimitiveType = type, Name = string.Empty, MaterialJson = PreviewLook, PhysicsJson = "{\"collision\":false}" });
        view.Visible = false; return view;
    }

    /// <summary>The rest height of a one-metre shape on a surface: half its height above the hit point along the surface normal.</summary>
    public static Vector3 RestingPoint(Vector3 hit, Vector3 normal, float halfHeight = .5f) => hit + normal * halfHeight;
}
