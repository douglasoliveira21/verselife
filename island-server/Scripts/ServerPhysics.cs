using System.Text.Json;
using Godot;
using LifeVerse.Shared.Building;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>
/// The physics of the island's objects. An object marked "physical" in the Construir window becomes a rigid body here (gravity, collisions, rolling, bouncing);
/// everything solid that is not physical stands in as a fixed body and the terrain is the ground. Only this server simulates, so everybody sees the same ball
/// in the same place: the poses of the moving bodies are sent to the players a few times per second (the "physics" operation) and each client smooths them.
/// Imported models have no geometry on the server: a physical model uses the shape chosen for it (box by default) and a fixed model does not stop bodies.
/// </summary>
public partial class ServerPhysics : Node3D
{
    public const int MaxBodies = 200;
    public const int PosesPerMessage = 40;
    public const float SendInterval = .1f;
    private const float MovedEnough = .004f;
    private const float FellOutBelow = -60f;

    private sealed class Dynamic
    {
        public RigidBody3D Node = null!; public string Signature = ""; public Transform3D Stored; public Vector3 SentPosition; public Quaternion SentRotation; public bool SentAsleep = true; public bool Mesh;
    }

    private sealed class Fixed { public StaticBody3D Node = null!; public string Signature = ""; }

    private readonly Dictionary<Guid, Dynamic> _bodies = [];
    private readonly Dictionary<Guid, Fixed> _fixed = [];
    private StaticBody3D? _ground;
    private TerrainMap? _terrain;
    private long _terrainRevision = -1;
    private double _sendTimer;
    private Action<string> _broadcast = _ => { };

    public int BodyCount => _bodies.Count;
    public int FixedCount => _fixed.Count;
    public bool HasGround => _ground is not null;

    public void Initialize(Action<string> broadcast) => _broadcast = broadcast;

    // ---- the world the bodies live in ---------------------------------------------------------------------------

    /// <summary>The terrain is the ground; it is built again when it has been sculpted.</summary>
    public void SetTerrain(TerrainMap terrain)
    {
        if (ReferenceEquals(terrain, _terrain) && terrain.Revision == _terrainRevision) return;
        _terrain = terrain; _terrainRevision = terrain.Revision;
        _ground?.QueueFree();
        var size = terrain.VertexCount; var data = new float[size * size];
        for (var z = 0; z < size; z++) for (var x = 0; x < size; x++) data[z * size + x] = terrain.HeightAt(x, z);
        var shape = new HeightMapShape3D { MapWidth = size, MapDepth = size, MapData = data };
        _ground = new StaticBody3D { Name = "Ground", CollisionLayer = 1, CollisionMask = 1 };
        _ground.AddChild(new CollisionShape3D { Shape = shape, Scale = new Vector3(terrain.CellSize, 1, terrain.CellSize) });
        AddChild(_ground);
    }

    /// <summary>Reads the objects again: new physical objects become bodies, moved ones are put back where the builder left them, removed ones go away.</summary>
    public void Sync(IEnumerable<PhysicsBodyDefinition> definitions)
    {
        var list = definitions as IReadOnlyList<PhysicsBodyDefinition> ?? definitions.ToList();
        var anyBody = list.Any(def => IsBody(def, PhysicsRules.Read(def.PhysicsJson))); // with nothing that moves, the fixed objects need no bodies at all
        var seenDynamic = new HashSet<Guid>(); var seenFixed = new HashSet<Guid>();
        foreach (var def in list)
        {
            var settings = PhysicsRules.Read(def.PhysicsJson);
            var mesh = def.PrimitiveType.Equals("mesh", StringComparison.OrdinalIgnoreCase);
            var transform = PoseOf(def);
            if (IsBody(def, settings))
            {
                if (seenDynamic.Count >= MaxBodies) continue;
                seenDynamic.Add(def.Id); SyncDynamic(def, settings, transform, mesh);
            }
            else if (anyBody && def.Blocks && !mesh || anyBody && def.Blocks && mesh && settings.CollisionShape is "box" or "sphere" or "cylinder" or "convex")
            {
                seenFixed.Add(def.Id); SyncFixed(def, settings, transform);
            }
        }
        foreach (var id in _bodies.Keys.Where(id => !seenDynamic.Contains(id)).ToArray()) { _bodies[id].Node.QueueFree(); _bodies.Remove(id); }
        foreach (var id in _fixed.Keys.Where(id => !seenFixed.Contains(id)).ToArray()) { _fixed[id].Node.QueueFree(); _fixed.Remove(id); }
    }

