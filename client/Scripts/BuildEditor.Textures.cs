using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// Textures and light for the build window: a library (the person's images, favourites, recently used and the built-in
/// patterns), UV settings, per-face materials and a light for glowing objects. Everything ends up as keys of the object's
/// material JSON and goes to the server like any other edit.
public partial class BuildEditor
{
    private sealed record LibraryEntry(Guid? Asset, string? Builtin, string Name, bool Favorite);

    private const string PrefsPath = "user://build_prefs.cfg";
    private OptionButton _librarySource = null!;
    private ItemList _library = null!;
    private TextureRect _preview = null!;
    private Label _libraryStatus = null!, _target = null!;
    private LineEdit _repeatX = null!, _repeatY = null!, _offsetX = null!, _offsetY = null!, _uvRotation = null!;
    private CheckBox _flipU = null!, _flipV = null!, _lightOn = null!;
    private ColorPickerButton _lightColor = null!;
    private HSlider _glowStrength = null!, _lightEnergy = null!, _lightRange = null!;
    private FileDialog _imageDialog = null!;
    private HttpRequest? _libraryHttp;
    private readonly List<LibraryEntry> _mine = [], _shown = [];
    private TextureCache? _textureCache;
    private bool _watchingInventory;

    private TextureCache Textures => _textureCache ??= GetParent()?.GetNodeOrNull<TextureCache>("TextureCache") ?? new TextureCache();

    private void SetUpTextureCache()
    {
        var parent = GetParent();
        if (parent is null) return;
        if (parent.GetNodeOrNull<TextureCache>("TextureCache") is { } existing) { _textureCache = existing; return; }
        _textureCache = new TextureCache { Name = "TextureCache" };
        parent.CallDeferred(Node.MethodName.AddChild, _textureCache);
    }

    // ---- the Textura tab -------------------------------------------------------------------------------------

