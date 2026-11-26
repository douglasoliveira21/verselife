using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The root of the whole interface: one UI Toolkit document over the game with the theme of Theme.uss (Inter font, the textures of Skin) and layers from back to front: the
    /// screens (login, loading), the HUD, the floating windows, the notifications and the hints. It draws at the scale of <see cref="UserSettings"/> (automatic from the screen height, or
    /// chosen). The layers let the mouse through where nothing is drawn, so the world can still be clicked.
    /// </summary>
    public sealed class UiRoot : MonoBehaviour
    {
        public UserSettings Settings { get; private set; } = new UserSettings();
        public VisualElement Root { get; private set; }
        public VisualElement ScreenLayer { get; private set; }
        public VisualElement HudLayer { get; private set; }
        public VisualElement WindowLayer { get; private set; }
        public VisualElement ToastLayer { get; private set; }
        public VisualElement HintLayer { get; private set; }
        public Tooltips Hints { get; private set; }
        public UIDocument Document { get; private set; }
        public PanelSettings Panel { get; private set; }

        public static UiRoot Create(UserSettings settings)
        {
            var go = new GameObject("Interface"); var root = go.AddComponent<UiRoot>(); root.Settings = settings ?? new UserSettings(); root.Build(); return root;
        }

        private static VisualElement Layer(string name)
        {
            var layer = new VisualElement { name = name, pickingMode = PickingMode.Ignore }; layer.style.position = Position.Absolute; layer.style.left = layer.style.top = layer.style.right = layer.style.bottom = 0; layer.AddToClassList("lv-passthrough"); return layer;
        }

        private void Build()
        {
            Panel = ScriptableObject.CreateInstance<PanelSettings>(); Panel.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            Panel.scaleMode = PanelScaleMode.ConstantPixelSize; Panel.sortingOrder = 100; Panel.clearColor = false;
            Document = gameObject.AddComponent<UIDocument>(); Document.panelSettings = Panel;
            Root = Document.rootVisualElement; Root.AddToClassList("lv-root"); Root.pickingMode = PickingMode.Ignore; Root.style.flexGrow = 1;
            var theme = Resources.Load<StyleSheet>("LifeVerse/UI/Theme"); if (theme != null) Root.styleSheets.Add(theme); else Debug.LogWarning("[LifeVerse] Theme.uss não encontrado");
            // the open list of a choice is added to the panel itself, outside the root: the theme goes there too
            if (theme != null) Root.RegisterCallback<AttachToPanelEvent>(e => Cover(theme)); Cover(theme);
            ScreenLayer = Layer("screens"); HudLayer = Layer("hud"); WindowLayer = Layer("windows"); ToastLayer = Layer("toasts"); HintLayer = Layer("hints");
            Root.Add(ScreenLayer); Root.Add(HudLayer); Root.Add(WindowLayer); Root.Add(ToastLayer); Root.Add(HintLayer); Hints = new Tooltips(Root, HintLayer);
            ApplySettings();
        }

        private void Cover(StyleSheet theme) { var top = Root?.panel?.visualTree; if (theme != null && top != null && !top.styleSheets.Contains(theme)) top.styleSheets.Add(theme); }

        /// <summary>Puts the scale and the accessibility choices on the interface (called when the settings change).</summary>
        public void ApplySettings()
        {
            if (Panel == null) return;
            Panel.scale = Settings.EffectiveScale(Screen.height);
            Root.EnableInClassList("lv-high-contrast", Settings.HighContrast); Root.EnableInClassList("lv-reduce-motion", Settings.ReduceMotion);
            if (Settings.HighContrast) Root.style.color = Color.white; else Root.style.color = StyleKeyword.Null;
        }

        private void OnDestroy() { if (Panel != null) { if (Panel.themeStyleSheet != null) Destroy(Panel.themeStyleSheet); Destroy(Panel); } }
    }
}
