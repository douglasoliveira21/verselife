using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Build;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The profile of a person, as in Second Life: picture, name, type of account (padrão or doador), whether they are married in the game, "sobre mim", "vida real", interests, birthday and the
/// groups they chose to show. Mine can be edited (and what is hidden from others is chosen here); another person's is read-only, with a button to propose marriage. The server decides
/// what each viewer may see; this only draws what it sent.
/// </summary>
public partial class ProfileWindow : Node
{
    private FloatingWindow _window = null!;
    private TextureRect _photo = null!;
    private Label _name = null!, _user = null!, _account = null!, _marriage = null!, _joined = null!, _status = null!, _photoNote = null!;
    private TextEdit _bio = null!, _firstLife = null!;
    private LineEdit _interests = null!, _proposeMessage = null!;
    private Label _birthdayText = null!;
    private Control _selfSettings = null!, _photoButtons = null!, _proposeBox = null!, _save = null!;
    private CheckBox _hideGroups = null!, _hideMarriage = null!, _privateProfile = null!;
    private VBoxContainer _groupRows = null!, _marriageRows = null!;
    private readonly List<Control> _pages = [];
    private UiTabs _tabs = null!;
    private BuildTexturePicker? _picker;
    private HttpRequest? _photoHttp;
    private ProfileApi _api = null!;
    private Func<string, string, string?, Task<(long, string)>>? _transport;
    private JsonElement _profile;
    private Guid _viewing;
    private Guid? _newPhoto;
    private bool _clearPhoto, _filling;

    public bool IsOpen => _window is not null && _window.Visible;
    public Guid Viewing => _viewing;
    public Guid MyId => GetNodeOrNull<SessionManager>("/root/SessionManager")?.UserId ?? Guid.Empty;
    public Func<Guid>? MyIdOverride { get; set; }
    private Guid Me => MyIdOverride?.Invoke() ?? MyId;
    private bool IsSelf => _profile.ValueKind == JsonValueKind.Object && _profile.TryGetProperty("isSelf", out var s) && s.ValueKind == JsonValueKind.True;

    // ---- test access -------------------------------------------------------------------------------------------------------

    public string NameForTesting => _name.Text;
    public string AccountForTesting => _account.Text;
    public string MarriageForTesting => _marriage.Text;
    public string BioForTesting { get => _bio.Text; set => _bio.Text = value; }
    public string FirstLifeForTesting { get => _firstLife.Text; set => _firstLife.Text = value; }
    public string InterestsForTesting { get => _interests.Text; set => _interests.Text = value; }
    public string BirthdayForTesting => _birthdayText.Text;
    public string StatusForTesting => _status.Text;
    public bool EditableForTesting => _bio.Editable;
    public bool SaveVisibleForTesting => _save.Visible;
    public bool ProposeVisibleForTesting => _proposeBox.Visible;
    public int GroupRowsForTesting => _groupRows.GetChildCount();
    public int MarriageRowsForTesting => _marriageRows.GetChildCount();
    public IReadOnlyList<string> TabsForTesting => [.. Enumerable.Range(0, _tabs.Count).Select(i => _tabs.TabAt(i).Text.Trim())];
    public bool PhotoShownForTesting => _photo.Texture is not null;
    public void UseTransportForTesting(Func<string, string, string?, Task<(long, string)>> transport) { _transport = transport; _api = new ProfileApi(transport); }
    public void SetHideGroupsForTesting(bool on) => _hideGroups.ButtonPressed = on;
    public void SetHideMarriageForTesting(bool on) => _hideMarriage.ButtonPressed = on;
    public void SetPrivateForTesting(bool on) => _privateProfile.ButtonPressed = on;
    public void PickPhotoForTesting(Guid asset) { _newPhoto = asset; _clearPhoto = false; _photoNote.Text = "Nova foto escolhida: salve para valer."; }
    public void SelectTabForTesting(int index) => _tabs.Select(index);
    public void SetProposeMessageForTesting(string text) => _proposeMessage.Text = text;
    public Task ToggleGroupForTesting(int row, bool shown) => SetGroupShown(row, shown);

