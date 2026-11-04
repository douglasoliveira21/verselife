using System.Globalization;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Models;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts;

public sealed record MirrorLook(Guid Id, string Name);

/// <summary>
/// The appearance editor inside the world: a window beside the avatar, and the camera put in front of it (the face or the whole body, turnable) like a
/// mirror. Every change shows on the avatar at once, only on this screen, until it is saved; closing without saving puts the saved look back.
/// The full editor (custom bodies, animations, importing items) stays in the character screen.
/// </summary>
public sealed partial class AppearanceWindow : Node
{
    private static readonly string[] BodyOrder = ["height", "shoulder", "torso", "arms", "legs", "head", "hips"];
    private static readonly string[] FaceOrder = ["eyeScale", "eyeSpacing", "noseSize", "mouthWidth", "jawWidth", "browHeight", "headWidth"];
    private static readonly Dictionary<string, string> BodyLabels = new() { ["height"] = "Altura", ["shoulder"] = "Ombros", ["torso"] = "Tronco", ["arms"] = "Braços", ["legs"] = "Pernas", ["head"] = "Cabeça", ["hips"] = "Quadril" };
    private static readonly Dictionary<string, string> FaceLabels = new() { ["eyeScale"] = "Tamanho dos olhos", ["eyeSpacing"] = "Distância entre os olhos", ["noseSize"] = "Nariz", ["mouthWidth"] = "Boca", ["jawWidth"] = "Maxilar", ["browHeight"] = "Sobrancelhas", ["headWidth"] = "Largura da cabeça" };
    private static readonly string[] WearSlots = ["top", "jacket", "bottom", "shoes", "hat", "glasses", "neck", "ears", "gloves", "back", "accessory", "hair", "eyes"];
    private static readonly Dictionary<string, string> SlotLabels = new()
    {
        ["top"] = "Parte de cima", ["jacket"] = "Casaco", ["bottom"] = "Parte de baixo", ["shoes"] = "Sapatos", ["hat"] = "Chapéu", ["glasses"] = "Óculos", ["neck"] = "Pescoço", ["ears"] = "Orelhas",
        ["gloves"] = "Luvas", ["back"] = "Costas", ["accessory"] = "Acessório", ["hair"] = "Cabelo seu (peça própria)", ["eyes"] = "Olhos seus (peça própria)"
    };
    private static readonly Dictionary<string, string> DefaultTints = new()
    {
        ["top"] = "#3AAFC1", ["bottom"] = "#263B56", ["shoes"] = "#E7E9EE", ["accessory"] = "#E9C46A", ["jacket"] = "#5B4636", ["hat"] = "#B23A48",
        ["glasses"] = "#222222", ["neck"] = "#E9C46A", ["ears"] = "#E9C46A", ["gloves"] = "#222222", ["back"] = "#7A5C3E", ["hair"] = "#FFFFFF", ["eyes"] = "#FFFFFF"
    };
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    private sealed record Wearable(Guid Id, string Name, string Slot, string Layer, string Kind, string Key, string Hash, string[] Covers, bool HidesHair, string AttachBone, float[] Offset, float[] Rotation, float Scale, string Tint, int Triangles);

    private FloatingWindow _window = null!;
    private UiTabs _tabs = null!;
    private readonly List<Control> _pages = [];
    private Label _status = null!;
    private OptionButton _baseModel = null!, _hairStyle = null!, _beard = null!;
    private ColorPickerButton _skin = null!, _hairColour = null!, _eyeColour = null!;
    private readonly Dictionary<string, HSlider> _body = [], _face = [];
    private readonly Dictionary<string, OptionButton> _menus = [];
    private ItemList _looks = null!; private LineEdit _lookName = null!;
    private Button _save = null!;
    private readonly List<Wearable> _wearables = [];
    private readonly List<MirrorLook> _lookList = [];
    private AvatarAppearanceData _draft = AvatarManager.CreateDefault();
    private bool _syncing, _dirty, _previewQueued;

    /// <summary>The player whose camera is the mirror and whose avatar shows the changes (set by the world screen).</summary>
    public PlayerController? Player { get; set; }
    /// <summary>Tests: answers requests without a network (path after /api/v1/users/me/avatar), instead of the avatar manager's own transport.</summary>
    public Func<Godot.HttpClient.Method, string, string?, (int Status, string Body)>? Transport { get; set; }

