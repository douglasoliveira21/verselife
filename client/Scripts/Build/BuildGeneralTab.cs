using System.Globalization;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Items;

namespace LifeVerse.Client.Build;

/// <summary>
/// The GERAL tab: name and description, creator, owner, previous owner, group, what a click does, the sale of the object and the permissions (anyone, and the next owner).
/// Every control changes the real object through <see cref="BuildObjectProperties"/>; what the person may not change (the object is not theirs) is disabled, not hidden.
/// </summary>
public partial class BuildGeneralTab : VBoxContainer
{
    private BuildObjectProperties _props = null!;
    private BuildNameDirectory _names = null!;
    private Func<IReadOnlyList<(Guid Id, string Name)>> _groups = () => [];
    private LineEdit _name = null!;
    private TextEdit _description = null!;
    private Label _creator = null!, _owner = null!, _lastOwner = null!, _note = null!;
    private OptionButton _group = null!, _click = null!, _mode = null!;
    private CheckBox _forSale = null!, _anyoneMove = null!, _anyoneCopy = null!, _nextModify = null!, _nextCopy = null!, _nextTransfer = null!;
    private SpinBox _price = null!;
    private Button _apply = null!;
    private Control _saleRows = null!;
    private readonly List<Guid?> _groupIds = [];
    private bool _filling, _mine;

    public event Action<string>? Said;

    public string NameForTesting { get => _name.Text; set => _name.Text = value; }
    public string DescriptionForTesting { get => _description.Text; set => _description.Text = value; }
    public string CreatorForTesting => _creator.Text;
    public string OwnerForTesting => _owner.Text;
    public string LastOwnerForTesting => _lastOwner.Text;
    public string ClickForTesting => BuildObjectProperties.ClickActions[Math.Max(0, _click.Selected)].Id;
    public bool ForSaleForTesting => _forSale.ButtonPressed;
    public bool SaleRowsVisibleForTesting => _saleRows.Visible;
    public bool EditableForTesting => !_click.Disabled;
    public bool NextTransferForTesting => _nextTransfer.ButtonPressed;
    public bool AnyoneMoveForTesting => _anyoneMove.ButtonPressed;
    public string NoteForTesting => _note.Text;
    public IReadOnlyList<string> ClickLabelsForTesting => [.. Enumerable.Range(0, _click.ItemCount).Select(_click.GetItemText)];
    public void CommitNameForTesting() => CommitIdentity();
    public void ChooseClickForTesting(string id) { var i = Array.FindIndex(BuildObjectProperties.ClickActions, a => a.Id == id); _click.Select(i); OnClickChosen(i); }
    public void ToggleForSaleForTesting(bool on) { _forSale.ButtonPressed = on; }
    public void SetSaleForTesting(string mode, double price) { _mode.Select(Array.FindIndex(BuildObjectProperties.SaleModes, m => m.Id == mode)); _price.Value = price; }
    public void PressApplyForTesting() => _apply.EmitSignal(BaseButton.SignalName.Pressed);
    public void ToggleAnyoneMoveForTesting(bool on) => _anyoneMove.ButtonPressed = on;
    public void ToggleNextForTesting(bool modify, bool copy, bool transfer) { _nextModify.ButtonPressed = modify; _nextCopy.ButtonPressed = copy; _nextTransfer.ButtonPressed = transfer; }
    public void ChooseGroupForTesting(int index) { _group.Select(index); OnGroupChosen(index); }

