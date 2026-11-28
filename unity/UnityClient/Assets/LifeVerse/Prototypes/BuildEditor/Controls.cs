using System.Collections.Generic;
using System.Linq;
using LifeVerse.Unity.UI;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Prototypes.BuildEditor
{
    /// <summary>
    /// Draws one component of the map with the LifeVerse look at the place the map gives it. Heights that would be too small to read are grown around the original's visual centre
    /// (the viewer draws check boxes at the bottom of their rectangle, so those grow upwards). Nothing here does anything when pressed: it is a prototype.
    /// </summary>
    public static class Controls
    {
        private static readonly string[] ModeButtons = { "button focus", "button move", "button edit", "button create", "button land" };
        private static readonly string[] ModeIcons = { "search", "hand", "move-all", "cube", "mountain" };
        private static readonly string[] ModeNames = { "Foco", "Mover", "Editar", "Criar", "Terreno" };

        /// <summary>The rectangle the component takes on screen, relative to the window.</summary>
        public static Rect Place(Widget w, float windowWidth)
        {
            var r = w.Lv;
            switch (w.Type)
            {
                case "text": { var h = Mathf.Max(r.height, 19f); if (w.Name == "perm_modify" || w.Name == "media_setting_note") h = Mathf.Max(r.height, 38f); else r.y = r.center.y - h / 2f; r.height = h; break; }
                case "check_box": r.y = r.yMax - 22f; r.height = 22f; break;
                case "radio_item": r.y = r.center.y - 10.5f; r.height = 21f; break;
                case "spinner": { var h = Mathf.Clamp(r.height, 23f, 28f); r.y = r.center.y - h / 2f; r.height = h; break; }
                case "combo_box": { if (w.Name == "combobox texgen") { r.y -= 2f; r.height = 25f; break; } var h = Mathf.Clamp(r.height, 27f, 30f); r.y = r.center.y - h / 2f; r.height = h; break; }   // the next row is closer than a tall list
                // the viewer's pickers keep the lower part of their rectangle for a caption, where the next component may sit: the picture is the square above it
                case "color_swatch": if (r.height > r.width) r.height = r.width; break;
                case "texture_picker": if (w.Name == "texture control" || w.Name == "light texture control") r.height = Mathf.Min(r.height, r.width - 3f); break;
                case "line_editor": case "filter_editor": case "name_box": { var h = Mathf.Max(r.height, 26f); r.y = r.center.y - h / 2f; r.height = h; break; }
                case "slider_bar": case "slider": r.height = Mathf.Max(r.height, 20f); break;
                case "avatar_icon": r.y += 7f; break;   // beside the name, whose line is centred in a taller rectangle
            }
            if (r.xMax > windowWidth - 6f) r.width = Mathf.Max(10f, windowWidth - 6f - r.x);
            return r;
        }

        public static VisualElement Build(Widget w, Scenario s, MapWindow window)
        {
            if (w.Type == "panel" && w.Name != "perms_build" || w.Type == "radio_group" || w.Type == "view" || w.Type == "icon" || w.Type == "loading_indicator" || w.Type == "web_browser" && w.Name == "title_media") return null;
            var rect = Place(w, window.Size.x); var text = w.Name == "text status" ? s.Values["status"] : Texts.Of(w); VisualElement element; var control = true;
            switch (w.Type)
            {
                case "panel": element = Box("bp-group"); control = false; break;
                case "view_border":
                    if (w.Lv.width < 1f) { rect = new Rect(w.Lv.x, w.Lv.y, 1f, Mathf.Min(w.Lv.height, window.Size.y - w.Lv.y - 12f)); } else rect.height = 1f;
                    element = Box("bp-line"); control = false; break;
                case "text": element = Words(w, s, text, ref control); break;
                case "button": case "flyout_button": case "menu_button": element = Press(w, s, text, rect); break;
                case "check_box": element = Check(text, s.Checked.Contains(w.Name) && (w.Tab == null || s.HasSelection), s.Mixed.Contains(w.Name)); break;
                case "radio_item": element = Radio(text, s.Checked.Contains(w.Name)); break;
                case "spinner": element = Spinner(w, text, s.HasSelection || w.Window != "tools" || w.Tab == null ? s.Value(w) : "", s.Mixed.Count > 0 && w.Name == "Edit Cost"); break;
                case "combo_box": element = Combo(s.HasSelection || w.Tab == null ? s.Value(w) : ""); break;
                case "line_editor": element = Field(s.HasSelection || w.Window != "tools" ? s.Value(w) : "", null); break;
                case "filter_editor": element = Field(w.Window == "tools" ? "Filtrar o conteúdo" : "Buscar nas texturas", "search"); element.Q<Label>().AddToClassList("bp-soft"); break;
                case "name_box": element = Field(s.HasSelection ? s.Value(w) : "", "people"); break;
                case "avatar_icon": element = Face(w.Name == "Creator Icon" ? "A" : "V"); control = false; break;
                case "color_swatch": element = Swatch(w); break;
                case "texture_picker": element = Tile(w, s, text); break;
                case "slider_bar": case "slider": element = SliderBar(w.Name == "slider force" || w.Name == "GridOpacity" ? .7f : .35f, rect); break;
                case "tab_container": element = Tabs(w, s, rect); rect.height = 36f; break;
                case "panel_inventory_object": element = Tree(s); break;
                case "asset_filtered_inv_panel": element = Textures(); break;
                case "scroll_list": element = Box("bp-list"); element.Add(Row("dot", "exemplo.org", false)); break;
                case "web_browser": element = Box("bp-list"); element.style.alignItems = Align.Center; element.style.justifyContent = Justify.Center; element.Add(new Icon("play", 28)); break;
                default: return null;
            }
            element.name = w.Id; element.AddToClassList("bp-w"); if (control) element.AddToClassList("bp-control");
            element.style.left = rect.x; element.style.top = rect.y; element.style.height = rect.height;
            // a check box or option is only as wide as its words (the original rectangles run over their neighbours)
            if (w.Type == "check_box" || w.Type == "radio_item") element.style.maxWidth = rect.width; else element.style.width = rect.width;
            if (w.Type != "view_border" && w.Type != "panel" && s.Disabled(w)) element.AddToClassList("bp-off");
            return element;
        }

        private static VisualElement Box(string kind) { var box = new VisualElement(); box.AddToClassList(kind); return box; }

        public static Label Line(string text, params string[] classes) { var label = new Label(text) { pickingMode = PickingMode.Ignore }; label.AddToClassList("bp-text"); foreach (var c in classes) label.AddToClassList(c); return label; }

        private static VisualElement Words(Widget w, Scenario s, string text, ref bool control)
        {
            if (w.Name == "Creator Name" || w.Name == "Owner Name") text = s.HasSelection ? s.Value(w) : "";
            if (w.Name == "media_info" || w.Name == "current_url") text = s.Value(w);
            if (w.Name == "selection_count") text = s.Values.TryGetValue(w.Name, out var count) ? count : s.Value(w);
            if (w.Name == "selection_faces") text = s.Values.TryGetValue(w.Name, out var faces) ? faces : "";
            var label = Line(text); if (w.Name == "perm_modify" || w.Name == "media_setting_note") label.AddToClassList("bp-text-wrap");
            if (w.Name == "text status") label.AddToClassList("bp-status");
            if (w.Name.StartsWith("label_parcel", System.StringComparison.Ordinal) || w.Name == "edit_object") label.AddToClassList("bp-title");
            if (w.Name == "selection_empty" || w.Name == "media_setting_note") label.AddToClassList("bp-soft");
            control = text.Length > 0; return label;
        }

        private static VisualElement Press(Widget w, Scenario s, string text, Rect rect)
        {
            var button = new Button(); button.AddToClassList("lv-btn"); button.AddToClassList("bp-btn");
            var mode = System.Array.IndexOf(ModeButtons, w.Name);
            if (mode >= 0) { button.AddToClassList("bp-icon-btn"); button.AddToClassList("bp-mode"); button.tooltip = ModeNames[mode]; button.Add(new Icon(ModeIcons[mode], 22)); if (s.Mode == ModeNames[mode]) button.AddToClassList("lv-btn-selected"); return button; }
            if (w.Name.StartsWith("Tool", System.StringComparison.Ordinal))
            {
                button.AddToClassList("bp-icon-btn"); button.AddToClassList("bp-shape"); button.Add(new ShapeIcon(w.Name.Substring(4), rect.width - 8f)); if (s.Checked.Contains(w.Name)) button.AddToClassList("lv-btn-selected"); Why(button, w, rect); return button;
            }
            if (w.Type == "menu_button") { button.AddToClassList("bp-icon-btn"); button.AddToClassList("lv-btn-flat"); button.Add(new Icon("copy", 13)); button.tooltip = "Copiar e colar"; return button; }
            if (w.Name == "Options...") { button.AddToClassList("bp-icon-btn"); button.Add(new Icon("settings", 15)); return button; }
            if (w.Name == "button set group" || w.Name == "color_pipette" || w.Name == "Pipette") { button.AddToClassList("bp-icon-btn"); button.Add(new Icon(w.Name == "button set group" ? "people" : "target", 15)); return button; }
            button.text = w.Type == "flyout_button" ? text + "  ▾" : text;
            if (w.Name == "link_btn" || w.Name == "select_btn" || w.Name == "Select" || w.Name == "OK") button.AddToClassList("lv-btn-primary");
            Why(button, w, rect); return button;
        }

        private static void Why(VisualElement element, Widget w, Rect rect)
        {
            if (!Scenario.Unavailable.TryGetValue(w.Name, out var reason)) return;
            var dot = Box("bp-why"); dot.tooltip = "Indisponível: " + reason; dot.style.right = -1; dot.style.top = -1; element.Add(dot);
        }

        private static VisualElement Check(string text, bool on, bool mixed)
        {
            var row = Box("bp-row"); var box = Box("bp-check-box"); if (mixed) { box.AddToClassList("bp-check-mixed"); box.Add(Box("bp-mixed-mark")); } else if (on) box.AddToClassList("bp-check-on");
            row.Add(box); var label = Line(text); label.style.flexShrink = 1; row.Add(label); return row;
        }

        private static VisualElement Radio(string text, bool on)
        {
            var row = Box("bp-row"); var ring = Box("bp-radio"); if (on) { ring.AddToClassList("bp-radio-on"); ring.Add(Box("bp-radio-dot")); }
            row.Add(ring); var label = Line(text); label.style.flexShrink = 1; row.Add(label); return row;
        }

        private static VisualElement Spinner(Widget w, string text, string value, bool mixed)
        {
            var row = Box("bp-row"); var labelWidth = text.Length == 0 ? 0f : w.Name == "Edit Cost" ? 30f : w.LabelWidth * Map.Scale;   // "VC$" is wider than "L$"
            if (labelWidth > 0f)
            {
                var label = Line(text); label.style.width = labelWidth; label.style.flexShrink = 0; row.Add(label);
                if (text == "X" && w.Name.Contains(" ")) label.style.color = new Color(.71f, .28f, .18f); else if (text == "Y" && w.Name.Contains(" ")) label.style.color = new Color(.08f, .42f, .39f); else if (text == "Z") label.style.color = new Color(.18f, .42f, .66f);
                if (w.Name.StartsWith("Taper", System.StringComparison.Ordinal) || w.Name.StartsWith("Shear", System.StringComparison.Ordinal)) label.style.color = StyleKeyword.Null;
            }
            var arrows = Box("bp-arrows"); foreach (var name in new[] { "caret-up", "caret-down" }) { var arrow = Box("bp-arrow"); arrow.Add(new Icon(name, 9)); arrows.Add(arrow); } row.Add(arrows);
            var field = Box("bp-field"); field.style.flexGrow = 1; field.style.flexShrink = 1; field.style.alignSelf = Align.Stretch; field.Add(Line(mixed ? "—" : value)); row.Add(field); return row;
        }

        private static VisualElement Combo(string value) { var box = Box("bp-combo"); box.Add(Line(value)); box.Add(Box("bp-caret")); return box; }

        private static VisualElement Field(string value, string icon)
        {
            var box = Box("bp-field"); if (icon != null) { var mark = new Icon(icon, 14); mark.style.marginRight = 5; box.Add(mark); } var label = Line(value); label.style.flexShrink = 1; label.style.textOverflow = TextOverflow.Ellipsis; box.Add(label); return box;
        }

        private static VisualElement Face(string initial)
        {
            var face = new VisualElement(); face.style.borderTopLeftRadius = face.style.borderTopRightRadius = face.style.borderBottomLeftRadius = face.style.borderBottomRightRadius = 14; face.style.backgroundColor = initial == "A" ? new Color(.08f, .42f, .39f) : new Color(.18f, .35f, .62f);
            var label = new Label(initial); label.AddToClassList("lv-face-initial"); label.style.flexGrow = 1; face.Add(label); return face;
        }

        private static VisualElement Swatch(Widget w)
        {
            var box = Box("bp-swatch"); box.style.backgroundColor = w.Name == "shinycolorswatch" ? new Color(1f, 1f, 1f) : w.Lv.height > 60f ? new Color(1f, .88f, .66f) : new Color(.97f, .94f, .88f); return box;
        }

        private static VisualElement Tile(Widget w, Scenario s, string text)
        {
            var box = Box("bp-tile"); var picture = Box("bp-tile-picture");
            picture.style.backgroundImage = new StyleBackground(BuiltInTextures.Get(w.Name == "light texture control" ? "checker" : w.Name == "pbr_control" ? "tiles" : "planks"));
            box.Add(picture); if (text.Length > 0 && w.Name == "pbr_control") { var caption = Line(text); caption.style.height = 18; box.Add(caption); } return box;
        }

        private static VisualElement SliderBar(float at, Rect rect)
        {
            var box = new VisualElement(); box.style.justifyContent = Justify.Center; box.Add(Box("bp-slider-track")); var knob = Box("bp-slider-knob"); knob.style.left = at * (rect.width - 16f); knob.style.top = (rect.height - 16f) / 2f; box.Add(knob); return box;
        }

        private static VisualElement Tabs(Widget w, Scenario s, Rect rect)
        {
            var bar = Box("bp-row"); var names = new[] { "Geral", "Objeto", "Recursos", "Textura", "Conteúdo" };
            for (var i = 0; i < names.Length; i++)
            {
                var tab = new Button { text = names[i], name = "tab-" + names[i] }; tab.AddToClassList("lv-btn"); tab.AddToClassList("bp-tab"); tab.style.flexGrow = 1; tab.style.flexBasis = 0; tab.style.height = 33; tab.style.marginLeft = i == 0 ? 0 : 2;
                if (names[i] == s.Tab) tab.AddToClassList("lv-btn-selected"); if (!s.HasSelection && i >= 2) tab.AddToClassList("bp-off"); bar.Add(tab);
            }
            var line = Box("bp-tab-line"); line.style.position = Position.Absolute; line.style.left = 0; line.style.right = 0; line.style.bottom = 0; bar.Add(line); return bar;
        }

        private static VisualElement Row(string icon, string text, bool on, float indent = 0f)
        {
            var row = Box("bp-list-row"); if (on) row.AddToClassList("bp-list-row-on"); row.style.paddingLeft = 6f + indent; var mark = new Icon(icon, 14); mark.style.marginRight = 7; row.Add(mark); row.Add(Line(text)); return row;
        }

        private static VisualElement Tree(Scenario s)
        {
            var list = Box("bp-list"); if (!s.HasSelection) return list;
            list.Add(Row("folder-open", "Conteúdo de «Caixote do deck»", false));
            list.Add(Row("code", "sentar.lua — rodando", true, 18f)); list.Add(Row("code", "boas-vindas.lua — parado", false, 18f)); list.Add(Row("image", "madeira-clara.png", false, 18f));
            list.Add(Row("volume", "rangido.ogg", false, 18f)); list.Add(Row("run", "sentar-de-lado.glb", false, 18f)); list.Add(Row("cube", "Almofada (construção)", false, 18f)); list.Add(Row("pin", "Marco: Praça do Deck", false, 18f));
            return list;
        }

        private static VisualElement Textures()
        {
            var list = Box("bp-list"); list.Add(Row("folder-open", "Do sistema", false));
            foreach (var (id, label) in BuiltInTextures.All.Take(5))
            {
                var row = Box("bp-list-row"); row.style.paddingLeft = 20; if (id == "planks") row.AddToClassList("bp-list-row-on"); var thumb = new VisualElement(); thumb.style.width = 20; thumb.style.height = 20; thumb.style.marginRight = 7; thumb.style.backgroundImage = new StyleBackground(BuiltInTextures.Get(id)); row.Add(thumb); row.Add(Line(label)); list.Add(row);
            }
            list.Add(Row("folder", "Meu inventário", false)); return list;
        }

        /// <summary>Where the part that scrolls begins (under the tabs): everything above stays still when the screen is short.</summary>
        public const float ScrollTop = 296f;

        /// <summary>One window of the map: the frame of the LifeVerse windows and every component the scenario shows. A <paramref name="height"/> under the map's makes the part under the tabs scroll.</summary>
        public static VisualElement Window(MapWindow map, Scenario s, string title, float height)
        {
            var frame = new VisualElement { name = "window-" + map.Key }; frame.AddToClassList("lv-window"); frame.AddToClassList("lv-window-front"); frame.style.width = map.Size.x; frame.style.height = height;
            var shadow = new VisualElement(); shadow.AddToClassList("lv-window-shadow"); frame.Add(shadow);
            var bar = new VisualElement(); bar.AddToClassList("lv-window-title"); bar.style.height = 36; bar.style.position = Position.Absolute; bar.style.left = 0; bar.style.right = 0; bar.style.top = 0;
            // the build window has no title strip in the viewer: the five modes sit at the very top, beside the window buttons
            if (map.Key == "tools") { bar.style.backgroundColor = Color.clear; bar.style.borderBottomWidth = 0; bar.style.justifyContent = Justify.FlexEnd; bar.style.alignItems = Align.FlexStart; bar.style.paddingTop = 5; }
            else { bar.Add(new Icon("hammer", 17)); var heading = new Label(title); heading.AddToClassList("lv-title-text"); heading.style.marginLeft = 8; bar.Add(heading); }
            foreach (var icon in new[] { "minimize", "close" }) { var button = new Button(); button.AddToClassList("lv-btn"); button.AddToClassList("lv-window-button"); button.Add(new Icon(icon, 13)); bar.Add(button); }
            frame.Add(bar);
            // the coordinates are measured from the outer corner: the 1 px border of the frame is taken back
            var body = new VisualElement { name = "body" }; body.style.position = Position.Absolute; body.style.left = -1; body.style.top = -1; body.style.width = map.Size.x; body.style.height = height; frame.Add(body);
            VisualElement under = null;
            if (height < map.Size.y - .5f)
            {
                var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "tab-scroll" }; scroll.AddToClassList("bp-scroll"); scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden; scroll.style.position = Position.Absolute; scroll.style.left = 0; scroll.style.top = ScrollTop; scroll.style.width = map.Size.x; scroll.style.height = height - ScrollTop - 8f; scroll.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
                under = new VisualElement(); under.style.height = map.Size.y - ScrollTop; under.style.flexShrink = 0; scroll.Add(under); body.Add(scroll);
            }
            foreach (var widget in map.Widgets)
            {
                if (!s.Visible(widget) || map.Key == "media" && (widget.Id.Contains("panel_media_settings_security") || widget.Id.Contains("panel_media_settings_permissions"))) continue;
                if (map.Key != "tools" && !widget.VisibleAtLoad) continue;
                if (map.Key == "media" && widget.Type == "tab_container") { var tabs = new VisualElement(); tabs.style.flexDirection = FlexDirection.Row; tabs.style.position = Position.Absolute; tabs.style.left = widget.Lv.x + 2; tabs.style.top = widget.Lv.y + 7; foreach (var (name, on) in new[] { ("Geral", true), ("Segurança", false), ("Permissões", false) }) { var tab = new Button { text = name }; tab.AddToClassList("lv-btn"); tab.AddToClassList("bp-tab"); if (on) tab.AddToClassList("lv-btn-selected"); tab.style.width = 130; tab.style.height = 26; tabs.Add(tab); } body.Add(tabs); continue; }
                var element = Build(widget, s, map); if (element == null) continue;
                if (under != null && element.style.top.value.value >= ScrollTop - 1f) { element.style.top = element.style.top.value.value - ScrollTop; under.Add(element); } else body.Add(element);
            }
            Painted(map, body);
            return frame;
        }

        /// <summary>What the viewer draws by code, not from the XML (its position is approximate): the picture preview of the texture picker and the colour field of the colour picker.</summary>
        private static void Painted(MapWindow map, VisualElement body)
        {
            VisualElement At(float x, float y, float width, float height, string kind) { var box = Box(kind); box.AddToClassList("bp-w"); box.name = "painted"; box.style.left = x; box.style.top = y; box.style.width = width; box.style.height = height; body.Add(box); return box; }
            if (map.Key == "texture-picker") { At(14, 48, 190, 190, "bp-tile-picture").style.backgroundImage = new StyleBackground(BuiltInTextures.Get("planks")); }
            if (map.Key == "color-picker")
            {
                var field = At(200, 48, 296, 208, "bp-swatch"); var gradient = new Texture2D(120, 84, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
                for (var x = 0; x < 120; x++) for (var y = 0; y < 84; y++) gradient.SetPixel(x, y, Color.HSVToRGB(x / 119f, y / 83f, .85f));
                gradient.Apply(); field.style.backgroundImage = new StyleBackground(gradient);
                var light = At(508, 48, 22, 208, "bp-swatch"); var ramp = new Texture2D(1, 64, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp }; for (var y = 0; y < 64; y++) ramp.SetPixel(0, y, Color.HSVToRGB(.03f, .74f, y / 63f)); ramp.Apply(); light.style.backgroundImage = new StyleBackground(ramp);
                At(14, 318, 96, 62, "bp-swatch").style.backgroundColor = new Color(.71f, .28f, .18f);   // the current colour, under its words
                for (var i = 0; i < 32; i++) At(14 + i % 16 * 38, 432 + i / 16 * 36, 32, 30, "bp-swatch").style.backgroundColor = i < 16 ? Color.HSVToRGB(i / 16f, .7f, .85f) : Color.HSVToRGB(0f, 0f, (i - 16) / 15f);   // the saved colours
            }
        }
    }
}
