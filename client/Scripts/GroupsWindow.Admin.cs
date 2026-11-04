using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Shared.Groups;

namespace LifeVerse.Client.Scripts;

public sealed record GroupInviteData(Guid Id, Guid GroupId, string GroupName, string From);

/// <summary>The rest of the administration window: the overview panel, the identity of the group (emblem and picture), invitations received, cancelling a request, and the paged history.</summary>
public sealed partial class GroupsWindow
{
    private static readonly string[] EmblemNames = ["Pessoas", "Estrela", "Martelo", "Sacola", "Moeda", "Mapa", "Câmera", "Conversa", "Ideia", "Asa", "Cubo", "Corrida"];
    private readonly List<GroupInviteData> _invites = [];
    private DateTimeOffset? _auditOldest; private bool _auditHasMore;
    private Guid _imageFor;

    private Label _panelLabel = null!;
    private TextureRect _groupImage = null!;
    private OptionButton _newEmblem = null!, _setEmblem = null!;
    private LineEdit _newImage = null!, _setImage = null!;
    private ItemList _inviteList = null!;
    private Button _inviteAccept = null!, _inviteDecline = null!, _cancelRequest = null!, _auditMore = null!;

    public IReadOnlyList<GroupInviteData> Invites => _invites;
    public string PanelTextForTesting => _panelLabel.Text;
    public static string EmblemKey(int index) => index >= 0 && index < GroupRules.Emblems.Length ? GroupRules.Emblems[index] : "people";
    public static string EmblemName(string key) { var i = Array.IndexOf(GroupRules.Emblems, key); return i >= 0 && i < EmblemNames.Length ? EmblemNames[i] : key; }

    // ---- the overview panel -----------------------------------------------------------------------------------------------------------------------------------

    private void RefreshPanel()
    {
        if (_panelLabel is null) return;
        if (_current is not { } g) { _panelLabel.Text = string.Empty; return; }
        var sb = new StringBuilder();
        sb.AppendLine($"{EmblemName(g.Emblem)} · {g.Name}{(g.Tag.Length > 0 ? $" [{g.Tag}]" : string.Empty)}");
        sb.AppendLine($"Seu cargo: {(_myRole.Length > 0 ? _myRole : "—")}{(Muted(DateTimeOffset.UtcNow) ? " · silenciado" : string.Empty)}");
        sb.AppendLine($"Membros: {g.Members}/{g.MemberLimit}");
        if (Has(GroupPermissions.ApproveRequests)) sb.AppendLine(_requests.Count == 0 ? "Pedidos de entrada: nenhum esperando." : $"Pedidos de entrada: {_requests.Count} esperando resposta (aba Pedidos).");
        var pinned = _announcements.Count(a => a.Pinned);
        sb.AppendLine(_announcements.Count == 0 ? "Anúncios: nenhum." : $"Anúncios: {_announcements.Count} ({pinned} fixado(s)). Último: {_announcements.OrderByDescending(a => a.At).First().Title}");
        var next = _events.OrderBy(e => e.StartsAt).FirstOrDefault();
        sb.AppendLine(next is null ? "Eventos: nenhum marcado." : $"Próximo evento: {next.Name}, {next.StartsAt.ToLocalTime().ToString("dd/MM HH:mm", Pt)} ({next.Attending}/{next.Capacity}).");
        sb.AppendLine(_lands.Count == 0 ? "Terrenos: nenhum." : $"Terrenos: {_lands.Count} ({_lands.Sum(l => l.Area).ToString("0", Pt)} m²).");
        if (_treasury is { CanView: true } t) sb.AppendLine($"Cofre: {Money(t.Balance)} moedas{(t.Frozen ? " (congelado)" : string.Empty)}.");
        else if (_treasury is { } mine) sb.AppendLine($"Você já contribuiu com {Money(mine.Mine)} moedas.");
        sb.AppendLine($"Loja: {_products.Count(p => p.Status == "active")} produto(s) à venda, {_stores.Count} loja(s) aberta(s).");
        _panelLabel.Text = sb.ToString().TrimEnd();
    }

