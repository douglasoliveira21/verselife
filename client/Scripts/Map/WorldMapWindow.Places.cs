using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts.Map;

/// <summary>
/// "Marcos" in the left column: the player's landmarks (loose and in folders), favorites, recent visits, the islands of friends and of groups, all read from the server; "Adicionar Marco" saves the chosen spot (or where the
/// player stands) and "Gerenciar" renames, moves, shares and deletes them. A landmark is only made where the server lets this person enter.
/// </summary>
public partial class WorldMapWindow
{
    private sealed record Mark(Guid Id, string Name, string Description, Guid Island, string IslandName, float X, float Z, Guid? Folder);
    private sealed record Folder(Guid Id, string Name);
    private sealed record Spot(Guid Island, string Name, string Note, float? X = null, float? Z = null);
    private sealed record GroupEntry(Guid Id, string Name, string Tag, List<Spot> Places);

    private List<Mark> _marks = [];
    private List<Folder> _folders = [];
    private List<Spot> _favorites = [], _recent = [], _friendIslands = [];
    private List<GroupEntry> _groups = [];
    private VBoxContainer _placesBox = null!;
    private Button _addMark = null!, _manage = null!;
    private readonly HashSet<string> _expanded = ["marks"];
    private FloatingWindow? _addWindow, _manageWindow;
    private int _maxLandmarks = 200, _maxFolders = 20;
    private bool _panelsLoaded; private string _panelsNote = string.Empty;

