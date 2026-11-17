using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// Things you can do to another player from the right-click menu: send a friend request, a private message, block, report. The game only
/// knows another avatar by its name, so the account is found first by searching that exact name (the same search the Pessoas window uses);
/// when two people share the name nothing is sent and the person is pointed to the Pessoas window. The API checks everything again.
public partial class PersonActions : Node
{
    private readonly Dictionary<string, Guid> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _relations = new(StringComparer.OrdinalIgnoreCase);

    /// The relation to a person as the last search reported it ("none", "friend", "pending_out", "pending_in"); empty until looked up.
    public string RelationOf(string name) => _relations.GetValueOrDefault(name, "");
    /// A person was just looked up: a menu open on them can show the right friendship slice.
    public event Action<string>? Known;
    /// Looks the person up in the background (so the menu can say Adicionar or Remover amizade).
    public void Prefetch(string name) { if (!_known.ContainsKey(name)) Find(name, _ => { }); }
    private Control? _ui;
    private FloatingWindow? _ask;

    /// Tells the person what happened (a toast).
    public event Action<string>? Said;

    /// Tests set this to answer requests without a network: (method, path, body) -> (status, body).
    public Func<Godot.HttpClient.Method, string, string?, (int Status, string Body)>? Transport { get; set; }

    public void Setup(Control uiRoot) => _ui = uiRoot;

    private void Say(string text) => Said?.Invoke(text);

    // ---- talking to the API -----------------------------------------------------------------------------------

