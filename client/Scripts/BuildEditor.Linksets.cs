using System.Text.Json;
using Godot;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Scripts;

/// Linksets and protection: objects that belong together under a root, shown as a tree in the "Hierarquia" tab, edited one part at a time when needed, and
/// protected against accidental editing. The editor only plans (with the same rules as the server, <see cref="LinksetRules"/>) and asks; the server checks
/// everything and answers with the objects as they now are.
public partial class BuildEditor
{
    private bool _partMode, _withChildren = true, _hierarchyDirty = true;
    private Tree _hierarchy = null!;
    private Label _hierarchySummary = null!;
    private CheckBox _partCheck = null!, _childrenCheck = null!;
    private bool _syncingTree;

    private List<LinkNode> LinkWorld() => [.. _objects.Values.Select(v => new LinkNode(v.State.Id, v.State.GroupId, v.State.ParentObjectId))];
    private Guid[] SelectedIds() => [.. _selection.Select(v => v.State.Id)];

    // ---- selection ------------------------------------------------------------------------------------------------

    /// What a click selects: the whole linkset, or (with Ctrl, or "Editar só a peça") only that part, with what hangs under it when "Incluir filhos" is on.
    private List<BuildObjectView> MembersOf(BuildObjectView view)
    {
        if (view.State.GroupId is not { } group) return [view];
        if (_partMode || Input.IsKeyPressed(Key.Ctrl))
        {
            var part = new List<BuildObjectView> { view };
            if (_withChildren) foreach (var id in LinksetRules.Descendants(LinkWorld(), view.State.Id)) if (_objects.TryGetValue(id, out var child)) part.Add(child);
            return part;
        }
        return [.. _objects.Values.Where(x => x.State.GroupId == group)];
    }

    // ---- planning and sending -------------------------------------------------------------------------------------

    /// Links the selected objects under a root: the first one selected (the one clicked first). Trees already built are kept; linksets joined hang under it.
    private void LinkSelection()
    {
        if (_selection.Count < 2) { _status.Text = "Selecione dois ou mais objetos para vincular."; return; }
        if (SelectionProtected()) return;
        var root = _selection[0].State.Id; var world = LinkWorld();
        var group = _selection.All(v => v.State.GroupId is { } g && g == _selection[0].State.GroupId) ? _selection[0].State.GroupId!.Value : Guid.NewGuid();
        var plan = LinksetRules.Link(world, SelectedIds(), root, group);
        if (plan.Count == 0) { _status.Text = "Esses objetos já estão vinculados desse jeito."; return; }
        SendStructure(plan, $"{_selection.Count} objetos vinculados; a raiz é “{_selection[0].State.Name}”.");
        Teach(TutorialStep.Link);
    }

    private void UnlinkSelection()
    {
        if (_selection.All(v => v.State.GroupId is null)) { _status.Text = "A seleção não está vinculada."; return; }
        if (SelectionProtected()) return;
        var plan = LinksetRules.Detach(LinkWorld(), SelectedIds());
        if (plan.Count == 0) return;
        SendStructure(plan, "Vínculo desfeito. Os objetos continuam no mundo, separados.");
    }

    /// Makes the selected part the root of its linkset.
    private void MakeRoot()
    {
        if (_selection.Count != 1 || _selection[0].State.GroupId is null) { _status.Text = "Escolha uma peça de um conjunto vinculado para torná-la a raiz."; return; }
        if (_selection[0].State.ParentObjectId is null) { _status.Text = "Essa peça já é a raiz."; return; }
        var plan = LinksetRules.SetRoot(LinkWorld(), _selection[0].State.Id); if (plan.Count == 0) return;
        if (plan.Any(n => _objects.TryGetValue(n.Id, out var v) && v.State.EditLocked)) { _status.Text = "Há peças protegidas no caminho até a raiz: desproteja antes."; return; }
        SendStructure(plan, $"“{_selection[0].State.Name}” agora é a raiz do conjunto.");
    }

    private void SendStructure(List<LinkNode> plan, string done, bool history = true)
    {
        var entry = new HistoryEntry("structure", []); var links = new List<object>();
        foreach (var node in plan)
        {
            if (!_objects.TryGetValue(node.Id, out var view)) continue;
            var before = Clone(view.State); var after = Clone(before); after.GroupId = node.GroupId; after.ParentObjectId = node.ParentId;
            entry.Items.Add(new HistoryItem(node.Id, before, after)); links.Add(new { objectId = node.Id, groupId = node.GroupId, parentId = node.ParentId, expectedRevision = before.Revision });
        }
        if (links.Count == 0) return;
        if (history) PushHistory(entry); Send(JsonSerializer.Serialize(new { operation = "structure", links })); if (done.Length > 0) _status.Text = done;
    }