    private void BuildPlaces(VBoxContainer column)
    {
        Section(column, "Marcos", out var body);
        _placesBox = new VBoxContainer(); _placesBox.AddThemeConstantOverride("separation", 4); body.AddChild(_placesBox);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6); body.AddChild(row);
        _addMark = Btn("Adicionar Marco", UiStyle.ButtonKind.Primary, AddMark, "Salva o ponto escolhido no mapa (ou onde você está) com um nome", 38); _addMark.Icon = UiStyle.Icon("plus", UiStyle.Text, 18); _addMark.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(_addMark);
        _manage = Btn("Gerenciar", UiStyle.ButtonKind.Secondary, ManageMarks, "Renomear, mover, copiar o link ou apagar marcos e pastas", 38); _manage.Icon = UiStyle.Icon("settings", UiStyle.Text, 18); _manage.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(_manage);
        FillPlaces();
    }

    // ---- reading -------------------------------------------------------------------------------------------------------------------------------

    public void LoadPanels()
    {
        Hub.Api.Send(Godot.HttpClient.Method.Get, "/api/v1/map/landmarks", null, (status, body) =>
        {
            if (status is < 200 or >= 300) { _panelsNote = status == 0 ? "Sem conexão: os marcos não carregaram." : $"Os marcos não carregaram (HTTP {status})."; FillPlaces(); return; }
            try { Parse(body); _panelsNote = string.Empty; _panelsLoaded = true; } catch (JsonException) { _panelsNote = "Resposta inesperada do servidor."; }
            RebuildPins(); FillPlaces(); if (_tabs.Selected == 4) FillGroups(); if (_manageWindow is not null && GodotObject.IsInstanceValid(_manageWindow)) FillManage();
        });
    }

    private void Parse(string json)
    {
        using var d = JsonDocument.Parse(json); var root = d.RootElement; Guid Id(JsonElement e, string key) => Guid.TryParse(Str(e, key), out var g) ? g : Guid.Empty;
        _folders = [.. Elements(root, "folders").Select(f => new Folder(Id(f, "id"), Str(f, "name")))];
        _marks = [.. Elements(root, "landmarks").Select(l => new Mark(Id(l, "id"), Str(l, "name"), Str(l, "description"), Id(l, "islandId"), Str(l, "islandName"), (float)Num(l, "x"), (float)Num(l, "z"), Guid.TryParse(Str(l, "folderId"), out var f) ? f : null))];
        _favorites = [.. Elements(root, "favorites").Select(f => new Spot(Id(f, "islandId"), Str(f, "name"), string.Empty))];
        _recent = [.. Elements(root, "recent").Select(r => new Spot(Id(r, "islandId"), Str(r, "name"), string.Empty, (float)Num(r, "x"), (float)Num(r, "z")))];
        _friendIslands = [.. Elements(root, "friends").Select(f => new Spot(Id(f, "islandId"), Str(f, "name"), Str(f, "ownerName") is { Length: > 0 } o ? "de " + o : string.Empty))];
        _groups = [.. Elements(root, "groups").Select(g => new GroupEntry(Id(g, "groupId"), Str(g, "name"), Str(g, "tag"), [.. Elements(g, "places").Select(p => new Spot(Id(p, "islandId"), Str(p, "name"), Str(p, "kind") == "headquarters" ? "Sede" : $"{Int(p, "parcels")} terreno(s)"))]))];
        if (root.TryGetProperty("limits", out var limits)) { _maxLandmarks = Math.Max(1, Int(limits, "landmarks")); _maxFolders = Math.Max(1, Int(limits, "folders")); }
    }

    private static IEnumerable<JsonElement> Elements(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

    // ---- the column ------------------------------------------------------------------------------------------------------------------------------

    private void FillPlaces()
    {
        foreach (var child in _placesBox.GetChildren()) { _placesBox.RemoveChild(child); child.QueueFree(); }
        if (_panelsNote.Length > 0) _placesBox.AddChild(Lbl(_panelsNote, 13, false, UiStyle.TextMuted, wrap: true));
        Group("marks", "Meus Marcos", "folder", _marks.Count, box =>
        {
            if (_marks.Count == 0 && _folders.Count == 0) { box.AddChild(Lbl("Você ainda não salvou nenhum marco. Escolha um ponto no mapa e use “Adicionar Marco”.", 13, false, UiStyle.TextMuted, wrap: true)); return; }
            foreach (var folder in _folders)
            {
                var inside = _marks.Where(m => m.Folder == folder.Id).ToList(); var key = "f" + folder.Id;
                Sub(box, key, folder.Name, inside.Count, inner => { foreach (var m in inside) inner.AddChild(MarkRow(m)); if (inside.Count == 0) inner.AddChild(Lbl("Pasta vazia.", 13, false, UiStyle.TextMuted)); });
            }
            foreach (var m in _marks.Where(m => m.Folder is null || _folders.All(f => f.Id != m.Folder))) box.AddChild(MarkRow(m));
        });
        Group("favorites", "Favoritos", "star", _favorites.Count, box => SpotRows(box, _favorites, "Nenhuma ilha favorita ainda. Use a estrela no cartão da ilha."));
        Group("recent", "Visita Recente", "history", _recent.Count, box => SpotRows(box, _recent, "Você ainda não visitou nenhuma ilha."));
        Group("friends", "Ilhas de Amigos", "people", _friendIslands.Count, box => SpotRows(box, _friendIslands, "Nenhuma ilha pública de amigos."));
        Group("groups", "Grupos", "user", _groups.Count, box =>
        {
            if (_groups.Count == 0) { box.AddChild(Lbl("Você não está em nenhum grupo com ilha ou terreno.", 13, false, UiStyle.TextMuted, wrap: true)); return; }
            foreach (var g in _groups) Sub(box, "g" + g.Id, g.Name + (g.Tag.Length > 0 ? $" [{g.Tag}]" : string.Empty), g.Places.Count, inner => SpotRows(inner, g.Places, string.Empty));
        });
        _addMark.Disabled = _marks.Count >= _maxLandmarks; _addMark.TooltipText = _addMark.Disabled ? $"Limite de {_maxLandmarks} marcos. Apague algum em Gerenciar." : "Salva o ponto escolhido no mapa (ou onde você está) com um nome";
    }

    private void Group(string key, string title, string icon, int count, Action<VBoxContainer> fill)
    {
        var open = _expanded.Contains(key);
        var header = new Button { Text = $"  {title} ({count})", Icon = UiStyle.Icon(open ? "folder-open" : icon, UiStyle.TextMuted, 20), Alignment = HorizontalAlignment.Left, ExpandIcon = false, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36), TooltipText = open ? "Recolher" : "Expandir" };
        UiStyle.StyleButton(header, UiStyle.ButtonKind.Tool, 8, 4, 10); header.AddThemeFontSizeOverride("font_size", 15); header.Pressed += () => { if (!_expanded.Remove(key)) _expanded.Add(key); FillPlaces(); }; _placesBox.AddChild(header);
        if (!open) return; var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 3); var margin = new MarginContainer(); margin.AddThemeConstantOverride("margin_left", 10); margin.AddChild(box); _placesBox.AddChild(margin); fill(box);
    }

    private void Sub(VBoxContainer parent, string key, string title, int count, Action<VBoxContainer> fill)
    {
        var open = _expanded.Contains(key); var header = new Button { Text = $"{(open ? "▾" : "▸")} {title} ({count})", Alignment = HorizontalAlignment.Left, Flat = true, FocusMode = Control.FocusModeEnum.None, ClipText = true };
        header.AddThemeFontSizeOverride("font_size", 14); header.AddThemeColorOverride("font_color", UiStyle.AccentText); header.Pressed += () => { if (!_expanded.Remove(key)) _expanded.Add(key); FillPlaces(); }; parent.AddChild(header);
        if (!open) return; var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 3); var margin = new MarginContainer(); margin.AddThemeConstantOverride("margin_left", 10); margin.AddChild(box); parent.AddChild(margin); fill(box);
    }

    private Button Row(string title, string subtitle, Action action)
    {
        var row = new Button { Text = title + (subtitle.Length > 0 ? "\n" + subtitle : string.Empty), Alignment = HorizontalAlignment.Left, ClipText = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, subtitle.Length > 0 ? 48 : 34), TooltipText = subtitle.Length > 0 ? title + " · " + subtitle : title };
        UiStyle.StyleButton(row, UiStyle.ButtonKind.Field, 10, 5, 10); row.AddThemeFontSizeOverride("font_size", 14); row.Pressed += action; return row;
    }

    private Button MarkRow(Mark m) => Row(m.Name, $"{m.IslandName} · {m.X:0}, {m.Z:0}", () => GoToSpot(m.Island, m.X, m.Z));

    private void SpotRows(VBoxContainer box, IEnumerable<Spot> spots, string empty)
    {
        var any = false; foreach (var s in spots) { any = true; var captured = s; box.AddChild(Row(s.Name, s.Note, () => GoToSpot(captured.Island, captured.X, captured.Z))); }
        if (!any && empty.Length > 0) box.AddChild(Lbl(empty, 13, false, UiStyle.TextMuted, wrap: true));
    }

    /// <summary>Takes the map to an island (its card shows) and, when there is a spot, to that spot.</summary>
    public void GoToSpot(Guid island, float? x = null, float? z = null)
    {
        if (_items.FirstOrDefault(i => i.Id == island) is not { } known) { Say("Esta ilha não está no mapa agora. Troque o filtro ou tente de novo."); return; }
        if (_tabs.Selected != 0) _tabs.Select(0); if (_narrow) { _filtersOpen = false; Adapt(); }
        FocusIsland(known);
        if (x is { } px && z is { } pz) { var (gx, gy) = MapCoordinates.ToGlobal(known, px, pz); _canvas.GlideTo(gx, gy, Math.Max(_canvas.Zoom, 1.6)); _canvas.PickPoint(known, px, pz); }
    }

    private void RebuildPins()
    {
        _canvas.Pins.Clear();
        foreach (var m in _marks) if (_items.FirstOrDefault(i => i.Id == m.Island) is { } island) { var (gx, gy) = MapCoordinates.ToGlobal(island, m.X, m.Z); _canvas.Pins.Add(new MapPin(m.Id, "landmark", m.Name, island.Id, gx, gy)); }
        if (Hub.Settings.ShowFriends) foreach (var f in _friends) if (_items.FirstOrDefault(i => i.Id == f.Island) is { } island) { var (gx, gy) = MapCoordinates.ToGlobal(island, f.X, f.Z); _canvas.Pins.Add(new MapPin(f.User, "friend", f.Name, island.Id, gx, gy)); }
        _canvas.QueueRedraw();
    }

    // ---- adding ---------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The spot a new landmark would take: the chosen point of the map, or else where the player stands.</summary>
    private (Guid Island, string Name, float X, float Z)? SpotForMark()
    {
        if (_canvas.Point is { } point && _items.FirstOrDefault(i => i.Id == point.Island) is { } chosen) return (chosen.Id, chosen.Name, point.X, point.Z);
        var current = Hub.CurrentIsland?.Invoke() ?? Guid.Empty; if (current == Guid.Empty || Hub.PlayerLocal?.Invoke() is not { } at) return null;
        return (current, _items.FirstOrDefault(i => i.Id == current)?.Name ?? "Ilha atual", at.X, at.Z);
    }

    private static string FailureWords(int status, string body)
    {
        var error = string.Empty; try { using var d = JsonDocument.Parse(body); error = Str(d.RootElement, "error"); } catch (JsonException) { }
        return error switch
        {
            "bad_name" => "Dê um nome de 1 a 60 letras.", "bad_description" => "A descrição pode ter até 300 letras.", "bad_position" => "Esse ponto fica fora da ilha.", "no_access" => "Você não pode entrar nesta ilha agora, então não dá para marcar nela.",
            "island_not_found" => "Esta ilha não está mais disponível.", "folder_not_found" => "Essa pasta não existe mais.", "too_many_landmarks" => "Você chegou ao limite de marcos. Apague algum em Gerenciar.", "too_many_folders" => "Você chegou ao limite de pastas.",
            "name_taken" => "Já existe uma pasta com esse nome.", "not_found" => "Isso não existe mais.", _ => status == 0 ? "Sem conexão com o servidor." : status == 429 ? "Calma! Muitas tentativas. Tente de novo daqui a pouco." : $"Não foi possível concluir (HTTP {status})."
        };
    }

    private void AddMark()
    {
        if (SpotForMark() is not { } spot) { Say("Aproxime o mapa e clique em um ponto de uma ilha, ou entre em uma ilha, para marcar."); return; }
        if (_addWindow is not null && GodotObject.IsInstanceValid(_addWindow)) _addWindow.QueueFree();
        _addWindow = new FloatingWindow("Adicionar Marco", icon: "pin") { Name = "WorldMapAddMark" }; _addWindow.Place(.5f, .5f, -230, -190, 230, 190); var window = _addWindow; var box = window.Content; box.AddThemeConstantOverride("separation", 8);
        box.AddChild(Lbl($"Local: {spot.Name} · x {spot.X:0}, z {spot.Z:0}", 14, false, UiStyle.AccentText, wrap: true));
        var name = new LineEdit { PlaceholderText = "Nome do marco", MaxLength = 60, CustomMinimumSize = new Vector2(0, 38), Name = "MarkName" }; name.AddThemeFontSizeOverride("font_size", 15); box.AddChild(name);
        var description = new LineEdit { PlaceholderText = "Descrição (opcional)", MaxLength = 300, CustomMinimumSize = new Vector2(0, 38), Name = "MarkDescription" }; description.AddThemeFontSizeOverride("font_size", 15); box.AddChild(description);
        var folder = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36), Name = "MarkFolder" }; folder.AddItem("Sem pasta"); foreach (var f in _folders) folder.AddItem(f.Name); box.AddChild(folder);
        var note = Lbl("", 13, false, WorldMapCanvas.Error, wrap: true); box.AddChild(note);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); box.AddChild(row);
        var save = Btn("Salvar", UiStyle.ButtonKind.Primary, () => { }, null, 40); save.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(save);
        save.Pressed += () =>
        {
            if (name.Text.Trim().Length == 0) { note.Text = "Dê um nome ao marco."; return; } save.Disabled = true;
            var payload = JsonSerializer.Serialize(new { name = name.Text.Trim(), description = description.Text.Trim(), islandId = spot.Island, x = spot.X, y = 0f, z = spot.Z, folderId = folder.Selected > 0 ? (Guid?)_folders[folder.Selected - 1].Id : null, isPublic = false });
            Hub.Api.Send(Godot.HttpClient.Method.Post, "/api/v1/map/landmarks", payload, (status, body) =>
            {
                if (!GodotObject.IsInstanceValid(window)) return; save.Disabled = false;
                if (status is < 200 or >= 300) { note.Text = FailureWords(status, body); return; }
                window.QueueFree(); Say($"Marco “{name.Text.Trim()}” salvo."); _expanded.Add("marks"); LoadPanels();
            });
        };
        row.AddChild(Btn("Cancelar", UiStyle.ButtonKind.Secondary, () => window.QueueFree(), null, 40)); AddChild(window); window.MoveToFront(); name.GrabFocus();
    }

    // ---- managing -------------------------------------------------------------------------------------------------------------------------------------------

    private VBoxContainer _manageBody = null!; private Label _manageNote = null!;

    private void ManageMarks()
    {
        if (_manageWindow is not null && GodotObject.IsInstanceValid(_manageWindow)) _manageWindow.QueueFree();
        _manageWindow = new FloatingWindow("Gerenciar Marcos", icon: "folder") { Name = "WorldMapManage" }; _manageWindow.Place(.5f, .5f, -330, -280, 330, 280); var window = _manageWindow;
        _manageNote = Lbl("", 13, false, UiStyle.AccentText, wrap: true); window.Content.AddChild(_manageNote);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; window.Content.AddChild(scroll);
        _manageBody = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _manageBody.AddThemeConstantOverride("separation", 8); scroll.AddChild(_manageBody);
        window.Content.AddChild(Btn("Fechar", UiStyle.ButtonKind.Secondary, () => window.QueueFree(), null, 36)); AddChild(window); window.MoveToFront(); FillManage();
    }

    private void FillManage()
    {
        foreach (var child in _manageBody.GetChildren()) { _manageBody.RemoveChild(child); child.QueueFree(); }
        _manageBody.AddChild(Lbl($"Pastas ({_folders.Count}/{_maxFolders})", 16, true));
        foreach (var folder in _folders)
        {
            var captured = folder; var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6); _manageBody.AddChild(row);
            var edit = new LineEdit { Text = folder.Name, MaxLength = 40, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 34) }; row.AddChild(edit);
            row.AddChild(Btn("Renomear", UiStyle.ButtonKind.Secondary, () => Change(Godot.HttpClient.Method.Patch, $"/api/v1/map/landmark-folders/{captured.Id}", JsonSerializer.Serialize(new { name = edit.Text.Trim() }), "Pasta renomeada."), null, 34));
            row.AddChild(ConfirmButton("Excluir pasta", "Os marcos dela ficam soltos. Confirmar?", () => Change(Godot.HttpClient.Method.Delete, $"/api/v1/map/landmark-folders/{captured.Id}", null, "Pasta excluída; os marcos ficaram soltos.")));
        }
        var create = new HBoxContainer(); create.AddThemeConstantOverride("separation", 6); _manageBody.AddChild(create);
        var fresh = new LineEdit { PlaceholderText = "Nome da nova pasta", MaxLength = 40, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 34), Name = "NewFolder" }; create.AddChild(fresh);
        var make = Btn("Criar pasta", UiStyle.ButtonKind.Primary, () => { if (fresh.Text.Trim().Length > 0) Change(Godot.HttpClient.Method.Post, "/api/v1/map/landmark-folders", JsonSerializer.Serialize(new { name = fresh.Text.Trim() }), "Pasta criada."); }, null, 34); make.Disabled = _folders.Count >= _maxFolders; create.AddChild(make);

        _manageBody.AddChild(Lbl($"Marcos ({_marks.Count}/{_maxLandmarks})", 16, true));
        if (_marks.Count == 0) _manageBody.AddChild(Lbl("Nenhum marco ainda.", 14, false, UiStyle.TextMuted));
        foreach (var mark in _marks)
        {
            var m = mark; var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.FieldFill, new Color(UiStyle.BorderSoft, .6f), 10, 1, 10, 8)); _manageBody.AddChild(card);
            var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 6); card.AddChild(column);
            var name = new LineEdit { Text = m.Name, MaxLength = 60, CustomMinimumSize = new Vector2(0, 34) }; column.AddChild(name);
            column.AddChild(Lbl($"{m.IslandName} · x {m.X:0}, z {m.Z:0}", 13, false, UiStyle.TextMuted));
            var folder = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 32) }; folder.AddItem("Sem pasta"); var selected = 0; for (var i = 0; i < _folders.Count; i++) { folder.AddItem(_folders[i].Name); if (_folders[i].Id == m.Folder) selected = i + 1; } folder.Select(selected); column.AddChild(folder);
            var buttons = new HFlowContainer(); buttons.AddThemeConstantOverride("h_separation", 6); column.AddChild(buttons);
            buttons.AddChild(Btn("Salvar", UiStyle.ButtonKind.Primary, () =>
            {
                var chosen = folder.Selected > 0 ? (Guid?)_folders[folder.Selected - 1].Id : null;
                Change(Godot.HttpClient.Method.Patch, $"/api/v1/map/landmarks/{m.Id}", JsonSerializer.Serialize(new { name = name.Text.Trim(), folderId = chosen, clearFolder = chosen is null }), "Marco atualizado.");
            }, null, 32));
            buttons.AddChild(Btn("Ir", UiStyle.ButtonKind.Secondary, () => { GoToSpot(m.Island, m.X, m.Z); }, "Mostra no mapa", 32));
            buttons.AddChild(Btn("Copiar link", UiStyle.ButtonKind.Secondary, () => { DisplayServer.ClipboardSet(LifeVerse.Client.Scripts.SearchLinks.For("island", m.Island, m.X, m.Z)); _manageNote.Text = "Link do local copiado."; }, "Copia o link do local (a ilha e o ponto)", 32));
            buttons.AddChild(ConfirmButton("Excluir", "Excluir de vez? Confirmar", () => Change(Godot.HttpClient.Method.Delete, $"/api/v1/map/landmarks/{m.Id}", null, "Marco excluído.")));
        }
    }

    /// <summary>A button that asks once more before doing something that cannot be undone: the first press changes its words, the second does it.</summary>
    private static Button ConfirmButton(string text, string again, Action action)
    {
        var armed = false; var button = Btn(text, UiStyle.ButtonKind.Secondary, () => { }, null, 32);
        button.Pressed += () => { if (!armed) { armed = true; button.Text = again; return; } armed = false; button.Text = text; action(); }; return button;
    }

    private void Change(Godot.HttpClient.Method method, string path, string? body, string done)
    {
        Hub.Api.Send(method, path, body, (status, text) =>
        {
            if (status is < 200 or >= 300) { var words = FailureWords(status, text); if (_manageWindow is not null && GodotObject.IsInstanceValid(_manageWindow)) _manageNote.Text = words; Say(words); return; }
            if (_manageWindow is not null && GodotObject.IsInstanceValid(_manageWindow)) _manageNote.Text = done; Say(done); LoadPanels();
        });
    }

    // ---- tests ----------------------------------------------------------------------------------------------------------------------------------------------

    public IReadOnlyList<string> MarkNamesForTesting => [.. _marks.Select(m => m.Name)];
    public IReadOnlyList<string> FolderNamesForTesting => [.. _folders.Select(f => f.Name)];
    public IReadOnlyList<string> PlaceButtonsForTesting => [.. _placesBox.FindChildren("*", "Button", true, false).OfType<Button>().Select(b => b.Text.Split('\n')[0].Trim())];
    public string PanelsNoteForTesting => _panelsNote;
    public void ExpandForTesting(string key) { _expanded.Add(key); FillPlaces(); }
    public void PressPlaceForTesting(string title) => _placesBox.FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text.Split('\n')[0].Trim().Contains(title)).EmitSignal(BaseButton.SignalName.Pressed);
    public void PressAddMarkForTesting() => AddMark();
    public bool AddWindowOpenForTesting => _addWindow is not null && GodotObject.IsInstanceValid(_addWindow) && !_addWindow.IsQueuedForDeletion();
    public void FillAddMarkForTesting(string name, string description = "", int folder = 0)
    {
        _addWindow!.FindChild("MarkName", true, false).Set("text", name); _addWindow.FindChild("MarkDescription", true, false).Set("text", description); ((OptionButton)_addWindow.FindChild("MarkFolder", true, false)).Select(folder);
        _addWindow.FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text == "Salvar").EmitSignal(BaseButton.SignalName.Pressed);
    }
    public string AddMarkNoteForTesting => string.Join("|", _addWindow?.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text) ?? []);
    public void OpenManageForTesting() => ManageMarks();
    public bool ManageOpenForTesting => _manageWindow is not null && GodotObject.IsInstanceValid(_manageWindow) && !_manageWindow.IsQueuedForDeletion();
    public string ManageNoteForTesting => _manageNote.Text;
    public void PressManageForTesting(string button, int index = 0) => _manageBody.FindChildren("*", "Button", true, false).OfType<Button>().Where(b => b.Text == button).ElementAt(index).EmitSignal(BaseButton.SignalName.Pressed);
    public void CreateFolderForTesting(string name) { ((LineEdit)_manageBody.FindChild("NewFolder", true, false)).Text = name; PressManageForTesting("Criar pasta"); }
    public bool AddMarkEnabledForTesting => !_addMark.Disabled;
    public IReadOnlyList<MapPin> PinsForTesting => _canvas.Pins;
}
