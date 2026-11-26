using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    public sealed record ChatLine(int PeerId, string Name, string Text, DateTimeOffset Time, bool System = false);

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
        private readonly Button _minimize;
        /// <summary>Minimized, the chat keeps only the line to write in (the messages stay and come back).</summary>
        public bool Minimized { get; private set; }
        public void SetMinimized(bool minimized) { Minimized = minimized; _log.style.display = minimized ? DisplayStyle.None : DisplayStyle.Flex; style.height = minimized ? 62 : 230; _minimize.text = minimized ? "+" : "–"; _minimize.tooltip = minimized ? "Mostrar as mensagens" : "Minimizar o chat"; }

        public ChatPanel(UserSettings settings)
        {
            _settings = settings; name = "chat"; AddToClassList("lv-glass"); style.width = 420; style.height = 230; style.paddingBottom = 6; pickingMode = PickingMode.Position;
            _minimize = UiKit.Button("–", () => SetMinimized(!Minimized), "lv-btn-flat"); _minimize.tooltip = "Minimizar o chat"; _minimize.style.position = Position.Absolute; _minimize.style.right = 4; _minimize.style.top = 2; _minimize.style.width = 26; _minimize.style.height = 22;
            _log = new ScrollView(ScrollViewMode.Vertical); _log.style.flexGrow = 1; _log.verticalScrollerVisibility = ScrollerVisibility.Auto; Add(_log); Add(_minimize);
            _input = UiKit.Field("", false, MaxLength); _input.style.marginTop = 4; Add(_input);
            _input.RegisterCallback<FocusInEvent>(e => SetTyping(true)); _input.RegisterCallback<FocusOutEvent>(e => SetTyping(false));
            _input.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            ApplySettings();
        }

        public void ApplySettings()
        {
            style.backgroundColor = new Color(27f / 255f, 38f / 255f, 46f / 255f, Mathf.Clamp(_settings.ChatOpacity, .3f, 1f) * .9f);
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
            var when = line.Time.ToLocalTime().ToString("HH:mm");
            label.text = line.System ? "<color=#A9B3BD><i>" + Escape(line.Text) + "</i></color>" : "<color=#7B858C>" + when + "</color> <color=#2EE6D0><b>" + Escape(line.Name) + "</b></color>  " + Escape(line.Text);
            _log.Add(label); schedule.Execute(() => _log.scrollOffset = new Vector2(0, float.MaxValue / 2f)).StartingIn(30);
        }

        private static string Escape(string text) => text.Replace("<", "‹").Replace(">", "›");

        /// <summary>Focuses the field (the Enter key of the game opens the chat).</summary>
        public void Focus() => _input.Focus();
    }
}