    private static bool IsBody(PhysicsBodyDefinition def, PhysicsSettings settings) => settings.Physical && settings.Collision && !settings.Trigger && settings.CollisionShape is not ("none" or "triangle");

    private static Transform3D PoseOf(PhysicsBodyDefinition def) =>
        new(Basis.FromEuler(new Vector3(Mathf.DegToRad(def.Rotation.X), Mathf.DegToRad(def.Rotation.Y), Mathf.DegToRad(def.Rotation.Z)), EulerOrder.Yxz), new Vector3(def.Position.X, def.Position.Y, def.Position.Z));

    /// <summary>The solid shape standing in for an object: a ball for spheres (or when "esfera" was chosen), a cylinder for round things, a box for the rest.</summary>
    public static Shape3D ShapeFor(string primitiveType, string choice, Vector3 scale)
    {
        var type = primitiveType.ToLowerInvariant();
        // "sphere" and "cylinder" are chosen explicitly; otherwise a primitive follows its own form and an imported model (no geometry here) is a box
        var kind = choice switch { "sphere" => "sphere", "cylinder" => "cylinder", _ => type switch { "sphere" => "sphere", "cylinder" or "cone" or "tube" => "cylinder", _ => "box" } };
        if (kind == "sphere") return new SphereShape3D { Radius = Mathf.Max(.05f, .5f * Mathf.Max(scale.X, Mathf.Max(scale.Y, scale.Z))) };
        if (kind == "cylinder") return new CylinderShape3D { Radius = Mathf.Max(.05f, .5f * Mathf.Max(scale.X, scale.Z)), Height = Mathf.Max(.05f, scale.Y) };
        return new BoxShape3D { Size = new Vector3(Mathf.Max(.05f, scale.X), Mathf.Max(.05f, scale.Y), Mathf.Max(.05f, scale.Z)) };
    }

    private static string SignatureOf(PhysicsBodyDefinition def, PhysicsSettings settings) =>
        string.Join('|', def.PrimitiveType, settings.CollisionShape, def.Scale, settings.Mass, settings.Friction, settings.Bounce, settings.Physical);

    private void SyncDynamic(PhysicsBodyDefinition def, PhysicsSettings settings, Transform3D stored, bool mesh)
    {
        var scale = new Vector3(def.Scale.X, def.Scale.Y, def.Scale.Z); var signature = SignatureOf(def, settings);
        if (!_bodies.TryGetValue(def.Id, out var body))
        {
            body = new Dynamic { Node = new RigidBody3D { Name = "Body_" + def.Id.ToString("N"), CollisionLayer = 1, CollisionMask = 1, ContinuousCd = true, CanSleep = true }, Mesh = mesh };
            AddChild(body.Node); _bodies[def.Id] = body;
            body.Node.GlobalTransform = stored; body.Stored = stored; body.SentPosition = stored.Origin; body.SentRotation = stored.Basis.GetRotationQuaternion();
        }
        else if (!SamePose(body.Stored, stored)) // the builder (or a script) moved it: put the body there and let it start over
        {
            body.Node.GlobalTransform = stored; body.Node.LinearVelocity = Vector3.Zero; body.Node.AngularVelocity = Vector3.Zero; body.Stored = stored; body.Node.Sleeping = false;
        }
        if (body.Signature == signature) return;
        body.Signature = signature;
        foreach (var child in body.Node.GetChildren()) { body.Node.RemoveChild(child); child.QueueFree(); }
        body.Node.AddChild(new CollisionShape3D { Shape = ShapeFor(def.PrimitiveType, settings.CollisionShape, scale) });
        body.Node.Mass = Mathf.Clamp(settings.Mass, PhysicsRules.MinMass, PhysicsRules.MaxMass);
        body.Node.PhysicsMaterialOverride = new PhysicsMaterial { Friction = settings.Friction, Bounce = settings.Bounce, Rough = false, Absorbent = false };
        body.Node.AngularDamp = .6f; body.Node.LinearDamp = .05f; body.Node.Sleeping = false;
    }

    private void SyncFixed(PhysicsBodyDefinition def, PhysicsSettings settings, Transform3D stored)
    {
        var scale = new Vector3(def.Scale.X, def.Scale.Y, def.Scale.Z); var signature = string.Join('|', def.PrimitiveType, settings.CollisionShape, def.Scale, stored.Origin, stored.Basis.GetEuler());
        if (_fixed.TryGetValue(def.Id, out var existing) && existing.Signature == signature) return;
        existing?.Node.QueueFree();
        var node = new StaticBody3D { Name = "Fixed_" + def.Id.ToString("N"), CollisionLayer = 1, CollisionMask = 1, Transform = stored };
        node.AddChild(new CollisionShape3D { Shape = ShapeFor(def.PrimitiveType, settings.CollisionShape, scale) });
        AddChild(node); _fixed[def.Id] = new Fixed { Node = node, Signature = signature };
    }

