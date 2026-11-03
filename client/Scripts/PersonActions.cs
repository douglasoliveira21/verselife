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

    /// Finds the account behind a name shown over an avatar.
    public void Find(string name, Action<Guid> found)
    {
        if (_known.TryGetValue(name, out var cached)) { found(cached); return; }
        Call(Godot.HttpClient.Method.Get, "/api/v1/social/people?q=" + Uri.EscapeDataString(name), null, (status, body) =>
        {
            if (status is < 200 or >= 300) { Say(status == 0 ? "Sem conexão com o servidor." : $"Não foi possível procurar “{name}” (HTTP {status})."); return; }
            var matches = new List<Guid>();
            try
            {
                using var document = JsonDocument.Parse(body);
                foreach (var person in document.RootElement.EnumerateArray())
                {
                    var display = person.TryGetProperty("displayName", out var d) ? d.GetString() : null; var user = person.TryGetProperty("username", out var u) ? u.GetString() : null;
                    if ((string.Equals(display, name, StringComparison.OrdinalIgnoreCase) || string.Equals(user, name, StringComparison.OrdinalIgnoreCase)) && Guid.TryParse(person.GetProperty("id").GetString(), out var id)) matches.Add(id);
                }
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { Say("Resposta inesperada do servidor."); return; }
            if (matches.Count == 0) { Say($"Não encontrei “{name}” entre as pessoas cadastradas."); return; }
            if (matches.Count > 1) { Say($"Há mais de uma pessoa chamada “{name}”: use a janela Pessoas para escolher."); return; }
            _known[name] = matches[0]; found(matches[0]);
        });
    }

    // ---- the actions -----------------------------------------------------------------------------------------------

    public void AddFriend(string name) => Find(name, id => Call(Godot.HttpClient.Method.Post, "/api/v1/social/friends/requests", JsonSerializer.Serialize(new { userId = id }), (status, _) =>
        Say(status is >= 200 and < 300 ? $"Pedido de amizade enviado para {name}." : status == 409 ? $"Você e {name} já são amigos ou já há um pedido." : $"Não foi possível enviar o pedido (HTTP {status}).")));

    public void SendMessage(string name, string text) => Find(name, id => Call(Godot.HttpClient.Method.Post, "/api/v1/social/messages",
        JsonSerializer.Serialize(new { content = text, recipientUserId = id, groupId = (Guid?)null, islandId = (Guid?)null }), (status, _) =>
        Say(status is >= 200 and < 300 ? $"Mensagem enviada para {name}." : status == 429 ? "Mensagens demais seguidas; espere um instante." : $"Não foi possível enviar (HTTP {status}).")));

    public void Block(string name) => Find(name, id => Call(Godot.HttpClient.Method.Post, $"/api/v1/social/blocks/{id}", null, (status, _) =>
        Say(status is >= 200 and < 300 ? $"{name} foi bloqueado: não pode mais falar com você." : $"Não foi possível bloquear (HTTP {status}).")));

    public void Report(string name, string reason) => Find(name, id => Call(Godot.HttpClient.Method.Post, "/api/v1/social/reports",
        JsonSerializer.Serialize(new { reportedUserId = id, messageId = (Guid?)null, islandId = (Guid?)null, reason }), (status, _) =>
        Say(status is >= 200 and < 300 ? $"Denúncia sobre {name} enviada à moderação." : status == 400 ? "Conte o motivo com pelo menos 3 letras." : $"Não foi possível denunciar (HTTP {status}).")));

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

    public void AskMessage(string name) => Ask($"Mensagem para {name}", "Escreva a mensagem…", "Enviar", text => SendMessage(name, text));
    public void AskReport(string name) => Ask($"Denunciar {name}", "Conte o que aconteceu…", "Denunciar", reason => Report(name, reason));
}
