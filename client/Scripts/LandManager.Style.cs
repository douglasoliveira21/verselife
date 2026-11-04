using System.Text.Json;
using Godot;
using LifeVerse.Shared.Land;

namespace LifeVerse.Client.Scripts;

/// The "Ambiente" tab of the parcel window: where people arrive and whether they may teleport here, the sky and weather inside the parcel,
/// and its music. The owner edits; everybody else sees the settings and can teleport. The server checks every value again.
public partial class LandManager
{
    private static readonly string[] TeleportModes = ["free", "forced", "disabled"];
    private static readonly string[] WeatherChoices = ["clear", "cloudy", "rain", "storm", "fog"];
    private Control _stylePage = null!;
    private Label _styleNote = null!, _arrivalText = null!, _musicStatus = null!, _hourText = null!, _fogText = null!, _lightText = null!;
    private OptionButton _teleportMode = null!, _weather = null!;
    private CheckBox _useHour = null!, _useFog = null!, _useLight = null!, _useSky = null!, _autoplay = null!, _musicOn = null!;
    private HSlider _hour = null!, _fog = null!, _light = null!, _volume = null!;
    private ColorPickerButton _sky = null!;
    private LineEdit _musicUrl = null!, _musicName = null!;
    private Button _useMyPlace = null!, _saveArrival = null!, _saveEnvironment = null!, _clearEnvironment = null!, _saveMusic = null!, _removeMusic = null!, _teleportHere = null!;
    private (double X, double Z, float Yaw)? _pendingArrival;
    private (Guid Id, int Revision) _styleShown;

    private LandParcelData? StyleParcel => _tradeParcel;
    private LandParcelData? StyleOwned => _tradeParcel is { Mine: true } parcel ? parcel : null;

