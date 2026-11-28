using System;
using System.Collections.Generic;
using System.Linq;
using LifeVerse.Unity.Avatar;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The interface while playing, in the Varanda design: at the top left the sign of the place (its name, who is there, where the person is, the way back and forward and what is known
    /// about the island and the land); at the top right the search, the balance and the bell, the clock and the connection; at the bottom the bar, with the person's own face and presence in
    /// the middle and the buttons that open the floating windows around it; the talk at the bottom left and the map, as a postcard, at the bottom right. "Mais" opens
    /// <see cref="HudSheet"/> with every menu and the choices of the bar. <see cref="Register"/> adds a button to the bar.
    /// </summary>
    public sealed class Hud
    {
        public const float Edge = 24f;
        public readonly UiRoot Ui; public readonly ChatPanel Chat; public readonly NotificationCenter Notices;
        /// <summary>The strip at the top; <see cref="NavigationBar"/> is the sign of the place on it, <see cref="MenuBar"/> the group at the right. <see cref="BottomBar"/> is the bar of buttons.</summary>
        public readonly VisualElement TopBar, BottomBar, MenuBar, NavigationBar; public readonly DropdownField Presence = new DropdownField(new List<string> { "online", "away", "busy", "invisible" }, 0); public readonly Minimap Map = new Minimap(); public readonly PerformanceOverlay Perf = new PerformanceOverlay(); public readonly RadialMenu Radial = new RadialMenu(); private readonly Label _connection, _people, _island, _position, _clock; private readonly Dictionary<string, FloatingWindow> _windows = new Dictionary<string, FloatingWindow>();
        public readonly HudSheet Sheet;
        private readonly VisualElement _dock, _faces, _face, _me, _divider; private readonly Label _badge;
        public bool Visible { get; private set; }
        /// <summary>The search field at the top: Enter asks the game to search for what was written (<see cref="SearchAsked"/>).</summary>
        public readonly TextField Search; public Action<string> SearchAsked;
        /// <summary>A click on the name of the place (the map), and the two links under it (about the island, about the land under the person).</summary>
        public Action AddressPressed, IslandPressed, ParcelPressed;
        /// <summary>The bell at the top.</summary>
        public Action BellPressed;
        /// <summary>The choices made on the sheet: show or hide a button of the bar, where the bar stands, the mode of the interface. The game keeps them.</summary>
        public Action<string, bool> ToolShownAsked; public Action<string> EdgeAsked, ModeAsked;

        /// <summary>The button of the bar that opens the sheet: always on the bar, always last.</summary>
        public const string FixedTool = "more";

        /// <summary>The words of each presence of the account (the values are the platform's).</summary>
        public static string PresenceWords(string status) => status switch { "online" => "Disponível", "away" => "Ausente", "busy" => "Ocupado", "invisible" => "Invisível", _ => status };

        public Hud(UiRoot ui, NotificationCenter notices)
        {
            Ui = ui; Notices = notices; Chat = new ChatPanel(ui.Settings);
            TopBar = Box("topbar", "lv-topbar");

            // the sign of the place: its name, who is there, where the person is and the way back and forward
            NavigationBar = Box("navbar", "lv-paper", "lv-sign"); NavigationBar.pickingMode = PickingMode.Position; TopBar.Add(NavigationBar);
            var address = new VisualElement { name = "address", tooltip = "Onde você está (clique para abrir o mapa)" }; address.AddToClassList("lv-address");
            _island = UiKit.Text("", "lv-place"); address.Add(_island);
            address.RegisterCallback<ClickEvent>(e => AddressPressed?.Invoke()); NavigationBar.Add(address);
            var who = Box("who", "lv-who"); _faces = Box("who-faces", "lv-who-faces"); who.Add(_faces); _people = UiKit.Text("0 pessoas", "lv-people", "lv-bold"); who.Add(_people); _position = UiKit.Text("", "lv-position"); _position.name = "position"; who.Add(_position); NavigationBar.Add(who);
            var links = Box("links", "lv-links"); NavigationBar.Add(links);
            IslandBack = RoundButton("back", "Voltar à ilha anterior", () => Travel?.Invoke(-1), 15); IslandBack.name = "island-back"; IslandForward = RoundButton("forward", "Avançar para a próxima ilha", () => Travel?.Invoke(1), 15); IslandForward.name = "island-forward"; links.Add(IslandBack); links.Add(IslandForward); SetTravel(false, false);
            links.Add(Link("about-island", "info", "Sobre a ilha", () => IslandPressed?.Invoke())); links.Add(Link("about-parcel", "parcel", "Este terreno", () => ParcelPressed?.Invoke()));

            // the pills at the right: search, money, and the state of things
            MenuBar = Box("menubar", "lv-pills"); TopBar.Add(MenuBar);
            var search = Pill("search-pill", "lv-search"); MenuBar.Add(search);
            Search = new TextField { maxLength = 120, name = "nav-search", tooltip = "Buscar no LifeVerse (Enter)" }; Search.textEdition.placeholder = "Procurar pessoas e lugares";
            Search.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { RunSearch(); e.StopPropagation(); } else if (e.keyCode == KeyCode.Escape) Search.Blur(); }, TrickleDown.TrickleDown); search.Add(Search);
            var find = RoundButton("search", "Buscar", RunSearch, 18); find.name = "nav-search-go"; search.Add(find);
            var money = Pill("money-pill"); MenuBar.Add(money); var coin = new VarandaIcon("coin", 22) { name = "coin" }; coin.AddToClassList("lv-sun"); money.Add(coin); _balance = UiKit.Text("", "lv-money", "lv-bold"); money.Add(_balance);
            BuyCoins = UiKit.Button("Comprar", () => BuyCoinsPressed?.Invoke(), "lv-btn-secondary"); BuyCoins.AddToClassList("lv-buy"); BuyCoins.name = "buy-coins"; BuyCoins.tooltip = "Carteira: comprar VerseCoin"; money.Add(BuyCoins);
            var state = Pill("state-pill"); MenuBar.Add(state);
            var bell = RoundButton("notifications", "Avisos", () => BellPressed?.Invoke(), 21); bell.name = "top-bell"; bell.AddToClassList("lv-bell"); _badge = UiKit.Text("", "lv-badge"); _badge.name = "bell-count"; _badge.style.display = DisplayStyle.None; bell.Add(_badge); state.Add(bell);
            _clock = UiKit.Text("", "lv-clock", "lv-bold"); _clock.name = "clock"; state.Add(_clock); Tick(); TopBar.schedule.Execute(Tick).Every(5000);
            _connection = UiKit.Text("● desconectado", "lv-connection", "lv-bad"); state.Add(_connection);

            // the bar, with the person in the middle
            _dock = Box("dock-wrap", "lv-dock-wrap");
            BottomBar = Box("bottombar", "lv-toolbar"); BottomBar.pickingMode = PickingMode.Position; _dock.Add(BottomBar);
            _me = Box("me", "lv-me"); var medal = Box("medal", "lv-medal"); _face = Faces.Make("Você", "lv-face-big"); medal.Add(_face); _me.Add(medal);
            Presence.formatSelectedValueCallback = PresenceWords; Presence.formatListItemCallback = PresenceWords; Presence.SetValueWithoutNotify(Presence.choices[0]); Presence.tooltip = "Como os outros veem você"; _me.Add(Presence);
            Presence.RegisterValueChangedCallback(_ => ShowPresence()); ShowPresence();
            _divider = Box("dock-divider", "lv-dock-divider");

            Sheet = new HudSheet(this);
            Chat.style.position = Position.Absolute; Map.style.position = Position.Absolute; Perf.style.display = DisplayStyle.None;
            Place();
            BottomBar.RegisterCallback<GeometryChangedEvent>(_ => FitMap()); Map.RegisterCallback<GeometryChangedEvent>(_ => FitMap());
        }

        /// <summary>The postcard stands at the corner; on a screen too narrow for it beside the bar, it goes above the bar.</summary>
        private void FitMap()
        {
            var bar = BottomBar.worldBound; var map = Map.worldBound; if (!(bar.width > 1f) || !(map.width > 1f)) return;
            Map.style.bottom = ToolbarEdge == "bottom" && BottomBar.style.display != DisplayStyle.None && bar.xMax + 8f > map.xMin ? FloatingWindow.BottomInset + 12f : 20f;
        }

        private static VisualElement Box(string name, params string[] classes) { var box = new VisualElement { name = name, pickingMode = PickingMode.Ignore }; foreach (var c in classes) box.AddToClassList(c); return box; }
        private static VisualElement Pill(string name, string extra = null) { var pill = Box(name, "lv-paper", "lv-pill"); if (extra != null) pill.AddToClassList(extra); pill.pickingMode = PickingMode.Position; return pill; }
        private static Button RoundButton(string icon, string tip, Action run, float size) { var button = UiKit.Button("", run); button.tooltip = tip; button.AddToClassList("lv-round"); button.Add(new VarandaIcon(icon, size)); return button; }
        private static Button Link(string name, string icon, string text, Action run)
        {
            var button = UiKit.WithIcon(UiKit.Button(text, run), "dots", 16); button.name = name; button.AddToClassList("lv-link"); ((UiKit.ButtonIcon)button.userData).Art = icon; button.tooltip = text == "Este terreno" ? "Sobre este terreno" : text; return button;
        }
        private void Tick() => _clock.text = DateTime.Now.ToString("HH:mm");
        /// <summary>Asks for the search with what the field says (an empty field opens the search).</summary>
        public void RunSearch() { var text = (Search.value ?? "").Trim(); Search.Blur(); Search.value = ""; SearchAsked?.Invoke(text); }

        /// <summary>Where the person is, in the metres of the game, on the sign of the place.</summary>
        public void SetPosition(Vector3? game) => _position.text = game is { } p ? Mathf.RoundToInt(p.x) + ", " + Mathf.RoundToInt(p.y) + ", " + Mathf.RoundToInt(p.z) : "";
        public string PositionText => _position.text;
        public string IslandText => _island.text;

        private string _self = "", _facesShown;
        /// <summary>The person's own name (their face on the bar) and the names of the people around, for the faces on the sign.</summary>
        public void SetPeople(string self, IReadOnlyList<string> others)
        {
            if (!string.IsNullOrWhiteSpace(self) && self != _self) { _self = self; Faces.Set(_face, self); }
            var names = (others ?? new List<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Take(4).ToList(); if (!string.IsNullOrWhiteSpace(_self)) names.Add(_self);
            var key = string.Join("\n", names); if (key == _facesShown) return; _facesShown = key; _faces.Clear(); foreach (var name in names) _faces.Add(Faces.Make(name, "lv-face-stacked"));
            _faces.style.display = names.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
        /// <summary>How many faces the sign shows.</summary>
        public int FacesShown => _faces.childCount;

        private string _ring;
        private void ShowPresence()
        {
            var ring = "lv-presence-" + (Presence.value ?? "online"); if (ring == _ring) return; if (_ring != null) _face.RemoveFromClassList(_ring); _ring = ring; _face.AddToClassList(ring);
        }

        /// <summary>The number on the bell at the top (nothing when there is nothing to read).</summary>
        public void SetBell(int unread) { _badge.style.display = unread > 0 ? DisplayStyle.Flex : DisplayStyle.None; _badge.text = unread > 99 ? "99+" : unread.ToString(); }
        public string BellText => _badge.style.display == DisplayStyle.None ? "" : _badge.text;

        public void Show()
        {
            if (Visible) return; Visible = true; Ui.HudLayer.Add(TopBar); Ui.HudLayer.Add(Map); Ui.HudLayer.Add(Perf); Ui.HudLayer.Add(Chat); Ui.HudLayer.Add(_dock); Refresh();
        }

        public void Hide() { Visible = false; CloseMenu(); TopBar.RemoveFromHierarchy(); Map.RemoveFromHierarchy(); Perf.RemoveFromHierarchy(); Radial.Close(); Chat.RemoveFromHierarchy(); _dock.RemoveFromHierarchy(); foreach (var window in _windows.Values) window.Close(); }

        /// <summary>Shows or hides the performance overlay (F3).</summary>
        public bool TogglePerformance() { var on = Perf.style.display == DisplayStyle.None; Perf.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; return on; }
        public bool PerformanceVisible => Perf.style.display != DisplayStyle.None;

        public void SetIsland(string text) => _island.text = text;
        /// <summary>The back and forward buttons of the islands visited (-1 back, +1 forward).</summary>
        public readonly Button IslandBack, IslandForward; public Action<int> Travel;
        public void SetTravel(bool back, bool forward) { IslandBack.SetEnabled(back); IslandForward.SetEnabled(forward); }
        private readonly Label _balance;
        /// <summary>The balance of the wallet at the top (kept up to date by the live channel); null hides it.</summary>
        public readonly Button BuyCoins; public Action BuyCoinsPressed;

        // ---- the menus: kept here, shown together on the sheet ("Mais"); one can also be opened alone under its title ----
        public sealed class Menu { public string Title; public IReadOnlyList<(string Label, string Icon, Action Run)> Items; public Button Heading; }
        private readonly List<Menu> _menus = new List<Menu>(); private VisualElement _openMenu;
        public IReadOnlyList<Menu> Menus => _menus;
        public IReadOnlyList<string> MenuTitles => _menus.Select(m => m.Title).ToList();
        /// <summary>The list on screen: a menu opened alone, or the sheet.</summary>
        public VisualElement OpenMenu => _openMenu != null && _openMenu.parent != null ? _openMenu : Sheet.IsOpen ? Sheet : null;

        /// <summary>A menu of the game (Eu, Comunicar, Mundo, Conteúdo, Ajuda, Desenvolver): a title with a list of actions.</summary>
        public Button AddMenu(string title, IReadOnlyList<(string Label, string Icon, Action Run)> items)
        {
            var menu = new Menu { Title = title, Items = items }; menu.Heading = UiKit.Button(title, () => ToggleMenu(menu.Heading, menu.Items), "lv-btn-flat"); menu.Heading.name = "menu-" + title; _menus.Add(menu); return menu.Heading;
        }

        /// <summary>An entry with this label draws a line between two groups of a menu.</summary>
        public const string MenuLine = "-";

        public void OpenSheet() { _openMenu?.RemoveFromHierarchy(); _openMenu = null; Sheet.Open(); BottomBar.Q<Button>("btn-" + FixedTool)?.AddToClassList("lv-btn-open"); }
        public void CloseSheet() { Sheet.Close(); BottomBar.Q<Button>("btn-" + FixedTool)?.RemoveFromClassList("lv-btn-open"); }
        public void ToggleSheet() { if (Sheet.IsOpen) CloseSheet(); else OpenSheet(); }

        /// <summary>Opens (or closes) the list of a menu under an element.</summary>
        public void ToggleMenu(Button title, IReadOnlyList<(string Label, string Icon, Action Run)> items)
        {
            var wasThis = _openMenu != null && _openMenu.parent != null && _openMenu.userData == title; CloseMenu(); if (wasThis) return;
            var list = new VisualElement { name = "menu-list", userData = title }; list.AddToClassList("lv-menu");
            var at = title.worldBound; list.style.left = float.IsFinite(at.xMin) ? at.xMin : Edge; list.style.top = float.IsFinite(at.yMax) ? at.yMax + 2 : 90f;
            foreach (var (label, icon, run) in items)
            {
                if (label == MenuLine) { var line = new VisualElement { pickingMode = PickingMode.Ignore }; line.AddToClassList("lv-menu-separator"); list.Add(line); continue; }
                var go = run; var entry = UiKit.WithIcon(UiKit.Button(label, () => { CloseMenu(); go?.Invoke(); }, "lv-btn-flat"), string.IsNullOrEmpty(icon) ? "dots" : icon, 15); entry.AddToClassList("lv-menu-item"); list.Add(entry);
            }
            Ui.WindowLayer.Add(list); _openMenu = list; list.BringToFront();
            Ui.Root.RegisterCallback<PointerDownEvent>(OutsideMenu, TrickleDown.TrickleDown);
        }

        private void OutsideMenu(PointerDownEvent e) { if (_openMenu == null || _openMenu.parent == null) return; if (_openMenu.worldBound.Contains((Vector2)e.position) || ((VisualElement)_openMenu.userData).worldBound.Contains((Vector2)e.position)) return; CloseMenu(); }
        public void CloseMenu() { _openMenu?.RemoveFromHierarchy(); _openMenu = null; Ui.Root.UnregisterCallback<PointerDownEvent>(OutsideMenu, TrickleDown.TrickleDown); CloseSheet(); }

        public void SetBalance(long? coins) => _balance.text = coins is { } c ? SectionWindow.Coins(c) : "";
        public string BalanceText => _balance.text;

        public void SetConnection(bool connected, int nearby)
        {
            _connection.text = connected ? "● conectado" : "● desconectado"; _connection.EnableInClassList("lv-good", connected); _connection.EnableInClassList("lv-bad", !connected);
            _people.text = nearby == 1 ? "1 pessoa perto" : nearby + " pessoas perto"; ShowPresence();
        }

        /// <summary>The ids of the bar's buttons in the order they were registered (the default order).</summary>
        public readonly List<string> ToolbarDefaults = new List<string>();
        public string Mode { get; private set; } = "full";
        /// <summary>In the immersive mode the interface hides; F10 shows it until F10 again.</summary>
        public bool Revealed { get; private set; }
        private bool _compactAsked;

        /// <summary>Puts the interface in a mode ("full": everything; "compact": the top and the bar only, with a smaller bar, no map or chat; "immersive": only the world), with the bar in the person's order and without the buttons they hid.</summary>
        public void ApplyLayout(string mode, bool compactToolbar, IReadOnlyList<string> order, IReadOnlyCollection<string> hidden)
        {
            Mode = mode is "compact" or "immersive" ? mode : "full"; if (Mode != "immersive") Revealed = false; _compactAsked = compactToolbar;
            var ids = ToolbarLayout.Order(ToolbarDefaults, order ?? new List<string>()); ids = ids.Where(id => id != FixedTool).Concat(ids.Where(id => id == FixedTool)).ToList();
            var buttons = ids.Select(id => BottomBar.Q<Button>("btn-" + id)).Where(b => b != null).ToList();
            foreach (var b in buttons)
            {
                b.RemoveFromHierarchy(); BottomBar.Add(b); var id = b.name.Substring(4);
                b.style.display = id != FixedTool && hidden != null && hidden.Contains(id) ? DisplayStyle.None : DisplayStyle.Flex; UiKit.IconOnly(b, compactToolbar || Mode == "compact" || ToolbarEdge != "bottom");
            }
            PlaceFixed(); Refresh();
        }

        /// <summary>The face goes between the two halves of the buttons shown (at the start of a bar on a side), and the line before "Mais".</summary>
        private void PlaceFixed()
        {
            _me.RemoveFromHierarchy(); _divider.RemoveFromHierarchy();
            var more = BottomBar.Q<Button>("btn-" + FixedTool); if (more != null) BottomBar.Insert(BottomBar.IndexOf(more), _divider);
            var shown = BottomBar.Children().OfType<Button>().Where(b => b != more && b.style.display != DisplayStyle.None).ToList();
            var half = (shown.Count + 1) / 2;
            if (ToolbarEdge != "bottom" || shown.Count == 0) BottomBar.Insert(0, _me);
            else BottomBar.Insert(BottomBar.IndexOf(shown[half - 1]) + 1, _me);
        }

        /// <summary>The room a bar on a side takes.</summary>
        public const float SideBarWidth = 66f;
        /// <summary>Where the bar stands: "bottom" (icons and names), or "left" / "right" (a column of icons).</summary>
        public string ToolbarEdge { get; private set; } = "bottom";

        public void SetToolbarEdge(string edge)
        {
            ToolbarEdge = edge == "left" || edge == "right" ? edge : "bottom"; Place();
            foreach (var b in BottomBar.Children().OfType<Button>().ToList()) UiKit.IconOnly(b, _compactAsked || Mode == "compact" || ToolbarEdge != "bottom");
            PlaceFixed();
        }

        private void Place()
        {
            var side = ToolbarEdge != "bottom"; var left = ToolbarEdge == "left"; var right = ToolbarEdge == "right";
            BottomBar.EnableInClassList("lv-toolbar-side", side); BottomBar.EnableInClassList("lv-toolbar-left", left); BottomBar.EnableInClassList("lv-toolbar-right", right);
            // along the bottom, centred; or a column against a side, between the sign at the top and the bottom of the screen
            _dock.style.left = right ? StyleKeyword.Auto : 0; _dock.style.right = left ? StyleKeyword.Auto : 0; _dock.style.top = side ? 184f : StyleKeyword.Auto; _dock.style.bottom = side ? 16f : 12f;
            _dock.style.justifyContent = side ? Justify.Center : Justify.FlexEnd;
            TopBar.style.paddingLeft = Edge; TopBar.style.paddingRight = Edge;
            Chat.style.left = Edge + (left ? SideBarWidth : 0f); Chat.style.bottom = side ? 20f : FloatingWindow.BottomInset + 12f;
            Map.style.right = Edge + (right ? SideBarWidth : 0f); Map.style.bottom = 20f; FitMap();
            Perf.style.left = Edge + (left ? SideBarWidth : 0f); Perf.style.top = 176f;
            Ui.ToastLayer.style.paddingRight = Edge + (right ? SideBarWidth : 0f);
        }

        public void ToggleReveal() { if (Mode != "immersive") return; Revealed = !Revealed; Refresh(); }

        private void Refresh()
        {
            var shown = Visible && (Mode != "immersive" || Revealed); var full = Mode == "full" || Revealed;
            TopBar.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None; BottomBar.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            Map.style.display = shown && full ? DisplayStyle.Flex : DisplayStyle.None; Chat.style.display = shown && full ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>The buttons that start hidden (they are on the sheet "Mais" and some also at the top of the screen), until the person arranges the bar.</summary>
        public static readonly string[] DefaultHidden = { "social", "settings", "help", "move", "camera", "minimap", "search", "wallet", "notifications" };

        /// <summary>The order the bar starts with; ids not named keep their place after these.</summary>
        public void SetDefaultOrder(params string[] ids) { var rest = ToolbarDefaults.Where(id => !ids.Contains(id)).ToList(); ToolbarDefaults.Clear(); ToolbarDefaults.AddRange(ids.Where(id => rest.Contains(id) || BottomBar.Q<Button>("btn-" + id) != null)); ToolbarDefaults.AddRange(rest); }

        /// <summary>The line icon of each button of the bar (the window it opens shows it in its title; the bar itself draws the Varanda icon of the id).</summary>
        public static readonly Dictionary<string, string> Icons = new Dictionary<string, string>
        {
            ["chat"] = "chat", ["people"] = "people", ["social"] = "user", ["inventory"] = "bag", ["wallet"] = "coin", ["search"] = "search", ["build"] = "hammer", ["voice"] = "mic",
            ["notifications"] = "bell", ["map"] = "globe", ["more"] = "dots", ["settings"] = "settings", ["help"] = "help", ["gestures"] = "hand", ["photo"] = "photo", ["minimap"] = "map", ["move"] = "run", ["camera"] = "camera",
        };

        private readonly Dictionary<string, string> _names = new Dictionary<string, string>();
        /// <summary>The name a button of the bar was registered with (its words on screen may change: the unread count, the voice off).</summary>
        public string NameOf(string id) => _names.TryGetValue(id, out var name) ? name : id;

        /// <summary>Adds a button to the bar that opens (or closes) a window.</summary>
        public Button Register(string label, string id, Func<FloatingWindow> create)
        {
            var button = UiKit.Button(label, () => Toggle(id, create)); button.name = "btn-" + id; button.tooltip = label; button.AddToClassList("lv-tool"); _names[id] = label;
            button.displayTooltipWhenElided = false;   // otherwise the engine takes the hint away once the name fits again, after the bar was on a side
            UiKit.Stacked(button, Icons.TryGetValue(id, out var icon) ? icon : "dots", 30f); if (VarandaIcons.Has(id)) ((UiKit.ButtonIcon)button.userData).Art = id;
            BottomBar.Add(button); if (!ToolbarDefaults.Contains(id)) ToolbarDefaults.Add(id); DragToReorder(button, id); PlaceFixed(); return button;
        }

        /// <summary>A button of the bar was dragged onto another (its id, the other's id): the game saves the new order.</summary>
        public event Action<string, string> Reordered;
        private string _dragId; private Vector2 _dragFrom; private bool _dragMoved;

        /// <summary>The id of the bar button under a point of the panel, or null.</summary>
        public string ToolbarIdAt(VisualElement picked) { for (var e = picked; e != null; e = e.parent) if (e is Button b && b.parent == BottomBar && b.name.StartsWith("btn-", StringComparison.Ordinal)) return b.name.Substring(4); return null; }

        public void DropOnto(string id, string onto) { if (id != null && onto != null && id != onto && id != FixedTool && onto != FixedTool) Reordered?.Invoke(id, onto); }

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
            // the button of an open window looks selected (sea, or clay for the inventory)
            if (BottomBar.Q<Button>("btn-" + id) is { } button) { var look = id == "inventory" ? "lv-btn-open-orange" : "lv-btn-open"; button.AddToClassList(look); window.Closed += () => button.RemoveFromClassList(look); }
            return window;
        }

        public FloatingWindow WindowOf(string id) => _windows.TryGetValue(id, out var window) && window.IsOpen ? window : null;

        /// <summary>The keys of the game, by what they do.</summary>
        public static readonly (string Title, (string Keys, string Does)[] Rows)[] HelpPages =
        {
            ("Mover", new[] { ("W A S D", "andar"), ("Shift", "correr"), ("Espaço", "pular ou subir voando"), ("F", "voar"), ("C ou Ctrl", "descer"), ("X", "sentar"), ("E", "entrar no carro e sair"), ("Duplo clique no chão", "andar até o ponto") }),
            ("Câmera", new[] { ("Botão direito + mouse", "girar a câmera"), ("Roda do mouse", "aproximar e afastar"), ("V", "primeira pessoa"), ("R", "câmera de volta atrás de você"), ("Alt + V", "câmera solta") }),
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
