using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>
/// The Ambiente tab: the look of the whole island (hour, weather, fog, light, sky colour) that the game shows wherever a parcel does not set its own. Each setting has a switch: off means the game's
/// own value. Nothing changes for anybody until "Aplicar na Ilha"; then the people already there see it within about 30 seconds. Looks can be saved by name and reused.
/// </summary>
public partial class IslandEnvironmentPage : ScrollContainer
{
    private static readonly CultureInfo Pt = new("pt-BR");
    private static readonly (string Id, string Text)[] Weathers = [("clear", "Limpo"), ("cloudy", "Nublado"), ("rain", "Chuva"), ("storm", "Tempestade"), ("fog", "Neblina")];

    private sealed record Preset(string Key, string Title, string Look, bool Custom);

    private IslandToggle _useHour = null!, _useLight = null!, _useWeather = null!, _useFog = null!, _useSky = null!;
    private HSlider _hour = null!, _light = null!, _fog = null!;
    private Label _hourText = null!, _lightText = null!, _fogText = null!, _status = null!, _dirty = null!, _effect = null!;
    private OptionButton _weather = null!, _presets = null!;
    private ColorPickerButton _sky = null!;
    private LineEdit _presetName = null!;
    private Button _apply = null!, _revert = null!, _reset = null!, _useButton = null!, _saveButton = null!, _deleteButton = null!;
    private IslandManagementApi? _api;
    private Guid _island;
    private DateTimeOffset _updatedAt;
    private string _loadedLook = "{}";
    private List<Preset> _all = [];
    private bool _filling;

    public event Action<string>? Notify;
    public Action<string, string, Action>? Ask { get; set; }

    public bool IsDirty => DraftJson != _loadedLook;
    public string DraftJson => BuildDraft().ToJsonString();
    public string LoadedForTesting => _loadedLook;
    public string StatusForTesting => _status.Text;
    public string EffectForTesting => _effect.Text;
    public bool ApplyEnabledForTesting => !_apply.Disabled;
    public IReadOnlyList<string> PresetNamesForTesting => [.. _all.Select(p => p.Title)];
    public void SetHourForTesting(bool use, double value) { _useHour.ButtonPressed = use; _hour.Value = value; }
    public void SetLightForTesting(bool use, double value) { _useLight.ButtonPressed = use; _light.Value = value; }
    public void SetFogForTesting(bool use, double value) { _useFog.ButtonPressed = use; _fog.Value = value; }
    public void SetWeatherForTesting(bool use, int index) { _useWeather.ButtonPressed = use; _weather.Select(index); Changed(); }
    public void SetSkyForTesting(bool use, Color color) { _useSky.ButtonPressed = use; _sky.Color = color; Changed(); }
    public void ChoosePresetForTesting(string title) { for (var i = 0; i < _presets.ItemCount; i++) if (_presets.GetItemText(i) == title) _presets.Select(i); UpdatePresetButtons(); }
    public void LoadPresetForTesting() => LoadPreset();
    public string PresetNameForTesting { get => _presetName.Text; set => _presetName.Text = value; }
    public Task ApplyForTesting() => ApplyAsync();
    public Task SavePresetForTesting() => SavePresetAsync();
    public Task DeletePresetForTesting() => DeletePresetAsync();
    public Task ResetForTesting() => ApplyAsync(clear: true);
    public void RevertForTesting() => Revert();

