using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The yellow hints of the viewer: a game's panel does not show the <c>tooltip</c> of an element by itself, so this waits a moment with the mouse over something that has one and shows it
    /// beside the pointer, inside the screen. A button that already says the same words shows nothing; moving away or clicking takes the hint down.
    /// </summary>
    public sealed class Tooltips
    {
        public const long DelayMs = 450;
        private readonly VisualElement _root, _layer; private readonly Label _label; private VisualElement _over, _shownFor; private Vector2 _at; private IVisualElementScheduledItem _wait;
        public bool Shown => _label.parent != null;
        public string Text => Shown ? _label.text : "";

        public Tooltips(VisualElement root, VisualElement layer)
        {
            _root = root; _layer = layer; _label = new Label { pickingMode = PickingMode.Ignore, name = "tooltip" }; _label.AddToClassList("lv-tooltip");
            root.RegisterCallback<PointerOverEvent>(e => { _at = e.position; Over(e.target as VisualElement); }, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerMoveEvent>(e => _at = e.position, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerDownEvent>(e => Hide(), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerLeaveEvent>(e => { _over = null; Hide(); });
            // leaving something for the world (where the interface draws nothing) takes the hint down too
            root.RegisterCallback<PointerOutEvent>(e => { if (_over != null && !_over.worldBound.Contains((Vector2)e.position)) { _over = null; Hide(); } }, TrickleDown.TrickleDown);
            // and so does closing what it was about
            root.schedule.Execute(() => { if (Shown && (_over ?? _shownFor)?.panel == null) { _over = null; Hide(); } }).Every(250);
        }

        /// <summary>What an element would say when the pointer rests on it ("" for nothing): its own tooltip or the one of what holds it.</summary>
        public static string Of(VisualElement element, out VisualElement owner)
        {
            for (var e = element; e != null; e = e.parent)
            {
                if (string.IsNullOrEmpty(e.tooltip)) continue;
                owner = e; var repeats = e is Button b && b.text == e.tooltip && !(b.userData is UiKit.ButtonIcon { Only: true });
                return repeats ? "" : e.tooltip;
            }
            owner = null; return "";
        }

        /// <summary>The pointer is over an element now.</summary>
        public void Over(VisualElement element)
        {
            var words = Of(element, out var owner); if (owner == _over && owner != null) return; _over = owner; Hide(); if (words.Length == 0) return;
            _wait = _root.schedule.Execute(() => Show(owner)); _wait.ExecuteLater(DelayMs);
        }

        /// <summary>Shows the hint of an element at once.</summary>
        public void Show(VisualElement owner)
        {
            var words = Of(owner, out _); if (words.Length == 0 || owner.panel == null) return;
            _shownFor = owner; _label.text = words; _layer.Add(_label);
            var screen = _root.layout; var x = _at.x + 14f; var y = _at.y + 20f;
            if (screen.width > 1f) { x = Mathf.Min(x, screen.width - 292f); if (y > screen.height - 60f) y = _at.y - 38f; }
            _label.style.left = Mathf.Max(2f, x); _label.style.top = Mathf.Max(2f, y);
        }

        public void Hide() { _wait?.Pause(); _wait = null; _label.RemoveFromHierarchy(); }
    }
}