    private Control BuildPanel(Control stack)
    {
        var page = new VBoxContainer { Visible = false }; page.SetAnchorsPreset(Control.LayoutPreset.FullRect); page.AddThemeConstantOverride("separation", 8); stack.AddChild(page);
        page.AddChild(Heading("Visão geral"));
        _panelLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 0) }; _panelLabel.AddThemeFontSizeOverride("font_size", 16); page.AddChild(_panelLabel);
        page.AddChild(Note("Cada aba do grupo mostra só o que o seu cargo deixa fazer. As regras valem no servidor, não importa o que a tela mostre."));
        return page;
    }

    // ---- identity ---------------------------------------------------------------------------------------------------------------------------------------------------

    private void AddIdentityToCreate(VBoxContainer create)
    {
        _newEmblem = Choice(EmblemNames); _newImage = Field("Endereço da imagem do grupo (https, opcional)", 512);
        create.AddChild(Row(new Label { Text = "Emblema" }, _newEmblem)); create.AddChild(_newImage);
    }

    private void AddIdentityToSettings(VBoxContainer box)
    {
        _setEmblem = Choice(EmblemNames); _setImage = Field("Endereço da imagem do grupo (https; vazio tira a imagem)", 512);
        box.AddChild(Row(new Label { Text = "Emblema" }, _setEmblem)); box.AddChild(_setImage);
    }

    private void FillIdentity(GroupCardData g) { _setEmblem.Select(Math.Max(Array.IndexOf(GroupRules.Emblems, g.Emblem), 0)); _setImage.Text = g.ImageUrl; }

    /// <summary>Shows the picture of the group when it has one; a bad address or file just leaves the picture out.</summary>
    private void LoadGroupImage(GroupCardData g)
    {
        _imageFor = g.Id; _groupImage.Texture = null; _groupImage.Visible = false;
        if (Transport is not null || !g.ImageUrl.StartsWith("https://", StringComparison.Ordinal)) return;
        var http = new HttpRequest { BodySizeLimit = 2_000_000, Timeout = 10 }; AddChild(http);
        http.RequestCompleted += (result, code, _, bytes) =>
        {
            http.QueueFree();
            if (result != (long)HttpRequest.Result.Success || code != 200 || bytes.Length < 12 || _imageFor != g.Id) return;
            var image = new Image();
            var error = bytes[0] == 0x89 && bytes[1] == 0x50 ? image.LoadPngFromBuffer(bytes) : bytes[0] == 0xFF && bytes[1] == 0xD8 ? image.LoadJpgFromBuffer(bytes) : bytes[8] == 0x57 && bytes[9] == 0x45 ? image.LoadWebpFromBuffer(bytes) : Error.FileUnrecognized;
            if (error != Error.Ok || image.GetWidth() == 0) return;
            image.Resize(96, 96, Image.Interpolation.Bilinear); _groupImage.Texture = ImageTexture.CreateFromImage(image); _groupImage.Visible = true;
        };
        if (http.Request(g.ImageUrl) != Error.Ok) http.QueueFree();
    }

    // ---- invitations and requests ----------------------------------------------------------------------------------------------------------------------------

    public void LoadInvites() => Call(Godot.HttpClient.Method.Get, "/api/v1/social/groups/invites", null, (status, body) =>
    {
        if (status is < 200 or >= 300) return;
        _invites.Clear(); _invites.AddRange(ReadList(body, i => new GroupInviteData(IdOf(i, "inviteId"), IdOf(i, "groupId"), Str(i, "groupName"), Str(i, "fromUsername"))));
        _inviteList.Clear(); foreach (var i in _invites) _inviteList.AddItem($"{i.GroupName} · de {i.From}");
        UpdateInviteButtons();
    });

    private GroupInviteData? SelectedInvite() { var p = _inviteList.GetSelectedItems(); return p.Length == 0 || p[0] >= _invites.Count ? null : _invites[p[0]]; }
    private void UpdateInviteButtons() { var any = SelectedInvite() is not null; _inviteAccept.Disabled = _inviteDecline.Disabled = !any; }

    public void AnswerInvite(bool accept)
    {
        if (SelectedInvite() is not { } invite) return;
        Do(Godot.HttpClient.Method.Post, $"/api/v1/social/groups/invites/{invite.Id}/{(accept ? "accept" : "decline")}", new { }, accept ? $"Você entrou em {invite.GroupName}." : $"Convite de {invite.GroupName} recusado.", () => { LoadInvites(); LoadMine(); });
    }

    public void CancelRequestSelected()
    {
        if (SelectedFound() is { RequestPending: true } g) Do(Godot.HttpClient.Method.Delete, $"/{g.Id}/join", null, $"Pedido para {g.Name} cancelado.", () => Search());
    }

    // ---- the history, a page at a time ---------------------------------------------------------------------------------------------------------------------------

    public void LoadAudit(bool more = false)
    {
        if (_current is not { } g) return;
        var path = $"/{g.Id}/audit?take=100" + (more && _auditOldest is { } before ? $"&before={Uri.EscapeDataString(before.UtcDateTime.ToString("o"))}" : string.Empty);
        Call(Godot.HttpClient.Method.Get, path, null, (status, body) =>
        {
            if (status is < 200 or >= 300) return;
            if (!more) _auditList.Clear();
            var entries = ReadList(body, e => e.Clone());
            foreach (var e in entries)
                _auditList.AddItem($"{(Time(e, "at") ?? DateTimeOffset.UtcNow).ToLocalTime().ToString("dd/MM HH:mm", Pt)} · {Str(e, "actor")} {ActionText(Str(e, "action"))}{(Str(e, "target").Length > 0 ? " " + Str(e, "target") : string.Empty)}");
            if (entries.Count > 0) _auditOldest = entries.Select(e => Time(e, "at")).Where(t => t is not null).Min();
            _auditHasMore = entries.Count >= 100; _auditMore.Visible = _auditHasMore;
        });
    }

    public int AuditCountForTesting => _auditList.ItemCount;
    public bool AuditMoreVisibleForTesting => _auditMore.Visible;
    public void LoadMoreAuditForTesting() => LoadAudit(true);

    // ---- hooks for tests -------------------------------------------------------------------------------------------------------------------------------------------

    public void SelectInviteForTesting(int index) { _inviteList.Select(index); UpdateInviteButtons(); }
    public bool InviteButtonEnabledForTesting(string which) => which == "accept" ? !_inviteAccept.Disabled : which == "decline" && !_inviteDecline.Disabled;
    public bool CancelRequestEnabledForTesting => !_cancelRequest.Disabled;
    public void SetCreateIdentityForTesting(int emblem, string image) { _newEmblem.Select(emblem); _newImage.Text = image; }
    public void SetSettingsIdentityForTesting(int emblem, string image) { _setEmblem.Select(emblem); _setImage.Text = image; }
    public string SettingsImageForTesting => _setImage.Text;
    public int SettingsEmblemForTesting => _setEmblem.Selected;
    public string OverviewTextForTesting => _overview.Text;
}
