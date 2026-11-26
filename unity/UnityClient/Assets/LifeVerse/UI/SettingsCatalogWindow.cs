using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// "Configurações", as in the Godot client: the 18 categories of the catalog on the side, a search over all of them, each setting with its help, the ones the Unity client cannot do yet shown disabled with the
    /// reason, "Restaurar o padrão" per category, and the profiles (save every value under a name, switch, delete) with "Restaurar tudo". A change applies at once and is saved. A change of the screen (mode,
    /// resolution, monitor) asks to be kept and goes back by itself after 15 seconds. The shortcut keys, the microphone test and the account sync stay in the classic window, opened from Perfis.
    /// </summary>
    public sealed class SettingsCatalogWindow : SectionWindow
    {
        public const float ScreenSeconds = 15f;
        private readonly SettingsStore _store; private readonly Action _applied; private readonly Func<string, string> _info; private readonly IReadOnlyDictionary<string, Action> _actions; private readonly Action _classic;
        private string _search = ""; private IVisualElementScheduledItem _revert; private Dictionary<string, string> _screenBefore;
        /// <summary>The control of each setting on the page (for the tests).</summary>
        public readonly Dictionary<string, VisualElement> Controls = new Dictionary<string, VisualElement>();
        public bool ScreenPending => _screenBefore != null;

        public static (string Id, string Text)[] Menu => SettingsCatalog.Categories.Select(c => (c.Id, c.Label)).Append(("perfis", "Perfis e restaurar")).ToArray();

        public SettingsCatalogWindow(ApiContext context, SettingsStore store, Action applied, Func<string, string> info = null, IReadOnlyDictionary<string, Action> actions = null, Action classic = null)
            : base(context, "settings-catalog", "Configurações", Menu, 900f, 580f, new Vector2(190, 50), 210f)
        {
            _store = store; _applied = applied; _info = info ?? (_ => ""); _actions = actions ?? new Dictionary<string, Action>(); _classic = classic; Show("Graphics");
            // "Navegação por teclado": Tab and the arrows move the focus (the toolkit does), Enter activates, Esc closes, Ctrl + F goes to the search
            focusable = true; RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) { Close(); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.F && (e.ctrlKey || e.commandKey)) { FocusSearch(); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
        }

        /// <summary>Puts the keyboard in the search field (Ctrl + F).</summary>
        public void FocusSearch() { _searchField?.Focus(); }
        private TextField _searchField;

        protected override void Fill(string section)
        {
            Controls.Clear(); SearchBar();
            if (section == "perfis") { Profiles(); return; }
            if (section == "busca") { Results(); return; }
            var defs = SettingsCatalog.Of(section).ToList(); var working = defs.Count(d => d.IsAvailable);
            _summary.text = SettingsCatalog.CategoryLabel(section) + "  ·  " + defs.Count + " opções, " + working + " valem no Unity" + (_store.ActiveProfile.Length > 0 ? "  ·  perfil: " + _store.ActiveProfile : "");
            foreach (var tab in defs.Select(d => d.Tab).Distinct()) { Title(tab); foreach (var d in defs.Where(d => d.Tab == tab)) Draw(d); }
            var name = SettingsCatalog.CategoryLabel(section);
            Buttons(Sure("Restaurar o padrão desta categoria", () => "Voltar " + name + " ao padrão?", () => { var n = _store.Restore(section); Applied(); Show(section); Say(n == 0 ? "Já estava tudo no padrão." : n + " opção(ões) voltaram ao padrão.", false); }));
        }

        private void SearchBar()
        {
            var field = Field("Buscar nas configurações", _search, 60); _searchField = field;
            field.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Find(field.value); }, TrickleDown.TrickleDown);
            Buttons(Btn("Buscar", () => Find(field.value)));
        }

        /// <summary>Shows every setting that matches the words, from any category.</summary>
        public void Find(string text) { _search = (text ?? "").Trim(); if (_search.Length == 0) { Show("Graphics"); return; } Show("busca"); }

        private void Results()
        {
            var found = SettingsCatalog.Search(_search); _summary.text = found.Count + " opção(ões) para “" + _search + "”";
            foreach (var category in found.Select(d => d.Category).Distinct()) { Title(SettingsCatalog.CategoryLabel(category)); foreach (var d in found.Where(d => d.Category == category)) Draw(d); }
            if (found.Count == 0) Line("Nada encontrado. Tente outra palavra (por exemplo: sombras, volume, câmera, chat).", "lv-small", "lv-muted");
        }

        // ---- one setting ----

        private void Draw(SettingDef d)
        {
            var box = new VisualElement(); box.AddToClassList("lv-row"); box.style.flexDirection = FlexDirection.Column; box.style.marginBottom = 4;
            VisualElement control = null; var value = _store.Get(d.Id);
            switch (d.Kind)
            {
                case "Bool": { var t = new Toggle(d.Label) { value = value == "true" }; t.RegisterValueChangedCallback(e => Change(d.Id, e.newValue ? "true" : "false")); control = t; Toggles[d.Label] = t; break; }
                case "Int":
                case "Float":
                {
                    var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center;
                    var slider = new Slider(d.Label, (float)d.Min, (float)d.Max) { value = SettingsCatalog.F(value) }; slider.style.flexGrow = 1;
                    var shown = UiKit.Text(Show(d, value), "lv-small"); shown.style.minWidth = 70;
                    slider.RegisterValueChangedCallback(e => { var kept = Change(d.Id, SettingsCatalog.S(e.newValue)); if (kept.Length > 0) { slider.SetValueWithoutNotify(SettingsCatalog.F(kept)); shown.text = Show(d, kept); } });
                    row.Add(slider); row.Add(shown); control = row; Controls[d.Id + ":slider"] = slider; break;
                }
                case "Choice":
                {
                    var choices = d.Device ? SettingsEffects.Choices(d.Id) : d.Choices.ToList(); if (choices.Count == 0) choices.Add((value, value)); if (!choices.Any(c => c.Id == value)) choices.Add((value, value));
                    var drop = new DropdownField(d.Label, choices.Select(c => c.Label).ToList(), Math.Max(0, choices.FindIndex(c => c.Id == value))); drop.userData = choices;
                    drop.RegisterValueChangedCallback(_ => Change(d.Id, choices[Math.Max(0, drop.index)].Id)); control = drop; Choices[d.Label] = drop; break;
                }
                case "Text": { var f = UiKit.Field(d.Label, false, 64, value); f.RegisterCallback<FocusOutEvent>(_ => Change(d.Id, f.value)); control = f; Fields[d.Label] = f; break; }
                case "Info": { var v = _info(d.Id); control = UiKit.Row(UiKit.Text(d.Label, "lv-bold"), UiKit.Spacer(), UiKit.Text(v.Length > 0 ? v : "—", "lv-small")); break; }
                case "Action":
                {
                    Action run = () => { if (_actions.TryGetValue(d.Id, out var a)) { a(); Say(d.Label + ": feito.", false); } else Say("Isso ainda não existe no Unity.", true); };
                    control = UiKit.Row(UiKit.Text(d.Label, "lv-bold"), UiKit.Spacer(), d.Confirm != null ? (VisualElement)Sure(d.ButtonText, () => d.Confirm, run) : Btn(d.ButtonText, run)); break;
                }
            }
            if (control == null) return; Controls[d.Id] = control; box.Add(control);
            if (!d.IsAvailable) { control.SetEnabled(false); var why = UiKit.Text(d.Unavailable, "lv-small", "lv-bad"); why.style.whiteSpace = WhiteSpace.Normal; box.Add(why); }
            var help = UiKit.Text(d.Help + (d.RestartRequired ? " (vale depois de reiniciar o jogo)" : ""), "lv-small", "lv-muted"); help.style.whiteSpace = WhiteSpace.Normal; box.Add(help);
            _page.Add(box);
        }

        private static string Show(SettingDef d, string value) => (d.Kind == "Float" ? SettingsCatalog.F(value).ToString(d.Step > 0 && d.Step < .1 ? "0.00" : "0.#", CultureInfo.GetCultureInfo("pt-BR")) : value) + d.Unit;

        /// <summary>Changes a setting as the window does: keeps it, saves, applies it, and for the screen asks to keep it (or goes back after 15 s). Returns the value kept, or "" when the setting cannot change here.</summary>
        public string Change(string id, string value)
        {
            var d = SettingsCatalog.Find(id); if (d == null || !d.IsValue || !d.IsAvailable) return "";
            var screen = id.StartsWith("display.", StringComparison.Ordinal); if (screen && _screenBefore == null) _screenBefore = new Dictionary<string, string> { ["display.mode"] = _store.Get("display.mode"), ["display.resolution"] = _store.Get("display.resolution"), ["display.monitor"] = _store.Get("display.monitor") };
            var kept = _store.Set(id, value); Applied();
            if (screen) { SettingsEffects.Screen_(_store); AskToKeepScreen(); }
            else if (d.PresetControlled && _store.Get("graphics.preset") == "custom") Say("A predefinição gráfica agora é Personalizada.", false);
            return kept;
        }

        private void Applied() { _store.Settings.Clamped(); _applied?.Invoke(); }

        private void AskToKeepScreen()
        {
            Say("Manter a nova tela? Ela volta sozinha em " + (int)ScreenSeconds + " segundos.", false); _revert?.Pause(); _revert = schedule.Execute(RevertScreen); _revert.ExecuteLater((long)(ScreenSeconds * 1000));
            Actions["Manter a tela"] = KeepScreen; Actions["Voltar a tela"] = RevertScreen; _page.Insert(0, UiKit.Row(UiKit.Button("Manter a tela", KeepScreen, "lv-btn-primary"), UiKit.Button("Voltar a tela", RevertScreen)));
        }

        public void KeepScreen() { _revert?.Pause(); _screenBefore = null; Say("Tela mantida.", false); }

        public void RevertScreen()
        {
            if (_screenBefore == null) return; _revert?.Pause(); var before = _screenBefore; _screenBefore = null;
            foreach (var pair in before) _store.Set(pair.Key, pair.Value, false); Applied(); SettingsEffects.Screen_(_store); Show(Section == "busca" ? "Graphics" : Section); Say("A tela voltou ao que era.", false);
        }

        // ---- Perfis e restaurar ----

        private void Profiles()
        {
            _summary.text = "Perfis: " + _store.Profiles.Count + " de 10" + (_store.ActiveProfile.Length > 0 ? "  ·  em uso: " + _store.ActiveProfile : "");
            Title("Perfis de configuração"); Line("Um perfil guarda todas as opções de agora com um nome (por exemplo: Trabalho, Gravação, Desempenho). Use para trocar tudo de uma vez.", "lv-small", "lv-muted");
            var name = Field("Nome do perfil", _store.ActiveProfile, 40);
            Buttons(Btn("Salvar como perfil", () => { var problem = _store.SaveProfile(name.value); if (problem != null) { Say(problem, true); return; } Applied(); Show("perfis"); Say("Perfil " + name.value.Trim() + " salvo.", false); }, "lv-btn-primary"));
            foreach (var p in _store.Profiles)
            {
                var profile = p; var count = _store.Settings.Profiles.TryGetValue(p, out var values) ? values.Count : 0;
                var r = UiKit.Row(UiKit.Text(p + (p == _store.ActiveProfile ? " (em uso)" : ""), "lv-bold"), UiKit.Text(count + " opções", "lv-small", "lv-muted"), UiKit.Spacer(),
                    Btn("Usar " + p, () => { _store.LoadProfile(profile); Applied(); Show("perfis"); Say("Perfil " + profile + " em uso.", false); }), Sure("Apagar " + p, () => "Apagar o perfil " + profile + "?", () => { _store.DeleteProfile(profile); Applied(); Show("perfis"); }));
                r.AddToClassList("lv-row"); _page.Add(r);
            }
            if (_store.Profiles.Count == 0) Line("Nenhum perfil ainda.", "lv-small", "lv-muted");
            Title("Restaurar"); Buttons(Sure("Restaurar tudo", () => "Voltar todas as configurações ao padrão?", () => { var n = _store.Restore(); Applied(); Show("perfis"); Say(n + " opção(ões) voltaram ao padrão.", false); }));
            if (_classic != null) { Title("Teclas, microfone e conta"); Line("Os atalhos de teclado, o teste do microfone e a sincronização com a conta ficam na janela clássica.", "lv-small", "lv-muted"); Buttons(Btn("Abrir teclas, microfone e conta…", _classic)); }
        }
    }
}
