using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts;

public sealed record LandAccessEntry(Guid Id, Guid UserId, string UserName, string Kind, DateTimeOffset? ExpiresAt);
public sealed record LandObjectRow(Guid Id, string Name, string OwnerName, Guid OwnerUserId, int Cost, bool HasScripts, bool Authorized);

/// Managing a parcel from the land window: who may enter (the Acesso tab), cutting it in two and joining neighbours (Administração), the
/// list of what stands on it and sending things back (Objetos), and the history of what was done. The server checks every one of these.
public partial class LandManager
{
    private PersonActions _people = null!;
    private Control _accessPage = null!, _adminPage = null!;
    private OptionButton _mode = null!, _entryKind = null!, _entryDuration = null!, _mergePrimary = null!, _objectFilter = null!, _buildMode = null!, _autoReturn = null!;
    private CheckBox _allowScripts = null!;
    private LineEdit _entryName = null!, _splitName = null!, _returnReason = null!;
    private VBoxContainer _accessRows = null!, _splitBox = null!, _mergeBox = null!, _auditRows = null!;
    private Label _accessNote = null!, _splitNote = null!, _mergeNote = null!, _objectsNote = null!;
    private ItemList _objectList = null!;
    private readonly List<LandAccessEntry> _entries = [];
    private readonly List<LandObjectRow> _objectRows = [], _shownObjects = [];
    private string _accessMode = "public";
    private static readonly string[] ModeIds = ["public", "list", "private"];
    private static readonly int?[] DurationMinutes = [null, 15, 60, 1440, 10080];
    private static readonly string[] BuildModeIds = ["owner", "list", "everyone", "nobody"];
    private static readonly int?[] AutoReturnChoices = [null, 5, 15, 30, 60];

    public PersonActions People => _people;
    public IReadOnlyList<LandAccessEntry> AccessEntries => _entries;
    public int ObjectsShownForTesting => _shownObjects.Count;

