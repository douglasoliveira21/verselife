using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// "Legendas": lines over the toolbar saying who is talking by voice and which sounds play around (speech is not written out: the game does not transcribe it).
    /// A line that is said again stays instead of repeating; each leaves a few seconds after it was last said.
    /// </summary>
    public sealed class Captions : VisualElement
    {
        public const int MaxLines = 3; public const float Seconds = 3f;
        private readonly List<(string Text, Label Label, float Until)> _lines = new List<(string, Label, float)>();
        public IReadOnlyList<string> Lines => _lines.Select(l => l.Text).ToList();

        public Captions()
        {
            name = "captions"; pickingMode = PickingMode.Ignore; style.position = Position.Absolute; style.left = 0; style.right = 0; style.bottom = FloatingWindow.BottomInset + 12f; style.alignItems = Align.Center;
        }

        public void Say(string text, float now)
        {
            if (string.IsNullOrEmpty(text)) return;
            var at = _lines.FindIndex(l => l.Text == text);
            if (at >= 0) { _lines[at] = (text, _lines[at].Label, now + Seconds); return; }
            if (_lines.Count >= MaxLines) Drop(0);
            var label = UiKit.Text(text, "lv-caption"); Add(label); _lines.Add((text, label, now + Seconds));
        }

        /// <summary>Takes away the lines whose time passed.</summary>
        public void Tick(float now) { for (var i = _lines.Count - 1; i >= 0; i--) if (_lines[i].Until <= now) Drop(i); }
        public void Clear() { while (_lines.Count > 0) Drop(0); }
        private void Drop(int at) { _lines[at].Label.RemoveFromHierarchy(); _lines.RemoveAt(at); }

        /// <summary>The words for a sound heard at a distance.</summary>
        public static string Sound(bool media, float metres) => (media ? "♪ Música ou rádio tocando" : "♪ Som de um objeto") + (metres >= 1f ? " a " + Mathf.RoundToInt(metres) + " m" : "");
    }
}
