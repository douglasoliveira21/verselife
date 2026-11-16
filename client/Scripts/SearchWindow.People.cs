using System.Globalization;
using System.Text.Json;
using Godot;
using LifeVerse.Shared.Search;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The People tab: when a person is chosen the right column shows their public profile (picture, name, bio, member since, public groups, whether they are online when they allow it) and the things one can do:
/// open the profile, add as a friend, send a message, offer a teleport, pay, block, and a "more" menu (copy the user name or the id, ask for a teleport, report). What is shown and allowed is decided by the server
/// (privacy, blocks); the actions are the same ones the right-click menu uses, aimed at the exact account found, never at a name.
/// </summary>
public partial class SearchWindow
{
    /// <summary>The actions on people (friend, message, block, pay, teleport, report). The game supplies it; tests give their own.</summary>
    public PersonActions? People { get; set; }
    public Func<PersonActions?>? PeopleSource { get; set; }
    private PersonActions? Actions => People ?? PeopleSource?.Invoke();

    private VBoxContainer _personBox = null!;
    private Button _friend = null!, _message = null!, _offer = null!, _pay = null!, _block = null!, _moreButton = null!;
    private PopupMenu _morePopup = null!;
    private Label _extra = null!;
    private PersonActions? _hooked;
    private readonly Dictionary<Guid, string> _relations = [];
    private readonly List<Label> _metaLabels = [];
    private string _relation = string.Empty, _personName = string.Empty, _username = string.Empty;
    private Guid _personId;
    private int _personSeq;

