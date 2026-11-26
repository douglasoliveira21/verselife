using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>The small pieces every screen is made of (labels, buttons, fields, rows), styled by the classes of Theme.uss ("vidro escuro").</summary>
    public static class UiKit
    {
        public static Label Text(string text, params string[] classes) { var label = new Label(text); foreach (var c in classes) label.AddToClassList(c); label.pickingMode = PickingMode.Ignore; return label; }

        /// <summary>Puts an icon before the words of a button (the words stay its text, for whoever reads it).</summary>
        /// <summary>The icon a button draws before its words (see <see cref="WithIcon"/>).</summary>
        public sealed class ButtonIcon { public string Name; public float Size; public bool Only; }

        /// <summary>Puts an icon before the words of a button. It is drawn by the button itself (a child element would stop the button from measuring its words), and the words stay its text.</summary>
        public static Button WithIcon(Button button, string icon, float size = 16f)
        {
            var mark = new ButtonIcon { Name = icon, Size = size }; button.userData = mark;
            button.style.paddingLeft = 8 + size + 5; button.style.flexShrink = 0; button.style.whiteSpace = WhiteSpace.NoWrap; button.AddToClassList("lv-btn-icon");
            button.generateVisualContent += context =>
            {
                var box = button.contentRect; var left = mark.Only ? (button.layout.width - mark.Size) / 2f - button.resolvedStyle.paddingLeft - button.resolvedStyle.borderLeftWidth : -mark.Size - 5f;
                var colour = mark.Only ? new Color(.9f, .93f, .95f) : button.resolvedStyle.color;
                Icon.DrawInto(context, mark.Name, new Rect(box.x + left, box.y + (box.height - mark.Size) / 2f, mark.Size, mark.Size), colour);
            };
            return button;
        }

        /// <summary>An icon after the words of a button (the caret of a menu).</summary>
        public static Button WithTrailingIcon(Button button, string icon, float size = 12f)
        {
            button.style.paddingRight = 8 + size + 4; button.style.flexShrink = 0; button.style.whiteSpace = WhiteSpace.NoWrap;
            button.generateVisualContent += context => { var box = button.contentRect; Icon.DrawInto(context, icon, new Rect(box.xMax + 4f, box.y + (box.height - size) / 2f, size, size), button.resolvedStyle.color); };
            return button;
        }

        public static string IconOf(Button button) => (button?.userData as ButtonIcon)?.Name;

        /// <summary>Shows only the icon of a button (the compact toolbar); the words come back when <paramref name="on"/> is false. The words become the tooltip.</summary>
        public static void IconOnly(Button button, bool on)
        {
            if (button.userData is not ButtonIcon mark) { button.style.fontSize = on ? 11 : StyleKeyword.Null; return; }
            mark.Only = on; button.style.color = on ? new Color(0f, 0f, 0f, 0f) : StyleKeyword.Null; button.style.width = on ? 36 : StyleKeyword.Null; button.style.overflow = on ? Overflow.Hidden : StyleKeyword.Null;
            button.style.paddingLeft = on ? 0 : 8 + mark.Size + 5; button.style.paddingRight = on ? 0 : StyleKeyword.Null; button.MarkDirtyRepaint();
        }

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
