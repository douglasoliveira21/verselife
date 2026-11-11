using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

/// <summary>
/// The faces being edited. With the Face tool a click picks the face under the cursor (Shift adds more of the same object); the faces light up and the texture and
/// material controls act on them instead of on the whole object. Clearing the faces never touches the object.
/// </summary>
public partial class BuildFaceSelectionManager : Node
{
    private BuildNetworkSynchronizer _sync = null!;
    private Guid _object;
    private readonly SortedSet<int> _faces = [];

    public event Action? Changed;

    public Guid ObjectId => _object;
    public IReadOnlyCollection<int> Faces => _faces;
    public int Count => _faces.Count;

    public void Initialize(BuildNetworkSynchronizer sync)
    {
        _sync = sync;
        sync.ObjectRemoved += id => { if (id == _object) Clear(); };
    }

    /// <summary>The name of a face of a shape, in the words the person sees in the editor.</summary>
    public static string NameOf(string type, int face)
    {
        type = type.ToLowerInvariant(); var round = type is "sphere" or "hemisphere" or "torus" or "ring";
        if (type == "cube") return face switch { 0 => "Topo", 1 => "Base", 2 => "Frente", 3 => "Esquerda", 4 => "Trás", 5 => "Direita", 6 => "Interior", 7 => "Corte inicial", 8 => "Corte final", _ => $"Face {face}" };
        return face switch
        {
            0 => round ? "Cima" : "Topo", 1 => "Base",
            2 => type is "prism" or "pyramid" ? "Lado 1" : round ? "Superfície" : "Lado", 3 => type is "prism" or "pyramid" ? "Lado 2" : "Interior",
            4 => type is "prism" or "pyramid" ? "Lado 3" : "Corte inicial", 5 => type == "pyramid" ? "Lado 4" : "Corte final", _ => $"Face {face}"
        };
    }

    public string Describe()
    {
        if (_faces.Count == 0) return "Nenhuma face escolhida: as mudanças valem para o objeto inteiro.";
        var type = _sync.Views.TryGetValue(_object, out var view) ? view.State.PrimitiveType : "cube";
        return _faces.Count == 1 ? $"Face escolhida: {NameOf(type, _faces.First())}" : $"{_faces.Count} faces escolhidas: {string.Join(", ", _faces.Select(f => NameOf(type, f)))}";
    }

    /// <summary>Picks the face under a screen position. Returns false when there is no face there (the object has no separate faces, or nothing is under the cursor).</summary>
    public bool Pick(Camera3D camera, Vector2 screen, World3D world, bool additive, out string message)
    {
        message = string.Empty;
        var origin = camera.ProjectRayOrigin(screen); var direction = camera.ProjectRayNormal(screen);
        var hit = world.DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(origin, origin + direction * 150f, uint.MaxValue));
        if (!hit.TryGetValue("collider", out var collider)) { if (!additive) Clear(); return false; }
        var node = collider.AsGodotObject() as Node;
        while (node is not null and not BuildObjectView) node = node.GetParent();
        if (node is not BuildObjectView view) { if (!additive) Clear(); return false; }
        if (!view.IsProcedural) { message = "Esta forma não tem faces separadas: use caixa, prisma, cilindro, tubo, cone, pirâmide ou uma forma redonda."; return false; }
        var face = hit.TryGetValue("face_index", out var index) && index.VariantType == Variant.Type.Int ? view.FaceAtTriangle((int)index) : -1;
        if (face < 0) return false;
        Toggle(view.State.Id, face, additive); return true;
    }


    /// <summary>Adds a face (Shift) or makes it the only one; picking a chosen face again with Shift takes it off.</summary>
    public void Toggle(Guid objectId, int face, bool additive)
    {
        if (objectId != _object || !additive) { ClearVisual(); _faces.Clear(); _object = objectId; }
        if (additive && _faces.Contains(face)) _faces.Remove(face); else _faces.Add(face);
        if (_faces.Count == 0) _object = Guid.Empty;
        Light(); Changed?.Invoke();
    }

    public void Clear()
    {
        if (_faces.Count == 0 && _object == Guid.Empty) return;
        ClearVisual(); _faces.Clear(); _object = Guid.Empty; Changed?.Invoke();
    }

    /// <summary>Every face of the chosen object, for a texture that goes on all of them.</summary>
    public void SelectAll(BuildObjectView view)
    {
        if (!view.IsProcedural) return;
        ClearVisual(); _faces.Clear(); _object = view.State.Id; foreach (var face in view.FaceIds) _faces.Add(face);
        Light(); Changed?.Invoke();
    }

    private void ClearVisual() { if (_sync.Views.TryGetValue(_object, out var view) && GodotObject.IsInstanceValid(view)) view.HighlightFace(-1); }
    private void Light() { if (_sync.Views.TryGetValue(_object, out var view) && GodotObject.IsInstanceValid(view)) view.HighlightFaces(_faces); }
}
