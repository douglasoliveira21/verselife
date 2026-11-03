using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

public readonly record struct ModelInstance(Node3D Node, Vector3 Size);

/// Downloads and prepares the imported 3D models (glTF/GLB) that "mesh" objects show. Each model is fetched once
/// from the island's asset endpoint, normalised so its longest side is one metre (the object's scale then sets its
/// real size) and centred on the origin; every object gets its own copy.
public partial class ModelCache : Node
{
    private const int MaxNodes = 2000;
    private sealed record Entry(Node3D? Prototype, Vector3 Size);
    private readonly Dictionary<Guid, Entry> _ready = [];
    private readonly Dictionary<Guid, List<Action<ModelInstance?>>> _waiting = [];

    public int CachedModels => _ready.Count;

    public void Request(Guid assetId, Action<ModelInstance?> done)
    {
        if (_ready.TryGetValue(assetId, out var entry)) { done(Instance(entry)); return; }
        if (_waiting.TryGetValue(assetId, out var list)) { list.Add(done); return; }
        _waiting[assetId] = [done];
        Fetch(assetId);
    }

    /// Supplies a model's bytes directly (used by tests and by the importer, which already has them).
    public bool Preload(Guid assetId, byte[] bytes)
    {
        var entry = Prepare(bytes);
        _ready[assetId] = entry;
        Resolve(assetId, entry);
        return entry.Prototype is not null;
    }

    public override void _ExitTree() { foreach (var entry in _ready.Values) entry.Prototype?.Free(); _ready.Clear(); }

    private void Fetch(Guid assetId)
    {
        var network = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (network is null || session is null || app is null || !session.IsAuthenticated || network.CurrentIslandId == Guid.Empty) { Fail(assetId); return; }
        var http = new HttpRequest { Timeout = 60, BodySizeLimit = 120 * 1024 * 1024 };
        AddChild(http);
        http.RequestCompleted += (_, code, _, body) =>
        {
            http.QueueFree();
            if (code is < 200 or >= 300) { Fail(assetId); return; }
            var entry = Prepare(body);
            _ready[assetId] = entry;
            Resolve(assetId, entry);
        };
        if (http.Request($"{app.ApiBaseUrl}/api/v1/worlds/islands/{network.CurrentIslandId}/assets/{assetId}/file", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) { http.QueueFree(); Fail(assetId); }
    }

    private void Fail(Guid assetId) { var entry = new Entry(null, Vector3.One); _ready[assetId] = entry; Resolve(assetId, entry); }

    private void Resolve(Guid assetId, Entry entry)
    {
        if (!_waiting.Remove(assetId, out var callbacks)) return;
        foreach (var callback in callbacks) callback(Instance(entry));
    }

    private static ModelInstance? Instance(Entry entry) => entry.Prototype is null ? null : new ModelInstance((Node3D)entry.Prototype.Duplicate(), entry.Size);

    /// Parses a glTF/GLB buffer into a centred, one-metre prototype. Failures yield an entry without a model.
    public static (Node3D? Node, Vector3 Size) Parse(byte[] bytes)
    {
        var entry = Prepare(bytes);
        return (entry.Prototype, entry.Size);
    }

    private static Entry Prepare(byte[] bytes)
    {
        var document = new GltfDocument();
        var state = new GltfState();
        // No base path: references to external files cannot resolve, so a model can only use what it embeds.
        if (document.AppendFromBuffer(bytes, string.Empty, state) != Error.Ok) return new Entry(null, Vector3.One);
        var scene = document.GenerateScene(state) as Node3D;
        if (scene is null) return new Entry(null, Vector3.One);
        if (Count(scene) > MaxNodes) { scene.Free(); return new Entry(null, Vector3.One); }
        var bounds = Bounds(scene);
        if (bounds is not { } box || box.Size.LengthSquared() < 1e-8f) { scene.Free(); return new Entry(null, Vector3.One); }
        var longest = Mathf.Max(box.Size.X, Mathf.Max(box.Size.Y, box.Size.Z));
        var factor = 1f / longest;
        var wrapper = new Node3D { Name = "Model" };
        scene.Scale = Vector3.One * factor;
        scene.Position = -(box.Position + box.Size / 2f) * factor;
        wrapper.AddChild(scene);
        return new Entry(wrapper, box.Size * factor);
    }

    private static int Count(Node node) { var total = 1; foreach (var child in node.GetChildren()) total += Count(child); return total; }

    /// Bounding box of every mesh under a root, in the root's own space (the nodes are not in a scene tree yet).
    public static Aabb? Bounds(Node3D root)
    {
        Aabb? result = null;
        void Visit(Node3D node, Transform3D parent)
        {
            var current = parent * node.Transform;
            if (node is MeshInstance3D { Mesh: not null } mesh)
            {
                var local = mesh.Mesh.GetAabb();
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = current * (local.Position + new Vector3((corner & 1) * local.Size.X, ((corner >> 1) & 1) * local.Size.Y, ((corner >> 2) & 1) * local.Size.Z));
                    result = result is { } existing ? existing.Expand(point) : new Aabb(point, Vector3.Zero);
                }
            }
            foreach (var child in node.GetChildren()) if (child is Node3D childNode) Visit(childNode, current);
        }
        Visit(root, Transform3D.Identity);
        return result;
    }
}
