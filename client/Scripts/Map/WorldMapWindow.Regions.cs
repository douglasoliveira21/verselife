using Godot;

namespace LifeVerse.Client.Scripts.Map;

/// <summary>The Regiões tab: the same islands as a list, with the same filters and the text of the search box, ordered by name, by how many are there or by which ones can be entered, each with "Ver no mapa" and "Teletransportar".</summary>
public partial class WorldMapWindow
{
    private ScrollContainer _regionsPage = null!;
    private VBoxContainer _regionRows = null!;
    private OptionButton _sort = null!;
    private Label _regionCount = null!;

    private void BuildRegions()
    {
        _regionsPage = new ScrollContainer { Visible = false, Name = "Regions", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(320, 240) }; _regionsPage.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _stack.AddChild(_regionsPage);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; column.AddThemeConstantOverride("separation", 8); _regionsPage.AddChild(column);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8); column.AddChild(head);
        head.AddChild(Lbl("Ordenar por", 14, false, UiStyle.TextMuted)); _sort = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(190, 36) }; foreach (var s in new[] { "Nome A–Z", "Mais visitantes", "Disponíveis primeiro" }) _sort.AddItem(s); _sort.ItemSelected += _ => FillRegions(); head.AddChild(_sort);
        _regionCount = Lbl("", 14, false, UiStyle.TextMuted); _regionCount.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; _regionCount.HorizontalAlignment = HorizontalAlignment.Right; head.AddChild(_regionCount);
        _regionRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _regionRows.AddThemeConstantOverride("separation", 6); column.AddChild(_regionRows);
    }

    private void FillRegions()
    {
        foreach (var child in _regionRows.GetChildren()) { _regionRows.RemoveChild(child); child.QueueFree(); }
        var text = Norm(_query.Text); IEnumerable<AtlasIsland> list = Filter(); if (text.Length > 0) list = list.Where(i => Score(i, text) > 0);
        list = _sort.Selected switch { 1 => list.OrderByDescending(i => i.Visitors ?? 0).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase), 2 => list.OrderByDescending(i => i.CanEnter).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase), _ => list.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase) };
        var shown = list.ToList(); _regionCount.Text = shown.Count == 1 ? "1 ilha" : $"{shown.Count} ilhas";
        if (shown.Count == 0) { _regionRows.AddChild(Lbl(_items.Count == 0 ? "Ainda não há ilhas no mapa." : "Nenhuma ilha encontrada com estes filtros.", 15, false, UiStyle.TextMuted, wrap: true)); return; }
        foreach (var island in shown)
        {
            var captured = island; var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.FieldFill, new Color(UiStyle.BorderSoft, .6f), 10, 1, 10, 8)); _regionRows.AddChild(card);
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); card.AddChild(row);
            var picture = new TextureRect { CustomMinimumSize = new Vector2(56, 56), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, ClipContents = true, Texture = UiStyle.Icon("map", new Color(UiStyle.TextMuted, .7f), 32) }; row.AddChild(picture);
            if (island.HasTile) Hub.Tiles.Get(island.Id, island.TileVersion, t => { if (t is not null && GodotObject.IsInstanceValid(picture)) picture.Texture = t; });
            var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; info.AddThemeConstantOverride("separation", 0); row.AddChild(info);
            info.AddChild(Lbl((island.Favorite ? "★ " : string.Empty) + island.Name, 16, true));
            info.AddChild(Lbl(string.Join(" · ", new[] { island.Category, RatingWords(island.Rating), island.Visitors is { } v ? $"{v}/{island.MaxPlayers}" : $"até {island.MaxPlayers}" }.Where(t => t.Length > 0)), 13, false, UiStyle.TextMuted));
            var (state, color) = StateOf(island); info.AddChild(Lbl("● " + state, 13, false, color));
            var go = Btn("Ver no mapa", UiStyle.ButtonKind.Tool, () => { _tabs.Select(0); FocusIsland(captured); }, "Mostra a ilha no mapa", 34); go.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(go);
            var tp = Btn("Teletransportar", UiStyle.ButtonKind.Primary, () => Teleport(captured), island.CanEnter ? "Entra nesta ilha" : state, 34); tp.Disabled = !island.CanEnter; tp.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(tp);
        }
    }

    public int RegionRowsForTesting => _regionRows.GetChildren().OfType<PanelContainer>().Count();
    public IReadOnlyList<string> RegionNamesForTesting => [.. _regionRows.GetChildren().OfType<PanelContainer>().Select(c => c.FindChildren("*", "Label", true, false).OfType<Label>().First().Text.TrimStart('★', ' '))];
    public void SetRegionSortForTesting(int index) { _sort.Select(index); FillRegions(); }
    public void PressRegionForTesting(int row, string button) => _regionRows.GetChildren().OfType<PanelContainer>().ElementAt(row).FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text == button).EmitSignal(BaseButton.SignalName.Pressed);
    public bool RegionButtonEnabledForTesting(int row, string button) => !_regionRows.GetChildren().OfType<PanelContainer>().ElementAt(row).FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text == button).Disabled;
}
