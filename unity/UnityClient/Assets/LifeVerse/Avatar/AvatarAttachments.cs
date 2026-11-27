using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LifeVerse.Shared.Avatar;
using UnityEngine;

namespace LifeVerse.Unity.Avatar
{
    /// <summary>One object worn on the avatar, as the platform describes it. Rotation is in degrees (X, Y, Z); position and scale are relative to the attachment point.</summary>
    public sealed class AttachmentData
    {
        public Guid Id, InventoryItemId, AssetId; public string Name = "", Point = "", PointName = ""; public float[] Position = { 0, 0, 0 }, Rotation = { 0, 0, 0 }, Scale = { 1, 1, 1 }; public bool Equipped, CanModify = true; public int Order; public long Revision; public string OriginalPointName; public float Sway;

        public bool IsHud => AttachmentPoints.Find(Point)?.IsHud == true;

        public string Describe() => (string.IsNullOrWhiteSpace(Name) ? "Objeto" : Name) + " · " + PointName + (OriginalPointName != null ? " (o corpo atual não tem " + OriginalPointName + ")" : "") + " · posição " + F(Position[0]) + ", " + F(Position[1]) + ", " + F(Position[2]) + " m · rotação " + F(Rotation[0]) + "°, " + F(Rotation[1]) + "°, " + F(Rotation[2]) + "° · escala " + F(Scale[0]) + ", " + F(Scale[1]) + ", " + F(Scale[2]) + (CanModify ? "" : " · sem direito de modificar");
        private static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

        public static AttachmentData Parse(JsonElement e)
        {
            try
            {
                float[] Three(string key, float fallback) => e.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() == 3 ? a.EnumerateArray().Select(v => v.GetSingle()).ToArray() : new[] { fallback, fallback, fallback };
                string Str(string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                return new AttachmentData
                {
                    Id = e.GetProperty("id").GetGuid(), InventoryItemId = e.TryGetProperty("inventoryItemId", out var inv) && inv.TryGetGuid(out var invId) ? invId : Guid.Empty, AssetId = e.GetProperty("assetId").GetGuid(), Name = Str("name"), Point = Str("point"), PointName = Str("pointName"),
                    Position = Three("position", 0), Rotation = Three("rotation", 0), Scale = Three("scale", 1), Equipped = e.TryGetProperty("equipped", out var q) && q.ValueKind == JsonValueKind.True, Order = e.TryGetProperty("order", out var o) && o.TryGetInt32(out var order) ? order : 0,
                    Revision = e.TryGetProperty("revision", out var r) && r.TryGetInt64(out var rev) ? rev : 0, CanModify = !e.TryGetProperty("canModify", out var m) || m.ValueKind != JsonValueKind.False, OriginalPointName = e.TryGetProperty("originalPointName", out var op) && op.ValueKind == JsonValueKind.String ? op.GetString() : null, Sway = e.TryGetProperty("sway", out var sw) && sw.TryGetSingle(out var swayValue) ? swayValue : 0,
                };
            }
            catch (Exception ex) when (ex is KeyNotFoundException || ex is InvalidOperationException || ex is FormatException) { return null; }
        }

        /// <summary>A bare list (what the island sends inside an appearance).</summary>
        public static List<AttachmentData> ParseArray(string json)
        {
            try { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json); return d.RootElement.ValueKind == JsonValueKind.Array ? d.RootElement.EnumerateArray().Select(Parse).Where(x => x != null).ToList() : new List<AttachmentData>(); }
            catch (JsonException) { return new List<AttachmentData>(); }
        }

        /// <summary>The answer of the attachments route: { attachments: [...], profiles: [...] }.</summary>
        public static List<AttachmentData> ParseList(string json, out List<string> profiles)
        {
            profiles = new List<string>();
            try
            {
                using var d = JsonDocument.Parse(json); if (d.RootElement.ValueKind != JsonValueKind.Object) return new List<AttachmentData>();
                if (d.RootElement.TryGetProperty("profiles", out var p) && p.ValueKind == JsonValueKind.Array) profiles = p.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                return d.RootElement.TryGetProperty("attachments", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(Parse).Where(x => x != null).ToList() : new List<AttachmentData>();
            }
            catch (JsonException) { return new List<AttachmentData>(); }
        }
    }

