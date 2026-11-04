using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts;

public sealed record LandGroupGrant(Guid Id, Guid GroupId, string GroupName, string Kind);

/// <summary>In the Acesso tab of a parcel: open the parcel to every member of one of your groups (to enter, or to build too) and take it back.</summary>
public partial class LandManager
{
    private VBoxContainer _groupRows = null!;
    private OptionButton _groupChoice = null!, _groupKind = null!;
    private readonly List<LandGroupGrant> _grants = [];
    private readonly List<(Guid Id, string Name)> _myGroups = [];

    public IReadOnlyList<LandGroupGrant> GroupGrants => _grants;
    public IReadOnlyList<(Guid Id, string Name)> MyGroups => _myGroups;

    private void BuildGroupAccessSection(VBoxContainer page)
    {
        var box = Section(page, "Abrir o terreno a um grupo");
        box.AddChild(Note("Todos os membros do grupo escolhido ganham o acesso, e o perdem ao sair dele. O banimento do terreno continua valendo."));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); box.AddChild(row);
        _groupChoice = Choice(); row.AddChild(_groupChoice); _groupKind = Choice("Pode entrar", "Pode construir"); row.AddChild(_groupKind);
        box.AddChild(Act("Dar acesso ao grupo", UiStyle.ButtonKind.Primary, () => GrantGroup()));
        _groupRows = new VBoxContainer(); _groupRows.AddThemeConstantOverride("separation", 4); box.AddChild(_groupRows);
    }

    private void GroupCall(Godot.HttpClient.Method method, string path, object? body, string done, Action? after = null) =>
        Call(method, path, body is null ? null : JsonSerializer.Serialize(body), (status, text) =>
        {
            if (status is >= 200 and < 300) { Say(done); after?.Invoke(); } else Say(GroupsWindow.Explain(status, text));
        });

    private void LoadGroupAccess()
    {
        if (Managed is not { } parcel) { _grants.Clear(); ShowGroupAccess(); return; }
        Call(Godot.HttpClient.Method.Get, $"/parcels/{parcel.Id}/group-access", null, (status, body) =>
        {
            if (status is < 200 or >= 300) return;
            _grants.Clear();
            try
            {
                using var d = JsonDocument.Parse(body);
                foreach (var g in d.RootElement.EnumerateArray()) _grants.Add(new LandGroupGrant(Guid.TryParse(g.GetProperty("id").GetString(), out var id) ? id : Guid.Empty, Guid.TryParse(g.GetProperty("groupId").GetString(), out var gid) ? gid : Guid.Empty, g.GetProperty("groupName").GetString() ?? "?", g.GetProperty("kind").GetString() ?? "allow"));
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
            ShowGroupAccess();
        });
        if (_myGroups.Count == 0)
            Call(Godot.HttpClient.Method.Get, "/api/v1/groups/mine", null, (status, body) =>
            {
                if (status is < 200 or >= 300) return;
                try
                {
                    using var d = JsonDocument.Parse(body); _myGroups.Clear(); _groupChoice.Clear();
                    foreach (var g in d.RootElement.EnumerateArray()) { var name = g.GetProperty("name").GetString() ?? "?"; _myGroups.Add((Guid.TryParse(g.GetProperty("id").GetString(), out var id) ? id : Guid.Empty, name)); _groupChoice.AddItem(name); }
                }
                catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
            });
    }

    private void ShowGroupAccess()
    {
        if (_groupRows is null) return;
        foreach (var child in _groupRows.GetChildren()) child.QueueFree();
        if (_grants.Count == 0) { _groupRows.AddChild(Note("Nenhum grupo tem acesso.")); return; }
        foreach (var grant in _grants)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            var label = new Label { Text = $"{grant.GroupName} · {(grant.Kind == "build" ? "pode construir" : "pode entrar")}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis }; label.AddThemeFontSizeOverride("font_size", 17); row.AddChild(label);
            var id = grant.Id; row.AddChild(Act("Retirar", UiStyle.ButtonKind.Secondary, () => RevokeGroup(id)));
            _groupRows.AddChild(row);
        }
    }

    public void GrantGroup(int? groupIndex = null, bool? build = null)
    {
        if (Managed is not { } parcel) return;
        var index = groupIndex ?? _groupChoice.Selected; if (index < 0 || index >= _myGroups.Count) return;
        GroupCall(Godot.HttpClient.Method.Post, $"/parcels/{parcel.Id}/group-access", new { groupId = _myGroups[index].Id, kind = (build ?? _groupKind.Selected == 1) ? "build" : "allow" }, $"O grupo {_myGroups[index].Name} agora tem acesso ao terreno.", LoadGroupAccess);
    }

    public void RevokeGroup(Guid entryId)
    {
        if (Managed is not { } parcel) return;
        GroupCall(Godot.HttpClient.Method.Delete, $"/parcels/{parcel.Id}/group-access/{entryId}", null, "Acesso do grupo retirado.", LoadGroupAccess);
    }
}
