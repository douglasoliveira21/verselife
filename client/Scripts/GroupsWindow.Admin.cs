using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
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
    private ConfirmationDialog? _imagePicker;
    private FileDialog? _imageUpload;
    private ItemList? _imageChoices;
    private LineEdit? _imageTarget;
    private readonly List<(Guid AssetId, string Name)> _imageAssets = [];
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
        _newEmblem = Choice(EmblemNames); _newImage = ImageField();
        create.AddChild(Row(new Label { Text = "Emblema" }, _newEmblem)); AddImageControls(create, _newImage);
    }

    private void AddIdentityToSettings(VBoxContainer box)
    {
        _setEmblem = Choice(EmblemNames); _setImage = ImageField();
        box.AddChild(Row(new Label { Text = "Emblema" }, _setEmblem)); AddImageControls(box, _setImage);
    }

    private static LineEdit ImageField() => new() { PlaceholderText = "Sem imagem selecionada", Editable = false, CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "A imagem do grupo vem do seu inventário." };

    private void AddImageControls(VBoxContainer box, LineEdit target)
    {
        var pick = Act("Escolher do inventário", UiStyle.ButtonKind.Secondary, () => OpenImagePicker(target));
        var upload = Act("Enviar imagem", UiStyle.ButtonKind.Secondary, () => UploadImage(target));
        var clear = Act("Remover", UiStyle.ButtonKind.Secondary, () => target.Text = string.Empty);
        box.AddChild(Row(target, pick)); box.AddChild(Row(upload, clear));
        box.AddChild(Note("PNG, JPG ou WebP. Ao enviar, a imagem entra primeiro no seu inventário e só então pode ser usada pelo grupo."));
    }

    private static string AssetReference(Guid assetId) => $"asset://{assetId}";

    private void EnsureImagePicker()
    {
        if (_imagePicker is not null) return;
        _imagePicker = new ConfirmationDialog { Title = "Escolher imagem do inventário", MinSize = new Vector2I(500, 400) };
        _imageChoices = new ItemList { CustomMinimumSize = new Vector2(460, 280) }; _imagePicker.AddChild(_imageChoices);
        _imagePicker.GetOkButton().Text = "Usar imagem";
        _imagePicker.Confirmed += () =>
        {
            var selected = _imageChoices.GetSelectedItems();
            if (_imageTarget is null || selected.Length == 0 || selected[0] >= _imageAssets.Count) return;
            _imageTarget.Text = AssetReference(_imageAssets[selected[0]].AssetId);
            Say($"Imagem “{_imageAssets[selected[0]].Name}” selecionada para o grupo.");
        };
        AddChild(_imagePicker);
    }

    private void OpenImagePicker(LineEdit target)
    {
        _imageTarget = target;
        Call(Godot.HttpClient.Method.Get, "/api/v1/inventory/items?category=textures&page=1&pageSize=100", null, (status, body) =>
        {
            if (status is < 200 or >= 300) { Say("Não foi possível abrir as imagens do inventário."); return; }
            try
            {
                using var document = JsonDocument.Parse(body);
                _imageAssets.Clear();
                foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
                {
                    var mime = Str(item, "mimeType"); var id = IdOf(item, "assetId");
                    if (id != Guid.Empty && mime is "image/png" or "image/jpeg" or "image/webp") _imageAssets.Add((id, Str(item, "name")));
                }
            }
            catch (JsonException) { Say("A lista de imagens do inventário está inválida."); return; }
            EnsureImagePicker(); _imageChoices!.Clear();
            foreach (var image in _imageAssets) _imageChoices.AddItem(image.Name);
            if (_imageAssets.Count == 0) { Say("Você ainda não tem imagens no inventário. Use “Enviar imagem”."); return; }
            _imagePicker!.PopupCentered();
        });
    }

    private void UploadImage(LineEdit target)
    {
        _imageTarget = target;
        if (_imageUpload is null)
        {
            _imageUpload = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Title = "Enviar imagem para o inventário", Filters = ["*.png,*.jpg,*.jpeg,*.webp ; Imagens"] };
            _imageUpload.FileSelected += UploadSelectedImage;
            AddChild(_imageUpload);
        }
        _imageUpload.PopupCentered();
    }

    private void UploadSelectedImage(string path)
    {
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp")) { Say("Escolha uma imagem PNG, JPG ou WebP."); return; }
        var bytes = Godot.FileAccess.GetFileAsBytes(path);
        if (bytes.Length == 0 || bytes.Length > 100 * 1024 * 1024) { Say("A imagem deve ter entre 1 byte e 100 MB."); return; }
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        if (app is null || session is null || !session.IsAuthenticated || _imageTarget is null) { Say("Entre na conta antes de enviar uma imagem."); return; }
        var boundary = "----LifeVerseGroup" + Guid.NewGuid().ToString("N"); var name = System.IO.Path.GetFileName(path);
        var head = Encoding.UTF8.GetBytes($"--{boundary}\r\nContent-Disposition: form-data; name=category\r\n\r\ntextures\r\n--{boundary}\r\nContent-Disposition: form-data; name=file; filename=\"{name}\"\r\nContent-Type: application/octet-stream\r\n\r\n");
        var tail = Encoding.UTF8.GetBytes($"\r\n--{boundary}--\r\n"); var payload = new byte[head.Length + bytes.Length + tail.Length];
        Buffer.BlockCopy(head, 0, payload, 0, head.Length); Buffer.BlockCopy(bytes, 0, payload, head.Length, bytes.Length); Buffer.BlockCopy(tail, 0, payload, head.Length + bytes.Length, tail.Length);
        var request = new HttpRequest { Timeout = 30 }; AddChild(request); Say($"Enviando {name} para o inventário…");
        request.RequestCompleted += (result, code, _, response) =>
        {
            request.QueueFree();
            if (result != (long)HttpRequest.Result.Success || code is < 200 or >= 300) { Say("Não foi possível enviar a imagem para o inventário."); return; }
            try
            {
                using var document = JsonDocument.Parse(response.GetStringFromUtf8()); var id = IdOf(document.RootElement, "assetId");
                if (id == Guid.Empty || _imageTarget is null) { Say("O servidor não retornou a imagem enviada."); return; }
                _imageTarget.Text = AssetReference(id); Say("Imagem enviada ao inventário e selecionada para o grupo.");
            }
            catch (JsonException) { Say("Resposta inválida ao enviar a imagem."); }
        };
        var headers = new[] { $"Authorization: Bearer {session.AccessToken}", $"Content-Type: multipart/form-data; boundary={boundary}" };
        if (request.RequestRaw($"{app.ApiBaseUrl}/api/v1/assets/upload", headers, Godot.HttpClient.Method.Post, payload) != Error.Ok) { request.QueueFree(); Say("Não foi possível iniciar o envio da imagem."); }
    }

    private void FillIdentity(GroupCardData g)
    {
        _setEmblem.Select(Math.Max(Array.IndexOf(GroupRules.Emblems, g.Emblem), 0));
        // Legacy external URLs are deliberately not carried into a later save.
        _setImage.Text = g.ImageUrl.StartsWith("asset://", StringComparison.OrdinalIgnoreCase) ? g.ImageUrl : string.Empty;
    }

    /// <summary>Loads group artwork through the API, which checks visibility and never follows a caller-provided URL.</summary>
    private void LoadGroupImage(GroupCardData g)
    {
        _imageFor = g.Id; _groupImage.Texture = null; _groupImage.Visible = false;
        if (Transport is not null || !g.ImageUrl.StartsWith("asset://", StringComparison.Ordinal)) return;
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
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        if (app is null || session is null || !session.IsAuthenticated || http.Request($"{app.ApiBaseUrl}/api/v1/groups/{g.Id}/image", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) http.QueueFree();
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
