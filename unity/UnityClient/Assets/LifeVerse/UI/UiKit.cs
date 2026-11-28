using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>The small pieces every screen is made of (labels, buttons, fields, rows), styled by the classes of Theme.uss.</summary>
    public static class UiKit
    {
        public static Label Text(string text, params string[] classes) { var label = new Label(text); foreach (var c in classes) label.AddToClassList(c); label.pickingMode = PickingMode.Ignore; return label; }

        /// <summary>The icon a button draws before its words (see <see cref="WithIcon"/>).</summary>
        public sealed class ButtonIcon { public string Name; public float Size; public bool Only, Stacked, Alert; /// <summary>The Varanda icon drawn instead of the line one (the bar and the sheet).</summary>
            public string Art; }

        /// <summary>Puts an icon before the words of a button. It is drawn by the button itself (a child element would stop the button from measuring its words), and the words stay its text.</summary>
        public static Button WithIcon(Button button, string icon, float size = 16f)
        {
            var mark = new ButtonIcon { Name = icon, Size = size }; button.userData = mark;
            button.style.paddingLeft = 8 + size + 5; button.style.flexShrink = 0; button.style.whiteSpace = WhiteSpace.NoWrap; button.AddToClassList("lv-btn-icon");
            button.generateVisualContent += context => Draw(context, button, mark); button.RegisterCallback<CustomStyleResolvedEvent>(_ => button.MarkDirtyRepaint());
            return button;
        }

        /// <summary>A button of the bottom toolbar: the icon on top and the words under it (the words stay its text).</summary>
        public static Button Stacked(Button button, string icon, float size = 22f)
        {
            var mark = new ButtonIcon { Name = icon, Size = size, Stacked = true }; button.userData = mark; button.style.whiteSpace = WhiteSpace.NoWrap; button.AddToClassList("lv-btn-icon");
            button.generateVisualContent += context => Draw(context, button, mark); button.RegisterCallback<CustomStyleResolvedEvent>(_ => button.MarkDirtyRepaint());
            return button;
        }

        private static void Draw(MeshGenerationContext context, Button button, ButtonIcon mark)
        {
            var box = button.contentRect; var (ink, accent, surface) = VarandaIcons.ColoursOf(button); var colour = mark.Only ? ink : button.resolvedStyle.color;
            Rect at;
            if (mark.Only) at = new Rect((button.layout.width - mark.Size) / 2f, (button.layout.height - mark.Size) / 2f, mark.Size, mark.Size);
            else if (mark.Stacked) at = new Rect((button.layout.width - mark.Size) / 2f, 8f, mark.Size, mark.Size);
            else at = new Rect(box.x - mark.Size - 5f, box.y + (box.height - mark.Size) / 2f, mark.Size, mark.Size);
            if (VarandaIcons.Has(mark.Art)) VarandaIcons.Draw(context, mark.Art, at, mark.Stacked ? ink : colour, accent, surface); else Icon.DrawInto(context, mark.Name, at, colour);
            if (!mark.Alert) return;
            var painter = context.painter2D; painter.fillColor = new Color(.84f, .24f, .17f); painter.BeginPath(); painter.Arc(new Vector2(at.xMax + 2f, at.y + 2f), 4.5f, new Angle(0f), new Angle(360f)); painter.Fill();
        }

        /// <summary>Puts a red dot on the icon of a button (something waits there), or takes it away.</summary>
        public static void Alert(Button button, bool on) { if (button?.userData is not ButtonIcon mark || mark.Alert == on) return; mark.Alert = on; button.MarkDirtyRepaint(); }

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
            mark.Only = on; button.style.color = on ? new Color(0f, 0f, 0f, 0f) : StyleKeyword.Null; button.style.width = on ? (mark.Stacked ? 42 : 36) : StyleKeyword.Null; button.style.overflow = on ? Overflow.Hidden : StyleKeyword.Null;
            if (mark.Stacked) { button.EnableInClassList("lv-tool-compact", on); button.style.minWidth = on ? 40 : StyleKeyword.Null; }
            else { button.style.paddingLeft = on ? 0 : 8 + mark.Size + 5; button.style.paddingRight = on ? 0 : StyleKeyword.Null; }
            button.MarkDirtyRepaint();
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
