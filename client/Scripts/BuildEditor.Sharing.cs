using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Shared.Items;

namespace LifeVerse.Client.Scripts;

/// The sharing part of the Geral tab: what the owner and the next owner may do (Modificar, Copiar, Transferir), the group the object is shared with,
/// what anyone may do, and whether it is for sale. Only the fields the person touched are sent, so a selection with mixed values keeps the ones left alone.
public partial class BuildEditor
{
    private readonly Dictionary<int, CheckBox> _ownerRightBoxes = [], _nextRightBoxes = [];
    private CheckBox _groupShareBox = null!, _anyoneMoveBox = null!, _anyoneCopyBox = null!;
    private OptionButton _groupPick = null!, _salePick = null!;
    private LineEdit _salePrice = null!;
    private Button _sharingApply = null!;
    private Label _sharingNote = null!;
    private readonly HashSet<string> _touched = [];
    private readonly List<Guid?> _groupIds = [];
    private static readonly (int Bit, string Label)[] RightChoices = [(ItemRights.Modify, "Modificar"), (ItemRights.Copy, "Copiar"), (ItemRights.Transfer, "Transferir")];
    private static readonly string[] SaleTypes = ["none", "original", "copy", "contents"];

    private CheckBox Flag(string text, string tip, string key)
    {
        var box = new CheckBox { Text = " " + text, TooltipText = tip }; box.AddThemeFontSizeOverride("font_size", 19); box.Toggled += _ => { if (!_filling) _touched.Add(key); };
        return box;
    }

    private Control RightsRow(Dictionary<int, CheckBox> boxes, string who)
    {
        var row = Row(14);
        foreach (var (bit, label) in RightChoices)
        {
            var box = new CheckBox { Text = " " + label }; box.AddThemeFontSizeOverride("font_size", 19); box.Toggled += _ => { if (!_filling) _touched.Add(who + ":" + bit); };
            boxes[bit] = box; row.AddChild(box);
        }
        return row;
    }

    private void UpdateSaleEnabled() { if (_salePrice is not null) _salePrice.Editable = _salePick.Selected > 0 && !_readOnly; }

    /// Fills the sharing controls from the selection (what every selected object allows is shown) and disables what the person may not change.
    private void FillSharing()
    {
        if (_sharingNote is null) return;
        _touched.Clear();
        var mine = _selection.Count > 0 && _selection.All(v => v.State.OwnerUserId == _session.UserId);
        var owner = _selection.Count == 0 ? 0 : ItemRights.Intersect(_selection.Select(v => v.State.OwnerRights));
        var next = _selection.Count == 0 ? 0 : ItemRights.Intersect(_selection.Select(v => v.State.NextOwnerRights));
        foreach (var (bit, _) in RightChoices)
        {
            _ownerRightBoxes[bit].SetPressedNoSignal((owner & bit) != 0); _nextRightBoxes[bit].SetPressedNoSignal((next & bit) != 0);
            _ownerRightBoxes[bit].Disabled = !mine || (owner & bit) == 0; // rights the owner does not have cannot be given, and rights cannot be raised
            _nextRightBoxes[bit].Disabled = !mine || (owner & bit) == 0;
        }
        var perms = _selection.Select(v => ParseObject(v.State.PermissionsJson)).ToList();
        bool All(string key) => perms.Count > 0 && perms.All(p => p[key]?.GetValue<bool>() == true);
        _groupShareBox.SetPressedNoSignal(All("groupShare")); _anyoneMoveBox.SetPressedNoSignal(All("anyoneMove")); _anyoneCopyBox.SetPressedNoSignal(All("anyoneCopy"));
        var types = perms.Select(p => Math.Max(Array.IndexOf(SaleTypes, p["saleType"]?.GetValue<string>() ?? "none"), 0)).Distinct().ToList();
        _salePick.Select(types.Count == 1 ? types[0] : 0);
        _salePrice.Text = perms.Count == 1 ? (perms[0]["salePrice"]?.GetValue<int>() ?? 0).ToString() : "";
        RefreshGroupChoices();
        _groupPick.Select(0);
        foreach (var control in new Control[] { _groupPick, _groupShareBox, _anyoneMoveBox, _anyoneCopyBox, _salePick, _sharingApply }) SetControlEnabled(control, mine && !_readOnly);
        UpdateSaleEnabled();
        _sharingNote.Text = _selection.Count == 0 ? "Selecione um objeto seu para mudar permissões."
            : !mine ? "Este objeto não é seu: você só pode olhar as permissões."
            : _selection.Count > 1 ? $"{_selection.Count} objetos: mostra o que todos permitem; só o que você mexer é enviado."
            : "Direitos que você não tem não podem ser dados; só dá para diminuir.";
    }

    private void RefreshGroupChoices()
    {
        var land = GetParent()?.GetNodeOrNull<LandManager>("LandManager");
        _groupIds.Clear(); _groupPick.Clear(); _groupPick.AddItem("(nenhum)"); _groupIds.Add(null);
        if (land is null) return;
        foreach (var (id, name) in land.MyGroups) { _groupPick.AddItem(name); _groupIds.Add(id); }
    }