    public override void _Ready()
    {
        _api = new ProfileApi(_transport ?? ScriptApi.HttpTransport(this));
        _window = new FloatingWindow("Perfil", icon: "user") { Name = "ProfileFloating", Visible = false, Theme = ViewerTheme.Create() };
        _window.Place(.5f, .5f, -250, -330, 250, 330); AddChild(_window);
        var content = _window.Content; content.AddThemeConstantOverride("separation", 8);

        var header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 12); content.AddChild(header);
        _photo = new TextureRect { CustomMinimumSize = new Vector2(112, 112), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, ClipContents = true }; header.AddChild(_photo);
        var identity = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; identity.AddThemeConstantOverride("separation", 2); header.AddChild(identity);
        _name = Text("", 22, true); _user = Text("", 15, false, UiStyle.TextMuted); _account = Text("", 16, true, UiStyle.AccentText); _marriage = Text("", 16); _joined = Text("", 15, false, UiStyle.TextMuted);
        foreach (var label in new[] { _name, _user, _account, _marriage, _joined }) identity.AddChild(label);

        _tabs = new UiTabs { EqualWidth = true, Flat = true, FontSize = 17 };
        foreach (var (title, icon) in new[] { ("SOBRE", "user"), ("VIDA REAL", "home"), ("GRUPOS", "people"), ("CASAMENTO", "heart") }) _tabs.AddTab(title, icon);
        content.AddChild(_tabs);
        _pages.Add(BuildAbout()); _pages.Add(BuildFirstLife()); _pages.Add(BuildGroups()); _pages.Add(BuildMarriage());
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 280) };
        var pageBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; foreach (var page in _pages) pageBox.AddChild(page); scroll.AddChild(pageBox); content.AddChild(scroll);
        _tabs.TabChanged += index => { for (var i = 0; i < _pages.Count; i++) _pages[i].Visible = i == index; };
        _tabs.Select(0); for (var i = 1; i < _pages.Count; i++) _pages[i].Visible = false;

        _status = Text("", 15, false, UiStyle.TextMuted); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; content.AddChild(_status);
        var save = new Button { Text = " Salvar perfil", Icon = UiStyle.Icon("check", UiStyle.Text, 20), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 42) }; UiStyle.StyleButton(save, UiStyle.ButtonKind.Primary, 10, 6, 14);
        save.Pressed += () => _ = SaveAsync(); _save = save; content.AddChild(save);
    }

    private static Label Text(string text, int size, bool bold = false, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        label.AddThemeFontSizeOverride("font_size", size); if (bold) label.AddThemeFontOverride("font", UiStyle.Bold); if (color is { } c) label.AddThemeColorOverride("font_color", c); return label;
    }

    private static HBoxContainer Row(string caption, Control control)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
        var label = new Label { Text = caption, CustomMinimumSize = new Vector2(110, 0) }; label.AddThemeFontSizeOverride("font_size", 15); label.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        row.AddChild(label); control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(control); return row;
    }

    private static Button SmallButton(string text, string tip, Action action)
    {
        var button = new Button { Text = text, TooltipText = tip, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36) }; UiStyle.StyleButton(button, UiStyle.ButtonKind.Secondary, 8, 5, 10);
        button.AddThemeFontSizeOverride("font_size", 15); button.Pressed += action; return button;
    }

    // ---- pages -------------------------------------------------------------------------------------------------------------

    private Control BuildAbout()
    {
        var page = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; page.AddThemeConstantOverride("separation", 8);
        page.AddChild(Text("SOBRE MIM", 16, true, UiStyle.AccentText));
        _bio = new TextEdit { CustomMinimumSize = new Vector2(0, 130), WrapMode = TextEdit.LineWrappingMode.Boundary, PlaceholderText = "Conte quem você é no LifeVerse…" }; _bio.AddThemeFontSizeOverride("font_size", 16); page.AddChild(_bio);
        _interests = new LineEdit { PlaceholderText = "Interesses (música, construção, jogos…)", MaxLength = 500 }; page.AddChild(Row("INTERESSES", _interests));
        _birthdayText = Text("", 16); page.AddChild(Row("ANIVERSÁRIO", _birthdayText));
        var settings = new VBoxContainer(); settings.AddThemeConstantOverride("separation", 4); _selfSettings = settings; page.AddChild(settings);
        _privateProfile = new CheckBox { Text = " Perfil privado (outros veem só o nome)" }; _hideGroups = new CheckBox { Text = " Esconder meus grupos de outras pessoas" }; _hideMarriage = new CheckBox { Text = " Esconder meu casamento de outras pessoas" };
        foreach (var box in new[] { _privateProfile, _hideGroups, _hideMarriage }) { box.FocusMode = Control.FocusModeEnum.None; box.AddThemeFontSizeOverride("font_size", 16); settings.AddChild(box); }
        var photo = new VBoxContainer(); photo.AddThemeConstantOverride("separation", 4); _photoButtons = photo; page.AddChild(photo);
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 6); photo.AddChild(buttons);
        buttons.AddChild(SmallButton("Trocar foto…", "Escolhe uma das suas texturas (ou importa uma imagem) como foto de perfil", OpenPicker));
        buttons.AddChild(SmallButton("Remover foto", "Tira a foto do perfil", () => { _clearPhoto = true; _newPhoto = null; _photo.Texture = null; _photoNote.Text = "A foto será removida quando você salvar."; }));
        _photoNote = Text("", 14, false, UiStyle.TextMuted); photo.AddChild(_photoNote);
        return page;
    }

    private Control BuildFirstLife()
    {
        var page = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; page.AddThemeConstantOverride("separation", 8);
        page.AddChild(Text("VIDA REAL", 16, true, UiStyle.AccentText));
        page.AddChild(Text("O que a pessoa quer contar sobre ela fora do jogo (opcional).", 14, false, UiStyle.TextMuted));
        _firstLife = new TextEdit { CustomMinimumSize = new Vector2(0, 200), WrapMode = TextEdit.LineWrappingMode.Boundary, PlaceholderText = "Cidade, profissão, o que quiser compartilhar…" }; _firstLife.AddThemeFontSizeOverride("font_size", 16); page.AddChild(_firstLife);
        return page;
    }

    private Control BuildGroups()
    {
        var page = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; page.AddThemeConstantOverride("separation", 6);
        page.AddChild(Text("GRUPOS", 16, true, UiStyle.AccentText));
        _groupRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _groupRows.AddThemeConstantOverride("separation", 4); page.AddChild(_groupRows);
        return page;
    }

    private Control BuildMarriage()
    {
        var page = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; page.AddThemeConstantOverride("separation", 6);
        page.AddChild(Text("CASAMENTO", 16, true, UiStyle.AccentText));
        _marriageRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _marriageRows.AddThemeConstantOverride("separation", 6); page.AddChild(_marriageRows);
        var propose = new VBoxContainer(); propose.AddThemeConstantOverride("separation", 6); _proposeBox = propose; page.AddChild(propose);
        _proposeMessage = new LineEdit { PlaceholderText = "Mensagem do pedido (opcional)", MaxLength = 300 }; propose.AddChild(_proposeMessage);
        propose.AddChild(SmallButton("Pedir em casamento", "Envia um pedido de casamento; vale por 14 dias", () => _ = ProposeAsync()));
        return page;
    }

    // ---- opening ---------------------------------------------------------------------------------------------------------------

    private void Show() { _window.Visible = true; _window.MoveToFront(); }
    public void Close() { if (_window is not null) _window.Visible = false; }

    public Task OpenSelfAsync() => OpenAsync(_api.ById(Me), Me);
    public Task OpenByIdAsync(Guid id) => OpenAsync(_api.ById(id), id);
    public async Task OpenByNameAsync(string name) { _status.Text = "Procurando…"; Show(); await OpenAsync(_api.ByName(name), Guid.Empty); }

    private async Task OpenAsync(Task<ProfileReply> request, Guid id)
    {
        Show(); _status.Text = "Carregando o perfil…"; _viewing = id;
        var reply = await request;
        if (!reply.Ok) { _status.Text = reply.Status == 404 ? "Não encontrei essa pessoa." : reply.Message; return; }
        _profile = reply.Body; _viewing = Guid.TryParse(Str(_profile, "id"), out var shown) ? shown : id; Fill(); await LoadPhotoAsync(); await LoadMarriageAsync();
    }

    private static string Str(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static bool Flag(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    private void Fill()
    {
        _filling = true; _newPhoto = null; _clearPhoto = false;
        var self = IsSelf; var p = _profile;
        _name.Text = Str(p, "displayName"); _user.Text = "@" + Str(p, "username") + (Str(p, "presence") == "online" ? "  ·  online agora" : "");
        var donor = Str(p, "accountType") == "donor";
        _account.Text = donor ? "★ Doador" + (DateTimeOffset.TryParse(Str(p, "donorSince"), out var since) ? $" desde {since:MM/yyyy}" : "") + " — apoia o LifeVerse" : "Conta padrão";
        _account.AddThemeColorOverride("font_color", donor ? UiStyle.Orange : UiStyle.TextMuted);
        _joined.Text = DateTimeOffset.TryParse(Str(p, "joinedAt"), out var joined) ? $"Personagem criado em {joined:dd/MM/yyyy}" : string.Empty;
        _bio.Text = Str(p, "bio"); _firstLife.Text = Str(p, "firstLifeBio"); _interests.Text = Str(p, "interests");
        foreach (var edit in new[] { _bio, _firstLife }) edit.Editable = self; _interests.Editable = self;
        _birthdayText.Text = Str(p, "birthday") is { Length: > 0 } b ? b + (p.TryGetProperty("characterAgeYears", out var age) && age.ValueKind == JsonValueKind.Number ? $"  ·  {age.GetInt32()} ano(s) de LifeVerse" : "") : "Perfil privado";
        _selfSettings.Visible = _photoButtons.Visible = _save.Visible = self;
        _proposeBox.Visible = !self;
        if (self && p.TryGetProperty("settings", out var s) && s.ValueKind == JsonValueKind.Object)
        {
            _hideGroups.ButtonPressed = Flag(s, "hideGroups"); _hideMarriage.ButtonPressed = Flag(s, "hideMarriage"); _privateProfile.ButtonPressed = Flag(s, "privateProfile");
            _photoNote.Text = Flag(p, "hasPhoto") ? "Foto de perfil definida." : "Sem foto de perfil.";
        }
        FillGroups(); FillMarriageHeader();
        _status.Text = Flag(p, "privateProfile") ? "Este perfil é privado: só o nome aparece." : string.Empty;
        _filling = false;
    }

    private void FillGroups()
    {
        foreach (var child in _groupRows.GetChildren()) { _groupRows.RemoveChild(child); child.QueueFree(); }
        var self = IsSelf;
        if (!_profile.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array || groups.GetArrayLength() == 0)
        { _groupRows.AddChild(Text(self ? "Você não participa de nenhum grupo." : "Nenhum grupo à mostra.", 16, false, UiStyle.TextMuted)); return; }
        var index = 0;
        foreach (var group in groups.EnumerateArray())
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); var tag = Str(group, "tag");
            row.AddChild(Text((tag.Length > 0 ? $"[{tag}] " : "") + Str(group, "name"), 17, true)); row.AddChild(Text(RoleLabel(Str(group, "role")), 15, false, UiStyle.TextMuted));
            if (self)
            {
                var captured = index; var box = new CheckBox { Text = " mostrar", ButtonPressed = Flag(group, "shown"), FocusMode = Control.FocusModeEnum.None, TooltipText = "Lista este grupo no seu perfil (só aparece se o grupo for público)" };
                box.Toggled += on => { if (!_filling) _ = SetGroupShown(captured, on); }; row.AddChild(box);
            }
            _groupRows.AddChild(row); index++;
        }
    }

    private static string RoleLabel(string role) => role switch { "owner" => "dono", "admin" => "administrador", "moderator" => "moderador", "member" => "membro", "" => "", _ => role };

    private async Task SetGroupShown(int row, bool shown)
    {
        if (!_profile.TryGetProperty("groups", out var groups) || row >= groups.GetArrayLength() || !Guid.TryParse(Str(groups[row], "id"), out var id)) return;
        var reply = await _api.GroupShown(id, shown); _status.Text = reply.Ok ? (shown ? "Grupo à mostra no perfil." : "Grupo escondido do perfil.") : reply.Message;
    }

    // ---- photo --------------------------------------------------------------------------------------------------------------------

    private void OpenPicker()
    {
        if (_picker is null) { _picker = new BuildTexturePicker { Name = "ProfileTexturePicker" }; (GetParent() ?? this).AddChild(_picker); }
        _picker.Open(false, entry => { if (Guid.TryParse(entry.Asset, out var asset)) PickPhotoForTesting(asset); });
    }

    private async Task LoadPhotoAsync()
    {
        _photo.Texture = null;
        if (!Flag(_profile, "hasPhoto") || _transport is not null) return;
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is not { IsAuthenticated: true } || app is null) return;
        _photoHttp ??= AddHttp();
        var viewing = _viewing;
        if (_photoHttp.Request($"{app.ApiBaseUrl}/api/v1/users/{viewing}/profile/photo", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) return;
        var done = await ToSignal(_photoHttp, HttpRequest.SignalName.RequestCompleted);
        if (viewing != _viewing || done[1].AsInt64() is < 200 or >= 300) return;
        ShowPhoto(done[3].AsByteArray());
    }

    private HttpRequest AddHttp() { var http = new HttpRequest { Timeout = 20, BodySizeLimit = 8 * 1024 * 1024 }; AddChild(http); return http; }

    public void ShowPhoto(byte[] bytes)
    {
        var image = new Image();
        var error = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 'P' ? image.LoadPngFromBuffer(bytes) : bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? image.LoadJpgFromBuffer(bytes) : bytes.Length > 12 && bytes[0] == 'R' && bytes[8] == 'W' ? image.LoadWebpFromBuffer(bytes) : Error.FileUnrecognized;
        if (error == Error.Ok && image.GetWidth() > 0) _photo.Texture = ImageTexture.CreateFromImage(image);
    }

    // ---- saving -----------------------------------------------------------------------------------------------------------------

    public async Task<bool> SaveAsync()
    {
        if (!IsSelf) return false;
        var changes = new JsonObject
        {
            ["bio"] = _bio.Text, ["firstLifeBio"] = _firstLife.Text, ["interests"] = _interests.Text,
            ["hideGroups"] = _hideGroups.ButtonPressed, ["hideMarriage"] = _hideMarriage.ButtonPressed, ["privateProfile"] = _privateProfile.ButtonPressed
        };
        if (_newPhoto is { } photo) changes["photoAssetId"] = photo.ToString(); else if (_clearPhoto) changes["clearPhoto"] = true;
        _status.Text = "Salvando…";
        var reply = await _api.Update(changes);
        if (!reply.Ok) { _status.Text = reply.Message; return false; }
        _profile = reply.Body; Fill(); await LoadPhotoAsync(); _status.Text = "Perfil salvo."; return true;
    }

    // ---- marriage --------------------------------------------------------------------------------------------------------------

    private void FillMarriageHeader()
    {
        if (_profile.TryGetProperty("marriage", out var m) && m.ValueKind == JsonValueKind.Object)
            _marriage.Text = $"♥ Casado(a) com {Str(m, "spouseName")}" + (DateTimeOffset.TryParse(Str(m, "since"), out var since) ? $" desde {since:dd/MM/yyyy}" : "");
        else _marriage.Text = Flag(_profile, "privateProfile") ? string.Empty : "Solteiro(a)";
    }

    private async Task LoadMarriageAsync()
    {
        foreach (var child in _marriageRows.GetChildren()) { _marriageRows.RemoveChild(child); child.QueueFree(); }
        if (!IsSelf)
        {
            _marriageRows.AddChild(Text(_marriage.Text.Length > 0 ? _marriage.Text : "Casamento não informado.", 17));
            _proposeBox.Visible = !(_profile.TryGetProperty("married", out var married) && married.ValueKind == JsonValueKind.True); return;
        }
        var reply = await _api.MarriageMine(); if (!reply.Ok) { _marriageRows.AddChild(Text(reply.Message, 16, false, UiStyle.Orange)); return; }
        var body = reply.Body;
        if (body.TryGetProperty("current", out var current) && current.ValueKind == JsonValueKind.Object)
        {
            _marriageRows.AddChild(Text($"♥ Você é casado(a) com {Str(current, "spouseName")}.", 17, true));
            _marriageRows.AddChild(SmallButton("Divorciar-se", "Encerra o casamento", () => _ = DivorceAsync()));
        }
        else _marriageRows.AddChild(Text("Você não está casado(a). Abra o perfil de outra pessoa para pedir em casamento.", 16, false, UiStyle.TextMuted));
        foreach (var item in body.GetProperty("incoming").EnumerateArray())
        {
            var id = Guid.Parse(Str(item, "id")); var row = new VBoxContainer(); row.AddChild(Text($"{Str(item, "fromName")} pediu você em casamento." + (Str(item, "message") is { Length: > 0 } msg ? $" “{msg}”" : ""), 16));
            var buttons = new HBoxContainer(); buttons.AddChild(SmallButton("Aceitar", "Casar", () => _ = AnswerAsync(id, "accept"))); buttons.AddChild(SmallButton("Recusar", "Recusar o pedido", () => _ = AnswerAsync(id, "decline"))); row.AddChild(buttons); _marriageRows.AddChild(row);
        }
        foreach (var item in body.GetProperty("outgoing").EnumerateArray())
        {
            var id = Guid.Parse(Str(item, "id")); var row = new HBoxContainer(); row.AddChild(Text($"Pedido enviado a {Str(item, "toName")} (aguardando).", 16));
            row.AddChild(SmallButton("Cancelar", "Cancela o pedido", () => _ = AnswerAsync(id, "cancel"))); _marriageRows.AddChild(row);
        }
    }

    public async Task ProposeAsync()
    {
        if (IsSelf || _viewing == Guid.Empty) return;
        var reply = await _api.Propose(_viewing, _proposeMessage.Text); _status.Text = reply.Ok ? "Pedido de casamento enviado." : reply.Message;
        if (reply.Ok) _proposeMessage.Text = string.Empty;
    }

    public async Task AnswerAsync(Guid id, string answer)
    {
        var reply = await _api.Answer(id, answer);
        _status.Text = reply.Ok ? answer switch { "accept" => "Vocês estão casados!", "decline" => "Pedido recusado.", _ => "Pedido cancelado." } : reply.Message;
        await RefreshOwnAsync();
    }

    public async Task DivorceAsync() { var reply = await _api.Divorce(); _status.Text = reply.Ok ? "Casamento encerrado." : reply.Message; await RefreshOwnAsync(); }

    private async Task RefreshOwnAsync()
    {
        if (!IsSelf) return;
        var status = _status.Text; var reply = await _api.ById(Me); if (reply.Ok) { _profile = reply.Body; Fill(); }
        await LoadMarriageAsync(); _status.Text = status;
    }

    public Task AnswerForTesting(int row, string answer) => AnswerByRowAsync(row, answer);

    private async Task AnswerByRowAsync(int row, string answer)
    {
        var reply = await _api.MarriageMine(); if (!reply.Ok) return;
        var list = (answer == "cancel" ? reply.Body.GetProperty("outgoing") : reply.Body.GetProperty("incoming")).EnumerateArray().ToList();
        if (row < list.Count) await AnswerAsync(Guid.Parse(Str(list[row], "id")), answer);
    }

    public Task DivorceForTesting() => DivorceAsync();
}
