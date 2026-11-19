using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>A scrolling list filled from a GET of the platform API: each row of the answer becomes an element made by <c>make</c>; a line says when it is empty or what went wrong.</summary>
    public sealed class ApiList : VisualElement
    {
        private readonly ApiContext _c; private readonly Func<string> _path; private readonly Func<Row, VisualElement> _make; private readonly string _empty;
        private readonly ScrollView _scroll = new ScrollView(); private readonly Label _status = UiKit.Text("", "lv-small", "lv-muted");
        public int Count => _scroll.contentContainer.childCount;
        public int LastStatus { get; private set; }
        public event Action Loaded;

        public ApiList(ApiContext context, Func<string> path, Func<Row, VisualElement> make, string empty = "Nada por aqui.", bool loadNow = true)
        {
            _c = context; _path = path; _make = make; _empty = empty; style.flexGrow = 1; _scroll.style.flexGrow = 1; Add(_status); Add(_scroll); if (loadNow) Reload();
        }

        public void Reload()
        {
            var path = _path(); if (path == null) { _scroll.Clear(); _status.text = _empty; return; }
            this.Then(_c.Api.ApiAsync("GET", path), r =>
            {
                LastStatus = r.Status; _scroll.Clear();
                if (r.Status != 200) { _status.text = UiTasks.Explain(r.Status); _status.AddToClassList("lv-bad"); Loaded?.Invoke(); return; }
                _status.RemoveFromClassList("lv-bad"); var rows = Json.Rows(r.Body); _status.text = rows.Count == 0 ? _empty : "";
                foreach (var row in rows) { var element = _make(row); if (element != null) { element.AddToClassList("lv-row"); _scroll.Add(element); } }
                Loaded?.Invoke();
            });
        }
    }

    public static class Controls
    {
        /// <summary>A button that asks for a second touch before doing something that cannot be undone: the first touch says what will happen, the second does it; it goes back by itself after six seconds.</summary>
        public static Button Confirm(string text, Func<string> describe, Action run, string kind = "lv-btn")
        {
            Button button = null; var armed = false; IVisualElementScheduledItem timer = null;
            button = UiKit.Button(text, () =>
            {
                if (!armed) { armed = true; button.text = describe(); timer = button.schedule.Execute(() => { armed = false; button.text = text; }); timer.ExecuteLater(6000); return; }
                armed = false; timer?.Pause(); button.text = text; run();
            }, kind);
            return button;
        }

        /// <summary>A #RRGGBB field with a swatch beside it; <paramref name="onChange"/> is told when the text is a valid colour.</summary>
        public static VisualElement ColourField(string label, string value, Action<string> onChange, params string[] presets)
        {
            var field = UiKit.Field(label, false, 7, value); var swatch = new VisualElement(); swatch.style.width = 26; swatch.style.height = 26; swatch.style.borderTopLeftRadius = swatch.style.borderTopRightRadius = swatch.style.borderBottomLeftRadius = swatch.style.borderBottomRightRadius = 6; swatch.style.marginLeft = 6;
            void Show(string hex) { if (ColorUtility.TryParseHtmlString(hex, out var color)) swatch.style.backgroundColor = color; }
            Show(value); field.RegisterValueChangedCallback(e => { if (IsColour(e.newValue)) { Show(e.newValue); onChange(e.newValue); } });
            var row = UiKit.Row(field, swatch); field.style.flexGrow = 1; var holder = new VisualElement(); holder.Add(row);
            if (presets.Length > 0)
            {
                var chips = UiKit.Row(); foreach (var preset in presets) { var hex = preset; var chip = new Button(() => field.value = hex); chip.style.width = 22; chip.style.height = 22; chip.style.marginRight = 4; chip.style.borderTopLeftRadius = chip.style.borderTopRightRadius = chip.style.borderBottomLeftRadius = chip.style.borderBottomRightRadius = 11; chip.style.backgroundColor = ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.gray; chip.style.borderTopWidth = chip.style.borderBottomWidth = chip.style.borderLeftWidth = chip.style.borderRightWidth = 1; chip.style.borderTopColor = chip.style.borderBottomColor = chip.style.borderLeftColor = chip.style.borderRightColor = new Color(.57f, .59f, .61f); chips.Add(chip); }
                holder.Add(chips);
            }
            return holder;
        }

        public static bool IsColour(string value) => value != null && System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$");
    }
}