    private HSlider Slider(double min, double max, double step, double value) { var slider = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 28), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; return slider; }

    private (CheckBox Use, HBoxContainer Row) Optional(VBoxContainer box, string caption)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); box.AddChild(row);
        var use = new CheckBox { Text = caption, CustomMinimumSize = new Vector2(150, 0), FocusMode = Control.FocusModeEnum.None }; use.AddThemeFontSizeOverride("font_size", 17); row.AddChild(use);
        return (use, row);
    }

    private Control BuildStylePage()
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false }; scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var page = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; page.AddThemeConstantOverride("separation", 12); scroll.AddChild(page);
        _styleNote = Note(); page.AddChild(_styleNote);

        var arrival = Section(page, "Chegada e teleporte");
        _teleportMode = Choice("Livre: cada um escolhe o ponto", "Forçado: sempre no ponto de chegada", "Desativado: ninguém teleporta para cá"); arrival.AddChild(_teleportMode);
        _arrivalText = Note(); arrival.AddChild(_arrivalText);
        var arrivalRow = new HBoxContainer(); arrivalRow.AddThemeConstantOverride("separation", 8); arrival.AddChild(arrivalRow);
        _useMyPlace = Act("Usar onde estou", UiStyle.ButtonKind.Secondary, UseMyPlace); arrivalRow.AddChild(_useMyPlace);
        _saveArrival = Act("Salvar chegada", UiStyle.ButtonKind.Primary, SaveArrival); arrivalRow.AddChild(_saveArrival);
        _teleportHere = Act("Teleportar para cá", UiStyle.ButtonKind.Secondary, () => { if (StyleParcel is { } p) TeleportTo(p); }); arrival.AddChild(_teleportHere);

        var look = Section(page, "Ambiente deste terreno");
        look.AddChild(Note("O que você não marcar continua igual ao da ilha. A mudança vale só para quem está dentro do terreno e aparece aos poucos."));
        var (useHour, hourRow) = Optional(look, "Hora do dia"); _useHour = useHour; _hour = Slider(0, 24, .25, 12); hourRow.AddChild(_hour); _hourText = new Label { CustomMinimumSize = new Vector2(64, 0) }; hourRow.AddChild(_hourText);
        _hour.ValueChanged += v => _hourText.Text = $"{(int)v:00}:{(int)((v - Math.Floor(v)) * 60):00}";
        var weatherRow = new HBoxContainer(); weatherRow.AddThemeConstantOverride("separation", 8); look.AddChild(weatherRow);
        var weatherCaption = new Label { Text = "Clima", CustomMinimumSize = new Vector2(182, 0) }; weatherCaption.AddThemeFontSizeOverride("font_size", 17); weatherRow.AddChild(weatherCaption);
        _weather = Choice("Igual ao da ilha", "Limpo", "Nublado", "Chuva", "Tempestade", "Neblina"); weatherRow.AddChild(_weather);
        var (useFog, fogRow) = Optional(look, "Névoa"); _useFog = useFog; _fog = Slider(0, 1, .05, .3); fogRow.AddChild(_fog); _fogText = new Label { CustomMinimumSize = new Vector2(64, 0) }; fogRow.AddChild(_fogText);
        _fog.ValueChanged += v => _fogText.Text = $"{(int)(v * 100)}%";
        var (useLight, lightRow) = Optional(look, "Luz"); _useLight = useLight; _light = Slider(ParcelEnvironment.MinLight, ParcelEnvironment.MaxLight, .05, 1); lightRow.AddChild(_light); _lightText = new Label { CustomMinimumSize = new Vector2(64, 0) }; lightRow.AddChild(_lightText);
        _light.ValueChanged += v => _lightText.Text = $"{(int)(v * 100)}%";
        var (useSky, skyRow) = Optional(look, "Cor do céu"); _useSky = useSky; _sky = new ColorPickerButton { Color = new Color("#7FB2FF"), CustomMinimumSize = new Vector2(120, 36), EditAlpha = false, FocusMode = Control.FocusModeEnum.None }; skyRow.AddChild(_sky);
        var lookButtons = new HBoxContainer(); lookButtons.AddThemeConstantOverride("separation", 8); look.AddChild(lookButtons);
        _saveEnvironment = Act("Aplicar ambiente", UiStyle.ButtonKind.Primary, SaveEnvironment); lookButtons.AddChild(_saveEnvironment);
        _clearEnvironment = Act("Voltar ao da ilha", UiStyle.ButtonKind.Secondary, ClearEnvironment); lookButtons.AddChild(_clearEnvironment);

        var music = Section(page, "Música do terreno");
        music.AddChild(Note("Cole o endereço (https://…) de um arquivo .mp3 ou .ogg. Cada pessoa baixa e toca no próprio computador. Vídeo não é aceito."));
        _musicUrl = Entry("https://exemplo.com/musica.mp3", 512); music.AddChild(_musicUrl);
        _musicName = Entry("Nome da música (opcional)", 80); music.AddChild(_musicName);
        var volumeRow = new HBoxContainer(); volumeRow.AddThemeConstantOverride("separation", 8); music.AddChild(volumeRow);
        var volumeCaption = new Label { Text = "Volume", CustomMinimumSize = new Vector2(182, 0) }; volumeCaption.AddThemeFontSizeOverride("font_size", 17); volumeRow.AddChild(volumeCaption);
        _volume = Slider(0, 1, .05, .6); volumeRow.AddChild(_volume);
        _autoplay = new CheckBox { Text = "Tocar sozinha quando alguém entrar", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None }; _autoplay.AddThemeFontSizeOverride("font_size", 17); music.AddChild(_autoplay);
        var musicButtons = new HBoxContainer(); musicButtons.AddThemeConstantOverride("separation", 8); music.AddChild(musicButtons);
        _saveMusic = Act("Salvar música", UiStyle.ButtonKind.Primary, SaveMusic); musicButtons.AddChild(_saveMusic);
        _removeMusic = Act("Remover música", UiStyle.ButtonKind.Secondary, RemoveMusic); musicButtons.AddChild(_removeMusic);

        var listen = Section(page, "Neste computador");
        _musicOn = new CheckBox { Text = "Ouvir a música dos terrenos", ButtonPressed = Presence.MusicEnabled, FocusMode = Control.FocusModeEnum.None }; _musicOn.AddThemeFontSizeOverride("font_size", 17);
        _musicOn.Toggled += on => Presence.MusicEnabled = on; listen.AddChild(_musicOn);
        _musicStatus = Note(); listen.AddChild(_musicStatus);
        Presence.Changed += () => { if (_musicStatus is not null) _musicStatus.Text = Presence.Status; };
        return scroll;
    }

    // ---- showing the parcel ------------------------------------------------------------------------------------------------

    private void UpdateStyle()
    {
        if (_styleNote is null) return;
        var parcel = StyleParcel; var owned = StyleOwned;
        _styleNote.Text = parcel is null ? "Selecione um terreno inteiro para ver como ele recebe as pessoas." : owned is null ? "Só o dono muda estas configurações. Você pode ver o que foi definido e teleportar." : string.Empty;
        _styleNote.Visible = _styleNote.Text.Length > 0;
        var editable = owned is not null;
        foreach (var control in new Control[] { _teleportMode, _weather, _useHour, _useFog, _useLight, _useSky, _autoplay, _hour, _fog, _light, _volume, _sky, _useMyPlace, _saveArrival, _saveEnvironment, _clearEnvironment, _saveMusic, _removeMusic })
        {
            if (control is BaseButton button) button.Disabled = !editable; else if (control is Slider slider) slider.Editable = editable;
        }
        _musicUrl.Editable = _musicName.Editable = editable;
        _teleportHere.Disabled = parcel is null || parcel.TeleportMode == "disabled";
        _teleportHere.Text = parcel is { TeleportMode: "disabled" } ? "Teleporte desativado neste terreno" : "Teleportar para cá";
        _musicOn.SetPressedNoSignal(Presence.MusicEnabled); _musicStatus.Text = Presence.Status;
        if (parcel is null) { _styleShown = default; return; }
        if (_styleShown == (parcel.Id, parcel.Revision)) return; // keep what the owner is typing until the parcel really changes
        _styleShown = (parcel.Id, parcel.Revision); _pendingArrival = null;
        _teleportMode.Select(Math.Max(Array.IndexOf(TeleportModes, parcel.TeleportMode), 0));
        ShowArrivalText(parcel);
        var env = parcel.Environment;
        _useHour.SetPressedNoSignal(env.Hour is not null); _hour.Value = env.Hour ?? 12; _hourText.Text = $"{(int)_hour.Value:00}:{(int)((_hour.Value - Math.Floor(_hour.Value)) * 60):00}";
        _weather.Select(env.Weather is null ? 0 : Array.IndexOf(WeatherChoices, env.Weather) + 1);
        _useFog.SetPressedNoSignal(env.Fog is not null); _fog.Value = env.Fog ?? .3; _fogText.Text = $"{(int)(_fog.Value * 100)}%";
        _useLight.SetPressedNoSignal(env.Light is not null); _light.Value = env.Light ?? 1; _lightText.Text = $"{(int)(_light.Value * 100)}%";
        _useSky.SetPressedNoSignal(env.Sky is not null); if (env.Sky is not null) _sky.Color = new Color(env.Sky);
        _musicUrl.Text = parcel.MusicUrl; _musicName.Text = parcel.MusicName; _volume.Value = parcel.MusicVolume; _autoplay.SetPressedNoSignal(parcel.MusicAutoplay);
    }

    private void ShowArrivalText(LandParcelData parcel)
    {
        var point = _pendingArrival is { } pending ? $"x {Metres(pending.X)}, z {Metres(pending.Z)} (ainda não salvo)" : parcel.HasArrival ? $"x {Metres(parcel.ArrivalX!.Value)}, z {Metres(parcel.ArrivalZ!.Value)}, virado para {Metres(parcel.ArrivalYaw)}°" : "no meio do terreno";
        _arrivalText.Text = $"Ponto de chegada: {point}.";
    }

    // ---- the owner's changes --------------------------------------------------------------------------------------------------

    private void UseMyPlace()
    {
        if (StyleOwned is not { } parcel) return;
        var player = GetParent()?.GetNodeOrNull<PlayerController>("Player");
        if (player is null) { Say("Não achei o seu avatar no mundo."); return; }
        var at = player.GlobalPosition;
        if (!parcel.Cells.Contains(LandMath.Key(LandMath.CellOf(at.X), LandMath.CellOf(at.Z)))) { Say("Fique dentro do terreno para usar o lugar onde você está."); return; }
        var yaw = (float)Mathf.PosMod(Mathf.RadToDeg(player.Rotation.Y), 360f);
        _pendingArrival = (at.X, at.Z, yaw); ShowArrivalText(parcel);
    }

    private void SaveArrival()
    {
        if (StyleOwned is not { } parcel) return;
        var mode = TeleportModes[Mathf.Clamp(_teleportMode.Selected, 0, TeleportModes.Length - 1)];
        var x = _pendingArrival?.X ?? parcel.ArrivalX; var z = _pendingArrival?.Z ?? parcel.ArrivalZ; var yaw = _pendingArrival?.Yaw ?? parcel.ArrivalYaw;
        MutateStyle($"/parcels/{parcel.Id}/arrival", new { x, z, yaw, mode }, "Chegada e teleporte atualizados.");
    }

    /// The settings the form describes, as the JSON the server stores (only what is ticked).
    public string EnvironmentFromForm()
    {
        var look = new ParcelEnvironment(
            _useHour.ButtonPressed ? (float)_hour.Value : null,
            _weather.Selected > 0 ? WeatherChoices[Mathf.Clamp(_weather.Selected - 1, 0, WeatherChoices.Length - 1)] : null,
            _useFog.ButtonPressed ? (float)_fog.Value : null,
            _useLight.ButtonPressed ? (float)_light.Value : null,
            _useSky.ButtonPressed ? "#" + _sky.Color.ToHtml(false).ToUpperInvariant() : null);
        return look.ToJson();
    }

    private void SaveEnvironment()
    {
        if (StyleOwned is not { } parcel) return;
        if (!parcel.EnvironmentAllowed) { Say("Esta ilha não deixa os terrenos mudarem o ambiente."); return; }
        MutateStyle($"/parcels/{parcel.Id}/environment", new { environment = EnvironmentFromForm() }, "Ambiente do terreno atualizado.");
    }

    private void ClearEnvironment()
    {
        if (StyleOwned is not { } parcel) return;
        MutateStyle($"/parcels/{parcel.Id}/environment", new { environment = "{}" }, "O terreno voltou a usar o ambiente da ilha.");
    }

    private void SaveMusic()
    {
        if (StyleOwned is not { } parcel) return;
        var url = _musicUrl.Text.Trim();
        if (url.Length == 0) { RemoveMusic(); return; }
        if (LandMediaUrl.Check(url) is { } why) { Say(LandPresence.MediaMessage(why)); return; }
        MutateStyle($"/parcels/{parcel.Id}/media", new { musicUrl = url, volume = (float)_volume.Value, autoplay = _autoplay.ButtonPressed, name = _musicName.Text.Trim() }, "Música do terreno salva.");
    }

    private void RemoveMusic()
    {
        if (StyleOwned is not { } parcel) return;
        MutateStyle($"/parcels/{parcel.Id}/media", new { musicUrl = string.Empty, volume = 0f, autoplay = false, name = string.Empty }, "A música do terreno foi removida.");
    }

    private void MutateStyle(string path, object body, string done)
    {
        Call(Godot.HttpClient.Method.Put, path, JsonSerializer.Serialize(body), (status, text) =>
        {
            if (status is >= 200 and < 300) { Say(done); _styleShown = default; Load(); }
            else Say(StyleMessage(status, ErrorCode(text)));
        });
    }

    private static string StyleMessage(int status, string error) => error switch
    {
        "forbidden" => "Só o dono do terreno pode mudar isso.",
        "environment_not_allowed" => "Esta ilha não deixa os terrenos mudarem o ambiente.",
        "bad_environment" => "Esses valores de ambiente não são válidos.",
        "outside_parcel" => "O ponto de chegada precisa ficar dentro do terreno.",
        "blocked" => "Há um objeto sólido nesse ponto. Escolha um lugar livre.",
        "bad_mode" => "Modo de teleporte inválido.",
        "bad_volume" => "Volume inválido.",
        "conflict" => "O terreno mudou enquanto você editava. Tente de novo.",
        "not_https" or "bad_host" or "bad_type" or "too_long" => LandPresence.MediaMessage(error),
        _ => status == 0 ? "Sem conexão com o servidor." : $"Não foi possível salvar (HTTP {status})."
    };

    // ---- teleporting ----------------------------------------------------------------------------------------------------------

    /// Asks the island to take us to the parcel (its arrival point and rules decide where exactly).
    public void TeleportTo(LandParcelData parcel)
    {
        if (parcel.TeleportMode == "disabled") { Say("Este terreno não aceita teleportes."); return; }
        if (TeleportTransport is not null) { TeleportTransport(parcel.Id.ToString(), 0, 0); return; }
        _network.RequestTeleport(parcel.Id.ToString(), 0, 0);
    }

    /// Asks the island to take us to a spot of the ground (a parcel that forces its arrival point sends us there instead).
    public void TeleportToPoint(double x, double z)
    {
        if (TeleportTransport is not null) { TeleportTransport(string.Empty, (float)x, (float)z); return; }
        _network.RequestTeleport(string.Empty, (float)x, (float)z);
    }

    /// Tests: receives the teleport requests instead of the network.
    public Action<string, float, float>? TeleportTransport { get; set; }

    private void OnTeleportResult(bool ok, string code, float x, float z, float yaw)
    {
        if (ok) { Say("Teleportado."); Presence.Evaluate(force: true); return; }
        Say(code switch
        {
            "teleport_disabled" => "Este terreno não aceita teleportes.",
            "no_access" => "Você não pode entrar nesse terreno.",
            "unknown_parcel" => "Esse terreno não existe mais.",
            "in_vehicle" => "Saia do veículo para teleportar.",
            "rate_limit" => "Calma: espere um instante para teleportar de novo.",
            _ => "Não foi possível teleportar."
        });
    }

    /// Shows the parcel in the window on the sale and rent tab (from the map).
    public void ShowTrade(LandParcelData parcel) { SelectCells([.. parcel.Cells]); _infoTabs.Select(2); }

    public void ShowStyleForTesting() => _infoTabs.Select(5);
    public string ArrivalTextForTesting => _arrivalText.Text;
    public int TeleportModeShownForTesting => _teleportMode.Selected;
    public void SetTeleportModeForTesting(int index) => _teleportMode.Select(index);
    public void SetEnvironmentFormForTesting(float? hour, int weather, float? fog, float? light, string? sky)
    {
        _useHour.ButtonPressed = hour is not null; if (hour is { } h) _hour.Value = h; _weather.Select(weather);
        _useFog.ButtonPressed = fog is not null; if (fog is { } f) _fog.Value = f; _useLight.ButtonPressed = light is not null; if (light is { } l) _light.Value = l;
        _useSky.ButtonPressed = sky is not null; if (sky is not null) _sky.Color = new Color(sky);
    }
    public void SaveEnvironmentForTesting() => SaveEnvironment();
    public void SaveArrivalForTesting() => SaveArrival();
    public void UseMyPlaceForTesting() => UseMyPlace();
    public void SetMusicFormForTesting(string url, string name, float volume, bool autoplay) { _musicUrl.Text = url; _musicName.Text = name; _volume.Value = volume; _autoplay.ButtonPressed = autoplay; }
    public void SaveMusicForTesting() => SaveMusic();
    public void RemoveMusicForTesting() => RemoveMusic();
    public bool StyleEditableForTesting => _saveMusic is { Disabled: false };
    public bool TeleportHereEnabledForTesting => _teleportHere is { Disabled: false };
}
