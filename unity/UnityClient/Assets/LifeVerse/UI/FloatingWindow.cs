using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// A window floating over the game, as the Second Life viewer's floaters: a title bar you drag (with the icon of what the window is), buttons to minimize it to the bar and to close it,
    /// a corner to resize the windows that have a fixed size, a shadow around it and a body for whatever the window shows. It comes to the front when clicked, stays inside the screen,
    /// and remembers where the person left it (the position goes into <see cref="UserSettings.Windows"/> under the window's id, as the Godot windows do). Several can be open.
    /// </summary>
    public class FloatingWindow : VisualElement
    {
        /// <summary>The height of the bars at the top of the screen (a window opens under them) and of the toolbar at the bottom (a tall window ends above it).</summary>
        public const float TopInset = 74f, BottomInset = 68f, MinWidth = 260f, MinHeight = 170f;
        public string Id { get; }
        public VisualElement Body { get; }
        public Label Title { get; }
        /// <summary>Minimized, only the title bar shows; the contents stay as they were.</summary>
        public bool Minimized { get; private set; }
        public bool Resizable => _grip != null;
        public readonly Button MinimizeButton, CloseButton;
        private readonly VisualElement _bar, _grip; private Icon _icon;
        /// <summary>An icon before the title (the one of the toolbar button that opened it).</summary>
        public void SetIcon(string name) { if (_icon == null) { _icon = new Icon(name, 17) { name = "title-icon" }; _icon.style.marginRight = 8; _icon.style.alignSelf = Align.Center; _icon.style.flexShrink = 0; _bar.Insert(0, _icon); } else _icon.Name = name; }
        public string IconName => _icon?.Name;
        public event Action Closed;
        protected readonly UiRoot Ui; private Vector2 _grab; private bool _dragging; private StyleLength _height; private Vector2 _sizeFrom, _pointerFrom; private bool _sizing;

        public FloatingWindow(UiRoot ui, string id, string title, float width = 360f, float height = -1f, Vector2? defaultPosition = null)
        {
            Ui = ui; Id = id; AddToClassList("lv-window"); style.width = width; pickingMode = PickingMode.Position;
            var saved = ui.Settings.Windows.TryGetValue(id, out var kept);
            var start = saved ? new Vector2(kept[0], kept[1]) : defaultPosition ?? new Vector2(80, 80);
            if (!saved) start.y = Mathf.Max(start.y, TopInset);
            if (height > 0) style.height = height;
            var shadow = new VisualElement { name = "window-shadow", pickingMode = PickingMode.Ignore }; shadow.AddToClassList("lv-window-shadow"); Add(shadow);
            var bar = new VisualElement { name = "window-title" }; bar.AddToClassList("lv-window-title"); Title = UiKit.Text(title, "lv-title-text"); bar.Add(Title);
            MinimizeButton = WindowButton("minus", "Minimizar", "lv-window-button", () => SetMinimized(!Minimized)); MinimizeButton.name = "window-minimize"; bar.Add(MinimizeButton);
            CloseButton = WindowButton("close", "Fechar", "lv-close", Close); CloseButton.name = "window-close"; bar.Add(CloseButton); Add(bar); _bar = bar;
            Body = new VisualElement { name = "window-body" }; Body.AddToClassList("lv-window-body"); Add(Body);
            if (IconOf(id) is { } picture) SetIcon(picture);
            if (height > 0)
            {
                _grip = new VisualElement { name = "window-grip", tooltip = "Arraste para mudar o tamanho" }; _grip.AddToClassList("lv-window-grip"); Add(_grip);
                _grip.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; _sizing = true; _sizeFrom = new Vector2(resolvedStyle.width, resolvedStyle.height); _pointerFrom = e.position; _grip.CapturePointer(e.pointerId); e.StopPropagation(); });
                _grip.RegisterCallback<PointerMoveEvent>(e => { if (_sizing) Resize(_sizeFrom + ((Vector2)e.position - _pointerFrom)); });
                _grip.RegisterCallback<PointerUpEvent>(e => { if (!_sizing) return; _sizing = false; _grip.ReleasePointer(e.pointerId); });
            }
            style.left = start.x; style.top = start.y;
            RegisterCallback<PointerDownEvent>(e => Raise(), TrickleDown.TrickleDown);
            bar.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0 || OnButton(e.target as VisualElement)) return; if (e.clickCount == 2) { SetMinimized(!Minimized); return; } _dragging = true; _grab = new Vector2(e.position.x - resolvedStyle.left, e.position.y - resolvedStyle.top); bar.CapturePointer(e.pointerId); });
            bar.RegisterCallback<PointerMoveEvent>(e => { if (_dragging) MoveTo(new Vector2(e.position.x - _grab.x, e.position.y - _grab.y)); });
            bar.RegisterCallback<PointerUpEvent>(e => { if (!_dragging) return; _dragging = false; bar.ReleasePointer(e.pointerId); Remember(); });
        }

        private static readonly (string Start, string Icon)[] IconsById =
        {
            ("attachment", "pin"), ("avatar", "avatar"), ("build-texture", "texture"), ("build", "hammer"), ("camera", "camera"), ("character", "user"), ("discovery", "calendar"), ("file-picker", "folder-open"), ("gallery", "image"),
            ("global-search", "search"), ("search", "search"), ("group", "people"), ("help", "help"), ("icons", "grid"), ("inventory", "bag"), ("object-contents", "bag"), ("island", "settings"), ("land-management", "grid"), ("listing-offer", "coin"),
            ("lost-objects", "trash"), ("lua", "code"), ("script", "code"), ("map-marks", "pin"), ("market", "bag"), ("marriage", "star"), ("model-import", "download"), ("more", "dots"), ("move", "run"), ("my-properties", "home"),
            ("notification", "bell"), ("offers", "download"), ("parcel", "pin"), ("payment", "coin"), ("people", "people"), ("person", "user"), ("profile", "user"), ("purchase", "coin"), ("real-estate", "home"), ("settings", "settings"),
            ("social", "chat-dots"), ("teleport", "globe"), ("terrain", "mountain"), ("texture", "texture"), ("toolbar", "layers"), ("wallet", "coin"), ("world-map", "globe"),
        };

        /// <summary>The picture in the title of a window, by the beginning of its id (null when it has none).</summary>
        public static string IconOf(string id) { foreach (var (start, icon) in IconsById) if (id != null && id.StartsWith(start, StringComparison.Ordinal)) return icon; return null; }

        /// <summary>For the windows whose contents scroll: no taller than the room between the bars of the screen.</summary>
        protected void FitScreen()
        {
            var screen = Ui.WindowLayer.layout.height; if (screen > 400f && style.height.keyword == StyleKeyword.Undefined) style.height = Mathf.Min(style.height.value.value, Mathf.Max(MinHeight, screen - TopInset - BottomInset));
        }

        private static bool OnButton(VisualElement element) { for (; element != null; element = element.parent) if (element is Button) return true; return false; }

        private static Button WindowButton(string icon, string tip, string kind, Action run)
        {
            var button = new Button(() => { LifeVerse.Unity.Audio.UiSounds.Click(); run(); }) { tooltip = tip }; button.AddToClassList("lv-btn"); button.AddToClassList(kind); button.Add(new Icon(icon, 13) { name = icon == "close" ? "close-icon" : "minimize-icon" }); return button;
        }

        /// <summary>Folds the window to its title bar, or brings it back.</summary>
        public void SetMinimized(bool minimized)
        {
            if (Minimized == minimized) return; Minimized = minimized;
            if (minimized) { _height = style.height; style.height = StyleKeyword.Auto; } else style.height = _height;
            Body.style.display = minimized ? DisplayStyle.None : DisplayStyle.Flex; if (_grip != null) _grip.style.display = minimized ? DisplayStyle.None : DisplayStyle.Flex;
            if (MinimizeButton.Q<Icon>() is { } icon) icon.Name = minimized ? "expand" : "minus"; MinimizeButton.tooltip = minimized ? "Restaurar" : "Minimizar";
        }

        /// <summary>A new size, never smaller than the window can be used nor larger than the screen.</summary>
        public void Resize(Vector2 size)
        {
            var host = Ui.WindowLayer.layout; var maxW = host.width > 1 ? host.width : 1920f; var maxH = host.height > 1 ? host.height : 1080f;
            style.width = Mathf.Clamp(size.x, MinWidth, maxW); style.height = Mathf.Clamp(size.y, MinHeight, maxH);
        }

        /// <summary>Moves the window, keeping the title bar on the screen.</summary>
        public void MoveTo(Vector2 position)
        {
            var host = Ui.WindowLayer.layout; var maxX = Mathf.Max(0f, (host.width > 1 ? host.width : 1920f) - 80f); var maxY = Mathf.Max(0f, (host.height > 1 ? host.height : 1080f) - 40f);
            style.left = Mathf.Clamp(position.x, -resolvedStyle.width + 80f, maxX); style.top = Mathf.Clamp(position.y, 0f, maxY);
        }

        private void Remember()
        {
            if (Ui.Settings.Windows.Count >= UserSettings.MaxSavedWindows && !Ui.Settings.Windows.ContainsKey(Id)) return;
            Ui.Settings.Windows[Id] = new[] { resolvedStyle.left, resolvedStyle.top };
        }

        /// <summary>Brings the window over the others and gives its title the colour of the active one.</summary>
        public void Raise() { if (parent == null) return; BringToFront(); Mark(parent); }

        private static void Mark(VisualElement layer)
        {
            FloatingWindow front = null; foreach (var child in layer.Children()) if (child is FloatingWindow w) front = w;
            foreach (var child in layer.Children()) if (child is FloatingWindow w) w.EnableInClassList("lv-window-front", w == front);
        }

        public void Open() { if (parent == null) { Ui.WindowLayer.Add(this); LifeVerse.Unity.Audio.UiSounds.Play(LifeVerse.Unity.Audio.UiSound.Open); } Raise(); }

        public void Close() { var layer = parent; RemoveFromHierarchy(); Remember(); if (layer != null) { Mark(layer); LifeVerse.Unity.Audio.UiSounds.Play(LifeVerse.Unity.Audio.UiSound.Close); } Closed?.Invoke(); }

        public bool IsOpen => parent != null;
    }
}