    /// <summary>
    /// What an avatar wears on its body: objects of the inventory fixed to a point of the skeleton (a hat on the head, a bag on the back, wings on the shoulders). Each worn object is a model that follows the bone of its point, placed by the offset of the point and the
    /// position, rotation and scale the person set. A HUD point is anchored to the screen and is not drawn on the body. Models are loaded once and shared by everyone who wears the same one.
    /// </summary>
    public sealed class AvatarAttachments : MonoBehaviour
    {
        private sealed class Worn { public AttachmentData Data; public GameObject Holder, Content; public bool Loading; public long Revision; public Vector3 LastPosition; public Vector3 Tilt; public bool Started; }
        private AvatarView _view; private readonly Dictionary<Guid, Worn> _worn = new Dictionary<Guid, Worn>(); private int _generation;
        /// <summary>Gets the bytes of a model by its identifier. Set by the game (the platform's asset route).</summary>
        public static Func<Guid, Task<byte[]>> Loader { get; set; }
        /// <summary>What a worn asset is: a 3D model, a picture (worn as a plate) or a construction of the inventory (worn as its shapes).</summary>
        public enum ContentKind { Model, Image, Construction }
        private sealed class Content { public ContentKind Kind; public GLTFast.GltfImport Import; public LifeVerse.Shared.Building.BuildPackage Package; }
        private static readonly Dictionary<Guid, Task<Content>> Imports = new Dictionary<Guid, Task<Content>>();
        /// <summary>Builds the shapes of a construction under a holder (the world's builder); without it pictures and constructions are not drawn.</summary>
        public static Func<LifeVerse.Shared.Building.BuildPackage, Transform, int> Constructions { get; set; }

        /// <summary>The kind of a file by its first bytes: PNG, JPEG or WebP is a picture, a JSON of the build format is a construction, anything else a model.</summary>
        public static ContentKind KindOf(byte[] b)
        {
            if (b == null || b.Length < 4) return ContentKind.Model;
            if ((b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) || (b[0] == 0xFF && b[1] == 0xD8) || LifeVerse.Unity.World.Webp.WebpDecoder.IsWebp(b)) return ContentKind.Image;
            var start = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0; while (start < b.Length && char.IsWhiteSpace((char)b[start])) start++;
            return start < b.Length && b[start] == (byte)'{' && System.Text.Encoding.UTF8.GetString(b, 0, Math.Min(b.Length, 512)).Contains(LifeVerse.Shared.Building.BuildPackages.FormatName) ? ContentKind.Construction : ContentKind.Model;
        }

        /// <summary>A picture worn on the body: a thin plate one metre on its longest side, showing the picture on its faces.</summary>
        public static LifeVerse.Shared.Building.BuildPackage Plate(Guid asset, int width, int height)
        {
            var longest = Mathf.Max(1, Mathf.Max(width, height)); var w = width / (float)longest; var h = height / (float)longest;
            var material = JsonSerializer.Serialize(new { texture = asset.ToString(), color = "#FFFFFF" });
            return new LifeVerse.Shared.Building.BuildPackage(LifeVerse.Shared.Building.BuildPackages.FormatName, 1, "Imagem", 0f, new[] { new LifeVerse.Shared.Building.PackageObject("cube", "Imagem", "", 0f, 0f, 0f, 0f, 0f, 0f, w, h, .01f, material, "{}", "{}", "[]", null) });
        }
        public int Count => _worn.Count(p => p.Value.Holder != null);
        public int Loaded { get; private set; }
        public IReadOnlyCollection<Guid> Ids => _worn.Keys;
        public Transform HolderOf(Guid id) => _worn.TryGetValue(id, out var w) && w.Holder != null ? w.Holder.transform : null;

        private void Awake() { _view = GetComponent<AvatarView>(); }

        /// <summary>The transform a point follows: the bone of a custom body, or the nearest part of the simple one.</summary>
        public Transform AnchorOf(string boneKey)
        {
            if (_view == null || string.IsNullOrEmpty(boneKey)) return null;
            if (_view.Skeleton != null && _view.Skeleton.BoneOf(boneKey) is { } bone) return bone;
            var key = boneKey.ToLowerInvariant();
            if (key.Contains("head") || key.Contains("neck") || key.Contains("jaw") || key.Contains("eye")) return _view.Head;
            if (key.EndsWith("_l") || key.StartsWith("left")) { if (key.Contains("leg") || key.Contains("foot") || key.Contains("thigh") || key.Contains("shin") || key.Contains("toe")) return _view.LeftLegPart; return _view.LeftArmPart; }
            if (key.EndsWith("_r") || key.StartsWith("right")) { if (key.Contains("leg") || key.Contains("foot") || key.Contains("thigh") || key.Contains("shin") || key.Contains("toe")) return _view.RightLegPart; return _view.RightArm; }
            return _view.Torso;
        }

