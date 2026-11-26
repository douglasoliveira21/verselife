using System;
using System.Collections.Generic;
using System.Linq;
using LifeVerse.IslandServer.Core;
using LifeVerse.IslandServer.Scripts;
using LifeVerse.Scripting;
using LifeVerse.Shared.Building;
using LifeVerse.Shared.Terrain;
using UnityEngine;

namespace LifeVerse.Unity.Server
{
    /// <summary>
    /// The physics of the island's objects on the PhysX of Unity: physical objects are rigid bodies, solid objects that do not move are fixed colliders and the terrain
    /// is a mesh collider rebuilt when it is sculpted. The world uses the game's numbers as they are (x, y, z and quaternions): rigid-body dynamics do not care about
    /// handedness, so the poses it produces mean the same thing to the clients. The simulation is stepped by the server (Physics.Simulate), never by the engine loop.
    /// </summary>
    public sealed class UnityIslandPhysics : IIslandPhysics
    {
        public const int BodyLimit = 200;
        /// <summary>
        /// Damping of the bodies. The Godot server uses .6 (angular) and .05 (linear); with the same numbers a PhysX ball rolls 11.6 m after the standard kick against 8.4 m in Godot,
        /// and 1.0 gives 8.1 m: the angular value is calibrated on that one scenario (rolling ball), so boxes that slide may still differ.
        /// </summary>
        public static float AngularDamping = 1.0f, LinearDamping = .05f;
        /// <summary>How the friction of a body and of what it touches are combined: Godot takes the smaller of the two (the ground and the fixed objects have friction 1, so a body keeps its own).</summary>
        public const PhysicsMaterialCombine FrictionCombineMode = PhysicsMaterialCombine.Minimum;
        /// <summary>What the ground and the fixed objects are made of, as in Godot, where they have no material of their own: friction 1, no bounce.</summary>
        private static PhysicsMaterial StillMaterial() => new PhysicsMaterial("still") { dynamicFriction = 1f, staticFriction = 1f, bounciness = 0f, frictionCombine = FrictionCombineMode, bounceCombine = PhysicsMaterialCombine.Maximum };
        private const float FellOutBelow = -60f, FixedStep = 1f / 60f;

        private sealed class Body
        {
            public GameObject Object; public Rigidbody Rigidbody; public string Signature = ""; public Vector3 StoredPosition; public Quaternion StoredRotation; public float Reach = .5f, GravityScale = 1f;
        }

        private sealed class Fixed { public GameObject Object; public string Signature = ""; }

        /// <summary>Marks an object of the island so a contact can say which one was touched.</summary>
        [ExecuteAlways]
        private sealed class BodyTag : MonoBehaviour
        {
            public Guid Id; public UnityIslandPhysics Owner;
            private void OnCollisionEnter(Collision collision) => Owner?.Contact(this, collision, true);
            private void OnCollisionExit(Collision collision) => Owner?.Contact(this, collision, false);
        }

        private readonly GameObject _root;
        private readonly Dictionary<Guid, Body> _bodies = new();
        private readonly Dictionary<Guid, Fixed> _fixed = new();
        private GameObject _ground;
        private TerrainMap _terrain;
        private long _terrainRevision = -1;
        private float _pending;

        public UnityIslandPhysics()
        {
            Physics.simulationMode = SimulationMode.Script;
            Physics.improvedPatchFriction = true;   // without it PhysX may apply up to twice the friction asked on a flat contact, and a box stops far sooner than in Godot
            _root = new GameObject("LifeVerse Island Physics");
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(_root);
        }

        public int BodyCount => _bodies.Count;
        public int FixedCount => _fixed.Count;
        public int MaxBodies => BodyLimit;
        public Action<Guid, Guid?, bool, float> Contacted { get; set; }
        public bool HasGround => _ground != null;

        private static void Kill(UnityEngine.Object obj) { if (obj == null) return; if (Application.isPlaying) UnityEngine.Object.Destroy(obj); else UnityEngine.Object.DestroyImmediate(obj); }

        // ---- the world the bodies live in ----