    public bool Visible => _window.Visible;
    public bool Dirty => _dirty;
    public AvatarAppearanceData Draft => _draft;
    public string StatusText => _status.Text;
    public IReadOnlyList<MirrorLook> Looks => _lookList;

    public override void _Ready() => Build();

    public void Toggle() { if (_window.Visible) Close(); else Open(); }

    /// <summary>Opens the window with the saved look, loads the wearables and looks of the person, and puts the camera in front of the avatar.</summary>
    public void Open()
    {
        _draft = Manager().Current.Copy(); _dirty = false;
        _window.Visible = true; _window.MoveToFront();
        Player?.EnterMirror(true);
        LoadWearables(); LoadLooks(); Sync(); Say("Mude o visual: aparece no seu avatar na hora. Salve para guardar.");
    }

    /// <summary>Closes the window. What was not saved is undone on the avatar, and the camera goes back.</summary>
    public void Close()
    {
        if (!_window.Visible) return;
        _window.Visible = false;
        if (_dirty) { Player?.ShowAppearance(Manager().Current); }
        _dirty = false;
        Player?.ExitMirror();
    }

    private AvatarManager Manager() => GetNode<AvatarManager>("/root/AvatarManager");

    private void Call(Godot.HttpClient.Method method, string path, string? body, Action<int, string> done)
    {
        if (Transport is not null) { var (status, text) = Transport(method, path, body); done(status, text); return; }
        Manager().Call(method, path, body, done);
    }

    private void Say(string text) => _status.Text = text;

    // ---- the draft ----------------------------------------------------------------------------------------------------------------------

    private static string Key(OptionButton menu) => menu.ItemCount > 0 && menu.Selected >= 0 ? menu.GetItemMetadata(menu.Selected).AsString() : "none";
    private static void Choose(OptionButton menu, string key) { for (var i = 0; i < menu.ItemCount; i++) if (menu.GetItemMetadata(i).AsString() == key) { menu.Select(i); return; } }

    private WearableItem? Resolver(string slot, string key) => _wearables.FirstOrDefault(w => w.Key == key && w.Slot == slot) is { } w ? new WearableItem(w.Slot, w.Key, w.Name, w.Layer, w.Covers.Length > 0 ? w.Covers : null, w.HidesHair) : null;

    /// <summary>Puts the controls as the draft says.</summary>
    private void Sync()
    {
        _syncing = true;
        Choose(_baseModel, _draft.BaseModel); Choose(_hairStyle, _draft.HairStyle); Choose(_beard, _draft.BeardStyle);
        _skin.Color = Color.FromHtml(_draft.SkinColor); _hairColour.Color = Color.FromHtml(_draft.HairColor); _eyeColour.Color = Color.FromHtml(_draft.EyeColor);
        var body = AvatarRules.ReadSettings(_draft.BodySettingsJson, AvatarRules.BodyRanges); foreach (var (key, slider) in _body) slider.Value = body[key];
        var face = AvatarRules.ReadSettings(_draft.FaceSettingsJson, AvatarRules.FaceRanges); foreach (var (key, slider) in _face) slider.Value = face[key];
        RefreshMenus();
        _syncing = false;
    }

    /// <summary>The menus of the wardrobe: the catalog of each slot, and the person's own items (marked with a star) that were made for it.</summary>
    private void RefreshMenus()
    {
        foreach (var (slot, menu) in _menus)
        {
            menu.Clear();
            foreach (var item in AvatarRules.ItemsFor(slot)) { menu.AddItem(item.Label); menu.SetItemMetadata(menu.ItemCount - 1, item.Key); }
            foreach (var w in _wearables.Where(x => x.Slot == slot)) { menu.AddItem("★ " + w.Name); menu.SetItemMetadata(menu.ItemCount - 1, w.Key); }
            Choose(menu, _draft.Item(slot, slot is "top" ? "tee_basic" : slot is "bottom" ? "pants_basic" : slot is "shoes" ? "sneakers_basic" : "none"));
        }
    }