    private void BuildPersonActions(Control parent)
    {
        _personBox = new VBoxContainer { Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _personBox.AddThemeConstantOverride("separation", 6); parent.AddChild(_personBox);
        _friend = Action("Adicionar amigo", UiStyle.ButtonKind.Primary, OnFriend); _personBox.AddChild(_friend);
        _message = Action("Enviar mensagem", UiStyle.ButtonKind.Secondary, () => { if (PeopleReady()) Actions!.AskMessage(_personName, _personId); }); _personBox.AddChild(_message);
        _offer = Action("Oferecer teleporte", UiStyle.ButtonKind.Secondary, () => Invite("offer")); _personBox.AddChild(_offer);
        _pay = Action("Pagar", UiStyle.ButtonKind.Secondary, () => { if (PeopleReady()) Actions!.AskPayment(_personName, _personId); }); _personBox.AddChild(_pay);
        _block = Action("Bloquear", UiStyle.ButtonKind.Secondary, OnBlock); _personBox.AddChild(_block);
        _moreButton = Action("Mais opções ▾", UiStyle.ButtonKind.Secondary, ShowMore); _personBox.AddChild(_moreButton);
        _morePopup = new PopupMenu(); _morePopup.AddItem("Copiar nome de usuário", 0); _morePopup.AddItem("Copiar identificador", 1); _morePopup.AddItem("Pedir teleporte até a pessoa", 2); _morePopup.AddItem("Denunciar", 3); _morePopup.IdPressed += OnMore; AddChild(_morePopup);
    }

    // ---- loading the person -------------------------------------------------------------------------------------------------------

    private void LoadPerson(SearchHit hit)
    {
        var seq = ++_personSeq; _personId = hit.Id; _personName = hit.Title; _username = hit.Subtitle.TrimStart('@'); _relation = _relations.GetValueOrDefault(hit.Id, string.Empty);
        _extra.Text = "Carregando o perfil…"; UpdatePersonButtons();
        Send(Godot.HttpClient.Method.Get, $"/api/v1/users/{hit.Id}/profile/full", null, (status, body) =>
        {
            if (seq != _personSeq) return;
            if (status is < 200 or >= 300) { _extra.Text = status == 404 ? "Este perfil não está mais disponível." : "Não foi possível carregar o perfil agora."; return; }
            try
            {
                using var document = JsonDocument.Parse(body); var p = document.RootElement;
                if (p.TryGetProperty("displayName", out var n) && n.GetString() is { Length: > 0 } display) { _personName = display; _pTitle.Text = display; }
                if (p.TryGetProperty("username", out var u) && u.GetString() is { Length: > 0 } user) _username = user;
                var lines = new List<string>();
                if (Flag(p, "privateProfile")) lines.Add("Este perfil é privado.");
                else
                {
                    if (Str(p, "bio") is { Length: > 0 } bio) _pDescription.Text = bio;
                    if (DateTimeOffset.TryParse(Str(p, "joinedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var joined)) lines.Add("No LifeVerse desde " + joined.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR")) + ".");
                    if (p.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array && groups.GetArrayLength() > 0) lines.Add("Grupos: " + string.Join(", ", groups.EnumerateArray().Select(g => Str(g, "name")).Where(t => t.Length > 0).Take(6)) + ".");
                    if (Str(p, "presence") is { Length: > 0 } presence && presence != "offline") lines.Add("● " + (presence == "online" ? "Online agora" : presence));
                    if (Flag(p, "hasPhoto") && hit.ThumbnailUrl is null) FetchBytes($"/api/v1/users/{hit.Id}/profile/photo", bytes => { if (seq == _personSeq && bytes is not null && DecodeImage(bytes) is { } texture) _pImage.Texture = texture; });
                }
                _extra.Text = string.Join("\n", lines);
            }
            catch (JsonException) { _extra.Text = "Resposta inesperada do servidor."; }
        });
        LoadRelationOnly(hit);
    }

    private static string Str(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
    private static bool Flag(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>How the buttons read for the friendship state: nothing can be asked twice, and a blocked person cannot be written to, paid or invited.</summary>
    private void UpdatePersonButtons()
    {
        var blocked = _relation == "blocked"; var known = _relation.Length > 0;
        (_friend.Text, _friend.Disabled) = _relation switch { "none" => ("Adicionar amigo", false), "pending_out" => ("Pedido de amizade enviado", true), "pending_in" => ("Aceitar pedido de amizade", false), "friend" => ("Vocês são amigos ✓", true), "blocked" => ("Pessoa bloqueada", true), _ => ("Adicionar amigo", true) };
        _message.Disabled = _offer.Disabled = _pay.Disabled = blocked; _block.Disabled = blocked; _block.Text = blocked ? "Você bloqueou esta pessoa" : "Bloquear";
        _moreButton.Disabled = false; if (!known) _friend.TooltipText = "Aguarde: verificando a amizade";
    }

    /// <summary>Decorates the cards of people with how I am related to them (one question for the whole page).</summary>
    private void DecoratePeople()
    {
        var ids = _hits.Where(h => h.Kind == SearchKinds.Person).Select(h => h.Id).Distinct().Take(50).ToList(); if (ids.Count == 0) return;
        var seq = _seq;
        Send(Godot.HttpClient.Method.Get, "/api/v1/social/relations?ids=" + string.Join(",", ids), null, (status, body) =>
        {
            if (seq != _seq || status is < 200 or >= 300) return;
            try
            {
                using var document = JsonDocument.Parse(body);
                foreach (var property in document.RootElement.EnumerateObject()) if (Guid.TryParse(property.Name, out var id) && property.Value.GetString() is { } relation) _relations[id] = relation;
            }
            catch (JsonException) { return; }
            for (var i = 0; i < _hits.Count && i < _metaLabels.Count; i++) if (_hits[i].Kind == SearchKinds.Person) _metaLabels[i].Text = MetaText(_hits[i]);
        });
    }

    private static string RelationWords(string relation) => relation switch { "friend" => "Amigo", "pending_out" => "Pedido enviado", "pending_in" => "Quer ser seu amigo", "blocked" => "Bloqueado", _ => string.Empty };

    // ---- the actions --------------------------------------------------------------------------------------------------------------

    private bool PeopleReady()
    {
        if (Actions is not { } people) { _note.Text = "As ações com pessoas não estão disponíveis agora."; return false; }
        if (_hooked != people) { _hooked = people; people.Said += OnPersonSaid; }
        return true;
    }

    /// <summary>What happened to an action is said in the window too, and the friendship is read again (it may have changed).</summary>
    private void OnPersonSaid(string text)
    {
        if (!Visible || Selected is not { Kind: SearchKinds.Person } hit) return;
        _note.Text = text; _relations.Remove(hit.Id); LoadRelationOnly(hit);
    }

    private void LoadRelationOnly(SearchHit hit)
    {
        var seq = _personSeq;
        Send(Godot.HttpClient.Method.Get, $"/api/v1/social/relations?ids={hit.Id}", null, (status, body) =>
        {
            if (seq != _personSeq || status is < 200 or >= 300) return;
            try { using var document = JsonDocument.Parse(body); if (document.RootElement.TryGetProperty(hit.Id.ToString(), out var r) && r.GetString() is { } relation) { _relations[hit.Id] = relation; _relation = relation; UpdatePersonButtons(); if (_hits.Count > 0) for (var i = 0; i < _hits.Count && i < _metaLabels.Count; i++) if (_hits[i].Id == hit.Id) _metaLabels[i].Text = MetaText(_hits[i]); } } catch (JsonException) { }
        });
    }

    private void OnFriend()
    {
        if (!PeopleReady()) return; var people = Actions!;
        if (_relation == "none") people.Using(_personId, () => people.AddFriend(_personName));
        else if (_relation == "pending_in") people.Using(_personId, () => people.AcceptFriend(_personName));
    }

    private void Invite(string kind)
    {
        if (!PeopleReady()) return; var island = CurrentIsland?.Invoke() ?? Guid.Empty;
        if (island == Guid.Empty) { _note.Text = "Entre em uma ilha para convidar alguém ao seu teleporte."; return; }
        var people = Actions!; people.Using(_personId, () => people.SendTeleportInvite(_personName, island, kind));
    }

    private void OnBlock()
    {
        if (!PeopleReady()) return; var people = Actions!; var id = _personId; var name = _personName;
        people.Confirm("Bloquear pessoa", $"Bloquear {name}? Vocês deixam de se ver na busca e não poderão trocar mensagens, convites nem pagamentos.", "Bloquear", () => people.Using(id, () => people.Block(name)));
    }

    private void ShowMore() { _morePopup.Position = (Vector2I)(_moreButton.GetScreenPosition() + new Vector2(0, _moreButton.Size.Y)); _morePopup.Popup(); }

    private void OnMore(long id)
    {
        switch (id)
        {
            case 0: DisplayServer.ClipboardSet(_username); _note.Text = "Nome de usuário copiado."; break;
            case 1: DisplayServer.ClipboardSet(_personId.ToString()); _note.Text = "Identificador copiado."; break;
            case 2: Invite("request"); break;
            case 3: if (PeopleReady()) Actions!.AskReport(_personName, _personId); break;
        }
    }

    // ---- tests --------------------------------------------------------------------------------------------------------------------

    public string FriendTextForTesting => _friend.Text;
    public bool FriendEnabledForTesting => !_friend.Disabled;
    public bool PersonBoxVisibleForTesting => _personBox.Visible;
    public bool MessageEnabledForTesting => !_message.Disabled;
    public string ExtraForTesting => _extra.Text;
    public void PressFriendForTesting() => OnFriend();
    public void PressInviteForTesting() => Invite("offer");
    public void ChooseMoreForTesting(long id) => OnMore(id);
    public void PressBlockForTesting() => OnBlock();
    public void PressMessageForTesting() => _message.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressPayForTesting() => _pay.EmitSignal(BaseButton.SignalName.Pressed);
    public string MetaForTesting(int row) => _metaLabels[row].Text;
}
