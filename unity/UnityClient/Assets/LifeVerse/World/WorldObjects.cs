using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using LifeVerse.Shared.Building;
using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>An object of the island as the API and the server describe it (the same fields as the Godot BuildObjectState).</summary>
    public sealed class WorldObjectState
    {
        public Guid Id, OwnerUserId; public Guid? GroupId, ParentObjectId;
        public string Name = "Object", PrimitiveType = "cube", MaterialJson = "{}", GeometryJson = "{}", PhysicsJson = "{}", ContentJson = "[]";
        public float PositionX, PositionY, PositionZ, RotationX, RotationY, RotationZ, ScaleX = 1, ScaleY = 1, ScaleZ = 1, PivotX, PivotY, PivotZ;
        public int Revision = 1;

        public static WorldObjectState Parse(JsonElement e)
        {
            Guid? Optional(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g) ? g : (Guid?)null;
            float Number(string name, float fallback = 0) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var n) ? n : fallback;
            string Text(string name, string fallback) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
            return new WorldObjectState
            {
                Id = e.GetProperty("id").GetGuid(), OwnerUserId = Optional("ownerUserId") ?? Guid.Empty, GroupId = Optional("groupId"), ParentObjectId = Optional("parentObjectId"),
                Name = Text("name", "Object"), PrimitiveType = Text("primitiveType", "cube"), MaterialJson = Text("materialJson", "{}"), GeometryJson = Text("geometryJson", "{}"),
                PhysicsJson = Text("physicsJson", "{}"), ContentJson = Text("contentJson", "[]"),
                PositionX = Number("positionX"), PositionY = Number("positionY"), PositionZ = Number("positionZ"), RotationX = Number("rotationX"), RotationY = Number("rotationY"), RotationZ = Number("rotationZ"),
                ScaleX = Number("scaleX", 1), ScaleY = Number("scaleY", 1), ScaleZ = Number("scaleZ", 1), PivotX = Number("pivotX"), PivotY = Number("pivotY"), PivotZ = Number("pivotZ"),
                Revision = e.TryGetProperty("revision", out var r) && r.TryGetInt32(out var revision) ? revision : 1,
            };
        }
    }

    /// <summary>
    /// The objects the players built on the island, drawn. It takes the list the API gives when joining and then the messages the island server broadcasts for every
    /// accepted build operation (create, update, delete, a linkset re-hung, a returned object, the poses of physical bodies) and keeps one GameObject per object, with the
    /// mesh of its shape (the shared PrimMesh and RoundMesh, the same geometry as the Godot client) and a material from its material JSON (colour, opacity, glow, roughness,
    /// metal; one entry per face). Positions and rotations go through <see cref="GameSpace"/>. Not yet: pictures on materials (textures), imported models, trees and
    /// grass (drawn as a simple stand-in), the pivot, collision, level of detail and the range culling of the Godot client.
    /// </summary>
    public sealed class WorldObjects : MonoBehaviour
    {
        public static readonly string[] StandIns = { "tree", "grass", "mesh" };
        public const string DefaultColor = "#A8C7D8";

        private sealed class Entry { public GameObject Object; public WorldObjectState State; public string ShapeKey = "", MaterialKey = ""; }

        private readonly Dictionary<Guid, Entry> _objects = new Dictionary<Guid, Entry>();
        private static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();
        private Material _opaque, _blend;

        public int Count => _objects.Count;
        public IReadOnlyCollection<Guid> Ids => _objects.Keys;
        public bool Has(Guid id) => _objects.ContainsKey(id);
        public GameObject ViewOf(Guid id) => _objects.TryGetValue(id, out var entry) ? entry.Object : null;
        public WorldObjectState StateOf(Guid id) => _objects.TryGetValue(id, out var entry) ? entry.State : null;

        private void Awake() => Prepare();

        private void Prepare()
        {
            if (_opaque != null) return;
            _opaque = Resources.Load<Material>("LifeVerse/ObjectMaterial"); _blend = Resources.Load<Material>("LifeVerse/ObjectMaterialBlend");
            if (_opaque == null) _opaque = new Material(Shader.Find("HDRP/Lit")); if (_blend == null) _blend = _opaque;
        }

        /// <summary>The list the API gives when joining: a JSON array of objects (a {"objects":[...]} wrapper is also accepted, as the island-content of the server has it).</summary>
        public int Load(string islandContentJson)
        {
            Prepare(); var count = 0;
            try
            {
                using var document = JsonDocument.Parse(islandContentJson);
                var list = document.RootElement;
                if (list.ValueKind == JsonValueKind.Object && !(list.TryGetProperty("objects", out list) && list.ValueKind == JsonValueKind.Array)) return 0;
                if (list.ValueKind != JsonValueKind.Array) return 0;
                foreach (var item in list.EnumerateArray()) { Upsert(WorldObjectState.Parse(item)); count++; }
            }
            catch (Exception exception) when (exception is JsonException || exception is KeyNotFoundException || exception is InvalidOperationException || exception is FormatException) { Debug.LogWarning("[LifeVerse] objetos da ilha ilegíveis: " + exception.Message); }
            return count;
        }

        /// <summary>One message of the island server (BuildOperationApplied). Returns false when the message was not about objects or could not be read.</summary>
        public bool ApplyOperation(string json)
        {
            Prepare();
            try
            {
                using var document = JsonDocument.Parse(json); var root = document.RootElement; var any = false;
                var operation = root.TryGetProperty("operation", out var op) && op.ValueKind == JsonValueKind.String ? op.GetString() : "";
                if (operation == "physics" && root.TryGetProperty("bodies", out var bodies) && bodies.ValueKind == JsonValueKind.Array) { ApplyPoses(bodies); return true; }
                if (operation is "lock" or "unlock" or "live" or "text" or "sound" or "sound_stop" or "effect" or "media") return false;
                if (root.TryGetProperty("objectState", out var state) && state.ValueKind == JsonValueKind.Object) { Upsert(WorldObjectState.Parse(state)); any = true; }
                else if (operation == "delete" || (operation != "bought" && root.TryGetProperty("objectId", out _) && !root.TryGetProperty("objects", out _)))
                {
                    if (root.TryGetProperty("objectId", out var id) && Guid.TryParse(id.GetString(), out var removed)) { Remove(removed); any = true; }
                }
                foreach (var listName in new[] { "objects", "healed" })
                    if (root.TryGetProperty(listName, out var list) && list.ValueKind == JsonValueKind.Array) foreach (var item in list.EnumerateArray()) { if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out _)) { Upsert(WorldObjectState.Parse(item)); any = true; } }
                return any;
            }
            catch (Exception exception) when (exception is JsonException || exception is KeyNotFoundException || exception is InvalidOperationException || exception is FormatException) { return false; }
        }

        public void Remove(Guid id) { if (_objects.TryGetValue(id, out var entry)) { Destroy(entry.Object); _objects.Remove(id); } }

        public void Clear() { foreach (var entry in _objects.Values) Destroy(entry.Object); _objects.Clear(); }

        private void ApplyPoses(JsonElement bodies)
        {
            foreach (var body in bodies.EnumerateArray())
            {
                if (!Guid.TryParse(body.GetProperty("id").GetString(), out var id) || !_objects.TryGetValue(id, out var entry)) continue;
                var p = body.GetProperty("p"); var q = body.GetProperty("q");
                entry.Object.transform.position = GameSpace.ToUnity(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle());
                entry.Object.transform.rotation = GameSpace.QuaternionToUnity(new Quaternion(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle()));
            }
        }

        private void Upsert(WorldObjectState state)
        {
            if (!_objects.TryGetValue(state.Id, out var entry))
            {
                var view = new GameObject("Object " + state.Id.ToString("N").Substring(0, 8)); view.transform.SetParent(transform, false);
                view.AddComponent<MeshFilter>(); view.AddComponent<MeshRenderer>();
                entry = new Entry { Object = view }; _objects[state.Id] = entry;
            }
            entry.State = state; var go = entry.Object; go.name = state.Name;
            go.transform.position = GameSpace.ToUnity(state.PositionX, state.PositionY, state.PositionZ);
            go.transform.rotation = GameSpace.RotationToUnity(state.RotationX, state.RotationY, state.RotationZ);
            go.transform.localScale = new Vector3(Mathf.Max(state.ScaleX, .001f), Mathf.Max(state.ScaleY, .001f), Mathf.Max(state.ScaleZ, .001f));
            var shapeKey = state.PrimitiveType.ToLowerInvariant() + "|" + state.GeometryJson;
            var filter = go.GetComponent<MeshFilter>(); var renderer = go.GetComponent<MeshRenderer>();
            if (entry.ShapeKey != shapeKey) { entry.ShapeKey = shapeKey; filter.sharedMesh = MeshFor(state); entry.MaterialKey = ""; }
            var materialKey = state.MaterialJson + "|" + filter.sharedMesh.subMeshCount;
            if (entry.MaterialKey != materialKey) { entry.MaterialKey = materialKey; renderer.sharedMaterials = MaterialsFor(state, filter.sharedMesh); }
        }

        // ---- shapes ----

        private static Mesh MeshFor(WorldObjectState state)
        {
            var type = state.PrimitiveType.ToLowerInvariant(); var key = type + "|" + state.GeometryJson;
            if (MeshCache.TryGetValue(key, out var cached) && cached != null) return cached;
            Mesh mesh;
            if (PrimMesh.Supports(type)) mesh = FromPrim(type, PrimParams.Parse(state.GeometryJson));
            else if (type == "plane") mesh = Quad();
            else mesh = FromPrim("cube", new PrimParams()); // a tree, grass or an imported model: a plain block stands in until those exist
            mesh.name = type; MeshCache[key] = mesh; return mesh;
        }

        private static Mesh FromPrim(string type, PrimParams parameters)
        {
            var data = PrimMesh.Build(type, parameters); var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var submeshes = new List<int[]>();
            foreach (var face in data.Faces)
            {
                var offset = vertices.Count;
                for (var i = 0; i < face.Positions.Length; i++)
                {
                    var p = face.Positions[i]; var n = face.Normals[i]; var uv = face.Uvs[i];
                    vertices.Add(new Vector3(p.X, p.Y, -p.Z)); normals.Add(new Vector3(n.X, n.Y, -n.Z)); uvs.Add(new Vector2(uv.X, 1f - uv.Y)); // z flipped (GameSpace); Unity's v starts at the bottom
                }
                var triangles = new int[face.Indices.Length]; for (var i = 0; i < triangles.Length; i++) triangles[i] = face.Indices[i] + offset;
                submeshes.Add(triangles);
            }
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.subMeshCount = submeshes.Count;
            for (var i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i);
            mesh.RecalculateBounds(); mesh.RecalculateTangents(); return mesh;
        }

        private static Mesh Quad()
        {
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(-.5f, 0, .5f), new Vector3(.5f, 0, .5f), new Vector3(.5f, 0, -.5f) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up }; mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.RecalculateBounds(); mesh.RecalculateTangents(); return mesh;
        }

        // ---- materials ----

        private Material[] MaterialsFor(WorldObjectState state, Mesh mesh)
        {
            JsonDocument document = null; JsonElement root = default; var valid = false;
            try { document = JsonDocument.Parse(string.IsNullOrWhiteSpace(state.MaterialJson) ? "{}" : state.MaterialJson); root = document.RootElement; valid = root.ValueKind == JsonValueKind.Object; } catch (JsonException) { }
            try
            {
                var materials = new Material[Mathf.Max(1, mesh.subMeshCount)];
                var faceIds = FaceIdsOf(state);
                for (var i = 0; i < materials.Length; i++)
                {
                    var faceId = i < faceIds.Length ? faceIds[i] : i;
                    materials[i] = valid ? Resolve(root, FaceEntry(root, faceId), state.MaterialJson + "#" + faceId) : _opaque;
                }
                return materials;
            }
            finally { document?.Dispose(); }
        }

        private static int[] FaceIdsOf(WorldObjectState state)
        {
            var type = state.PrimitiveType.ToLowerInvariant(); if (!PrimMesh.Supports(type)) return new[] { 0 };
            var data = PrimMesh.Build(type, PrimParams.Parse(state.GeometryJson)); var ids = new int[data.Faces.Count]; for (var i = 0; i < ids.Length; i++) ids[i] = data.Faces[i].Id; return ids;
        }

        private static JsonElement? FaceEntry(JsonElement root, int faceId)
        {
            if (!root.TryGetProperty("faces", out var faces) || faces.ValueKind != JsonValueKind.Array) return null;
            foreach (var face in faces.EnumerateArray()) if (face.ValueKind == JsonValueKind.Object && face.TryGetProperty("index", out var index) && index.TryGetInt32(out var i) && i == faceId) return face;
            return null;
        }

        private static string Text(JsonElement layer, JsonElement? over, string key)
        {
            if (over is { } o && o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();
            return layer.TryGetProperty(key, out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;
        }

        private static float Number(JsonElement layer, JsonElement? over, string key, float fallback)
        {
            if (over is { } o && o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number) return v.GetSingle();
            return layer.TryGetProperty(key, out var w) && w.ValueKind == JsonValueKind.Number ? w.GetSingle() : fallback;
        }

        private static bool Flag(JsonElement layer, JsonElement? over, string key)
        {
            if (over is { } o && o.TryGetProperty(key, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)) return v.GetBoolean();
            return layer.TryGetProperty(key, out var w) && w.ValueKind == JsonValueKind.True;
        }

        public static Color ColorOf(string hex, Color fallback) => hex != null && hex.Length == 7 && hex[0] == '#' && ColorUtility.TryParseHtmlString(hex, out var color) ? color : fallback;

        private Material Resolve(JsonElement layer, JsonElement? over, string cacheKey)
        {
            if (MaterialCache.TryGetValue(cacheKey, out var cached) && cached != null) return cached;
            var color = ColorOf(Text(layer, over, "color"), ColorOf(DefaultColor, Color.white));
            var roughness = Mathf.Clamp01(Number(layer, over, "roughness", .7f)); var metallic = Mathf.Clamp01(Number(layer, over, "metallic", 0f));
            var opacity = Mathf.Clamp01(Number(layer, over, "transparency", 1f)); var alphaMode = Text(layer, over, "alphaMode");
            var blend = alphaMode != "none" && opacity < .999f; // without a choice, an object with transparency below 1 blends (as in Godot)
            var material = new Material(blend ? _blend : _opaque);
            material.SetColor("_BaseColor", new Color(color.r, color.g, color.b, blend ? opacity : 1f));
            material.SetFloat("_Smoothness", 1f - roughness); material.SetFloat("_Metallic", metallic);
            if (Flag(layer, over, "doubleSided")) { material.SetFloat("_DoubleSidedEnable", 1f); material.SetFloat("_CullMode", 0f); material.SetFloat("_CullModeForward", 0f); }
            var glow = Text(layer, over, "emission");
            if (glow != null)
            {
                var strength = Mathf.Clamp(Number(layer, over, "emissionStrength", 1f), 0f, 16f); var e = ColorOf(glow, Color.black);
                material.SetColor("_EmissiveColor", new Color(e.r, e.g, e.b) * strength * 25f); // nits: a strength of 1 is a lamp you can see in daylight
            }
            if (Flag(layer, over, "fullBright")) material.SetColor("_EmissiveColor", color * 40f);
            MaterialCache[cacheKey] = material; return material;
        }

        private void OnDestroy() { Clear(); }
    }
}
