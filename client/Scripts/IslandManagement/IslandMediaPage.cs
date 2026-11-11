using System.Globalization;
using System.Text.Json;
using Godot;
using LifeVerse.Shared.Land;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>
/// The Mídia tab: the music of the whole island, played wherever a parcel does not play its own. The address is checked as the person types (the same rules the game and the server apply, because each
/// player's computer downloads the file), it can be tested here before saving, and each player can still switch music off in their preferences. There is no video: the game cannot show one.
/// </summary>
public partial class IslandMediaPage : ScrollContainer
{
    private static readonly CultureInfo Pt = new("pt-BR");

    private LineEdit _url = null!, _name = null!;
    private HSlider _volume = null!;
    private Label _volumeText = null!, _check = null!, _status = null!, _dirty = null!, _tested = null!;
    private IslandToggle _autoplay = null!;
    private Button _test = null!, _stop = null!, _save = null!, _remove = null!, _revert = null!;
    private IslandManagementApi? _api;
    private Guid _island;
    private DateTimeOffset _updatedAt;
    private (string Url, double Volume, bool Autoplay, string Name) _loaded = ("", .6, true, "");
    private bool _filling, _previewing;

    public event Action<string>? Notify;
    public Action<string, string, Action>? Ask { get; set; }
    /// <summary>Plays an address for the person testing it, and goes back to what the place plays.</summary>
    public Action<string, float>? PreviewStart { get; set; }
    public Action? PreviewStop { get; set; }

    public bool IsDirty => (_url.Text.Trim(), Math.Round(_volume.Value / 100, 2), _autoplay.ButtonPressed, _name.Text.Trim()) != (_loaded.Url, Math.Round(_loaded.Volume, 2), _loaded.Autoplay, _loaded.Name);
    public string StatusForTesting => _status.Text;
    public string CheckForTesting => _check.Text;
    public bool SaveEnabledForTesting => !_save.Disabled;
    public bool TestEnabledForTesting => !_test.Disabled;
    public bool RemoveEnabledForTesting => !_remove.Disabled;
    public bool PreviewingForTesting => _previewing;
    public string LoadedUrlForTesting => _loaded.Url;
    public string UrlForTesting { get => _url.Text; set { _url.Text = value; Changed(); } }
    public string NameForTesting { get => _name.Text; set { _name.Text = value; Changed(); } }
    public double VolumeForTesting { get => _volume.Value; set => _volume.Value = value; }
    public bool AutoplayForTesting { get => _autoplay.ButtonPressed; set => _autoplay.ButtonPressed = value; }
    public void TestForTesting() => Test();
    public void StopTestForTesting() => StopTest();
    public Task SaveForTesting() => SaveAsync(false);
    public Task RemoveForTesting() => SaveAsync(true);