    private List<EquippedItemData> WornFromMenus(string? changed)
    {
        var worn = _menus.Select(m => (m.Key, Key(m.Value))).ToList();
        return [.. AvatarRules.Resolve(worn, changed, Resolver).Where(w => w.Slot is "top" or "bottom" or "shoes" || w.Key != "none")
            .Select(w => new EquippedItemData { Slot = w.Slot, ItemKey = w.Key, TintColor = _draft.Tint(w.Slot, DefaultTints.GetValueOrDefault(w.Slot, "#FFFFFF")) })];
    }

    /// <summary>The custom items worn in the draft, as the server describes them, so the avatar downloads and draws the same files.</summary>
    private string WornJson()
    {
        var worn = new List<WornWearable>(); var known = _draft.CustomWearables.ToDictionary(w => w.Key); // what the server already described stays until the list of items arrives
        foreach (var item in _draft.EquippedItems.Where(i => AvatarRules.IsCustom(i.ItemKey)))
            if (!_wearables.Any(w => w.Key == item.ItemKey) && known.TryGetValue(item.ItemKey, out var already)) worn.Add(already);
            else if (_wearables.FirstOrDefault(w => w.Key == item.ItemKey) is { } w)
                worn.Add(new WornWearable { Id = w.Id, Key = w.Key, Hash = w.Hash, Slot = w.Slot, Layer = w.Layer, Kind = w.Kind, HidesHair = w.HidesHair, Covers = w.Covers, Triangles = w.Triangles, AttachBone = w.AttachBone, Offset = w.Offset, Rotation = w.Rotation, Scale = w.Scale, Tint = w.Tint });
        return JsonSerializer.Serialize(worn);
    }

    /// <summary>A control changed: read everything into the draft and show it on the avatar (once per frame, however many sliders move).</summary>
    private void Changed(string? wornSlot = null)
    {
        if (_syncing) return;
        _draft.BaseModel = Key(_baseModel); _draft.HairStyle = Key(_hairStyle); _draft.BeardStyle = Key(_beard);
        _draft.SkinColor = "#" + _skin.Color.ToHtml(false); _draft.HairColor = "#" + _hairColour.Color.ToHtml(false); _draft.EyeColor = "#" + _eyeColour.Color.ToHtml(false);
        _draft.BodySettingsJson = AvatarRules.WriteSettings(BodyOrder.ToDictionary(k => k, k => (float)_body[k].Value), AvatarRules.BodyRanges);
        _draft.FaceSettingsJson = AvatarRules.WriteSettings(FaceOrder.ToDictionary(k => k, k => (float)_face[k].Value), AvatarRules.FaceRanges);
        _draft.EquippedItems = WornFromMenus(wornSlot);
        if (wornSlot is not null) { _syncing = true; var resolved = _draft.EquippedItems.ToDictionary(i => i.Slot, i => i.ItemKey); foreach (var (slot, menu) in _menus) Choose(menu, resolved.GetValueOrDefault(slot, "none")); _syncing = false; }
        _draft.CustomWearablesJson = WornJson();
        _dirty = true; _save.Disabled = false;
        if (!_previewQueued) { _previewQueued = true; Callable.From(ShowDraft).CallDeferred(); }
    }

    private void ShowDraft() { _previewQueued = false; Player?.ShowAppearance(_draft); }

    /// <summary>Tests and the world screen: shows the draft on the avatar now.</summary>
    public void FlushPreview() { if (_previewQueued) ShowDraft(); }

    // ---- talking to the server ------------------------------------------------------------------------------------------------------------

