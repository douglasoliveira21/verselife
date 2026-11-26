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
            TopBar.Add(UiKit.Text("LifeVerse", "lv-title-text", "lv-accent")); IslandBack = UiKit.Button("‹", () => Travel?.Invoke(-1), "lv-btn-flat"); IslandBack.name = "island-back"; IslandBack.tooltip = "Voltar à ilha anterior"; IslandForward = UiKit.Button("›", () => Travel?.Invoke(1), "lv-btn-flat"); IslandForward.name = "island-forward"; IslandForward.tooltip = "Avançar para a próxima ilha"; IslandBack.style.marginLeft = 10; TopBar.Add(IslandBack); TopBar.Add(IslandForward); SetTravel(false, false); _island = UiKit.Text("", "lv-muted"); _island.style.marginLeft = 14; TopBar.Add(_island); TopBar.Add(UiKit.Spacer());
            _balance = UiKit.Text("", "lv-accent"); _balance.style.marginRight = 18; TopBar.Add(_balance);
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
            foreach (var b in buttons) { b.RemoveFromHierarchy(); BottomBar.Add(b); var id = b.name.Substring(4); b.style.display = hidden != null && hidden.Contains(id) ? DisplayStyle.None : DisplayStyle.Flex; b.style.fontSize = compactToolbar || Mode == "compact" ? 11 : StyleKeyword.Null; b.style.paddingLeft = b.style.paddingRight = compactToolbar || Mode == "compact" ? 4 : StyleKeyword.Null; }
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
        public Button Register(string label, string id, Func<FloatingWindow> create)
        {
            var button = UiKit.Button(label, () => Toggle(id, create)); button.name = "btn-" + id; BottomBar.Add(button); if (!ToolbarDefaults.Contains(id)) ToolbarDefaults.Add(id); return button;
        }

        public FloatingWindow Toggle(string id, Func<FloatingWindow> create)
        {
            if (_windows.TryGetValue(id, out var open) && open.IsOpen) { open.Close(); return null; }
            var window = create(); if (window == null) return null; _windows[id] = window; window.Open(); return window;
        }

        public FloatingWindow WindowOf(string id) => _windows.TryGetValue(id, out var window) && window.IsOpen ? window : null;

        /// <summary>The window of the people around: everyone the server tells about, with how far they are, nearest first.</summary>
        public static FloatingWindow PeopleWindow(UiRoot ui, PlayerViews views, int selfPeer, Func<Vector3> selfPosition, Action<string> actions = null)
        {
            var window = new FloatingWindow(ui, "people", "Pessoas por perto", 320f, 360f, new Vector2(420, 140));
            var list = new ScrollView(); list.style.flexGrow = 1; window.Body.Add(list);
            void Refresh()
            {
                list.Clear(); var me = selfPosition();
                foreach (var peer in views.Peers.Where(p => p != selfPeer).OrderBy(p => Vector3.Distance(views.ViewOf(p).GamePosition, me)))
                {
                    var view = views.ViewOf(peer); var who = view.DisplayName; var row = UiKit.Row(UiKit.Text(who, "lv-bold"), UiKit.Spacer(), UiKit.Text(Mathf.RoundToInt(Vector3.Distance(view.GamePosition, me)) + " m", "lv-muted")); if (actions != null) row.Add(UiKit.Button("Ações", () => actions(who))); row.AddToClassList("lv-row"); list.Add(row);
                }
                if (list.childCount == 0) list.Add(UiKit.Text("Ninguém por perto.", "lv-muted"));
            }
            Refresh(); window.schedule.Execute(Refresh).Every(1000); return window;
        }

        public static FloatingWindow HelpWindow(UiRoot ui)
        {
            var window = new FloatingWindow(ui, "help", "Ajuda", 380f, -1f, new Vector2(520, 160));
            foreach (var line in new[] { "W A S D: andar · Shift: correr · Espaço: pular", "F: voar · C ou Ctrl: descer · X: sentar", "Botão direito + mouse: girar a câmera · roda: aproximar", "V: primeira pessoa · E: entrar no carro e sair", "G acenar · H dançar · J aplaudir · K apontar · L cumprimentar · N rir", "Enter: abre o chat · Esc: sai do chat" })
            { var label = UiKit.Text(line, "lv-muted"); label.style.whiteSpace = WhiteSpace.Normal; label.style.marginBottom = 4; window.Body.Add(label); }
            return window;
        }
    }
}
