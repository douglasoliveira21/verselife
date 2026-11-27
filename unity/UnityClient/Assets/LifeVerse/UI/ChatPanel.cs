using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    public sealed record ChatLine(int PeerId, string Name, string Text, DateTimeOffset Time, bool System = false);

    /// <summary>A private or group conversation kept as a tab of the chat.</summary>
    public sealed class ChatTab
    {
        public Guid Id; public bool Group; public string Name; public int Unread;
        public ScrollView Log; public VisualElement Header; public Button Choose, Close;
        /// <summary>The lines on screen, as text (for the checks).</summary>
        public readonly List<string> Shown = new List<string>();
    }

    /// <summary>
    /// The chat of the island: the lines people said (newest at the bottom, the last 300 kept), a field to say something and the keys that go with it. Enter sends, Escape leaves the
    /// field; while the field has the focus the avatar types on a keyboard for everybody to see (<see cref="TypingChanged"/>), and the walking keys do not move the avatar.
    /// Text is cleaned of control characters and cut at <see cref="MaxLength"/>; empty lines are not sent. The server decides who hears (the island's interest range).
    /// </summary>
    public sealed class ChatPanel : VisualElement
    {
        public const int MaxLines = 300, MaxLength = 300;
        private readonly List<ChatLine> _lines = new List<ChatLine>();
        private readonly ScrollView _log; private readonly TextField _input; private readonly UserSettings _settings;
        /// <summary>Called with the text to send; false means it could not be sent (the line stays in the field).</summary>
        public Func<string, bool> Send;
        public event Action<bool> TypingChanged;
        public bool Typing { get; private set; }
        public IReadOnlyList<ChatLine> Lines => _lines;
        public TextField Input => _input;
        /// <summary>The hour before each message, and for how long the messages stay on screen after one arrives (when the person is not writing); 0 keeps them.</summary>
        public bool ShowTime = true; public float VisibleSeconds = 6f;
        private float _lastMessage; private bool _hovered, _watching;
        public bool LogShown => _log.style.opacity.value > .5f;
        private void ShowLog() { _log.style.opacity = 1f; SetFrame(true); if (!_watching) { _watching = true; RegisterCallback<PointerEnterEvent>(_ => { _hovered = true; ShowLog(); }); RegisterCallback<PointerLeaveEvent>(_ => { _hovered = false; _lastMessage = Time.unscaledTime; }); schedule.Execute(FadeIfIdle).Every(250); } }
        private void FadeIfIdle() { if (VisibleSeconds <= 0f || Typing || _hovered || Minimized || Active != null) { _log.style.opacity = 1f; SetFrame(true); return; } if (Time.unscaledTime - _lastMessage > VisibleSeconds) { _log.style.opacity = 0f; SetFrame(false); } }
        /// <summary>The window around the chat: gone while nothing is being said, as the nearby chat of the viewer, and back with a message, the mouse or the keyboard.</summary>
        public bool FrameShown { get; private set; } = true;
        private readonly VisualElement _title;
        private void SetFrame(bool shown) { FrameShown = shown; style.unityBackgroundImageTintColor = new Color(1f, 1f, 1f, shown ? Mathf.Clamp(_settings.ChatOpacity, .3f, 1f) : 0f); if (_title != null) _title.style.opacity = shown ? 1f : 0f; }
        private readonly Button _minimize;
        /// <summary>Minimized, the chat keeps only the line to write in (the messages stay and come back).</summary>
        public bool Minimized { get; private set; }
        public const float OpenHeight = 250f, FoldedHeight = 76f;
        public void SetMinimized(bool minimized) { Minimized = minimized; if (!minimized && Active != null) Active.Unread = 0; ShowLogs(); style.height = minimized ? FoldedHeight : OpenHeight; if (_minimize.Q<Icon>() is { } icon) icon.Name = minimized ? "expand" : "minus"; _minimize.tooltip = minimized ? "Mostrar as mensagens" : "Minimizar o chat"; }
        // ---- conversations: one tab each, beside the nearby chat, as the viewer's conversations ----
        public const int MaxTabs = 6;
        private readonly List<ChatTab> _tabs = new List<ChatTab>();
        private VisualElement _tabBar; private Button _nearby; private Label _heading;
        public IReadOnlyList<ChatTab> Tabs => _tabs;
        /// <summary>The conversation on screen (null: the nearby chat).</summary>
        public ChatTab Active { get; private set; }
        /// <summary>Asks the platform for the messages of a conversation (the game answers with <see cref="Fill"/>).</summary>
        public Action<ChatTab> Load;
        /// <summary>Sends a message of a conversation; the answer says whether it went.</summary>
        public Action<ChatTab, string, Action<bool>> SendConversation;
        public ChatTab TabOf(Guid id, bool group) => _tabs.Find(t => t.Id == id && t.Group == group);

        /// <summary>Opens (or brings back) the tab of a conversation and reads its messages.</summary>
        public ChatTab Open(Guid id, bool group, string name, bool select = true)
        {
            var tab = TabOf(id, group);
            if (tab == null)
            {
                if (_tabs.Count >= MaxTabs) CloseTab(_tabs.Find(t => t != Active));
                tab = new ChatTab { Id = id, Group = group, Name = string.IsNullOrWhiteSpace(name) ? (group ? "Grupo" : "Mensagem") : name };
                var mine = tab; tab.Header = new VisualElement(); tab.Header.style.flexDirection = FlexDirection.Row; tab.Header.style.flexShrink = 1;
                tab.Choose = UiKit.Button(tab.Name, () => Select(mine)); tab.Choose.name = "tab-" + id; tab.Choose.style.flexShrink = 1; tab.Choose.style.maxWidth = 120; tab.Choose.style.overflow = Overflow.Hidden; tab.Choose.style.textOverflow = TextOverflow.Ellipsis; tab.Choose.style.whiteSpace = WhiteSpace.NoWrap; tab.Choose.style.marginRight = 0;
                tab.Close = UiKit.Button("×", () => CloseTab(mine)); tab.Close.tooltip = "Fechar a conversa"; tab.Close.style.paddingLeft = tab.Close.style.paddingRight = 5; tab.Close.style.marginLeft = 0;
                tab.Header.Add(tab.Choose); tab.Header.Add(tab.Close); _tabBar.Add(tab.Header);
                tab.Log = new ScrollView(ScrollViewMode.Vertical); tab.Log.AddToClassList("lv-chat-log"); tab.Log.style.flexGrow = 1; tab.Log.style.flexShrink = 1; tab.Log.style.minHeight = 0; tab.Log.style.display = DisplayStyle.None; Insert(IndexOf(_log) + 1, tab.Log); _tabs.Add(tab);
            }
            else if (!string.IsNullOrWhiteSpace(name)) tab.Name = name;
            if (select) Select(tab); else { ShowLogs(); Refresh(); }
            Load?.Invoke(tab); return tab;
        }

        /// <summary>Shows a conversation (null: the nearby chat).</summary>
        public void Select(ChatTab tab)
        {
            Active = tab; if (tab != null) tab.Unread = 0; ShowLogs(); Refresh(); ShowLog();
        }

        public void CloseTab(ChatTab tab)
        {
            if (tab == null || !_tabs.Remove(tab)) return; tab.Header.RemoveFromHierarchy(); tab.Log.RemoveFromHierarchy();
            if (Active == tab) Select(null); else Refresh();
        }

        /// <summary>Something arrived in a conversation: its tab appears (without taking the screen) and counts it when it is not the one being read.</summary>
        public ChatTab Arrived(Guid id, bool group, string name)
        {
            var tab = TabOf(id, group);
            if (tab == null) tab = Open(id, group, name, false); else Load?.Invoke(tab);
            if (Active != tab || Minimized) tab.Unread++;
            Refresh(); _lastMessage = Time.unscaledTime; ShowLog(); return tab;
        }

        /// <summary>Puts the messages of a conversation in its tab, oldest first.</summary>
        public void Fill(ChatTab tab, IEnumerable<(string Who, string Text, bool Mine, DateTimeOffset? Time)> lines)
        {
            if (tab == null || !_tabs.Contains(tab)) return; tab.Log.Clear(); tab.Shown.Clear();
            foreach (var line in lines)
            {
                var label = new Label { enableRichText = true }; label.AddToClassList("lv-chat-line"); label.style.fontSize = _settings.ChatFontSize;
                var when = ShowTime && line.Time is { } at ? "<color=#77829A>" + at.ToLocalTime().ToString("HH:mm") + "</color> " : "";
                label.text = when + "<color=" + (line.Mine ? "#FFC45C" : "#3FE3D3") + "><b>" + Escape(line.Who) + "</b></color>  " + Escape(line.Text); tab.Log.Add(label); tab.Shown.Add(line.Who + ": " + line.Text);
            }
            var log = tab.Log; schedule.Execute(() => log.scrollOffset = new Vector2(0, float.MaxValue / 2f)).StartingIn(30);
        }

        private void ShowLogs()
        {
            _log.style.display = !Minimized && Active == null ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var tab in _tabs) tab.Log.style.display = !Minimized && Active == tab ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Refresh()
        {
            _tabBar.style.display = _tabs.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None; _nearby.EnableInClassList("lv-btn-selected", Active == null);
            foreach (var tab in _tabs) { tab.Choose.text = tab.Unread > 0 ? tab.Name + " (" + tab.Unread + ")" : tab.Name; tab.Choose.EnableInClassList("lv-btn-selected", Active == tab); tab.Choose.EnableInClassList("lv-btn-secondary", tab.Unread > 0); tab.Choose.tooltip = (tab.Group ? "Grupo " : "Conversa com ") + tab.Name; }
            _heading.text = Active == null ? "Conversa por perto" : (Active.Group ? "Grupo: " : "Conversa com ") + Active.Name;
        }

        /// <summary>The button beside the field that says what was written.</summary>
        public readonly Button SendButton;

        public ChatPanel(UserSettings settings)
        {
            _settings = settings; name = "chat"; AddToClassList("lv-window"); AddToClassList("lv-chat"); style.width = 420; style.height = OpenHeight; pickingMode = PickingMode.Position;
            var title = _title = new VisualElement { name = "chat-title" }; title.AddToClassList("lv-window-title"); var mark = new Icon("chat", 16); mark.style.marginRight = 7; title.Add(mark); _heading = UiKit.Text("Conversa por perto", "lv-title-text"); title.Add(_heading);
            _minimize = UiKit.Button("", () => SetMinimized(!Minimized), "lv-window-button"); _minimize.tooltip = "Minimizar o chat"; _minimize.name = "chat-minimize"; _minimize.Add(new Icon("minus", 13)); title.Add(_minimize); Add(title);
            _tabBar = new VisualElement { name = "chat-tabs" }; _tabBar.AddToClassList("lv-tabs"); _tabBar.AddToClassList("lv-chat-tabs"); _tabBar.style.flexWrap = Wrap.NoWrap; _tabBar.style.display = DisplayStyle.None;
            _nearby = UiKit.Button("Por perto", () => Select(null)); _nearby.name = "tab-nearby"; _nearby.AddToClassList("lv-btn-selected"); _tabBar.Add(_nearby); Add(_tabBar);
            _log = new ScrollView(ScrollViewMode.Vertical); _log.AddToClassList("lv-chat-log"); _log.style.flexGrow = 1; _log.style.flexShrink = 1; _log.style.minHeight = 0; _log.verticalScrollerVisibility = ScrollerVisibility.Auto; Add(_log);
            var entry = new VisualElement { name = "chat-entry" }; entry.AddToClassList("lv-chat-entry");
            _input = UiKit.Field("", false, MaxLength); _input.textEdition.placeholder = "Escreva e aperte Enter…"; entry.Add(_input);
            SendButton = UiKit.Button("", () => Submit(), "lv-btn-primary"); SendButton.name = "chat-send"; SendButton.tooltip = "Enviar"; SendButton.style.width = 36; SendButton.style.paddingLeft = SendButton.style.paddingRight = 0; SendButton.style.alignItems = Align.Center; SendButton.style.justifyContent = Justify.Center; SendButton.Add(new Icon("send", 15)); entry.Add(SendButton); Add(entry);
            _input.RegisterCallback<FocusInEvent>(e => SetTyping(true)); _input.RegisterCallback<FocusOutEvent>(e => SetTyping(false));
            _input.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            ApplySettings();
        }

        public void ApplySettings()
        {
            SetFrame(FrameShown);
            foreach (var child in _log.contentContainer.Children()) child.style.fontSize = _settings.ChatFontSize;
        }

        private void SetTyping(bool typing) { if (Typing == typing) return; Typing = typing; TypingChanged?.Invoke(typing); }

        private void OnKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Submit(); e.StopPropagation(); }
            else if (e.keyCode == KeyCode.Escape) { _input.Blur(); e.StopPropagation(); }
        }

        /// <summary>The text as it will be sent: control characters out, spaces trimmed, cut at the limit. Null when nothing is left.</summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var builder = new System.Text.StringBuilder(text.Length);
            foreach (var c in text) if (!char.IsControl(c) || c == ' ') builder.Append(c);
            var clean = builder.ToString().Trim(); if (clean.Length == 0) return null;
            return clean.Length > MaxLength ? clean.Substring(0, MaxLength) : clean;
        }

        public bool Submit()
        {
            var text = Clean(_input.value); if (text == null) return false;
            if (Active != null)
            {
                var tab = Active; var typed = _input.value; if (SendConversation == null) return false;
                _input.value = ""; SendConversation(tab, text, sent => { if (sent) Load?.Invoke(tab); else if (_input.value.Length == 0) _input.value = typed; });
                return true;
            }
            if (Send != null && !Send(text)) { AddSystem("Sem conexão com a ilha: a mensagem não foi enviada."); return false; }
            _input.value = ""; return true;
        }

        public void Add(int peer, string name, string text, DateTimeOffset time) => Append(new ChatLine(peer, name, Clean(text) ?? "", time));
        public void AddSystem(string text) => Append(new ChatLine(-1, "", text, DateTimeOffset.Now, true));

        private void Append(ChatLine line)
        {
            if (line.Text.Length == 0) return;
            _lines.Add(line); if (_lines.Count > MaxLines) { _lines.RemoveAt(0); if (_log.contentContainer.childCount > 0) _log.contentContainer.RemoveAt(0); }
            var label = new Label(); label.AddToClassList("lv-chat-line"); label.style.fontSize = _settings.ChatFontSize; label.enableRichText = true;
            var when = line.Time.ToLocalTime().ToString("HH:mm"); _lastMessage = Time.unscaledTime; ShowLog();
            label.text = line.System ? "<color=#A6B1C4><i>" + Escape(line.Text) + "</i></color>" : (ShowTime ? "<color=#77829A>" + when + "</color> " : "") + "<color=#3FE3D3><b>" + Escape(line.Name) + "</b></color>  " + Escape(line.Text);
            _log.Add(label); schedule.Execute(() => _log.scrollOffset = new Vector2(0, float.MaxValue / 2f)).StartingIn(30);
        }

        private static string Escape(string text) => text.Replace("<", "‹").Replace(">", "›");

        /// <summary>Focuses the field (the Enter key of the game opens the chat).</summary>
        public void Focus() => _input.Focus();
    }
}
