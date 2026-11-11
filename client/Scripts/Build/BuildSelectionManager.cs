using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

/// <summary>
/// The set of objects being edited. It picks objects with a ray from the camera, keeps the active one, follows the whole linked set when one part is picked
/// (Ctrl picks a single part), highlights what is selected and forgets objects that disappear. It never changes an object: the other modules do that.
/// </summary>
public partial class BuildSelectionManager : Node
{
    private readonly List<Guid> _selected = [];
    private BuildNetworkSynchronizer _sync = null!;

    public event Action? Changed;

    public IReadOnlyList<Guid> Ids => _selected;
    public int Count => _selected.Count;
    /// <summary>The object the tools act on first: the last one picked.</summary>
    public Guid Active => _selected.Count == 0 ? Guid.Empty : _selected[^1];

    public void Initialize(BuildNetworkSynchronizer sync)
    {
        _sync = sync;
        sync.ObjectChanged += (state, _) => { if (_selected.Contains(state.Id) && Views().FirstOrDefault(v => v.State.Id == state.Id) is { } view) view.SetSelected(true); Changed?.Invoke(); };
        sync.ObjectRemoved += id => { if (_selected.Remove(id)) Changed?.Invoke(); };
    }

    public IEnumerable<BuildObjectView> Views() => _selected.Select(id => _sync.Views.GetValueOrDefault(id)).Where(v => v is not null && IsInstanceValid(v))!;
    public bool Contains(Guid id) => _selected.Contains(id);

    /// <summary>The objects that move together with <paramref name="id"/>: its whole linked set, or just itself.</summary>
    public IReadOnlyList<Guid> SetOf(Guid id)
    {
        if (!_sync.Views.TryGetValue(id, out var view) || view.State.GroupId is not { } group) return [id];
        return [.. _sync.Views.Values.Where(v => IsInstanceValid(v) && v.State.GroupId == group).Select(v => v.State.Id)];
    }

    /// <summary>Selects what was picked. <paramref name="additive"/> (Shift) adds it or, when already there, removes it; <paramref name="single"/> (Ctrl) takes only that part of a linked set.</summary>
    public void Pick(Guid id, bool additive, bool single)
    {
        var members = single ? [id] : SetOf(id);
        if (additive && _selected.Contains(id)) { foreach (var member in members) _selected.Remove(member); }
        else
        {
            if (!additive) _selected.Clear();
            foreach (var member in members) if (!_selected.Contains(member)) _selected.Add(member);
            _selected.Remove(id); _selected.Add(id); // the one picked is the active one
        }
        Refresh();
    }

    public void Set(IEnumerable<Guid> ids)
    {
        _selected.Clear(); foreach (var id in ids) if (_sync.Views.ContainsKey(id) && !_selected.Contains(id)) _selected.Add(id);
        Refresh();
    }

    public void Clear() { if (_selected.Count == 0) return; _selected.Clear(); Refresh(); }

    private void Refresh()
    {
        foreach (var view in _sync.Views.Values) if (IsInstanceValid(view)) view.SetSelected(_selected.Contains(view.State.Id));
        Changed?.Invoke();
    }

    /// <summary>The object under a screen position, or none. The ray ignores the editor's own handles.</summary>
    public BuildObjectView? PickAt(Camera3D camera, Vector2 screen, World3D world, uint ignoreLayers = 0)
    {
        var origin = camera.ProjectRayOrigin(screen); var direction = camera.ProjectRayNormal(screen);
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + direction * 150f, uint.MaxValue & ~ignoreLayers);
        var hit = world.DirectSpaceState.IntersectRay(query);
        if (!hit.TryGetValue("collider", out var collider)) return null;
        var node = collider.AsGodotObject() as Node;
        while (node is not null and not BuildObjectView) node = node.GetParent();
        return node as BuildObjectView;
    }
}
