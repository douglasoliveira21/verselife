using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>The small pieces every screen is made of (labels, buttons, fields, rows), styled by the classes of Theme.uss ("vidro escuro").</summary>
    public static class UiKit
    {
        public static Label Text(string text, params string[] classes) { var label = new Label(text); foreach (var c in classes) label.AddToClassList(c); label.pickingMode = PickingMode.Ignore; return label; }

        public static Button Button(string text, Action onClick, string kind = "lv-btn")
        {
            var button = new Button(() => { LifeVerse.Unity.Audio.UiSounds.Click(); onClick?.Invoke(); }) { text = text }; button.AddToClassList("lv-btn"); if (kind != "lv-btn") button.AddToClassList(kind); return button;
        }

        public static TextField Field(string label, bool password = false, int maxLength = 128, string value = "")
        {
            var field = new TextField(label) { isPasswordField = password, maxLength = maxLength, value = value }; field.AddToClassList("lv-field");
            field.labelElement.AddToClassList("lv-muted"); field.labelElement.style.minWidth = 90; field.style.flexDirection = FlexDirection.Column; field.labelElement.style.minWidth = 0; return field;
        }

        public static Toggle Check(string label, bool value, Action<bool> onChange)
        {
            var toggle = new Toggle(label) { value = value }; toggle.AddToClassList("lv-toggle"); toggle.RegisterValueChangedCallback(e => { LifeVerse.Unity.Audio.UiSounds.Play(LifeVerse.Unity.Audio.UiSound.Toggle); onChange(e.newValue); }); return toggle;
        }

        public static Slider Slide(string label, float min, float max, float value, Action<float> onChange, bool percent = false)
        {
            var slider = new Slider(label, min, max) { value = value, showInputField = false }; slider.AddToClassList("lv-slider"); slider.RegisterValueChangedCallback(e => onChange(e.newValue)); return slider;
        }

        public static VisualElement Row(params VisualElement[] children)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center; foreach (var child in children) row.Add(child); return row;
        }

        public static VisualElement Spacer(float grow = 1f) { var spacer = new VisualElement(); spacer.style.flexGrow = grow; return spacer; }

        public static VisualElement Divider() { var divider = new VisualElement(); divider.AddToClassList("lv-divider"); return divider; }

        public static VisualElement Glass() { var glass = new VisualElement(); glass.AddToClassList("lv-glass"); return glass; }

        /// <summary>Tells apart a click on the interface from one on the world (the interface eats the mouse only where it draws something).</summary>
        public static bool IsOver(VisualElement root, Vector2 screenPosition)
        {
            if (root?.panel == null) return false;
            var position = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            var picked = root.panel.Pick(position); return picked != null && picked != root && !picked.ClassListContains("lv-passthrough");
        }
    }
}