    private Control BuildTextureTab()
    {
        var box = Section();
        box.AddChild(FieldLabel("Cor"));
        _colorPicker = new ColorPickerButton { Color = new Color("#A8C7D8"), CustomMinimumSize = new Vector2(0, 40), EditAlpha = false }; box.AddChild(_colorPicker);
        _roughness = Slider(box, "Rugosidade", .7f); _metallic = Slider(box, "Metalicidade", 0f); _opacity = Slider(box, "Opacidade", 1f);
        _glow = new CheckBox { Text = " Brilho (emissão luminosa)" }; box.AddChild(_glow);
        _emissionPicker = new ColorPickerButton { Color = new Color("#FFFFFF"), CustomMinimumSize = new Vector2(0, 40), EditAlpha = false }; box.AddChild(_emissionPicker);
        _glowStrength = Slider(box, "Intensidade", .125f);
        box.AddChild(TextButton("Aplicar material à seleção", "Aplica ao objeto inteiro; texturas e cores por face são mantidas", ApplySelected, UiStyle.ButtonKind.Primary));

        box.AddChild(FieldLabel("Biblioteca de texturas"));
        _librarySource = new OptionButton();
        foreach (var name in new[] { "Minhas texturas", "Favoritas", "Recentes", "Texturas do sistema" }) _librarySource.AddItem(name);
        _librarySource.ItemSelected += _ => RefreshLibrary();
        box.AddChild(_librarySource);
        var libraryRow = new HBoxContainer(); libraryRow.AddThemeConstantOverride("separation", 8); box.AddChild(libraryRow);
        _library = new ItemList { CustomMinimumSize = new Vector2(0, 150), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SelectMode = ItemList.SelectModeEnum.Single };
        _library.ItemSelected += index => ShowPreview((int)index);
        libraryRow.AddChild(_library);
        _preview = new TextureRect { CustomMinimumSize = new Vector2(96, 96), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        libraryRow.AddChild(_preview);
        _libraryStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _libraryStatus.AddThemeColorOverride("font_color", UiStyle.TextMuted); _libraryStatus.AddThemeFontSizeOverride("font_size", 15);
        box.AddChild(_libraryStatus);
        box.AddChild(TextButton("Importar imagem (PNG, JPG, WebP)…", "Envia uma imagem ao seu inventário como textura", () => _imageDialog.PopupCentered(new Vector2I(800, 500))));

        box.AddChild(FieldLabel("Alvo"));
        _target = new Label { Text = "Objeto inteiro" }; _target.AddThemeColorOverride("font_color", UiStyle.AccentText); box.AddChild(_target);
        var apply = new HBoxContainer(); apply.AddThemeConstantOverride("separation", 6); box.AddChild(apply);
        apply.AddChild(TextButton("Aplicar textura", "Usa a textura escolhida no alvo (objeto ou face selecionada)", () => ApplyTexture(normal: false), UiStyle.ButtonKind.Primary));
        apply.AddChild(TextButton("Como mapa normal", "Usa a imagem escolhida como relevo", () => ApplyTexture(normal: true)));
        apply.AddChild(TextButton("Remover", "Tira a textura do alvo", RemoveTexture));

        box.AddChild(FieldLabel("Repetição, deslocamento e rotação (UV)"));
        var uv = new GridContainer { Columns = 2 }; uv.AddThemeConstantOverride("h_separation", 8); uv.AddThemeConstantOverride("v_separation", 6); box.AddChild(uv);
        _repeatX = Labeled(uv, "Repetição horizontal", "1"); _repeatY = Labeled(uv, "Repetição vertical", "1");
        _offsetX = Labeled(uv, "Deslocamento horizontal", "0"); _offsetY = Labeled(uv, "Deslocamento vertical", "0");
        _uvRotation = Labeled(uv, "Rotação (graus)", "0");
        _flipU = new CheckBox { Text = " Espelhar horizontal" }; _flipV = new CheckBox { Text = " Espelhar vertical" };
        box.AddChild(_flipU); box.AddChild(_flipV);
        box.AddChild(TextButton("Aplicar UV ao alvo", "Aplica repetição, deslocamento, rotação e espelhamento", ApplyUv, UiStyle.ButtonKind.Primary));

        box.AddChild(FieldLabel("Face"));
        _faceLabel = new Label { Text = "Use a ferramenta Face (5) e clique em uma face de uma primitiva.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _faceLabel.AddThemeColorOverride("font_color", UiStyle.TextMuted); _faceLabel.AddThemeFontSizeOverride("font_size", 15); box.AddChild(_faceLabel);
        var faceRow = new HBoxContainer(); faceRow.AddThemeConstantOverride("separation", 6); box.AddChild(faceRow);
        faceRow.AddChild(TextButton("Cor na face", "Pinta só a face selecionada com a cor acima", () => PaintFace(clear: false)));
        faceRow.AddChild(TextButton("Limpar face", "Devolve a face ao material do objeto", () => PaintFace(clear: true)));
        var props = new HBoxContainer(); props.AddThemeConstantOverride("separation", 6); box.AddChild(props);
        props.AddChild(TextButton("Copiar propriedades", "Copia material e forma do objeto selecionado", CopyProperties));
        props.AddChild(TextButton("Colar propriedades", "Aplica material e forma copiados à seleção", PasteProperties));

        _imageDialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.png,*.jpg,*.jpeg,*.webp ; Imagens"], Title = "Importar textura" };
        _imageDialog.FileSelected += ImportImage;
        AddChild(_imageDialog);
        RefreshLibrary();
        return box;
    }

    // ---- the Recursos tab ------------------------------------------------------------------------------------

    private Control BuildResourcesTab()
    {
        var box = Section();
        box.AddChild(FieldLabel("Iluminação"));
        _lightOn = new CheckBox { Text = " Emitir luz", TooltipText = "O objeto passa a iluminar o que está perto (até 16 luzes acesas por vez no seu jogo)" }; box.AddChild(_lightOn);
        _lightColor = new ColorPickerButton { Color = new Color("#FFE0A8"), CustomMinimumSize = new Vector2(0, 40), EditAlpha = false }; box.AddChild(_lightColor);
        _lightEnergy = new HSlider { MinValue = 0, MaxValue = 8, Step = .1, Value = 1.5, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 28) };
        _lightRange = new HSlider { MinValue = .5, MaxValue = 30, Step = .5, Value = 8, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 28) };
        box.AddChild(FieldLabel("Intensidade (0 a 8)")); box.AddChild(_lightEnergy);
        box.AddChild(FieldLabel("Alcance (0,5 a 30 metros)")); box.AddChild(_lightRange);
        box.AddChild(TextButton("Aplicar luz à seleção", "Envia a luz ao servidor; desmarque Emitir luz para remover", ApplyLight, UiStyle.ButtonKind.Primary));
        var note = new Label { Text = "O brilho do material fica na aba Textura. Flexibilidade, partículas e animação de textura ainda não existem.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        note.AddThemeColorOverride("font_color", UiStyle.TextMuted); note.AddThemeFontSizeOverride("font_size", 15); box.AddChild(note);
        return box;
    }

    private void ApplyLight()
    {
        if (_selection.Count == 0) { _status.Text = "Selecione um objeto primeiro."; return; }
        var afters = new List<BuildObjectState>();
        foreach (var view in _selection)
        {
            var after = FromView(view); var material = ParseObject(after.MaterialJson);
            if (_lightOn.ButtonPressed) material["light"] = new JsonObject { ["color"] = "#" + _lightColor.Color.ToHtml(false), ["energy"] = Math.Round(_lightEnergy.Value, 1), ["range"] = Math.Round(_lightRange.Value, 1) };
            else material.Remove("light");
            after.MaterialJson = material.ToJsonString(); afters.Add(after);
        }
        CommitUpdates(afters);
        _status.Text = _lightOn.ButtonPressed ? "Luz aplicada." : "Luz removida.";
    }

    // ---- library ---------------------------------------------------------------------------------------------

    private void LoadLibrary()
    {
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is null || app is null || !session.IsAuthenticated) { _libraryStatus.Text = "Entre em uma ilha para ver suas texturas."; return; }
        if (_libraryHttp is null) { _libraryHttp = new HttpRequest { Timeout = 20 }; AddChild(_libraryHttp); _libraryHttp.RequestCompleted += OnLibraryLoaded; }
        if (_libraryHttp.GetHttpClientStatus() != Godot.HttpClient.Status.Disconnected) return;
        _libraryHttp.Request($"{app.ApiBaseUrl}/api/v1/inventory/items?category=textures&page=1&pageSize=100", [$"Authorization: Bearer {session.AccessToken}"]);
    }