    private static Wearable? ReadWearable(JsonElement item)
    {
        string Text(string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
        float[] Numbers(string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? [.. v.EnumerateArray().Select(x => x.GetSingle())] : [0, 0, 0];
        if (!Guid.TryParse(Text("id"), out var id)) return null;
        return new Wearable(id, Text("name"), Text("slot"), Text("layer") is { Length: > 0 } layer ? layer : "base", Text("kind"), Text("key"), Text("hash"),
            item.TryGetProperty("covers", out var c) && c.ValueKind == JsonValueKind.Array ? [.. c.EnumerateArray().Select(x => x.GetString() ?? string.Empty)] : [],
            item.TryGetProperty("hidesHair", out var h) && h.ValueKind == JsonValueKind.True, Text("attachBone"), Numbers("offset"), Numbers("rotation"), item.TryGetProperty("scale", out var s) && s.TryGetSingle(out var sv) ? sv : 1f, Text("tint"),
            item.TryGetProperty("triangles", out var t) && t.TryGetInt32(out var tv) ? tv : 0);
    }

    public void LoadWearables() => Call(Godot.HttpClient.Method.Get, "/wearables", null, (status, body) =>
    {
        if (status is < 200 or >= 300) return;
        _wearables.Clear();
        try { using var d = JsonDocument.Parse(body); foreach (var item in d.RootElement.EnumerateArray()) if (ReadWearable(item) is { } w) _wearables.Add(w); }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException) { _wearables.Clear(); }
        _syncing = true; RefreshMenus(); _syncing = false;
        if (_dirty is false) { _draft.CustomWearablesJson = WornJson(); }
    });

    public void LoadLooks() => Call(Godot.HttpClient.Method.Get, "/looks", null, (status, body) =>
    {
        if (status is < 200 or >= 300) return;
        _lookList.Clear(); _looks.Clear();
        try
        {
            using var d = JsonDocument.Parse(body);
            foreach (var item in d.RootElement.EnumerateArray())
                if (Guid.TryParse(item.GetProperty("id").GetString(), out var id)) { var name = item.GetProperty("name").GetString() ?? "?"; _lookList.Add(new MirrorLook(id, name)); _looks.AddItem(name); }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { _lookList.Clear(); _looks.Clear(); }
    });

    private static string Problem(int status, string body)
    {
        try
        {
            using var d = JsonDocument.Parse(body);
            if (d.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                foreach (var field in errors.EnumerateObject()) if (field.Value.ValueKind == JsonValueKind.Array && field.Value.GetArrayLength() > 0) return "O servidor não aceitou: " + field.Value[0].GetString();
            if (d.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) return "O servidor não aceitou: " + e.GetString();
        }
        catch (JsonException) { }
        return status == 0 ? "Sem conexão com o servidor." : status == 401 ? "Entre na sua conta de novo." : $"Não foi possível concluir (HTTP {status}).";
    }

    /// <summary>Takes what the server sent back as the saved look (it holds everything the others need to draw it) and shows it.</summary>
    private bool Adopt(string body)
    {
        if (AvatarManager.ParseAppearance(body) is not { } value) return false;
        value.DisplayName = string.IsNullOrWhiteSpace(value.DisplayName) ? _draft.DisplayName : value.DisplayName;
        Manager().SetCurrent(value); _draft = value.Copy(); _dirty = false; _save.Disabled = true; Player?.ShowAppearance(value);
        Sync();
        return true;
    }

    public void SaveNow(Action? then = null)
    {
        Call(Godot.HttpClient.Method.Put, string.Empty, JsonSerializer.Serialize(_draft), (status, body) =>
        {
            if (status is < 200 or >= 300) { Say(Problem(status, body)); return; }
            if (!Adopt(body)) { Manager().SetCurrent(_draft.Copy()); _dirty = false; _save.Disabled = true; }
            Say("Visual salvo. Todos na ilha já veem.");
            then?.Invoke();
        });
    }

    /// <summary>Puts the saved look back on the avatar and the controls.</summary>
    public void Undo() { _draft = Manager().Current.Copy(); _dirty = false; _save.Disabled = true; Player?.ShowAppearance(_draft); Sync(); Say("Desfeito: voltou ao visual salvo."); }

    public void ApplyLook(int index)
    {
        if (index < 0 || index >= _lookList.Count) return;
        var look = _lookList[index];
        Call(Godot.HttpClient.Method.Post, $"/looks/{look.Id}/apply", "{}", (status, body) =>
        {
            if (status is < 200 or >= 300) { Say(Problem(status, body)); return; }
            if (Adopt(body)) Say($"Look “{look.Name}” aplicado e salvo." + CharacterCreator.SkippedText(body)); else Say("Resposta inesperada do servidor.");
        });
    }

    public void SaveAsLook()
    {
        var name = _lookName.Text.Trim(); if (name.Length == 0) { Say("Dê um nome ao look."); return; }
        SaveNow(() => Call(Godot.HttpClient.Method.Post, "/looks", JsonSerializer.Serialize(new { name }), (status, body) =>
        {
            if (status is >= 200 and < 300) { Say($"Look “{name}” salvo."); _lookName.Text = string.Empty; LoadLooks(); } else Say(Problem(status, body));
        }));
    }

    // ---- building the window -------------------------------------------------------------------------------------------------------------------------

    private static Button Act(string text, UiStyle.ButtonKind kind, Action action) { var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 36), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None }; UiStyle.StyleButton(b, kind, 10, 6); b.AddThemeFontSizeOverride("font_size", 15); b.Pressed += action; return b; }
    private static OptionButton Choice(params (string Label, string Value)[] items) { var o = new OptionButton { CustomMinimumSize = new Vector2(0, 34), FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; foreach (var (label, value) in items) { o.AddItem(label); o.SetItemMetadata(o.ItemCount - 1, value); } return o; }
    private static ColorPickerButton Colour(string html) => new() { Color = Color.FromHtml(html), CustomMinimumSize = new Vector2(90, 32), EditAlpha = false, FocusMode = Control.FocusModeEnum.None };
    private static Label Caption(string text) { var l = new Label { Text = text }; l.AddThemeFontSizeOverride("font_size", 15); return l; }
    private static HBoxContainer Row(params Control[] children) { var r = new HBoxContainer(); r.AddThemeConstantOverride("separation", 8); foreach (var c in children) r.AddChild(c); return r; }

    private void Slider(VBoxContainer box, Dictionary<string, HSlider> store, string key, string label, AvatarRules.Range range)
    {
        var slider = new HSlider { MinValue = range.Min, MaxValue = range.Max, Step = .01, Value = range.Default, CustomMinimumSize = new Vector2(150, 24), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        slider.ValueChanged += _ => Changed();
        var name = Caption(label); name.CustomMinimumSize = new Vector2(150, 0); box.AddChild(Row(name, slider)); store[key] = slider;
    }

    private VBoxContainer Page()
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 6);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Visible = false, CustomMinimumSize = new Vector2(0, 300) };
        scroll.AddChild(box); _pages.Add(scroll); return box;
    }

