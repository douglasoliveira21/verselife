using System;
using System.Collections.Generic;
using System.Linq;
using LifeVerse.Unity.Avatar;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The interface while playing, laid out as the Second Life viewer: at the top the menu bar (the menus, the balance, how many people are near, the clock and the connection) and under it
    /// the navigation bar (back, forward, where the person is, the search and the presence); the nearby chat at the bottom left, the mini-map at the top right, and at the bottom the toolbar
    /// whose buttons open the floating windows. <see cref="Register"/> adds a button to the toolbar.
    /// </summary>
    public sealed class Hud
    {
        public const float Edge = 8f;
        public readonly UiRoot Ui; public readonly ChatPanel Chat; public readonly NotificationCenter Notices;
        public readonly VisualElement TopBar, BottomBar, MenuBar, NavigationBar; public readonly DropdownField Presence = new DropdownField(new System.Collections.Generic.List<string> { "online", "away", "busy", "invisible" }, 0); public readonly Minimap Map = new Minimap(); public readonly PerformanceOverlay Perf = new PerformanceOverlay(); public readonly RadialMenu Radial = new RadialMenu(); private readonly Label _connection, _people, _island, _position, _clock; private readonly Dictionary<string, FloatingWindow> _windows = new Dictionary<string, FloatingWindow>();
        public bool Visible { get; private set; }
        /// <summary>The field of the navigation bar: Enter asks the game to search for what was written (<see cref="SearchAsked"/>).</summary>
        public readonly TextField Search; public Action<string> SearchAsked;
        /// <summary>A click on the address (the map), and the two buttons at its end (about the island, about the land under the person).</summary>
        public Action AddressPressed, IslandPressed, ParcelPressed;

        /// <summary>The words of each presence of the account (the values are the platform's).</summary>
        public static string PresenceWords(string status) => status switch { "online" => "Disponível", "away" => "Ausente", "busy" => "Ocupado", "invisible" => "Invisível", _ => status };

        public Hud(UiRoot ui, NotificationCenter notices)
        {
            Ui = ui; Notices = notices; Chat = new ChatPanel(ui.Settings);
            TopBar = new VisualElement { name = "topbar", pickingMode = PickingMode.Position }; TopBar.style.position = Position.Absolute; TopBar.style.top = 0; TopBar.style.left = 0; TopBar.style.right = 0;

            // the menu bar: the menus on the left, the state of things on the right
            MenuBar = Bar("lv-menubar", "menubar"); TopBar.Add(MenuBar);
            var logo = new Icon("cube", 20, Teal) { name = "logo", tooltip = "LifeVerse" }; logo.style.marginRight = 8; MenuBar.Add(logo); _menus = new VisualElement { name = "menus" }; _menus.style.flexDirection = FlexDirection.Row; MenuBar.Add(_menus); MenuBar.Add(UiKit.Spacer());
            var coin = new Icon("coin", 17, Gold) { name = "coin" }; coin.style.marginRight = 6; MenuBar.Add(coin); _balance = UiKit.Text("", "lv-bold", "lv-gold"); MenuBar.Add(_balance);
            BuyCoins = UiKit.WithIcon(UiKit.Button("Comprar", () => BuyCoinsPressed?.Invoke(), "lv-btn-secondary"), "plus", 13); BuyCoins.AddToClassList("lv-money"); BuyCoins.name = "buy-coins"; BuyCoins.tooltip = "Carteira: comprar VerseCoin"; MenuBar.Add(BuyCoins);
            MenuBar.Add(Separator()); var people = new Icon("people", 16, Muted); people.style.marginRight = 5; MenuBar.Add(people); _people = UiKit.Text("0 pessoas", "lv-muted"); MenuBar.Add(_people);
            MenuBar.Add(Separator()); _clock = UiKit.Text("", "lv-muted"); _clock.name = "clock"; MenuBar.Add(_clock); Tick(); MenuBar.schedule.Execute(Tick).Every(5000);
            MenuBar.Add(Separator()); _connection = UiKit.Text("● desconectado", "lv-bad"); MenuBar.Add(_connection);

            // the navigation bar: where the person is, with the way back and forward
            NavigationBar = Bar("lv-navbar", "navbar"); TopBar.Add(NavigationBar);
            IslandBack = IconButton("chevron-left", "Voltar à ilha anterior", () => Travel?.Invoke(-1)); IslandBack.name = "island-back"; IslandForward = IconButton("chevron-right", "Avançar para a próxima ilha", () => Travel?.Invoke(1)); IslandForward.name = "island-forward"; NavigationBar.Add(IslandBack); NavigationBar.Add(IslandForward); SetTravel(false, false);
            var address = new VisualElement { name = "address", tooltip = "Onde você está (clique para abrir o mapa)" }; address.AddToClassList("lv-address");
            var pin = new Icon("pin", 16, Teal); pin.style.marginRight = 7; address.Add(pin); _island = UiKit.Text("", "lv-bold"); _island.style.flexShrink = 1; _island.style.overflow = Overflow.Hidden; _island.style.textOverflow = TextOverflow.Ellipsis; address.Add(_island);
            _position = UiKit.Text("", "lv-muted"); _position.name = "position"; _position.style.marginLeft = 12; address.Add(_position); address.Add(UiKit.Spacer());
            var about = IconButton("info", "Sobre a ilha", () => IslandPressed?.Invoke(), 15); about.name = "about-island"; address.Add(about); var parcel = IconButton("grid", "Sobre este terreno", () => ParcelPressed?.Invoke(), 15); parcel.name = "about-parcel"; address.Add(parcel);
            address.RegisterCallback<ClickEvent>(e => { for (var t = e.target as VisualElement; t != null && t != address; t = t.parent) if (t is Button) return; AddressPressed?.Invoke(); });
            NavigationBar.Add(address);
            Search = new TextField { maxLength = 120, name = "nav-search", tooltip = "Buscar no LifeVerse (Enter)" }; Search.style.width = 220; Search.style.marginRight = 2; Search.textEdition.placeholder = "Buscar no LifeVerse…";
            Search.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { RunSearch(); e.StopPropagation(); } else if (e.keyCode == KeyCode.Escape) Search.Blur(); }, TrickleDown.TrickleDown); NavigationBar.Add(Search);
            var find = IconButton("search", "Buscar", RunSearch); find.name = "nav-search-go"; NavigationBar.Add(find); NavigationBar.Add(Separator());
            Presence.formatSelectedValueCallback = PresenceWords; Presence.formatListItemCallback = PresenceWords; Presence.SetValueWithoutNotify(Presence.choices[0]); Presence.tooltip = "Como os outros veem você"; Presence.style.width = 128; NavigationBar.Add(Presence);

            BottomBar = Bar("lv-toolbar", "bottombar"); BottomBar.style.position = Position.Absolute; BottomBar.style.bottom = 0; BottomBar.style.left = 0; BottomBar.style.right = 0;
            Chat.style.position = Position.Absolute; Chat.style.left = Edge; Chat.style.bottom = FloatingWindow.BottomInset;
            Map.style.position = Position.Absolute; Map.style.right = Edge; Map.style.top = FloatingWindow.TopInset; Perf.style.left = Edge; Perf.style.top = FloatingWindow.TopInset; Perf.style.display = DisplayStyle.None;
        }

        private static readonly Color Teal = new Color(.247f, .89f, .827f), Gold = new Color(1f, .77f, .36f), Muted = new Color(.65f, .69f, .77f);
        private static VisualElement Bar(string kind, string name) { var bar = new VisualElement { name = name, pickingMode = PickingMode.Position }; bar.AddToClassList("lv-bar"); bar.AddToClassList(kind); return bar; }
        private static VisualElement Separator() { var line = new VisualElement { pickingMode = PickingMode.Ignore }; line.AddToClassList("lv-separator"); return line; }
        private static Button IconButton(string icon, string tip, Action run, float size = 16f) { var button = UiKit.Button("", run, "lv-btn-flat"); button.tooltip = tip; button.AddToClassList("lv-nav-icon"); button.Add(new Icon(icon, size)); return button; }
        private void Tick() => _clock.text = DateTime.Now.ToString("HH:mm");
        /// <summary>Asks for the search with what the field of the navigation bar says (an empty field opens the search).</summary>
        public void RunSearch() { var text = (Search.value ?? "").Trim(); Search.Blur(); Search.value = ""; SearchAsked?.Invoke(text); }

        /// <summary>Where the person is, in the metres of the game, on the address of the navigation bar.</summary>
        public void SetPosition(Vector3? game) => _position.text = game is { } p ? Mathf.RoundToInt(p.x) + ", " + Mathf.RoundToInt(p.y) + ", " + Mathf.RoundToInt(p.z) : "";
        public string PositionText => _position.text;
        public string IslandText => _island.text;

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
            Button button = null; button = UiKit.Button(title, () => ToggleMenu(button, items), "lv-btn-flat"); button.name = "menu-" + title; _menus.Add(button); return button;
        }

        /// <summary>An entry with this label draws a line between two groups of a menu.</summary>
        public const string MenuLine = "-";

        /// <summary>Opens (or closes) the list of a menu under its title.</summary>
        public void ToggleMenu(Button title, IReadOnlyList<(string Label, string Icon, Action Run)> items)
        {
            var wasThis = _openMenu != null && _openMenu.parent != null && _openMenu.userData == title; CloseMenu(); if (wasThis) return;
            var list = new VisualElement { name = "menu-list", userData = title }; list.AddToClassList("lv-menu");
            var at = title.worldBound; list.style.left = at.xMin; list.style.top = at.yMax + 2;
            foreach (var (label, icon, run) in items)
            {
                if (label == MenuLine) { var line = new VisualElement { pickingMode = PickingMode.Ignore }; line.AddToClassList("lv-menu-separator"); list.Add(line); continue; }
                var go = run; var entry = UiKit.WithIcon(UiKit.Button(label, () => { CloseMenu(); go?.Invoke(); }, "lv-btn-flat"), string.IsNullOrEmpty(icon) ? "dots" : icon, 15); entry.AddToClassList("lv-menu-item"); list.Add(entry);
            }
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
            foreach (var b in buttons) { b.RemoveFromHierarchy(); BottomBar.Add(b); var id = b.name.Substring(4); b.style.display = hidden != null && hidden.Contains(id) ? DisplayStyle.None : DisplayStyle.Flex; UiKit.IconOnly(b, compactToolbar || Mode == "compact" || ToolbarEdge != "bottom"); }
            Refresh();
        }

        /// <summary>The room a toolbar on a side takes.</summary>
        public const float SideBarWidth = 54f;
        /// <summary>Where the toolbar stands: "bottom" (icons and names), or "left" / "right" (a column of icons).</summary>
        public string ToolbarEdge { get; private set; } = "bottom";

        public void SetToolbarEdge(string edge)
        {
            ToolbarEdge = edge == "left" || edge == "right" ? edge : "bottom"; var side = ToolbarEdge != "bottom"; var left = ToolbarEdge == "left"; var right = ToolbarEdge == "right";
            BottomBar.EnableInClassList("lv-toolbar-side", side);
            BottomBar.style.top = side ? FloatingWindow.TopInset : StyleKeyword.Auto; BottomBar.style.left = right ? StyleKeyword.Auto : 0; BottomBar.style.right = left ? StyleKeyword.Auto : 0;
            Chat.style.left = Edge + (left ? SideBarWidth : 0f); Chat.style.bottom = side ? Edge : FloatingWindow.BottomInset; Map.style.right = Edge + (right ? SideBarWidth : 0f); Perf.style.left = Edge + (left ? SideBarWidth : 0f);
            Ui.ToastLayer.style.paddingRight = 10f + (right ? SideBarWidth : 0f); Ui.ToastLayer.style.paddingBottom = side ? 10f : FloatingWindow.BottomInset;
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
        public static readonly string[] DefaultHidden = { "social", "more", "settings", "help" };

        /// <summary>The order the bar starts with (the Godot client's: speak, gestures, photo, build, people, map, inventory, mini-map…); ids not named keep their place after these.</summary>
        public void SetDefaultOrder(params string[] ids) { var rest = ToolbarDefaults.Where(id => !ids.Contains(id)).ToList(); ToolbarDefaults.Clear(); ToolbarDefaults.AddRange(ids.Where(id => rest.Contains(id) || BottomBar.Q<Button>("btn-" + id) != null)); ToolbarDefaults.AddRange(rest); }

        public static readonly Dictionary<string, string> Icons = new Dictionary<string, string>
        {
            ["chat"] = "chat", ["people"] = "people", ["social"] = "user", ["inventory"] = "bag", ["wallet"] = "coin", ["search"] = "search", ["build"] = "hammer", ["voice"] = "mic",
            ["notifications"] = "bell", ["map"] = "globe", ["more"] = "dots", ["settings"] = "settings", ["help"] = "help", ["gestures"] = "hand", ["photo"] = "photo", ["minimap"] = "map", ["move"] = "run", ["camera"] = "camera",
        };

        public Button Register(string label, string id, Func<FloatingWindow> create)
        {
            var button = UiKit.Button(label, () => Toggle(id, create)); button.name = "btn-" + id; button.tooltip = label; button.AddToClassList("lv-tool");
            UiKit.Stacked(button, Icons.TryGetValue(id, out var icon) ? icon : "dots");
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

        /// <summary>The keys of the game, by what they do.</summary>
        public static readonly (string Title, (string Keys, string Does)[] Rows)[] HelpPages =
        {
            ("Mover", new[] { ("W A S D", "andar"), ("Shift", "correr"), ("Espaço", "pular ou subir voando"), ("F", "voar"), ("C ou Ctrl", "descer"), ("X", "sentar"), ("E", "entrar no carro e sair") }),
            ("Câmera", new[] { ("Botão direito + mouse", "girar a câmera"), ("Roda do mouse", "aproximar e afastar"), ("V", "primeira pessoa"), ("Alt + V", "câmera solta") }),
            ("Gestos", new[] { ("G", "acenar"), ("H", "dançar"), ("J", "aplaudir"), ("K", "apontar"), ("L", "cumprimentar"), ("N", "rir"), ("Q", "menu de gestos") }),
            ("Interface", new[] { ("Enter", "abrir a conversa"), ("Esc", "sair da conversa"), ("B", "construir"), ("T", "editar o terreno"), ("F3", "desempenho"), ("F10", "mostrar a interface no modo imersivo"), ("F12", "tirar foto") }),
        };

        public static FloatingWindow HelpWindow(UiRoot ui)
        {
            var window = new FloatingWindow(ui, "help", "Ajuda: atalhos e controles", 430f, 500f, new Vector2(420, 90)); window.SetIcon("help");
            var list = new ScrollView(); list.style.flexGrow = 1; window.Body.style.minHeight = 0; window.Body.style.overflow = Overflow.Hidden; window.Body.Add(list);
            foreach (var (title, rows) in HelpPages)
            {
                list.Add(UiKit.Text(title, "lv-heading"));
                foreach (var (keys, does) in rows)
                {
                    var key = UiKit.Text(keys, "lv-inset", "lv-small", "lv-bold"); key.style.width = 160; key.style.paddingTop = key.style.paddingBottom = 2; key.style.unityTextAlign = TextAnchor.MiddleCenter; key.style.flexShrink = 0;
                    var what = UiKit.Text(does, "lv-muted"); what.style.marginLeft = 10; what.style.whiteSpace = WhiteSpace.Normal; what.style.flexShrink = 1; var row = UiKit.Row(key, what); row.style.marginBottom = 3; list.Add(row);
                }
            }
            return window;
        }
    }
}
