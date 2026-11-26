using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using LifeVerse.Shared.Building;
using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>An object of the island as the API and the server describe it (the same fields as the Godot BuildObjectState).</summary>
    public sealed class WorldObjectState
    {
        public Guid Id, OwnerUserId, CreatorUserId; public Guid? GroupId, ParentObjectId; public string Description = "", PermissionsJson = "{}"; public bool EditLocked; public int OwnerRights = 15, NextOwnerRights = 15;
        public string Name = "Object", PrimitiveType = "cube", MaterialJson = "{}", GeometryJson = "{}", PhysicsJson = "{}", ContentJson = "[]";
        public float PositionX, PositionY, PositionZ, RotationX, RotationY, RotationZ, ScaleX = 1, ScaleY = 1, ScaleZ = 1, PivotX, PivotY, PivotZ;
        public int Revision = 1;
        public WorldObjectState Copy() => (WorldObjectState)MemberwiseClone();

        public static WorldObjectState Parse(JsonElement e)
        {
            Guid? Optional(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g) ? g : (Guid?)null;
            float Number(string name, float fallback = 0) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var n) ? n : fallback;
            string Text(string name, string fallback) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
            return new WorldObjectState
            {
                Id = e.GetProperty("id").GetGuid(), OwnerUserId = Optional("ownerUserId") ?? Guid.Empty, CreatorUserId = Optional("creatorUserId") ?? Guid.Empty, Description = Text("description", ""), PermissionsJson = Text("permissionsJson", "{}"), EditLocked = e.TryGetProperty("editLocked", out var locked) && locked.ValueKind == JsonValueKind.True, OwnerRights = (int)Number("ownerRights", 15), NextOwnerRights = (int)Number("nextOwnerRights", 15), GroupId = Optional("groupId"), ParentObjectId = Optional("parentObjectId"),
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
    /// mesh of its shape (the shared PrimMesh and RoundMesh, the same geometry as the Godot client; trees and grass generated; imported glTF models loaded from the API)
    /// and a material from its material JSON: colour, opacity, glow, roughness, metal, double sided, one entry per face, and the pictures (base colour, normal, ORM and
    /// emissive maps, with the repeat and offset of the base map) fetched through <see cref="AssetLoader"/>. Objects beyond their range are not drawn (a 1 m box vanishes
    /// past 120 m, a 10 m wall past 600 m, as in Godot). Positions and rotations go through <see cref="GameSpace"/>. Not yet: planar mapping, a rotation and separate repeat for
    /// each map (HDRP shares the base map's), the Blinn-Phong shading model, model animations, the pivot, collision and mesh simplification for far models.
    /// </summary>
    public sealed partial class WorldObjects : MonoBehaviour
    {
        public const string DefaultColor = "#A8C7D8";
        public const float RangePerMetre = 120f, MinRange = 40f, MaxRange = 600f;
        public const int MaxTextureSize = 2048;

        private sealed class Entry
        {
            public GameObject Object; public WorldObjectState State; public string ShapeKey = "", MaterialKey = ""; public int Generation;
            public Renderer[] Renderers = Array.Empty<Renderer>(); public float Range = MaxRange; public bool Culled; public GameObject Proxy, Model, Lamp, Probe; public string ClipPlaying = ""; public List<(MeshFilter Filter, Mesh Full, Mesh Low)> Lods; public bool LowShown; public UnityEngine.Video.VideoPlayer Video; public RenderTexture VideoTarget; public Material[] MediaMaterials;
        }

        private readonly Dictionary<Guid, Entry> _objects = new Dictionary<Guid, Entry>();
        private static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();
        private static readonly Dictionary<Guid, Task<Texture2D>> TextureCache = new Dictionary<Guid, Task<Texture2D>>();
        private Material _opaque, _blend, _opaqueDouble, _blendDouble;
        private float _cullClock;

        /// <summary>Gets the bytes of an asset of the island (a texture, a model). Set by <see cref="WorldJoin"/>; without it pictures and models are skipped.</summary>
        public Func<Guid, Task<byte[]>> AssetLoader;
        public Camera Camera;
        public int Count => _objects.Count;
        public int CulledCount { get; private set; }
        public int ModelsLoaded { get; private set; }
        public int TexturesApplied { get; private set; }
        public IReadOnlyCollection<Guid> Ids => _objects.Keys;
        public bool Has(Guid id) => _objects.ContainsKey(id);
        public GameObject ViewOf(Guid id) => _objects.TryGetValue(id, out var entry) ? entry.Object : null;
        public WorldObjectState StateOf(Guid id) => _objects.TryGetValue(id, out var entry) ? entry.State : null;
        public float RangeOf(Guid id) => _objects.TryGetValue(id, out var entry) ? entry.Range : 0f;
        public bool IsCulled(Guid id) => _objects.TryGetValue(id, out var entry) && entry.Culled;

        /// <summary>The nearest object a ray (the cursor) hits, by the bounds of what is drawn, up to a distance. The top of that box is where a person sits.</summary>
        public bool Pick(Ray ray, float maxDistance, out Guid id, out UnityEngine.Bounds bounds)
        {
            id = Guid.Empty; bounds = default; var best = maxDistance;
            foreach (var pair in _objects)
            {
                var entry = pair.Value; if (entry.Object == null || entry.Culled || !entry.Object.activeInHierarchy) continue;
                var any = false; var box = default(UnityEngine.Bounds);
                foreach (var renderer in entry.Renderers) { if (renderer == null || !renderer.enabled) continue; if (!any) { box = renderer.bounds; any = true; } else box.Encapsulate(renderer.bounds); }
                if (!any || !box.IntersectRay(ray, out var distance) || distance > best) continue;
                best = distance; id = pair.Key; bounds = box;
            }
            return id != Guid.Empty;
        }

        private void Awake() => Prepare();

        private void Prepare()
        {
            if (_opaque != null) return;
            _opaque = Resources.Load<Material>("LifeVerse/ObjectMaterial"); _blend = Resources.Load<Material>("LifeVerse/ObjectMaterialBlend");
            _opaqueDouble = Resources.Load<Material>("LifeVerse/ObjectMaterialDouble"); _blendDouble = Resources.Load<Material>("LifeVerse/ObjectMaterialBlendDouble");
            if (_opaque == null) _opaque = new Material(Shader.Find("HDRP/Lit"));
            if (_blend == null) _blend = _opaque; if (_opaqueDouble == null) _opaqueDouble = _opaque; if (_blendDouble == null) _blendDouble = _blend;
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
                if (operation == "lock" || operation == "unlock" || operation == "live" || operation == "text" || operation == "sound" || operation == "sound_stop" || operation == "effect" || operation == "media") return false;
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

        /// <summary>The solid shapes of the objects, built the way the island server builds them, so walking and jumping are predicted the same here (standing on a built object, bumping into one).</summary>
        public readonly LifeVerse.Shared.Networking.ObstacleField Obstacles = new LifeVerse.Shared.Networking.ObstacleField();

        private void UpdateObstacle(WorldObjectState s)
        {
            Obstacles.Set(s.Id, LifeVerse.Shared.Networking.ObstacleField.Blocks(s.PhysicsJson, s.PrimitiveType)
                ? LifeVerse.Shared.Networking.ObstacleField.For(s.PrimitiveType, new System.Numerics.Vector3(s.PositionX, s.PositionY, s.PositionZ), new System.Numerics.Vector3(s.RotationX, s.RotationY, s.RotationZ), new System.Numerics.Vector3(s.ScaleX, s.ScaleY, s.ScaleZ)) : null);
        }

        public void Remove(Guid id) { Obstacles.Remove(id); if (_objects.TryGetValue(id, out var entry)) { DropMedia(entry); DropResources(entry); Destroy(entry.Object); _objects.Remove(id); } }

        /// <summary>The ids of the faces of an object that has them (a box has 6, a round one fewer); empty for a model or a plant.</summary>
        public int[] FacesOf(Guid id) => _objects.TryGetValue(id, out var entry) && entry.State != null && entry.Object.GetComponent<MeshFilter>()?.sharedMesh is { } mesh && mesh.subMeshCount > 1 ? FaceIdsOf(entry.State) : Array.Empty<int>();

        /// <summary>The face of an object a ray (the cursor) hits first, by the triangles of its mesh. False when it hits none or the object has no faces.</summary>
        public bool PickFace(Guid id, Ray ray, out int faceId)
        {
            faceId = -1; if (!_objects.TryGetValue(id, out var entry) || entry.State == null) return false; var mesh = entry.Object.GetComponent<MeshFilter>()?.sharedMesh; if (mesh == null || mesh.subMeshCount < 2) return false;
            var ids = FaceIdsOf(entry.State); var toLocal = entry.Object.transform.worldToLocalMatrix; var origin = toLocal.MultiplyPoint3x4(ray.origin); var direction = toLocal.MultiplyVector(ray.direction); var vertices = mesh.vertices; var best = float.MaxValue;
            for (var sub = 0; sub < mesh.subMeshCount && sub < ids.Length; sub++)
            {
                var triangles = mesh.GetTriangles(sub);
                for (var i = 0; i + 2 < triangles.Length; i += 3)
                {
                    var a = vertices[triangles[i]]; var e1 = vertices[triangles[i + 1]] - a; var e2 = vertices[triangles[i + 2]] - a; var p = Vector3.Cross(direction, e2); var det = Vector3.Dot(e1, p); if (Mathf.Abs(det) < 1e-9f) continue;
                    var inv = 1f / det; var s = origin - a; var u = Vector3.Dot(s, p) * inv; if (u < 0f || u > 1f) continue; var q = Vector3.Cross(s, e1); var v = Vector3.Dot(direction, q) * inv; if (v < 0f || u + v > 1f) continue; var t = Vector3.Dot(e2, q) * inv;
                    if (t > 0f && t < best) { best = t; faceId = ids[sub]; }
                }
            }
            return faceId >= 0;
        }

        /// <summary>Draws an object again from the state the island last confirmed (a preview was refused).</summary>
        public void Refresh(Guid id) { if (_objects.TryGetValue(id, out var entry) && entry.State != null) Upsert(entry.State); }

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

        // ---- the object ----

        private void Upsert(WorldObjectState state)
        {
            if (!_objects.TryGetValue(state.Id, out var entry))
            {
                var view = new GameObject("Object " + state.Id.ToString("N").Substring(0, 8)); view.transform.SetParent(transform, false);
                view.AddComponent<MeshFilter>(); view.AddComponent<MeshRenderer>();
                entry = new Entry { Object = view }; _objects[state.Id] = entry;
            }
            entry.State = state; UpdateObstacle(state); var go = entry.Object; go.name = state.Name;
            go.transform.position = GameSpace.ToUnity(state.PositionX, state.PositionY, state.PositionZ);
            go.transform.rotation = GameSpace.RotationToUnity(state.RotationX, state.RotationY, state.RotationZ);
            go.transform.localScale = new Vector3(Mathf.Max(state.ScaleX, .001f), Mathf.Max(state.ScaleY, .001f), Mathf.Max(state.ScaleZ, .001f));
            entry.Range = Mathf.Clamp(Mathf.Max(state.ScaleX, Mathf.Max(state.ScaleY, state.ScaleZ)) * RangePerMetre, MinRange, MaxRange);
            var type = state.PrimitiveType.ToLowerInvariant();
            var shapeKey = type + "|" + state.GeometryJson + "|" + ModelAnimations.WithoutAnimation(state.MaterialJson);
            if (entry.ShapeKey == shapeKey) { ApplyAnimation(entry); return; }
            entry.ShapeKey = shapeKey; entry.Generation++;
            Build(entry, state, type);
        }

        private void Build(Entry entry, WorldObjectState state, string type)
        {
            var go = entry.Object; var filter = go.GetComponent<MeshFilter>(); var renderer = go.GetComponent<MeshRenderer>();
            if (entry.Model != null) { Destroy(entry.Model); entry.Model = null; } if (entry.Proxy != null) { Destroy(entry.Proxy); entry.Proxy = null; }
            renderer.enabled = true;
            if (type == "mesh") { BuildModel(entry, state, filter, renderer); }
            else if (type == "tree" || type == "grass") { filter.sharedMesh = NatureMesh(type); renderer.sharedMaterials = NatureMaterials(type, state); }
            else { filter.sharedMesh = MeshFor(state, type); renderer.sharedMaterials = MaterialsFor(state, filter.sharedMesh); }
            entry.Renderers = go.GetComponentsInChildren<Renderer>(true);
            ApplyMedia(entry, state); ApplyResources(entry, state);
        }

        // ---- what plays on the surface: a picture from the internet, or a video, as the object's material says ("media": type, url, playing, loop, glow) ----

        /// <summary>Gets the bytes of a picture by its address (https). Replaced in tests; by default it is a web request with a time limit.</summary>
        public Func<string, Task<byte[]>> MediaLoader;
        /// <summary>Makes the audio source a video plays its sound through (in 3D at the object, under the Mídia volume). Set by the sound system; without it a video is silent.</summary>
        public Func<GameObject, AudioSource> MediaAudio { get; set; }
        public AudioSource VideoAudioOf(Guid id) => _objects.TryGetValue(id, out var entry) && entry.Video != null ? entry.Video.GetTargetAudioSource(0) : null;
        public int MediaImages { get; private set; }
        public int MediaVideos { get; private set; }

        public static bool MediaOf(string materialJson, out string type, out string url, out bool playing, out bool loop, out bool glow)
        {
            type = url = ""; playing = loop = glow = false;
            try
            {
                using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(materialJson) ? "{}" : materialJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Object) return false;
                string Str(string name) => media.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                bool Flag(string name, bool fallback) => media.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.True || (fallback && v.ValueKind != JsonValueKind.False) : fallback;
                type = Str("type"); url = Str("url"); playing = Flag("playing", true); loop = Flag("loop", true); glow = Flag("glow", false);
                return (type == "image" || type == "video") && Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == "file" && MediaFilesAllowed);
            }
            catch (JsonException) { return false; }
        }

        /// <summary>Only tests let a media address be a local file; people's objects are https only (the island checks the same).</summary>
        public static bool MediaFilesAllowed;

        private void DropMedia(Entry entry)
        {
            if (entry.Video != null) { entry.Video.Stop(); Destroy(entry.Video); entry.Video = null; }
            if (entry.VideoTarget != null) { entry.VideoTarget.Release(); Destroy(entry.VideoTarget); entry.VideoTarget = null; }
            if (entry.MediaMaterials != null) { foreach (var m in entry.MediaMaterials) if (m != null) Destroy(m); entry.MediaMaterials = null; }
        }

        private void ApplyMedia(Entry entry, WorldObjectState state)
        {
            DropMedia(entry); if (!MediaOf(state.MaterialJson, out var type, out var url, out var playing, out var loop, out var glow)) return;
            var renderer = entry.Object.GetComponent<MeshRenderer>(); if (renderer == null || !renderer.enabled || renderer.sharedMaterials.Length == 0) return;
            var instances = renderer.materials; entry.MediaMaterials = instances;   // this object's own copies, so a picture on one wall is not on every wall
            if (type == "image") { _ = ShowImage(entry, url, glow, entry.Generation); return; }
            var target = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32); entry.VideoTarget = target;
            foreach (var m in instances) { m.SetTexture("_BaseColorMap", target); m.SetColor("_BaseColor", Color.white); if (glow) { m.SetTexture("_EmissiveColorMap", target); m.SetColor("_EmissiveColor", Color.white * 2f); } }
            var player = entry.Object.AddComponent<UnityEngine.Video.VideoPlayer>(); entry.Video = player; player.playOnAwake = false; player.source = UnityEngine.Video.VideoSource.Url; player.url = url; player.isLooping = loop; player.renderMode = UnityEngine.Video.VideoRenderMode.RenderTexture; player.targetTexture = target;
            var sound = MediaAudio != null ? MediaAudio(entry.Object) : null; if (sound != null) { player.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.AudioSource; player.controlledAudioTrackCount = 1; player.SetTargetAudioSource(0, sound); } else player.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.None;
            player.errorReceived += (p, message) => Debug.LogWarning("[LifeVerse] vídeo " + url + ": " + message);
            player.Prepare(); player.prepareCompleted += p => { if (entry.Video == p && playing) p.Play(); }; MediaVideos++;
        }

        /// <summary>Plays or pauses the video of an object (what the owner's "playing" switch says).</summary>
        public bool VideoPlaying(Guid id) => _objects.TryGetValue(id, out var entry) && entry.Video != null && entry.Video.isPlaying;
        public bool HasVideo(Guid id) => _objects.TryGetValue(id, out var entry) && entry.Video != null;

        private async Task ShowImage(Entry entry, string url, bool glow, int generation)
        {
            try
            {
                var bytes = await (MediaLoader != null ? MediaLoader(url) : DownloadAsync(url)); if (bytes == null || bytes.Length < 12 || entry.Object == null || entry.Generation != generation || entry.MediaMaterials == null) return;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                if (!texture.LoadImage(bytes, false)) { Destroy(texture); return; } if (Mathf.Max(texture.width, texture.height) > MaxTextureSize) texture = Shrink(texture, MaxTextureSize); texture.Apply(true);
                foreach (var m in entry.MediaMaterials) { if (m == null) continue; m.SetTexture("_BaseColorMap", texture); m.SetColor("_BaseColor", Color.white); if (glow) { m.SetTexture("_EmissiveColorMap", texture); m.SetColor("_EmissiveColor", Color.white * 2f); } }
                MediaImages++;
            }
            catch (Exception exception) { Debug.LogWarning("[LifeVerse] mídia " + url + ": " + exception.Message); }
        }

        private static Task<byte[]> DownloadAsync(string url)
        {
            var source = new TaskCompletionSource<byte[]>(); var request = UnityEngine.Networking.UnityWebRequest.Get(url); request.timeout = 20; request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SendWebRequest().completed += _ => { source.TrySetResult(request.result == UnityEngine.Networking.UnityWebRequest.Result.Success && request.downloadedBytes <= 16 * 1024 * 1024 ? request.downloadHandler.data : null); request.Dispose(); };
            return source.Task;
        }

        // ---- shapes ----

        /// <summary>Builds a construction of the inventory (or a picture made into a plate) under a parent, outside the world: what a person wears on the body. The shapes and materials are the world's own;
        /// nothing is registered as an object of the island. The number of shapes made.</summary>
        public int BuildPackage(LifeVerse.Shared.Building.BuildPackage package, Transform parent)
        {
            if (package?.Objects == null || parent == null) return 0; var count = 0;
            foreach (var o in System.Linq.Enumerable.Take(package.Objects, 64))
            {
                var state = new WorldObjectState { Id = Guid.NewGuid(), PrimitiveType = string.IsNullOrEmpty(o.PrimitiveType) || o.PrimitiveType == "mesh" ? "cube" : o.PrimitiveType, Name = o.Name ?? "", MaterialJson = o.MaterialJson ?? "{}", GeometryJson = o.GeometryJson ?? "{}", ScaleX = o.ScaleX, ScaleY = o.ScaleY, ScaleZ = o.ScaleZ };
                var go = new GameObject(string.IsNullOrEmpty(o.Name) ? "Parte" : o.Name); go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(o.X, o.Y + package.BaseOffset, -o.Z); go.transform.localRotation = GameSpace.RotationToUnity(o.RotationX, o.RotationY, o.RotationZ); go.transform.localScale = new Vector3(Mathf.Max(.001f, o.ScaleX), Mathf.Max(.001f, o.ScaleY), Mathf.Max(.001f, o.ScaleZ));
                var filter = go.AddComponent<MeshFilter>(); filter.sharedMesh = MeshFor(state, state.PrimitiveType); go.AddComponent<MeshRenderer>().sharedMaterials = MaterialsFor(state, filter.sharedMesh); count++;
            }
            return count;
        }

        private static Mesh MeshFor(WorldObjectState state, string type)
        {
            var key = type + "|" + state.GeometryJson;
            if (MeshCache.TryGetValue(key, out var cached) && cached != null) return cached;
            Mesh mesh;
            if (PrimMesh.Supports(type)) mesh = FromPrim(type, PrimParams.Parse(state.GeometryJson));
            else if (type == "plane") mesh = Quad();
            else mesh = FromPrim("cube", new PrimParams());
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

        // ---- trees and grass: generated (nothing to import), inside a one-metre box like every primitive ----

        private static void AddCone(List<Vector3> vertices, List<int> triangles, float topRadius, float bottomRadius, float height, int segments, float centreY)
        {
            var start = vertices.Count; var bottom = centreY - height / 2f; var top = centreY + height / 2f;
            for (var i = 0; i < segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(a) * bottomRadius, bottom, Mathf.Sin(a) * bottomRadius)); vertices.Add(new Vector3(Mathf.Cos(a) * topRadius, top, Mathf.Sin(a) * topRadius));
            }
            for (var i = 0; i < segments; i++)
            {
                var b0 = start + i * 2; var t0 = b0 + 1; var b1 = start + (i + 1) % segments * 2; var t1 = b1 + 1;
                triangles.AddRange(new[] { b0, t0, b1, b1, t0, t1 }); // outward faces
            }
            var capBottom = vertices.Count; vertices.Add(new Vector3(0, bottom, 0));
            for (var i = 0; i < segments; i++) triangles.AddRange(new[] { capBottom, start + (i + 1) % segments * 2, start + i * 2 });
            if (topRadius > 0f) { var capTop = vertices.Count; vertices.Add(new Vector3(0, top, 0)); for (var i = 0; i < segments; i++) triangles.AddRange(new[] { capTop, start + i * 2 + 1, start + (i + 1) % segments * 2 + 1 }); }
        }

        private static Mesh NatureMesh(string type)
        {
            var key = "nature|" + type; if (MeshCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var mesh = new Mesh { name = type, indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 }; var vertices = new List<Vector3>();
            if (type == "tree")
            {
                var trunk = new List<int>(); var low = new List<int>(); var high = new List<int>();
                AddCone(vertices, trunk, .05f, .08f, .6f, 8, -.2f); AddCone(vertices, low, 0f, .38f, .5f, 10, .05f); AddCone(vertices, high, 0f, .27f, .45f, 10, .275f);
                mesh.SetVertices(vertices); mesh.subMeshCount = 3; mesh.SetTriangles(trunk, 0); mesh.SetTriangles(low, 1); mesh.SetTriangles(high, 2);
            }
            else
            {
                var blades = new List<int>(); uint seed = 12345; float Next() { seed = seed * 1664525u + 1013904223u; return (seed >> 8) / (float)(1 << 24); }
                for (var i = 0; i < 18; i++)
                {
                    var angle = Next() * Mathf.PI * 2f; var radius = .38f * Mathf.Sqrt(Next()); var centre = new Vector3(Mathf.Cos(angle) * radius, -.5f, Mathf.Sin(angle) * radius);
                    var yaw = Next() * Mathf.PI; var side = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw)) * .045f; var height = .55f + Next() * .4f; var lean = new Vector3(Next() - .5f, 0, Next() - .5f) * .18f;
                    var tip = centre + new Vector3(0, height, 0) + lean; var at = vertices.Count;
                    vertices.Add(centre - side); vertices.Add(centre + side); vertices.Add(tip);
                    blades.AddRange(new[] { at, at + 2, at + 1, at, at + 1, at + 2 }); // both faces, so a blade is seen from either side
                }
                mesh.SetVertices(vertices); mesh.subMeshCount = 1; mesh.SetTriangles(blades, 0);
            }
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); MeshCache[key] = mesh; return mesh;
        }

        private Material[] NatureMaterials(string type, WorldObjectState state)
        {
            JsonDocument document = null; Color chosen = Color.white;
            try { document = JsonDocument.Parse(string.IsNullOrWhiteSpace(state.MaterialJson) ? "{}" : state.MaterialJson); if (document.RootElement.ValueKind == JsonValueKind.Object) chosen = ColorOf(Text(document.RootElement, null, "color"), Color.white); }
            catch (JsonException) { }
            finally { document?.Dispose(); }
            // the default plywood colour of a new object means "no colour chosen": plants keep their own greens until the person picks one
            var leaves = chosen == Color.white ? (type == "tree" ? ColorOf("#3F8F3A", Color.green) : ColorOf("#6DBE45", Color.green)) : chosen;
            var leafMaterial = Plain("leaf|" + leaves, leaves, .9f, true);
            return type == "tree" ? new[] { Plain("trunk", ColorOf("#7A5230", Color.gray), .95f, false), leafMaterial, leafMaterial } : new[] { leafMaterial };
        }

        private Material Plain(string key, Color color, float roughness, bool doubleSided)
        {
            if (MaterialCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var material = new Material(doubleSided ? _opaqueDouble : _opaque); material.SetColor("_BaseColor", color); SetSurface(material, roughness, 0f);
            MaterialCache[key] = material; return material;
        }

        private static void SetSurface(Material material, float roughness, float metallic)
        {
            // the prototype carries a mask map of all ones, so the numbers are the remap range: metal 0..metallic, smoothness fixed at 1 - roughness
            material.SetFloat("_MetallicRemapMin", 0f); material.SetFloat("_MetallicRemapMax", metallic);
            material.SetFloat("_SmoothnessRemapMin", 1f - roughness); material.SetFloat("_SmoothnessRemapMax", 1f - roughness);
            material.SetFloat("_Smoothness", 1f - roughness); material.SetFloat("_Metallic", metallic);
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
            var doubleSided = Flag(layer, over, "doubleSided");
            var material = new Material(blend ? (doubleSided ? _blendDouble : _blend) : (doubleSided ? _opaqueDouble : _opaque));
            material.SetColor("_BaseColor", new Color(color.r, color.g, color.b, blend ? opacity : 1f)); SetSurface(material, roughness, metallic);
            var glow = Text(layer, over, "emission");
            if (glow != null)
            {
                var strength = Mathf.Clamp(Number(layer, over, "emissionStrength", 1f), 0f, 16f); var e = ColorOf(glow, Color.black);
                material.SetColor("_EmissiveColor", new Color(e.r, e.g, e.b) * strength * 25f); // nits: a strength of 1 is a lamp you can see in daylight
            }
            if (Flag(layer, over, "fullBright")) material.SetColor("_EmissiveColor", color * 40f);
            if (!Flag(layer, over, "noTexture") && AssetLoader != null) ApplyPictures(material, layer, over, color, glow != null);
            // a pattern of the system ("texturas do sistema") where no picture of the inventory is chosen
            if (!Flag(layer, over, "noTexture") && AssetOf(layer, over, "texture") == null && BuiltInTextures.Get(Text(layer, over, "builtin")) is { } pattern)
            { material.SetTexture("_BaseColorMap", pattern); material.SetTextureScale("_BaseColorMap", new Vector2(Number(layer, over, "uvRepeatX", 1f), Number(layer, over, "uvRepeatY", 1f))); material.SetTextureOffset("_BaseColorMap", new Vector2(Number(layer, over, "uvOffsetX", 0f), Number(layer, over, "uvOffsetY", 0f))); }
            MaterialCache[cacheKey] = material; return material;
        }

        // ---- pictures on materials ----

        private static Guid? AssetOf(JsonElement layer, JsonElement? over, string key) => Text(layer, over, key) is { } text && Guid.TryParse(text, out var id) ? id : (Guid?)null;

        private void ApplyPictures(Material material, JsonElement layer, JsonElement? over, Color color, bool glows)
        {
            var scale = new Vector2(Number(layer, over, "uvRepeatX", 1f), Number(layer, over, "uvRepeatY", 1f)); var offset = new Vector2(Number(layer, over, "uvOffsetX", 0f), Number(layer, over, "uvOffsetY", 0f));
            var strength = Mathf.Clamp(Number(layer, over, "normalStrength", 1f), 0f, 2f);
            if (AssetOf(layer, over, "texture") is { } baseMap) Fetch(baseMap, true, texture => { material.SetTexture("_BaseColorMap", texture); material.SetTextureScale("_BaseColorMap", scale); material.SetTextureOffset("_BaseColorMap", offset); });
            if (AssetOf(layer, over, "normalMap") is { } normalMap) Fetch(normalMap, false, texture => { material.SetTexture("_NormalMap", texture); material.SetFloat("_NormalScale", strength); });
            if (AssetOf(layer, over, "ormMap") is { } ormMap)
                Fetch(ormMap, false, texture => { var mask = OrmToMask(texture); if (mask != null) { material.SetTexture("_MaskMap", mask); material.SetFloat("_MetallicRemapMin", 0f); material.SetFloat("_MetallicRemapMax", 1f); material.SetFloat("_SmoothnessRemapMin", 0f); material.SetFloat("_SmoothnessRemapMax", 1f); material.SetFloat("_AORemapMin", 1f - Mathf.Clamp01(Number(layer, over, "aoStrength", 1f))); } });
            if (AssetOf(layer, over, "emissiveMap") is { } emissiveMap) Fetch(emissiveMap, true, texture =>
            {
                material.SetTexture("_EmissiveColorMap", texture);
                if (!glows) material.SetColor("_EmissiveColor", new Color(1f, 1f, 1f) * Mathf.Clamp(Number(layer, over, "emissionStrength", 1f), 0f, 16f) * 25f);
            });
        }

        private void Fetch(Guid asset, bool srgb, Action<Texture2D> apply) => _ = FetchAsync(asset, srgb, apply);

        private async Task FetchAsync(Guid asset, bool srgb, Action<Texture2D> apply)
        {
            try
            {
                var key = asset; if (!TextureCache.TryGetValue(key, out var task)) { task = LoadTextureAsync(asset); TextureCache[key] = task; }
                var texture = await task; if (texture == null) return;
                // the same bytes serve base (sRGB) and the data maps (linear): a data map must not be gamma-corrected
                apply(srgb ? texture : Linear(texture)); TexturesApplied++;
            }
            catch (Exception exception) { Debug.LogWarning("[LifeVerse] textura " + asset + ": " + exception.Message); }
        }

        private async Task<Texture2D> LoadTextureAsync(Guid asset)
        {
            var bytes = await AssetLoader(asset); if (bytes == null || bytes.Length < 12) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            if (!texture.LoadImage(bytes, false)) { Destroy(texture); return null; } // PNG and JPEG; WebP is not read by Unity
            if (Mathf.Max(texture.width, texture.height) > MaxTextureSize) texture = Shrink(texture, MaxTextureSize);
            texture.Apply(true); return texture;
        }

        private static Texture2D Shrink(Texture2D source, int limit)
        {
            var scale = limit / (float)Mathf.Max(source.width, source.height); var width = Mathf.Max(1, (int)(source.width * scale)); var height = Mathf.Max(1, (int)(source.height * scale));
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); Graphics.Blit(source, target);
            var previous = RenderTexture.active; RenderTexture.active = target;
            var result = new Texture2D(width, height, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0); result.Apply(true); RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Destroy(source); return result;
        }

        private static readonly Dictionary<Texture2D, Texture2D> LinearCopies = new Dictionary<Texture2D, Texture2D>();
        private static Texture2D Linear(Texture2D srgb)
        {
            if (LinearCopies.TryGetValue(srgb, out var copy) && copy != null) return copy;
            copy = new Texture2D(srgb.width, srgb.height, TextureFormat.RGBA32, true, true) { wrapMode = srgb.wrapMode, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            copy.SetPixels32(srgb.GetPixels32(0)); copy.Apply(true); LinearCopies[srgb] = copy; return copy;
        }

        /// <summary>glTF packs occlusion (red), roughness (green) and metal (blue); HDRP's mask map wants metal (red), occlusion (green) and smoothness (alpha).</summary>
        private static Texture2D OrmToMask(Texture2D orm)
        {
            if (orm == null) return null;
            var pixels = orm.GetPixels32(0); for (var i = 0; i < pixels.Length; i++) { var p = pixels[i]; pixels[i] = new Color32(p.b, p.r, 0, (byte)(255 - p.g)); }
            var mask = new Texture2D(orm.width, orm.height, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            mask.SetPixels32(pixels); mask.Apply(true); return mask;
        }

        // ---- imported models (glTF / GLB), normalised so the longest side is one metre and centred on the origin; the object's scale then sets the real size ----

        private void BuildModel(Entry entry, WorldObjectState state, MeshFilter filter, MeshRenderer renderer)
        {
            // until the model arrives (or when it cannot be had) a translucent box stands in for it
            filter.sharedMesh = MeshFor(state, "cube");
            var proxy = new Material(_blend); proxy.SetColor("_BaseColor", new Color(.6f, .7f, .8f, .35f)); SetSurface(proxy, .6f, 0f); renderer.sharedMaterials = new[] { proxy };
            entry.Proxy = null;
            if (AssetLoader == null || ModelAssetOf(state.GeometryJson) is not { } asset) return;
            _ = LoadModelAsync(entry, asset, entry.Generation, filter, renderer);
        }

        /// <summary>Plays what the object's animation setting says on its model (nothing while the model has not arrived).</summary>
        private void ApplyAnimation(Entry entry)
        {
            if (entry.Model == null || entry.State == null) return;
            entry.ClipPlaying = ModelAnimations.Apply(entry.Model, ModelAnimations.Settings(entry.State.MaterialJson), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
        }

        /// <summary>The clips of an object's model, and the one that plays now (for the editor and the tests).</summary>
        public IReadOnlyList<string> ClipsOf(Guid id) => _objects.TryGetValue(id, out var entry) && entry.Model != null ? ModelAnimations.Clips(entry.Model) : Array.Empty<string>();
        public string ClipPlayingOf(Guid id) => _objects.TryGetValue(id, out var entry) ? entry.ClipPlaying ?? "" : "";
        public float ClipTimeOf(Guid id) { if (!_objects.TryGetValue(id, out var entry) || entry.Model == null || string.IsNullOrEmpty(entry.ClipPlaying)) return 0f; var player = ModelAnimations.PlayerOf(entry.Model); return player != null ? player[entry.ClipPlaying].time : 0f; }

        private static Guid? ModelAssetOf(string geometryJson)
        {
            try { using var document = JsonDocument.Parse(geometryJson); return document.RootElement.TryGetProperty("assetId", out var value) && Guid.TryParse(value.GetString(), out var id) ? id : (Guid?)null; }
            catch (JsonException) { return null; }
        }

        private async Task LoadModelAsync(Entry entry, Guid asset, int generation, MeshFilter filter, MeshRenderer renderer)
        {
            try
            {
                var bytes = await AssetLoader(asset);
                if (entry.Object == null || entry.Generation != generation) return;
                if (bytes == null) { if (renderer != null) renderer.sharedMaterials[0].SetColor("_BaseColor", new Color(.9f, .3f, .3f, .4f)); return; }
                var gltf = new GLTFast.GltfImport();
                var loaded = await gltf.LoadGltfBinary(bytes, new Uri("file:///model.glb"));
                if (!loaded || entry.Object == null || entry.Generation != generation) { if (!loaded && renderer != null) renderer.sharedMaterials[0].SetColor("_BaseColor", new Color(.9f, .3f, .3f, .4f)); return; }
                // a glTF is right-handed and glTFast mirrors x to get to Unity; the game space flips z instead: turning the import half a turn about the vertical gives exactly that
                // measured away from the object (identity, far from anything) so its scale and rotation do not enter the bounds; parented afterwards
                var holder = new GameObject("Model"); holder.transform.position = new Vector3(0f, -10000f, 0f);
                var content = new GameObject("Content"); content.transform.SetParent(holder.transform, false); content.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var ok = await gltf.InstantiateMainSceneAsync(content.transform);
                if (!ok || entry.Generation != generation) { Destroy(holder); return; }
                var renderers = content.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) { Destroy(holder); return; }
                var bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                var longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)); var factor = longest > 1e-5f ? 1f / longest : 1f;
                var centre = holder.transform.InverseTransformPoint(bounds.center);
                holder.transform.SetParent(entry.Object.transform, false); holder.transform.localRotation = Quaternion.identity;
                holder.transform.localScale = Vector3.one * factor; holder.transform.localPosition = -centre * factor;
                renderer.enabled = false; entry.Model = holder; ModelsLoaded++; ApplyAnimation(entry);
                entry.Renderers = entry.Object.GetComponentsInChildren<Renderer>(true); entry.Lods = null; entry.LowShown = false;
            }
            catch (Exception exception) { Debug.LogWarning("[LifeVerse] modelo " + asset + ": " + exception.Message); }
        }

        // ---- range: objects too far to matter are not drawn ----

        private void Update()
        {
            _cullClock += Time.deltaTime; if (_cullClock < .25f) return; _cullClock = 0f;
            Cull(Camera != null ? Camera : UnityEngine.Camera.main);
        }

        /// <summary>From this share of its range on, a model is drawn with a lighter copy of its meshes.</summary>
        public const float LodFraction = .3f;
        public int LodObjects { get; private set; }
        public bool IsLow(Guid id) => _objects.TryGetValue(id, out var entry) && entry.LowShown;

        /// <summary>Swaps the meshes of an imported model for their lighter copies (made the first time they are needed) when it is far, and back when it is near.</summary>
        private void SetLod(Entry entry, bool far)
        {
            if (entry.Model == null || far == entry.LowShown) return;
            if (far && entry.Lods == null)
            {
                entry.Lods = new List<(MeshFilter, Mesh, Mesh)>(); foreach (var filter in entry.Model.GetComponentsInChildren<MeshFilter>(true)) { var low = MeshSimplifier.Simplify(filter.sharedMesh, .25f); if (low != null) entry.Lods.Add((filter, filter.sharedMesh, low)); }
                if (entry.Lods.Count > 0) LodObjects++;
            }
            if (entry.Lods == null) return;
            foreach (var (filter, full, low) in entry.Lods) if (filter != null) filter.sharedMesh = far ? low : full;
            entry.LowShown = far;
        }

        /// <summary>Hides what is beyond its range of the camera (a 1 m object vanishes past 120 m, a 10 m one past 600 m) and shows it again inside 90 % of the range, so it does not flicker at the edge.</summary>
        public void Cull(Camera camera)
        {
            if (camera == null) return; var origin = camera.transform.position; var culled = 0;
            foreach (var entry in _objects.Values)
            {
                var distance = Vector3.Distance(origin, entry.Object.transform.position);
                SetLod(entry, entry.LowShown ? distance > entry.Range * LodFraction * .9f : distance > entry.Range * LodFraction);
                var hide = entry.Culled ? distance > entry.Range * .9f : distance > entry.Range;
                if (hide != entry.Culled) { entry.Culled = hide; foreach (var r in entry.Renderers) if (r != null) r.enabled = !hide && !(entry.Model != null && r == entry.Object.GetComponent<MeshRenderer>()); }
                if (entry.Culled) culled++;
            }
            CulledCount = culled;
        }

        private void OnDestroy() { Clear(); }
    }
}