    /// Undoing or redoing a link change asks the server for the structure the objects had (all in one step, so it is never half undone).
    private void ReplayStructure(HistoryEntry entry, bool undo)
    {
        var links = new List<object>();
        foreach (var item in entry.Items)
        {
            var target = undo ? item.Before : item.After; if (target is null || !_objects.TryGetValue(Resolve(item.Id), out var view)) continue;
            links.Add(new { objectId = view.State.Id, groupId = target.GroupId, parentId = target.ParentObjectId, expectedRevision = view.State.Revision });
        }
        if (links.Count > 0) Send(JsonSerializer.Serialize(new { operation = "structure", links }));
    }

    // ---- protection -----------------------------------------------------------------------------------------------

    private bool SelectionProtected()
    {
        var locked = _selection.FirstOrDefault(v => v.State.EditLocked);
        if (locked is null) return false;
        _status.Text = $"“{locked.State.Name}” está protegido contra edição. Use “Desproteger” (aba Hierarquia) para mexer nele.";
        return true;
    }

    private void ProtectSelection(bool locked)
    {
        var targets = _selection.Where(v => v.State.EditLocked != locked).ToList();
        if (_selection.Count == 0) { _status.Text = "Selecione os objetos que quer proteger."; return; }
        if (targets.Count == 0) { _status.Text = locked ? "A seleção já está protegida." : "A seleção não está protegida."; return; }
        var entry = new HistoryEntry("protect", []);
        foreach (var view in targets) { var before = Clone(view.State); var after = Clone(before); after.EditLocked = locked; entry.Items.Add(new HistoryItem(view.State.Id, before, after)); }
        PushHistory(entry); Send(JsonSerializer.Serialize(new { operation = "protect", objectIds = targets.Select(v => v.State.Id).ToArray(), locked }));
        _status.Text = locked ? $"{targets.Count} objeto(s) protegido(s) contra edição acidental." : $"{targets.Count} objeto(s) liberado(s) para edição.";
    }

    private void ReplayProtection(HistoryEntry entry, bool undo)
    {
        foreach (var group in entry.Items.GroupBy(i => (undo ? i.Before : i.After)?.EditLocked ?? false))
        {
            var ids = group.Select(i => Resolve(i.Id)).Where(id => _objects.ContainsKey(id)).ToArray(); if (ids.Length == 0) continue;
            Send(JsonSerializer.Serialize(new { operation = "protect", objectIds = ids, locked = group.Key }));
        }
    }

    // ---- what the server says -------------------------------------------------------------------------------------

    /// A message that carries several objects at once (a linkset changed, objects protected, what was re-hung after a delete): every one is updated.
    private void ApplyObjects(JsonElement list)
    {
        if (list.ValueKind != JsonValueKind.Array) return;
        foreach (var one in list.EnumerateArray())
        {
            var state = JsonSerializer.Deserialize<BuildObjectState>(one.GetRawText(), Web)!; AddOrUpdate(state); Settle(state.Id, state.Revision);
        }
        _hierarchyDirty = true; AfterSelectionChanged();
    }

    // ---- the tab --------------------------------------------------------------------------------------------------

