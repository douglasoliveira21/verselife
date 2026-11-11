using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Build;

/// <summary>
/// Linking and unlinking. The plan (who becomes the root, who hangs under whom) is made with the same rules the server uses, so a request that would be refused is
/// explained before it is sent; the server still checks permissions, protection and versions and answers with the objects as they now are.
/// </summary>
public partial class BuildLinkManager : Node
{
    private BuildNetworkSynchronizer _sync = null!;

    public void Initialize(BuildNetworkSynchronizer sync) => _sync = sync;

    private List<LinkNode> World() => [.. _sync.Views.Values.Where(v => GodotObject.IsInstanceValid(v)).Select(v => new LinkNode(v.State.Id, v.State.GroupId, v.State.ParentObjectId))];

    public sealed record Plan(string Message, List<BuildUndoRedoManager.Change> Changes, bool Sent);

    /// <summary>Links the selected objects under the first one selected (the root). Linksets that are already built are kept and hang under it.</summary>
    public Plan Link(IReadOnlyList<BuildObjectView> selection)
    {
        if (selection.Count < 2) return new("Selecione dois ou mais objetos para vincular.", [], false);
        if (selection.Any(v => v.State.EditLocked)) return new("Há objetos protegidos contra edição na seleção: desproteja antes de vincular.", [], false);
        var root = selection[0].State.Id; var group = selection.All(v => v.State.GroupId is { } g && g == selection[0].State.GroupId) ? selection[0].State.GroupId!.Value : Guid.NewGuid();
        var plan = LinksetRules.Link(World(), [.. selection.Select(v => v.State.Id)], root, group);
        if (plan.Count == 0) return new("Esses objetos já estão vinculados desse jeito.", [], false);
        return Send(plan, $"{selection.Count} objetos vinculados; a raiz é “{selection[0].State.Name}”.");
    }

    /// <summary>Unlinks the selected objects; they keep their place in the world, their materials, properties and contents.</summary>
    public Plan Unlink(IReadOnlyList<BuildObjectView> selection)
    {
        if (selection.All(v => v.State.GroupId is null)) return new("A seleção não está vinculada.", [], false);
        if (selection.Any(v => v.State.EditLocked)) return new("Há objetos protegidos contra edição na seleção: desproteja antes de desvincular.", [], false);
        var plan = LinksetRules.Detach(World(), [.. selection.Select(v => v.State.Id)]);
        if (plan.Count == 0) return new("Nada para desvincular.", [], false);
        return Send(plan, "Vínculo desfeito. Os objetos continuam no mundo, separados.");
    }

    private Plan Send(List<LinkNode> plan, string done)
    {
        var changes = new List<BuildUndoRedoManager.Change>(); var links = new List<(Guid, Guid?, Guid?, int)>();
        foreach (var node in plan)
        {
            if (!_sync.Views.TryGetValue(node.Id, out var view)) continue;
            var before = BuildPersistenceManager.Clone(view.State); var after = BuildPersistenceManager.Clone(before); after.GroupId = node.GroupId; after.ParentObjectId = node.ParentId;
            changes.Add(new BuildUndoRedoManager.Change { Id = node.Id, Before = before, After = after, Known = before.Revision + 1, Structure = true });
            links.Add((node.Id, node.GroupId, node.ParentId, before.Revision));
        }
        if (links.Count == 0) return new("Nada para vincular.", [], false);
        return _sync.Structure(links) ? new(done, changes, true) : new("Não foi possível enviar o vínculo.", [], false);
    }

    /// <summary>The linked parts as a tree for the hierarchy list: (object, depth), roots first, children under their parent.</summary>
    public IReadOnlyList<(BuildObjectView View, int Depth)> Tree()
    {
        var views = _sync.Views.Values.Where(v => GodotObject.IsInstanceValid(v) && v.State.GroupId is not null).ToList(); var result = new List<(BuildObjectView, int)>();
        void Walk(BuildObjectView view, int depth) { result.Add((view, depth)); foreach (var child in views.Where(v => v.State.ParentObjectId == view.State.Id).OrderBy(v => v.State.Name)) Walk(child, depth + 1); }
        foreach (var root in views.Where(v => v.State.ParentObjectId is null || !views.Any(o => o.State.Id == v.State.ParentObjectId)).OrderBy(v => v.State.Name)) Walk(root, 0);
        return result;
    }
}