    private static bool SamePose(Transform3D a, Transform3D b) => a.Origin.DistanceSquaredTo(b.Origin) < 1e-6f && a.Basis.GetEuler().DistanceSquaredTo(b.Basis.GetEuler()) < 1e-6f;

    // ---- stepping and telling the players ---------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        foreach (var (id, body) in _bodies.ToArray())
            if (body.Node.GlobalPosition.Y < FellOutBelow) { body.Node.GlobalTransform = body.Stored; body.Node.LinearVelocity = Vector3.Zero; body.Node.AngularVelocity = Vector3.Zero; } // fell off the world: back where it was put
        _sendTimer += delta;
        if (_sendTimer < SendInterval) return;
        _sendTimer = 0;
        var changed = new List<object>();
        foreach (var (id, body) in _bodies)
        {
            var position = body.Node.GlobalPosition; var rotation = body.Node.GlobalTransform.Basis.GetRotationQuaternion(); var asleep = body.Node.Sleeping;
            if (asleep && body.SentAsleep) continue;
            if (!asleep && position.DistanceTo(body.SentPosition) < MovedEnough && body.SentRotation.AngleTo(rotation) < .01f && !body.SentAsleep) continue;
            body.SentPosition = position; body.SentRotation = rotation; body.SentAsleep = asleep;
            changed.Add(PoseOf(id, position, rotation, asleep));
        }
        for (var start = 0; start < changed.Count; start += PosesPerMessage)
            _broadcast(JsonSerializer.Serialize(new { operation = "physics", bodies = changed.Skip(start).Take(PosesPerMessage) }));
    }

    private static object PoseOf(Guid id, Vector3 p, Quaternion q, bool asleep) =>
        new { id, p = new[] { Round(p.X), Round(p.Y), Round(p.Z) }, q = new[] { Round(q.X), Round(q.Y), Round(q.Z), Round(q.W) }, asleep };

    private static float Round(float value) => MathF.Round(value, 3);

    /// <summary>The pose of every body, as messages for somebody who has just arrived.</summary>
    public IEnumerable<string> PoseMessages()
    {
        var all = _bodies.Select(pair => PoseOf(pair.Key, pair.Value.Node.GlobalPosition, pair.Value.Node.GlobalTransform.Basis.GetRotationQuaternion(), pair.Value.Node.Sleeping)).ToList();
        for (var start = 0; start < all.Count; start += PosesPerMessage) yield return JsonSerializer.Serialize(new { operation = "physics", bodies = all.Skip(start).Take(PosesPerMessage) });
    }

    // ---- people pushing things ---------------------------------------------------------------------------------

    /// <summary>A walking avatar shoves the bodies it touches: a ball is kicked away from the feet and goes with the speed of the walker.</summary>
    public void Push(Vector3 feet, Vector3 velocity)
    {
        const float body = .45f, height = 1.8f;
        foreach (var dynamic in _bodies.Values)
        {
            var node = dynamic.Node; var center = node.GlobalPosition; var reach = ReachOf(node) + body;
            if (center.Y + reach < feet.Y || center.Y - reach > feet.Y + height) continue;
            var away = new Vector3(center.X - feet.X, 0, center.Z - feet.Z); var distance = away.Length();
            if (distance >= reach || distance < 1e-4f) continue;
            var speed = new Vector2(velocity.X, velocity.Z).Length();
            if (speed < .3f) continue;
            node.Sleeping = false;
            var push = away / distance * Mathf.Min(speed, 9f) * Mathf.Clamp(node.Mass, .5f, 20f) * .9f;
            node.ApplyCentralImpulse(push);
        }
    }

    private static float ReachOf(RigidBody3D node)
    {
        foreach (var child in node.GetChildren())
            if (child is CollisionShape3D { Shape: { } shape })
                return shape switch { SphereShape3D s => s.Radius, CylinderShape3D c => c.Radius, BoxShape3D b => Mathf.Max(b.Size.X, b.Size.Z) / 2f, _ => .5f };
        return .5f;
    }

    /// <summary>For tests: the body of an object, when there is one.</summary>
    public RigidBody3D? BodyOf(Guid id) => _bodies.TryGetValue(id, out var body) ? body.Node : null;
}