    private void OnLibraryLoaded(long result, long code, string[] headers, byte[] body)
    {
        if (code is < 200 or >= 300) { _libraryStatus.Text = $"Não foi possível carregar as texturas (HTTP {code})."; return; }
        try { LoadLibraryForTesting(body.GetStringFromUtf8()); }
        catch (JsonException) { _libraryStatus.Text = "Resposta de texturas inválida."; }
    }

    /// Reads the inventory answer ({ items: [...] }) and keeps the entries that are images.
    public void LoadLibraryForTesting(string json)
    {
        _mine.Clear();
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("items", out var items))
            foreach (var item in items.EnumerateArray())
            {
                var mime = item.TryGetProperty("mimeType", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
                if (!mime.StartsWith("image/", StringComparison.Ordinal) || mime == "image/svg+xml") continue;
                if (!item.TryGetProperty("assetId", out var asset) || !Guid.TryParse(asset.GetString(), out var assetId)) continue;
                var name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "Textura" : "Textura";
                _mine.Add(new LibraryEntry(assetId, null, name, item.TryGetProperty("favorite", out var f) && f.ValueKind == JsonValueKind.True));
            }
        RefreshLibrary();
    }

    private void RefreshLibrary()
    {
        if (_library is null) return;
        _shown.Clear(); _library.Clear();
        switch (_librarySource.Selected)
        {
            case 1: _shown.AddRange(_mine.Where(x => x.Favorite)); break;
            case 2: _shown.AddRange(ReadRecent()); break;
            case 3: _shown.AddRange(BuiltInTextures.All.Select(x => new LibraryEntry(null, x.Id, x.Label, false))); break;
            default: _shown.AddRange(_mine); if (_mine.Count == 0 && _librarySource.Selected == 0) LoadLibrary(); break;
        }
        foreach (var entry in _shown) _library.AddItem(entry.Name);
        _libraryStatus.Text = _shown.Count == 0 ? (_librarySource.Selected switch { 0 => "Nenhuma textura ainda. Use Importar imagem.", 1 => "Nenhuma favorita. Favorite itens no inventário.", 2 => "Nada usado recentemente.", _ => "" }) : $"{_shown.Count} textura(s). Escolha uma e use Aplicar textura.";
        _preview.Texture = null;
    }

