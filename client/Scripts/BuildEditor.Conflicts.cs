using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// Building with other people without losing anyone's work. Undo and redo are checked against what really happened on the server: a step the server
/// refused leaves the history (so it can't be undone later), and an undo never writes over a change somebody else made after yours. The history of a
/// whole linkset can be read too.
public partial class BuildEditor
{
    private readonly Dictionary<Guid, int> _lastMine = [], _remoteEdit = [];
    private (HistoryEntry Entry, bool Undo)? _stepped;

    /// Tells my own acknowledged changes from other people's edits: an answer with the revision I asked for plus one is mine.
    private void NoteRevision(string? operation, BuildObjectState state)
    {
        if (_pending.TryGetValue(state.Id, out var expected) && state.Revision == expected + 1) { _lastMine[state.Id] = state.Revision; return; }
        if (operation == "update") _remoteEdit[state.Id] = Math.Max(_remoteEdit.GetValueOrDefault(state.Id), state.Revision);
    }

    /// Someone else changed this object after my last change to it, so undoing or redoing my change would write over theirs.
    private bool ChangedByOthersSince(Guid id)
    {
        id = Resolve(id);
        return _remoteEdit.TryGetValue(id, out var theirs) && theirs > _lastMine.GetValueOrDefault(id);
    }

    private bool PendingIsExactly(HistoryEntry entry)
    {
        var ids = entry.Items.Select(i => Resolve(i.Id)).Distinct().ToList();
        return ids.Count > 0 && _pending.Count == ids.Count && ids.All(_pending.ContainsKey);
    }

    /// The server refused the only thing that was waiting for an answer: take its step back out of the history, so undo and redo keep describing what is really in the world.
    private void RollBackHistoryOfRefusal()
    {
        if (_stepped is { } step && PendingIsExactly(step.Entry))
        {
            var (from, to) = step.Undo ? (_redo, _undo) : (_undo, _redo);
            if (from.Remove(step.Entry)) to.Add(step.Entry); // the step never happened: it goes back where it was
            _stepped = null; return;
        }
        if (_undo.Count > 0 && _undo[^1] is { Kind: "update" or "delete" } last && PendingIsExactly(last)) _undo.RemoveAt(_undo.Count - 1);
    }

    private static string ConflictText(int skipped) => skipped == 1
        ? "1 objeto foi alterado por outra pessoa depois da sua edição e ficou como está: desfazer apagaria o trabalho dela."
        : $"{skipped} objetos foram alterados por outra pessoa depois da sua edição e ficaram como estão: desfazer apagaria o trabalho dela.";

    public bool ChangedByOthersForTesting(Guid id) => ChangedByOthersSince(id);
    public int UndoDepthForTesting => _undo.Count;
    public int RedoDepthForTesting => _redo.Count;

    // ---- history of a whole linkset -------------------------------------------------------------------------------------

    private HttpRequest? _linksetAuditHttp;

    private void ShowLinksetHistory()
    {
        if (_selection.Count == 0 || _selection[0].State.GroupId is not { } group) { _status.Text = "Selecione uma peça de um conjunto vinculado para ver o histórico dele."; return; }
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is null || app is null || !session.IsAuthenticated || _network.CurrentIslandId == Guid.Empty) { _status.Text = "Entre em uma ilha para ver o histórico."; return; }
        if (_linksetAuditHttp is null)
        {
            _linksetAuditHttp = new HttpRequest { Timeout = 10 }; AddChild(_linksetAuditHttp);
            _linksetAuditHttp.RequestCompleted += (_, code, _, body) => { if (code is >= 200 and < 300) ShowLinksetHistoryText(body.GetStringFromUtf8()); else _status.Text = code == 403 ? "Só quem pode editar todas as peças do conjunto vê o histórico dele." : $"Não foi possível abrir o histórico (HTTP {code})."; };
        }
        if (_linksetAuditHttp.GetHttpClientStatus() == Godot.HttpClient.Status.Disconnected) _linksetAuditHttp.Request($"{app.ApiBaseUrl}/api/v1/worlds/islands/{_network.CurrentIslandId}/linksets/{group}/audit", [$"Authorization: Bearer {session.AccessToken}"]);
    }

    /// Lists the linkset audit (newest first): when, what, which part, who and the revision.
    public void ShowLinksetHistoryText(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            var members = root.GetProperty("members").GetArrayLength();
            var lines = root.GetProperty("log").EnumerateArray().Select(e =>
            {
                var actor = Guid.TryParse(e.GetProperty("actorUserId").GetString(), out var id) ? (id == _session.UserId ? "você" : id.ToString()[..8]) : "?";
                var at = DateTimeOffset.TryParse(e.GetProperty("at").GetString(), out var when) ? when.ToLocalTime().ToString("dd/MM HH:mm") : "";
                return $"{at}  ·  {OperationLabel(e.GetProperty("operation").GetString() ?? "")} · {e.GetProperty("objectName").GetString()} por {actor}  ·  revisão {e.GetProperty("revision").GetInt32()}";
            }).ToList();
            _historyText.Text = $"Conjunto de {members} peça(s)\n" + (lines.Count == 0 ? "Sem registros." : string.Join("\n", lines));
            _historyDialog.PopupCentered();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { _status.Text = "Histórico inválido recebido do servidor."; }
    }
}