    private void Build()
    {
        _window = new FloatingWindow("Espelho · aparência", icon: "people") { Name = "AppearanceWindow", Visible = false };
        _window.Place(1f, .5f, -440, -320, -16, 320);
        _window.CloseRequested += Close;
        var content = _window.Content; content.AddThemeConstantOverride("separation", 8);

        var camera = Row(Act("Rosto", UiStyle.ButtonKind.Secondary, () => Player?.SetMirrorView(true)), Act("Corpo inteiro", UiStyle.ButtonKind.Secondary, () => Player?.SetMirrorView(false)),
            Act("◀ Girar", UiStyle.ButtonKind.Secondary, () => Player?.TurnMirror(-Mathf.Pi / 4)), Act("Girar ▶", UiStyle.ButtonKind.Secondary, () => Player?.TurnMirror(Mathf.Pi / 4)));
        content.AddChild(camera);

        _tabs = new UiTabs { EqualWidth = true, Flat = true, FontSize = 15 }; foreach (var t in new[] { "Corpo", "Rosto", "Roupas", "Looks" }) _tabs.AddTab(t); content.AddChild(_tabs);
        var stack = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(420, 310) }; content.AddChild(stack);

        // body
        var body = Page(); _baseModel = Choice(("Clássico", "human_a"), ("Atlético", "human_b")); _baseModel.ItemSelected += _ => Changed();
        body.AddChild(Row(Caption("Corpo base"), _baseModel));
        foreach (var key in BodyOrder) Slider(body, _body, key, BodyLabels[key], AvatarRules.BodyRanges[key]);
        body.AddChild(Row(Caption("Pele"), _skin = Colour("#C98F65"))); _skin.ColorChanged += _ => Changed();

        // face and hair
        var face = Page();
        foreach (var key in FaceOrder) Slider(face, _face, key, FaceLabels[key], AvatarRules.FaceRanges[key]);
        _hairStyle = Choice(("Curto", "short"), ("Chanel", "bob"), ("Moicano", "mohawk"), ("Careca", "bald"), ("Longo", "long"), ("Rabo de cavalo", "ponytail")); _hairStyle.ItemSelected += _ => Changed();
        _beard = Choice(("Sem barba", "none"), ("Barba por fazer", "stubble"), ("Barba curta", "short"), ("Barba cheia", "full")); _beard.ItemSelected += _ => Changed();
        face.AddChild(Row(Caption("Cabelo"), _hairStyle)); face.AddChild(Row(Caption("Barba"), _beard));
        face.AddChild(Row(Caption("Cor do cabelo"), _hairColour = Colour("#2B1B14"), Caption("Cor dos olhos"), _eyeColour = Colour("#4A3326")));
        _hairColour.ColorChanged += _ => Changed(); _eyeColour.ColorChanged += _ => Changed();

