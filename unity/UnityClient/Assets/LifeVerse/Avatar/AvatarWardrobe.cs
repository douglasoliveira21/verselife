using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LifeVerse.Shared.Avatar;
using UnityEngine;

namespace LifeVerse.Unity.Avatar
{
    /// <summary>
    /// What a person brought for their avatar, on a skeletal body (the man of the game or their own body), as the Godot client does it: their own clothes (a rigid piece — hat, glasses, backpack — fixed to a bone with
    /// its offset, turn and size; a skinned garment bound to the body's bones by their standard names, so it bends with the body) and their own animations for some states (custom:{file}#{name}). The files come from
    /// <c>/api/v1/avatars/wearables|animations/{id}/file</c> through <see cref="Files"/>; each file is fetched once and kept.
    /// </summary>
    public sealed class AvatarWardrobe : MonoBehaviour
    {
        /// <summary>Gets the bytes of a file of avatars: ("wearables" or "animations", id). Set by the game; without it nothing custom is drawn.</summary>
        public static Func<string, Guid, Task<byte[]>> Files;
        private static readonly Dictionary<(string, Guid), Task<byte[]>> Cache = new Dictionary<(string, Guid), Task<byte[]>>();
        private readonly List<GameObject> _items = new List<GameObject>(); private string _dressed = ""; private int _generation; private SkeletalBody _body;
        public int Wearables => _items.Count(i => i != null);
        public int Clips { get; private set; }
        public int Skinned { get; private set; }

        /// <summary>One custom item as the platform describes it in the appearance.</summary>
        public sealed class Item { public Guid Id; public string Slot = "", Kind = "", AttachBone = ""; public Vector3 Offset, Rotation; public float Scale = 1f; }

        public static List<Item> ReadItems(string json)
        {
            var list = new List<Item>();
            try
            {
                using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json); if (d.RootElement.ValueKind != JsonValueKind.Array) return list;
                foreach (var e in d.RootElement.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("id", out var id) || !Guid.TryParse(id.GetString(), out var g)) continue;
                    string S(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                    Vector3 V(string n) { if (!e.TryGetProperty(n, out var v) || v.ValueKind != JsonValueKind.Array) return Vector3.zero; var f = v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetSingle() : 0f).ToArray(); return f.Length >= 3 ? new Vector3(f[0], f[1], f[2]) : Vector3.zero; }
                    list.Add(new Item { Id = g, Slot = S("slot"), Kind = S("kind"), AttachBone = S("attachBone"), Offset = V("offset"), Rotation = V("rotation"), Scale = e.TryGetProperty("scale", out var sc) && sc.ValueKind == JsonValueKind.Number && sc.GetSingle() > 0 ? sc.GetSingle() : 1f });
                    if (list.Count >= 12) break;
                }
            }
            catch (JsonException) { }
            return list;
        }

        /// <summary>Puts the person's own clothes and animations on their skeletal body (again only when something changed).</summary>
        public async void Dress(SkeletalBody body, AvatarAppearance appearance)
        {
            if (body == null || appearance == null) return;
            if (body != _body) { _body = body; _dressed = ""; } var key = "|" + appearance.CustomWearablesJson + "|" + string.Join(",", appearance.AnimationOverrides.Select(p => p.Key + "=" + p.Value)); if (key == _dressed) return; _dressed = key;
            var generation = ++_generation; Clear(); body.Overrides.Clear(); Clips = 0; Skinned = 0; if (Files == null) return;
            try
            {
                foreach (var pair in appearance.AnimationOverrides)
                {
                    if (!AnimationStates.TryParseCustom(pair.Value, out var file, out var name)) continue;
                    var bytes = await Fetch("animations", file); if (generation != _generation || body == null) return;
                    if (bytes != null && SkeletonClip.Read(bytes, name) is { } clip) { body.Overrides[pair.Key] = clip; Clips++; }
                }
                foreach (var item in ReadItems(appearance.CustomWearablesJson))
                {
                    var bytes = await Fetch("wearables", item.Id); if (generation != _generation || body == null) return; if (bytes == null) continue;
                    var import = new GLTFast.GltfImport(); if (!await import.LoadGltfBinary(bytes, new Uri("file:///wearable.glb"))) continue; if (generation != _generation || body == null) return;
                    var holder = new GameObject("Wearable " + item.Slot); if (!await import.InstantiateMainSceneAsync(holder.transform)) { Destroy(holder); continue; }
                    foreach (var collider in holder.GetComponentsInChildren<Collider>()) Destroy(collider);
                    var skins = holder.GetComponentsInChildren<SkinnedMeshRenderer>();
                    if (skins.Length > 0 && Bind(skins, body)) { holder.transform.SetParent(body.Content, false); Skinned++; }
                    else
                    {
                        var bone = body.BoneOf(BoneMapper.Map(item.AttachBone) ?? DefaultBone(item.Slot)) ?? body.Content; holder.transform.SetParent(bone, false);
                        holder.transform.localPosition = new Vector3(item.Offset.x, item.Offset.y, -item.Offset.z); var r = Quaternion.Euler(item.Rotation); holder.transform.localRotation = new Quaternion(-r.x, -r.y, r.z, r.w) * Quaternion.Euler(0f, 180f, 0f);
                        holder.transform.localScale = Vector3.one * Mathf.Clamp(item.Scale, .05f, 10f) / Mathf.Max(.001f, bone.lossyScale.x / Mathf.Max(.001f, body.Content.lossyScale.x));
                    }
                    _items.Add(holder);
                }
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException)) { Debug.LogWarning("[LifeVerse] roupa ou animação própria: " + exception.Message); }
        }

        /// <summary>The bone a piece goes on when it does not say: by its slot.</summary>
        public static string DefaultBone(string slot) => slot switch { "hat" or "glasses" or "ears" or "eyes" or "hair" => "head", "neck" => "neck", "back" => "chest", "gloves" => "hand_r", "shoes" => "foot_l", _ => "hips" };

        /// <summary>Points each bone of a skinned garment at the body's bone of the same standard name; false when too few match (then the piece is fixed rigidly instead).</summary>
        public static bool Bind(SkinnedMeshRenderer[] skins, SkeletalBody body)
        {
            var bound = 0; var total = 0;
            foreach (var skin in skins)
            {
                var bones = skin.bones; var mapped = new Transform[bones.Length];
                for (var i = 0; i < bones.Length; i++) { total++; var standard = bones[i] != null ? BoneMapper.Map(bones[i].name) : null; var target = standard != null ? body.BoneOf(standard) : null; mapped[i] = target != null ? target : body.BoneOf("hips"); if (target != null) bound++; }
                if (mapped.Any(b => b == null)) return false; skin.bones = mapped; skin.rootBone = body.BoneOf("hips") ?? mapped.FirstOrDefault(); skin.updateWhenOffscreen = true;
            }
            return total > 0 && bound * 2 >= total;
        }

        private static Task<byte[]> Fetch(string kind, Guid id)
        {
            lock (Cache) { if (!Cache.TryGetValue((kind, id), out var task) || task.IsFaulted) { task = Files(kind, id); Cache[(kind, id)] = task; } return task; }
        }

        private void Clear() { foreach (var item in _items) if (item != null) Destroy(item); _items.Clear(); }
        private void OnDestroy() => Clear();
    }
}