    public void Initialize(BuildObjectProperties props, BuildNameDirectory names, Func<IReadOnlyList<(Guid Id, string Name)>> groups) { _props = props; _names = names; _groups = groups; }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8); SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _name = new LineEdit { PlaceholderText = "Nome do objeto", MaxLength = 160, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38) }; _name.TextSubmitted += _ => CommitIdentity(); _name.FocusExited += CommitIdentity;
        AddChild(Row("NOME", _name));
        _description = new TextEdit { PlaceholderText = "Descrição (opcional)", WrapMode = TextEdit.LineWrappingMode.Boundary, CustomMinimumSize = new Vector2(0, 66), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _description.TextChanged += () => { if (_description.Text.Length > 4000) _description.Text = _description.Text[..4000]; }; _description.FocusExited += CommitIdentity;
        AddChild(Row("DESCRIÇÃO", _description));
        _creator = Value(); _owner = Value(); _lastOwner = Value();
        AddChild(Row("CRIADOR", _creator)); AddChild(Row("DONO", _owner)); AddChild(Row("ÚLTIMO DONO", _lastOwner));
        _group = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38), FocusMode = FocusModeEnum.None, TooltipText = "O grupo com quem o objeto é compartilhado" }; _group.ItemSelected += i => OnGroupChosen((int)i);
        AddChild(Row("GRUPO", _group));
        AddChild(new HSeparator());

        _click = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38), FocusMode = FocusModeEnum.None, TooltipText = "O que acontece quando alguém clica no objeto" };
        foreach (var (_, label) in BuildObjectProperties.ClickActions) _click.AddItem(label);
        _click.ItemSelected += i => OnClickChosen((int)i);
        AddChild(Row("AÇÃO AO CLICAR", _click));
        AddChild(new HSeparator());

        _forSale = Check("Colocar objeto à venda", "Quem clicar (com a ação Comprar) ou escolher Comprar paga o preço e recebe o que você vende"); _forSale.Toggled += OnForSaleToggled; AddChild(_forSale);
        var sale = new VBoxContainer { Visible = false }; sale.AddThemeConstantOverride("separation", 6); _saleRows = sale; AddChild(sale);
        _mode = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38), FocusMode = FocusModeEnum.None, TooltipText = "O que o comprador recebe" };
        foreach (var (_, label) in BuildObjectProperties.SaleModes) _mode.AddItem(label);
        sale.AddChild(Row("MODALIDADE", _mode));
        _price = new SpinBox { MinValue = 0, MaxValue = 10_000_000, Step = 1, Value = 10, CustomMinimumSize = new Vector2(150, 38), SelectAllOnFocus = true, Suffix = "moedas" };
        sale.AddChild(Row("PREÇO", _price));
        _apply = new Button { Text = " APLICAR", Icon = UiStyle.Icon("check", UiStyle.Text, 22), FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 42), TooltipText = "Coloca o objeto à venda com este preço e esta modalidade" };
        UiStyle.StyleButton(_apply, UiStyle.ButtonKind.Primary, 12, 6, 14); _apply.AddThemeFontSizeOverride("font_size", 18); _apply.Pressed += ApplySale; sale.AddChild(_apply);
        AddChild(new HSeparator());

        AddChild(Heading("QUALQUER UM"));
        _anyoneMove = Check("Permitir mover objeto", "Qualquer pessoa pode mover e girar o objeto (não muda o que ele é)"); _anyoneMove.Toggled += _ => ApplyAnyone();
        _anyoneCopy = Check("Permitir copiar objeto", "Qualquer pessoa pode pegar uma cópia (só vale se você pode copiar)"); _anyoneCopy.Toggled += _ => ApplyAnyone();
        AddChild(_anyoneMove); AddChild(_anyoneCopy);
        AddChild(Heading("PRÓXIMO DONO"));
        _nextModify = Check("Modificar", "Quem receber poderá editar o objeto"); _nextCopy = Check("Copiar", "Quem receber poderá copiá-lo"); _nextTransfer = Check("Transferir", "Quem receber poderá dá-lo ou vendê-lo");
        var next = new HBoxContainer(); next.AddThemeConstantOverride("separation", 14); foreach (var box in new[] { _nextModify, _nextCopy, _nextTransfer }) { box.Toggled += _ => ApplyNext(); next.AddChild(box); }
        AddChild(next);
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill }; _note.AddThemeFontSizeOverride("font_size", 16); _note.AddThemeColorOverride("font_color", UiStyle.TextMuted); AddChild(_note);
        Refresh();
    }

    // ---- building blocks -----------------------------------------------------------------------------------------------

    private static Label Value() { var label = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis }; label.AddThemeFontOverride("font", UiStyle.Bold); label.AddThemeFontSizeOverride("font_size", 18); label.AddThemeColorOverride("font_color", UiStyle.AccentText); return label; }

    private static HBoxContainer Row(string caption, Control control)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
        var label = new Label { Text = caption, CustomMinimumSize = new Vector2(150, 0), VerticalAlignment = VerticalAlignment.Center }; label.AddThemeFontSizeOverride("font_size", 16); label.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        row.AddChild(label); control.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(control); return row;
    }

    private static Label Heading(string text) { var label = new Label { Text = text }; label.AddThemeFontOverride("font", UiStyle.Bold); label.AddThemeFontSizeOverride("font_size", 18); label.AddThemeColorOverride("font_color", UiStyle.AccentText); return label; }

    private static CheckBox Check(string text, string tip) { var box = new CheckBox { Text = " " + text, TooltipText = tip, FocusMode = FocusModeEnum.None }; box.AddThemeFontSizeOverride("font_size", 18); return box; }

    // ---- showing the selection -------------------------------------------------------------------------------------------

    /// <summary>Fills the tab from the selected objects (called when the selection or an object changes).</summary>
    public void Refresh()
    {
        if (_props is null || _name is null) return;
        _filling = true;
        var model = _props.Read();
        if (model is null) { _name.Text = _description.Text = string.Empty; _creator.Text = _owner.Text = _lastOwner.Text = "—"; _note.Text = "Selecione um objeto para ver e editar as informações dele."; SetEnabled(false, false); _filling = false; return; }
        _mine = model.Mine;
        _name.Text = model.Name ?? string.Empty; _name.PlaceholderText = model.Name is null ? "(vários nomes)" : "Nome do objeto"; _description.Text = model.Description ?? string.Empty;
        _creator.Text = _names.NameOf(model.Creator, _ => Refresh()); _owner.Text = _names.NameOf(model.Owner, _ => Refresh()); _lastOwner.Text = model.LastOwner == Guid.Empty ? "—" : _names.NameOf(model.LastOwner, _ => Refresh());
        _groupIds.Clear(); _group.Clear(); _group.AddItem("(nenhum)"); _groupIds.Add(null);
        foreach (var (id, name) in _groups()) { _group.AddItem(name); _groupIds.Add(id); }
        _group.Select(Math.Max(0, _groupIds.IndexOf(model.Group)));
        _click.Select(Math.Max(0, Array.FindIndex(BuildObjectProperties.ClickActions, a => a.Id == model.ClickAction)));
        var onSale = model.SaleType is { } t && t != "none"; _forSale.SetPressedNoSignal(onSale); _saleRows.Visible = onSale || _forSale.ButtonPressed;
        if (model.SaleType is { } type && type != "none") _mode.Select(Math.Max(0, Array.FindIndex(BuildObjectProperties.SaleModes, m => m.Id == type)));
        if (model.Price is { } price) _price.SetValueNoSignal(price);
        _anyoneMove.SetPressedNoSignal(model.AnyoneMove == true); _anyoneCopy.SetPressedNoSignal(model.AnyoneCopy == true);
        _nextModify.SetPressedNoSignal((model.NextRights & ItemRights.Modify) != 0); _nextCopy.SetPressedNoSignal((model.NextRights & ItemRights.Copy) != 0); _nextTransfer.SetPressedNoSignal((model.NextRights & ItemRights.Transfer) != 0);
        SetEnabled(true, model.Mine);
        // a right the owner does not hold cannot be given to the next owner
        _nextModify.Disabled = !model.Mine || (model.OwnerRights & ItemRights.Modify) == 0; _nextCopy.Disabled = !model.Mine || (model.OwnerRights & ItemRights.Copy) == 0; _nextTransfer.Disabled = !model.Mine || (model.OwnerRights & ItemRights.Transfer) == 0;
        _note.Text = !model.Mine ? "Este objeto não é seu: você só pode olhar as informações." : model.Count > 1 ? $"{model.Count} objetos: mostra o que todos têm em comum; só o que você mexer é enviado." : "Direitos que você não tem não podem ser dados ao próximo dono.";
        _filling = false;
    }

    private void SetEnabled(bool any, bool mine)
    {
        _name.Editable = mine && !_name.PlaceholderText.StartsWith('('); _description.Editable = mine;
        foreach (var option in new[] { _group, _click, _mode }) option.Disabled = !mine;
        foreach (var box in new[] { _forSale, _anyoneMove, _anyoneCopy, _nextModify, _nextCopy, _nextTransfer }) box.Disabled = !mine;
        _price.Editable = mine; _apply.Disabled = !mine;
        if (!any) _saleRows.Visible = false;
    }

    // ---- what the person changes ---------------------------------------------------------------------------------------

    private void CommitIdentity()
    {
        if (_filling || !_mine || _props is null) return;
        if (_name.Text.Trim().Length == 0) { Refresh(); Said?.Invoke("O nome não pode ficar vazio."); return; }
        if (_props.SaveIdentity(_name.Text, _description.Text)) Said?.Invoke("Nome e descrição enviados ao servidor.");
    }

    private void OnGroupChosen(int index) { if (_filling || !_mine) return; if (_props.SetGroup(_groupIds.ElementAtOrDefault(index))) Said?.Invoke(index == 0 ? "Grupo removido." : "Grupo enviado ao servidor."); }

    private void OnClickChosen(int index)
    {
        if (_filling || !_mine) return;
        var action = BuildObjectProperties.ClickActions[Math.Clamp(index, 0, BuildObjectProperties.ClickActions.Length - 1)].Id;
        if (action == "buy" && !_forSale.ButtonPressed) { Said?.Invoke("Para usar “Comprar objeto”, coloque o objeto à venda primeiro."); Refresh(); return; }
        if (_props.SetClickAction(action)) Said?.Invoke($"Ao clicar: {BuildObjectProperties.ClickActions[index].Label}.");
    }

    private void OnForSaleToggled(bool on)
    {
        if (_filling || !_mine) return;
        _saleRows.Visible = on;
        if (!on && _props.TakeOffSale()) Said?.Invoke("O objeto saiu da venda.");
        else if (on) Said?.Invoke("Escolha a modalidade e o preço e aperte APLICAR.");
    }

    private void ApplySale()
    {
        if (!_mine) return;
        var mode = BuildObjectProperties.SaleModes[Math.Max(0, _mode.Selected)].Id; var price = (long)_price.Value;
        if (BuildObjectProperties.SaleProblem(mode, price) is { } problem) { Said?.Invoke(problem); return; }
        if (_props.PutOnSale(mode, price)) Said?.Invoke($"À venda por {price.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} moedas ({BuildObjectProperties.SaleModes.First(m => m.Id == mode).Label.ToLowerInvariant()}).");
    }

    private void ApplyAnyone() { if (_filling || !_mine) return; if (_props.SetAnyone(_anyoneMove.ButtonPressed, _anyoneCopy.ButtonPressed)) Said?.Invoke("Permissões de qualquer pessoa enviadas ao servidor."); }
    private void ApplyNext() { if (_filling || !_mine) return; if (_props.SetNextOwner(_nextModify.ButtonPressed, _nextCopy.ButtonPressed, _nextTransfer.ButtonPressed)) Said?.Invoke("Permissões do próximo dono enviadas ao servidor."); }
}
