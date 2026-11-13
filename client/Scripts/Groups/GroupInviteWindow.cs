using Godot;
using LifeVerse.Client.Scripts.IslandManagement;
using LifeVerse.Client.Scripts.LandManagement;

namespace LifeVerse.Client.Scripts.Groups;

/// <summary>
/// "Convidar para grupo", from the menu of an avatar: pick one of my groups, add an optional message and send. The server decides whether the cargo may invite (and says so in words when it may not).
/// </summary>
public sealed partial class GroupInviteWindow : Node
{
    private ConfirmationDialog? _dialog;
    private Func<string, string, string?, Task<(long, string)>>? _transport;
    private GroupProfileApi? _api;
    private GroupsHubApi? _hub;

    public Action<string>? Said { get; set; }
    public ConfirmationDialog? DialogForTesting => _dialog;
    public void SetTransportForTesting(Func<string, string, string?, Task<(long, string)>> transport) { _transport = transport; _api = new GroupProfileApi(new IslandManagementApi(transport)); _hub = new GroupsHubApi(new IslandManagementApi(transport)); }
    private GroupProfileApi Api => _api ??= new GroupProfileApi(new IslandManagementApi(_transport ?? ScriptApi.HttpTransport(this)));
    private GroupsHubApi Hub => _hub ??= new GroupsHubApi(new IslandManagementApi(_transport ?? ScriptApi.HttpTransport(this)));

    public async Task OpenAsync(string username)
    {
        if (_dialog is not null && IsInstanceValid(_dialog)) _dialog.QueueFree();
        var reply = await Hub.Mine(); if (!reply.Ok) { Said?.Invoke(reply.Status == 0 ? "Sem conexão com o servidor." : GroupProfileWindow.GroupWords(reply)); return; }
        var groups = GroupsHubParse.Groups(reply.Body); if (groups.Count == 0) { Said?.Invoke("Você ainda não faz parte de nenhum grupo para convidar alguém."); return; }
        var content = IslandUi.Column(8);
        content.AddChild(new Label { Text = $"Convidar {username} para:" });
        var pick = IslandUi.Dropdown(groups.Select(g => (g.Tag.Length > 0 ? $"{g.Name}  [{g.Tag}]" : g.Name, g.Id.ToString()))); pick.Name = "group"; content.AddChild(pick);
        var note = new Label { Text = "Mensagem (opcional)" }; note.AddThemeFontSizeOverride("font_size", 12); note.AddThemeColorOverride("font_color", UiStyle.TextMuted); content.AddChild(note);
        var message = IslandUi.Input("Ex.: Venha construir com a gente!", 200); message.Name = "message"; content.AddChild(message);
        _dialog = new ConfirmationDialog { Title = "Convidar para grupo", OkButtonText = "Enviar convite", CancelButtonText = "Cancelar", Exclusive = true, Theme = ViewerTheme.Create() };
        _dialog.AddChild(IslandUi.Padded(content, 6, 6, 6, 6)); AddChild(_dialog); var dialog = _dialog;
        dialog.Confirmed += async () =>
        {
            var id = Guid.Parse(IslandUi.IdOf(pick)); var sent = await Api.Invite(id, username, message.Text.Trim(), null);
            Said?.Invoke(sent.Ok ? $"Convite enviado para {username}." : GroupProfileWindow.GroupWords(sent)); dialog.QueueFree();
        };
        dialog.Canceled += () => dialog.QueueFree(); IslandUi.Fit(dialog, 420);
    }
}