    public void Bind(IslandManagementApi api) => _api = api;
    public void SetIsland(Guid island) => _island = island;

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Auto; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        var column = IslandUi.Column(14); column.SizeFlagsHorizontal = SizeFlags.ExpandFill; AddChild(column);
        var top = IslandUi.Row(14); top.SizeFlagsHorizontal = SizeFlags.ExpandFill; column.AddChild(top);
        top.AddChild(BuildLighting()); top.AddChild(BuildAtmosphere());
        column.AddChild(BuildPresets());
        var foot = IslandUi.Row(10); column.AddChild(foot);
        _dirty = IslandUi.Text("", 14, IslandUi.AccentSecondary, true); foot.AddChild(_dirty);
        _status = IslandUi.Text("", 14, IslandUi.TextMuted, false, true); _status.SizeFlagsHorizontal = SizeFlags.ExpandFill; foot.AddChild(_status);
        _revert = IslandUi.Action("Desfazer alterações", null, IslandUi.Kind.Secondary, 42); _revert.Pressed += Revert; foot.AddChild(_revert);
        _reset = IslandUi.Action("Voltar ao padrão do jogo", "reset", IslandUi.Kind.Danger, 42); _reset.Pressed += () => Confirm("Tirar o ambiente da ilha e voltar ao céu padrão do jogo para todos? As predefinições salvas continuam.", "Voltar ao padrão", () => _ = ApplyAsync(clear: true)); foot.AddChild(_reset);
        _apply = IslandUi.Action("Aplicar na Ilha", "ok", IslandUi.Kind.Primary, 42); _apply.CustomMinimumSize = new Vector2(200, 42); _apply.Pressed += () => Confirm("Aplicar este ambiente na ilha? As pessoas que estão nela passam a ver em até 30 segundos.", "Aplicar", () => _ = ApplyAsync()); foot.AddChild(_apply);
        Changed();
    }

    // ---- cards ------------------------------------------------------------------------------------------------------------

    private Control BuildLighting()
    {
        var (card, body) = IslandUi.Card("Iluminação", "sun"); card.SizeFlagsStretchRatio = 50; body.AddThemeConstantOverride("separation", 12);
        (_useHour, _hour, _hourText) = Row(body, "Horário do dia", 0, 24, .25, 12); (_useLight, _light, _lightText) = Row(body, "Intensidade da luz", .2, 1.6, .05, 1);
        body.AddChild(IslandUi.Text("Com o interruptor desligado, vale o horário e a luz do próprio jogo.", 13, IslandUi.TextMuted, false, true));
        return card;
    }

    private Control BuildAtmosphere()
    {
        var (card, body) = IslandUi.Card("Atmosfera", "waves"); card.SizeFlagsStretchRatio = 50; body.AddThemeConstantOverride("separation", 12);
        var weather = IslandUi.Row(12); _useWeather = new IslandToggle(); _useWeather.Toggled += _on => Changed(); weather.AddChild(_useWeather);
        var caption = IslandUi.Text("Clima", 14, IslandUi.TextPrimary); caption.CustomMinimumSize = new Vector2(130, 0); weather.AddChild(caption);
        _weather = IslandUi.Dropdown(Weathers.Select(w => (w.Text, w.Id))); _weather.ItemSelected += _i => { if (!_filling) _useWeather.ButtonPressed = true; Changed(); }; weather.AddChild(_weather); body.AddChild(weather);
        (_useFog, _fog, _fogText) = Row(body, "Neblina", 0, 1, .05, .3);
        var sky = IslandUi.Row(12); _useSky = new IslandToggle(); _useSky.Toggled += _on => Changed(); sky.AddChild(_useSky);
        var skyCaption = IslandUi.Text("Cor do céu", 14, IslandUi.TextPrimary); skyCaption.CustomMinimumSize = new Vector2(130, 0); sky.AddChild(skyCaption);
        _sky = new ColorPickerButton { Color = new Color("#7FB3FF"), EditAlpha = false, CustomMinimumSize = new Vector2(120, 40), FocusMode = FocusModeEnum.None }; _sky.ColorChanged += _c => { if (!_filling) _useSky.ButtonPressed = true; Changed(); }; sky.AddChild(_sky); body.AddChild(sky);
        return card;
    }

    private (IslandToggle, HSlider, Label) Row(VBoxContainer body, string title, double min, double max, double step, double value)
    {
        var row = IslandUi.Row(12); var toggle = new IslandToggle(); toggle.Toggled += _on => Changed(); row.AddChild(toggle);
        var caption = IslandUi.Text(title, 14, IslandUi.TextPrimary); caption.CustomMinimumSize = new Vector2(130, 0); row.AddChild(caption);
        var slider = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 28), SizeFlagsVertical = SizeFlags.ShrinkCenter, FocusMode = FocusModeEnum.None };
        var text = IslandUi.Text("", 14, IslandUi.AccentPrimary, true); text.CustomMinimumSize = new Vector2(64, 0); text.HorizontalAlignment = HorizontalAlignment.Right;
        slider.ValueChanged += _v => { if (!_filling) toggle.ButtonPressed = true; Changed(); };
        row.AddChild(slider); row.AddChild(text); body.AddChild(row); return (toggle, slider, text);
    }

    private Control BuildPresets()
    {
        var (card, body) = IslandUi.Card("Predefinições", "layers");
        var first = IslandUi.Row(10); body.AddChild(first);
        _presets = IslandUi.Dropdown([("—", "")]); _presets.ItemSelected += _i => UpdatePresetButtons(); first.AddChild(_presets);
        _useButton = IslandUi.Action("Usar no formulário", "play", IslandUi.Kind.Secondary, 40); _useButton.Pressed += LoadPreset; first.AddChild(_useButton);
        _deleteButton = IslandUi.Action("Excluir", "trash", IslandUi.Kind.Danger, 40); _deleteButton.Pressed += () => { var name = SelectedPreset()?.Title ?? ""; Confirm($"Excluir a predefinição {name}?", "Excluir", () => _ = DeletePresetAsync()); }; first.AddChild(_deleteButton);
        var second = IslandUi.Row(10); body.AddChild(second);
        _presetName = IslandUi.Input("Nome para salvar o que está no formulário", 40); second.AddChild(_presetName);
        _saveButton = IslandUi.Action("Salvar Predefinição", "plus", IslandUi.Kind.Secondary, 40); _saveButton.Pressed += () => _ = SavePresetAsync(); second.AddChild(_saveButton);
        _effect = IslandUi.Text("", 14, IslandUi.TextSecondary, false, true); body.AddChild(_effect);
        return card;
    }

    // ---- the form ---------------------------------------------------------------------------------------------------------

    private static string Hour(double h) { var minutes = (int)Math.Round(h * 60); return $"{minutes / 60 % 24:00}:{minutes % 60:00}"; }

    private JsonObject BuildDraft()
    {
        var look = new JsonObject();
        if (_useHour.ButtonPressed) look["hour"] = Math.Round(_hour.Value, 2);
        if (_useWeather.ButtonPressed && _weather.Selected >= 0) look["weather"] = Weathers[_weather.Selected].Id;
        if (_useFog.ButtonPressed) look["fog"] = Math.Round(_fog.Value, 2);
        if (_useLight.ButtonPressed) look["light"] = Math.Round(_light.Value, 2);
        if (_useSky.ButtonPressed) look["sky"] = "#" + _sky.Color.ToHtml(false).ToUpperInvariant();
        return look;
    }

    /// <summary>The normal form of a look: keys in the same order and numbers rounded the same way the server writes them, so "is it changed" compares like with like.</summary>
    private static string Canon(JsonElement look)
    {
        var node = new JsonObject();
        if (look.ValueKind != JsonValueKind.Object) return "{}";
        if (look.TryGetProperty("hour", out var h) && h.TryGetDouble(out var hour)) node["hour"] = Math.Round(hour, 2);
        if (look.TryGetProperty("weather", out var w) && w.ValueKind == JsonValueKind.String) node["weather"] = w.GetString();
        if (look.TryGetProperty("fog", out var f) && f.TryGetDouble(out var fog)) node["fog"] = Math.Round(fog, 2);
        if (look.TryGetProperty("light", out var l) && l.TryGetDouble(out var light)) node["light"] = Math.Round(light, 2);
        if (look.TryGetProperty("sky", out var s) && s.ValueKind == JsonValueKind.String) node["sky"] = s.GetString()!.ToUpperInvariant();
        return node.ToJsonString();
    }

    private void Fill(JsonElement look)
    {
        _filling = true;
        bool Has(string key, out JsonElement value) { value = default; return look.ValueKind == JsonValueKind.Object && look.TryGetProperty(key, out value); }
        if (Has("hour", out var h) && h.TryGetDouble(out var hour)) { _useHour.SetPressedNoSignal(true); _hour.Value = hour; } else _useHour.SetPressedNoSignal(false);
        if (Has("light", out var l) && l.TryGetDouble(out var light)) { _useLight.SetPressedNoSignal(true); _light.Value = light; } else _useLight.SetPressedNoSignal(false);
        if (Has("fog", out var f) && f.TryGetDouble(out var fog)) { _useFog.SetPressedNoSignal(true); _fog.Value = fog; } else _useFog.SetPressedNoSignal(false);
        var wi = Has("weather", out var w) ? Array.FindIndex(Weathers, x => x.Id == w.GetString()) : -1; if (wi >= 0) { _useWeather.SetPressedNoSignal(true); _weather.Select(wi); } else _useWeather.SetPressedNoSignal(false);
        if (Has("sky", out var s) && s.ValueKind == JsonValueKind.String && Color.HtmlIsValid(s.GetString()!)) { _useSky.SetPressedNoSignal(true); _sky.Color = new Color(s.GetString()!); } else _useSky.SetPressedNoSignal(false);
        _filling = false; Changed();
    }

    private void Changed()
    {
        if (_hourText is null) return;
        _hourText.Text = Hour(_hour.Value); _lightText.Text = _light.Value.ToString("0.##", Pt) + "×"; _fogText.Text = (_fog.Value * 100).ToString("0", Pt) + "%";
        foreach (var (toggle, slider) in new[] { (_useHour, (Godot.Range)_hour), (_useLight, _light), (_useFog, _fog) }) slider.Modulate = toggle.ButtonPressed ? Colors.White : new Color(1, 1, 1, .4f);
        _weather.Modulate = _useWeather.ButtonPressed ? Colors.White : new Color(1, 1, 1, .5f); _sky.Modulate = _useSky.ButtonPressed ? Colors.White : new Color(1, 1, 1, .4f);
        var dirty = IsDirty; _dirty.Text = dirty ? "Alterações ainda não aplicadas" : "";
        _apply.Disabled = !dirty; _revert.Disabled = !dirty;
        var set = BuildDraft().Count; _effect.Text = set == 0 ? "O formulário está sem nenhum ajuste: a ilha usa o céu padrão do jogo." : $"{set} ajuste(s) no formulário; o resto fica com o padrão do jogo.";
    }

    private void Revert() { using var document = JsonDocument.Parse(_loadedLook); Fill(document.RootElement); Say("Alterações desfeitas.", false); }

    private void Confirm(string text, string ok, Action action) { if (Ask is { } ask) ask(text, ok, action); else action(); }

    // ---- data -------------------------------------------------------------------------------------------------------------

    public async Task LoadAsync()
    {
        if (_api is null || _island == Guid.Empty) return;
        if (IsDirty) return; // an unapplied draft is kept when the person comes back to the tab
        var reply = await _api.EnvironmentGet(_island);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        Show(reply.Body, keepForm: false);
    }

    private void Show(JsonElement d, bool keepForm)
    {
        var look = d.TryGetProperty("look", out var l) ? l : default;
        _updatedAt = d.TryGetProperty("updatedAt", out var u) && u.TryGetDateTimeOffset(out var at) ? at : default;
        _loadedLook = Canon(look);
        _all = [];
        if (d.TryGetProperty("builtIn", out var builtIn)) foreach (var p in builtIn.EnumerateArray()) _all.Add(new Preset(p.GetProperty("id").GetString() ?? "", p.GetProperty("title").GetString() ?? "", Canon(p.GetProperty("look")), false));
        if (d.TryGetProperty("custom", out var custom)) foreach (var p in custom.EnumerateArray()) _all.Add(new Preset(p.GetProperty("name").GetString() ?? "", p.GetProperty("name").GetString() ?? "", Canon(p.GetProperty("look")), true));
        var keep = SelectedPreset()?.Title;
        _presets.Clear(); foreach (var preset in _all) _presets.AddItem(preset.Custom ? preset.Title + "  (minha)" : preset.Title);
        for (var i = 0; i < _all.Count; i++) if (_all[i].Title == keep) _presets.Select(i);
        if (_presets.Selected < 0 && _all.Count > 0) _presets.Select(0);
        if (!keepForm) Fill(look);
        UpdatePresetButtons(); Changed();
    }

    private Preset? SelectedPreset() => _presets is { Selected: >= 0 } && _presets.Selected < _all.Count ? _all[_presets.Selected] : null;

    private void UpdatePresetButtons() { if (_useButton is null) return; var chosen = SelectedPreset(); _useButton.Disabled = chosen is null; _deleteButton.Disabled = chosen is not { Custom: true }; }

    private void LoadPreset()
    {
        if (SelectedPreset() is not { } preset) return;
        using var document = JsonDocument.Parse(preset.Look); Fill(document.RootElement); Say($"“{preset.Title}” está no formulário. Falta aplicar na ilha.", false);
    }

    public async Task ApplyAsync(bool clear = false)
    {
        if (_api is null) return;
        var look = clear ? "{}" : DraftJson;
        _apply.Disabled = true; var reply = await _api.EnvironmentSave(_island, look, _updatedAt);
        if (!reply.Ok)
        {
            Changed();
            if (reply.Conflict) { Say("Outra pessoa alterou a ilha antes de você. Recarregue (aba Geral) para ver as mudanças.", true); return; }
            Say(reply.Message, true); return;
        }
        Show(reply.Body, keepForm: false); Say(look == "{}" ? "A ilha voltou ao céu padrão do jogo." : "Ambiente aplicado. Quem está na ilha vê em até 30 segundos.", false); Notify?.Invoke("Ambiente da ilha atualizado.");
    }

    public async Task SavePresetAsync()
    {
        if (_api is null) return;
        var name = _presetName.Text.Trim(); if (name.Length == 0) { Say("Dê um nome à predefinição.", true); return; }
        var reply = await _api.EnvironmentPresetSave(_island, name, DraftJson);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        _presetName.Text = ""; Show(reply.Body, keepForm: true); Say($"Predefinição “{name}” salva.", false);
        for (var i = 0; i < _all.Count; i++) if (_all[i].Title == name) _presets.Select(i); UpdatePresetButtons();
    }

    public async Task DeletePresetAsync()
    {
        if (_api is null || SelectedPreset() is not { Custom: true } preset) return;
        var reply = await _api.EnvironmentPresetDelete(_island, preset.Title);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        Say($"Predefinição “{preset.Title}” excluída.", false); await LoadKeepingFormAsync();
    }

    private async Task LoadKeepingFormAsync()
    {
        if (_api is null) return;
        var reply = await _api.EnvironmentGet(_island); if (reply.Ok) Show(reply.Body, keepForm: true);
    }

    private void Say(string text, bool error) { _status.Text = text; _status.AddThemeColorOverride("font_color", error ? IslandUi.Error : IslandUi.Success); }
}
