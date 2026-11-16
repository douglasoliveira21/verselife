using System.Globalization;
using System.Text.Json;
using Godot;
using LifeVerse.Shared.Search;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The Groups tab: the chosen group shows its public facts (members, how one enters, the fee, the leader, category) read from the group service, and the buttons follow what this person can do: join (free, with a fee
/// they confirm, or by asking), open the group, write in its chat (members only) or see its members. Hidden groups never get here: the search does not list them.
/// </summary>
public partial class SearchWindow
{
    /// <summary>Opens the window of a group on one of its tabs (geral, membros, chat…).</summary>
    public Action<Guid, string>? OpenGroupTab { get; set; }

    private VBoxContainer _groupBox = null!;
    private Button _join = null!, _groupChat = null!, _groupMembers = null!;
    private int _groupSeq;
    private Guid _groupId;
    private string _groupName = string.Empty, _joinPolicy = string.Empty;
    private long _joinFee;
    private bool _isMember, _requestPending, _invited, _banned, _groupLoaded;

    private void BuildGroupActions(Control parent)
    {
        _groupBox = new VBoxContainer { Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _groupBox.AddThemeConstantOverride("separation", 6); parent.AddChild(_groupBox);
        _join = Action("Participar", UiStyle.ButtonKind.Primary, OnJoin); _groupBox.AddChild(_join);
        _groupChat = Action("Enviar mensagem ao grupo", UiStyle.ButtonKind.Secondary, () => OpenGroupAt("chat")); _groupBox.AddChild(_groupChat);
        _groupMembers = Action("Ver membros", UiStyle.ButtonKind.Secondary, () => OpenGroupAt("membros")); _groupBox.AddChild(_groupMembers);
    }

    private void OpenGroupAt(string tab) { if (OpenGroupTab is not null) OpenGroupTab(_groupId, tab); else OpenGroup?.Invoke(_groupId); }

    private void LoadGroup(SearchHit hit)
    {
        var seq = ++_groupSeq; _groupId = hit.Id; _groupName = hit.Title; _groupLoaded = false; _extra.Text = "Carregando o grupo…"; UpdateGroupButtons();
        Send(Godot.HttpClient.Method.Get, $"/api/v1/groups/{hit.Id}", null, (status, body) =>
        {
            if (seq != _groupSeq) return;
            if (status is < 200 or >= 300) { _extra.Text = status == 404 ? "Este grupo não está mais disponível." : "Não foi possível carregar o grupo agora."; return; }
            try
            {
                using var document = JsonDocument.Parse(body); var view = document.RootElement; if (!view.TryGetProperty("card", out var card)) { _extra.Text = "Resposta inesperada do servidor."; return; }
                var members = Int(card, "members"); var limit = Int(card, "memberLimit"); _joinPolicy = Str(card, "joinPolicy"); _joinFee = Long(card, "joinFee");
                _isMember = Flag(card, "isMember"); _requestPending = Flag(card, "requestPending"); _invited = Flag(card, "invited"); _banned = Flag(card, "banned"); _groupLoaded = true;
                if (Str(card, "description") is { Length: > 0 } description) _pDescription.Text = description;
                var lines = new List<string> { $"{members} {(members == 1 ? "membro" : "membros")}" + (limit > 0 ? $" (limite {limit})" : "") + "." };
                lines.Add("Entrada: " + PolicyWords(_joinPolicy) + (_joinFee > 0 ? $" · taxa de L$ {_joinFee:N0}" : "") + ".");
                if (Str(view, "leaderName") is { Length: > 0 } leader && leader != "?") lines.Add("Líder: " + leader + ".");
                var facts = new[] { Str(card, "category"), Str(card, "language"), Str(card, "contentRating") }.Where(t => t.Length > 0); lines.Add(string.Join(" · ", facts));
                if (DateTimeOffset.TryParse(Str(view, "createdAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var created)) lines.Add("Criado em " + created.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR")) + ".");
                if (_isMember) lines.Add("Você faz parte deste grupo."); else if (_banned) lines.Add("Você não pode entrar neste grupo."); else if (_invited) lines.Add("Você recebeu um convite para este grupo.");
                _extra.Text = string.Join("\n", lines.Where(l => l.Length > 0));
            }
            catch (JsonException) { _extra.Text = "Resposta inesperada do servidor."; }
            UpdateGroupButtons();
        });
    }

    private static int Int(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : 0;
    private static long Long(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.TryGetInt64(out var n) ? n : 0;
    private static string PolicyWords(string policy) => policy switch { "open" => "livre", "request" => "mediante aprovação", "invite" => "somente por convite", _ => "consulte o grupo" };

    /// <summary>The join button says what it will do for this person, and is off when nothing can be done.</summary>
    private void UpdateGroupButtons()
    {
        (_join.Text, _join.Disabled) = !_groupLoaded ? ("Participar", true)
            : _isMember ? ("Você já é membro", true)
            : _banned ? ("Entrada indisponível", true)
            : _requestPending ? ("Pedido enviado", true)
            : _invited ? ("Abrir convite", false)
            : _joinPolicy == "invite" ? ("Somente por convite", true)
            : _joinPolicy == "request" ? ("Pedir para entrar", false)
            : _joinFee > 0 ? ($"Entrar (L$ {_joinFee:N0})", false) : ("Participar", false);
        _groupChat.Disabled = !_isMember; _groupChat.TooltipText = _isMember ? "Abre o chat do grupo" : "Só os membros escrevem no chat do grupo";
        _groupMembers.Disabled = !_groupLoaded;
    }

    private void OnJoin()
    {
        if (!_groupLoaded) return;
        if (_invited) { OpenGroupAt("geral"); return; }
        var name = _groupName;
        if (_joinPolicy == "request") { AskYes("Pedir para entrar", $"Enviar um pedido de entrada para “{name}”? Os responsáveis decidem.", "Enviar pedido", () => Join(null)); return; }
        if (_joinFee > 0) { var fee = _joinFee; AskYes($"Entrar em {name}", $"A entrada custa L$ {fee:N0}, cobrados da sua carteira agora. Deseja pagar e entrar?", "Pagar e entrar", () => Join(fee)); return; }
        Join(null);
    }

    /// <summary>When there is no actions node (tests without one) a confirmation is answered at once so the flow can be exercised.</summary>
    private void AskYes(string title, string message, string button, Action confirmed) { if (Actions is { } people) people.Confirm(title, message, button, confirmed); else new ConfirmShim(this).Confirm(title, message, button, confirmed); }

    private sealed class ConfirmShim(SearchWindow window)
    {
        public void Confirm(string title, string message, string button, Action confirmed) { window._lastConfirm = (title, message); confirmed(); }
    }

    private (string Title, string Message) _lastConfirm;
    public string LastConfirmForTesting => _lastConfirm.Title + " | " + _lastConfirm.Message;

    private void Join(long? confirmedFee)
    {
        var id = _groupId; var name = _groupName; var seq = _groupSeq; _join.Disabled = true; _note.Text = "Entrando…";
        Send(Godot.HttpClient.Method.Post, $"/api/v1/groups/{id}/join", JsonSerializer.Serialize(new { message = (string?)null, confirmFee = confirmedFee }), (status, body) =>
        {
            if (status is >= 200 and < 300)
            {
                var requested = body.Contains("\"requested\":true"); _note.Text = requested ? $"Pedido enviado para “{name}”. Você será avisado." : $"Você entrou em “{name}”.";
                if (Selected is { Kind: SearchKinds.Group } current && current.Id == id) LoadGroup(current); return;
            }
            var code = ErrorCode(body);
            if (code == "fee_required" && TryFee(body) is { } fee) { _joinFee = fee; UpdateGroupButtons(); _note.Text = $"A taxa de entrada agora é L$ {fee:N0}. Confira e tente de novo."; return; }
            _note.Text = JoinWords(status, code); if (Selected is { Kind: SearchKinds.Group } now && now.Id == id) LoadGroup(now);
        });
    }

    private static string ErrorCode(string body) { try { using var d = JsonDocument.Parse(body); return d.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : string.Empty; } catch (JsonException) { return string.Empty; } }

    private static long? TryFee(string body) { try { using var d = JsonDocument.Parse(body); return d.RootElement.TryGetProperty("detail", out var detail) && detail.TryGetProperty("fee", out var fee) && fee.TryGetInt64(out var n) ? n : null; } catch (JsonException) { return null; } }

    private static string JoinWords(int status, string code) => code switch
    {
        "already_member" => "Você já faz parte deste grupo.", "banned" => "Você não pode entrar neste grupo.", "group_full" or "full" => "O grupo está cheio.", "invite_only" => "Este grupo só entra por convite.",
        "too_many_memberships" => "Você já participa do máximo de grupos.", "too_many_requests" => "Você já tem pedidos demais esperando resposta.", "insufficient_balance" => "Saldo insuficiente para a taxa de entrada.", "wallet_blocked" => "Sua carteira está bloqueada.",
        "not_found" => "Este grupo não está mais disponível.", "rate_limited" => "Calma: tente de novo daqui a pouco.", _ => status == 0 ? "Sem conexão com o servidor." : $"Não foi possível entrar agora (HTTP {status})."
    };

    // ---- tests --------------------------------------------------------------------------------------------------------------------

    public string JoinTextForTesting => _join.Text;
    public bool JoinEnabledForTesting => !_join.Disabled;
    public bool GroupBoxVisibleForTesting => _groupBox.Visible;
    public bool GroupChatEnabledForTesting => !_groupChat.Disabled;
    public void PressJoinForTesting() => OnJoin();
    public void PressGroupChatForTesting() => _groupChat.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressGroupMembersForTesting() => _groupMembers.EmitSignal(BaseButton.SignalName.Pressed);
}
