using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Build;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The pictures of the profile: the big picture with the strip of thumbnails under it (select, enlarge, and for me add, remove, set as main, move), and the wide cover at the top of the identity card.
/// Everything is stored on the server; the pictures are images of my own inventory (imported, or taken in the world and imported).
/// </summary>
public partial class ProfileWindow
{
    private sealed record GalleryEntry(Guid Id, bool Main);

    /// <summary>How the bytes of a picture are fetched (path of the API → bytes, null when it is not there). The game uses the real network; tests answer for it.</summary>
    public Func<string, Task<byte[]?>>? ImageFetcher { get; set; }
    /// <summary>Takes a picture of the world and returns the file (null when it failed). The game supplies it.</summary>
    public Func<Task<string?>>? CaptureWorld { get; set; }

    private readonly List<GalleryEntry> _gallery = [];
    private readonly Dictionary<Guid, Texture2D> _textures = [];
    private int _selected;
    private BuildTexturePicker? _picker;
    private HBoxContainer _thumbs = null!;
    private Control _galleryTools = null!;
    private Button _setMain = null!, _removeImage = null!, _moveLeft = null!, _moveRight = null!, _zoom = null!;
    private PopupMenu? _addMenu, _coverMenu;
    private PanelContainer _coverFrame = null!;
    private TextureRect _cover = null!;
    private Button _coverEdit = null!;
    private int _imagesFor;

    // ---- test access -------------------------------------------------------------------------------------------------------
    public int GalleryCountForTesting => _gallery.Count;
    public int SelectedImageForTesting => _selected;
    public bool MainShownForTesting => _selected >= 0 && _selected < _gallery.Count && _gallery[_selected].Main;
    public int ThumbsForTesting => _thumbs.GetChildCount();
    public bool GalleryToolsVisibleForTesting => _galleryTools.Visible;
    public bool CoverShownForTesting => _cover.Texture is not null;
    public bool CoverFrameVisibleForTesting => _coverFrame.Visible;
    public bool SetMainEnabledForTesting => !_setMain.Disabled;
    public bool MoveEnabledForTesting(bool left) => !(left ? _moveLeft : _moveRight).Disabled;
    public void SelectImageForTesting(int index) => SelectImage(index);
    public void PressImageToolForTesting(string tool) => (tool switch { "main" => _setMain, "remove" => _removeImage, "left" => _moveLeft, "right" => _moveRight, _ => _zoom }).EmitSignal(BaseButton.SignalName.Pressed);
    public IReadOnlyList<string> AddMenuForTesting => [.. Enumerable.Range(0, _addMenu?.ItemCount ?? 0).Select(i => _addMenu!.GetItemText(i))];
    public IReadOnlyList<string> CoverMenuForTesting => [.. Enumerable.Range(0, _coverMenu?.ItemCount ?? 0).Select(i => _coverMenu!.GetItemText(i))];
    public void PressAddForTesting() => ShowAddMenu();
    public void ChooseAddForTesting(int index) => OnAdd(_addMenu!.GetItemId(index));
    public void PressCoverForTesting() => ShowCoverMenu();
    public void ChooseCoverForTesting(int index) => OnCover(_coverMenu!.GetItemId(index));
    public Task AddImageForTesting(Guid asset) => AddImageAsync(asset);
    public Task SetCoverForTesting(Guid? asset) => SetCoverAsync(asset);