        /// <summary>Puts on what is in the list (equipped, not HUD) and takes off the rest. A change of revision places the object again.</summary>
        public void Apply(IReadOnlyList<AttachmentData> list)
        {
            var wanted = (list ?? new List<AttachmentData>()).Where(a => a.Equipped && !a.IsHud && AttachmentPoints.Find(a.Point) != null).ToList(); var ids = new HashSet<Guid>(wanted.Select(a => a.Id));
            foreach (var id in _worn.Keys.Where(k => !ids.Contains(k)).ToList()) { if (_worn[id].Holder != null) Destroy(_worn[id].Holder); _worn.Remove(id); }
            foreach (var data in wanted)
            {
                if (_worn.TryGetValue(data.Id, out var current) && current.Data.AssetId == data.AssetId) { current.Data = data; if (current.Holder != null) Place(current); continue; }
                if (_worn.TryGetValue(data.Id, out var old) && old.Holder != null) Destroy(old.Holder);
                var worn = new Worn { Data = data, Loading = true }; _worn[data.Id] = worn; _ = Load(worn, ++_generation);
            }
        }

        private async Task Load(Worn worn, int generation)
        {
            try
            {
                if (Loader == null) { worn.Loading = false; return; }
                if (!Imports.TryGetValue(worn.Data.AssetId, out var task)) { task = ImportAsync(worn.Data.AssetId); Imports[worn.Data.AssetId] = task; }
                var import = await task; if (import == null) { Imports.Remove(worn.Data.AssetId); worn.Loading = false; return; }
                Kinds[worn.Data.Id] = import.Kind;
                if (this == null || !_worn.TryGetValue(worn.Data.Id, out var still) || still != worn) return;
                var holder = new GameObject("Worn " + worn.Data.Name); var content = new GameObject("Content"); content.transform.SetParent(holder.transform, false); content.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // the same half turn as the world's models
                if (import.Kind == ContentKind.Model) { if (!await import.Import.InstantiateMainSceneAsync(content.transform)) { Destroy(holder); worn.Loading = false; return; } }
                else { content.transform.localRotation = Quaternion.identity; if (Constructions == null || Constructions(import.Package, content.transform) == 0) { Destroy(holder); worn.Loading = false; return; } }
                foreach (var collider in holder.GetComponentsInChildren<Collider>()) Destroy(collider);
                worn.Holder = holder; worn.Content = content; worn.Loading = false; Loaded++; Place(worn); LifeVerse.Unity.World.ModelAnimations.StartFirst(content);
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException)) { worn.Loading = false; Debug.LogWarning("[LifeVerse] anexo " + worn.Data.Name + ": " + exception.Message); }
        }

        private static async Task<Content> ImportAsync(Guid asset)
        {
            var bytes = await Loader(asset); if (bytes == null) return null;
            switch (KindOf(bytes))
            {
                case ContentKind.Image:
                {
                    var picture = new Texture2D(2, 2); try { if (!LifeVerse.Unity.World.Pictures.Load(picture, bytes)) return null; return new Content { Kind = ContentKind.Image, Package = Plate(asset, picture.width, picture.height) }; } finally { Destroy(picture); }
                }
                case ContentKind.Construction:
                {
                    var package = LifeVerse.Shared.Building.BuildPackages.Parse(System.Text.Encoding.UTF8.GetString(bytes).TrimStart((char)0xFEFF), out _); return package == null || package.Objects.Length == 0 ? null : new Content { Kind = ContentKind.Construction, Package = package };
                }
                default:
                {
                    var import = new GLTFast.GltfImport(); return await import.LoadGltfBinary(bytes, new Uri("file:///attachment.glb")) ? new Content { Kind = ContentKind.Model, Import = import } : null;
                }
            }
        }

        private readonly Dictionary<Guid, ContentKind> Kinds = new Dictionary<Guid, ContentKind>();
        /// <summary>What a worn object turned out to be (for the checks).</summary>
        public ContentKind? KindOfWorn(Guid id) => Kinds.TryGetValue(id, out var k) ? k : (ContentKind?)null;

