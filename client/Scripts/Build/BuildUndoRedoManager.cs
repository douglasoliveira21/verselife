using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

/// <summary>
/// The history of what the person did, as reversible commands. Undo and redo are new commands to the server like any other edit, checked against the version of the
/// object: if somebody else changed it since, the step is skipped and the person is told, never forced over their work. Money and ownership changes are not here.
/// </summary>
public partial class BuildUndoRedoManager : Node
{
    public const int Limit = 100;

    public sealed class Change
    {
        public Guid Id;
        public BuildObjectState? Before, After;   // before null: the object was created; after null: it was deleted
        public int Known;                         // the version of the object right after our own last change
        public bool Structure;                    // the change is in the links (group and parent), which the server only takes through the structure command
    }

    public sealed record Entry(string Label, List<Change> Changes);

    private readonly List<Entry> _undo = [], _redo = [];
    private readonly Dictionary<Guid, Guid> _alias = [];
    private readonly HashSet<Guid> _awaiting = [];
    private readonly Queue<(Change Change, bool Making)> _creations = new();
    private BuildNetworkSynchronizer _sync = null!;

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public string NextUndoLabel => _undo.Count > 0 ? _undo[^1].Label : string.Empty;
    public event Action<string>? Message;
    public event Action? Changed;

    public void Initialize(BuildNetworkSynchronizer sync)
    {
        _sync = sync; sync.ObjectChanged += OnObjectChanged; sync.ObjectRemoved += id => _awaiting.Remove(id);
    }

    private Guid Resolve(Guid id) { for (var hops = 0; hops < 16 && _alias.TryGetValue(id, out var next) && next != id; hops++) id = next; return id; }

    /// <summary>Remembers one action. A new action ends the redo line.</summary>
    public void Record(string label, IEnumerable<Change> changes)
    {
        var list = changes.ToList(); if (list.Count == 0) return;
        _undo.Add(new Entry(label, list)); if (_undo.Count > Limit) _undo.RemoveAt(0);
        _redo.Clear(); foreach (var change in list) _awaiting.Add(change.Id);
        Changed?.Invoke();
    }

    /// <summary>A create the server confirmed: the entry that made it learns the id of the object.</summary>
    public void Created(Change change, BuildObjectState state) { change.Id = state.Id; change.After = BuildPersistenceManager.Clone(state); change.Known = state.Revision; _awaiting.Remove(state.Id); }

    private void OnObjectChanged(BuildObjectState state, bool isNew)
    {
        if (_creations.Count > 0 && isNew && state.CreatorUserId == _sync.MyUserId)
        {
            var (change, making) = _creations.Dequeue();
            if (change.Id != Guid.Empty && change.Id != state.Id) _alias[change.Id] = state.Id; // the object came back with a new identity: older steps follow it
            change.Id = state.Id; change.Known = state.Revision; _awaiting.Remove(state.Id); return;
        }
        if (_awaiting.Remove(state.Id)) foreach (var entry in _undo.Concat(_redo)) foreach (var change in entry.Changes) if (Resolve(change.Id) == state.Id) change.Known = state.Revision;
    }

    public void Undo() => Step(_undo, _redo, undo: true);
    public void Redo() => Step(_redo, _undo, undo: false);

    private void Step(List<Entry> from, List<Entry> to, bool undo)
    {
        if (from.Count == 0) { Message?.Invoke(undo ? "Nada para desfazer." : "Nada para refazer."); return; }
        var entry = from[^1]; from.RemoveAt(from.Count - 1);
        var applied = new List<Change>(); var skipped = 0;
        // changes in the links go to the server together, as one all-or-nothing command
        var links = new List<(Guid, Guid?, Guid?, int)>(); var linked = new List<Change>();
        foreach (var change in entry.Changes.Where(c => c.Structure))
        {
            var target = undo ? change.Before : change.After; var id = Resolve(change.Id);
            if (target is null || !_sync.Views.TryGetValue(id, out var view) || view.State.Revision != change.Known) { skipped++; continue; }
            links.Add((id, target.GroupId, target.ParentObjectId, view.State.Revision)); linked.Add(change);
        }
        if (links.Count > 0) { if (_sync.Structure(links)) { applied.AddRange(linked); foreach (var l in links) _awaiting.Add(l.Item1); } else skipped += links.Count; }
        foreach (var change in entry.Changes.Where(c => !c.Structure))
        {
            var target = undo ? change.Before : change.After; var source = undo ? change.After : change.Before; var id = Resolve(change.Id);
            if (source is not null && target is not null)
            {
                // an edit: put the object back, as long as nobody else changed it since
                if (!_sync.Views.TryGetValue(id, out var view) || view.State.Revision != change.Known) { skipped++; continue; }
                var next = BuildPersistenceManager.Clone(target); next.Id = id; next.Revision = view.State.Revision;
                if (_sync.Update(next)) { _awaiting.Add(id); applied.Add(change); } else skipped++;
            }
            else if (target is null)
            {
                // the object did not exist before this step (or will not exist after it): remove it
                if (!_sync.Views.TryGetValue(id, out var view) || view.State.Revision != change.Known) { skipped++; continue; }
                if (_sync.Delete(id)) { applied.Add(change); _alias.Remove(change.Id); } else skipped++;
            }
            else
            {
                // the object has to exist again: ask the server for a new one with the same content
                var copy = BuildPersistenceManager.Clone(target); copy.Id = Guid.NewGuid(); var oldId = change.Id;
                if (_sync.Create(copy, created => { _alias[oldId] = created.Id; change.Id = created.Id; change.Known = created.Revision; })) applied.Add(change); else skipped++;
            }
        }
        if (applied.Count > 0) to.Add(new Entry(entry.Label, applied));
        Message?.Invoke(skipped > 0 ? $"{(undo ? "Desfazer" : "Refazer")}: {skipped} objeto(s) foram alterados por outra pessoa ou não existem mais e ficaram como estão." : (undo ? "Desfeito: " : "Refeito: ") + entry.Label + ".");
        Changed?.Invoke();
    }

    public void Clear() { _undo.Clear(); _redo.Clear(); _alias.Clear(); _awaiting.Clear(); _creations.Clear(); Changed?.Invoke(); }
}