        // wardrobe
        var wear = Page();
        foreach (var slot in WearSlots)
        {
            var menu = new OptionButton { CustomMinimumSize = new Vector2(0, 34), FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var captured = slot; menu.ItemSelected += _ => Changed(captured); _menus[slot] = menu;
            var name = Caption(SlotLabels[slot]); name.CustomMinimumSize = new Vector2(150, 0); wear.AddChild(Row(name, menu));
        }
        var hint = Caption("Itens com ★ são seus. Para importar roupas, corpos e animações, use o editor completo."); hint.AutowrapMode = TextServer.AutowrapMode.WordSmart; hint.CustomMinimumSize = new Vector2(380, 0); hint.AddThemeColorOverride("font_color", UiStyle.TextMuted); wear.AddChild(hint);

        // looks
        var looks = Page();
        looks.AddChild(Caption("Seus looks salvos")); _looks = new ItemList { CustomMinimumSize = new Vector2(0, 150), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _looks.AddThemeFontSizeOverride("font_size", 15); looks.AddChild(_looks);
        looks.AddChild(Act("Aplicar o look escolhido", UiStyle.ButtonKind.Primary, () => { var p = _looks.GetSelectedItems(); if (p.Length > 0) ApplyLook(p[0]); else Say("Escolha um look na lista."); }));
        _lookName = new LineEdit { PlaceholderText = "Nome do novo look", MaxLength = 40, CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; looks.AddChild(Row(_lookName, Act("Salvar como look", UiStyle.ButtonKind.Secondary, SaveAsLook)));

        foreach (var page in _pages) { page.SetAnchorsPreset(Control.LayoutPreset.FullRect); stack.AddChild(page); }
        _pages[0].Visible = true;
        _tabs.TabChanged += i => { for (var p = 0; p < _pages.Count; p++) _pages[p].Visible = p == i; };

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 0) }; _status.AddThemeColorOverride("font_color", UiStyle.AccentText); _status.AddThemeFontSizeOverride("font_size", 15); content.AddChild(_status);
        _save = Act("Salvar", UiStyle.ButtonKind.Primary, () => SaveNow()); _save.Disabled = true;
        content.AddChild(Row(_save, Act("Desfazer", UiStyle.ButtonKind.Secondary, Undo), Act("Editor completo…", UiStyle.ButtonKind.Secondary, OpenFullEditor), Act("Fechar", UiStyle.ButtonKind.Secondary, Close)));
        AddChild(_window);
    }

    private void OpenFullEditor()
    {
        var leave = () => { Close(); GetNode<UIManager>("/root/UIManager").ShowCharacterCreator(); };
        if (_dirty) SaveNow(leave); else leave();
    }

    // ---- hooks for tests ---------------------------------------------------------------------------------------------------------------------------------

    public void SetSliderForTesting(string key, double value) { var slider = _body.TryGetValue(key, out var b) ? b : _face[key]; slider.Value = value; }
    public void ChooseForTesting(string control, string key)
    {
        var menu = control switch { "hairstyle" => _hairStyle, "beard" => _beard, "base" => _baseModel, _ => _menus[control] };
        Choose(menu, key); if (_menus.ContainsKey(control)) Changed(control); else Changed();
    }
    public void SetColourForTesting(string which, string html) { var picker = which switch { "skin" => _skin, "eyes" => _eyeColour, _ => _hairColour }; picker.Color = Color.FromHtml(html); Changed(); }
    public int MenuCountForTesting(string slot) => _menus[slot].ItemCount;
    public string MenuKeyForTesting(string slot) => Key(_menus[slot]);
    public bool SaveEnabledForTesting => !_save.Disabled;
    public void SelectLookForTesting(int index) => _looks.Select(index);
    public void SetLookNameForTesting(string name) => _lookName.Text = name;
    public void OpenFullEditorForTesting() => OpenFullEditor();
}
