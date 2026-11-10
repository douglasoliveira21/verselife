using Godot;

namespace LifeVerse.Client.Scripts;

/// What the next change of the Textura tab goes to: the whole object, or one or more faces. The faces are the real faces of the object's mesh, so a sphere or a ring
/// have theirs too. A box has the usual names (Frente, Esquerda, Trás, Direita, Topo, Base). The list is the "Selecionar faces" box of the Textura tab; the face tool
/// (5) and Shift + click in the world still add faces and show here as well.
public partial class BuildEditor
{
    private string _targetKey = string.Empty;

    public static string FaceName(string type, int id)
    {
        type = type.ToLowerInvariant();
        if (type is "cube") return id switch { 0 => "Topo", 1 => "Base", 2 => "Frente", 3 => "Esquerda", 4 => "Trás", 5 => "Direita", 6 => "Interior", 7 => "Corte inicial", 8 => "Corte final", _ => $"Face {id}" };
        var round = type is "sphere" or "hemisphere" or "torus" or "ring";
        return id switch
        {
            0 => round ? "Cima" : "Topo", 1 => "Base",
            2 => type is "prism" or "pyramid" ? "Lado 1" : round ? "Superfície" : "Lado", 3 => type is "prism" or "pyramid" ? "Lado 2" : "Interior",
            4 => type is "prism" or "pyramid" ? "Lado 3" : "Corte inicial", 5 => type is "pyramid" ? "Lado 4" : "Corte final", _ => $"Face {id}"
        };
    }

    private void SetFaceSelection(BuildObjectView view, IReadOnlyCollection<int> ids)
    {
        ClearFace();
        if (ids.Count == 0) { RefreshTargetCard(); return; }
        _faceView = view; foreach (var id in ids) _faceSet.Add(id);
        _selectedFace = _faceSet.Max; view.HighlightFaces(_faceSet); ShowFaceLabel(); FillTextureFields(); RefreshTargetCard();
    }
}
