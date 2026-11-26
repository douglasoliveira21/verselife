using System;
using System.Linq;
using System.Collections.Generic;
using LifeVerse.Unity.Avatar;
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
            foreach (var name in new[] { "Gráficos", "Áudio", "Controles", "Interface", "Acessibilidade", "Conta" })
            {
                var tab = new VisualElement(); tab.style.display = DisplayStyle.None; Body.Add(tab); _tabs[name] = tab; var captured = name;
                var button = UiKit.Button(name, () => Show(captured)); _buttons[name] = button; tabBar.Add(button);
            }
            BuildGraphics(_tabs["Gráficos"]); BuildAudio(_tabs["Áudio"]); BuildControls(_tabs["Controles"]); BuildInterface(_tabs["Interface"]); BuildAccessibility(_tabs["Acessibilidade"]); BuildAccount(_tabs["Conta"]);
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
            _s.Clamped(); GraphicsQuality.Apply(_s); Ui.ApplySettings(); _s.Save(_path); Changed?.Invoke(); SchedulePush();
        }

        /// <summary>The sync of the account, when the game has one (set by the game).</summary>
        public AccountSync Sync { get; set; }
        private Label _syncStatus; private IVisualElementScheduledItem _push;
        public string SyncStatusText => _syncStatus?.text ?? "";

        /// <summary>A moment after the last change (sliders send many), the shared settings go to the account.</summary>
        private void SchedulePush() { if (Sync == null || !Sync.Enabled) return; _push?.Pause(); _push = schedule.Execute(() => { _ = Sync.PushAppliedAsync(); }); _push.ExecuteLater(1500); }

        private void BuildAccount(VisualElement tab)
        {
            Section(tab, "Sincronizar com a conta"); var info = UiKit.Text("Valem para todos os seus computadores: inverter o mouse, correr sempre, a opacidade e o tamanho do texto do chat, reduzir movimento e a primeira tecla de cada ação (andar, pular, correr, voar, gestos, construir, voz). Gráficos e som ficam só neste computador.", "lv-small", "lv-muted"); info.style.whiteSpace = WhiteSpace.Normal; tab.Add(info);
            _syncStatus = UiKit.Text("", "lv-small"); _syncStatus.name = "sync-status"; _syncStatus.style.whiteSpace = WhiteSpace.Normal; _syncStatus.style.marginTop = 6; tab.Add(_syncStatus);
            tab.Add(UiKit.Button("Ligar e usar as da conta", () => TurnOnFromAccount(), "lv-btn-primary")); tab.Add(UiKit.Button("Ligar e enviar as deste computador", () => TurnOnFromHere())); tab.Add(UiKit.Button("Desligar", () => { Sync?.TurnOff(); ShowSyncStatus(); }));
            ShowSyncStatus();
        }

        private void ShowSyncStatus() { if (_syncStatus != null) _syncStatus.text = Sync == null ? "Entre na conta para sincronizar." : Sync.Status.Length > 0 ? Sync.Status : Sync.Enabled ? "Conta: ligada (revisão " + Sync.Revision + ")." : "Local: as configurações valem só neste computador."; }

        public async void TurnOnFromAccount()
        {
            if (Sync == null) { ShowSyncStatus(); return; } _syncStatus.text = "Falando com a conta…"; var snapshot = await Sync.FetchAsync();
            if (snapshot.Result != SyncResult.Ok) { _syncStatus.text = "Conta: " + snapshot.Message; return; } Sync.UseAccount(snapshot); _s.Save(_path); Ui.ApplySettings(); GraphicsQuality.Apply(_s); Changed?.Invoke(); ShowSyncStatus();
        }

        public async void TurnOnFromHere()
        {
            if (Sync == null) { ShowSyncStatus(); return; } _syncStatus.text = "Falando com a conta…"; var snapshot = await Sync.FetchAsync();
            if (snapshot.Result != SyncResult.Ok) { _syncStatus.text = "Conta: " + snapshot.Message; return; }
            var answer = await Sync.PushAllAsync(snapshot.Revision); if (answer.Result != SyncResult.Ok) _syncStatus.text = "Conta: " + (answer.Message.Length > 0 ? answer.Message : "não foi possível enviar."); else _s.Save(_path); ShowSyncStatus();
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

        /// <summary>The microphone test the game provides (null outside the game).</summary>
        public LifeVerse.Unity.Audio.MicTest Mic { get; set; }
        private DropdownField _inputPicker; public DropdownField InputPicker => _inputPicker;
        private ProgressBar _micBar; private Label _micStatus; private Button _micButton; private IVisualElementScheduledItem _micTick;
        public ProgressBar MicBar => _micBar;
        public string MicStatus => _micStatus.text;

        public void ToggleMicTest()
        {
            if (Mic == null) { _micStatus.text = "O teste do microfone só funciona dentro do jogo."; return; }
            if (Mic.Active) { Mic.End(); _micTick?.Pause(); _micButton.text = "Testar microfone"; _micBar.value = 0f; _micStatus.text = ""; return; }
            if (!Mic.Begin()) { _micStatus.text = Mic.Problem; return; }
            _micButton.text = "Parar o teste"; _micStatus.text = "Fale: a barra mostra o quanto o microfone ouve (nada é gravado)."; _micTick = _micBar.schedule.Execute(() => { _micBar.value = Mic.Level; _micBar.EnableInClassList("lv-bad", Mic.Peak >= .92f); }).Every(50);
        }

        private void BuildAudio(VisualElement tab)
        {
            Section(tab, "Volumes");
            tab.Add(UiKit.Slide("Geral", 0f, 1f, _s.MasterVolume, v => { _s.MasterVolume = v; Touch(); })); tab.Add(UiKit.Slide("Música", 0f, 1f, _s.MusicVolume, v => { _s.MusicVolume = v; Touch(); }));
            tab.Add(UiKit.Slide("Efeitos", 0f, 1f, _s.EffectsVolume, v => { _s.EffectsVolume = v; Touch(); })); tab.Add(UiKit.Slide("Voz", 0f, 1f, _s.VoiceVolume, v => { _s.VoiceVolume = v; Touch(); }));
            tab.Add(UiKit.Slide("Ambiente", 0f, 1f, _s.AmbientVolume, v => { _s.AmbientVolume = v; Touch(); })); tab.Add(UiKit.Slide("Mídia", 0f, 1f, _s.MediaVolume, v => { _s.MediaVolume = v; Touch(); }));
            Section(tab, "Dispositivos"); var inputs = LifeVerse.Unity.Audio.MicDevices.Choices(); if (!inputs.Contains(_s.InputDevice)) inputs.Add(_s.InputDevice);
            _inputPicker = new DropdownField("Microfone", inputs.Select(LifeVerse.Unity.Audio.MicDevices.Label).ToList(), Mathf.Max(0, inputs.IndexOf(_s.InputDevice))); _inputPicker.RegisterValueChangedCallback(e => { var at = _inputPicker.index; if (at >= 0 && at < inputs.Count) { _s.InputDevice = inputs[at]; LifeVerse.Unity.Audio.MicDevices.Preferred = _s.InputDevice; Touch(); } }); tab.Add(_inputPicker);
            tab.Add(UiKit.Text("A saída de som segue o dispositivo padrão do sistema (o jogo não consegue escolher outro): mude no Windows e o jogo acompanha.", "lv-muted", "lv-small"));
            Section(tab, "Voz e música"); tab.Add(UiKit.Slide("Sensibilidade do microfone", .02f, .6f, _s.VoiceSensitivity, v => { _s.VoiceSensitivity = v; Touch(); }));
            tab.Add(UiKit.Check("Cancelar eco (não enviar enquanto você ouve os outros)", _s.EchoGuard, v => { _s.EchoGuard = v; Touch(); })); tab.Add(UiKit.Check("Tocar a música dos terrenos", _s.ParcelMusic, v => { _s.ParcelMusic = v; Touch(); })); tab.Add(UiKit.Check("Sons da interface (cliques, janelas, avisos)", _s.UiSounds, v => { _s.UiSounds = v; LifeVerse.Unity.Audio.UiSounds.Enabled = v; Touch(); }));
            tab.Add(UiKit.Text("Fale segurando a tecla de falar (em Controles); o botão Voz da barra de baixo mantém o microfone aberto.", "lv-muted", "lv-small"));
            _micBar = new ProgressBar { lowValue = 0f, highValue = 1f, title = "Nível do microfone" }; _micStatus = UiKit.Text("", "lv-small", "lv-muted"); _micButton = UiKit.Button("Testar microfone", ToggleMicTest); tab.Add(_micButton); tab.Add(_micBar); tab.Add(_micStatus);
        }

        private void BuildControls(VisualElement tab)
        {
            Section(tab, "Mouse e movimento");
            tab.Add(UiKit.Slide("Sensibilidade da câmera", .02f, .6f, _s.MouseSensitivity, v => { _s.MouseSensitivity = v; Touch(); }));
            tab.Add(UiKit.Check("Inverter o eixo vertical", _s.InvertY, v => { _s.InvertY = v; Touch(); })); tab.Add(UiKit.Check("Correr sempre (Shift anda)", _s.AlwaysRun, v => { _s.AlwaysRun = v; Touch(); }));
            Section(tab, "Teclas"); tab.Add(UiKit.Text("Clique numa tecla e aperte a nova. Se ela já era de outra ação, as duas trocam. Esc cancela. As setas também andam.", "lv-muted", "lv-small"));
            foreach (var action in KeyBindings.All) { var captured = action; var button = UiKit.Button(KeyBindings.KeyName(KeyBindings.Of(action)), () => BeginRebind(captured)); button.style.minWidth = 130; _keyButtons[action] = button; var label = UiKit.Text(KeyBindings.Label(action), "lv-body"); label.style.flexGrow = 1; tab.Add(UiKit.Row(label, button)); }
            _keyStatus = UiKit.Text("", "lv-small", "lv-muted"); tab.Add(_keyStatus); tab.Add(UiKit.Button("Restaurar as teclas padrão", () => { KeyBindings.Reset(); SaveKeys(); RefreshKeys(); _keyStatus.text = "Teclas padrão restauradas."; }));
        }

        private readonly Dictionary<LifeVerse.Unity.Avatar.GameAction, Button> _keyButtons = new Dictionary<LifeVerse.Unity.Avatar.GameAction, Button>(); private Label _keyStatus; private IVisualElementScheduledItem _rebinding; private LifeVerse.Unity.Avatar.GameAction? _rebindingAction;
        public bool Rebinding => _rebindingAction.HasValue;
        public string KeyStatus => _keyStatus.text;
        public string KeyButtonText(LifeVerse.Unity.Avatar.GameAction action) => _keyButtons[action].text;

        public void BeginRebind(LifeVerse.Unity.Avatar.GameAction action)
        {
            CancelRebind(); _rebindingAction = action; _keyButtons[action].text = "Aperte uma tecla…"; _keyStatus.text = KeyBindings.Label(action) + ": aperte a nova tecla (Esc cancela).";
            _rebinding = _keyButtons[action].schedule.Execute(() =>
            {
                var board = UnityEngine.InputSystem.Keyboard.current; if (board == null || !_rebindingAction.HasValue) return;
                if (board.escapeKey.wasPressedThisFrame) { CancelRebind(); return; }
                foreach (var key in board.allKeys) if (key.wasPressedThisFrame) { ChooseKey(key.keyCode); return; }
            }).Every(20);
        }

        /// <summary>Gives the key to the action being changed (public so tests can press a key without a keyboard).</summary>
        public void ChooseKey(UnityEngine.InputSystem.Key key)
        {
            if (!_rebindingAction.HasValue) return; var action = _rebindingAction.Value;
            if (!KeyBindings.Set(action, key, out var swapped)) { _keyStatus.text = "Essa tecla não pode ser usada."; return; }
            _keyStatus.text = swapped.HasValue ? KeyBindings.Label(action) + " agora é " + KeyBindings.KeyName(key) + "; " + KeyBindings.Label(swapped.Value) + " ficou com " + KeyBindings.KeyName(KeyBindings.Of(swapped.Value)) + "." : KeyBindings.Label(action) + " agora é " + KeyBindings.KeyName(key) + ".";
            _rebinding?.Pause(); _rebindingAction = null; SaveKeys(); RefreshKeys();
        }

        public void CancelRebind() { _rebinding?.Pause(); _rebindingAction = null; RefreshKeys(); }
        private void RefreshKeys() { foreach (var pair in _keyButtons) pair.Value.text = KeyBindings.KeyName(KeyBindings.Of(pair.Key)); }
        private void SaveKeys() { _s.Keys = KeyBindings.Save(); Touch(); }

        private void BuildInterface(VisualElement tab)
        {
            Section(tab, "Tamanho");
            var scale = UiKit.Slide("Escala da interface", UserSettings.MinScale, UserSettings.MaxScale, _s.InterfaceScale, v => { _s.InterfaceScale = v; Touch(); }); scale.name = "scale"; scale.SetEnabled(!_s.AutoScale);
            tab.Add(UiKit.Check("Escala automática (pela altura da tela)", _s.AutoScale, v => { _s.AutoScale = v; scale.SetEnabled(!v); Touch(); })); tab.Add(scale);
            Section(tab, "Chat");
            tab.Add(UiKit.Slide("Opacidade do chat", .3f, 1f, _s.ChatOpacity, v => { _s.ChatOpacity = v; Touch(); }));
            tab.Add(UiKit.Slide("Tamanho do texto do chat", 10f, 28f, _s.ChatFontSize, v => { _s.ChatFontSize = Mathf.RoundToInt(v); Touch(); }));
            Section(tab, "Avisos na tela"); var toastInfo = UiKit.Text("O sino guarda todas as notificações; aqui você escolhe quais também aparecem como aviso na tela.", "lv-small", "lv-muted"); toastInfo.style.whiteSpace = WhiteSpace.Normal; tab.Add(toastInfo);
            tab.Add(UiKit.Check("Grupos (cargos, avisos, pedidos)", _s.ToastGroups, v => { _s.ToastGroups = v; Touch(); })); tab.Add(UiKit.Check("Convites", _s.ToastInvites, v => { _s.ToastInvites = v; Touch(); })); tab.Add(UiKit.Check("Sistema, amigos e mensagens", _s.ToastSystem, v => { _s.ToastSystem = v; Touch(); }));
        }

        private void BuildAccessibility(VisualElement tab)
        {
            Section(tab, "Acessibilidade");
            tab.Add(UiKit.Check("Reduzir movimento (sem balanço da câmera)", _s.ReduceMotion, v => { _s.ReduceMotion = v; Touch(); })); tab.Add(UiKit.Check("Texto maior na interface", _s.LargeText, v => { _s.LargeText = v; Touch(); }));
            tab.Add(UiKit.Check("Alto contraste", _s.HighContrast, v => { _s.HighContrast = v; Touch(); }));
        }
    }
}
