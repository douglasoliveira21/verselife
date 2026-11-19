using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// A window floating over the game: a title bar you drag, a close button, and a body for whatever the window shows. It comes to the front when clicked, stays inside the screen,
    /// and remembers where the person left it (the position goes into <see cref="UserSettings.Windows"/> under the window's id, as the Godot windows do). Several can be open.
    /// </summary>
    public class FloatingWindow : VisualElement
    {
        public string Id { get; }
        public VisualElement Body { get; }
        public Label Title { get; }
        public event Action Closed;
        protected readonly UiRoot Ui; private Vector2 _grab; private bool _dragging;

        public FloatingWindow(UiRoot ui, string id, string title, float width = 360f, float height = -1f, Vector2? defaultPosition = null)
        {
            Ui = ui; Id = id; AddToClassList("lv-window"); style.width = width; if (height > 0) style.height = height; pickingMode = PickingMode.Position;
            var bar = new VisualElement(); bar.AddToClassList("lv-window-title"); Title = UiKit.Text(title, "lv-title-text"); bar.Add(Title);
            var close = UiKit.Button("✕", Close, "lv-close"); bar.Add(close); Add(bar);
            Body = new VisualElement(); Body.AddToClassList("lv-window-body"); Add(Body);
            var start = ui.Settings.Windows.TryGetValue(id, out var saved) ? new Vector2(saved[0], saved[1]) : defaultPosition ?? new Vector2(80, 80);
            style.left = start.x; style.top = start.y;
            RegisterCallback<PointerDownEvent>(e => BringToFront());
            bar.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0 || e.target is Button) return; _dragging = true; _grab = new Vector2(e.position.x - resolvedStyle.left, e.position.y - resolvedStyle.top); bar.CapturePointer(e.pointerId); });
            bar.RegisterCallback<PointerMoveEvent>(e => { if (_dragging) MoveTo(new Vector2(e.position.x - _grab.x, e.position.y - _grab.y)); });
            bar.RegisterCallback<PointerUpEvent>(e => { if (!_dragging) return; _dragging = false; bar.ReleasePointer(e.pointerId); Remember(); });
        }

        /// <summary>Moves the window, keeping the title bar on the screen.</summary>
        public void MoveTo(Vector2 position)
        {
            var host = Ui.WindowLayer.layout; var maxX = Mathf.Max(0f, (host.width > 1 ? host.width : 1920f) - 80f); var maxY = Mathf.Max(0f, (host.height > 1 ? host.height : 1080f) - 40f);
            style.left = Mathf.Clamp(position.x, -resolvedStyle.width + 80f, maxX); style.top = Mathf.Clamp(position.y, 0f, maxY);
        }

        private void Remember()
        {
            if (Ui.Settings.Windows.Count >= UserSettings.MaxSavedWindows && !Ui.Settings.Windows.ContainsKey(Id)) return;
            Ui.Settings.Windows[Id] = new[] { resolvedStyle.left, resolvedStyle.top };
        }

        public void Open() { if (parent == null) Ui.WindowLayer.Add(this); BringToFront(); }

        public void Close() { RemoveFromHierarchy(); Remember(); Closed?.Invoke(); }

        public bool IsOpen => parent != null;
    }
}
