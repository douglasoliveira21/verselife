using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>The face of a person where the interface has no picture of them: a disc in a colour that is always the same for that name, with the first letter.</summary>
    public static class Faces
    {
        // white letters read on every one of these
        private static readonly Color32[] Palette = { new Color32(0x14, 0x6B, 0x64, 255), new Color32(0x8A, 0x57, 0x0C, 255), new Color32(0x5E, 0x4B, 0x91, 255), new Color32(0x2F, 0x57, 0x91, 255), new Color32(0x7A, 0x3B, 0x5E, 255), new Color32(0x4C, 0x65, 0x2A, 255) };

        public static Color ColourOf(string name)
        {
            var hash = 17; foreach (var c in (name ?? "").Trim().ToLowerInvariant()) hash = unchecked(hash * 31 + c);
            return Palette[(hash & 0x7FFFFFFF) % Palette.Length];
        }

        public static string InitialOf(string name)
        {
            var text = (name ?? "").Trim(); if (text.Length == 0) return "?";
            return char.IsSurrogatePair(text, 0) ? text.Substring(0, 2) : text.Substring(0, 1).ToUpperInvariant();
        }

        public static VisualElement Make(string name, params string[] classes)
        {
            var face = new VisualElement { pickingMode = PickingMode.Ignore }; face.AddToClassList("lv-face"); foreach (var c in classes) face.AddToClassList(c);
            face.Add(UiKit.Text("", "lv-face-initial")); Set(face, name); return face;
        }

        public static void Set(VisualElement face, string name) { face.style.backgroundColor = ColourOf(name); if (face.childCount > 0 && face[0] is Label initial) initial.text = InitialOf(name); }
    }
}