    private Button ToolButton(string icon, string tip, Action action)
    {
        var button = new Button { Icon = UiStyle.Icon(icon, UiStyle.Text, 18), FocusMode = Control.FocusModeEnum.None, TooltipText = tip, CustomMinimumSize = new Vector2(40, 36), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        UiStyle.StyleButton(button, UiStyle.ButtonKind.Tool, 6, 4, 8); button.Pressed += action; return button;
    }

    /// <summary>The strip of thumbnails and the tools of the selected picture (the tools only for me).</summary>
    private Control BuildGalleryStrip()
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 6);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 66), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; box.AddChild(scroll);
        _thumbs = new HBoxContainer(); _thumbs.AddThemeConstantOverride("separation", 6); scroll.AddChild(_thumbs);
        var tools = new HBoxContainer(); tools.AddThemeConstantOverride("separation", 4); _galleryTools = tools; box.AddChild(tools);
        _moveLeft = ToolButton("chevron-left", "Mover a imagem para a esquerda", () => _ = MoveAsync(-1)); tools.AddChild(_moveLeft);
        _moveRight = ToolButton("chevron-right", "Mover a imagem para a direita", () => _ = MoveAsync(1)); tools.AddChild(_moveRight);
        _setMain = ToolButton("star", "Definir como imagem principal (ela vira a foto do perfil)", () => _ = SetMainAsync()); tools.AddChild(_setMain);
        _zoom = ToolButton("zoom-in", "Ver a imagem ampliada", () => ShowZoom()); tools.AddChild(_zoom);
        _removeImage = ToolButton("trash", "Remover esta imagem da galeria", () => { if (Current is { } entry) Confirm("Remover imagem?", "Ela sai da galeria. O arquivo continua no seu inventário.", "Remover", () => _ = RemoveAsync(entry.Id), true); }); tools.AddChild(_removeImage);
        return box;
    }

    /// <summary>The wide banner above the identity (it only appears when there is a cover, or for me to add one).</summary>
    private Control BuildCoverFrame()
    {
        _coverFrame = new PanelContainer { CustomMinimumSize = new Vector2(0, 88), ClipChildren = CanvasItem.ClipChildrenMode.AndDraw, Visible = false };
        _coverFrame.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.FieldFill, new Color(UiStyle.BorderSoft, .55f), 12, 1, 0, 0));
        _cover = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore };
        _coverFrame.AddChild(_cover);
        var overlay = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; foreach (var side in new[] { "margin_right", "margin_top" }) overlay.AddThemeConstantOverride(side, 8); _coverFrame.AddChild(overlay);
        _coverEdit = new Button { Icon = UiStyle.Icon("image", UiStyle.Text, 18), Text = " Capa", FocusMode = Control.FocusModeEnum.None, TooltipText = "Trocar, capturar ou remover a imagem de capa", SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, CustomMinimumSize = new Vector2(0, 32) };
        UiStyle.StyleButton(_coverEdit, UiStyle.ButtonKind.Tool, 10, 4, 8); _coverEdit.Pressed += ShowCoverMenu; overlay.AddChild(_coverEdit);
        return _coverFrame;
    }

    private GalleryEntry? Current => _selected >= 0 && _selected < _gallery.Count ? _gallery[_selected] : null;

    // ---- drawing -----------------------------------------------------------------------------------------------------------

    private void FillGallery()
    {
        var self = IsSelf; var keep = Current?.Id;
        _gallery.Clear();
        if (_profile.ValueKind == JsonValueKind.Object && _profile.TryGetProperty("gallery", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var item in list.EnumerateArray()) if (Guid.TryParse(Str(item, "id"), out var id)) _gallery.Add(new GalleryEntry(id, Flag(item, "main")));
        _selected = keep is { } id0 && _gallery.FindIndex(x => x.Id == id0) is >= 0 and var found ? found : Math.Max(0, _gallery.FindIndex(x => x.Main));
        RebuildThumbs();
        _coverFrame.Visible = self || Flag(_profile, "hasCover");
        _coverEdit.Visible = self;
        UpdateGalleryTools();
    }

    private void UpdateGalleryTools()
    {
        var self = IsSelf; var has = _gallery.Count > 0;
        _galleryTools.Visible = has;
        foreach (var button in new[] { _moveLeft, _moveRight, _setMain, _removeImage }) button.Visible = self;
        _moveLeft.Disabled = !has || _selected <= 0; _moveRight.Disabled = !has || _selected >= _gallery.Count - 1; _setMain.Disabled = !has || Current is not { Main: false }; _removeImage.Disabled = !has;
    }

    private void RebuildThumbs()
    {
        foreach (var child in _thumbs.GetChildren()) { _thumbs.RemoveChild(child); child.QueueFree(); }
        for (var i = 0; i < _gallery.Count; i++)
        {
            var index = i; var entry = _gallery[i]; var selected = i == _selected;
            var button = new Button { CustomMinimumSize = new Vector2(58, 58), FocusMode = Control.FocusModeEnum.None, ClipChildren = CanvasItem.ClipChildrenMode.AndDraw, TooltipText = entry.Main ? "Imagem principal" : "Ver imagem" };
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" }) button.AddThemeStyleboxOverride(state, UiStyle.Box(UiStyle.FieldFill, selected ? UiStyle.Accent : new Color(UiStyle.BorderSoft, .7f), 10, selected ? 2 : 1, 0, 0));
            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            var picture = new TextureRect { Name = "Thumb", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore, Texture = _textures.GetValueOrDefault(entry.Id) };
            picture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); button.AddChild(picture);
            if (entry.Main) { var mark = UiStyle.IconRect("star", UiStyle.Orange, 16); mark.Position = new Vector2(4, 4); button.AddChild(mark); }
            button.Pressed += () => SelectImage(index); _thumbs.AddChild(button);
        }
        if (IsSelf && _gallery.Count < 12)
        {
            var add = new Button { Icon = UiStyle.Icon("plus", UiStyle.TextMuted, 24), CustomMinimumSize = new Vector2(58, 58), FocusMode = Control.FocusModeEnum.None, TooltipText = "Adicionar imagem à galeria" };
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" }) add.AddThemeStyleboxOverride(state, UiStyle.Box(state == "normal" ? UiStyle.PanelFillSolid : UiStyle.ButtonFillHover, new Color(UiStyle.BorderSoft, .8f), 10, 1, 0, 0));
            add.AddThemeStyleboxOverride("focus", new StyleBoxEmpty()); add.Pressed += ShowAddMenu; _thumbs.AddChild(add);
        }
    }

    private void SelectImage(int index)
    {
        if (index < 0 || index >= _gallery.Count) return;
        _selected = index; ShowSelected(); RebuildThumbs(); UpdateGalleryTools();
    }

    private void ShowSelected()
    {
        if (Current is { } entry && _textures.TryGetValue(entry.Id, out var texture)) { _photo.Texture = texture; UpdatePhotoEmpty(); }
    }

    /// <summary>Shows a picture (bytes of a PNG, JPG or WebP) as the big one and as the small avatar.</summary>
    public void ShowPhoto(byte[] bytes)
    {
        if (TextureOf(bytes) is { } texture) { _photo.Texture = texture; _avatarSmall.Texture = texture; UpdatePhotoEmpty(); }
    }

    private static Texture2D? TextureOf(byte[] bytes)
    {
        var image = new Image();
        var error = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 'P' ? image.LoadPngFromBuffer(bytes) : bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? image.LoadJpgFromBuffer(bytes) : bytes.Length > 12 && bytes[0] == 'R' && bytes[8] == 'W' ? image.LoadWebpFromBuffer(bytes) : Error.FileUnrecognized;
        return error == Error.Ok && image.GetWidth() > 0 ? ImageTexture.CreateFromImage(image) : null;
    }

    // ---- fetching ----------------------------------------------------------------------------------------------------------

    private async Task<byte[]?> FetchAsync(string path)
    {
        if (ImageFetcher is { } fetch) return await fetch(path);
        if (_transport is not null) return null; // tests without a fetcher have no pictures
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is not { IsAuthenticated: true } || app is null) return null;
        var http = new HttpRequest { Timeout = 20, BodySizeLimit = 8 * 1024 * 1024 }; AddChild(http);
        if (http.Request($"{app.ApiBaseUrl}{path}", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) { http.QueueFree(); return null; }
        var done = await ToSignal(http, HttpRequest.SignalName.RequestCompleted); http.QueueFree();
        return done[1].AsInt64() is >= 200 and < 300 ? done[3].AsByteArray() : null;
    }

    /// <summary>Loads the pictures of the profile on screen: the main one and the thumbnails, then the cover. A profile opened meanwhile cancels what is left.</summary>
    private async Task LoadImagesAsync()
    {
        var token = ++_imagesFor; var viewing = _viewing;
        _photo.Texture = null; _avatarSmall.Texture = UiStyle.Icon("user", UiStyle.TextDisabled, 56); UpdatePhotoEmpty(); _cover.Texture = null;
        _textures.Clear(); RebuildThumbs();
        if (_gallery.Count == 0 && Flag(_profile, "hasPhoto") && await FetchAsync($"/api/v1/users/{viewing}/profile/photo") is { } main && token == _imagesFor) ShowPhoto(main);
        foreach (var entry in _gallery.ToList())
        {
            if (token != _imagesFor) return;
            if (await FetchAsync($"/api/v1/users/{viewing}/profile/gallery/{entry.Id}") is not { } bytes || token != _imagesFor || TextureOf(bytes) is not { } texture) continue;
            _textures[entry.Id] = texture;
            if (Current?.Id == entry.Id) { _photo.Texture = texture; UpdatePhotoEmpty(); if (entry.Main) _avatarSmall.Texture = texture; }
            RebuildThumbs();
        }
        if (Flag(_profile, "hasCover") && await FetchAsync($"/api/v1/users/{viewing}/profile/cover") is { } cover && token == _imagesFor && TextureOf(cover) is { } coverTexture) _cover.Texture = coverTexture;
    }

    // ---- changing (mine) -----------------------------------------------------------------------------------------------------

    /// <summary>Applies the answer of a gallery call: the new profile comes back and is drawn again.</summary>
    private async Task<bool> ApplyAsync(Task<ProfileReply> call, string ok)
    {
        _status.Text = "Salvando…"; var reply = await call;
        if (!reply.Ok) { _status.Text = Describe(reply); return false; }
        if (reply.Body.ValueKind == JsonValueKind.Object) { _profile = reply.Body; FillGallery(); await LoadImagesAsync(); }
        _status.Text = ok; return true;
    }

    private Task AddImageAsync(Guid asset) => ApplyAsync(_api.GalleryAdd(asset), "Imagem adicionada à galeria.");
    private Task RemoveAsync(Guid image) => ApplyAsync(_api.GalleryRemove(image), "Imagem removida da galeria.");
    private Task SetMainAsync() => Current is { } entry ? ApplyAsync(_api.GalleryMain(entry.Id), "Essa é a sua imagem principal e a foto do perfil.") : Task.CompletedTask;
    private Task SetCoverAsync(Guid? asset) => ApplyAsync(_api.CoverSet(asset), asset is null ? "Capa removida." : "Capa atualizada.");

    private async Task MoveAsync(int step)
    {
        var to = _selected + step; if (Current is null || to < 0 || to >= _gallery.Count) return;
        var order = _gallery.Select(x => x.Id).ToList(); (order[_selected], order[to]) = (order[to], order[_selected]);
        var moved = _gallery[_selected].Id;
        if (await ApplyAsync(_api.GalleryOrder(order), "Ordem salva.")) { var index = _gallery.FindIndex(x => x.Id == moved); if (index >= 0) SelectImage(index); }
    }

    // ---- choosing a picture: inventory, import, capture ------------------------------------------------------------------------

    private void ShowAddMenu()
    {
        if (_addMenu is null) { _addMenu = new PopupMenu(); _addMenu.IdPressed += OnAdd; AddChild(_addMenu); }
        _addMenu.Clear(); _addMenu.AddItem("Escolher do inventário ou importar…", 1); if (CaptureWorld is not null) _addMenu.AddItem("Capturar imagem do mundo", 2);
        PopAt(_addMenu);
    }

    private void ShowCoverMenu()
    {
        if (_coverMenu is null) { _coverMenu = new PopupMenu(); _coverMenu.IdPressed += OnCover; AddChild(_coverMenu); }
        _coverMenu.Clear(); _coverMenu.AddItem("Escolher do inventário ou importar…", 1); if (CaptureWorld is not null) _coverMenu.AddItem("Capturar imagem do mundo", 2);
        if (Flag(_profile, "hasCover")) _coverMenu.AddItem("Remover capa", 3);
        PopAt(_coverMenu);
    }

    private void OnAdd(long id) { if (id == 1) PickImage(asset => _ = AddImageAsync(asset)); else if (id == 2) _ = CaptureAndPickAsync(asset => _ = AddImageAsync(asset)); }

    private void OnCover(long id)
    {
        if (id == 1) PickImage(asset => _ = SetCoverAsync(asset));
        else if (id == 2) _ = CaptureAndPickAsync(asset => _ = SetCoverAsync(asset));
        else if (id == 3) _ = SetCoverAsync(null);
    }

    private BuildTexturePicker Picker()
    {
        if (_picker is null) { _picker = new BuildTexturePicker { Name = "ProfileTexturePicker" }; (GetParent() ?? this).AddChild(_picker); }
        return _picker;
    }

    private void PickImage(Action<Guid> chosen) => Picker().Open(false, entry => { if (Guid.TryParse(entry.Asset, out var asset)) chosen(asset); });

    /// <summary>Takes a picture of the world with this window out of the way, then sends it as a new texture and lets the person choose it when the upload ends.</summary>
    private async Task CaptureAndPickAsync(Action<Guid> chosen)
    {
        if (CaptureWorld is null) return;
        _status.Text = "Capturando…"; _window.Visible = false;
        string? path; try { path = await CaptureWorld(); } finally { _window.Visible = true; _window.MoveToFront(); }
        if (path is null) { _status.Text = "Não foi possível capturar a imagem."; return; }
        _status.Text = "Imagem capturada: escolha-a na lista quando o envio terminar.";
        Picker().OpenAndImport(entry => { if (Guid.TryParse(entry.Asset, out var asset)) chosen(asset); }, path);
    }

    // ---- enlarged view -------------------------------------------------------------------------------------------------------

    private void ShowZoom()
    {
        if (Current is null) return;
        var dialog = MakeDialog("Imagem do perfil", 780, 660);
        var picture = new TextureRect { Name = "Big", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 460) };
        dialog.Content.AddChild(picture);
        var at = _selected;
        void Draw() { picture.Texture = at >= 0 && at < _gallery.Count ? _textures.GetValueOrDefault(_gallery[at].Id) : null; }
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; row.AddThemeConstantOverride("separation", 8); dialog.Content.AddChild(row);
        var previous = ToolButton("chevron-left", "Imagem anterior", () => { at = (at - 1 + _gallery.Count) % _gallery.Count; Draw(); }); var next = ToolButton("chevron-right", "Próxima imagem", () => { at = (at + 1) % _gallery.Count; Draw(); });
        previous.SizeFlagsHorizontal = next.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; row.AddChild(previous); row.AddChild(next);
        DialogButtons(dialog, "Fechar", () => CloseDialog(dialog, _dialogs)); Draw();
    }
}
