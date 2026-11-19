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
            TopBar.Add(UiKit.Text("LifeVerse", "lv-title-text", "lv-accent")); _island = UiKit.Text("", "lv-muted"); _island.style.marginLeft = 14; TopBar.Add(_island); TopBar.Add(UiKit.Spacer());
            _people = UiKit.Text("0 pessoas", "lv-muted"); _people.style.marginRight = 18; TopBar.Add(_people); Presence.style.width = 110; Presence.style.marginRight = 10; TopBar.Add(Presence); _connection = UiKit.Text("● desconectado", "lv-bad"); TopBar.Add(_connection);
            BottomBar = new VisualElement(); BottomBar.AddToClassList("lv-bar"); BottomBar.name = "bottombar"; BottomBar.style.position = Position.Absolute; BottomBar.style.bottom = 14; BottomBar.style.left = 18; BottomBar.style.right = 18; BottomBar.style.justifyContent = Justify.Center; BottomBar.pickingMode = PickingMode.Position;
            Chat.style.position = Position.Absolute; Chat.style.left = 18; Chat.style.bottom = 76;
            Map.style.position = Position.Absolute; Map.style.left = 18; Map.style.top = 70; Perf.style.top = 270; Perf.style.display = DisplayStyle.None;
        }

        public void Show()
        {
            if (Visible) return; Visible = true; Ui.HudLayer.Add(TopBar); Ui.HudLayer.Add(Map); Ui.HudLayer.Add(Perf); Ui.HudLayer.Add(Chat); Ui.HudLayer.Add(BottomBar);
        }

        public void Hide() { Visible = false; TopBar.RemoveFromHierarchy(); Map.RemoveFromHierarchy(); Perf.RemoveFromHierarchy(); Radial.Close(); Chat.RemoveFromHierarchy(); BottomBar.RemoveFromHierarchy(); foreach (var window in _windows.Values) window.Close(); }

        /// <summary>Shows or hides the performance overlay (F3).</summary>
        public bool TogglePerformance() { var on = Perf.style.display == DisplayStyle.None; Perf.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; return on; }
        public bool PerformanceVisible => Perf.style.display != DisplayStyle.None;

        public void SetIsland(string text) => _island.text = text;

        public void SetConnection(bool connected, int nearby)
        {
            _connection.text = connected ? "● conectado" : "● desconectado"; _connection.EnableInClassList("lv-good", connected); _connection.EnableInClassList("lv-bad", !connected);
            _people.text = nearby == 1 ? "1 pessoa" : nearby + " pessoas";
        }

        /// <summary>Adds a button to the bottom bar that opens (or closes) a window.</summary>
        public Button Register(string label, string id, Func<FloatingWindow> create)
        {
            var button = UiKit.Button(label, () => Toggle(id, create)); button.name = "btn-" + id; BottomBar.Add(button); return button;
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