    public void Bind(IslandManagementApi api) => _api = api;
    public void SetIsland(Guid island) => _island = island;

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Auto; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        var column = IslandUi.Column(14); column.SizeFlagsHorizontal = SizeFlags.ExpandFill; AddChild(column);
        column.AddChild(BuildMusic()); column.AddChild(BuildVideo());
        Changed();
    }

    public override void _ExitTree() { if (_previewing) PreviewStop?.Invoke(); }

    // ---- cards ------------------------------------------------------------------------------------------------------------

    private Control BuildMusic()
    {
        var (card, body) = IslandUi.Card("Música da Ilha", "volume"); body.AddThemeConstantOverride("separation", 10);
        body.AddChild(IslandUi.Text("Endereço do arquivo de música", 14, IslandUi.TextSecondary));
        _url = IslandUi.Input("https://… (arquivo .mp3 ou .ogg)", 512); _url.TextChanged += _t => { if (!_filling) Changed(); }; body.AddChild(_url);
        _check = IslandUi.Text("", 13, IslandUi.TextMuted, false, true); body.AddChild(_check);
        body.AddChild(IslandUi.Text("Nome da música ou estação (aparece só aqui)", 14, IslandUi.TextSecondary));
        _name = IslandUi.Input("Rádio da Praia", 80); _name.TextChanged += _t => { if (!_filling) Changed(); }; body.AddChild(_name);
        var volume = IslandUi.Row(12); var caption = IslandUi.Text("Volume padrão", 14, IslandUi.TextPrimary); caption.CustomMinimumSize = new Vector2(130, 0); volume.AddChild(caption);
        _volume = new HSlider { MinValue = 0, MaxValue = 100, Step = 1, Value = 60, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(160, 28), SizeFlagsVertical = SizeFlags.ShrinkCenter, FocusMode = FocusModeEnum.None }; _volume.ValueChanged += _v => { if (!_filling) Changed(); }; volume.AddChild(_volume);
        _volumeText = IslandUi.Text("", 14, IslandUi.AccentPrimary, true); _volumeText.CustomMinimumSize = new Vector2(56, 0); _volumeText.HorizontalAlignment = HorizontalAlignment.Right; volume.AddChild(_volumeText); body.AddChild(volume);
        var auto = IslandUi.Row(12); _autoplay = new IslandToggle(); _autoplay.SetPressedNoSignal(true); _autoplay.Toggled += _on => { if (!_filling) Changed(); }; auto.AddChild(_autoplay);
        auto.AddChild(IslandUi.Text("Tocar sozinha ao chegar na ilha", 14, IslandUi.TextPrimary)); body.AddChild(auto);
        body.AddChild(IslandUi.Divider());
        var buttons = IslandUi.Row(10); body.AddChild(buttons);
        _test = IslandUi.Action("Testar", "play", IslandUi.Kind.Secondary, 42); _test.Pressed += Test; buttons.AddChild(_test);
        _stop = IslandUi.Action("Parar teste", "close", IslandUi.Kind.Secondary, 42); _stop.Pressed += StopTest; buttons.AddChild(_stop);
        _tested = IslandUi.Text("", 13, IslandUi.TextMuted, false, true); _tested.SizeFlagsHorizontal = SizeFlags.ExpandFill; buttons.AddChild(_tested);
        _remove = IslandUi.Action("Remover música", "trash", IslandUi.Kind.Danger, 42); _remove.Pressed += () => Confirm("Tirar a música da ilha? Ninguém mais ouve música onde nenhum terreno toca a sua.", "Remover", () => _ = SaveAsync(true)); buttons.AddChild(_remove);
        _revert = IslandUi.Action("Desfazer", null, IslandUi.Kind.Secondary, 42); _revert.Pressed += Revert; buttons.AddChild(_revert);
        _save = IslandUi.Action("Salvar na Ilha", "ok", IslandUi.Kind.Primary, 42); _save.CustomMinimumSize = new Vector2(180, 42); _save.Pressed += () => _ = SaveAsync(false); buttons.AddChild(_save);
        var foot = IslandUi.Row(10); body.AddChild(foot); _dirty = IslandUi.Text("", 14, IslandUi.AccentSecondary, true); foot.AddChild(_dirty);
        _status = IslandUi.Text("", 14, IslandUi.TextMuted, false, true); _status.SizeFlagsHorizontal = SizeFlags.ExpandFill; foot.AddChild(_status);
        body.AddChild(IslandUi.Text("A música toca onde nenhum terreno toca a sua. Quem está na ilha passa a ouvir em até 30 segundos. Cada jogador pode desligar a música nas próprias preferências. O arquivo é baixado no computador de cada jogador (até 12 MB).", 13, IslandUi.TextMuted, false, true));
        return card;
    }

    private Control BuildVideo()
    {
        var (card, body) = IslandUi.Card("Vídeo da Ilha", "camera");
        var row = IslandUi.Row(12); row.AddChild(IslandUi.Pill("Ainda não disponível", IslandUi.AccentSecondary)); body.AddChild(row);
        body.AddChild(IslandUi.Text("O jogo ainda não sabe mostrar vídeo, então não há o que configurar aqui. Quando existir uma superfície que toque vídeo, ela vai aparecer nesta aba.", 14, IslandUi.TextSecondary, false, true));
        return card;
    }

    // ---- form -------------------------------------------------------------------------------------------------------------

    private void Changed()
    {
        if (_check is null) return;
        _volumeText.Text = (_volume.Value).ToString("0", Pt) + "%";
        var url = _url.Text.Trim(); string? problem = null;
        if (url.Length == 0) { _check.Text = "Sem música: a ilha fica em silêncio onde nenhum terreno toca a sua."; _check.AddThemeColorOverride("font_color", IslandUi.TextMuted); }
        else if (LandMediaUrl.Check(url) is { } why) { problem = why; _check.Text = LandPresence.MediaMessage(why); _check.AddThemeColorOverride("font_color", IslandUi.Error); }
        else { _check.Text = $"Endereço aceito ({(url.Contains(".ogg", StringComparison.OrdinalIgnoreCase) ? "OGG" : "MP3")}). O arquivo precisa ter até 12 MB e ser tocável; use “Testar” para conferir."; _check.AddThemeColorOverride("font_color", IslandUi.Success); }
        var dirty = IsDirty; _dirty.Text = dirty ? "Alterações ainda não salvas" : "";
        _save.Disabled = !dirty || problem is not null; _revert.Disabled = !dirty; _test.Disabled = url.Length == 0 || problem is not null; _stop.Disabled = !_previewing; _remove.Disabled = _loaded.Url.Length == 0;
    }

    private void Test()
    {
        var url = _url.Text.Trim(); if (url.Length == 0 || LandMediaUrl.Check(url) is not null) return;
        _previewing = true; PreviewStart?.Invoke(url, (float)(_volume.Value / 100)); _tested.Text = "Tocando só para você. Pare o teste antes de sair da aba."; Changed();
    }

    /// <summary>Ends a test that is playing (leaving the tab or closing the window).</summary>
    public void EndTest() => StopTest();

    private void StopTest() { if (!_previewing) return; _previewing = false; PreviewStop?.Invoke(); _tested.Text = ""; Changed(); }

    private void Revert() { Fill(_loaded); Say("Alterações desfeitas.", false); }

    private void Confirm(string text, string ok, Action action) { if (Ask is { } ask) ask(text, ok, action); else action(); }

    private void Fill((string Url, double Volume, bool Autoplay, string Name) value)
    {
        _filling = true; _url.Text = value.Url; _name.Text = value.Name; _volume.Value = Math.Round(value.Volume * 100); _autoplay.SetPressedNoSignal(value.Autoplay); _filling = false; Changed();
    }

    // ---- data -------------------------------------------------------------------------------------------------------------

    public async Task LoadAsync()
    {
        if (_api is null || _island == Guid.Empty) return;
        if (IsDirty) return; // an unsaved draft is kept when the person comes back to the tab; saving still checks against the version it was based on
        var reply = await _api.MediaGet(_island);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        Show(reply.Body);
    }

    private void Show(JsonElement d)
    {
        _updatedAt = d.TryGetProperty("updatedAt", out var u) && u.TryGetDateTimeOffset(out var at) ? at : default;
        var music = d.TryGetProperty("music", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;
        string Str(string key) => music.ValueKind == JsonValueKind.Object && music.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        _loaded = (Str("url"), music.ValueKind == JsonValueKind.Object && music.TryGetProperty("volume", out var vol) && vol.TryGetDouble(out var volume) ? volume : .6, !(music.ValueKind == JsonValueKind.Object && music.TryGetProperty("autoplay", out var a) && a.ValueKind == JsonValueKind.False), Str("name"));
        Fill(_loaded);
    }

    public async Task SaveAsync(bool remove)
    {
        if (_api is null) return;
        if (_previewing) StopTest();
        var url = remove ? "" : _url.Text.Trim();
        _save.Disabled = true; var reply = await _api.MediaSave(_island, url, remove ? _loaded.Volume : Math.Round(_volume.Value / 100, 2), remove ? _loaded.Autoplay : _autoplay.ButtonPressed, remove ? "" : _name.Text.Trim(), _updatedAt);
        if (!reply.Ok)
        {
            Changed();
            Say(reply.Conflict ? "Outra pessoa alterou a ilha antes de você. Troque de aba e volte para recarregar." : reply.Message, true); return;
        }
        Show(reply.Body); Say(url.Length == 0 ? "A música foi removida da ilha." : "Música salva. Quem está na ilha ouve em até 30 segundos.", false); Notify?.Invoke(url.Length == 0 ? "Música da ilha removida." : "Música da ilha atualizada.");
    }

    private void Say(string text, bool error) { _status.Text = text; _status.AddThemeColorOverride("font_color", error ? IslandUi.Error : IslandUi.Success); }
}