        /// <summary>Fixes the object to its point: the offset of the point plus the person's position, turned and scaled as they set it (game space to Unity's: z is mirrored).</summary>
        private void Place(Worn worn)
        {
            var point = AttachmentPoints.Find(worn.Data.Point); var anchor = AnchorOf(point?.Bone); if (anchor == null || worn.Holder == null) return;
            var offset = point.Offset != null && point.Offset.Length == 3 ? new Vector3(point.Offset[0], point.Offset[1], point.Offset[2]) : Vector3.zero; var position = new Vector3(worn.Data.Position[0], worn.Data.Position[1], worn.Data.Position[2]);
            var t = worn.Holder.transform; t.SetParent(anchor, false); var sum = offset + position; t.localPosition = new Vector3(sum.x, sum.y, -sum.z);
            var rotation = Quaternion.Euler(worn.Data.Rotation[0], worn.Data.Rotation[1], worn.Data.Rotation[2]); t.localRotation = new Quaternion(-rotation.x, -rotation.y, rotation.z, rotation.w); t.localScale = new Vector3(Mathf.Max(.001f, worn.Data.Scale[0]), Mathf.Max(.001f, worn.Data.Scale[1]), Mathf.Max(.001f, worn.Data.Scale[2]));
            worn.Revision = worn.Data.Revision;
        }

        /// <summary>Shows an object at another placement at once (the editor on the body); the saved one comes back with the next list from the platform or another preview.</summary>
        public bool Preview(Guid id, float[] position, float[] rotation, float[] scale)
        {
            if (!_worn.TryGetValue(id, out var worn) || worn.Holder == null) return false;
            worn.Data.Position = (float[])position.Clone(); worn.Data.Rotation = (float[])rotation.Clone(); worn.Data.Scale = (float[])scale.Clone(); Place(worn); return true;
        }

        /// <summary>Sway edited on screen (0 to 1) while the person is still choosing; null puts back what is saved.</summary>
        public void SetSwayOverride(Guid id, float? sway) { if (sway is { } s) _swayOverride[id] = s; else _swayOverride.Remove(id); }
        private readonly Dictionary<Guid, float> _swayOverride = new Dictionary<Guid, float>();
        /// <summary>How far an object leans at the moment because of sway, in degrees (for the tests).</summary>
        public float TiltOf(Guid id) => _worn.TryGetValue(id, out var w) ? w.Tilt.magnitude * Mathf.Rad2Deg : 0f;

        /// <summary>The lean of an object that follows a moving body, given the velocity of its holder in the holder's own space: backwards when it moves ahead, sideways when it goes sideways.</summary>
        public static Vector3 SwayTarget(Vector3 localVelocity, float amount)
        {
            if (amount <= .001f) return Vector3.zero; var v = localVelocity; if (v.magnitude > 12f) v = v.normalized * 12f;
            return new Vector3(Mathf.Clamp(-v.z * .12f * amount, -.5f, .5f), 0f, Mathf.Clamp(v.x * .12f * amount - v.y * .05f * amount, -.5f, .5f));   // the top trails behind: forward turns it back, a step to the right turns it to the left
        }

        private void LateUpdate()
        {
            var dt = Time.deltaTime; if (dt <= 0f) return;
            foreach (var worn in _worn.Values)
            {
                if (worn.Holder == null || worn.Content == null) continue;
                var amount = _swayOverride.TryGetValue(worn.Data.Id, out var edited) ? edited : worn.Data.Sway; var position = worn.Holder.transform.position; var velocity = (position - worn.LastPosition) / dt; worn.LastPosition = position;
                if (amount <= .001f) { if (worn.Tilt != Vector3.zero) { worn.Tilt = Vector3.zero; worn.Content.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); } continue; }
                var local = Quaternion.Inverse(worn.Holder.transform.rotation) * velocity; var target = SwayTarget(local, amount);
                worn.Tilt = Vector3.Lerp(worn.Tilt, target, 1f - Mathf.Exp(-9f * dt)); worn.Content.transform.localRotation = Quaternion.Euler(worn.Tilt * Mathf.Rad2Deg) * Quaternion.Euler(0f, 180f, 0f);
            }
        }

        /// <summary>Whether something is worn at the hand of a side (the fingers close on it).</summary>
        public bool HoldsHand(char side) => _worn.Values.Any(w => w.Holder != null && AttachmentPoints.Find(w.Data.Point)?.Bone == "hand_" + side);

        public void Clear() { foreach (var w in _worn.Values) if (w.Holder != null) Destroy(w.Holder); _worn.Clear(); }
    }
}