    private static Label Note(string text = "")
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(520, 0) };
        label.AddThemeColorOverride("font_color", UiStyle.TextMuted); label.AddThemeFontSizeOverride("font_size", 16);
        return label;
    }

    private static OptionButton Choice(params string[] items) { var option = new OptionButton { CustomMinimumSize = new Vector2(0, 40), FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; foreach (var item in items) option.AddItem(item); return option; }

    private static LineEdit Entry(string placeholder, int max = 120) { var edit = new LineEdit { PlaceholderText = placeholder, MaxLength = max, CustomMinimumSize = new Vector2(0, 40), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; edit.AddThemeFontSizeOverride("font_size", 18); return edit; }

    // ---- building the pages ------------------------------------------------------------------------------------

    private Control BuildAccessPage()
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false }; scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var page = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; page.AddThemeConstantOverride("separation", 12); scroll.AddChild(page);
        _accessNote = Note(); page.AddChild(_accessNote);

        var modeBox = Section(page, "Quem pode entrar");
        var modeRow = new HBoxContainer(); modeRow.AddThemeConstantOverride("separation", 8); modeBox.AddChild(modeRow);
        _mode = Choice("Público (todos)", "Só a lista de permitidos", "Privado (só eu e o locatário)"); modeRow.AddChild(_mode);
        modeRow.AddChild(Act("Aplicar", UiStyle.ButtonKind.Primary, ApplyMode));
        modeBox.AddChild(Note("Quem está banido não entra em nenhum modo. O dono da ilha e os administradores sempre entram."));

        var buildBox = Section(page, "Quem pode construir");
        _buildMode = Choice("Só o dono (e o locatário)", "O dono e a lista de construtores", "Todos", "Ninguém (só administradores da ilha)"); buildBox.AddChild(_buildMode);
        _allowScripts = new CheckBox { Text = " Permitir scripts nos objetos de quem constrói aqui", TooltipText = "O dono e o locatário sempre podem; os outros só se isto estiver ligado" }; _allowScripts.AddThemeFontSizeOverride("font_size", 17); buildBox.AddChild(_allowScripts);
        var returnRow = new HBoxContainer(); returnRow.AddThemeConstantOverride("separation", 8); buildBox.AddChild(returnRow);
        returnRow.AddChild(Caption("Devolver objetos de visitantes")); _autoReturn = Choice("Nunca", "Depois de 5 minutos", "Depois de 15 minutos", "Depois de 30 minutos", "Depois de 1 hora"); returnRow.AddChild(_autoReturn);
        buildBox.AddChild(Act("Aplicar regras de construção", UiStyle.ButtonKind.Primary, ApplyBuildOptions));
        buildBox.AddChild(Note("Objetos do dono, do locatário e de quem está nas listas nunca são devolvidos sozinhos. Quem está banido não constrói."));

        var addBox = Section(page, "Permitir, banir, expulsar ou liberar a construção");
        _entryName = Entry("Nome da pessoa"); addBox.AddChild(_entryName);
        var optionRow = new HBoxContainer(); optionRow.AddThemeConstantOverride("separation", 8); addBox.AddChild(optionRow);
        _entryKind = Choice("Permitir entrada", "Banir", "Pode construir"); optionRow.AddChild(_entryKind);
        _entryDuration = Choice("Sem prazo", "15 minutos", "1 hora", "24 horas", "7 dias"); optionRow.AddChild(_entryDuration);
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 8); addBox.AddChild(buttons);
        buttons.AddChild(Act("Adicionar à lista", UiStyle.ButtonKind.Primary, AddEntry));
        buttons.AddChild(Act("Expulsar agora", UiStyle.ButtonKind.Secondary, EjectPerson));

        var listBox = Section(page, "Lista de acesso");
        _accessRows = new VBoxContainer(); _accessRows.AddThemeConstantOverride("separation", 4); listBox.AddChild(_accessRows);
        BuildGroupAccessSection(page);
        return scroll;
    }

    private Control BuildAdminPage()
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false }; scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var page = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; page.AddThemeConstantOverride("separation", 12); scroll.AddChild(page);

        _splitBox = Section(page, "Dividir terreno");
        _splitNote = Note("Selecione, arrastando sobre o chão, a parte do terreno que vai virar um terreno novo. As duas partes precisam ficar inteiras (de uma peça só).");
        _splitBox.AddChild(_splitNote);
        var splitRow = new HBoxContainer(); splitRow.AddThemeConstantOverride("separation", 8); _splitBox.AddChild(splitRow);
        _splitName = Entry("Nome do novo terreno"); splitRow.AddChild(_splitName);
        splitRow.AddChild(Act("Dividir", UiStyle.ButtonKind.Primary, SplitSelection));

        _mergeBox = Section(page, "Unir terrenos");
        _mergeNote = Note("Selecione, arrastando, uma área que toque dois ou mais terrenos seus que se encostam.");
        _mergeBox.AddChild(_mergeNote);
        var mergeRow = new HBoxContainer(); mergeRow.AddThemeConstantOverride("separation", 8); _mergeBox.AddChild(mergeRow);
        mergeRow.AddChild(Caption("Valem as configurações de")); _mergePrimary = Choice(); mergeRow.AddChild(_mergePrimary);
        _mergeBox.AddChild(Act("Unir terrenos", UiStyle.ButtonKind.Primary, MergeSelection));

        var history = Section(page, "Histórico administrativo");
        _auditRows = new VBoxContainer(); _auditRows.AddThemeConstantOverride("separation", 2); history.AddChild(_auditRows);
        return scroll;
    }

    /// The list of objects and the buttons that send them back, at the end of the Objetos tab.
    private void BuildObjectsList(VBoxContainer page)
    {
        var box = Section(page, "Objetos no terreno");
        _objectsNote = Note(); box.AddChild(_objectsNote);
        _objectFilter = Choice("Todos", "Meus", "De terceiros", "Com scripts"); _objectFilter.ItemSelected += _ => ShowObjects(); box.AddChild(_objectFilter);
        _objectList = new ItemList { SelectMode = ItemList.SelectModeEnum.Multi, CustomMinimumSize = new Vector2(0, 150), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; box.AddChild(_objectList);
        _returnReason = Entry("Motivo da devolução (obrigatório)", 200); box.AddChild(_returnReason);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); box.AddChild(row);
        row.AddChild(Act("Devolver selecionados", UiStyle.ButtonKind.Secondary, () => ReturnObjects("ids")));
        row.AddChild(Act("Devolver não autorizados", UiStyle.ButtonKind.Secondary, () => ReturnObjects("others")));
        box.AddChild(Note("Devolver manda o objeto de volta ao inventário de quem o fez, como construção salva. Nada é apagado de vez."));
    }

    // ---- showing the right things ----------------------------------------------------------------------------------

    private void ShowInfoPage(int index)
    {
        var pages = new[] { _generalPage, _objectsPage, _tradePage, _accessPage, _adminPage, _stylePage };
        for (var i = 0; i < pages.Length; i++) pages[i].Visible = i == index;
        switch (index)
        {
            case 1: LoadObjects(); break;
            case 2: LoadHistory(); break;
            case 3: LoadAccess(); break;
            case 4: LoadAudit(); break;
        }
    }

    private LandParcelData? Managed => _tradeParcel is { Mine: true } parcel ? parcel : null;

    private void UpdateAdmin(List<LandParcelData> parcels, int free)
    {
        if (_accessNote is null) return;
        var parcel = Managed;
        _accessNote.Text = parcel is null ? "O acesso é do proprietário: selecione um terreno seu (inteiro)." : string.Empty; _accessNote.Visible = parcel is null;
        foreach (var control in new Control[] { _mode, _entryName, _entryKind, _entryDuration }) if (control is BaseButton button) button.Disabled = parcel is null; else if (control is LineEdit edit) edit.Editable = parcel is not null;

        // splitting: part of one parcel of mine
        var mine = parcels.Where(p => p.Mine).ToList();
        var partOfOne = parcels.Count == 1 && free == 0 && mine.Count == 1 && _selection.Count > 0 && _selection.Count < parcels[0].CellCount;
        _splitBox.Visible = true; _splitName.Editable = partOfOne;
        _splitNote.Text = partOfOne ? $"Vai virar um terreno novo: {_selection.Count:N0} quadrados ({Metres(LandMathArea(_selection.Count))} m²) do terreno “{parcels[0].Name}”.".Replace(",", ".") : "Selecione, arrastando sobre o chão, a parte do terreno que vai virar um terreno novo (só uma parte de um terreno seu). As duas partes precisam ficar de uma peça só.";

        // joining: two or more of mine
        var canMerge = parcels.Count >= 2 && parcels.All(p => p.Mine) && free == 0;
        _mergeNote.Text = canMerge ? $"Vão virar um só: {string.Join(", ", parcels.Select(p => $"“{p.Name}”"))}. Os objetos continuam onde estão; o limite de construção é somado." : "Selecione, arrastando, uma área que toque dois ou mais terrenos seus que se encostam.";
        _mergePrimary.Clear(); if (canMerge) foreach (var p in parcels) _mergePrimary.AddItem(p.Name);
        _mergePrimary.Disabled = !canMerge;

        if (parcel is not null)
        {
            var index = Array.IndexOf(ModeIds, parcel.AccessMode); _mode.Select(Math.Max(index, 0)); _accessMode = parcel.AccessMode;
            _buildMode.Select(Math.Max(Array.IndexOf(BuildModeIds, parcel.BuildMode), 0)); _allowScripts.SetPressedNoSignal(parcel.AllowScripts); _autoReturn.Select(Math.Max(Array.FindIndex(AutoReturnChoices, c => c == parcel.AutoReturnMinutes), 0));
        }
        _buildMode.Disabled = _allowScripts.Disabled = _autoReturn.Disabled = parcel is null;
        if (_objectsNote is not null) _objectsNote.Text = parcel is null ? "A lista de objetos aparece para o dono do terreno." : string.Empty;
    }

    private static double LandMathArea(int cells) => cells * 16.0;

    private void ShowAccess()
    {
        foreach (var child in _accessRows.GetChildren()) child.QueueFree();
        if (_entries.Count == 0) { _accessRows.AddChild(Note("Ninguém na lista.")); return; }
        foreach (var entry in _entries)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            var until = entry.ExpiresAt is { } at ? $" até {at.ToLocalTime().ToString("dd/MM HH:mm", Pt)}" : string.Empty;
            var label = new Label { Text = $"{entry.UserName} · {(entry.Kind == "ban" ? "banido" : entry.Kind == "build" ? "pode construir" : "permitido")}{until}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis }; label.AddThemeFontSizeOverride("font_size", 17);
            if (entry.Kind == "ban") label.AddThemeColorOverride("font_color", UiStyle.DangerBright);
            row.AddChild(label);
            var id = entry.Id; row.AddChild(Act("Remover", UiStyle.ButtonKind.Secondary, () => RemoveEntry(id)));
            _accessRows.AddChild(row);
        }
    }

    private void ShowObjects()
    {
        _shownObjects.Clear(); _objectList?.Clear();
        if (_objectList is null) return;
        var me = _session.UserId;
        IEnumerable<LandObjectRow> rows = _objectFilter.Selected switch { 1 => _objectRows.Where(o => o.OwnerUserId == me), 2 => _objectRows.Where(o => !o.Authorized), 3 => _objectRows.Where(o => o.HasScripts), _ => _objectRows };
        foreach (var row in rows) { _shownObjects.Add(row); _objectList.AddItem($"{row.Name} · {row.OwnerName} · custo {row.Cost}{(row.HasScripts ? " · script" : string.Empty)}{(row.Authorized ? string.Empty : " · de terceiros")}"); }
    }

    // ---- loading -----------------------------------------------------------------------------------------------

    private void LoadAccess()
    {
        if (Managed is not { } parcel) { _entries.Clear(); ShowAccess(); LoadGroupAccess(); return; }
        LoadGroupAccess();
        Call(Godot.HttpClient.Method.Get, $"/parcels/{parcel.Id}/access", null, (status, body) =>
        {
            if (status is < 200 or >= 300) { Say(CommerceMessage(status, ErrorCode(body))); return; }
            try
            {
                using var document = JsonDocument.Parse(body); var root = document.RootElement;
                _accessMode = root.GetProperty("mode").GetString() ?? "public"; _mode.Select(Math.Max(Array.IndexOf(ModeIds, _accessMode), 0));
                _entries.Clear();
                foreach (var item in root.GetProperty("entries").EnumerateArray())
                    _entries.Add(new LandAccessEntry(Guid.Parse(item.GetProperty("id").GetString()!), Guid.Parse(item.GetProperty("userId").GetString()!), item.GetProperty("userName").GetString() ?? "—", item.GetProperty("kind").GetString() ?? "allow",
                        item.TryGetProperty("expiresAt", out var e) && e.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(e.GetString(), out var at) ? at : null));
                ShowAccess();
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { Say("Resposta inesperada do servidor."); }
        });
    }

    private void LoadObjects()
    {
        if (_objectList is null) return;
        if (Managed is not { } parcel) { _objectRows.Clear(); ShowObjects(); return; }
        Call(Godot.HttpClient.Method.Get, $"/parcels/{parcel.Id}/objects", null, (status, body) =>
        {
            if (status is < 200 or >= 300) { Say(CommerceMessage(status, ErrorCode(body))); return; }
            try
            {
                using var document = JsonDocument.Parse(body); _objectRows.Clear();
                foreach (var item in document.RootElement.EnumerateArray())
                    _objectRows.Add(new LandObjectRow(Guid.Parse(item.GetProperty("id").GetString()!), item.GetProperty("name").GetString() ?? "Objeto", item.GetProperty("ownerName").GetString() ?? "—", Guid.Parse(item.GetProperty("ownerUserId").GetString()!),
                        item.GetProperty("cost").GetInt32(), item.GetProperty("hasScripts").GetBoolean(), item.GetProperty("authorized").GetBoolean()));
                ShowObjects();
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { Say("Resposta inesperada do servidor."); }
        });
    }

    private static string OperationName(string operation) => operation switch
    {
        "claim" => "Terreno criado", "split" => "Dividido", "split_from" => "Criado por divisão", "merge" => "Unido a outros", "merged_into" => "Unido a outro terreno", "access_mode" => "Modo de acesso mudou", "access_add" => "Pessoa adicionada à lista",
        "access_remove" => "Pessoa tirada da lista", "eject" => "Pessoa expulsa", "return_objects" => "Objetos devolvidos", "sold" => "Vendido", "list_sale" => "Colocado à venda", "cancel_sale" => "Saiu da venda", "list_rent" => "Oferecido para aluguel",
        "cancel_rent_offer" => "Oferta de aluguel retirada", "rented" => "Alugado", "rent_ended" => "Aluguel encerrado", "update" => "Dados alterados", _ => operation
    };

    private void LoadAudit()
    {
        if (_auditRows is null) return;
        foreach (var child in _auditRows.GetChildren()) child.QueueFree();
        if (Managed is not { } parcel) { _auditRows.AddChild(Note("O histórico administrativo aparece para o dono do terreno.")); return; }
        Call(Godot.HttpClient.Method.Get, $"/parcels/{parcel.Id}/audit", null, (status, body) =>
        {
            foreach (var child in _auditRows.GetChildren()) child.QueueFree();
            if (status is < 200 or >= 300) { _auditRows.AddChild(Note("Não foi possível carregar o histórico.")); return; }
            try
            {
                using var document = JsonDocument.Parse(body);
                foreach (var item in document.RootElement.EnumerateArray().Take(15))
                {
                    var when = DateTimeOffset.TryParse(item.GetProperty("at").GetString(), out var at) ? at.ToLocalTime().ToString("dd/MM HH:mm", Pt) : "—";
                    _auditRows.AddChild(Note($"{when} · {OperationName(item.GetProperty("operation").GetString() ?? string.Empty)} · {item.GetProperty("actorName").GetString()}"));
                }
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { _auditRows.AddChild(Note("Resposta inesperada do servidor.")); }
        });
    }

    // ---- doing things ------------------------------------------------------------------------------------------

    private static string AdminMessage(int status, string error) => error switch
    {
        "not_contiguous" => "O terreno precisa ficar de uma peça só (os quadrados têm de se encostar pelos lados).",
        "whole_parcel" => "Selecione só uma parte: o terreno inteiro não pode virar um novo.",
        "not_in_parcel" => "A seleção sai do terreno.",
        "limit_too_low" => "Não dá para dividir: já há objetos demais para o limite de construção ser repartido.",
        "name_taken" => "Já existe um terreno com esse nome nesta ilha.",
        "bad_name" => "Dê um nome ao terreno.",
        "not_adjacent" => "Esses terrenos não se encostam (só pelos lados conta).",
        "primary_required" => "Escolha de qual terreno valem as configurações.",
        "protected" => "Essa pessoa não pode ser banida (dono ou administrador da ilha).",
        "bad_target" => "Escolha outra pessoa que não seja você.",
        "list_full" => "A lista está cheia (200 por tipo).",
        "budget_exceeded" => "Os terrenos da ilha já usam todo o limite de objetos da ilha.",
        "bad_reason" => "Conte o motivo (de 1 a 200 letras).",
        "nothing_to_return" => "Não há objetos para devolver.",
        "rented" => "O terreno está alugado: espere o fim do contrato.",
        _ => CommerceMessage(status, error)
    };

    private void MutateAdmin(Godot.HttpClient.Method method, string path, object? body, string done, Action? after = null)
    {
        Call(method, path, body is null ? null : JsonSerializer.Serialize(body), (status, text) =>
        {
            if (status is >= 200 and < 300) { Say(done); Load(); after?.Invoke(); }
            else Say(AdminMessage(status, ErrorCode(text)));
        });
    }

    private void ApplyBuildOptions()
    {
        if (Managed is not { } parcel) return;
        MutateAdmin(Godot.HttpClient.Method.Put, $"/parcels/{parcel.Id}/options", new { buildMode = BuildModeIds[Mathf.Clamp(_buildMode.Selected, 0, BuildModeIds.Length - 1)], allowScripts = _allowScripts.ButtonPressed, autoReturnMinutes = AutoReturnChoices[Mathf.Clamp(_autoReturn.Selected, 0, AutoReturnChoices.Length - 1)] }, "Regras de construção atualizadas.", LoadAccess);
    }

    private void ApplyMode()
    {
        if (Managed is not { } parcel) return;
        MutateAdmin(Godot.HttpClient.Method.Put, $"/parcels/{parcel.Id}/access-mode", new { mode = ModeIds[Mathf.Clamp(_mode.Selected, 0, ModeIds.Length - 1)] }, "Modo de acesso atualizado.", LoadAccess);
    }

    /// Finds the person by name, then does something with their account.
    private void WithPerson(string name, Action<Guid> action)
    {
        if (string.IsNullOrWhiteSpace(name)) { Say("Digite o nome da pessoa."); return; }
        _people.Find(name.Trim(), action);
    }

    private void AddEntry()
    {
        if (Managed is not { } parcel) return;
        var kind = _entryKind.Selected switch { 1 => "ban", 2 => "build", _ => "allow" }; var minutes = DurationMinutes[Mathf.Clamp(_entryDuration.Selected, 0, DurationMinutes.Length - 1)]; var name = _entryName.Text.Trim();
        WithPerson(name, id => MutateAdmin(Godot.HttpClient.Method.Post, $"/parcels/{parcel.Id}/access", new { userId = id, kind, minutes, reason = string.Empty }, kind == "ban" ? $"{name} foi banido." : kind == "build" ? $"{name} pode construir neste terreno." : $"{name} foi permitido.", () => { _entryName.Text = string.Empty; LoadAccess(); }));
    }

    private void EjectPerson()
    {
        if (Managed is not { } parcel) return;
        var name = _entryName.Text.Trim();
        WithPerson(name, id => MutateAdmin(Godot.HttpClient.Method.Post, $"/parcels/{parcel.Id}/eject", new { userId = id }, $"{name} foi expulso do terreno.", LoadAccess));
    }

    private void RemoveEntry(Guid id)
    {
        if (Managed is not { } parcel) return;
        MutateAdmin(Godot.HttpClient.Method.Delete, $"/parcels/{parcel.Id}/access/{id}", null, "Pessoa tirada da lista.", LoadAccess);
    }

    private void SplitSelection()
    {
        var (parcels, free) = Covered();
        if (parcels.Count != 1 || free != 0 || !parcels[0].Mine || _selection.Count == 0 || _selection.Count >= parcels[0].CellCount) { Say("Selecione só uma parte de um terreno seu."); return; }
        if (string.IsNullOrWhiteSpace(_splitName.Text)) { Say("Dê um nome ao novo terreno."); return; }
        var cells = _selection.Select(k => { var (cx, cz) = LifeVerse.Shared.Land.LandMath.Unkey(k); return new[] { cx, cz }; }).ToArray(); var name = _splitName.Text.Trim();
        MutateAdmin(Godot.HttpClient.Method.Post, $"/parcels/{parcels[0].Id}/split", new { cells, name }, $"Terreno dividido: “{name}” foi criado.", () => { _splitName.Text = string.Empty; ClearSelection(); });
    }

    private void MergeSelection()
    {
        var (parcels, free) = Covered();
        if (parcels.Count < 2 || free != 0 || !parcels.All(p => p.Mine)) { Say("Selecione dois ou mais terrenos seus que se encostam."); return; }
        var primary = parcels[Mathf.Clamp(_mergePrimary.Selected, 0, parcels.Count - 1)];
        MutateAdmin(Godot.HttpClient.Method.Post, "/merge", new { parcelIds = parcels.Select(p => p.Id).ToArray(), primaryId = primary.Id }, $"Terrenos unidos em “{primary.Name}”.", () => { var cells = parcels.SelectMany(p => p.Cells).ToHashSet(); SelectCells(cells, false); });
    }

    private void ReturnObjects(string scope)
    {
        if (Managed is not { } parcel) return;
        var reason = _returnReason.Text.Trim();
        if (reason.Length == 0) { Say("Conte o motivo da devolução."); return; }
        var ids = scope == "ids" ? _objectList.GetSelectedItems().Where(i => i < _shownObjects.Count).Select(i => _shownObjects[i].Id).ToArray() : [];
        if (scope == "ids" && ids.Length == 0) { Say("Escolha os objetos da lista."); return; }
        MutateAdmin(Godot.HttpClient.Method.Post, $"/parcels/{parcel.Id}/objects/return", new { scope, objectIds = ids, reason }, "Objetos devolvidos aos donos.", () => { _returnReason.Text = string.Empty; LoadObjects(); });
    }

    // ---- tests -------------------------------------------------------------------------------------------------

    public void ShowPageForTesting(int index) => _infoTabs.Select(index);
    public string AccessNoteForTesting => _accessNote.Visible ? _accessNote.Text : string.Empty;
    public string SplitNoteForTesting => _splitNote.Text;
    public string MergeNoteForTesting => _mergeNote.Text;
    public bool SplitEnabledForTesting => _splitName.Editable;
    public bool MergeEnabledForTesting => !_mergePrimary.Disabled;
    public void SetModeForTesting(int index) => _mode.Select(index);
    public void ApplyModeForTesting() => ApplyMode();
    public void SetBuildOptionsForTesting(int mode, bool scripts, int autoReturn) { _buildMode.Select(mode); _allowScripts.SetPressedNoSignal(scripts); _autoReturn.Select(autoReturn); }
    public void ApplyBuildOptionsForTesting() => ApplyBuildOptions();
    public int BuildModeShownForTesting => _buildMode.Selected;
    public bool AllowScriptsShownForTesting => _allowScripts.ButtonPressed;
    public int AutoReturnShownForTesting => _autoReturn.Selected;
    public void SetEntryForTesting(string name, int kind, int duration) { _entryName.Text = name; _entryKind.Select(kind); _entryDuration.Select(duration); }
    public void AddEntryForTesting() => AddEntry();
    public void EjectForTesting() => EjectPerson();
    public void RemoveEntryForTesting(int index) => RemoveEntry(_entries[index].Id);
    public void SetSplitNameForTesting(string name) => _splitName.Text = name;
    public void SplitForTesting() => SplitSelection();
    public void MergeForTesting(int primaryIndex) { _mergePrimary.Select(primaryIndex); MergeSelection(); }
    public void SetFilterForTesting(int index) { _objectFilter.Select(index); ShowObjects(); }
    public void SelectObjectsForTesting(params int[] indexes) { _objectList.DeselectAll(); foreach (var index in indexes) _objectList.Select(index, false); }
    public void ReturnForTesting(string scope, string reason) { _returnReason.Text = reason; ReturnObjects(scope); }
    public int AuditRowsForTesting => _auditRows.GetChildCount();
    public int AccessRowsForTesting => _accessRows.GetChildCount();
}
