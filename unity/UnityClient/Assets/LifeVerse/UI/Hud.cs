using System;
using System.Collections.Generic;
using System.Linq;
using LifeVerse.Unity.Avatar;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The interface while playing: a bar at the top (the name of the game, the island, the connection and how many people are near), the chat at the bottom left, and a bar at the
    /// bottom with the buttons that open the windows (chat, people, settings, help). <see cref="Register"/> adds a button for a window; the windows float over the game.
    /// </summary>
    public sealed class Hud
    {
        public readonly UiRoot Ui; public readonly ChatPanel Chat; public readonly NotificationCenter Notices;
        public readonly VisualElement TopBar, BottomBar; public readonly DropdownField Presence = new DropdownField(new System.Collections.Generic.List<string> { "online", "away", "busy", "invisible" }, 0); public readonly Minimap Map = new Minimap(); public readonly PerformanceOverlay Perf = new PerformanceOverlay(); public readonly RadialMenu Radial = new RadialMenu(); private readonly Label _connection, _people, _island; private readonly Dictionary<string, FloatingWindow> _windows = new Dictionary<string, FloatingWindow>();
        public bool Visible { get; private set; }

        public Hud(UiRoot ui, NotificationCenter notices)
        {
            Ui = ui; Notices = notices; Chat = new ChatPanel(ui.Settings);
            TopBar = new VisualElement(); TopBar.AddToClassList("lv-bar"); TopBar.name = "topbar"; TopBar.style.position = Position.Absolute; TopBar.style.top = 14; TopBar.style.left = 18; TopBar.style.right = 18; TopBar.pickingMode = PickingMode.Position;
            var logo = new Icon("cube", 28, new Color(.07f, .95f, .88f)) { name = "logo", tooltip = "LifeVerse" }; logo.style.marginRight = 6; TopBar.Add(logo); _menus = new VisualElement { name = "menus" }; _menus.style.flexDirection = FlexDirection.Row; TopBar.Add(_menus); IslandBack = UiKit.Button("‹", () => Travel?.Invoke(-1), "lv-btn-flat"); IslandBack.name = "island-back"; IslandBack.tooltip = "Voltar à ilha anterior"; IslandForward = UiKit.Button("›", () => Travel?.Invoke(1), "lv-btn-flat"); IslandForward.name = "island-forward"; IslandForward.tooltip = "Avançar para a próxima ilha"; IslandBack.style.marginLeft = 10; TopBar.Add(IslandBack); TopBar.Add(IslandForward); SetTravel(false, false); _island = UiKit.Text("", "lv-muted"); _island.style.marginLeft = 4; var pin = new Icon("pin", 18, new Color(.07f, .95f, .88f)); pin.style.marginLeft = 10; TopBar.Add(pin); TopBar.Add(_island); TopBar.Add(UiKit.Spacer());
            var coin = new Icon("coin", 20, new Color(.96f, .64f, .25f)) { name = "coin" }; coin.style.marginRight = 6; TopBar.Add(coin); _balance = UiKit.Text("", "lv-bold"); _balance.style.marginRight = 6; TopBar.Add(_balance);
            BuyCoins = UiKit.WithIcon(UiKit.Button("", () => BuyCoinsPressed?.Invoke(), "lv-btn-primary"), "plus", 16); BuyCoins.name = "buy-coins"; BuyCoins.tooltip = "Carteira: comprar VerseCoin"; BuyCoins.style.width = 32; BuyCoins.style.paddingLeft = 0; BuyCoins.style.marginRight = 18; ((UiKit.ButtonIcon)BuyCoins.userData).Only = true; TopBar.Add(BuyCoins);
            _people = UiKit.Text("0 pessoas", "lv-muted"); _people.style.marginRight = 18; TopBar.Add(_people); Presence.style.width = 110; Presence.style.marginRight = 10; TopBar.Add(Presence); _connection = UiKit.Text("● desconectado", "lv-bad"); TopBar.Add(_connection);
            BottomBar = new VisualElement(); BottomBar.AddToClassList("lv-bar"); BottomBar.name = "bottombar"; BottomBar.style.position = Position.Absolute; BottomBar.style.bottom = 14; BottomBar.style.left = 18; BottomBar.style.right = 18; BottomBar.style.justifyContent = Justify.Center; BottomBar.pickingMode = PickingMode.Position;
            Chat.style.position = Position.Absolute; Chat.style.left = 18; Chat.style.bottom = 76;
            Map.style.position = Position.Absolute; Map.style.left = 18; Map.style.top = 70; Perf.style.top = 270; Perf.style.display = DisplayStyle.None;
        }

        public void Show()
        {
            if (Visible) return; Visible = true; Ui.HudLayer.Add(TopBar); Ui.HudLayer.Add(Map); Ui.HudLayer.Add(Perf); Ui.HudLayer.Add(Chat); Ui.HudLayer.Add(BottomBar); Refresh();
        }

        public void Hide() { Visible = false; TopBar.RemoveFromHierarchy(); Map.RemoveFromHierarchy(); Perf.RemoveFromHierarchy(); Radial.Close(); Chat.RemoveFromHierarchy(); BottomBar.RemoveFromHierarchy(); foreach (var window in _windows.Values) window.Close(); }

        /// <summary>Shows or hides the performance overlay (F3).</summary>
        public bool TogglePerformance() { var on = Perf.style.display == DisplayStyle.None; Perf.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; return on; }
        public bool PerformanceVisible => Perf.style.display != DisplayStyle.None;

        public void SetIsland(string text) => _island.text = text;
        /// <summary>The back and forward buttons of the islands visited (-1 back, +1 forward).</summary>
        public readonly Button IslandBack, IslandForward; public Action<int> Travel;
        public void SetTravel(bool back, bool forward) { IslandBack.SetEnabled(back); IslandForward.SetEnabled(forward); }
        private Label _balance;
        /// <summary>The balance of the wallet on the top bar (kept up to date by the live channel); null hides it.</summary>
        public readonly Button BuyCoins; public Action BuyCoinsPressed;
        private readonly VisualElement _menus; private VisualElement _openMenu;
        public IReadOnlyList<string> MenuTitles => _menus.Children().OfType<Button>().Select(b => b.text).ToList();
        public VisualElement OpenMenu => _openMenu != null && _openMenu.parent != null ? _openMenu : null;

        /// <summary>A menu of the top bar, as the Godot client's (Eu, Comunicar, Mundo, Conteúdo, Ajuda, Desenvolver): a title with a caret that opens a list of actions with icons under it.</summary>
        public Button AddMenu(string title, IReadOnlyList<(string Label, string Icon, Action Run)> items)
        {
            Button button = null; button = UiKit.Button(title, () => ToggleMenu(button, items), "lv-btn-flat"); button.name = "menu-" + title; UiKit.WithTrailingIcon(button, "caret-down", 12); _menus.Add(button); return button;
        }

        /// <summary>Opens (or closes) the list of a menu under its title.</summary>
        public void ToggleMenu(Button title, IReadOnlyList<(string Label, string Icon, Action Run)> items)
        {
            var wasThis = _openMenu != null && _openMenu.parent != null && _openMenu.userData == title; CloseMenu(); if (wasThis) return;
            var list = new VisualElement { name = "menu-list", userData = title }; list.AddToClassList("lv-glass"); list.style.position = Position.Absolute; list.style.paddingTop = list.style.paddingBottom = 6; list.style.minWidth = 230;
            var scale = Mathf.Max(.01f, Ui.Panel.scale); var at = title.worldBound; list.style.left = at.xMin; list.style.top = at.yMax + 6;
            foreach (var (label, icon, run) in items) { var go = run; var entry = UiKit.WithIcon(UiKit.Button(label, () => { CloseMenu(); go?.Invoke(); }, "lv-btn-flat"), string.IsNullOrEmpty(icon) ? "dots" : icon); entry.style.unityTextAlign = TextAnchor.MiddleLeft; entry.style.marginBottom = 1; list.Add(entry); }
            Ui.WindowLayer.Add(list); _openMenu = list; list.BringToFront();
            Ui.Root.RegisterCallback<PointerDownEvent>(OutsideMenu, TrickleDown.TrickleDown);
        }

        private void OutsideMenu(PointerDownEvent e) { if (_openMenu == null || _openMenu.parent == null) return; if (_openMenu.worldBound.Contains((Vector2)e.position) || ((VisualElement)_openMenu.userData).worldBound.Contains((Vector2)e.position)) return; CloseMenu(); }
        public void CloseMenu() { _openMenu?.RemoveFromHierarchy(); _openMenu = null; Ui.Root.UnregisterCallback<PointerDownEvent>(OutsideMenu, TrickleDown.TrickleDown); }

        public void SetBalance(long? coins) => _balance.text = coins is { } c ? SectionWindow.Coins(c) : "";
        public string BalanceText => _balance.text;

        public void SetConnection(bool connected, int nearby)
        {
            _connection.text = connected ? "● conectado" : "● desconectado"; _connection.EnableInClassList("lv-good", connected); _connection.EnableInClassList("lv-bad", !connected);
            _people.text = nearby == 1 ? "1 pessoa" : nearby + " pessoas";
        }

        /// <summary>The ids of the bottom bar's buttons in the order they were registered (the default order).</summary>
        public readonly List<string> ToolbarDefaults = new List<string>();
        public string Mode { get; private set; } = "full";
        /// <summary>In the immersive mode the interface hides; F10 shows it until F10 again.</summary>
        public bool Revealed { get; private set; }

        /// <summary>Puts the interface in a mode ("full": everything; "compact": the bars only, a smaller toolbar, no map or chat; "immersive": only the world), with the toolbar in the person's order and without the buttons they hid.</summary>
        public void ApplyLayout(string mode, bool compactToolbar, IReadOnlyList<string> order, IReadOnlyCollection<string> hidden)
        {
            Mode = mode is "compact" or "immersive" ? mode : "full"; if (Mode != "immersive") Revealed = false;
            var ids = ToolbarLayout.Order(ToolbarDefaults, order ?? new List<string>()); var buttons = ids.Select(id => BottomBar.Q<Button>("btn-" + id)).Where(b => b != null).ToList();
            foreach (var b in buttons) { b.RemoveFromHierarchy(); BottomBar.Add(b); var id = b.name.Substring(4); b.style.display = hidden != null && hidden.Contains(id) ? DisplayStyle.None : DisplayStyle.Flex; UiKit.IconOnly(b, compactToolbar || Mode == "compact"); }
            Refresh();
        }

        public void ToggleReveal() { if (Mode != "immersive") return; Revealed = !Revealed; Refresh(); }

        private void Refresh()
        {
            var shown = Visible && (Mode != "immersive" || Revealed); var full = Mode == "full" || Revealed;
            TopBar.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None; BottomBar.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            Map.style.display = shown && full ? DisplayStyle.Flex : DisplayStyle.None; Chat.style.display = shown && full ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Adds a button to the bottom bar that opens (or closes) a window.</summary>
        /// <summary>The icon of each button of the bar, as in the Godot client's toolbar (the window it opens shows it in its title too).</summary>
        /// <summary>The buttons that start hidden (they are in the menus of the top bar), until the person arranges the bar.</summary>
        public static readonly string[] DefaultHidden = { "chat", "social", "more", "settings", "help" };

        /// <summary>The order the bar starts with (the Godot client's: speak, gestures, photo, build, people, map, inventory, mini-map…); ids not named keep their place after these.</summary>
        public void SetDefaultOrder(params string[] ids) { var rest = ToolbarDefaults.Where(id => !ids.Contains(id)).ToList(); ToolbarDefaults.Clear(); ToolbarDefaults.AddRange(ids.Where(id => rest.Contains(id) || BottomBar.Q<Button>("btn-" + id) != null)); ToolbarDefaults.AddRange(rest); }

        public static readonly Dictionary<string, string> Icons = new Dictionary<string, string>
        {
            ["chat"] = "chat", ["people"] = "people", ["social"] = "user", ["inventory"] = "bag", ["wallet"] = "coin", ["search"] = "search", ["build"] = "hammer", ["voice"] = "mic",
            ["notifications"] = "bell", ["map"] = "pin", ["more"] = "dots", ["settings"] = "settings", ["help"] = "help", ["gestures"] = "hand", ["photo"] = "photo", ["minimap"] = "map",
        };

        public Button Register(string label, string id, Func<FloatingWindow> create)
        {
            var button = UiKit.Button(label, () => Toggle(id, create)); button.name = "btn-" + id; button.tooltip = label;
            if (Icons.TryGetValue(id, out var icon)) UiKit.WithIcon(button, icon);
            BottomBar.Add(button); if (!ToolbarDefaults.Contains(id)) ToolbarDefaults.Add(id); DragToReorder(button, id); return button;
        }

        /// <summary>A button of the bar was dragged onto another (its id, the other's id): the game saves the new order.</summary>
        public event Action<string, string> Reordered;
        private string _dragId; private Vector2 _dragFrom; private bool _dragMoved;

        /// <summary>The id of the bar button under a point of the panel, or null.</summary>
        public string ToolbarIdAt(VisualElement picked) { for (var e = picked; e != null; e = e.parent) if (e is Button b && b.parent == BottomBar && b.name.StartsWith("btn-", StringComparison.Ordinal)) return b.name.Substring(4); return null; }

        public void DropOnto(string id, string onto) { if (id != null && onto != null && id != onto) Reordered?.Invoke(id, onto); }

        private void DragToReorder(Button button, string id)
        {
            button.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; _dragId = id; _dragFrom = e.position; _dragMoved = false; }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerMoveEvent>(e => { if (_dragId != id || _dragMoved) return; if (((Vector2)e.position - _dragFrom).sqrMagnitude > 400f) { _dragMoved = true; button.AddToClassList("lv-dragging"); } }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerUpEvent>(e =>
            {
                if (_dragId != id) return; _dragId = null; button.RemoveFromClassList("lv-dragging"); if (!_dragMoved) return;
                DropOnto(id, ToolbarIdAt(button.panel?.Pick(e.position)));
            }, TrickleDown.TrickleDown);
        }

        public FloatingWindow Toggle(string id, Func<FloatingWindow> create)
        {
            if (_windows.TryGetValue(id, out var open) && open.IsOpen) { open.Close(); return null; }
            var window = create(); if (window == null) return null; if (Icons.TryGetValue(id, out var icon)) window.SetIcon(icon); _windows[id] = window; window.Open();
            // the button of an open window looks selected (teal, or orange for the inventory), as in the Godot client
            if (BottomBar.Q<Button>("btn-" + id) is { } button) { var look = id == "inventory" ? "lv-btn-open-orange" : "lv-btn-open"; button.AddToClassList(look); window.Closed += () => button.RemoveFromClassList(look); }
            return window;
        }

        public FloatingWindow WindowOf(string id) => _windows.TryGetValue(id, out var window) && window.IsOpen ? window : null;

        /// <summary>The window of the people around: everyone the server tells about, with how far they are, nearest first.</summary>
        public static FloatingWindow HelpWindow(UiRoot ui)
        {
            var window = new FloatingWindow(ui, "help", "Ajuda", 380f, -1f, new Vector2(520, 160));
            foreach (var line in new[] { "W A S D: andar · Shift: correr · Espaço: pular", "F: voar · C ou Ctrl: descer · X: sentar", "Botão direito + mouse: girar a câmera · roda: aproximar", "V: primeira pessoa · E: entrar no carro e sair", "G acenar · H dançar · J aplaudir · K apontar · L cumprimentar · N rir", "Enter: abre o chat · Esc: sai do chat" })
            { var label = UiKit.Text(line, "lv-muted"); label.style.whiteSpace = WhiteSpace.Normal; label.style.marginBottom = 4; window.Body.Add(label); }
            return window;
        }
    }
}