    public int LibraryCountForTesting => _shown.Count;
    public void SelectLibraryForTesting(int index, int source = -1) { if (source >= 0) { _librarySource.Select(source); RefreshLibrary(); } _library.Select(index); ShowPreview(index); }
    public void ApplyTextureForTesting(bool normal) => ApplyTexture(normal);
    public void RemoveTextureForTesting() => RemoveTexture();
    public void ApplyUvForTesting(float repeatX, float repeatY, float offsetX, float rotation, bool flipU) { _repeatX.Text = Num(repeatX); _repeatY.Text = Num(repeatY); _offsetX.Text = Num(offsetX); _uvRotation.Text = Num(rotation); _flipU.ButtonPressed = flipU; ApplyUv(); }
    public void ApplyLightForTesting(bool on, float energy, float range) { _lightOn.ButtonPressed = on; _lightEnergy.Value = energy; _lightRange.Value = range; ApplyLight(); }

    private LibraryEntry? Chosen()
    {
        var picked = _library.GetSelectedItems();
        return picked.Length == 1 && picked[0] < _shown.Count ? _shown[picked[0]] : null;
    }

    private void ShowPreview(int index)
    {
        if (index < 0 || index >= _shown.Count) return;
        var entry = _shown[index];
        _preview.Texture = null;
        if (entry.Builtin is { } builtin) _preview.Texture = BuiltInTextures.Get(builtin);
        else if (entry.Asset is { } asset) Textures.Request(asset, texture => { if (IsInstanceValid(_preview) && Chosen() == entry) _preview.Texture = texture; });
    }

    private void ImportImage(string path)
    {
        var size = Godot.FileAccess.FileExists(path) ? (long)Godot.FileAccess.GetFileAsBytes(path).Length : 0;
        if (size == 0) { _libraryStatus.Text = "Não foi possível ler o arquivo."; return; }
        if (size > 8L * 1024 * 1024) { _libraryStatus.Text = "A imagem passa de 8 MB; reduza o tamanho antes de importar."; return; }
        var inventory = GetParent()?.GetNodeOrNull<InventoryPanel>("InventoryPanel");
        if (inventory is null) { _libraryStatus.Text = "O inventário não está disponível."; return; }
        if (!_watchingInventory) { inventory.ItemsChanged += () => { if (_librarySource.Selected == 0) LoadLibrary(); }; _watchingInventory = true; }
        inventory.ImportTexture(path);
        _libraryStatus.Text = "Enviando a imagem… ela aparece em Minhas texturas quando terminar.";
    }

    // ---- recent textures -------------------------------------------------------------------------------------

    private static List<LibraryEntry> ReadRecent()
    {
        var list = new List<LibraryEntry>();
        try
        {
            var config = new ConfigFile();
            if (config.Load(PrefsPath) != Error.Ok) return list;
            foreach (var raw in (string[])config.GetValue("textures", "recent", Array.Empty<string>()))
            {
                var parts = raw.Split('|', 3);
                if (parts.Length == 3 && parts[0] == "asset" && Guid.TryParse(parts[1], out var asset)) list.Add(new LibraryEntry(asset, null, parts[2], false));
                else if (parts.Length == 3 && parts[0] == "builtin" && BuiltInTextures.All.Any(x => x.Id == parts[1])) list.Add(new LibraryEntry(null, parts[1], parts[2], false));
            }
        }
        catch (Exception) { /* preferences are a convenience: a damaged file just means no recents */ }
        return list;
    }

    private static void RememberRecent(LibraryEntry entry)
    {
        var line = entry.Builtin is { } builtin ? $"builtin|{builtin}|{entry.Name}" : $"asset|{entry.Asset}|{entry.Name}";
        var recent = ReadRecent().Select(x => x.Builtin is { } b ? $"builtin|{b}|{x.Name}" : $"asset|{x.Asset}|{x.Name}").Where(x => x != line).Prepend(line).Take(12).ToArray();
        var config = new ConfigFile(); config.Load(PrefsPath);
        config.SetValue("textures", "recent", recent);
        config.Save(PrefsPath);
    }

    // ---- applying to the object or to a face -----------------------------------------------------------------

    /// The material layer the tools write to: the selected face's entry when a face is selected, else the whole object's material.
    private JsonObject TargetLayer(JsonObject material)
    {
        if (_selectedFace < 0) return material;
        var faces = material["faces"] as JsonArray ?? [];
        foreach (var node in faces) if (node is JsonObject entry && entry["index"]?.GetValue<int>() == _selectedFace) { material["faces"] = faces; return entry; }
        var created = new JsonObject { ["index"] = _selectedFace };
        faces.Add(created); material["faces"] = faces;
        return created;
    }

