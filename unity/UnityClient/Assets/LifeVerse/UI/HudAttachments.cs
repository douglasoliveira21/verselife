using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeVerse.Shared.Avatar;
using LifeVerse.Unity.Avatar;
using UnityEngine;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// Objects worn at a HUD point sit on the screen, not on the body. Each one is a model held in front of the camera and kept at its corner (or the middle) of the screen, sized in pixels: the position moves it from the anchor
    /// (a metre is 200 pixels), the rotation turns it and the scale sizes it. Being drawn in the scene itself it is lit like the world and needs no transparent picture (HDRP drops the alpha of one). It takes no mouse.
    /// </summary>
    public sealed class HudAttachments
    {
        public const int Cell = 160, Margin = 24, PixelsPerMetre = 200;
        /// <summary>How far in front of the camera the models are held, in metres (past the near plane, short of ordinary things).</summary>
        public const float Depth = .7f;
        private sealed class Shown { public AttachmentData Data; public GameObject Root, Turn; }
        private sealed class Driver : MonoBehaviour { public HudAttachments Owner; private void LateUpdate() { Owner?.Follow(); } }

        private readonly UiRoot _ui; private readonly Func<Camera> _camera; private readonly Dictionary<Guid, Shown> _shown = new Dictionary<Guid, Shown>(); private readonly Dictionary<Guid, Task<GLTFast.GltfImport>> _imports = new Dictionary<Guid, Task<GLTFast.GltfImport>>();
        private readonly GameObject _host;
        public int Count => _shown.Values.Count(s => s.Root != null);
        public Transform RootOf(Guid id) => _shown.TryGetValue(id, out var s) && s.Root != null ? s.Root.transform : null;

        public HudAttachments(UiRoot ui, Func<Camera> camera)
        {
            _ui = ui; _camera = camera; _host = new GameObject("HudAttachments"); _host.AddComponent<Driver>().Owner = this;
        }

        /// <summary>Where a HUD point puts the top-left corner of the model's cell, for a screen of this size and an offset in metres (the screen's y grows downwards).</summary>
        public static Vector2 Anchor(string point, Vector2 screen, Vector2 offsetMetres)
        {
            var half = Cell / 2f; var x = point == "hud_top_left" || point == "hud_bottom_left" ? Margin : point == "hud_top_right" || point == "hud_bottom_right" ? screen.x - Cell - Margin : screen.x / 2f - half;
            var y = point == "hud_top_left" || point == "hud_top" || point == "hud_top_right" ? Margin : point == "hud_bottom_left" || point == "hud_bottom" || point == "hud_bottom_right" ? screen.y - Cell - Margin : screen.y / 2f - half;
            return new Vector2(x + offsetMetres.x * PixelsPerMetre, y - offsetMetres.y * PixelsPerMetre);
        }

        /// <summary>Where the middle of the model is on the screen, in pixels with y going up.</summary>
        public static Vector2 ScreenPoint(AttachmentData data, Vector2 screen)
        {
            var at = Anchor(data.Point, screen, new Vector2(data.Position[0], data.Position[1])); return new Vector2(at.x + Cell / 2f, screen.y - (at.y + Cell / 2f));
        }

        /// <summary>Shows what is worn at HUD points and removes the rest.</summary>
        public void Apply(IReadOnlyList<AttachmentData> list)
        {
            var wanted = (list ?? new List<AttachmentData>()).Where(a => a.Equipped && a.IsHud).ToList(); var ids = new HashSet<Guid>(wanted.Select(a => a.Id));
            foreach (var id in _shown.Keys.Where(k => !ids.Contains(k)).ToList()) Remove(id);
            foreach (var data in wanted)
            {
                if (_shown.TryGetValue(data.Id, out var current) && current.Data.AssetId == data.AssetId) { current.Data = data; continue; }
                if (_shown.ContainsKey(data.Id)) Remove(data.Id);
                var shown = new Shown { Data = data }; _shown[data.Id] = shown; _ = Load(shown);
            }
        }

        private async Task Load(Shown shown)
        {
            try
            {
                if (AvatarAttachments.Loader == null) return;
                if (!_imports.TryGetValue(shown.Data.AssetId, out var task)) { task = Import(shown.Data.AssetId); _imports[shown.Data.AssetId] = task; }
                var import = await task; if (import == null) { _imports.Remove(shown.Data.AssetId); return; }
                if (_host == null || !_shown.TryGetValue(shown.Data.Id, out var still) || still != shown) return;
                var root = new GameObject("Hud " + shown.Data.Point); root.transform.SetParent(_host.transform, false); var turn = new GameObject("Turn"); turn.transform.SetParent(root.transform, false);
                var model = new GameObject("Model"); model.transform.SetParent(turn.transform, false); model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                if (!await import.InstantiateMainSceneAsync(model.transform) || !_shown.ContainsKey(shown.Data.Id)) { UnityEngine.Object.Destroy(root); return; }
                foreach (var collider in root.GetComponentsInChildren<Collider>()) UnityEngine.Object.Destroy(collider);
                // measured at the origin (identity): its longest side becomes one unit and its middle the origin of the turn
                var renderers = root.GetComponentsInChildren<Renderer>(); if (renderers.Length == 0) { UnityEngine.Object.Destroy(root); return; }
                var bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds); var longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)); var factor = longest > 1e-5f ? 1f / longest : 1f;
                model.transform.localScale = Vector3.one * factor; model.transform.localPosition = -(bounds.center - turn.transform.position) * factor;
                shown.Root = root; shown.Turn = turn; LifeVerse.Unity.World.ModelAnimations.StartFirst(model); Follow();
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException)) { Debug.LogWarning("[LifeVerse] anexo de tela " + shown.Data.Name + ": " + exception.Message); }
        }

        private static async Task<GLTFast.GltfImport> Import(Guid asset)
        {
            var bytes = await AvatarAttachments.Loader(asset); if (bytes == null) return null; var import = new GLTFast.GltfImport(); return await import.LoadGltfBinary(bytes, new Uri("file:///hud.glb")) ? import : null;
        }

        /// <summary>Puts every model where its point is on the screen, in front of the camera, sized in pixels.</summary>
        public void Follow()
        {
            var camera = _camera != null ? _camera() : null; if (camera == null) return; var screen = new Vector2(camera.pixelWidth, camera.pixelHeight); if (screen.x < 2f || screen.y < 2f) return;
            // the UI is laid out in its own units (it may be scaled): the anchor is worked out there and mapped to the camera's pixels
            var panel = _ui != null && _ui.Root != null && _ui.Root.resolvedStyle.width > 1f ? new Vector2(_ui.Root.resolvedStyle.width, _ui.Root.resolvedStyle.height) : screen; var scale = screen.x / panel.x;
            var worldPerPixel = 2f * Depth * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad) / screen.y;
            foreach (var shown in _shown.Values)
            {
                if (shown.Root == null) continue; var d = shown.Data; var point = ScreenPoint(d, panel) * scale;
                var world = camera.ScreenToWorldPoint(new Vector3(point.x, point.y, Depth)); var t = shown.Root.transform; t.position = world; t.rotation = camera.transform.rotation;
                var size = Cell * scale * worldPerPixel * .8f; t.localScale = Vector3.one * size;
                var rotation = Quaternion.Euler(d.Rotation[0], d.Rotation[1], d.Rotation[2]); shown.Turn.transform.localRotation = new Quaternion(-rotation.x, -rotation.y, rotation.z, rotation.w);
                shown.Turn.transform.localScale = new Vector3(Mathf.Max(.001f, d.Scale[0]), Mathf.Max(.001f, d.Scale[1]), Mathf.Max(.001f, d.Scale[2]));
            }
        }

        private void Remove(Guid id)
        {
            if (!_shown.TryGetValue(id, out var s)) return; _shown.Remove(id); if (s.Root != null) UnityEngine.Object.Destroy(s.Root);
        }

        public void Clear() { foreach (var id in _shown.Keys.ToList()) Remove(id); if (_host != null) UnityEngine.Object.Destroy(_host); }
    }
}
