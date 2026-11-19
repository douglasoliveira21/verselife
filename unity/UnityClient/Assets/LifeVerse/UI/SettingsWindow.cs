using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The settings window, in five tabs: Gráficos (preset Baixo/Médio/Alto/Ultra, vsync, frame limit), Áudio (four volumes), Controles (mouse sensitivity, invert Y, always run),
    /// Interface (automatic or chosen scale, chat opacity and text size) and Acessibilidade (reduce motion, larger text, high contrast). Every change is applied at once to the engine
    /// and the interface and saved to the settings file; <see cref="Changed"/> tells the rest of the game (the audio mixer, the camera).
    /// </summary>
    public sealed class SettingsWindow : FloatingWindow
    {
        private readonly UserSettings _s; private readonly string _path;
        private readonly Dictionary<string, VisualElement> _tabs = new Dictionary<string, VisualElement>(); private readonly Dictionary<string, Button> _buttons = new Dictionary<string, Button>();
        public event Action Changed;
        public string CurrentTab { get; private set; }

        public SettingsWindow(UiRoot ui, string path = null) : base(ui, "settings", "Configurações", 460f, -1f, new Vector2(300, 120))
        {
            _s = ui.Settings; _path = path;
            var tabBar = UiKit.Row(); tabBar.style.flexWrap = Wrap.Wrap; tabBar.style.marginBottom = 8; Body.Add(tabBar);
            foreach (var name in new[] { "Gráficos", "Áudio", "Controles", "Interface", "Acessibilidade" })
            {
                var tab = new VisualElement(); tab.style.display = DisplayStyle.None; Body.Add(tab); _tabs[name] = tab; var captured = name;
                var button = UiKit.Button(name, () => Show(captured)); _buttons[name] = button; tabBar.Add(button);
            }
            BuildGraphics(_tabs["Gráficos"]); BuildAudio(_tabs["Áudio"]); BuildControls(_tabs["Controles"]); BuildInterface(_tabs["Interface"]); BuildAccessibility(_tabs["Acessibilidade"]);
            Show("Gráficos");
        }

        public void Show(string tab)
        {
            if (!_tabs.ContainsKey(tab)) return; CurrentTab = tab;
            foreach (var pair in _tabs) pair.Value.style.display = pair.Key == tab ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var pair in _buttons) pair.Value.EnableInClassList("lv-btn-selected", pair.Key == tab);
        }

        /// <summary>A setting was changed by the person: apply, save, tell.</summary>
        private void Touch()
        {
            _s.Clamped(); GraphicsQuality.Apply(_s); Ui.ApplySettings(); _s.Save(_path); Changed?.Invoke();
        }

        private static VisualElement Section(VisualElement tab, string title) { var label = UiKit.Text(title, "lv-bold", "lv-accent"); label.style.marginTop = 6; tab.Add(label); return tab; }

        private void BuildGraphics(VisualElement tab)
        {
            Section(tab, "Qualidade");
            var preset = new DropdownField("Preset", new List<string>(UserSettings.PresetNames), _s.GraphicsPreset) { name = "preset" }; preset.RegisterValueChangedCallback(e => { _s.GraphicsPreset = preset.index; Touch(); }); tab.Add(preset);
            tab.Add(UiKit.Text("Muda o alcance do detalhe dos modelos, a nitidez das texturas, as sombras e a chuva.", "lv-muted", "lv-small"));
            tab.Add(UiKit.Check("Sincronia vertical (vsync)", _s.VSync, v => { _s.VSync = v; Touch(); }));
            var limit = new DropdownField("Limite de quadros", new List<string> { "Sem limite", "30", "60", "120", "144", "240" }, FpsIndex(_s.FpsLimit)) { name = "fps" };
            limit.RegisterValueChangedCallback(e => { _s.FpsLimit = limit.index == 0 ? 0 : int.Parse(limit.value); Touch(); }); tab.Add(limit);
        }

        private static int FpsIndex(int fps) => fps == 30 ? 1 : fps == 60 ? 2 : fps == 120 ? 3 : fps == 144 ? 4 : fps == 240 ? 5 : 0;

        private void BuildAudio(VisualElement tab)
        {
            Section(tab, "Volumes");
            tab.Add(UiKit.Slide("Geral", 0f, 1f, _s.MasterVolume, v => { _s.MasterVolume = v; Touch(); })); tab.Add(UiKit.Slide("Música", 0f, 1f, _s.MusicVolume, v => { _s.MusicVolume = v; Touch(); }));
            tab.Add(UiKit.Slide("Efeitos", 0f, 1f, _s.EffectsVolume, v => { _s.EffectsVolume = v; Touch(); })); tab.Add(UiKit.Slide("Voz", 0f, 1f, _s.VoiceVolume, v => { _s.VoiceVolume = v; Touch(); }));
        }

        private void BuildControls(VisualElement tab)
        {
            Section(tab, "Mouse e movimento");
            tab.Add(UiKit.Slide("Sensibilidade da câmera", .02f, .6f, _s.MouseSensitivity, v => { _s.MouseSensitivity = v; Touch(); }));
            tab.Add(UiKit.Check("Inverter o eixo vertical", _s.InvertY, v => { _s.InvertY = v; Touch(); })); tab.Add(UiKit.Check("Correr sempre (Shift anda)", _s.AlwaysRun, v => { _s.AlwaysRun = v; Touch(); }));
            tab.Add(UiKit.Text("W A S D andam · Shift corre · Espaço pula · F voa · C desce · X senta · E carro · V primeira pessoa · G H J K L N gestos · Enter abre o chat.", "lv-muted", "lv-small"));
        }

        private void BuildInterface(VisualElement tab)
        {
            Section(tab, "Tamanho");
            var scale = UiKit.Slide("Escala da interface", UserSettings.MinScale, UserSettings.MaxScale, _s.InterfaceScale, v => { _s.InterfaceScale = v; Touch(); }); scale.name = "scale"; scale.SetEnabled(!_s.AutoScale);
            tab.Add(UiKit.Check("Escala automática (pela altura da tela)", _s.AutoScale, v => { _s.AutoScale = v; scale.SetEnabled(!v); Touch(); })); tab.Add(scale);
            Section(tab, "Chat");
            tab.Add(UiKit.Slide("Opacidade do chat", .3f, 1f, _s.ChatOpacity, v => { _s.ChatOpacity = v; Touch(); }));
            tab.Add(UiKit.Slide("Tamanho do texto do chat", 10f, 28f, _s.ChatFontSize, v => { _s.ChatFontSize = Mathf.RoundToInt(v); Touch(); }));
        }

        private void BuildAccessibility(VisualElement tab)
        {
            Section(tab, "Acessibilidade");
            tab.Add(UiKit.Check("Reduzir movimento (sem balanço da câmera)", _s.ReduceMotion, v => { _s.ReduceMotion = v; Touch(); })); tab.Add(UiKit.Check("Texto maior na interface", _s.LargeText, v => { _s.LargeText = v; Touch(); }));
            tab.Add(UiKit.Check("Alto contraste", _s.HighContrast, v => { _s.HighContrast = v; Touch(); }));
        }
    }
}