    private void Call(Godot.HttpClient.Method method, string path, string? body, Action<int, string> done)
    {
        if (Transport is not null) { var (status, text) = Transport(method, path, body); done(status, text); return; }
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is null || app is null || !session.IsAuthenticated) { Say("Entre na sua conta para fazer isso."); return; }
        var http = new HttpRequest { Timeout = 15 }; AddChild(http);
        http.RequestCompleted += (result, code, _, bytes) => { http.QueueFree(); done(result == (long)HttpRequest.Result.Success ? (int)code : 0, bytes.GetStringFromUtf8()); };
        var headers = new List<string> { $"Authorization: Bearer {session.AccessToken}" };
        if (body is not null) headers.Add("Content-Type: application/json");
        if (http.Request($"{app.ApiBaseUrl}{path}", [.. headers], method, body ?? string.Empty) != Error.Ok) { http.QueueFree(); Say("Não foi possível falar com o servidor."); }
    }

    private Guid? _direct;
    /// Runs an action on exactly this account instead of looking a name up (two people can share a name; an id cannot be confused). The action must start inside the call.
    /// A purchase went through somewhere else (the search window): the balance on the top bar should be asked for again.
    public void NotifyPaymentMade() => PaymentMade?.Invoke();

    public void Using(Guid? id, Action action) { _direct = id; try { action(); } finally { _direct = null; } }

    /// Finds the account behind a name shown over an avatar.
    public void Find(string name, Action<Guid> found)
    {
        if (_direct is { } forced) { found(forced); return; } // the caller already knows exactly who (a search result): no guessing by name
        if (_known.TryGetValue(name, out var cached)) { found(cached); return; }
        Call(Godot.HttpClient.Method.Get, "/api/v1/social/people?q=" + Uri.EscapeDataString(name), null, (status, body) =>
        {
            if (status is < 200 or >= 300) { Say(status == 0 ? "Sem conexão com o servidor." : $"Não foi possível procurar “{name}” (HTTP {status})."); return; }
            var matches = new List<Guid>(); var relationOf = new Dictionary<Guid, string>();
            try
            {
                using var document = JsonDocument.Parse(body);
                foreach (var person in document.RootElement.EnumerateArray())
                {
                    var display = person.TryGetProperty("displayName", out var d) ? d.GetString() : null; var user = person.TryGetProperty("username", out var u) ? u.GetString() : null;
                    if ((string.Equals(display, name, StringComparison.OrdinalIgnoreCase) || string.Equals(user, name, StringComparison.OrdinalIgnoreCase)) && Guid.TryParse(person.GetProperty("id").GetString(), out var id)) { matches.Add(id); relationOf[id] = person.TryGetProperty("relation", out var r) ? r.GetString() ?? "" : ""; }
                }
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { Say("Resposta inesperada do servidor."); return; }
            if (matches.Count == 0) { Say($"Não encontrei “{name}” entre as pessoas cadastradas."); return; }
            if (matches.Count > 1) { Say($"Há mais de uma pessoa chamada “{name}”: use a janela Pessoas para escolher."); return; }
            _known[name] = matches[0]; _relations[name] = relationOf.GetValueOrDefault(matches[0], ""); Known?.Invoke(name); found(matches[0]);
        });
    }

    // ---- the actions -----------------------------------------------------------------------------------------------

    public void AddFriend(string name) => Find(name, id => Call(Godot.HttpClient.Method.Post, "/api/v1/social/friends/requests", JsonSerializer.Serialize(new { userId = id }), (status, _) =>
        Say(status is >= 200 and < 300 ? $"Pedido de amizade enviado para {name}." : status == 409 ? $"Você e {name} já são amigos ou já há um pedido." : $"Não foi possível enviar o pedido (HTTP {status}).")));

    public void SendMessage(string name, string text) => Find(name, id => Call(Godot.HttpClient.Method.Post, "/api/v1/social/messages",
        JsonSerializer.Serialize(new { content = text, recipientUserId = id, groupId = (Guid?)null, islandId = (Guid?)null }), (status, _) =>
        Say(status is >= 200 and < 300 ? $"Mensagem enviada para {name}." : status == 429 ? "Mensagens demais seguidas; espere um instante." : status == 403 ? "Você não pode enviar mensagens para esta pessoa." : $"Não foi possível enviar (HTTP {status}).")));

    public void Block(string name) => Find(name, id => Call(Godot.HttpClient.Method.Post, $"/api/v1/social/blocks/{id}", null, (status, _) =>
        Say(status is >= 200 and < 300 ? $"{name} foi bloqueado: não pode mais falar com você." : $"Não foi possível bloquear (HTTP {status}).")));

    public void Report(string name, string reason) => Find(name, id => Call(Godot.HttpClient.Method.Post, "/api/v1/social/reports",
        JsonSerializer.Serialize(new { reportedUserId = id, messageId = (Guid?)null, islandId = (Guid?)null, reason }), (status, _) =>
        Say(status is >= 200 and < 300 ? $"Denúncia sobre {name} enviada à moderação." : status == 400 ? "Conte o motivo com pelo menos 3 letras." : $"Não foi possível denunciar (HTTP {status}).")));


    public void RemoveFriend(string name) => Find(name, id => Call(Godot.HttpClient.Method.Delete, $"/api/v1/social/friends/{id}", null, (status, _) =>
    {
        if (status is >= 200 and < 300) { _relations[name] = "none"; Known?.Invoke(name); Say($"Amizade com {name} desfeita."); } else Say($"Não foi possível desfazer a amizade (HTTP {status}).");
    }));

    public void AcceptFriend(string name) => Find(name, id => Call(Godot.HttpClient.Method.Post, $"/api/v1/social/friends/{id}/accept", null, (status, _) =>
    {
        if (status is >= 200 and < 300) { _relations[name] = "friend"; Known?.Invoke(name); Say($"Agora você e {name} são amigos."); } else Say($"Não foi possível aceitar o pedido (HTTP {status}).");
    }));

    /// Copies the account id of a person (looked up by name) to the clipboard.
    public void CopyId(string name) => Find(name, id => { DisplayServer.ClipboardSet(id.ToString()); Say("Identificador copiado."); });

    // ---- paying ------------------------------------------------------------------------------------------------------

    /// Sends coins. The key makes a repeated send harmless: the server does the transfer once per key.
    public void Pay(string name, long coins, string idempotencyKey, Action<bool>? done = null)
    {
        if (coins <= 0) { Say("Digite um valor maior que zero."); done?.Invoke(false); return; }
        Find(name, id => Call(Godot.HttpClient.Method.Post, "/api/v1/economy/transfer", JsonSerializer.Serialize(new { recipientUserId = id, amountCoins = coins, idempotencyKey }), (status, body) =>
        {
            var ok = status is >= 200 and < 300;
            Say(ok ? (body.Contains("already_processed") ? $"Este pagamento para {name} já tinha sido feito." : $"Pagamento de VC$ {coins:N0} enviado para {name}.") : status == 409 ? "Pagamento recusado: saldo insuficiente ou carteira bloqueada." : status == 404 ? "Esta pessoa não foi encontrada." : $"Não foi possível pagar (HTTP {status}).");
            if (ok) PaymentMade?.Invoke();
            done?.Invoke(ok);
        }));
    }

    /// A payment went through: the balance on the top bar should be asked for again.
    public event Action? PaymentMade;

    /// Reads the balance (VC$) of the signed-in person for the payment window.
    public void Balance(Action<long?> done) => Call(Godot.HttpClient.Method.Get, "/api/v1/economy/wallet", null, (status, body) =>
    {
        if (status is < 200 or >= 300) { done(null); return; }
        try { using var document = JsonDocument.Parse(body); done(document.RootElement.TryGetProperty("availableCoins", out var c) ? c.GetInt64() : null); } catch (JsonException) { done(null); }
    });

    /// The payment window: balance, amount and a confirmation. One key per window, so pressing twice cannot pay twice.
    public void AskPayment(string name, Guid? id = null)
    {
        if (_ui is null) return;
        _ask?.QueueFree();
        var window = _ask = new FloatingWindow($"Pagar a {name}", icon: "coin") { Name = "AskPayment" };
        window.Place(.5f, .5f, -260, -150, 260, 150);
        var balance = new Label { Text = "Saldo: consultando…" }; window.Content.AddChild(balance);
        var field = new LineEdit { PlaceholderText = "Valor em VC$", MaxLength = 9, CustomMinimumSize = new Vector2(0, 44) }; window.Content.AddChild(field);
        var info = new Label { Text = "O pagamento é feito na hora e não pode ser desfeito por aqui.", AutowrapMode = TextServer.AutowrapMode.WordSmart }; window.Content.AddChild(info);
        var key = Guid.NewGuid().ToString("N"); long? have = null; var sent = false;
        Balance(value => { have = value; if (IsInstanceValid(balance)) balance.Text = value is null ? "Saldo indisponível agora." : $"Saldo: VC$ {value:N0}"; });
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); window.Content.AddChild(row);
        var pay = new Button { Text = "Pagar", CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        void Send()
        {
            if (sent) return;
            if (!long.TryParse(field.Text.Trim(), out var coins) || coins <= 0) { info.Text = "Digite um valor inteiro maior que zero."; return; }
            if (have is { } available && coins > available) { info.Text = $"Saldo insuficiente: você tem VC$ {available:N0}."; return; }
            sent = true; pay.Disabled = true; info.Text = "Enviando…";
            Using(id, () => Pay(name, coins, key, ok => { if (!IsInstanceValid(window)) return; if (ok) { window.QueueFree(); _ask = null; } else { sent = false; pay.Disabled = false; info.Text = "O pagamento não foi feito. Confira e tente de novo."; key = Guid.NewGuid().ToString("N"); } }));
        }
        UiStyle.StyleButton(pay, UiStyle.ButtonKind.Primary); pay.Pressed += Send; row.AddChild(pay);
        var cancel = new Button { Text = "Cancelar", CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(cancel, UiStyle.ButtonKind.Secondary); cancel.Pressed += () => { window.QueueFree(); _ask = null; }; row.AddChild(cancel);
        field.TextSubmitted += _ => Send();
        _ui.AddChild(window); window.MoveToFront(); field.GrabFocus();
    }

    // ---- moderating a parcel or the island ------------------------------------------------------------------------------

    private readonly Dictionary<Guid, bool> _admin = [];
    /// Whether this person owns or administers the island (asked of the server once per island: it answers 403 to everybody else).
    public bool IsIslandAdmin(Guid island) => _admin.GetValueOrDefault(island);
    public event Action? AdminKnown;
    public void ProbeAdmin(Guid island)
    {
        if (island == Guid.Empty || _admin.ContainsKey(island)) return;
        _admin[island] = false;
        Call(Godot.HttpClient.Method.Get, $"/api/v1/worlds/islands/{island}/management/server", null, (status, _) => { if (status is >= 200 and < 300) { _admin[island] = true; AdminKnown?.Invoke(); } });
    }

    private static string Reason(int status, string body)
    {
        var code = LandManager.ErrorCode(body);
        return status switch
        {
            403 => "Você não tem permissão para isso aqui.",
            404 => "O terreno ou a pessoa não foi encontrado.",
            0 => "Sem conexão com o servidor.",
            _ => code switch
            {
                "not_online" => "Essa pessoa não está mais na ilha.",
                "staff" => "O proprietário e os administradores não podem ser expulsos.",
                "offline" => "A ilha está desligada: não há servidor para receber o pedido.",
                "self" => "Você não pode fazer isso consigo mesmo.",
                "forbidden" => "Você não tem permissão para isso aqui.",
                "" => $"Não foi possível concluir (HTTP {status}).",
                _ => $"Não foi possível concluir ({code})."
            }
        };
    }

    /// Sends a person out of a parcel: a one-minute ban, so the island pushes them out and they cannot walk straight back in. The API checks that the actor owns or rents the parcel or administers the island.
    public void EjectFromParcel(string name, Guid island, Guid parcel) => Find(name, id => Call(Godot.HttpClient.Method.Post, $"/api/v1/worlds/islands/{island}/land/parcels/{parcel}/eject", JsonSerializer.Serialize(new { userId = id }), (status, body) =>
        Say(status is >= 200 and < 300 ? $"{name} foi expulso do terreno." : Reason(status, body))));

    /// Keeps a person out of a parcel for some minutes (null = for good). They are pushed out at once if they are inside.
    public void BanFromParcel(string name, Guid island, Guid parcel, int? minutes) => Find(name, id => Call(Godot.HttpClient.Method.Post, $"/api/v1/worlds/islands/{island}/land/parcels/{parcel}/access",
        JsonSerializer.Serialize(new { userId = id, kind = "ban", minutes, reason = "banido pelo menu" }), (status, body) =>
        Say(status is >= 200 and < 300 ? $"{name} foi banido do terreno{(minutes is { } m ? $" por {DescribeMinutes(m)}" : " para sempre")}." : Reason(status, body))));

    /// Sends a person out of the island (an administrator's tool). They can come back: this is not a ban.
    public void KickFromRegion(string name, Guid island, string reason) => Find(name, id => Call(Godot.HttpClient.Method.Post, $"/api/v1/worlds/islands/{island}/management/server/kick", JsonSerializer.Serialize(new { userId = id, reason }), (status, body) =>
        Say(status is >= 200 and < 300 ? $"{name} foi expulso da ilha." : Reason(status, body))));

    public static string DescribeMinutes(int minutes) => minutes switch { < 60 => $"{minutes} min", < 1440 => $"{minutes / 60} h", _ => $"{minutes / 1440} dia(s)" };

    // ---- teleports between two people on the same island ---------------------------------------------------------------------

    /// Who the signed-in person is (set by the viewer; tests set it too), to know whether an accepted teleport moves them.
    public Func<Guid> Me { get; set; } = () => GetNodeOrNullStatic<SessionManager>("/root/SessionManager")?.UserId ?? Guid.Empty;
    private static T? GetNodeOrNullStatic<T>(string path) where T : Node => (Engine.GetMainLoop() as SceneTree)?.Root.GetNodeOrNull<T>(path);
    /// An accepted teleport moves this player to a spot of the island (the island still decides whether the arrival is allowed).
    public event Action<float, float>? TeleportRequested;
    private readonly HashSet<Guid> _openInvites = [];

    /// "offer": the other person is taken to where you are. "request": you ask to be taken to where they are. Nobody moves until they accept.
    public void SendTeleportInvite(string name, Guid island, string kind) => Find(name, id => Call(Godot.HttpClient.Method.Post, "/api/v1/social/teleports", JsonSerializer.Serialize(new { targetUserId = id, kind, islandId = island }), (status, body) =>
        Say(status is >= 200 and < 300 ? (kind == "offer" ? $"Convite de teleporte enviado a {name}. Ele vale por 2 minutos." : $"Pedido de teleporte enviado a {name}. Ele vale por 2 minutos.")
            : status == 429 ? "Convites demais seguidos; espere um instante."
            : InviteError(LandManager.ErrorCode(body), status))));

    private static string InviteError(string code, int status) => code switch
    {
        "not_on_island" => "A pessoa e você precisam estar na mesma ilha agora.",
        "island_offline" => "A ilha está desligada: não dá para teleportar.",
        "blocked" => "Não dá para convidar quem bloqueou você ou foi bloqueado por você.",
        "already_pending" => "Já existe um convite desse tipo esperando resposta.",
        "self" => "Você não pode convidar a si mesmo.",
        "expired" => "O convite expirou.",
        "already_answered" or "closed" => "Esse convite já foi respondido.",
        "not_found" => "O convite ou a pessoa não foi encontrado.",
        _ => status == 0 ? "Sem conexão com o servidor." : $"Não foi possível concluir (HTTP {status})."
    };

    /// A live event from the API: a teleport invite to answer, or the answer to one of ours.
    public void OnLiveEvent(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            var data = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d : root;
            if (type == "teleport_invite" && Guid.TryParse(data.GetProperty("inviteId").GetString(), out var invite)) ShowInvite(invite, data.GetProperty("fromName").GetString() ?? "Alguém", data.GetProperty("kind").GetString() ?? "offer");
            else if (type == "teleport_answer") OnAnswer(data);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
    }

    private void OnAnswer(JsonElement data)
    {
        var by = data.TryGetProperty("byName", out var n) ? n.GetString() ?? "A pessoa" : "A pessoa";
        if (data.GetProperty("status").GetString() != "accepted") { Say($"{by} recusou o teleporte."); return; }
        if (data.TryGetProperty("moverUserId", out var mover) && Guid.TryParse(mover.GetString(), out var moverId) && moverId == Me() && data.TryGetProperty("x", out var x) && data.TryGetProperty("z", out var z))
        { Say($"{by} aceitou: levando você até lá."); TeleportRequested?.Invoke((float)x.GetDouble() + 1.5f, (float)z.GetDouble()); }
        else Say($"{by} aceitou o teleporte.");
    }

    /// The window that asks whether to accept. One per invite; the answer goes to the API, which reads the spot from the island and spends the invite.
    public void ShowInvite(Guid invite, string fromName, string kind)
    {
        if (_ui is null || !_openInvites.Add(invite)) return;
        var window = new FloatingWindow("Teleporte", icon: "map") { Name = $"TeleportInvite_{invite:N}" };
        window.Place(.5f, .3f, -250, -80, 250, 80);
        var text = kind == "offer" ? $"{fromName} quer levar você até onde está." : $"{fromName} pede para ir até onde você está.";
        window.Content.AddChild(new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); window.Content.AddChild(row);
        var accept = new Button { Text = "Aceitar", CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        var decline = new Button { Text = "Recusar", CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(accept, UiStyle.ButtonKind.Primary); UiStyle.StyleButton(decline, UiStyle.ButtonKind.Secondary);
        accept.Pressed += () => { accept.Disabled = decline.Disabled = true; AnswerInvite(invite, true); window.QueueFree(); _openInvites.Remove(invite); };
        decline.Pressed += () => { accept.Disabled = decline.Disabled = true; AnswerInvite(invite, false); window.QueueFree(); _openInvites.Remove(invite); };
        row.AddChild(accept); row.AddChild(decline);
        _ui.AddChild(window); window.MoveToFront();
        GetTree().CreateTimer(TeleportInviteSeconds).Timeout += () => { if (IsInstanceValid(window)) { window.QueueFree(); _openInvites.Remove(invite); } };
    }

    public const double TeleportInviteSeconds = 120;

    public void AnswerInvite(Guid invite, bool accept) => Call(Godot.HttpClient.Method.Post, $"/api/v1/social/teleports/{invite}/{(accept ? "accept" : "decline")}", "{}", (status, body) =>
    {
        if (status is < 200 or >= 300) { Say(InviteError(LandManager.ErrorCode(body), status)); return; }
        if (!accept) { Say("Teleporte recusado."); return; }
        try
        {
            using var document = JsonDocument.Parse(body); var root = document.RootElement;
            if (Guid.TryParse(root.GetProperty("moverUserId").GetString(), out var mover) && mover == Me()) { Say("Teleporte aceito: indo até lá."); TeleportRequested?.Invoke((float)root.GetProperty("x").GetDouble() + 1.5f, (float)root.GetProperty("z").GetDouble()); }
            else Say("Teleporte aceito: a outra pessoa vai até você.");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { Say("Teleporte aceito."); }
    });
    // ---- asking for the text ---------------------------------------------------------------------------------------

    /// A small window with a field: for the message to send, or the reason of a report.
    public void Ask(string title, string placeholder, string button, Action<string> submit)
    {
        if (_ui is null) return;
        _ask?.QueueFree();
        var window = _ask = new FloatingWindow(title, icon: "chat") { Name = "AskText" };
        window.Place(.5f, .5f, -260, -110, 260, 110);
        var field = new LineEdit { PlaceholderText = placeholder, MaxLength = 1000, CustomMinimumSize = new Vector2(0, 44) };
        window.Content.AddChild(field);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); window.Content.AddChild(row);
        void Send() { var text = field.Text.Trim(); if (text.Length == 0) return; window.QueueFree(); _ask = null; submit(text); }
        var send = new Button { Text = button, CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(send, UiStyle.ButtonKind.Primary); send.Pressed += Send; row.AddChild(send);
        var cancel = new Button { Text = "Cancelar", CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(cancel, UiStyle.ButtonKind.Secondary); cancel.Pressed += () => { window.QueueFree(); _ask = null; }; row.AddChild(cancel);
        field.TextSubmitted += _ => Send();
        _ui.AddChild(window); window.MoveToFront(); field.GrabFocus();
    }

    public void AskMessage(string name, Guid? id = null) => Ask($"Mensagem para {name}", "Escreva a mensagem…", "Enviar", text => Using(id, () => SendMessage(name, text)));
    public void AskReport(string name, Guid? id = null) => Ask($"Denunciar {name}", "Conte o que aconteceu…", "Denunciar", reason => Using(id, () => Report(name, reason)));

    /// A small window that asks yes or no before something that cannot be taken back easily (blocking a person).
    public void Confirm(string title, string message, string button, Action confirmed)
    {
        if (_ui is null) return;
        _ask?.QueueFree();
        var window = _ask = new FloatingWindow(title, icon: "lock") { Name = "AskConfirm" };
        window.Place(.5f, .5f, -250, -90, 250, 90);
        window.Content.AddChild(new Label { Text = message, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); window.Content.AddChild(row);
        var yes = new Button { Text = button, CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(yes, UiStyle.ButtonKind.Primary); yes.Pressed += () => { window.QueueFree(); _ask = null; confirmed(); }; row.AddChild(yes);
        var no = new Button { Text = "Cancelar", CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(no, UiStyle.ButtonKind.Secondary); no.Pressed += () => { window.QueueFree(); _ask = null; }; row.AddChild(no);
        _ui.AddChild(window); window.MoveToFront();
    }
}
