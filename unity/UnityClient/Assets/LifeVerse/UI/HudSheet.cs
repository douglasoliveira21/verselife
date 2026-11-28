using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// "Tudo da ilha": the sheet that "Mais" opens over the bar, with every menu of the game side by side and, under them, "Sua varanda": which buttons the bar shows, where it stands and
    /// the mode of the interface. A click on an entry closes the sheet and does it; Esc or a click outside only closes it.
    /// </summary>
    public sealed class HudSheet : VisualElement
    {
        private readonly Hud _hud; private readonly VisualElement _panel; private readonly ScrollView _scroll;
        public bool IsOpen => parent != null;

        private static readonly (string Value, string Words)[] Edges = { ("bottom", "Embaixo"), ("left", "À esquerda"), ("right", "À direita") };
        private static readonly (string Value, string Words)[] Modes = { ("full", "Completa"), ("compact", "Compacta"), ("immersive", "Imersiva (F10)") };

        public HudSheet(Hud hud)
        {
            _hud = hud; name = "menu-sheet"; AddToClassList("lv-veil"); pickingMode = PickingMode.Position; focusable = true;
            RegisterCallback<PointerDownEvent>(e => { if (e.target == this) { _hud.CloseSheet(); e.StopPropagation(); } });
            RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape) { _hud.CloseSheet(); e.StopPropagation(); } }, TrickleDown.TrickleDown);
            _panel = new VisualElement { name = "sheet" }; _panel.AddToClassList("lv-paper"); _panel.AddToClassList("lv-sheet"); Add(_panel);
            var head = Row("lv-sheet-head"); head.Add(UiKit.Text("Tudo da ilha", "lv-sheet-title")); var about = UiKit.Text("o menu completo, agrupado pelo que você quer fazer", "lv-muted"); about.style.flexGrow = 1; about.style.flexShrink = 1; about.style.overflow = Overflow.Hidden; head.Add(about); head.Add(UiKit.Text("Esc fecha", "lv-muted"));
            var close = UiKit.Button("", () => _hud.CloseSheet()); close.name = "sheet-close"; close.tooltip = "Fechar"; close.AddToClassList("lv-round"); close.style.marginLeft = 10; close.Add(new VarandaIcon("close", 13)); head.Add(close); _panel.Add(head);
            // the menus scroll when the screen is low; the choices of the bar stay in sight under them
            _scroll = new ScrollView(); _scroll.style.flexShrink = 1; _scroll.style.minHeight = 0; _scroll.style.overflow = Overflow.Hidden; _panel.Add(_scroll);
            _own = Row("lv-own"); _panel.Add(_own);
        }

        private readonly VisualElement _own;

        private static VisualElement Row(string kind) { var row = new VisualElement(); row.AddToClassList(kind); return row; }

        public void Open()
        {
            Fill(); if (parent == null) _hud.Ui.WindowLayer.Add(this); BringToFront(); Focus();
        }

        public void Close() => RemoveFromHierarchy();

        /// <summary>Draws the sheet again from the menus and the bar as they are now.</summary>
        public void Fill()
        {
            _scroll.Clear(); var columns = Row("lv-columns"); _scroll.Add(columns); VisualElement column = null;
            foreach (var menu in _hud.Menus)
            {
                var under = menu.Title == "Desenvolver" && column != null;
                if (!under) { column = Row("lv-column"); columns.Add(column); if (menu.Items.Count(i => i.Label != Hud.MenuLine) > 11) column.AddToClassList("lv-column-double"); }
                var heading = menu.Heading; heading.RemoveFromHierarchy(); heading.RemoveFromClassList("lv-btn-flat"); heading.AddToClassList("lv-sheet-heading"); heading.EnableInClassList("lv-sheet-heading-second", under); heading.focusable = false; heading.pickingMode = PickingMode.Ignore; column.Add(heading);
                var items = Row("lv-column-items"); column.Add(items);
                foreach (var (label, _, run) in menu.Items)
                {
                    if (label == Hud.MenuLine) { var line = Row("lv-sheet-rule"); line.pickingMode = PickingMode.Ignore; items.Add(line); continue; }
                    items.Add(Item(label, run));
                }
            }
            var own = _own; own.Clear();
            var title = Row("lv-own-row"); title.Add(UiKit.Text("Sua varanda", "lv-heading")); var hint = UiKit.Text("toque para mostrar ou guardar um botão; na barra, arraste para mudar a ordem", "lv-small", "lv-muted"); hint.style.marginLeft = 14; title.Add(hint); own.Add(title);
            var chips = Row("lv-own-row"); chips.name = "sheet-chips"; own.Add(chips);
            foreach (var id in _hud.ToolbarDefaults)
            {
                if (id == Hud.FixedTool) continue; var button = _hud.BottomBar.Q<Button>("btn-" + id); if (button == null) continue;
                var key = id; var shown = button.style.display != DisplayStyle.None;
                var chip = UiKit.WithIcon(UiKit.Button(_hud.NameOf(id), () => { _hud.ToolShownAsked?.Invoke(key, !shown); Fill(); }), "dots", 16); chip.name = "chip-" + id; chip.AddToClassList("lv-chip"); chip.EnableInClassList("lv-btn-selected", shown);
                if (VarandaIcons.Has(id)) ((UiKit.ButtonIcon)chip.userData).Art = id; chip.tooltip = shown ? "Na barra: toque para guardar" : "Guardado: toque para pôr na barra"; chips.Add(chip);
            }
            var options = Row("lv-own-row"); own.Add(options);
            Segments(options, "Onde fica", "sheet-edge", Edges, _hud.ToolbarEdge, value => _hud.EdgeAsked?.Invoke(value));
            Segments(options, "Interface", "sheet-mode", Modes, _hud.Mode, value => { _hud.ModeAsked?.Invoke(value); if (value == "immersive") _hud.CloseSheet(); });
        }

        private Button Item(string label, Action run)
        {
            // "Construir   B": the words, and the key of the keyboard at the end of the line
            var parts = label.Split(new[] { "   " }, StringSplitOptions.RemoveEmptyEntries); var words = parts.Length > 0 ? parts[0].Trim() : label;
            var button = UiKit.Button("", () => { _hud.CloseSheet(); run?.Invoke(); }); button.name = "item-" + words; button.AddToClassList("lv-sheet-item");
            button.Add(UiKit.Text(words, "lv-sheet-word")); if (parts.Length > 1) button.Add(UiKit.Text(parts[parts.Length - 1].Trim(), "lv-sheet-key"));
            return button;
        }

        private void Segments(VisualElement parent, string title, string name, (string Value, string Words)[] choices, string chosen, Action<string> pick)
        {
            parent.Add(UiKit.Text(title, "lv-bold")); var row = Row("lv-segments"); row.name = name; parent.Add(row);
            foreach (var (value, words) in choices) { var key = value; var button = UiKit.Button(words, () => { pick(key); Fill(); }); button.name = name + "-" + value; button.EnableInClassList("lv-btn-selected", value == chosen); row.Add(button); }
        }
    }
}
