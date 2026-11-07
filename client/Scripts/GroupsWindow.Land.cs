using System.Globalization;
using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts;

public sealed record GroupLandData(Guid ParcelId, Guid IslandId, string IslandName, string Name, double Area, int MaxObjects, string AccessMode, string BuildMode, bool ForSale);
public sealed record GroupLandGrantData(Guid Id, Guid GroupId, string GroupName, string Kind);

/// <summary>The land part of the groups window: the terrains the group owns, putting one of yours in it, handing one back to a member, and opening a terrain to another group.</summary>
public sealed partial class GroupsWindow
{
    private readonly List<GroupLandData> _lands = [];
    private readonly List<(Guid Id, string Label)> _offerable = [];
    private readonly List<GroupLandGrantData> _grants = [];
    private readonly List<(Guid Id, string Name)> _grantTargets = [];
    private bool _canManageLand, _canReleaseLand;

    private ItemList _landList = null!, _grantList = null!;
    private OptionButton _landOffer = null!, _landHeir = null!, _grantGroup = null!, _grantKind = null!;
    private Button _landDonate = null!, _landRelease = null!, _grantAdd = null!, _grantRemove = null!;
    private Label _landNote = null!;

    public IReadOnlyList<GroupLandData> Lands => _lands;
    public IReadOnlyList<GroupLandGrantData> Grants => _grants;

    private GroupLandData? SelectedLand() { var p = _landList.GetSelectedItems(); return p.Length == 0 || p[0] >= _lands.Count ? null : _lands[p[0]]; }

    public void LoadLands()
    {
        if (_current is not { } g) return;
        Call(Godot.HttpClient.Method.Get, $"/{g.Id}/lands", null, (status, body) =>
        {
            if (status is < 200 or >= 300) return;
            try
            {
                using var d = JsonDocument.Parse(body); var root = d.RootElement;
                _canManageLand = Flag(root, "canManage"); _canReleaseLand = Flag(root, "canRelease"); _lands.Clear(); _landList.Clear();
                if (root.TryGetProperty("lands", out var lands))
                    foreach (var l in lands.EnumerateArray())
                    {
                        var land = new GroupLandData(IdOf(l, "parcelId"), IdOf(l, "islandId"), Str(l, "islandName"), Str(l, "name"), l.TryGetProperty("area", out var a) && a.TryGetDouble(out var area) ? area : 0, Num(l, "maxObjects"), Str(l, "accessMode"), Str(l, "buildMode"), Flag(l, "forSale"));
                        _lands.Add(land); _landList.AddItem($"{land.Name} · {land.IslandName} · {land.Area.ToString("0", Pt)} m² · {AccessName(land.AccessMode)}");
                    }
                _landNote.Text = _lands.Count == 0 ? "O grupo ainda não tem terrenos." : "Terrenos do grupo: quem tem o cargo certo administra e constrói neles junto.";
            }
            catch (JsonException) { }
            _grantTargets.Clear(); _grantGroup.Clear();
            foreach (var m in _mine.Where(x => x.Id != g.Id)) { _grantTargets.Add((m.Id, m.Name)); _grantGroup.AddItem(m.Name); }
            _grants.Clear(); _grantList.Clear(); RefreshHeirs(); UpdateButtons();
        });
        if (Has(Shared.Groups.GroupPermissions.ManageLand))
            Call(Godot.HttpClient.Method.Get, $"/{g.Id}/lands/available", null, (status, body) =>
            {
                if (status is < 200 or >= 300) return;
                _offerable.Clear(); _landOffer.Clear();
                foreach (var l in ReadList(body, x => (IdOf(x, "parcelId"), $"{Str(x, "name")} · {Str(x, "islandName")}"))) { _offerable.Add(l); _landOffer.AddItem(l.Item2); }
                UpdateButtons();
            });
    }

    private static string AccessName(string mode) => mode switch { "private" => "só o grupo", "list" => "por lista", _ => "aberto" };

    private void RefreshHeirs()
    {
        if (_landHeir is null) return;
        _landHeir.Clear(); foreach (var m in _members) { _landHeir.AddItem(m.Username); _landHeir.SetItemMetadata(_landHeir.ItemCount - 1, m.UserId.ToString()); }
    }

    private void ShowLand()
    {
        _grants.Clear(); _grantList.Clear();
        if (SelectedLand() is { } land)
            Call(Godot.HttpClient.Method.Get, $"/api/v1/worlds/islands/{land.IslandId}/land/parcels/{land.ParcelId}/group-access", null, (status, body) =>
            {
                if (status is < 200 or >= 300) return;
                _grants.Clear(); _grants.AddRange(ReadList(body, x => new GroupLandGrantData(IdOf(x, "id"), IdOf(x, "groupId"), Str(x, "groupName"), Str(x, "kind"))));
                _grantList.Clear(); foreach (var gr in _grants) _grantList.AddItem($"{gr.GroupName} · {(gr.Kind == "build" ? "pode construir" : "pode entrar")}");
                UpdateButtons();
            });
        UpdateButtons();
    }

