using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Build;

/// <summary>
/// Building together: who is editing what. The island server hands out short leases (20 s) on the objects a player has selected; this keeps mine in step with my selection (take what I
/// select, give back what I let go, renew while I work), remembers the ones other people hold, tags those objects in the world and refuses edits to them before asking the server (which
/// refuses too). When somebody drags an object the others see it move as it happens; if the drag is dropped without being saved, the object goes back to where it really is. When the
/// connection comes back, the leases are asked for again.
/// </summary>
public partial class BuildLockManager : Node
{
    public sealed record Holder(Guid User, string Name, long ExpiresMs);

    public const double RenewSeconds = 8, LiveSeconds = .12;

    private BuildNetworkSynchronizer _sync = null!;
    private BuildSelectionManager _selection = null!;
    private readonly Dictionary<Guid, Holder> _others = [];
    private readonly HashSet<Guid> _mine = [];
    private double _renewClock, _liveClock;
    private bool _active;
    private Func<long> _now = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public IReadOnlyCollection<Guid> Mine => _mine;
    public int OthersCount => _others.Count;
    public Func<long> Clock { set => _now = value; }

    public void Initialize(BuildNetworkSynchronizer sync, BuildSelectionManager selection)
    {
        _sync = sync; _selection = selection;
        sync.LockGate = ids => FirstHeldByOther(ids);
        sync.LockMessage += Handle; sync.LiveMoved += OnLive; sync.Reconnected += OnReconnected;
        sync.ObjectRemoved += id => { _others.Remove(id); _mine.Remove(id); };
        selection.Changed += Sync;
    }

    public bool LockedByOther(Guid id, out string name)
    {
        name = string.Empty;
        if (!_others.TryGetValue(id, out var holder) || holder.ExpiresMs <= _now() || holder.User == _sync.MyUserId && _sync.MyUserId != Guid.Empty) return false;
        name = holder.Name; return true;
    }

    public string? FirstHeldByOther(IEnumerable<Guid> ids) { foreach (var id in ids) if (LockedByOther(id, out var name)) return name; return null; }

    public string? SelectionHeldBy() => FirstHeldByOther(_selection.Ids);

    public void SetActive(bool active) { _active = active; if (active) Sync(); else ReleaseAll(); }

    /// <summary>Takes the leases on what is selected (and free) and gives back the rest.</summary>
    public void Sync()
    {
        if (!_active) return;
        var wanted = _selection.Ids.Where(id => !LockedByOther(id, out _)).ToHashSet();
        var give = _mine.Where(id => !wanted.Contains(id)).ToArray();
        if (give.Length > 0) { _sync.SendRaw(Command("unlock", give)); foreach (var id in give) _mine.Remove(id); }
        var take = wanted.Where(id => !_mine.Contains(id)).ToArray();
        if (take.Length > 0) { _sync.SendRaw(Command("lock", wanted.ToArray())); foreach (var id in take) _mine.Add(id); }
    }

    private void ReleaseAll() { if (_mine.Count == 0) return; _sync.SendRaw(Command("unlock", _mine.ToArray())); _mine.Clear(); }

    private static string Command(string operation, Guid[] ids) => JsonSerializer.Serialize(new { operation, objectIds = ids });

    public void Renew() { if (_active && _mine.Count > 0) _sync.SendRaw(Command("lock", _mine.ToArray())); }

    public override void _Process(double delta)
    {
        if (!_active) return;
        _renewClock += delta; if (_renewClock >= RenewSeconds) { _renewClock = 0; Renew(); ForgetExpired(); }
    }

    private void ForgetExpired()
    {
        var now = _now();
        foreach (var id in _others.Where(p => p.Value.ExpiresMs <= now).Select(p => p.Key).ToList()) { _others.Remove(id); Tag(id); Restore(id); }
    }

    /// <summary>A lock or unlock announced by the island.</summary>
    private void Handle(string operation, JsonElement root)
    {
        if (!root.TryGetProperty("objectIds", out var ids) || ids.ValueKind != JsonValueKind.Array) return;
        var user = root.TryGetProperty("userId", out var u) && Guid.TryParse(u.GetString(), out var parsed) ? parsed : Guid.Empty;
        var name = root.TryGetProperty("userName", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "outro jogador" : "outro jogador";
        var expires = root.TryGetProperty("expiresAtMs", out var e) && e.TryGetInt64(out var ms) ? ms : _now() + 20_000;
        foreach (var raw in ids.EnumerateArray())
        {
            if (!Guid.TryParse(raw.GetString(), out var id)) continue;
            if (user != Guid.Empty && user == _sync.MyUserId) { if (operation == "lock") _mine.Add(id); else _mine.Remove(id); continue; }
            if (operation == "lock") { _others[id] = new Holder(user, name, expires); if (_mine.Remove(id)) { /* the island gave it to someone else first */ } }
            else if (_others.TryGetValue(id, out var current) && current.User == user) { _others.Remove(id); Restore(id); }
            Tag(id);
        }
        Changed?.Invoke();
    }

    public event Action? Changed;

    private void Tag(Guid id) { if (_sync.Views.TryGetValue(id, out var view) && IsInstanceValid(view)) view.SetLockedBy(LockedByOther(id, out var name) ? name : null); }

    /// <summary>Puts an object back where it really is (after somebody's live drag ended without a save).</summary>
    private void Restore(Guid id) { if (_mine.Contains(id) || !_sync.Views.TryGetValue(id, out var view) || !IsInstanceValid(view)) return; view.Apply(view.State); }

    /// <summary>Somebody else is dragging: show the objects where they are now, but only objects that person really holds.</summary>
    private void OnLive(Guid user, IReadOnlyList<LivePose> poses)
    {
        foreach (var pose in poses)
        {
            if (!_others.TryGetValue(pose.Id, out var holder) || holder.User != user || holder.ExpiresMs <= _now()) continue;
            if (!_sync.Views.TryGetValue(pose.Id, out var view) || !IsInstanceValid(view)) continue;
            view.Position = new Vector3(pose.PositionX, pose.PositionY, pose.PositionZ);
            view.Basis = BuildGizmoMath.BasisOf(pose.RotationX, pose.RotationY, pose.RotationZ); view.Scale = new Vector3(pose.ScaleX, pose.ScaleY, pose.ScaleZ);
        }
        LiveShown?.Invoke(user, poses.Count);
    }

    public event Action<Guid, int>? LiveShown;

    /// <summary>The connection came back and the objects were loaded again: nobody's lease is known any more, and mine are asked for again.</summary>
    private void OnReconnected()
    {
        _others.Clear(); var again = _mine.ToArray(); _mine.Clear();
        if (_active && again.Length > 0) { _sync.SendRaw(Command("lock", again)); foreach (var id in again) _mine.Add(id); }
        Changed?.Invoke();
    }

    /// <summary>While I drag, the others are shown where the objects are (about eight times a second).</summary>
    public void Stream(IEnumerable<BuildObjectView> dragged, double delta)
    {
        _liveClock += delta; if (_liveClock < LiveSeconds) return; _liveClock = 0;
        var poses = dragged.Where(v => IsInstanceValid(v) && _mine.Contains(v.State.Id)).Select(v =>
        {
            var euler = BuildGizmoMath.EulerDegrees(v.Basis); var scale = v.Scale;
            return new LivePose(v.State.Id, v.Position.X, v.Position.Y, v.Position.Z, euler.X, euler.Y, euler.Z, scale.X, scale.Y, scale.Z);
        }).ToList();
        if (poses.Count > 0) _sync.SendLive(poses);
    }
}