    private Control BuildHierarchyTab()
    {
        var page = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; page.AddThemeConstantOverride("separation", 12);
        var card = Card("Hierarquia e proteção", "layers", out var body, out _, 12); card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; page.AddChild(card);
        body.AddChild(Txt("Um conjunto vinculado tem uma raiz e peças penduradas nela (ou em outras peças). Clicar em uma peça seleciona o conjunto todo; para mexer só em uma peça, use “Editar só a peça” ou Ctrl + clique.", 18, false, UiStyle.TextMuted));
        var modes = Row(16); body.AddChild(modes);
        _partCheck = new CheckBox { Text = " Editar só a peça" }; _partCheck.AddThemeFontSizeOverride("font_size", 20); _partCheck.Toggled += on => _partMode = on; modes.AddChild(_partCheck);
        _childrenCheck = new CheckBox { Text = " Incluir o que está pendurado nela", ButtonPressed = true }; _childrenCheck.AddThemeFontSizeOverride("font_size", 20); _childrenCheck.Toggled += on => _withChildren = on; modes.AddChild(_childrenCheck);
        body.AddChild(Rule());
        _hierarchy = new Tree { HideRoot = true, CustomMinimumSize = new Vector2(0, 330), SizeFlagsVertical = Control.SizeFlags.ExpandFill, SelectMode = Tree.SelectModeEnum.Single };
        _hierarchy.AddThemeFontSizeOverride("font_size", 20); _hierarchy.ItemSelected += OnHierarchyPicked; body.AddChild(_hierarchy);
        _hierarchySummary = Txt("Nenhum conjunto vinculado nesta ilha.", 18, false, UiStyle.TextMuted); body.AddChild(_hierarchySummary);
        body.AddChild(Rule());
        var links = Row(10); body.AddChild(links);
        links.AddChild(IconButton("link", "Vincular", "Junta a seleção sob uma raiz: a primeira peça selecionada (Ctrl+G)", LinkSelection, UiStyle.ButtonKind.Primary, 46));
        links.AddChild(IconButton("unlink", "Desvincular", "Separa a seleção do conjunto; os objetos continuam no mundo (Ctrl+Shift+G)", UnlinkSelection, UiStyle.ButtonKind.Secondary, 46));
        links.AddChild(IconButton("target", "Tornar raiz", "Faz da peça selecionada a raiz do conjunto", MakeRoot, UiStyle.ButtonKind.Secondary, 46));
        var guard = Row(10); body.AddChild(guard);
        guard.AddChild(IconButton("lock", "Proteger", "Protege a seleção contra edição acidental: nem mover, nem alterar, nem apagar", () => ProtectSelection(true), UiStyle.ButtonKind.Secondary, 46));
        guard.AddChild(IconButton("unlock", "Desproteger", "Libera a seleção para edição de novo", () => ProtectSelection(false), UiStyle.ButtonKind.Secondary, 46));
        return page;
    }

