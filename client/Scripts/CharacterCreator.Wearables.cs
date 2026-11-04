using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Models;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts;

/// <summary>One custom item as the server lists it.</summary>
public sealed record WearableEntry(Guid Id, string Name, string Slot, string Layer, string Kind, int Triangles, string Key, string Hash, string[] Covers, bool HidesHair, string AttachBone, float[] Offset, float[] Rotation, float Scale, string Tint = "");

/// <summary>
/// "Roupas e acessórios personalizados": import a GLB (clothing rigged to the standard skeleton, or a rigid piece fixed to a bone), see why the
/// server refused it, wear it (it joins the menu of its slot, with the same conflict rules as the catalog), take it off and delete it. Two
/// reference files show people how such items are made.
/// </summary>
public partial class CharacterCreator
{
    private readonly List<WearableEntry> _wearableEntries = [];
    private ItemList _wearables = null!;
    private Label _wearableMessage = null!;
    private FileDialog? _wearableOpen, _wearableSave;

    public IReadOnlyList<WearableEntry> Wearables => _wearableEntries;

    private void BuildWearablesManager(VBoxContainer fields)
    {
        fields.AddChild(new Label { Text = "Roupas e acessórios personalizados (GLB)" });
        var hint = new Label { Text = "Roupa com esqueleto (só em corpo personalizado) ou peça rígida presa a um osso, inclusive cabelo e olhos seus (que trocam os do catálogo). O arquivo diz para qual slot serve; o servidor confere tudo.", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(280, 0) };
        hint.AddThemeFontSizeOverride("font_size", 14); fields.AddChild(hint);
        var top = new HBoxContainer(); fields.AddChild(top);
        Button Add(HBoxContainer row, string text, System.Action pressed) { var b = new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill }; b.Pressed += pressed; row.AddChild(b); return b; }
        Add(top, "Importar item…", PickWearable); Add(top, "Exemplo de acessório…", () => PickExample("accessory")); Add(top, "Exemplo de roupa…", () => PickExample("clothing"));
        var faceRow = new HBoxContainer(); fields.AddChild(faceRow); Add(faceRow, "Exemplo de cabelo…", () => PickExample("hair")); Add(faceRow, "Exemplo de olhos…", () => PickExample("eyes"));
        _wearables = new ItemList { Name = "Wearables", CustomMinimumSize = new Vector2(0, 100), SizeFlagsHorizontal = SizeFlags.ExpandFill }; fields.AddChild(_wearables);
        var row = new HBoxContainer(); fields.AddChild(row);
        Add(row, "Vestir", WearSelected); Add(row, "Tirar", TakeOffSelected); Add(row, "Excluir", DeleteWearable);
        _wearableMessage = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(280, 0) }; fields.AddChild(_wearableMessage);
    }

    private WearableEntry? SelectedWearable() { var picked = _wearables.GetSelectedItems(); return picked.Length == 0 || picked[0] >= _wearableEntries.Count ? null : _wearableEntries[picked[0]]; }

    /// <summary>What the outfit rules need to know about the custom items of this person.</summary>
    private WearableItem? CustomResolver(string slot, string key) => _wearableEntries.FirstOrDefault(w => w.Key == key && w.Slot == slot) is { } w ? new WearableItem(w.Slot, w.Key, w.Name, w.Layer, w.Covers.Length > 0 ? w.Covers : null, w.HidesHair) : null;

    private OptionButton? MenuOf(string slot) => slot switch { "top" => _top, "bottom" => _bottom, "shoes" => _shoes, "accessory" => _accessory, _ => _extra.GetValueOrDefault(slot) };

    public void LoadWearables() => _manager.Call(Godot.HttpClient.Method.Get, "/wearables", null, (status, body) => { if (status is >= 200 and < 300) ReadWearables(body); });

    private void ReadWearables(string body)
    {
        _wearableEntries.Clear(); _wearables.Clear();
        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                string Text(string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
                float[] Numbers(string name, float[] fallback) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? [.. v.EnumerateArray().Select(x => x.GetSingle())] : fallback;
                if (!Guid.TryParse(Text("id"), out var id)) continue;
                _wearableEntries.Add(new WearableEntry(id, Text("name"), Text("slot"), Text("layer") is { Length: > 0 } layer ? layer : "base", Text("kind"), item.TryGetProperty("triangles", out var t) ? t.GetInt32() : 0, Text("key"), Text("hash"),
                    item.TryGetProperty("covers", out var c) && c.ValueKind == JsonValueKind.Array ? [.. c.EnumerateArray().Select(x => x.GetString() ?? string.Empty)] : [],
                    item.TryGetProperty("hidesHair", out var h) && h.ValueKind == JsonValueKind.True, Text("attachBone"), Numbers("offset", [0, 0, 0]), Numbers("rotation", [0, 0, 0]), item.TryGetProperty("scale", out var s) && s.TryGetSingle(out var sv) ? sv : 1f, Text("tint")));
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException) { _wearableEntries.Clear(); }
        foreach (var entry in _wearableEntries) _wearables.AddItem($"{entry.Name} · {SlotName(entry.Slot)} · {(entry.Kind == "clothing" ? "roupa com esqueleto" : "peça rígida")} · {entry.Triangles:N0} triângulos");
        RefreshWearableMenus();
    }

    private static string SlotName(string slot) => SlotLabels.GetValueOrDefault(slot, slot switch { "top" => "Parte de cima", "bottom" => "Parte de baixo", "shoes" => "Sapatos", "accessory" => "Acessório", _ => slot });

    /// <summary>Puts the custom items into the menu of their slot (and takes out the ones that are gone), keeping what is chosen.</summary>
    private void RefreshWearableMenus()
    {
        _syncing = true;
        foreach (var slot in AvatarRules.Slots)
        {
            if (MenuOf(slot) is not { } menu) continue;
            var chosen = menu.ItemCount > 0 && menu.Selected >= 0 ? menu.GetItemMetadata(menu.Selected).AsString() : string.Empty;
            for (var i = menu.ItemCount - 1; i >= 0; i--) if (AvatarRules.IsCustom(menu.GetItemMetadata(i).AsString())) menu.RemoveItem(i);
            foreach (var entry in _wearableEntries.Where(w => w.Slot == slot)) { menu.AddItem("★ " + entry.Name); menu.SetItemMetadata(menu.ItemCount - 1, entry.Key); }
            var keep = AvatarRules.IsCustom(chosen) && _wearableEntries.Any(w => w.Key == chosen) ? chosen : _lastSaved.Item(slot, string.Empty) is { Length: > 0 } saved && menu.ItemCount > 0 ? saved : chosen;
            if (keep.Length > 0) Select(menu, keep);
        }
        _syncing = false;
        RefreshPreview();
    }

    /// <summary>The custom items worn on the form, described the way the server describes them to everybody (so the preview downloads and draws the same files).</summary>
    private string WornWearablesJson()
    {
        var worn = new List<WornWearable>();
        foreach (var item in ReadWorn().Where(i => AvatarRules.IsCustom(i.ItemKey)))
            if (_wearableEntries.FirstOrDefault(w => w.Key == item.ItemKey) is { } w)
                worn.Add(new WornWearable { Id = w.Id, Key = w.Key, Hash = w.Hash, Slot = w.Slot, Layer = w.Layer, Kind = w.Kind, HidesHair = w.HidesHair, Covers = w.Covers, AttachBone = w.AttachBone, Offset = w.Offset, Rotation = w.Rotation, Scale = w.Scale, Tint = w.Tint });
        return JsonSerializer.Serialize(worn);
    }

    private void WearSelected()
    {
        if (SelectedWearable() is not { } entry) { _wearableMessage.Text = "Escolha um item na lista."; return; }
        if (entry.Kind == "clothing" && _custom.Id is null) { _wearableMessage.Text = "Roupa com esqueleto só pode ser vestida em um corpo personalizado. Escolha um corpo primeiro."; return; }
        if (MenuOf(entry.Slot) is not { } menu) return;
        Select(menu, entry.Key); OnWearChanged(entry.Slot);
        _wearableMessage.Text = $"Você está vestindo “{entry.Name}”. Toque em Salvar para guardar.";
    }

    private void TakeOffSelected()
    {
        if (SelectedWearable() is not { } entry) { _wearableMessage.Text = "Escolha um item na lista."; return; }
        if (MenuOf(entry.Slot) is not { } menu) return;
        Select(menu, AvatarRules.DefaultFor(entry.Slot)); OnWearChanged(entry.Slot);
        _wearableMessage.Text = $"“{entry.Name}” foi tirado.";
    }

    private void DeleteWearable()
    {
        if (SelectedWearable() is not { } entry) { _wearableMessage.Text = "Escolha um item na lista."; return; }
        _manager.Call(Godot.HttpClient.Method.Delete, $"/wearables/{entry.Id}", null, (status, body) =>
        {
            if (status is < 200 or >= 300) { _wearableMessage.Text = WearableMessage(status, body); return; }
            _wearableMessage.Text = $"Item “{entry.Name}” excluído.";
            _manager.Call(Godot.HttpClient.Method.Get, string.Empty, null, (s, text) =>
            {
                if (s is >= 200 and < 300 && AvatarManager.ParseAppearance(text) is { } value) { _manager.SetCurrent(value); _lastSaved = value.Copy(); _syncing = true; ApplyAvatar2(value); Select(_top, value.Item("top", "tee_basic")); Select(_bottom, value.Item("bottom", "pants_basic")); Select(_shoes, value.Item("shoes", "sneakers_basic")); _syncing = false; }
                LoadWearables();
            });
        });
    }

    private void PickWearable()
    {
        _wearableOpen ??= MakeDialog(FileDialog.FileModeEnum.OpenFile, "Importar roupa ou acessório (.glb)", path => { try { ImportWearable(System.IO.Path.GetFileName(path), System.IO.File.ReadAllBytes(path)); } catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { _wearableMessage.Text = "Não foi possível ler esse arquivo."; } });
        _wearableOpen.PopupCentered(new Vector2I(900, 560));
    }

    private string _exampleKind = "accessory";

    /// <summary>The reference file for a kind of item (clothing, accessory, hair or eyes): what the file says it is, and where it sits.</summary>
    public static byte[] ExampleFile(string kind) => kind switch
    {
        "clothing" => WearableTemplate.Clothing("top", WearableTemplate.ShirtBones),
        "hair" => WearableTemplate.Attachment(WearableTemplate.Extras("hair", bone: "head", offset: [0, .12f, 0], tint: "hair"), .3f),
        "eyes" => WearableTemplate.Attachment(WearableTemplate.Extras("eyes", bone: "head", offset: [0, .06f, .09f], tint: "eyes"), .05f),
        _ => WearableTemplate.Attachment(WearableTemplate.Extras("hat", bone: "head", offset: [0, .15f, 0]))
    };

    private static string ExampleSaved(string kind) => kind switch
    {
        "clothing" => "Exemplo de roupa salvo: o esqueleto padrão, com a camiseta presa aos ossos do tronco e dos braços. O slot fica nos extras do arquivo.",
        "hair" => "Exemplo de cabelo salvo: uma peça rígida presa à cabeça, no slot \"hair\". Ela troca o cabelo do catálogo e, com \"tint\": \"hair\", pega a cor de cabelo escolhida no editor.",
        "eyes" => "Exemplo de olhos salvo: uma peça pequena presa à cabeça, no slot \"eyes\". Ela troca os olhos do catálogo e, com \"tint\": \"eyes\", pega a cor de olhos escolhida.",
        _ => "Exemplo de acessório salvo: uma peça rígida presa ao osso da cabeça. O arquivo diz o slot, o osso e a posição."
    };

    private void PickExample(string kind)
    {
        _exampleKind = kind;
        _wearableSave ??= MakeDialog(FileDialog.FileModeEnum.SaveFile, "Salvar exemplo", path =>
        {
            try { System.IO.File.WriteAllBytes(path, ExampleFile(_exampleKind)); _wearableMessage.Text = ExampleSaved(_exampleKind); }
            catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { _wearableMessage.Text = "Não foi possível salvar nesse lugar."; }
        });
        _wearableSave.CurrentFile = $"lifeverse-exemplo-{kind switch { "clothing" => "roupa", "hair" => "cabelo", "eyes" => "olhos", _ => "acessorio" }}.glb"; _wearableSave.PopupCentered(new Vector2I(900, 560));
    }

    /// <summary>Sends a GLB item to the server and shows what came back: the item, or the reasons it was refused.</summary>
    public void ImportWearable(string fileName, byte[] bytes)
    {
        if (!fileName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) { _wearableMessage.Text = "Use um arquivo .glb com tudo dentro."; return; }
        _wearableMessage.Text = "Enviando e conferindo o item…";
        _manager.Upload("/wearables", fileName, bytes, new Dictionary<string, string> { ["name"] = System.IO.Path.GetFileNameWithoutExtension(fileName) }, (status, body) =>
        {
            if (status is >= 200 and < 300) { _wearableMessage.Text = Describe("Item aceito.", body); LoadWearables(); return; }
            _wearableMessage.Text = status == 422 ? Describe("O item foi recusado:", body) : WearableMessage(status, body);
        });
    }

    private static string WearableMessage(int status, string body)
    {
        var error = string.Empty;
        try { using var document = JsonDocument.Parse(body); error = document.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? string.Empty : string.Empty; } catch (JsonException) { }
        return error switch
        {
            "too_many_items" => "O limite é de 30 itens. Exclua algum antes.",
            "too_big" => $"O arquivo passa de {WearableValidator.MaxBytes / 1024 / 1024} MB.",
            "bad_file" => "Use um arquivo .glb.",
            "not_found" => "Item não encontrado.",
            _ => status == 0 ? "Sem conexão com o servidor." : $"Não foi possível concluir (HTTP {status})."
        };
    }

    // ---- hooks for tests ------------------------------------------------------------------------------------------------------------

    public string WearableMessageForTesting => _wearableMessage.Text;
    public void SelectWearableForTesting(int index) => _wearables.Select(index);
    public void WearForTesting() => WearSelected();
    public void TakeOffForTesting() => TakeOffSelected();
    public void DeleteWearableForTesting() => DeleteWearable();
    public int MenuItemCountForTesting(string slot) => MenuOf(slot)?.ItemCount ?? 0;
    public string WornWearablesJsonForTesting => WornWearablesJson();
}
