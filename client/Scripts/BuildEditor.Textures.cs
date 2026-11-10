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

    private void ApplyLight()
    {
        if (_selection.Count == 0) { _status.Text = "Selecione um objeto primeiro."; return; }
        var afters = new List<BuildObjectState>();
        foreach (var view in _selection)
        {
            var after = FromView(view); var material = ParseObject(after.MaterialJson);
            WriteResources(material);
            after.MaterialJson = material.ToJsonString(); afters.Add(after);
        }
        CommitUpdates(afters);
        _status.Text = "Recursos aplicados.";
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
        switch (_source)
        {
            case 1: _shown.AddRange(_mine.Where(x => x.Favorite)); break;
            case 2: _shown.AddRange(ReadRecent()); break;
            case 3: _shown.AddRange(BuiltInTextures.All.Select(x => new LibraryEntry(null, x.Id, x.Label, false))); break;
            default: _shown.AddRange(_mine); if (_mine.Count == 0 && _source == 0) LoadLibrary(); break;
        }
        for (var i = 0; i < _shown.Count; i++)
        {
            var entry = _shown[i]; var slot = _library.AddItem(string.Empty, entry.Builtin is { } builtin ? BuiltInTextures.Get(builtin) ?? Placeholder : Placeholder, true);
            _library.SetItemTooltip(slot, entry.Name);
            if (entry.Asset is { } asset) Textures.Request(asset, texture => { if (texture is not null && IsInstanceValid(_library) && slot < _library.ItemCount && slot < _shown.Count && _shown[slot] == entry) _library.SetItemIcon(slot, texture); });
        }
        _libraryStatus.Text = _shown.Count == 0 ? (_source switch { 0 => "Nenhuma textura ainda. Use Importar imagem.", 1 => "Nenhuma favorita. Favorite itens no inventário.", 2 => "Nada usado recentemente.", _ => "" }) : $"{_shown.Count} textura(s). Escolha uma e use Aplicar textura.";
        _preview.Texture = null; _previewName.Text = "—"; _previewInfo.Text = string.Empty;
    }

    public int LibraryCountForTesting => _shown.Count;
    public void SelectLibraryForTesting(int index, int source = -1) { if (source >= 0) SelectSource(source); _library.Select(index); ShowPreview(index); }
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
        _preview.Texture = null; _previewName.Text = entry.Name; _previewInfo.Text = string.Empty;
        void Shown(Texture2D? texture) { if (texture is null || !IsInstanceValid(_preview) || Chosen() != entry) return; _preview.Texture = texture; _previewInfo.Text = $"{texture.GetWidth()} × {texture.GetHeight()}"; }
        if (entry.Builtin is { } builtin) Shown(BuiltInTextures.Get(builtin));
        else if (entry.Asset is { } asset) Textures.Request(asset, Shown);
    }

    private void ImportImage(string path)
    {
        var size = Godot.FileAccess.FileExists(path) ? (long)Godot.FileAccess.GetFileAsBytes(path).Length : 0;
        if (size == 0) { _libraryStatus.Text = "Não foi possível ler o arquivo."; return; }
        if (size > 8L * 1024 * 1024) { _libraryStatus.Text = "A imagem passa de 8 MB; reduza o tamanho antes de importar."; return; }
        var inventory = GetParent()?.GetNodeOrNull<InventoryPanel>("InventoryPanel");
        if (inventory is null) { _libraryStatus.Text = "O inventário não está disponível."; return; }
        if (!_watchingInventory) { inventory.ItemsChanged += () => { if (_source == 0) LoadLibrary(); }; _watchingInventory = true; }
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

    /// The material layer the tools write to for one face (its entry, made when missing), or the whole object's material when face is -1.
    private static JsonObject TargetLayer(JsonObject material, int face)
    {
        if (face < 0) return material;
        var faces = material["faces"] as JsonArray ?? [];
        foreach (var node in faces) if (node is JsonObject entry && entry["index"]?.GetValue<int>() == face) { material["faces"] = faces; return entry; }
        var created = new JsonObject { ["index"] = face };
        faces.Add(created); material["faces"] = faces;
        return created;
    }

    private bool EditTarget(Action<JsonObject> change, string done)
    {
        var onFaces = _faceSet.Count > 0 && _faceView is not null && IsInstanceValid(_faceView);
        var view = onFaces ? _faceView : _selection.Count == 1 ? _selection[0] : null;
        if (view is null) { _status.Text = _selection.Count > 1 ? "Selecione um só objeto para aplicar texturas." : "Selecione um objeto primeiro."; return false; }
        var after = FromView(view); var material = ParseObject(after.MaterialJson);
        if (onFaces) foreach (var face in _faceSet.ToArray()) change(TargetLayer(material, face)); else change(material);
        if (view.State.PrimitiveType.Equals("mesh", StringComparison.OrdinalIgnoreCase)) material["override"] = true; // an imported model keeps its own materials until the person textures it
        after.MaterialJson = material.ToJsonString();
        var kept = _selectedFace;
        CommitUpdates([after]);
        _selectedFace = kept;
        _status.Text = onFaces && _faceSet.Count > 1 ? done.TrimEnd('.') + $" em {_faceSet.Count} faces." : done;
        return true;
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
        _appliedLabel.Text = layer["texture"]?.GetValue<string>() is { } applied ? (_mine.FirstOrDefault(x => x.Asset?.ToString() == applied)?.Name ?? "imagem") : layer["builtin"]?.GetValue<string>() is { } builtinId ? (BuiltInTextures.All.FirstOrDefault(x => x.Id == builtinId).Label ?? builtinId) : "nenhuma";
        _repeatX.Text = Disp(Read(layer, "uvRepeatX", 1), 2); _repeatY.Text = Disp(Read(layer, "uvRepeatY", 1), 2);
        _offsetX.Text = Disp(Read(layer, "uvOffsetX", 0), 2); _offsetY.Text = Disp(Read(layer, "uvOffsetY", 0), 2); _uvRotation.Text = Disp(Read(layer, "uvRotation", 0), 2);
        _flipU.ButtonPressed = layer["flipU"] is JsonValue u && u.TryGetValue<bool>(out var fu) && fu; _flipV.ButtonPressed = layer["flipV"] is JsonValue v && v.TryGetValue<bool>(out var fv) && fv;
        _glowStrength.Value = Mathf.Clamp(Read(material, "emissionStrength", 1f) / 8f, 0, 1);
        ShowAppliedThumb(layer); RefreshTargetCard(); FillMapControls(layer, material);
        FillAdvancedMaterial(layer, material);
        var light = material["light"] as JsonObject;
        _lightOn.ButtonPressed = light is not null;
        if (light is not null) { _lightEnergy.Value = Read(light, "energy", 1.5f); _lightRange.Value = Read(light, "range", 8f); if (light["color"]?.GetValue<string>() is { } c) _lightColor.Color = Color.FromHtml(c); }
    }
}
