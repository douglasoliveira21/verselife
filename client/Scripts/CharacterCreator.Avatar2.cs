using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Models;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts;

/// <summary>One saved look as the server lists it.</summary>
public sealed record LookEntry(Guid Id, string Name, string? ShareCode, string SkinColor, string HairStyle);

/// <summary>
/// Avatar 2.0 in the character editor: body and face sliders, every wearable slot with conflict handling (a dress takes the legs, an outer layer
/// needs a top) and the manager of saved looks (save, put on, duplicate, delete, share with a code, import a code).
/// </summary>
public partial class CharacterCreator
{
    private static readonly string[] BodyOrder = ["height", "shoulder", "torso", "arms", "legs", "head", "hips"];
    private static readonly string[] FaceOrder = ["eyeScale", "eyeSpacing", "noseSize", "mouthWidth", "jawWidth", "browHeight", "headWidth"];
    private static readonly Dictionary<string, string> BodyLabels = new() { ["height"] = "Altura", ["shoulder"] = "Ombros", ["torso"] = "Tronco", ["arms"] = "Braços", ["legs"] = "Pernas", ["head"] = "Cabeça", ["hips"] = "Quadril" };
    private static readonly Dictionary<string, string> FaceLabels = new() { ["eyeScale"] = "Tamanho dos olhos", ["eyeSpacing"] = "Distância entre os olhos", ["noseSize"] = "Nariz", ["mouthWidth"] = "Boca", ["jawWidth"] = "Maxilar", ["browHeight"] = "Sobrancelhas", ["headWidth"] = "Largura da cabeça" };
    private static readonly string[] ExtraSlots = ["jacket", "hat", "glasses", "neck", "ears", "gloves", "back"];
    private static readonly Dictionary<string, string> SlotLabels = new() { ["jacket"] = "Casaco (camada de fora)", ["hat"] = "Chapéu", ["glasses"] = "Óculos", ["neck"] = "Pescoço", ["ears"] = "Orelhas", ["gloves"] = "Luvas", ["back"] = "Costas" };
    private static readonly Dictionary<string, string> DefaultTints = new()
    {
        ["top"] = "#3AAFC1", ["bottom"] = "#263B56", ["shoes"] = "#E7E9EE", ["accessory"] = "#E9C46A", ["jacket"] = "#5B4636", ["hat"] = "#B23A48",
        ["glasses"] = "#222222", ["neck"] = "#E9C46A", ["ears"] = "#E9C46A", ["gloves"] = "#222222", ["back"] = "#7A5C3E"
    };

    private readonly Dictionary<string, HSlider> _body = [], _face = [];
    private readonly Dictionary<string, OptionButton> _extra = [];
    private readonly Dictionary<string, string> _tints = new(DefaultTints);
    private ItemList _looks = null!;
    private LineEdit _lookName = null!, _lookCode = null!, _importCode = null!;
    private Label _lookMessage = null!;
    private readonly List<LookEntry> _lookEntries = [];
    private bool _syncing;

    private static HSlider Range(Dictionary<string, HSlider> map, VBoxContainer fields, string key, string label, AvatarRules.Range range, Action changed)
    {
        var caption = new Label { Text = label }; fields.AddChild(caption);
        var slider = new HSlider { Name = key, MinValue = range.Min, MaxValue = range.Max, Step = .01, Value = range.Default, TooltipText = label };
        slider.ValueChanged += _ => changed(); fields.AddChild(slider); map[key] = slider;
        return slider;
    }

    private void AddAvatar2Controls(VBoxContainer fields)
    {
        fields.AddChild(new Label { Text = "Corpo" });
        foreach (var key in BodyOrder) Range(_body, fields, key, BodyLabels[key], AvatarRules.BodyRanges[key], RefreshPreview);
        fields.AddChild(new Label { Text = "Rosto" });
        foreach (var key in FaceOrder) Range(_face, fields, key, FaceLabels[key], AvatarRules.FaceRanges[key], RefreshPreview);
        fields.AddChild(new Label { Text = "Roupas e acessórios" });
        foreach (var slot in ExtraSlots)
        {
            fields.AddChild(new Label { Text = SlotLabels[slot] });
            var option = new OptionButton { Name = "Wear_" + slot };
            foreach (var item in AvatarRules.ItemsFor(slot)) { option.AddItem(item.Label); option.SetItemMetadata(option.ItemCount - 1, item.Key); }
            var captured = slot; option.ItemSelected += _ => OnWearChanged(captured);
            fields.AddChild(option); _extra[slot] = option;
        }
        BuildLooksManager(fields);
        BuildModelsManager(fields);
    }

