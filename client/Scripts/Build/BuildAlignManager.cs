using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Build;

/// <summary>
/// Automatic alignment (line the selected objects up by their low side, middle or high side along an axis, spread them evenly, rest them on the ground) and the copy and paste of
/// the look and the shape of an object. Alignment only computes how far each object moves (<see cref="AlignMath"/>) and sends ordinary updates, so the server and the history
/// treat it like any other move.
/// </summary>
public partial class BuildAlignManager : Node
{
    private BuildNetworkSynchronizer _sync = null!;
    private BuildSelectionManager _selection = null!;
    private BuildUndoRedoManager _history = null!;
    private string? _clipboard;

    public bool HasClipboard => _clipboard is not null;
    public event Action<string>? Said;

    public void Initialize(BuildNetworkSynchronizer sync, BuildSelectionManager selection, BuildUndoRedoManager history) { _sync = sync; _selection = selection; _history = history; }

    /// <summary>The world box of an object: its size turned by its rotation.</summary>
    public static Bounds BoundsOf(BuildObjectState s)
    {
        var basis = BuildGizmoMath.BasisOf(s.RotationX, s.RotationY, s.RotationZ); var half = new Vector3(MathF.Abs(s.ScaleX), MathF.Abs(s.ScaleY), MathF.Abs(s.ScaleZ)) / 2f;
        var extent = new System.Numerics.Vector3(MathF.Abs(basis.X.X) * half.X + MathF.Abs(basis.Y.X) * half.Y + MathF.Abs(basis.Z.X) * half.Z, MathF.Abs(basis.X.Y) * half.X + MathF.Abs(basis.Y.Y) * half.Y + MathF.Abs(basis.Z.Y) * half.Z, MathF.Abs(basis.X.Z) * half.X + MathF.Abs(basis.Y.Z) * half.Y + MathF.Abs(basis.Z.Z) * half.Z);
        var centre = new System.Numerics.Vector3(s.PositionX, s.PositionY, s.PositionZ); return new Bounds(centre - extent, centre + extent);
    }

    private int Move(IReadOnlyList<BuildObjectView> views, float[] offsets, AlignAxis axis, string label)
    {
        var changes = new List<BuildUndoRedoManager.Change>(); var sent = 0;
        for (var i = 0; i < views.Count && i < offsets.Length; i++)
        {
            if (MathF.Abs(offsets[i]) < 1e-4f) continue;
            var before = BuildPersistenceManager.Clone(views[i].State); var after = BuildPersistenceManager.Clone(before);
            switch (axis) { case AlignAxis.X: after.PositionX += offsets[i]; break; case AlignAxis.Y: after.PositionY += offsets[i]; break; default: after.PositionZ += offsets[i]; break; }
            after.PositionX = MathF.Round(after.PositionX, 4); after.PositionY = MathF.Round(after.PositionY, 4); after.PositionZ = MathF.Round(after.PositionZ, 4);
            if (!_sync.Update(after)) continue;
            sent++; changes.Add(new BuildUndoRedoManager.Change { Id = before.Id, Before = before, After = after, Known = before.Revision + 1 });
        }
        if (sent > 0) _history.Record(label, changes);
        return sent;
    }

    public int Align(AlignAxis axis, AlignEdge edge)
    {
        var views = _selection.Views().ToList(); if (views.Count < 2) { Said?.Invoke("Selecione dois ou mais objetos para alinhar."); return 0; }
        var sent = Move(views, AlignMath.Align([.. views.Select(v => BoundsOf(v.State))], axis, edge), axis, "alinhar");
        Said?.Invoke(sent == 0 ? "Eles já estão alinhados." : $"{sent} objetos alinhados."); return sent;
    }

    public int Distribute(AlignAxis axis)
    {
        var views = _selection.Views().ToList(); if (views.Count < 3) { Said?.Invoke("Selecione três ou mais objetos para distribuir."); return 0; }
        var sent = Move(views, AlignMath.Distribute([.. views.Select(v => BoundsOf(v.State))], axis), axis, "distribuir");
        Said?.Invoke(sent == 0 ? "Eles já estão distribuídos." : $"{sent} objetos distribuídos."); return sent;
    }

    /// <summary>Lowers or lifts the whole selection so its lowest point touches the ground (<paramref name="groundAt"/> answers the height of the ground under a point, or null).</summary>
    public int RestOnGround(Func<Vector3, float?> groundAt)
    {
        var views = _selection.Views().ToList(); if (views.Count == 0) { Said?.Invoke("Selecione um objeto para pousar no chão."); return 0; }
        var boxes = views.Select(v => BoundsOf(v.State)).ToList(); var centre = new Vector3(boxes.Average(b => b.Center.X), boxes.Min(b => b.Min.Y), boxes.Average(b => b.Center.Z));
        if (groundAt(centre) is not { } height) { Said?.Invoke("Não há chão embaixo da seleção."); return 0; }
        var offset = AlignMath.RestOnGround(boxes, height);
        var sent = Move(views, [.. views.Select(_ => offset)], AlignAxis.Y, "pousar no chão");
        Said?.Invoke(sent == 0 ? "A seleção já está no chão." : "Pousado no chão."); return sent;
    }

    // ---- copy and paste the properties ---------------------------------------------------------------------------------------

    /// <summary>Remembers the look (material), the shape (geometry) and the physics of the active object.</summary>
    public bool CopyProperties()
    {
        if (_selection.Views().LastOrDefault()?.State is not { } s) { Said?.Invoke("Selecione um objeto para copiar as propriedades."); return false; }
        _clipboard = JsonSerializer.Serialize(new { primitiveType = s.PrimitiveType, materialJson = s.MaterialJson, geometryJson = s.GeometryJson, physicsJson = s.PhysicsJson });
        Said?.Invoke($"Propriedades de “{s.Name}” copiadas."); return true;
    }

    /// <summary>Gives the selection what was copied. The shape (geometry) only goes to objects of the same kind of primitive; imported models keep their own geometry.</summary>
    public int PasteProperties()
    {
        if (_clipboard is null) { Said?.Invoke("Copie as propriedades de um objeto primeiro."); return 0; }
        var data = JsonNode.Parse(_clipboard)!.AsObject(); var type = data["primitiveType"]!.GetValue<string>(); var changes = new List<BuildUndoRedoManager.Change>(); var sent = 0;
        foreach (var view in _selection.Views().ToList())
        {
            var before = BuildPersistenceManager.Clone(view.State); var after = BuildPersistenceManager.Clone(before);
            after.MaterialJson = data["materialJson"]!.GetValue<string>(); after.PhysicsJson = data["physicsJson"]!.GetValue<string>();
            if (before.PrimitiveType == type) after.GeometryJson = data["geometryJson"]!.GetValue<string>();
            else if (BuildGeometryManager.IsParametric(before.PrimitiveType) && BuildGeometryManager.IsParametric(type)) after.GeometryJson = BuildGeometryManager.Keep(data["geometryJson"]!.GetValue<string>(), before.PrimitiveType);
            if (after.MaterialJson == before.MaterialJson && after.PhysicsJson == before.PhysicsJson && after.GeometryJson == before.GeometryJson) continue;
            if (!_sync.Update(after)) continue;
            sent++; changes.Add(new BuildUndoRedoManager.Change { Id = before.Id, Before = before, After = after, Known = before.Revision + 1 });
        }
        if (sent > 0) _history.Record("colar propriedades", changes);
        Said?.Invoke(sent == 0 ? "Nada mudou: a seleção já tem essas propriedades." : $"Propriedades coladas em {sent} objeto(s)."); return sent;
    }
}