    /// Rebuilds the tree: every linkset of the island with its root and what hangs under each part, then the objects that are protected on their own.
    private void RefreshHierarchy()
    {
        if (_hierarchy is null || !_hierarchyDirty) return; _hierarchyDirty = false; _syncingTree = true;
        _hierarchy.Clear(); var root = _hierarchy.CreateItem(); var sets = 0; var parts = 0;
        var byGroup = _objects.Values.Where(v => v.State.GroupId is not null).GroupBy(v => v.State.GroupId!.Value).OrderBy(g => g.Min(v => v.State.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var group in byGroup)
        {
            var members = group.ToList(); var top = LinksetRules.RootOf(LinkWorld(), group.Key); sets++; parts += members.Count;
            void Add(TreeItem parent, BuildObjectView view, int depth)
            {
                var item = _hierarchy.CreateItem(parent); item.SetText(0, $"{(view.State.EditLocked ? "🔒 " : string.Empty)}{view.State.Name}{(depth == 0 ? "  (raiz)" : string.Empty)}"); item.SetMetadata(0, view.State.Id.ToString());
                if (_selection.Contains(view)) item.Select(0);
                if (depth < LinksetRules.MaxDepth) foreach (var child in members.Where(m => m.State.ParentObjectId == view.State.Id).OrderBy(m => m.State.Name, StringComparer.OrdinalIgnoreCase)) Add(item, child, depth + 1);
            }
            var start = members.FirstOrDefault(m => m.State.Id == top) ?? members[0];
            var header = _hierarchy.CreateItem(root); header.SetText(0, $"Conjunto de {members.Count} peças"); header.SetSelectable(0, false);
            Add(header, start, 0);
            foreach (var stray in members.Where(m => m.State.Id != start.State.Id && !Reaches(members, m, start.State.Id))) Add(header, stray, 1); // objects of an old group without a root still show up
        }
        var alone = _objects.Values.Where(v => v.State.GroupId is null && v.State.EditLocked).ToList();
        if (alone.Count > 0)
        {
            var header = _hierarchy.CreateItem(root); header.SetText(0, $"Protegidos ({alone.Count})"); header.SetSelectable(0, false);
            foreach (var view in alone.OrderBy(v => v.State.Name, StringComparer.OrdinalIgnoreCase)) { var item = _hierarchy.CreateItem(header); item.SetText(0, "🔒 " + view.State.Name); item.SetMetadata(0, view.State.Id.ToString()); }
        }
        _hierarchySummary.Text = sets == 0 && alone.Count == 0 ? "Nenhum conjunto vinculado nem objeto protegido nesta ilha." : $"{sets} conjunto(s) com {parts} peça(s); {_objects.Values.Count(v => v.State.EditLocked)} objeto(s) protegido(s).";
        _syncingTree = false;
    }

    private static bool Reaches(List<BuildObjectView> members, BuildObjectView from, Guid top)
    {
        var at = from; for (var depth = 0; depth <= LinksetRules.MaxDepth; depth++) { if (at.State.Id == top) return true; var up = members.FirstOrDefault(m => m.State.Id == at.State.ParentObjectId); if (up is null) return false; at = up; }
        return false;
    }

    private void OnHierarchyPicked()
    {
        if (_syncingTree || _hierarchy.GetSelected() is not { } item || !Guid.TryParse(item.GetMetadata(0).AsString(), out var id) || !_objects.TryGetValue(id, out var view)) return;
        var wasPart = _partMode; _partMode = true; try { SelectView(view, false); } finally { _partMode = wasPart; } // picking a name in the tree is picking that part
    }

    // ---- tests ----------------------------------------------------------------------------------------------------

    public void LinkForTesting() => LinkSelection();
    public void UnlinkForTesting() => UnlinkSelection();
    public void MakeRootForTesting() => MakeRoot();
    public void ProtectForTesting(bool locked) => ProtectSelection(locked);
    public void SetPartModeForTesting(bool on, bool children = true) { _partMode = on; _withChildren = children; }
    public void PickInHierarchyForTesting(Guid id) { if (_objects.TryGetValue(id, out var view)) { var was = _partMode; _partMode = true; try { SelectView(view, false); } finally { _partMode = was; } } }
    public IReadOnlyList<string> HierarchyLinesForTesting { get { _hierarchyDirty = true; RefreshHierarchy(); var lines = new List<string>(); void Walk(TreeItem? item, int depth) { for (var child = item?.GetFirstChild(); child is not null; child = child.GetNext()) { lines.Add(new string(' ', depth * 2) + child.GetText(0)); Walk(child, depth + 1); } } Walk(_hierarchy.GetRoot(), 0); return lines; } }
}

public partial class BuildEditor
{
    /// " · raiz do conjunto", " · peça de “X”" and " · 🔒 protegido" for the selection (several objects: only what is true of all of them).
    private string LinkNote(IReadOnlyList<BuildObjectView> selection)
    {
        if (selection.Count == 0) return string.Empty;
        var text = string.Empty;
        if (selection.Count == 1 && selection[0].State.GroupId is not null)
            text = selection[0].State.ParentObjectId is { } parent ? $" · peça de “{(_objects.TryGetValue(parent, out var up) ? up.State.Name : "?")}”" : " · raiz do conjunto";
        if (selection.All(v => v.State.EditLocked)) text += " · 🔒 protegido"; else if (selection.Any(v => v.State.EditLocked)) text += " · 🔒 alguns protegidos";
        return text;
    }

    public string LinkNoteForTesting => LinkNote(_selection);
}

public partial class BuildEditor
{
    /// A pasted or duplicated linkset keeps its tree: the copies are created as loose members of a new linkset, and once they all exist the editor hangs each copy under the copy of its parent.
    private void RestoreCopiedHierarchy(List<BuildObjectState> source, Dictionary<Guid, Guid> copyOf, Dictionary<Guid, Guid> groups)
    {
        var links = new List<LinkNode>();
        foreach (var set in source.Where(s => s.GroupId is not null && groups.ContainsKey(s.GroupId!.Value)).GroupBy(s => s.GroupId!.Value))
        {
            var members = set.ToList(); var ids = members.Select(m => m.Id).ToHashSet();
            var roots = members.Where(m => m.ParentObjectId is not { } p || !ids.Contains(p)).OrderBy(m => m.Id).ToList(); var top = roots.Count > 0 ? roots[0] : members.OrderBy(m => m.Id).First();
            if (!copyOf.TryGetValue(top.Id, out var topCopy)) continue;
            foreach (var m in members)
            {
                if (m.Id == top.Id || !copyOf.TryGetValue(m.Id, out var mine)) continue;
                var parent = m.ParentObjectId is { } p && ids.Contains(p) && copyOf.TryGetValue(p, out var parentCopy) ? parentCopy : topCopy;
                links.Add(new LinkNode(mine, groups[set.Key], parent));
            }
        }
        if (links.Count > 0) SendStructure(links, string.Empty, history: false); // part of the paste's own undo step: undoing it deletes the copies
    }
}