    /// Sends only the sharing fields the person changed. The server keeps the final say (owner only, no raising of rights, group membership).
    private void ApplySharing()
    {
        if (_selection.Count == 0) { _status.Text = "Selecione os objetos que quer configurar."; return; }
        if (_touched.Count == 0) { _status.Text = "Nada mudou nas permissões."; return; }
        var ownerBits = RightChoices.Where(r => _ownerRightBoxes[r.Bit].ButtonPressed).Sum(r => r.Bit);
        var nextBits = RightChoices.Where(r => _nextRightBoxes[r.Bit].ButtonPressed).Sum(r => r.Bit) & ownerBits;
        var props = new JsonObject();
        var ownerTouched = _touched.Any(t => t.StartsWith("owner:"));
        if (ownerTouched) props["ownerRights"] = ownerBits | (_selection.Select(v => v.State.OwnerRights).Aggregate(-1, (a, b) => a & b) & ItemRights.Resell);
        if (ownerTouched || _touched.Any(t => t.StartsWith("next:"))) props["nextOwnerRights"] = nextBits;
        if (_touched.Contains("groupShare")) props["groupShare"] = _groupShareBox.ButtonPressed;
        if (_touched.Contains("anyoneMove")) props["anyoneMove"] = _anyoneMoveBox.ButtonPressed;
        if (_touched.Contains("anyoneCopy")) props["anyoneCopy"] = _anyoneCopyBox.ButtonPressed;
        if (_touched.Contains("group")) props["groupId"] = _groupPick.Selected > 0 ? _groupIds[_groupPick.Selected]?.ToString() : null;
        if (_touched.Contains("saleType")) props["saleType"] = SaleTypes[Mathf.Clamp(_salePick.Selected, 0, SaleTypes.Length - 1)];
        if ((_touched.Contains("salePrice") || _touched.Contains("saleType")) && _salePick.Selected > 0)
        {
            if (!int.TryParse(_salePrice.Text.Trim(), out var price) || price < 0 || price > 10_000_000) { _status.Text = "Preço inválido: use um número inteiro de 0 a 10.000.000."; return; }
            props["salePrice"] = price;
        }
        var ids = _selection.Select(v => v.State.Id).ToArray();
        var command = JsonSerializer.Serialize(new { operation = "properties", objectIds = ids, expectedRevision = 0, properties = JsonNode.Parse(props.ToJsonString()) });
        SharingCommandForTesting = command;
        Send(command);
        _status.Text = "Permissões enviadas ao servidor.";
    }

    public string SharingCommandForTesting { get; private set; } = string.Empty;
    public void ToggleSharingForTesting(string key, bool on)
    {
        switch (key)
        {
            case "groupShare": _groupShareBox.ButtonPressed = on; break;
            case "anyoneMove": _anyoneMoveBox.ButtonPressed = on; break;
            case "anyoneCopy": _anyoneCopyBox.ButtonPressed = on; break;
            case "ownerCopy": _ownerRightBoxes[ItemRights.Copy].ButtonPressed = on; break;
            case "nextTransfer": _nextRightBoxes[ItemRights.Transfer].ButtonPressed = on; break;
        }
    }
    public void SetSaleForTesting(int type, string price) { _salePick.Select(type); _touched.Add("saleType"); _salePrice.Text = price; UpdateSaleEnabled(); }
    public void ApplySharingForTesting() => ApplySharing();
    public bool SharingEnabledForTesting => !_groupShareBox.Disabled;
    public bool OwnerBitEnabledForTesting(int bit) => !_ownerRightBoxes[bit].Disabled;
}

public partial class BuildEditor
{
    /// The permissions block of the Geral tab as in the reference: what you can do and what the next owner will be able to do, the group, what anyone may do, and the sale.
    private void BuildSharingSection(VBoxContainer box)
    {
        box.AddChild(Heading("Permissões"));
        _sharingNote = Muted("Selecione um objeto seu para mudar permissões.", 16); _sharingNote.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_sharingNote);
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; grid.AddThemeConstantOverride("h_separation", 24); grid.AddThemeConstantOverride("v_separation", 4);
        grid.AddChild(Txt("Você pode:", 18, true, UiStyle.TextMuted)); grid.AddChild(Txt("Próximo proprietário:", 18, true, UiStyle.TextMuted));
        foreach (var (bit, label) in RightChoices)
        {
            foreach (var (boxes, who) in new[] { (_ownerRightBoxes, "owner"), (_nextRightBoxes, "next") })
            {
                var check = Check(label); var captured = bit; check.Toggled += _ => { if (!_filling) _touched.Add(who + ":" + captured); }; boxes[bit] = check; grid.AddChild(check);
            }
        }
        box.AddChild(grid);
        _groupPick = Pick("(nenhum)"); _groupPick.ItemSelected += _ => _touched.Add("group"); box.AddChild(FormRow("Grupo:", _groupPick, 150));
        _groupShareBox = Flag("Compartilhar com o grupo", "Membros do grupo podem editar o objeto", "groupShare"); box.AddChild(_groupShareBox);
        _anyoneMoveBox = Flag("Qualquer um pode mover", "Qualquer pessoa pode mover e girar (sem alterar a forma)", "anyoneMove"); box.AddChild(_anyoneMoveBox);
        _anyoneCopyBox = Flag("Qualquer um pode copiar", "Qualquer pessoa pode copiar o objeto, se ele permitir cópia", "anyoneCopy"); box.AddChild(_anyoneCopyBox);
        _salePick = Pick("Não está à venda", "Original", "Cópia", "Conteúdo"); _salePick.ItemSelected += _ => { _touched.Add("saleType"); UpdateSaleEnabled(); };
        _salePrice = Field("0", 8); _salePrice.CustomMinimumSize = new Vector2(110, 40); _salePrice.TextChanged += _ => _touched.Add("salePrice");
        var sale = Row(8); sale.AddChild(Txt("À venda:", 19)); _salePick.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; sale.AddChild(_salePick); sale.AddChild(Txt("moedas", 18, false, UiStyle.TextMuted)); sale.AddChild(_salePrice); box.AddChild(sale);
        _sharingApply = IconButton("check", "Aplicar permissões", "Envia as permissões, o grupo e a venda ao servidor", ApplySharing, UiStyle.ButtonKind.Primary, 42); box.AddChild(_sharingApply);
        UpdateSaleEnabled();
    }
}