    // ---- reading and writing the form -----------------------------------------------------------------------------------------------

    private string BodyJson() => AvatarRules.WriteSettings(BodyOrder.ToDictionary(k => k, k => _body.TryGetValue(k, out var s) ? (float)s.Value : 1f), AvatarRules.BodyRanges);
    private string FaceJson() => AvatarRules.WriteSettings(FaceOrder.ToDictionary(k => k, k => _face.TryGetValue(k, out var s) ? (float)s.Value : 1f), AvatarRules.FaceRanges);

    private List<EquippedItemData> ReadWorn()
    {
        var worn = new List<(string Slot, string Key)> { ("top", Value(_top)), ("bottom", Value(_bottom)), ("shoes", Value(_shoes)), ("accessory", Value(_accessory)) };
        foreach (var slot in ExtraSlots) if (_extra.TryGetValue(slot, out var option) && option.ItemCount > 0) worn.Add((slot, Value(option)));
        // "none" slots are not stored; the server fills nothing in for them
        return [.. AvatarRules.Resolve(worn).Where(w => w.Slot is "top" or "bottom" or "shoes" || w.Key != "none")
            .Select(w => new EquippedItemData { Slot = w.Slot, ItemKey = w.Key, TintColor = _tints.GetValueOrDefault(w.Slot, "#FFFFFF") })];
    }

    private void ApplyAvatar2(AvatarAppearanceData value)
    {
        _syncing = true;
        var body = AvatarRules.ReadSettings(value.BodySettingsJson, AvatarRules.BodyRanges);
        foreach (var (key, slider) in _body) slider.Value = body[key];
        var face = AvatarRules.ReadSettings(value.FaceSettingsJson, AvatarRules.FaceRanges);
        foreach (var (key, slider) in _face) slider.Value = face[key];
        _custom = (value.CustomModelId, value.CustomModelHash, value.CustomModelScale, value.CustomModelBonesJson);
        _tints.Clear(); foreach (var pair in DefaultTints) _tints[pair.Key] = pair.Value;
        foreach (var item in value.EquippedItems) _tints[item.Slot] = item.TintColor;
        foreach (var slot in ExtraSlots) if (_extra.TryGetValue(slot, out var option)) Select(option, value.Item(slot, "none"));
        _syncing = false;
    }

    /// <summary>A wearable changed: apply the conflict rules (the newest choice wins) and show the resolved set in the menus.</summary>
    private void OnWearChanged(string slot)
    {
        if (_syncing) return;
        var worn = new List<(string Slot, string Key)> { ("top", Value(_top)), ("bottom", Value(_bottom)), ("shoes", Value(_shoes)) };
        foreach (var extra in ExtraSlots) if (_extra.TryGetValue(extra, out var option) && option.ItemCount > 0) worn.Add((extra, Value(option)));
        var resolved = AvatarRules.Resolve(worn, slot).ToDictionary(x => x.Slot, x => x.Key);
        _syncing = true;
        Select(_top, resolved["top"]); Select(_bottom, resolved["bottom"]);
        foreach (var extra in ExtraSlots) if (_extra.TryGetValue(extra, out var option)) Select(option, resolved.GetValueOrDefault(extra, "none"));
        _syncing = false;
        RefreshPreview();
    }

    // ---- saved looks ----------------------------------------------------------------------------------------------------------------

