using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    public enum NoticeKind { Info, Good, Warn, Bad }

    public sealed record Notice(string Text, NoticeKind Kind, DateTimeOffset Time);

    /// <summary>
    /// Short messages for the person (someone arrived, the connection dropped, a purchase went through): they appear in a corner for a few seconds and are kept in a history of the last
    /// fifty with a count of the ones not read yet. The same message repeated within two seconds counts once. At most four are drawn at the same time.
    /// </summary>
    public sealed class NotificationCenter
    {
        public const int MaxHistory = 50, MaxVisible = 4;
        private readonly UiRoot _ui; private readonly List<Notice> _history = new List<Notice>(); private readonly Queue<VisualElement> _visible = new Queue<VisualElement>();
        private string _lastText; private DateTimeOffset _lastTime;
        public IReadOnlyList<Notice> History => _history;
        public int Unread { get; private set; }
        public event Action Changed;

        public NotificationCenter(UiRoot ui) { _ui = ui; var corner = _ui.ToastLayer; corner.style.alignItems = Align.FlexEnd; corner.style.justifyContent = Justify.FlexStart; corner.style.paddingTop = 70; corner.style.paddingRight = 18; }

        public Notice Show(string text, NoticeKind kind = NoticeKind.Info, float seconds = 5f)
        {
            var now = DateTimeOffset.Now; var notice = new Notice(text, kind, now);
            if (text == _lastText && (now - _lastTime).TotalSeconds < 2) return notice;
            _lastText = text; _lastTime = now; _history.Add(notice); if (_history.Count > MaxHistory) _history.RemoveAt(0); Unread++;
            var toast = new VisualElement(); toast.AddToClassList("lv-toast"); toast.AddToClassList(kind == NoticeKind.Good ? "lv-toast-good" : kind == NoticeKind.Warn ? "lv-toast-warn" : kind == NoticeKind.Bad ? "lv-toast-bad" : "lv-toast-info");
            var label = UiKit.Text(text); label.style.whiteSpace = WhiteSpace.Normal; toast.Add(label); toast.pickingMode = PickingMode.Ignore;
            _ui.ToastLayer.Add(toast); _visible.Enqueue(toast);
            while (_visible.Count > MaxVisible) _visible.Dequeue().RemoveFromHierarchy();
            toast.schedule.Execute(() => toast.RemoveFromHierarchy()).StartingIn((long)(Mathf.Max(1f, seconds) * 1000f));
            Changed?.Invoke(); return notice;
        }

        public void MarkRead() { if (Unread == 0) return; Unread = 0; Changed?.Invoke(); }

        public int VisibleCount => _ui.ToastLayer.childCount;
    }
}
