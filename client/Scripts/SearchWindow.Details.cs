using Godot;
using LifeVerse.Shared.Search;

namespace LifeVerse.Client.Scripts;

/// <summary>The right column of the search window: the picture, the facts and the buttons of the chosen result (go there, open the profile or the group, favorite, report). Kind-specific actions are added per tab.</summary>
public partial class SearchWindow
{
    private Control _detailBody = null!, _detailEmpty = null!, _reportRow = null!;
    private TextureRect _pImage = null!;
    private Label _pTitle = null!, _pMeta = null!, _pDescription = null!, _pWhere = null!;
    private Button _goButton = null!, _profile = null!, _groupButton = null!, _favorite = null!, _report = null!, _backInDetails = null!;
    private LineEdit _reason = null!;
    private Guid _opened;
    private VBoxContainer _actions = null!;

    public bool ProfileVisibleForTesting => _profile.Visible;
    public void PressProfileForTesting() => _profile.EmitSignal(BaseButton.SignalName.Pressed);
    public bool GroupVisibleForTesting => _groupButton.Visible;
    public void PressGroupForTesting() => _groupButton.EmitSignal(BaseButton.SignalName.Pressed);

    private void BuildDetails()
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2, CustomMinimumSize = new Vector2(0, 0) };
        panel.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.RowFill, new Color(UiStyle.BorderSoft, .55f), 12, 1, 12, 12)); _right = panel; _body.AddChild(panel);
        var inner = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; inner.AddThemeConstantOverride("separation", 8); panel.AddChild(inner);
        _backInDetails = Action("‹ Voltar aos resultados", UiStyle.ButtonKind.Secondary, () => { _detailOpen = false; Adapt(); }, 34); _backInDetails.Visible = false; inner.AddChild(_backInDetails);
        _detailEmpty = Text("Selecione um resultado para visualizar os detalhes.", true, 16); ((Label)_detailEmpty).HorizontalAlignment = HorizontalAlignment.Center; _detailEmpty.SizeFlagsVertical = Control.SizeFlags.ExpandFill; ((Label)_detailEmpty).VerticalAlignment = VerticalAlignment.Center; inner.AddChild(_detailEmpty);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _detailBody = scroll; inner.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 6); scroll.AddChild(body);
        _pImage = new TextureRect { CustomMinimumSize = new Vector2(0, 150), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, ClipContents = true }; body.AddChild(_pImage);
        _pTitle = Text("", false, 20, true); body.AddChild(_pTitle);
        _pMeta = Text("", true); body.AddChild(_pMeta); _pDescription = Text(); body.AddChild(_pDescription); _extra = Text("", true); body.AddChild(_extra); _placeExtra = Text("", true); _placeExtra.Visible = false; body.AddChild(_placeExtra); _pWhere = Text("", true); body.AddChild(_pWhere);
        _actions = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _actions.AddThemeConstantOverride("separation", 6); body.AddChild(_actions);
        _goButton = Action("Ir até lá", UiStyle.ButtonKind.Primary, GoToResult); _actions.AddChild(_goButton);
        _profile = Action("Ver perfil", UiStyle.ButtonKind.Secondary, () => { if (Selected is { Kind: SearchKinds.Person } person) OpenProfile?.Invoke(person.Id); }); _actions.AddChild(_profile);
        _groupButton = Action("Ver grupo", UiStyle.ButtonKind.Secondary, () => { if (Selected is { Kind: SearchKinds.Group } group) OpenGroup?.Invoke(group.Id); }); _actions.AddChild(_groupButton);
        BuildPersonActions(_actions); BuildGroupActions(_actions); BuildPlaceActions(_actions); BuildEventActions(_actions); BuildMarketActions(_actions);
        _favorite = Action("Favoritar", UiStyle.ButtonKind.Secondary, ToggleFavorite); _actions.AddChild(_favorite);
        _report = Action("Denunciar resultado", UiStyle.ButtonKind.Secondary, () => { _reportRow.Visible = !_reportRow.Visible; if (_reportRow.Visible) _reason.GrabFocus(); }); _actions.AddChild(_report);
        _reportRow = new VBoxContainer { Visible = false }; _actions.AddChild(_reportRow);
        _reason = new LineEdit { PlaceholderText = "Por que você está denunciando?", CustomMinimumSize = new Vector2(0, 36), MaxLength = SearchModerationLimits.MaxReason }; _reason.AddThemeFontSizeOverride("font_size", 15); _reportRow.AddChild(_reason);
        _reportRow.AddChild(Action("Enviar denúncia", UiStyle.ButtonKind.Secondary, SendReport, 34));
    }

    // ---- showing the chosen result ------------------------------------------------------------------------------------------------

    private void ShowPreview(SearchHit? hit)
    {
        _reportRow.Visible = false; _reason.Text = string.Empty;
        _detailEmpty.Visible = hit is null; _detailBody.Visible = hit is not null;
        if (hit is null) { _pTitle.Text = _pMeta.Text = _pDescription.Text = _pWhere.Text = string.Empty; _pImage.Texture = null; _opened = Guid.Empty; _selectedIndex = -1; _extra.Text = string.Empty; _personSeq++; _groupSeq++; _placeSeq++; _eventSeq++; _placeNote = _placeFacts = string.Empty; _placeExtra.Visible = false; _guestEvents.Visible = false; _personBox.Visible = _groupBox.Visible = _placeBox.Visible = _eventBox.Visible = _itemBox.Visible = _storeBox.Visible = false; _itemSeq++; _storeSeq++; return; }
        _pTitle.Text = hit.Title; _pMeta.Text = hit.KindLabel + (hit.Subtitle.Length > 0 ? " · " + hit.Subtitle : "") + (hit.Category.Length > 0 && !hit.Subtitle.Contains(hit.Category) ? " · " + hit.Category : "");
        _pDescription.Text = hit.Description.Length > 0 ? hit.Description : "Sem descrição."; _pWhere.Text = WhereText(hit);
        _pImage.Texture = hit.ThumbnailUrl is not null && _thumbnails.TryGetValue(hit.ThumbnailUrl, out var thumb) ? thumb : KindIcon(hit, 72);
        _goButton.Visible = hit.Kind is not (SearchKinds.Person or SearchKinds.Group or SearchKinds.Product); _pWhere.Visible = _goButton.Visible;
        _goButton.Disabled = !hit.HasPlace; _goButton.Text = hit.Kind == SearchKinds.Store ? (!hit.HasPlace ? "Loja virtual (sem endereço)" : IsHere(hit) ? "Visitar loja (nesta ilha)" : "Visitar loja") : !hit.HasPlace ? "Sem lugar para visitar" : IsHere(hit) ? "Teleportar até lá" : "Ir para a ilha";
        _profile.Visible = hit.Kind == SearchKinds.Person; _groupButton.Visible = hit.Kind == SearchKinds.Group && OpenGroup is not null;
        _favorite.Visible = hit.Kind is not (SearchKinds.Person or SearchKinds.Group); _favorite.Text = hit.Favorite ? "Remover dos favoritos" : "Favoritar"; _report.Disabled = false; _report.Visible = hit.Kind != SearchKinds.Person; _personBox.Visible = hit.Kind == SearchKinds.Person; _groupBox.Visible = hit.Kind == SearchKinds.Group; _placeBox.Visible = IsPlaceKind(hit.Kind); _eventBox.Visible = hit.Kind == SearchKinds.Event; _itemBox.Visible = hit.Kind == SearchKinds.Product; _storeBox.Visible = hit.Kind == SearchKinds.Store;
        if (_opened != hit.Id)
        {
            _opened = hit.Id; _extra.Text = string.Empty; _placeNote = _placeFacts = string.Empty; _placeExtra.Visible = false; _guestEvents.Visible = false; SendEvent("open");
            if (hit.Kind == SearchKinds.Person) LoadPerson(hit); else if (hit.Kind == SearchKinds.Group) LoadGroup(hit);
            else if (hit.Kind == SearchKinds.Event) { LoadEvent(hit); LoadPlace(hit); } else if (IsPlaceKind(hit.Kind)) LoadPlace(hit);
            else if (hit.Kind == SearchKinds.Product) LoadItem(hit); else if (hit.Kind == SearchKinds.Store) { LoadStore(hit); if (hit.IslandId is not null) LoadPlace(hit); }
        }
    }

    private bool IsHere(SearchHit hit) => hit.IslandId is { } island && island != Guid.Empty && island == (CurrentIsland?.Invoke() ?? Guid.Empty);

    /// <summary>Where the result is, in words.</summary>
    public string WhereText(SearchHit hit)
    {
        if (!hit.HasPlace) return "Este resultado não tem um lugar para visitar.";
        var spot = hit.X is { } x && hit.Z is { } z ? $"x {x:0}, z {z:0}" : "sem ponto marcado";
        return IsHere(hit) ? $"Nesta ilha · {spot}." : $"Em outra ilha · {spot}. Você entra na ilha e é levado até o ponto.";
    }

    private void SendEvent(string type) => Send(Godot.HttpClient.Method.Post, SearchApi.EventsPath, SearchApi.EventBody(type, _lastQuery), (_, _) => { });

    // ---- actions ------------------------------------------------------------------------------------------------------------------

    /// <summary>Takes the avatar to the chosen result: a spot or plot on this island, or into the island and then to the spot.</summary>
    public void GoToResult()
    {
        if (Selected is not { } hit) return;
        if (!hit.HasPlace || hit.IslandId is not { } island) { _note.Text = "Este resultado não tem um lugar para visitar."; return; }
        SendEvent("teleport");
        if (IsHere(hit))
        {
            if (hit.Kind == SearchKinds.Parcel && TeleportToParcel?.Invoke(hit.Id) == true) { _note.Text = $"Indo para {hit.Title}…"; return; }
            if (hit.X is { } x && hit.Z is { } z && TeleportToPoint is not null) { TeleportToPoint(x, z); _note.Text = $"Indo para {hit.Title}…"; return; }
            _note.Text = "Você já está nesta ilha."; return;
        }
        if (hit.X is { } px && hit.Z is { } pz) SearchTravel.Set(island, px, pz); else SearchTravel.Clear();
        _note.Text = $"Entrando na ilha de {hit.Title}…"; JoinIsland?.Invoke(island);
    }

    private void ToggleFavorite()
    {
        if (Selected is not { } hit) return; var index = _selectedIndex; var wanted = !hit.Favorite;
        Send(wanted ? Godot.HttpClient.Method.Put : Godot.HttpClient.Method.Delete, SearchApi.FavoritePath(hit.Kind, hit.Id), null, (status, _) =>
        {
            if (status is < 200 or >= 300) { _note.Text = status == 404 ? "Este resultado não está mais disponível." : status == 409 ? "Sua lista de favoritos está cheia." : Failure(status); return; }
            if (_onlyFavorites.ButtonPressed && !wanted) { LoadFavorites(_page.Page); return; }
            var updated = hit with { Favorite = wanted }; _hits[index] = updated; ShowPreview(updated);
            if (_cards[index].GetChild(0).GetChild(1).GetChild(0) is Label title) title.Text = (wanted ? "★ " : "") + updated.Title;
            _cardTexts[index] = RowText(updated); _note.Text = wanted ? "Adicionado aos favoritos." : "Removido dos favoritos.";
        });
    }

    private void SendReport()
    {
        if (Selected is not { } hit) return; var reason = _reason.Text.Trim();
        if (reason.Length < 3) { _note.Text = "Conte em poucas palavras o motivo da denúncia."; return; }
        Send(Godot.HttpClient.Method.Post, SearchApi.ReportPath(hit.Kind, hit.Id), SearchApi.ReportBody(reason), (status, _) =>
        {
            _reportRow.Visible = false; _reason.Text = string.Empty;
            _note.Text = status is >= 200 and < 300 ? "Denúncia enviada. Obrigado por ajudar a manter o LifeVerse seguro." : status == 404 ? "Este resultado não está mais disponível." : status == 400 ? "Você não pode denunciar o seu próprio conteúdo." : Failure(status);
        });
    }

    // ---- tests --------------------------------------------------------------------------------------------------------------------

    public void SetQueryForTesting(string text) { _query.Text = text; }
    public void SelectForTesting(int row) { if (row < 0) ShowPreview(null); else Select(row); }
    public void SelectTabForTesting(int tab) => _tabs.Select(tab);
    public void SetSortForTesting(int sort) { _sort.Selected = sort; }
    public void SetOnlyFavoritesForTesting(bool on) { _onlyFavorites.SetPressedNoSignal(on); }
    public void SetCategoryForTesting(string name) { for (var i = 0; i < _category.ItemCount; i++) if (_category.GetItemText(i) == name || _category.GetItemMetadata(i).AsString() == name) _category.Select(i); }
    public void FavoriteForTesting() => ToggleFavorite();
    public void ReportForTesting(string reason) { _reason.Text = reason; SendReport(); }
    public void SetInterestsForTesting(string text) { _interestsText = text; SaveInterests(); }
    public void NextPageForTesting() => Move(1);
    public void PressGoForTesting() => OnGo();
    public void PressCardActionForTesting(int row) => _cards[row].FindChildren("*", "Button", true, false).OfType<Button>().First().EmitSignal(BaseButton.SignalName.Pressed);
    public void PressRetryForTesting() => _resultBox.FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text == "Tentar novamente").EmitSignal(BaseButton.SignalName.Pressed);
    public void PressSeeAllForTesting(string title) => _resultBox.FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text.StartsWith("Ver todos") && b.GetParent().GetChild(0) is Label l && l.Text == title).EmitSignal(BaseButton.SignalName.Pressed);
    public string NoteForTesting => _note.Text;
    public string StateForTesting => _stateText;
    public string CountForTesting => _count.Text;
    public string GoButtonLabelForTesting => _go.Text;
    public string PreviewTitleForTesting => _pTitle.Text;
    public string PreviewWhereForTesting => _pWhere.Text;
    public string GoTextForTesting => _goButton.Text;
    public bool GoEnabledForTesting => _goButton.Visible && !_goButton.Disabled;
    public string FavoriteTextForTesting => _favorite.Text;
    public bool DetailsEmptyVisibleForTesting => _detailEmpty.Visible;
    public IReadOnlyList<string> HintsForTesting => _hintTexts;
    public IReadOnlyList<string> CategoriesForTesting => [.. Enumerable.Range(0, _category.ItemCount).Select(_category.GetItemText)];
    public string InterestsForTesting => _interestsText;
    public string RowTextForTesting(int row) => _cardTexts[row];
    public IReadOnlyList<string> HeadersForTesting => [.. _resultBox.GetChildren().OfType<HBoxContainer>().Select(h => h.GetChildOrNull<Label>(0)?.Text ?? string.Empty).Where(t => t.Length > 0)];
    public bool PagerVisibleForTesting => _pager.Visible;
    public int TabForTesting => _tabs.Selected;
    public IReadOnlyList<string> TabsForTesting => [.. Enumerable.Range(0, _tabs.Count).Select(i => _tabs.TabAt(i).Text.Trim())];
    public Control WindowForTesting => _window;
    public bool NarrowForTesting => _narrow;
    public bool DetailsShownForTesting => _right.Visible;
    public bool ResultsShownForTesting => _left.Visible;
    public void ResizeForTesting(Vector2 size) { _window.Size = size; }
}