    private bool EditTarget(Action<JsonObject> change, string done)
    {
        var view = _selectedFace >= 0 && _faceView is not null && IsInstanceValid(_faceView) ? _faceView : _selection.Count == 1 ? _selection[0] : null;
        if (view is null) { _status.Text = _selection.Count > 1 ? "Selecione um só objeto para aplicar texturas." : "Selecione um objeto primeiro."; return false; }
        var after = FromView(view); var material = ParseObject(after.MaterialJson);
        change(TargetLayer(material));
        after.MaterialJson = material.ToJsonString();
        var face = _selectedFace;
        CommitUpdates([after]);
        _selectedFace = face;
        _status.Text = done;
        return true;
    }

    private void ApplyTexture(bool normal)
    {
        if (Chosen() is not { } entry) { _status.Text = "Escolha uma textura na biblioteca."; return; }
        if (normal && entry.Builtin is not null) { _status.Text = "Texturas do sistema não servem de mapa normal; use uma imagem sua."; return; }
        var ok = EditTarget(layer =>
        {
            if (normal) layer["normalMap"] = entry.Asset!.Value.ToString();
            else
            {
                layer.Remove("texture"); layer.Remove("builtin");
                if (entry.Builtin is { } builtin) layer["builtin"] = builtin; else layer["texture"] = entry.Asset!.Value.ToString();
            }
        }, normal ? "Mapa normal aplicado." : "Textura aplicada.");
        if (ok && !normal) RememberRecent(entry);
    }

    private void RemoveTexture() => EditTarget(layer => { layer.Remove("texture"); layer.Remove("builtin"); layer.Remove("normalMap"); }, "Textura removida.");

    private void ApplyUv()
    {
        EditTarget(layer =>
        {
            layer["uvRepeatX"] = Mathf.Clamp(NumOr(_repeatX, 1), .01f, 100f); layer["uvRepeatY"] = Mathf.Clamp(NumOr(_repeatY, 1), .01f, 100f);
            layer["uvOffsetX"] = Mathf.Clamp(NumOr(_offsetX, 0), -100f, 100f); layer["uvOffsetY"] = Mathf.Clamp(NumOr(_offsetY, 0), -100f, 100f);
            layer["uvRotation"] = Mathf.Clamp(NumOr(_uvRotation, 0), -360f, 360f);
            if (_flipU.ButtonPressed) layer["flipU"] = true; else layer.Remove("flipU");
            if (_flipV.ButtonPressed) layer["flipV"] = true; else layer.Remove("flipV");
        }, "UV aplicado.");
    }

    /// Shows the target's UV and the light of the selected object in the fields.
    private void FillTextureFields()
    {
        if (_repeatX is null) return;
        if (_selection.Count != 1) { _target.Text = _selection.Count == 0 ? "Nada selecionado" : "Selecione um só objeto"; return; }
        var material = ParseObject(_selection[0].State.MaterialJson);
        var layer = material;
        if (_selectedFace >= 0 && material["faces"] is JsonArray faces) foreach (var node in faces) if (node is JsonObject entry && entry["index"]?.GetValue<int>() == _selectedFace) layer = entry;
        _target.Text = _selectedFace >= 0 ? $"Face {_selectedFace}" : "Objeto inteiro";
        _repeatX.Text = Num(Read(layer, "uvRepeatX", 1)); _repeatY.Text = Num(Read(layer, "uvRepeatY", 1));
        _offsetX.Text = Num(Read(layer, "uvOffsetX", 0)); _offsetY.Text = Num(Read(layer, "uvOffsetY", 0)); _uvRotation.Text = Num(Read(layer, "uvRotation", 0));
        _flipU.ButtonPressed = layer["flipU"] is JsonValue u && u.TryGetValue<bool>(out var fu) && fu; _flipV.ButtonPressed = layer["flipV"] is JsonValue v && v.TryGetValue<bool>(out var fv) && fv;
        _glowStrength.Value = Mathf.Clamp(Read(material, "emissionStrength", 1f) / 8f, 0, 1);
        var light = material["light"] as JsonObject;
        _lightOn.ButtonPressed = light is not null;
        if (light is not null) { _lightEnergy.Value = Read(light, "energy", 1.5f); _lightRange.Value = Read(light, "range", 8f); if (light["color"]?.GetValue<string>() is { } c) _lightColor.Color = Color.FromHtml(c); }
    }
}
