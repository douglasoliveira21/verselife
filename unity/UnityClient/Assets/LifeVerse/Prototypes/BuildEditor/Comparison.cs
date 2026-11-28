using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Prototypes.BuildEditor
{
    /// <summary>
    /// One state side by side: on the left the original window rebuilt from the coordinates of its XML files (plain grey boxes and the original English words — it is NOT a capture of the
    /// viewer), on the right the prototype; the same numbers mark the same components, and the list says where each is and why anything differs.
    /// </summary>
    public static class Comparison
    {
        private static readonly Color Back = new Color(.11f, .11f, .12f), Frame = new Color(.27f, .27f, .28f), Bar = new Color(.19f, .19f, .2f), Slot = new Color(.16f, .16f, .17f), Edge = new Color(.52f, .52f, .54f), Ink = new Color(.9f, .9f, .9f), Faint = new Color(.64f, .64f, .66f);

        public static VisualElement Build(Dictionary<string, MapWindow> maps, Scenario s, System.Func<MapWindow, string> titleOf)
        {
            var stage = new VisualElement(); stage.style.position = Position.Absolute; stage.style.left = stage.style.top = stage.style.right = stage.style.bottom = 0; stage.style.backgroundColor = Back;
            var map = maps[s.Extra ?? "tools"];
            var fit = Mathf.Min(1f, 930f / map.Size.y, 560f / map.Size.x);
            var leftX = 20f; var rightX = leftX + map.Size.x * fit + 24f;
            Label Head(string text, float x, float y, float width, int size, Color colour, bool bold = false)
            {
                var label = new Label(text); label.style.position = Position.Absolute; label.style.left = x; label.style.top = y; label.style.width = width; label.style.fontSize = size; label.style.color = colour; label.style.whiteSpace = WhiteSpace.Normal; label.style.marginLeft = label.style.marginTop = 0; label.style.paddingLeft = label.style.paddingTop = 0; if (bold) label.AddToClassList("lv-bold"); stage.Add(label); return label;
            }
            Head($"{s.Number:00} · {s.Title}", 20, 12, 1500, 24, Ink, true);
            Head("Original (reconstrução pelos XML, não é captura)", leftX, 52, map.Size.x * fit + 10, 13, Faint);
            Head("Protótipo LifeVerse", rightX, 52, map.Size.x * fit, 13, Faint);

            var original = Rebuilt(map, s); var prototype = Controls.Window(map, s, titleOf(map), map.Size.y);
            foreach (var (element, x) in new[] { (original, leftX), (prototype, rightX) })
            {
                element.style.position = Position.Absolute; element.style.left = x; element.style.top = 92; element.style.transformOrigin = new TransformOrigin(0, 0); element.style.scale = new Scale(new Vector3(fit, fit, 1f)); stage.Add(element);
            }

            var listX = rightX + map.Size.x * fit + 28f; var width = 1920f - listX - 20f; var y = 92f;
            var number = 0;
            foreach (var (name, note) in s.Compare)
            {
                var w = map.Find(name) ?? maps["tools"].Find(name); if (w == null || w.Window != map.Key) continue;
                number++;
                var shown = Controls.Place(w, map.Size.x);
                foreach (var x in new[] { leftX, rightX }) { var marker = new Label(number.ToString()); marker.AddToClassList("bp-marker"); var r = x == leftX ? w.Lv : shown; marker.style.left = x + r.x * fit - 19f; marker.style.top = 92 + r.y * fit - 9f; stage.Add(marker); }
                var o = w.Original; var grown = Mathf.Abs(shown.height - w.Lv.height) >= 2f ? $" Altura {w.Lv.height:0} → {shown.height:0} px para leitura." : "";
                var narrowed = Mathf.Abs(shown.width - w.Lv.width) >= 2f ? $" Largura {w.Lv.width:0} → {shown.width:0} px (a original passa da borda)." : "";
                Head($"{number}.  {w.Function}", listX, y, width, 15, Ink, true); y += 21f;
                Head($"«{name}» · original x {o.x:0}, y {o.y:0}, {o.width:0} × {o.height:0} → LifeVerse x {shown.x:0}, y {shown.y:0}, {shown.width:0} × {shown.height:0}." + grown + narrowed + (note.Length > 0 ? " " + char.ToUpper(note[0]) + note.Substring(1) + "." : ""), listX + 22, y, width - 22, 13, Faint);
                y += width > 800 ? 40f : 56f;
            }
            Head($"Escala {Map.Scale:0.00} sobre a janela original ({map.OriginalSize.x:0} × {map.OriginalSize.y:0} → {map.Size.x:0} × {map.Size.y:0}). Em todos: cores, letras e cantos do LifeVerse; textos em português; ícones do jogo. Os números azuis marcam o mesmo componente nos dois lados. O que o visualizador desenha por código (fora dos XML) não aparece à esquerda." + (fit < .999f ? $" Aqui as duas janelas aparecem a {fit * 100f:0}%." : ""), listX, Mathf.Max(y + 16f, 940f), width, 13, Faint);
            return stage;
        }

        private static VisualElement Rebuilt(MapWindow map, Scenario s)
        {
            VisualElement Box(float x, float y, float w, float h, Color? back, Color? border = null, float radius = 2f)
            {
                var box = new VisualElement(); box.style.position = Position.Absolute; box.style.left = x; box.style.top = y; box.style.width = w; box.style.height = h;
                if (back.HasValue) box.style.backgroundColor = back.Value;
                if (border.HasValue) { box.style.borderLeftWidth = box.style.borderRightWidth = box.style.borderTopWidth = box.style.borderBottomWidth = 1; box.style.borderLeftColor = box.style.borderRightColor = box.style.borderTopColor = box.style.borderBottomColor = border.Value; }
                box.style.borderTopLeftRadius = box.style.borderTopRightRadius = box.style.borderBottomLeftRadius = box.style.borderBottomRightRadius = radius; box.style.overflow = Overflow.Hidden; return box;
            }
            void Write(VisualElement into, string text, float left = 2f, Color? colour = null)
            {
                var label = new Label(text); label.AddToClassList("bp-text"); label.style.position = Position.Absolute; label.style.left = left; label.style.top = 0; label.style.bottom = 0; label.style.right = 0; label.style.color = colour ?? Ink; label.style.fontSize = 12; into.Add(label);
            }
            var frame = Box(0, 0, map.Size.x, map.Size.y, Frame, Edge, 4f); if (map.Key != "tools") { var bar = Box(0, 0, map.Size.x, 36, Bar); Write(bar, map.Key.ToUpperInvariant().Replace('-', ' '), 12); frame.Add(bar); }
            foreach (var w in map.Widgets)
            {
                if (!s.Visible(w) || map.Key != "tools" && !w.VisibleAtLoad || map.Key == "media" && (w.Id.Contains("panel_media_settings_security") || w.Id.Contains("panel_media_settings_permissions"))) continue;
                if (w.Type == "panel" || w.Type == "radio_group" || w.Type == "view" || w.Type == "icon" || w.Type == "loading_indicator" || w.Name == "title_media") continue;
                var r = w.Lv; var words = (w.Label ?? "").Replace("[DIMENSIONS]", "256 x 256").Replace("[PRICE]", "1200").Replace("[AREA]", "512");
                if (r.xMax > map.Size.x - 2f) r.width = map.Size.x - 2f - r.x;
                var dim = s.Disabled(w) ? .45f : 1f; VisualElement e;
                if (w.Type == "check_box" && r.height < 19f) { r.y = r.yMax - 19f; r.height = 19f; }
                switch (w.Type)
                {
                    case "text": e = Box(r.x, r.y, r.width, Mathf.Max(r.height, 17f), null); if (w.Name == "text status") words = Status(s); if (w.Name == "selection_count") words = "1 objects selected, land impact 1"; if (w.Name == "Creator Name" || w.Name == "Owner Name") words = "Resident Name"; Write(e, words, 0); break;
                    case "check_box": e = Box(r.x, r.y, r.width, r.height, null); var tick = Box(3, r.height - 19f, 15, 15, s.Checked.Contains(w.Name) ? Edge : Slot, Edge); e.Add(tick); var caption = Box(24, r.height - 21f, r.width - 24f, 19, null); Write(caption, words, 0); e.Add(caption); break;
                    case "radio_item": e = Box(r.x, r.center.y - 9f, r.width, 18f, null); e.Add(Box(3, 2, 14, 14, s.Checked.Contains(w.Name) ? Edge : Slot, Edge, 7f)); Write(e, words, 23); break;
                    case "spinner": e = Box(r.x, r.y, r.width, r.height, null); var lw = words.Length == 0 ? 0f : w.LabelWidth * Map.Scale; if (lw > 0f) { var l = Box(0, 0, lw, r.height, null); Write(l, words, 0); e.Add(l); } e.Add(Box(lw, 3, 12, r.height - 6f, Edge)); var field = Box(lw + 15f, 1, r.width - lw - 15f, r.height - 2f, Slot, Edge); Write(field, s.HasSelection || w.Tab == null ? s.Value(w) : "", 3); e.Add(field); break;
                    case "combo_box": e = Box(r.x, r.y, r.width, r.height, Slot, Edge, 4f); Write(e, English(w.Name, s), 4); var caret = Box(r.width - 17f, 0, 15, r.height, null); Write(caret, "▾", 2); e.Add(caret); break;
                    case "line_editor": case "filter_editor": case "name_box": e = Box(r.x, r.y, r.width, r.height, Slot, Edge); Write(e, w.Type == "filter_editor" ? (w.Label ?? "") : s.HasSelection || map.Key != "tools" ? s.Value(w) : "", 4); break;
                    case "button": case "flyout_button": case "menu_button":
                        // the viewer's mode and shape buttons carry pictures only: their names are written so that they can be told apart
                        var mode = System.Array.IndexOf(new[] { "button focus", "button move", "button edit", "button create", "button land" }, w.Name); if (mode >= 0) words = new[] { "Focus", "Move", "Edit", "Create", "Land" }[mode];
                        if (w.Name.StartsWith("Tool", System.StringComparison.Ordinal)) words = w.Name.Substring(4, 2);
                        if (w.Name == "Options...") words = "…";
                        e = Box(r.x, r.y, r.width, r.height, IsOn(w.Name, s) || s.Checked.Contains(w.Name) ? Edge : Bar, Edge, 5f); Write(e, words, 0); e.Q<Label>()?.AddToClassList("bp-status"); break;
                    case "tab_container": e = Box(r.x, r.y, r.width, 36, Bar); var media = map.Key == "media"; var names = media ? new[] { "General", "Security", "Permissions" } : new[] { "General", "Object", "Features", "Texture", "Content" }; var shown = media ? new[] { s.Tab, "", "" } : new[] { "Geral", "Objeto", "Recursos", "Textura", "Conteúdo" }; for (var i = 0; i < names.Length; i++) { var tab = Box(i * r.width / names.Length + 1, 3, r.width / names.Length - 2, 33, shown[i] == s.Tab ? Frame : Slot, Edge, 4f); Write(tab, names[i], 12); e.Add(tab); } break;
                    case "slider_bar": case "slider": e = Box(r.x, r.y, r.width, r.height, null); e.Add(Box(0, r.height / 2f - 2f, r.width, 4, Slot, Edge)); e.Add(Box(r.width * .4f, r.height / 2f - 8f, 14, 16, Edge, null, 7f)); break;
                    case "view_border": e = Box(r.x, r.y, Mathf.Max(r.width, 1f), r.width < 1f ? Mathf.Min(r.height, map.Size.y - r.y - 12f) : 1f, Edge); break;
                    default: e = Box(r.x, r.y, r.width, r.height, Slot, Edge); if (words.Length > 0) Write(e, words, 4); break;
                }
                e.style.opacity = dim; frame.Add(e);
            }
            return frame;
        }

        /// <summary>The instruction line of the viewer for this state (the strings of floater_tools.xml).</summary>
        private static string Status(Scenario s) => s.Mode == "Foco" ? "Click and drag to move camera" : s.Mode == "Mover" ? "Drag to move, Ctrl to lift, Ctrl+Shift to rotate" : s.Mode == "Criar" ? "Click inworld to build" : s.Mode == "Terreno" ? "Click and hold to modify land" : s.Tool == "rotate" ? "Drag colored bands to rotate object" : s.Tool == "stretch" ? "Click and drag to stretch selected side" : "Drag to move, shift-drag to copy";

        /// <summary>The choice a list of the original would show in this state.</summary>
        private static string English(string name, Scenario s)
        {
            switch (name)
            {
                case "combobox grid mode": return "World"; case "comboBaseType": return s.Selected == Subject.Ring ? "Torus" : "Box"; case "hole": return "Default"; case "clickaction": return "Touch  (default)"; case "sale type": return "Copy";
                case "material": return "Wood"; case "Physics Shape Type Combo Ctrl": return "Prim"; case "Probe Volume Type": return "Sphere"; case "combobox matmedia": return s.Surface == "pbr" ? "PBR Metallic Roughness" : s.Surface == "media" ? "Media" : "Textures";
                case "combobox alphamode": return "Alpha blending"; case "combobox texgen": return "Default"; case "mode_selection": return "Inventory"; default: return "";
            }
        }

        private static bool IsOn(string name, Scenario s) => name == "button focus" && s.Mode == "Foco" || name == "button move" && s.Mode == "Mover" || name == "button edit" && s.Mode == "Editar" || name == "button create" && s.Mode == "Criar" || name == "button land" && s.Mode == "Terreno";
    }
}
