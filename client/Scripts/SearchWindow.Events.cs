using System.Globalization;
using System.Text.Json;
using Godot;
using LifeVerse.Shared.Search;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The Events tab: the chosen event shows its description, when (in the time of this computer, with the offset), where, who organizes it, what it costs and how many places are left, and the buttons follow what this
/// person can do: go there, sign up or leave, keep it in the calendar, share the link, open the organizer, call it off (organizer or island owner). "Meu calendário" lists what was signed up for or saved, and
/// "Anunciar evento" opens the form (the server checks who may announce on the island).
/// </summary>
public partial class SearchWindow
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly (string Key, string Label)[] EventCategories = [("show", "Show"), ("festa", "Festa"), ("encontro", "Encontro social"), ("rpg", "RPG"), ("competicao", "Competição"), ("aula", "Aula"), ("exposicao", "Exposição"), ("comercial", "Evento comercial"), ("comunitario", "Evento comunitário"), ("outros", "Outros")];
    private static string EventCategoryLabel(string key) => EventCategories.FirstOrDefault(c => c.Key == key).Label ?? key;

    private VBoxContainer _eventBox = null!;
    private Button _eventJoin = null!, _eventCalendar = null!, _eventShare = null!, _eventOrganizer = null!, _eventCancel = null!;
    private int _eventSeq;
    private Guid _eventId, _organizerId;
    private bool _eventJoined, _eventInCalendar, _eventCanJoin, _eventFull;
    private string _eventStatus = string.Empty;

    private void BuildEventActions(Control parent)
    {
        _eventBox = new VBoxContainer { Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _eventBox.AddThemeConstantOverride("separation", 6); parent.AddChild(_eventBox);
        _eventJoin = Action("Participar", UiStyle.ButtonKind.Primary, OnEventJoin); _eventBox.AddChild(_eventJoin);
        _eventCalendar = Action("Adicionar ao calendário", UiStyle.ButtonKind.Secondary, OnEventCalendar); _eventBox.AddChild(_eventCalendar);
        _eventShare = Action("Compartilhar", UiStyle.ButtonKind.Secondary, () => { if (Selected is { Kind: SearchKinds.Event } e) { DisplayServer.ClipboardSet(SearchLinks.For(SearchKinds.Event, e.Id)); _note.Text = "Link do evento copiado."; } }); _eventBox.AddChild(_eventShare);
        _eventOrganizer = Action("Ver organizador", UiStyle.ButtonKind.Secondary, () => { if (_organizerId != Guid.Empty) OpenProfile?.Invoke(_organizerId); }); _eventBox.AddChild(_eventOrganizer);
        _eventCancel = Action("Cancelar evento", UiStyle.ButtonKind.Secondary, OnEventCancel); _eventCancel.Visible = false; _eventBox.AddChild(_eventCancel);
    }

    private static string ZoneText(DateTimeOffset when) { var offset = TimeZoneInfo.Local.GetUtcOffset(when); return "UTC" + (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString(@"hh\:mm"); }

    private static string RangeText(DateTimeOffset start, DateTimeOffset end)
    {
        var a = start.ToLocalTime(); var b = end.ToLocalTime();
        return a.Date == b.Date ? $"{a.ToString("dd/MM/yyyy HH:mm", Br)} às {b.ToString("HH:mm", Br)} ({ZoneText(a)})" : $"{a.ToString("dd/MM/yyyy HH:mm", Br)} até {b.ToString("dd/MM/yyyy HH:mm", Br)} ({ZoneText(a)})";
    }

    private static string PriceText(long? coins) => coins is > 0 ? $"L$ {coins:N0}" : "Gratuito";

    /// <summary>The grey line of an event card: when it is (or "Agora"), where and what it costs.</summary>
    private static string EventMeta(SearchHit hit)
    {
        var place = hit.Subtitle.StartsWith("Evento em ", StringComparison.Ordinal) ? hit.Subtitle["Evento em ".Length..] : hit.Subtitle; var parts = new List<string> { "Evento" };
        if (hit.StartsAt is { } start)
        {
            var now = DateTimeOffset.Now; var live = hit.EndsAt is { } end && start <= now && now <= end;
            parts.Add(live ? "● Agora" : start.ToLocalTime().ToString("dd/MM HH:mm", Br));
        }
        if (place.Length > 0) parts.Add(place); if (hit.PriceCoins is not null) parts.Add(PriceText(hit.PriceCoins));
        return string.Join(" · ", parts);
    }

    // ---- loading the event ---------------------------------------------------------------------------------------------------------

    private void LoadEvent(SearchHit hit)
    {
        var seq = ++_eventSeq; _eventId = hit.Id; _organizerId = Guid.Empty; _eventStatus = string.Empty; _extra.Text = "Carregando o evento…"; UpdateEventButtons();
        Send(Godot.HttpClient.Method.Get, SearchApi.EventPath(hit.Id), null, (status, body) =>
        {
            if (seq != _eventSeq || Selected?.Id != hit.Id) return;
            if (status is < 200 or >= 300) { _extra.Text = status == 404 ? "Este evento não está mais disponível." : "Não foi possível carregar o evento agora."; _eventStatus = status == 404 ? "gone" : string.Empty; UpdateEventButtons(); return; }
            try
            {
                using var document = JsonDocument.Parse(body); var e = document.RootElement;
                _eventStatus = Str(e, "status"); _eventJoined = Flag(e, "joined"); _eventInCalendar = Flag(e, "inCalendar"); _eventCanJoin = Flag(e, "canJoin"); _eventFull = Flag(e, "full");
                if (e.TryGetProperty("organizer", out var organizer) && Guid.TryParse(Str(organizer, "id"), out var organizerId)) _organizerId = organizerId;
                if (Str(e, "description") is { Length: > 0 } description) _pDescription.Text = description;
                DateTimeOffset.TryParse(Str(e, "startsAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var start); DateTimeOffset.TryParse(Str(e, "endsAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var end);
                var capacity = Int(e, "capacity"); var going = Int(e, "going");
                var lines = new List<string>
                {
                    _eventStatus switch { "live" => "● Acontecendo agora", "scheduled" => "Agendado", "ended" => "Encerrado", "cancelled" => "Cancelado", _ => string.Empty },
                    "Quando: " + RangeText(start, end),
                    e.TryGetProperty("island", out var island) ? "Onde: " + Str(island, "name") : string.Empty,
                    _organizerId != Guid.Empty ? "Organização: " + Str(organizer, "displayName") : string.Empty,
                    "Valor: " + (Long(e, "priceCoins") > 0 ? $"L$ {Long(e, "priceCoins"):N0}, cobrado na entrada" : "gratuito"),
                    capacity > 0 ? $"Vagas: {going} de {capacity} preenchidas" : $"Vagas ilimitadas · {going} {(going == 1 ? "inscrito" : "inscritos")}",
                    string.Join(" · ", new[] { Str(e, "categoryLabel"), "classificação " + Str(e, "contentRating") }.Where(t => t.Length > 0)),
                };
                _extra.Text = string.Join("\n", lines.Where(l => l.Length > 0)); _eventCancel.Visible = Flag(e, "canManage");
            }
            catch (JsonException) { _extra.Text = "Resposta inesperada do servidor."; }
            UpdateEventButtons();
        });
    }

    private void UpdateEventButtons()
    {
        var closed = _eventStatus is "ended" or "cancelled" or "gone";
        (_eventJoin.Text, _eventJoin.Disabled) = _eventStatus == "" ? ("Participar", true) : _eventStatus == "cancelled" ? ("Evento cancelado", true) : _eventStatus is "ended" or "gone" ? ("Evento encerrado", true)
            : _eventJoined ? ("Sair do evento", false) : _eventFull ? ("Evento lotado", true) : _eventCanJoin ? ("Participar", false) : ("Participar", true);
        _eventCalendar.Text = _eventInCalendar ? "Remover do calendário" : "Adicionar ao calendário"; _eventCalendar.Disabled = _eventStatus == "" || (closed && !_eventInCalendar);
        _eventOrganizer.Visible = _organizerId != Guid.Empty;
        if (closed && Selected is { Kind: SearchKinds.Event }) { _goButton.Disabled = true; _goButton.Text = _eventStatus == "cancelled" ? "Evento cancelado" : "Evento encerrado"; }
    }

    private static string EventWords(int status, string code) => code switch
    {
        "event_full" => "Este evento está lotado.", "cancelled" => "Este evento foi cancelado.", "ended" or "already_ended" => "Este evento já terminou.", "not_found" => "Este evento não está mais disponível.", "forbidden" => "Só quem organiza o evento ou é dono da ilha pode fazer isso.",
        _ => status == 0 ? "Sem conexão com o servidor." : $"Não foi possível concluir agora (HTTP {status})."
    };

    private void EventCall(Godot.HttpClient.Method method, string path, string? body, string ok)
    {
        var id = _eventId;
        Send(method, path, body, (status, text) =>
        {
            if (status is < 200 or >= 300) { _note.Text = EventWords(status, ErrorCode(text)); if (Selected is { Kind: SearchKinds.Event } now && now.Id == id) LoadEvent(now); return; }
            _note.Text = ok; if (Selected is { Kind: SearchKinds.Event } current && current.Id == id) LoadEvent(current);
        });
    }

    private void OnEventJoin()
    {
        if (Selected is not { Kind: SearchKinds.Event }) return;
        if (_eventJoined) EventCall(Godot.HttpClient.Method.Post, $"/api/v1/discovery/events/{_eventId}/leave", "{}", "Você saiu do evento.");
        else EventCall(Godot.HttpClient.Method.Post, $"/api/v1/discovery/events/{_eventId}/join", "{}", "Inscrição feita. Até lá!");
    }

    private void OnEventCalendar()
    {
        if (Selected is not { Kind: SearchKinds.Event }) return; var on = !_eventInCalendar;
        EventCall(Godot.HttpClient.Method.Put, $"/api/v1/discovery/events/{_eventId}/calendar", JsonSerializer.Serialize(new { on }), on ? "Evento guardado no seu calendário." : "Evento tirado do seu calendário.");
        if (_onlyCalendar.ButtonPressed && !on) _retry = LoadAgenda;
    }

    private void OnEventCancel()
    {
        if (Selected is not { Kind: SearchKinds.Event } e) return; var id = e.Id;
        AskYes("Cancelar evento", $"Cancelar “{e.Title}”? Quem se inscreveu verá o evento como cancelado.", "Cancelar evento", () => Send(Godot.HttpClient.Method.Post, $"/api/v1/discovery/events/{id}/cancel", "{}", (status, text) =>
        {
            if (status is < 200 or >= 300) { _note.Text = EventWords(status, ErrorCode(text)); return; }
            _flash = "Evento cancelado."; _note.Text = _flash; if (_onlyCalendar.ButtonPressed) LoadAgenda(); else Search(_page.Page);
        }));
    }

    // ---- my calendar ---------------------------------------------------------------------------------------------------------------

    private void LoadAgenda()
    {
        if (!Begin(SearchApi.MyEventsPath)) return; var seq = _seq; _retry = LoadAgenda; if (_hits.Count == 0) ShowState(Pending, icon: "sun");
        Send(Godot.HttpClient.Method.Get, SearchApi.MyEventsPath, null, (status, body) =>
        {
            if (seq != _seq) return; Done();
            if (status is < 200 or >= 300) { Fail(status); return; }
            var hits = new List<SearchHit>();
            try
            {
                using var document = JsonDocument.Parse(body);
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (!Guid.TryParse(Str(item, "id"), out var id)) continue; item.TryGetProperty("island", out var island); Guid.TryParse(Str(island, "id"), out var islandId);
                    DateTimeOffset.TryParse(Str(item, "startsAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var start); DateTimeOffset.TryParse(Str(item, "endsAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var end);
                    hits.Add(new SearchHit(SearchKinds.Event, "Evento", id, Str(item, "name"), Str(island, "name"), string.Empty, string.Empty, null, islandId == Guid.Empty ? null : islandId, null, null, 0, false, start, end, null));
                }
            }
            catch (JsonException) { Fail(0); return; }
            Fill(hits, SearchPageData.Empty); _count.Text = hits.Count == 1 ? "1 evento no calendário" : $"{hits.Count} eventos no calendário";
            if (hits.Count == 0) ShowState("Seu calendário está vazio. Participe de um evento ou guarde um no calendário.", icon: "sun");
            Note(hits.Count == 0 ? "Seu calendário está vazio." : $"{hits.Count} evento(s) no seu calendário.");
        });
    }

    // ---- announcing an event -------------------------------------------------------------------------------------------------------

    private FloatingWindow? _form;
    private LineEdit _fName = null!, _fDate = null!, _fTime = null!, _fImage = null!;
    private TextEdit _fDescription = null!;
    private OptionButton _fCategory = null!, _fRating = null!;
    private SpinBox _fHours = null!, _fCapacity = null!, _fPrice = null!;
    private CheckBox _fPublic = null!;
    private Label _fNote = null!;

    private void OpenEventForm()
    {
        if (_form is not null && GodotObject.IsInstanceValid(_form)) { _form.Visible = true; _form.MoveToFront(); return; }
        _form = new FloatingWindow("Anunciar evento", icon: "sun") { Name = "SearchEventForm" }; _form.Place(.5f, .5f, -330, -340, 330, 340);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _form.Content.AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 6); scroll.AddChild(box);
        Label Caption(string text) { var l = Text(text, true, 13); box.AddChild(l); return l; }
        Caption("O evento acontece na ilha onde você está agora. Só o dono da ilha anuncia, ou qualquer pessoa se o dono permitir.");
        Caption("Nome"); _fName = new LineEdit { MaxLength = 100, PlaceholderText = "Ex.: Show de música ao vivo", CustomMinimumSize = new Vector2(0, 38) }; box.AddChild(_fName);
        Caption("Descrição"); _fDescription = new TextEdit { CustomMinimumSize = new Vector2(0, 90), WrapMode = TextEdit.LineWrappingMode.Boundary, PlaceholderText = "Conte o que vai acontecer…" }; box.AddChild(_fDescription);
        _fDescription.TextChanged += () => { if (_fDescription.Text.Length > 1500) _fDescription.Text = _fDescription.Text[..1500]; };
        Caption("Categoria"); _fCategory = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36) }; foreach (var (_, label) in EventCategories) _fCategory.AddItem(label); box.AddChild(_fCategory);
        var when = new HBoxContainer(); when.AddThemeConstantOverride("separation", 8); box.AddChild(when);
        var tomorrow = DateTimeOffset.Now.AddDays(1);
        _fDate = new LineEdit { Text = tomorrow.ToString("dd/MM/yyyy", Br), PlaceholderText = "dd/mm/aaaa", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38), TooltipText = "Data" }; when.AddChild(_fDate);
        _fTime = new LineEdit { Text = "20:00", PlaceholderText = "hh:mm", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38), TooltipText = "Hora de começo (horário deste computador)" }; when.AddChild(_fTime);
        Caption("Duração (horas)"); _fHours = new SpinBox { MinValue = 1, MaxValue = 24, Value = 2, CustomMinimumSize = new Vector2(0, 36) }; box.AddChild(_fHours);
        Caption("Classificação de conteúdo"); _fRating = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36) }; _fRating.AddItem("Livre"); _fRating.AddItem("Moderado"); _fRating.AddItem("Adulto"); box.AddChild(_fRating);
        Caption("Vagas (0 = sem limite)"); _fCapacity = new SpinBox { MinValue = 0, MaxValue = 1000, Value = 0, CustomMinimumSize = new Vector2(0, 36) }; box.AddChild(_fCapacity);
        Caption("Valor na entrada em L$ (0 = gratuito)"); _fPrice = new SpinBox { MinValue = 0, MaxValue = 1_000_000, Value = 0, CustomMinimumSize = new Vector2(0, 36) }; box.AddChild(_fPrice);
        Caption("Imagem (endereço https, opcional)"); _fImage = new LineEdit { MaxLength = 1024, PlaceholderText = "https://…", CustomMinimumSize = new Vector2(0, 38) }; box.AddChild(_fImage);
        _fPublic = new CheckBox { Text = " Aparece na busca", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None }; box.AddChild(_fPublic);
        _fNote = Text("", false, 14); _fNote.AddThemeColorOverride("font_color", UiStyle.Orange); box.AddChild(_fNote);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); _form.Content.AddChild(row);
        row.AddChild(Action("Anunciar", UiStyle.ButtonKind.Primary, SubmitEventForm)); row.AddChild(Action("Cancelar", UiStyle.ButtonKind.Secondary, () => _form?.QueueFree()));
        AddChild(_form); _form.MoveToFront();
    }

    private static string FormWords(string code) => code switch
    {
        "not_allowed" => "Só o dono da ilha (ou qualquer pessoa, se o dono permitir) anuncia eventos aqui.", "island_not_found" => "Esta ilha não está mais disponível.", "bad_name" => "O nome precisa ter de 3 a 100 letras.", "bad_description" => "A descrição passou de 1500 letras.",
        "bad_category" => "Escolha uma categoria.", "bad_rating" => "Escolha a classificação.", "bad_price" => "O valor vai de 0 a 1.000.000.", "bad_capacity" => "As vagas vão de 0 a 1000.", "bad_start" => "O evento precisa começar entre agora e um ano.",
        "bad_length" => "A duração vai de 10 minutos a 24 horas.", "bad_image" => "A imagem precisa ser um endereço https.", "bad_arrival" => "O ponto de chegada está fora da ilha.", "too_many_events" => "Você já tem eventos demais em andamento.", "island_busy" => "Esta ilha já tem eventos demais agora.",
        _ => "Não foi possível anunciar agora."
    };

    private void SubmitEventForm()
    {
        var island = CurrentIsland?.Invoke() ?? Guid.Empty;
        if (island == Guid.Empty) { _fNote.Text = "Entre em uma ilha para anunciar um evento nela."; return; }
        if (!DateTime.TryParseExact(_fDate.Text.Trim() + " " + _fTime.Text.Trim(), "dd/MM/yyyy HH:mm", Br, DateTimeStyles.None, out var local)) { _fNote.Text = "Escreva a data como dd/mm/aaaa e a hora como hh:mm."; return; }
        var start = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)); var end = start.AddHours(_fHours.Value);
        var body = JsonSerializer.Serialize(new { name = _fName.Text.Trim(), description = _fDescription.Text.Trim(), islandId = island, startsAt = start.ToUniversalTime(), endsAt = end.ToUniversalTime(), capacity = (int)_fCapacity.Value, isPublic = _fPublic.ButtonPressed, category = EventCategories[_fCategory.Selected].Key, imageUrl = _fImage.Text.Trim(), priceCoins = (long)_fPrice.Value, contentRating = new[] { "livre", "moderado", "adulto" }[_fRating.Selected] });
        _fNote.Text = "Anunciando…";
        Send(Godot.HttpClient.Method.Post, "/api/v1/discovery/events", body, (status, text) =>
        {
            if (status is < 200 or >= 300) { _fNote.Text = status == 0 ? "Sem conexão com o servidor." : FormWords(ErrorCode(text)); return; }
            if (_form is not null && GodotObject.IsInstanceValid(_form)) _form.QueueFree(); _form = null; _flash = "Evento anunciado. Ele já aparece na busca em instantes."; _note.Text = _flash;
            if (_onlyCalendar.ButtonPressed) _onlyCalendar.SetPressedNoSignal(false); if (_tabs.Selected == 3) Search(); else _tabs.Select(3);
        });
    }

    // ---- tests --------------------------------------------------------------------------------------------------------------------

    public bool EventBoxVisibleForTesting => _eventBox.Visible;
    public string EventJoinTextForTesting => _eventJoin.Text;
    public bool EventJoinEnabledForTesting => !_eventJoin.Disabled;
    public string EventCalendarTextForTesting => _eventCalendar.Text;
    public bool EventCalendarEnabledForTesting => !_eventCalendar.Disabled;
    public bool EventCancelVisibleForTesting => _eventCancel.Visible;
    public bool EventOrganizerVisibleForTesting => _eventOrganizer.Visible;
    public string EventMetaForTesting(int row) => _metaLabels[row].Text;
    public void PressEventJoinForTesting() => OnEventJoin();
    public void PressEventCalendarForTesting() => OnEventCalendar();
    public void PressEventOrganizerForTesting() => _eventOrganizer.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressEventCancelForTesting() => OnEventCancel();
    public void PressEventShareForTesting() => _eventShare.EmitSignal(BaseButton.SignalName.Pressed);
    public void SetWhenForTesting(int index) => _when.Selected = index;
    public void SetPriceForTesting(int index) => _price.Selected = index;
    public void SetRatingForTesting(int index) => _rating.Selected = index;
    public void SetOnlyCalendarForTesting(bool on) => _onlyCalendar.SetPressedNoSignal(on);
    public IReadOnlyList<string> SortLabelsForTesting => [.. Enumerable.Range(0, _sort.ItemCount).Select(_sort.GetItemText)];
    public bool FilterRowShowsForTesting(string which) => which switch { "when" => _when.Visible, "price" => _price.Visible, "rating" => _rating.Visible, "calendar" => _onlyCalendar.Visible, "new" => _newEvent.Visible, _ => false };
    public void OpenEventFormForTesting() => OpenEventForm();
    public void FillEventFormForTesting(string name, string date, string time, int hours, int capacity, int price) { _fName.Text = name; _fDate.Text = date; _fTime.Text = time; _fHours.Value = hours; _fCapacity.Value = capacity; _fPrice.Value = price; }
    public void SubmitEventFormForTesting() => SubmitEventForm();
    public string EventFormNoteForTesting => _fNote.Text;
    public bool EventFormOpenForTesting => _form is not null && GodotObject.IsInstanceValid(_form) && !_form.IsQueuedForDeletion();
}