    private void BuildLooksManager(VBoxContainer fields)
    {
        var heading = new Label { Text = "Looks salvos" }; fields.AddChild(heading);
        _looks = new ItemList { Name = "Looks", CustomMinimumSize = new Vector2(0, 130), SizeFlagsHorizontal = SizeFlags.ExpandFill }; fields.AddChild(_looks);
        _lookName = new LineEdit { Name = "LookName", PlaceholderText = "Nome do look", MaxLength = 40 }; fields.AddChild(_lookName);
        var row = new HBoxContainer(); fields.AddChild(row);
        Button Action(string text, System.Action pressed) { var b = new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill }; b.Pressed += pressed; row.AddChild(b); return b; }
        Action("Salvar", SaveLook); Action("Aplicar", ApplyLook); Action("Duplicar", DuplicateLook); Action("Excluir", DeleteLook);
        var share = new HBoxContainer(); fields.AddChild(share);
        var shareButton = new Button { Text = "Compartilhar", SizeFlagsHorizontal = SizeFlags.ExpandFill }; shareButton.Pressed += () => ShareLook(true); share.AddChild(shareButton);
        var unshare = new Button { Text = "Parar de compartilhar", SizeFlagsHorizontal = SizeFlags.ExpandFill }; unshare.Pressed += () => ShareLook(false); share.AddChild(unshare);
        _lookCode = new LineEdit { Name = "LookCode", Editable = false, PlaceholderText = "Código para compartilhar" }; fields.AddChild(_lookCode);
        var import = new HBoxContainer(); fields.AddChild(import);
        _importCode = new LineEdit { Name = "ImportCode", PlaceholderText = "Código de um look", MaxLength = 8, SizeFlagsHorizontal = SizeFlags.ExpandFill }; import.AddChild(_importCode);
        var importButton = new Button { Text = "Importar" }; importButton.Pressed += ImportLook; import.AddChild(importButton);
        _lookMessage = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; fields.AddChild(_lookMessage);
        _looks.ItemSelected += index => { if (index >= 0 && index < _lookEntries.Count) { _lookName.Text = _lookEntries[(int)index].Name; _lookCode.Text = _lookEntries[(int)index].ShareCode ?? string.Empty; } };
    }

    public IReadOnlyList<LookEntry> Looks => _lookEntries;

    private LookEntry? SelectedLook() { var picked = _looks.GetSelectedItems(); return picked.Length == 0 || picked[0] >= _lookEntries.Count ? null : _lookEntries[picked[0]]; }

    /// <summary>Asks the server for the looks of this avatar.</summary>
    public void LoadLooks() => _manager.Call(Godot.HttpClient.Method.Get, "/looks", null, (status, body) => { if (status is >= 200 and < 300) ReadLooks(body); });

    private void ReadLooks(string body)
    {
        var selected = SelectedLook()?.Id;
        _lookEntries.Clear(); _looks.Clear();
        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                string Text(string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
                if (!Guid.TryParse(Text("id"), out var id)) continue;
                var entry = new LookEntry(id, Text("name"), Text("shareCode") is { Length: > 0 } code ? code : null, Text("skinColor"), Text("hairStyle"));
                _lookEntries.Add(entry);
                _looks.AddItem(entry.ShareCode is null ? entry.Name : $"{entry.Name}  ·  compartilhado");
                if (entry.Id == selected) _looks.Select(_lookEntries.Count - 1);
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { _lookEntries.Clear(); _looks.Clear(); }
    }

    private void SaveLook()
    {
        var name = _lookName.Text.Trim();
        if (name.Length == 0) { _lookMessage.Text = "Dê um nome ao look."; return; }
        // the look is what is saved on the server, so save the form first and then snapshot it
        _manager.Call(Godot.HttpClient.Method.Put, string.Empty, JsonSerializer.Serialize(ReadForm()), (status, _) =>
        {
            if (status is < 200 or >= 300) { _lookMessage.Text = "Não foi possível salvar o avatar antes do look."; return; }
            _manager.Call(Godot.HttpClient.Method.Post, "/looks", JsonSerializer.Serialize(new { name }), (s, body) =>
            {
                if (s is >= 200 and < 300) { _lookMessage.Text = $"Look “{name}” salvo."; LoadLooks(); }
                else _lookMessage.Text = LookMessage(s, body);
            });
        });
    }

    private void ApplyLook()
    {
        if (SelectedLook() is not { } look) { _lookMessage.Text = "Escolha um look na lista."; return; }
        _manager.Call(Godot.HttpClient.Method.Post, $"/looks/{look.Id}/apply", "{}", (status, body) =>
        {
            if (status is < 200 or >= 300) { _lookMessage.Text = LookMessage(status, body); return; }
            if (AvatarManager.ParseAppearance(body) is { } value) { value.DisplayName = string.IsNullOrWhiteSpace(value.DisplayName) ? _displayName.Text : value.DisplayName; _lastSaved = value.Copy(); _undoValue = value.Copy(); _manager.SetCurrent(value); ApplyToForm(value); }
            _lookMessage.Text = $"Look “{look.Name}” aplicado.";
        });
    }

    private void DuplicateLook()
    {
        if (SelectedLook() is not { } look) { _lookMessage.Text = "Escolha um look na lista."; return; }
        _manager.Call(Godot.HttpClient.Method.Post, $"/looks/{look.Id}/duplicate", "{}", (status, body) => { if (status is >= 200 and < 300) { _lookMessage.Text = "Look duplicado."; LoadLooks(); } else _lookMessage.Text = LookMessage(status, body); });
    }

    private void DeleteLook()
    {
        if (SelectedLook() is not { } look) { _lookMessage.Text = "Escolha um look na lista."; return; }
        _manager.Call(Godot.HttpClient.Method.Delete, $"/looks/{look.Id}", null, (status, body) => { if (status is >= 200 and < 300) { _lookMessage.Text = $"Look “{look.Name}” excluído."; _lookCode.Text = string.Empty; LoadLooks(); } else _lookMessage.Text = LookMessage(status, body); });
    }

    private void ShareLook(bool on)
    {
        if (SelectedLook() is not { } look) { _lookMessage.Text = "Escolha um look na lista."; return; }
        _manager.Call(on ? Godot.HttpClient.Method.Post : Godot.HttpClient.Method.Delete, $"/looks/{look.Id}/share", on ? "{}" : null, (status, body) =>
        {
            if (status is < 200 or >= 300) { _lookMessage.Text = LookMessage(status, body); return; }
            using var document = JsonDocument.Parse(body);
            var code = document.RootElement.TryGetProperty("shareCode", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? string.Empty : string.Empty;
            _lookCode.Text = code;
            if (code.Length > 0) { DisplayServer.ClipboardSet(code); _lookMessage.Text = $"Código {code} copiado. Quem tiver o código pode importar este look."; }
            else _lookMessage.Text = "O look deixou de ser compartilhado.";
            LoadLooks();
        });
    }

    private void ImportLook()
    {
        var code = _importCode.Text.Trim();
        if (code.Length != 8) { _lookMessage.Text = "O código tem 8 letras e números."; return; }
        _manager.Call(Godot.HttpClient.Method.Post, "/looks/import", JsonSerializer.Serialize(new { code }), (status, body) =>
        {
            if (status is >= 200 and < 300) { _lookMessage.Text = "Look importado. Escolha-o na lista e toque em Aplicar."; _importCode.Text = string.Empty; LoadLooks(); }
            else _lookMessage.Text = LookMessage(status, body);
        });
    }

    private static string LookMessage(int status, string body)
    {
        string error = string.Empty;
        try { using var document = JsonDocument.Parse(body); error = document.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? string.Empty : string.Empty; } catch (JsonException) { }
        return error switch
        {
            "name_taken" => "Já existe um look com esse nome.",
            "too_many_looks" => $"O limite é de 20 looks. Exclua algum antes.",
            "bad_name" => "O nome do look precisa ter de 1 a 40 caracteres.",
            "bad_code" => "Esse código não é válido.",
            "not_found" => "Look não encontrado (o código pode ter deixado de ser compartilhado).",
            "look_invalid" => "Este look não segue mais as regras do avatar e não pôde ser usado.",
            _ => status == 0 ? "Sem conexão com o servidor." : $"Não foi possível concluir (HTTP {status})."
        };
    }

    // ---- hooks for tests ------------------------------------------------------------------------------------------------------------

    public void SetLookNameForTesting(string name) => _lookName.Text = name;
    public void SetImportCodeForTesting(string code) => _importCode.Text = code;
    public void SelectLookForTesting(int index) { _looks.Select(index); _looks.EmitSignal(ItemList.SignalName.ItemSelected, index); }
    public void SaveLookForTesting() => SaveLook();
    public void ApplyLookForTesting() => ApplyLook();
    public void DuplicateLookForTesting() => DuplicateLook();
    public void DeleteLookForTesting() => DeleteLook();
    public void ShareLookForTesting(bool on) => ShareLook(on);
    public void ImportLookForTesting() => ImportLook();
    public string LookMessageForTesting => _lookMessage.Text;
    public string LookCodeForTesting => _lookCode.Text;
    public void SetBodyForTesting(string key, double value) => _body[key].Value = value;
    public void SetFaceForTesting(string key, double value) => _face[key].Value = value;
    public void ChooseForTesting(string slot, string key)
    {
        var option = slot switch { "top" => _top, "bottom" => _bottom, "shoes" => _shoes, _ => _extra[slot] };
        Select(option, key); if (slot is "top" or "bottom" or "shoes" || _extra.ContainsKey(slot)) OnWearChanged(slot);
    }
    public AvatarAppearanceData FormForTesting() => ReadForm();
    public string BodyJsonForTesting => BodyJson();
    public string FaceJsonForTesting => FaceJson();
    public AvatarRig PreviewForTesting => _preview;
}
