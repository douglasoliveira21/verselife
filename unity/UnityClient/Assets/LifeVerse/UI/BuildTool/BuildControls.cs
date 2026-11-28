using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// A number field of the build tool: its own label, the two arrows and the text. The number is taken when Enter is pressed, the field is left or an arrow is pressed (and at once when
    /// <see cref="TextField.value"/> is set by code). A value that is not a number, or outside the limits, is not sent: the field turns red, says why and shows the last good one again.
    /// With several objects that differ it shows a dash until the person types.
    /// </summary>
    public sealed class BuildSpinner : VisualElement
    {
        public readonly TextField Input;
        public float Min = float.MinValue, Max = float.MaxValue, Step = .1f;
        public string Format = "0.###";
        /// <summary>A good number the person chose.</summary>
        public event Action<float> Committed;
        /// <summary>Why a typed text was refused.</summary>
        public event Action<string> Refused;
        private float? _value; private readonly string _title; private readonly Button _up, _down;
        public float? Value => _value;

        public BuildSpinner(string label, float labelWidth, string title)
        {
            _title = title; AddToClassList("bt-spinner");
            if (!string.IsNullOrEmpty(label)) { var words = new Label(label) { pickingMode = PickingMode.Ignore }; words.AddToClassList("bt-text"); words.style.width = labelWidth; words.style.flexShrink = 0; Add(words); LabelElement = words; }
            var arrows = new VisualElement(); arrows.AddToClassList("bt-arrows"); _up = Arrow("caret-up", +1); _down = Arrow("caret-down", -1); arrows.Add(_up); arrows.Add(_down); Add(arrows);
            Input = new TextField { isDelayed = true, maxLength = 16 }; Input.AddToClassList("bt-input"); Input.RegisterValueChangedCallback(e => Typed(e.newValue)); Add(Input);
        }

        public Label LabelElement { get; }

        private Button Arrow(string icon, int direction)
        {
            var button = new Button(() => Nudge(direction)) { focusable = false }; button.AddToClassList("bt-arrow"); button.Add(new Icon(icon, 9)); return button;
        }

        /// <summary>One press of an arrow.</summary>
        public void Nudge(int direction)
        {
            if (!enabledInHierarchy) return; var next = Mathf.Clamp((_value ?? 0f) + Step * direction, Min, Max); Accept((float)Math.Round(next, 5));
        }

        /// <summary>Reads a typed number (comma or dot).</summary>
        public static bool TryRead(string text, out float value) => float.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>Why a text cannot be this number, or null.</summary>
        public string ProblemOf(string text, out float value)
        {
            if (!TryRead(text, out value)) return _title + ": isso não é um número.";
            if (value < Min - 1e-5f || value > Max + 1e-5f) return _title + ": vai de " + Show(Min) + " a " + Show(Max) + ".";
            return null;
        }

        private void Typed(string text)
        {
            var problem = ProblemOf(text, out var value);
            if (problem != null) { EnableInClassList("bt-bad", true); Input.tooltip = problem; Input.SetValueWithoutNotify(_value.HasValue ? Show(_value.Value) : "—"); Refused?.Invoke(problem); return; }
            Accept(value);
        }

        private void Accept(float value) { EnableInClassList("bt-bad", false); Input.tooltip = ""; _value = value; Input.SetValueWithoutNotify(Show(value)); Committed?.Invoke(value); }

        private string Show(float value) => value.ToString(Format, CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator == "," ? Comma : CultureInfo.InvariantCulture);
        private static readonly NumberFormatInfo Comma = new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = "" };

        /// <summary>Shows what the island has (null: several different values). A field the person is typing in is left alone.</summary>
        public void Show(float? value, bool force = false)
        {
            if (!force && Input.focusController?.focusedElement == Input) return;
            _value = value; EnableInClassList("bt-bad", false); Input.tooltip = ""; Input.SetValueWithoutNotify(value.HasValue ? Show(value.Value) : "—");
        }

        public void Blank() { _value = null; EnableInClassList("bt-bad", false); Input.SetValueWithoutNotify(""); }
    }

    /// <summary>A small picture of one of the fifteen shapes of the Create tool, drawn in lines (24 × 24 box) in the colour of the button's text.</summary>
    public sealed class BuildShapeIcon : VisualElement
    {
        private readonly string _shape;

        public BuildShapeIcon(string shape, float size = 20f)
        {
            _shape = shape; pickingMode = PickingMode.Ignore; style.width = size; style.height = size; generateVisualContent += Draw; RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
        }

        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D; var k = contentRect.width / 24f; p.strokeColor = resolvedStyle.color; p.lineWidth = 1.5f; p.lineJoin = LineJoin.Round; p.lineCap = LineCap.Round;
            void Line(params float[] v) { p.BeginPath(); p.MoveTo(new Vector2(v[0], v[1]) * k); for (var i = 2; i < v.Length; i += 2) p.LineTo(new Vector2(v[i], v[i + 1]) * k); p.Stroke(); }
            void Oval(float cx, float cy, float rx, float ry, float from = 0f, float to = 360f)
            {
                p.BeginPath();
                for (var i = 0; i <= 28; i++) { var a = Mathf.Lerp(from, to, i / 28f) * Mathf.Deg2Rad; var at = new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry) * k; if (i == 0) p.MoveTo(at); else p.LineTo(at); }
                p.Stroke();
            }
            switch (_shape)
            {
                case "Cube": Line(4, 8, 12, 4, 20, 8, 20, 17, 12, 21, 4, 17, 4, 8, 12, 12, 20, 8); Line(12, 12, 12, 21); break;
                case "Prism": Line(7, 19, 3, 15, 10, 5, 14, 9, 7, 19, 21, 19, 14, 9); Line(10, 5, 17, 15, 21, 19); break;
                case "Pyramid": Line(12, 4, 3, 17, 12, 21, 21, 17, 12, 4, 12, 21); break;
                case "Tetrahedron": Line(12, 4, 4, 19, 20, 19, 12, 4); Line(4, 19, 13, 15, 20, 19); Line(12, 4, 13, 15); break;
                case "Cylinder": Oval(12, 6, 7, 2.6f); Line(5, 6, 5, 18); Line(19, 6, 19, 18); Oval(12, 18, 7, 2.6f, 0, 180); break;
                case "HemiCylinder": Oval(12, 6, 7, 2.6f, 0, 180); Line(5, 6, 19, 6); Line(5, 6, 5, 18); Line(19, 6, 19, 18); Oval(12, 18, 7, 2.6f, 0, 180); break;
                case "Cone": Oval(12, 18, 7, 2.6f); Line(5, 18, 12, 3, 19, 18); break;
                case "HemiCone": Oval(12, 18, 7, 2.6f, 0, 180); Line(5, 18, 19, 18); Line(5, 18, 12, 3, 19, 18); break;
                case "Sphere": Oval(12, 12, 8.5f, 8.5f); Oval(12, 12, 8.5f, 3.2f, 0, 180); break;
                case "HemiSphere": Oval(12, 16, 8.5f, 8.5f, 180, 360); Oval(12, 16, 8.5f, 3.2f); break;
                case "Torus": Oval(12, 12, 9.5f, 6.5f); Oval(12, 11, 3.8f, 2f); break;
                case "Tube": Oval(12, 7, 8, 3.2f); Oval(12, 7, 4, 1.5f); Line(4, 7, 4, 17); Line(20, 7, 20, 17); Oval(12, 17, 8, 3.2f, 0, 180); break;
                case "Ring": Line(12, 3, 21, 19, 3, 19, 12, 3); Line(12, 9, 16.5f, 16.5f, 7.5f, 16.5f, 12, 9); break;
                case "Tree": Oval(12, 9, 6.5f, 6f); Line(12, 12, 12, 21); Line(8, 21, 16, 21); break;
                default: Line(4, 21, 20, 21); Line(8, 21, 6, 11); Line(12, 21, 12, 7); Line(16, 21, 18, 12); break;
            }
        }
    }

    /// <summary>
    /// A window drawn from a sheet of <see cref="BuildLayout"/>: it makes the controls by their original names and puts each where the sheet says. Buttons, check boxes and options only
    /// do what <see cref="BuildCanvas.On"/> was given; whatever has nothing behind it yet stays greyed out with the reason in its tooltip.
    /// </summary>
    public sealed class BuildCanvas
    {
        public readonly BuildSheet Sheet;
        public readonly Dictionary<string, VisualElement> Parts = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, string> _why = new Dictionary<string, string>();

        public BuildCanvas(string sheet) { Sheet = BuildLayout.Sheet(sheet); }

        public VisualElement Part(string name) => Parts.TryGetValue(name, out var element) ? element : null;
        public Button Button(string name) => Part(name) as Button;
        public Toggle Check(string name) => Part(name) as Toggle;
        public BuildSpinner Spinner(string name) => Part(name) as BuildSpinner;
        public DropdownField Combo(string name) => Part(name) as DropdownField;
        public TextField Text(string name) => Part(name) as TextField;
        public Label Words(string name) => Part(name) as Label;
        public Slider Slider(string name) => Part(name) as Slider;

        /// <summary>Greys a component out for good and says why when the mouse is over it.</summary>
        public void Unavailable(string name, string why) { _why[name] = why; var element = Part(name); if (element == null) return; element.tooltip = "Ainda não: " + why; element.SetEnabled(false); element.AddToClassList("bt-unavailable"); }
        public bool IsUnavailable(string name) => _why.ContainsKey(name);
        public string WhyNot(string name) => _why.TryGetValue(name, out var why) ? why : null;

        public void Enable(string name, bool on) { var element = Part(name); if (element != null) element.SetEnabled(on && !_why.ContainsKey(name)); }
        public void Visible(string name, bool on) { var element = Part(name); if (element != null) element.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; }
        public bool IsVisible(string name) { var element = Part(name); return element != null && element.style.display != DisplayStyle.None; }

        /// <summary>Puts an element of our own where a component of the sheet would go (or at a rectangle).</summary>
        public VisualElement Put(string name, VisualElement element, Rect area, VisualElement into)
        {
            element.name = name; element.AddToClassList("bt-part"); element.style.left = area.x; element.style.top = area.y; element.style.height = area.height;
            if (element is Toggle) element.style.maxWidth = area.width; else element.style.width = area.width;
            into.Add(element); Parts[name] = element; return element;
        }

        public Rect AreaOf(string name) { var slot = Sheet.Of(name); return BuildLayout.Place(slot, Sheet.Size.x); }

        /// <summary>The control a component of the sheet is drawn with, or null for the ones that only hold others.</summary>
        public VisualElement Make(BuildSlot slot, Func<BuildSlot, VisualElement> special = null)
        {
            var area = BuildLayout.Place(slot, Sheet.Size.x); var words = BuildTexts.Of(Sheet.Key, slot.Name); var element = special?.Invoke(slot);
            if (element != null) return element;
            switch (slot.Type)
            {
                case "text": { var label = new Label(words) { pickingMode = PickingMode.Ignore }; label.AddToClassList("bt-text"); if (slot.Name == "perm_modify" || slot.Name == "media_setting_note") label.AddToClassList("bt-wrap"); return label; }
                case "button": case "flyout_button": case "menu_button": { var button = new Button { text = words }; button.AddToClassList("lv-btn"); button.AddToClassList("bt-btn"); return button; }
                case "check_box": { var toggle = new Toggle { text = words }; toggle.AddToClassList("bt-check"); return toggle; }
                case "radio_item": { var radio = new Toggle { text = words }; radio.AddToClassList("bt-radio"); return radio; }
                case "spinner": return new BuildSpinner(words, slot.LabelWidth * BuildLayout.Scale, words.Length > 0 ? words : slot.Name);
                case "combo_box": { var combo = new DropdownField(new List<string> { "—" }, 0); combo.AddToClassList("bt-combo"); return combo; }
                case "line_editor": { var field = new TextField { isDelayed = true }; field.AddToClassList("bt-input"); return field; }
                case "filter_editor": { var field = new TextField(); field.AddToClassList("bt-input"); field.textEdition.placeholder = Sheet.Key == "tools" ? "Filtrar o conteúdo" : words; return field; }
                case "slider_bar": case "slider": { var slider = new Slider(0f, 1f); slider.AddToClassList("bt-slider"); return slider; }
                case "view_border": { var line = new VisualElement { pickingMode = PickingMode.Ignore }; line.AddToClassList("bt-line"); return line; }
                case "panel": if (slot.Name != "perms_build") return null; { var group = new VisualElement { pickingMode = PickingMode.Ignore }; group.AddToClassList("bt-group"); return group; }
                default: return null;
            }
        }
    }
}
