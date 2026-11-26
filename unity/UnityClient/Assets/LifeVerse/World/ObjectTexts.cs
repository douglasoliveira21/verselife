using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// The floating text that scripts put over objects (the island announces "text": objectId, text, color and the spot x, y, z), as the Godot client shows it: one line per object, a little above the spot, turned to
    /// the camera, in the colour asked, cut at 120 letters; an empty text takes it away. At most <see cref="MaxTexts"/> at once, and each one is seen only from up to <see cref="SeenFrom"/> metres.
    /// </summary>
    public sealed class ObjectTexts : MonoBehaviour
    {
        public const int MaxTexts = 64, MaxLength = 120; public const float SeenFrom = 40f, Above = .4f;
        private WorldClient _client; private readonly Dictionary<Guid, TextMesh> _labels = new Dictionary<Guid, TextMesh>(); private Font _font;
        public int Count => _labels.Count;
        public string TextOf(Guid id) => _labels.TryGetValue(id, out var label) && label != null ? label.text : null;
        public Color ColorOf(Guid id) => _labels.TryGetValue(id, out var label) && label != null ? label.color : Color.clear;
        public Vector3? PlaceOf(Guid id) => _labels.TryGetValue(id, out var label) && label != null ? label.transform.position : (Vector3?)null;

        public void Attach(WorldClient client)
        {
            if (_client != null) _client.BuildOperation -= OnOperation; _client = client; if (client != null) client.BuildOperation += OnOperation;
        }

        private void OnDestroy() { if (_client != null) _client.BuildOperation -= OnOperation; }

        /// <summary>One operation of the island (public so tests feed the same JSON).</summary>
        public void OnOperation(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json); var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("operation", out var op) || op.GetString() != "text" || !root.TryGetProperty("objectId", out var id) || !Guid.TryParse(id.GetString(), out var objectId)) return;
                float N(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var f) && !float.IsNaN(f) && !float.IsInfinity(f) ? f : 0f;
                Show(objectId, root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "", root.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : "#FFFFFF", new Vector3(N("x"), N("y"), N("z")));
            }
            catch (Exception exception) when (exception is JsonException || exception is InvalidOperationException || exception is FormatException) { }
        }

        /// <summary>Puts (or changes, or with an empty text takes away) the text of an object at a spot of the game.</summary>
        public void Show(Guid objectId, string text, string color, Vector3 gameSpot)
        {
            text = (text ?? "").Replace("\r", ""); if (text.Length == 0) { if (_labels.TryGetValue(objectId, out var old)) { if (old != null) Drop(old.gameObject); _labels.Remove(objectId); } return; }
            if (!_labels.TryGetValue(objectId, out var label) || label == null)
            {
                if (_labels.Count >= MaxTexts) return;
                var go = new GameObject("Text " + objectId.ToString("N").Substring(0, 8)); go.transform.SetParent(transform, false);
                label = go.AddComponent<TextMesh>(); _font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.font = _font; go.GetComponent<MeshRenderer>().sharedMaterial = _font.material;
                go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; label.fontSize = 56; label.characterSize = .03f; label.anchor = TextAnchor.LowerCenter; label.alignment = TextAlignment.Center;
                _labels[objectId] = label;
            }
            label.text = text.Length > MaxLength ? text.Substring(0, MaxLength) : text; label.color = ColorUtility.TryParseHtmlString(color ?? "", out var parsed) ? parsed : Color.white;
            label.transform.position = GameSpace.ToUnity(gameSpot.x, gameSpot.y + Above, gameSpot.z);
        }

        public void Clear() { foreach (var label in _labels.Values) if (label != null) Drop(label.gameObject); _labels.Clear(); }
        private static void Drop(GameObject go) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }

        private void LateUpdate()
        {
            var camera = Camera.main; if (camera == null) return;
            foreach (var label in _labels.Values)
            {
                if (label == null) continue; var away = camera.transform.position - label.transform.position; var show = away.sqrMagnitude < SeenFrom * SeenFrom;
                if (show) label.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);   // TextMesh is read from its back
                if (label.gameObject.activeSelf != show) label.gameObject.SetActive(show);
            }
        }
    }
}