        public void SetTerrain(TerrainMap terrain)
        {
            if (ReferenceEquals(terrain, _terrain) && terrain.Revision == _terrainRevision) return;
            _terrain = terrain; _terrainRevision = terrain.Revision;
            Kill(_ground);
            var size = terrain.VertexCount; var half = terrain.HalfExtent; var cell = terrain.CellSize;
            var vertices = new Vector3[size * size];
            for (var z = 0; z < size; z++) for (var x = 0; x < size; x++) vertices[z * size + x] = new Vector3(x * cell - half, terrain.HeightAt(x, z), z * cell - half);
            var triangles = new int[(size - 1) * (size - 1) * 6]; var t = 0;
            for (var z = 0; z < size - 1; z++)
                for (var x = 0; x < size - 1; x++)
                {
                    var a = z * size + x; var b = (z + 1) * size + x; var c = z * size + x + 1; var d = (z + 1) * size + x + 1;
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = c; triangles[t++] = c; triangles[t++] = b; triangles[t++] = d; // clockwise seen from above: the ground faces up
                }
            var mesh = new Mesh { name = "IslandGround", indexFormat = vertices.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.vertices = vertices; mesh.triangles = triangles;
            _ground = new GameObject("Ground"); _ground.transform.SetParent(_root.transform, false);
            var floor = _ground.AddComponent<MeshCollider>(); floor.sharedMesh = mesh; floor.sharedMaterial = StillMaterial();
        }

        /// <summary>The object, as a body, a fixed collider or nothing: only what moves and what bodies can hit exists.</summary>
        private static bool IsBody(PhysicsSettings settings) => settings.Physical && settings.Collision && !settings.Trigger && settings.CollisionShape is not ("none" or "triangle");

        public void Sync(IEnumerable<PhysicsBodyDefinition> definitions)
        {
            var list = definitions as IReadOnlyList<PhysicsBodyDefinition> ?? definitions.ToList();
            var anyBody = list.Any(def => IsBody(PhysicsRules.Read(def.PhysicsJson))); // with nothing that moves, the fixed objects need no colliders at all
            var seenDynamic = new HashSet<Guid>(); var seenFixed = new HashSet<Guid>();
            foreach (var def in list)
            {
                var settings = PhysicsRules.Read(def.PhysicsJson);
                var mesh = def.PrimitiveType.Equals("mesh", StringComparison.OrdinalIgnoreCase);
                if (IsBody(settings))
                {
                    if (seenDynamic.Count >= BodyLimit) continue;
                    seenDynamic.Add(def.Id); SyncDynamic(def, settings);
                }
                else if (anyBody && def.Blocks && (!mesh || settings.CollisionShape is "box" or "sphere" or "cylinder" or "convex"))
                {
                    seenFixed.Add(def.Id); SyncFixed(def, settings);
                }
            }
            foreach (var id in _bodies.Keys.Where(id => !seenDynamic.Contains(id)).ToArray()) { Kill(_bodies[id].Object); _bodies.Remove(id); }
            foreach (var id in _fixed.Keys.Where(id => !seenFixed.Contains(id)).ToArray()) { Kill(_fixed[id].Object); _fixed.Remove(id); }
        }

        private static Quaternion RotationOf(PhysicsBodyDefinition def) => Quaternion.Euler(def.Rotation.X, def.Rotation.Y, def.Rotation.Z); // Z, then X, then Y: the Yxz order of the Godot server

        private static Mesh CylinderMesh(float radius, float height)
        {
            const int sides = 24;
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (var i = 0; i < sides; i++) { var angle = i / (float)sides * Mathf.PI * 2f; vertices.Add(new Vector3(Mathf.Cos(angle) * radius, -height / 2f, Mathf.Sin(angle) * radius)); vertices.Add(new Vector3(Mathf.Cos(angle) * radius, height / 2f, Mathf.Sin(angle) * radius)); }
            vertices.Add(new Vector3(0, -height / 2f, 0)); vertices.Add(new Vector3(0, height / 2f, 0));
            var bottom = sides * 2; var top = bottom + 1;
            for (var i = 0; i < sides; i++)
            {
                var a = i * 2; var b = (i + 1) % sides * 2;
                triangles.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1, bottom, b, a, top, a + 1, b + 1 });
            }
            var mesh = new Mesh { name = "CylinderCollider" }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); return mesh;
        }

        /// <summary>The solid shape standing in for an object: a ball for spheres (or when "esfera" was chosen), a cylinder for round things, a box for the rest (an imported model has no geometry here).</summary>
        private static float AddShape(GameObject target, string primitiveType, string choice, Vector3 scale, bool convexRequired)
        {
            var type = primitiveType.ToLowerInvariant();
            var kind = choice switch { "sphere" => "sphere", "cylinder" => "cylinder", _ => type switch { "sphere" => "sphere", "cylinder" or "cone" or "tube" => "cylinder", _ => "box" } };
            if (kind == "sphere") { var radius = Mathf.Max(.05f, .5f * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z))); target.AddComponent<SphereCollider>().radius = radius; return radius; }
            if (kind == "cylinder")
            {
                var radius = Mathf.Max(.05f, .5f * Mathf.Max(scale.x, scale.z)); var collider = target.AddComponent<MeshCollider>();
                collider.sharedMesh = CylinderMesh(radius, Mathf.Max(.05f, scale.y)); collider.convex = true; return radius;
            }
            var size = new Vector3(Mathf.Max(.05f, scale.x), Mathf.Max(.05f, scale.y), Mathf.Max(.05f, scale.z));
            target.AddComponent<BoxCollider>().size = size; return Mathf.Max(size.x, size.z) / 2f;
        }

        private void SyncDynamic(PhysicsBodyDefinition def, PhysicsSettings settings)
        {
            var scale = new Vector3(def.Scale.X, def.Scale.Y, def.Scale.Z);
            var position = new Vector3(def.Position.X, def.Position.Y, def.Position.Z); var rotation = RotationOf(def);
            var signature = string.Join("|", def.PrimitiveType, settings.CollisionShape, scale, settings.Mass, settings.Friction, settings.Bounce, settings.Physical);
            if (!_bodies.TryGetValue(def.Id, out var body))
            {
                var obj = new GameObject("Body_" + def.Id.ToString("N")); obj.transform.SetParent(_root.transform, false); obj.transform.SetPositionAndRotation(position, rotation);
                var rigidbody = obj.AddComponent<Rigidbody>(); rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                var tag = obj.AddComponent<BodyTag>(); tag.Id = def.Id; tag.Owner = this;
                body = new Body { Object = obj, Rigidbody = rigidbody, StoredPosition = position, StoredRotation = rotation };
                _bodies[def.Id] = body;
            }
            else if ((body.StoredPosition - position).sqrMagnitude > 1e-6f || Quaternion.Angle(body.StoredRotation, rotation) > .05f) // the builder (or a script) moved it: put the body there and let it start over
            {
                var rigidbody = body.Rigidbody; rigidbody.position = position; rigidbody.rotation = rotation; body.Object.transform.SetPositionAndRotation(position, rotation);
                rigidbody.linearVelocity = Vector3.zero; rigidbody.angularVelocity = Vector3.zero; rigidbody.WakeUp();
                body.StoredPosition = position; body.StoredRotation = rotation;
            }
            if (body.Signature == signature) return;
            body.Signature = signature;
            foreach (var collider in body.Object.GetComponents<Collider>()) Kill(collider);
            body.Reach = AddShape(body.Object, def.PrimitiveType, settings.CollisionShape, scale, true);
            // Godot adds the two bounces (the ground has none, so the body keeps its own) and Unity averages them by default: Maximum gives the Godot result against a ground without bounce
            // (between two bouncy bodies it gives less than Godot: a measured sum came out further from Godot than this)
            var material = new PhysicsMaterial { dynamicFriction = settings.Friction, staticFriction = settings.Friction, bounciness = settings.Bounce, bounceCombine = PhysicsMaterialCombine.Maximum, frictionCombine = FrictionCombineMode };
            foreach (var collider in body.Object.GetComponents<Collider>()) collider.sharedMaterial = material;
            var rb = body.Rigidbody; rb.mass = Mathf.Clamp(settings.Mass, PhysicsRules.MinMass, PhysicsRules.MaxMass); rb.angularDamping = AngularDamping; rb.linearDamping = LinearDamping; rb.WakeUp();
        }

        private void SyncFixed(PhysicsBodyDefinition def, PhysicsSettings settings)
        {
            var scale = new Vector3(def.Scale.X, def.Scale.Y, def.Scale.Z);
            var position = new Vector3(def.Position.X, def.Position.Y, def.Position.Z); var rotation = RotationOf(def);
            var signature = string.Join("|", def.PrimitiveType, settings.CollisionShape, scale, position, rotation.eulerAngles);
            if (_fixed.TryGetValue(def.Id, out var existing) && existing.Signature == signature) return;
            if (existing != null) Kill(existing.Object);
            var obj = new GameObject("Fixed_" + def.Id.ToString("N")); obj.transform.SetParent(_root.transform, false); obj.transform.SetPositionAndRotation(position, rotation);
            AddShape(obj, def.PrimitiveType, settings.CollisionShape, scale, false);
            foreach (var collider in obj.GetComponents<Collider>()) collider.sharedMaterial = StillMaterial();
            var tag = obj.AddComponent<BodyTag>(); tag.Id = def.Id; tag.Owner = this;
            _fixed[def.Id] = new Fixed { Object = obj, Signature = signature };
        }

        // ---- stepping ----

        public void Step(float seconds)
        {
            foreach (var body in _bodies.Values)
            {
                var rb = body.Rigidbody;
                if (rb.position.y < FellOutBelow) { rb.position = body.StoredPosition; rb.rotation = body.StoredRotation; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; continue; } // fell off the world: back where it was put
                if (body.GravityScale != 1f && !rb.IsSleeping()) rb.AddForce(Physics.gravity * (body.GravityScale - 1f), ForceMode.Acceleration);
            }
            _pending = Mathf.Min(_pending + seconds, FixedStep * 4f);
            while (_pending >= FixedStep) { Physics.Simulate(FixedStep); _pending -= FixedStep; }
        }

        public IReadOnlyList<BodyPose> Poses()
        {
            var poses = new List<BodyPose>(_bodies.Count);
            foreach (var pair in _bodies)
            {
                var rb = pair.Value.Rigidbody; var p = rb.position; var q = rb.rotation;
                poses.Add(new BodyPose(pair.Key, p.x, p.y, p.z, q.x, q.y, q.z, q.w, rb.IsSleeping()));
            }
            return poses;
        }

        // ---- people and scripts acting on bodies ----

        public void Push(float feetX, float feetY, float feetZ, float velocityX, float velocityZ)
        {
            const float avatar = .45f, height = 1.8f;
            var speed = Mathf.Sqrt(velocityX * velocityX + velocityZ * velocityZ);
            if (speed < .3f) return;
            foreach (var body in _bodies.Values)
            {
                var center = body.Rigidbody.position; var reach = body.Reach + avatar;
                if (center.y + reach < feetY || center.y - reach > feetY + height) continue;
                var away = new Vector3(center.x - feetX, 0, center.z - feetZ); var distance = away.magnitude;
                if (distance >= reach || distance < 1e-4f) continue;
                body.Rigidbody.WakeUp();
                body.Rigidbody.AddForce(away / distance * Mathf.Min(speed, 9f) * Mathf.Clamp(body.Rigidbody.mass, .5f, 20f) * .9f, ForceMode.Impulse);
            }
        }

        public bool Apply(Guid id, string action, Vec3 value)
        {
            if (!_bodies.TryGetValue(id, out var body)) return false;
            var vector = new Vector3(value.X, value.Y, value.Z); var rb = body.Rigidbody;
            switch (action)
            {
                case "impulse": rb.linearVelocity += vector / rb.mass; break; // at once, so a script that asks for the velocity right after sees the change
                case "velocity": rb.linearVelocity = vector; break;
                case "angular": rb.angularVelocity = vector; break;
                case "gravity": body.GravityScale = value.X; break;
                default: return false;
            }
            rb.WakeUp();
            return true;
        }

        public Vec3? VelocityOf(Guid id) => _bodies.TryGetValue(id, out var body) ? new Vec3(body.Rigidbody.linearVelocity.x, body.Rigidbody.linearVelocity.y, body.Rigidbody.linearVelocity.z) : null;

        private void Contact(BodyTag self, Collision collision, bool entered)
        {
            if (Contacted == null) return;
            Guid? other = null;
            if (collision.gameObject != null && collision.gameObject.TryGetComponent<BodyTag>(out var tag)) other = tag.Id;
            else if (collision.gameObject == null || collision.gameObject != _ground) return;
            // when it starts, how hard it hit (the relative speed before the impact; the body's own speed is already the one after it); when it ends, how fast it leaves
            Contacted(self.Id, other, entered, entered ? collision.relativeVelocity.magnitude : _bodies.TryGetValue(self.Id, out var body) ? body.Rigidbody.linearVelocity.magnitude : 0f);
        }

        public Rigidbody BodyOf(Guid id) => _bodies.TryGetValue(id, out var body) ? body.Rigidbody : null;

        /// <summary>Gone at once, not at the end of the frame: Unity has a single physics world, and what is only "to be destroyed" still collides with whatever comes next.</summary>
        public void Dispose() { if (_root != null) UnityEngine.Object.DestroyImmediate(_root); _bodies.Clear(); _fixed.Clear(); }
    }
}