    private void UpdateLandButtons()
    {
        var land = SelectedLand(); var offer = _landOffer.ItemCount > 0 && _landOffer.Selected >= 0;
        _landDonate.Disabled = !(_canManageLand && offer); _landOffer.Disabled = !_canManageLand;
        _landRelease.Disabled = !(_canReleaseLand && land is not null && _landHeir.ItemCount > 0); _landHeir.Disabled = !_canReleaseLand;
        _grantAdd.Disabled = !(_canManageLand && land is not null && _grantGroup.ItemCount > 0); _grantGroup.Disabled = !_canManageLand; _grantKind.Disabled = !_canManageLand;
        _grantRemove.Disabled = !(_canManageLand && land is not null && _grantList.GetSelectedItems().Length > 0);
        RefreshPanel();
    }

    public void DonateLand()
    {
        if (_current is not { } g || _landOffer.ItemCount == 0 || _landOffer.Selected < 0 || _landOffer.Selected >= _offerable.Count) return;
        var parcel = _offerable[_landOffer.Selected];
        Do(Godot.HttpClient.Method.Post, $"/{g.Id}/lands", new { parcelId = parcel.Id }, $"O terreno “{parcel.Label.Split(" · ")[0]}” agora é do grupo.", LoadLands);
    }

    public void ReleaseLand()
    {
        if (_current is not { } g || SelectedLand() is not { } land || _landHeir.ItemCount == 0 || !Guid.TryParse(_landHeir.GetItemMetadata(_landHeir.Selected).AsString(), out var heir)) return;
        Do(Godot.HttpClient.Method.Post, $"/{g.Id}/lands/{land.ParcelId}/release", new { userId = heir }, $"O terreno “{land.Name}” saiu do grupo e foi para {_landHeir.GetItemText(_landHeir.Selected)}.", LoadLands);
    }

    public void GrantAccess()
    {
        if (SelectedLand() is not { } land || _grantGroup.ItemCount == 0 || _grantGroup.Selected < 0 || _grantGroup.Selected >= _grantTargets.Count) return;
        var target = _grantTargets[_grantGroup.Selected];
        Do(Godot.HttpClient.Method.Post, $"/api/v1/worlds/islands/{land.IslandId}/land/parcels/{land.ParcelId}/group-access", new { groupId = target.Id, kind = _grantKind.Selected == 1 ? "build" : "allow" }, $"O grupo {target.Name} agora tem acesso a “{land.Name}”.", ShowLand);
    }

    public void RevokeAccess()
    {
        if (SelectedLand() is not { } land || _grantList.GetSelectedItems() is not { Length: > 0 } sel || sel[0] >= _grants.Count) return;
        Do(Godot.HttpClient.Method.Delete, $"/api/v1/worlds/islands/{land.IslandId}/land/parcels/{land.ParcelId}/group-access/{_grants[sel[0]].Id}", null, "Acesso do grupo retirado.", ShowLand);
    }

    private Control BuildLands(Control stack)
    {
        var scroll = PageScroll(stack, 6, out var page);
        _landNote = Note("O grupo ainda não tem terrenos."); page.AddChild(_landNote);
        _landList = List(80); _landList.ItemSelected += _ => ShowLand(); page.AddChild(_landList);
        _landHeir = Choice(); _landRelease = Act("Devolver a um membro", UiStyle.ButtonKind.Secondary, ReleaseLand); page.AddChild(Row(_landHeir, _landRelease));
        _landOffer = Choice(); _landDonate = Act("Colocar meu terreno no grupo", UiStyle.ButtonKind.Primary, DonateLand); page.AddChild(Row(_landOffer, _landDonate));
        page.AddChild(Heading("Abrir o terreno escolhido a outro grupo"));
        _grantList = List(50); _grantList.ItemSelected += _ => UpdateButtons(); page.AddChild(_grantList);
        _grantGroup = Choice(); _grantKind = Choice("Pode entrar", "Pode construir"); _grantAdd = Act("Dar acesso", UiStyle.ButtonKind.Primary, GrantAccess); _grantRemove = Act("Retirar", UiStyle.ButtonKind.Secondary, RevokeAccess);
        page.AddChild(Row(_grantGroup, _grantKind)); page.AddChild(Row(_grantAdd, _grantRemove));
        return scroll;
    }

    // ---- hooks for tests ---------------------------------------------------------------------------------------------------------------------------------------------

    public void SelectLandForTesting(int index) { _landList.Select(index); ShowLand(); }
    public void SelectGrantForTesting(int index) { _grantList.Select(index); UpdateButtons(); }
    public void ChooseHeirForTesting(string name) { for (var i = 0; i < _landHeir.ItemCount; i++) if (_landHeir.GetItemText(i) == name) _landHeir.Select(i); }
    public void ChooseGrantGroupForTesting(string name, bool build) { for (var i = 0; i < _grantGroup.ItemCount; i++) if (_grantGroup.GetItemText(i) == name) _grantGroup.Select(i); _grantKind.Select(build ? 1 : 0); }
    public bool LandButtonEnabledForTesting(string which) => which switch { "donate" => !_landDonate.Disabled, "release" => !_landRelease.Disabled, "grant" => !_grantAdd.Disabled, "revoke" => !_grantRemove.Disabled, _ => false };
    public string LandNoteForTesting => _landNote.Text;
}
