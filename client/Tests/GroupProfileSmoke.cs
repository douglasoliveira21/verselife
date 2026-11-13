using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Scripts.Groups;
using LifeVerse.Client.Scripts.Notifications;

namespace LifeVerse.Client.Tests;

/// <summary>
/// The profile of a group against a pretend server: the side bar with ten tabs, what a member and what somebody outside see, joining, members with filters and the menu of actions, cargos, the matrix of
/// permissions, announcements, chat, land, the money of the group, proposals and votes, the settings and the dialogs fitting the screen.
/// </summary>
public partial class GroupProfileSmoke : Control
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Group = Guid.NewGuid(), Island = Guid.NewGuid(), Leader = Guid.NewGuid(), Ana = Guid.NewGuid(), Bruno = Guid.NewGuid();
    private static readonly Guid OwnerRole = Guid.NewGuid(), AdminRole = Guid.NewGuid(), MemberRole = Guid.NewGuid();
    private static readonly string[] AllPermissions = ["invite", "kick", "ban", "mute", "edit_profile", "manage_roles", "assign_roles", "announce", "manage_land", "manage_treasury", "view_treasury", "manage_proposals"];

    private sealed class Server
    {
        public bool IsMember = true, Pending, IsLeader = true; public string Policy = "request"; public long Fee;
        public readonly List<string> Calls = []; public readonly List<string> Bodies = [];
        public readonly List<(Guid Id, string Title, string Body)> Notices = [(Guid.NewGuid(), "Reunião sábado", "No Mirante às 20h")];
        public readonly List<(string User, string Text)> Chat = [("ana", "Oi pessoal")];
        public readonly List<Guid> Requests = [Guid.NewGuid()];
        public readonly Guid Proposal = Guid.NewGuid(); public bool Voted; public int MyOption = -1;
        public readonly List<string> AdminPermissions = ["invite", "kick", "announce"];
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
        private static (long, string) Ok(object? body = null) => (200, JsonSerializer.Serialize(body ?? new { }, Web));

        public Task<(long, string)> Handle(string method, string path, string? body)
        {
            Calls.Add($"{method} {path}"); if (body is not null) Bodies.Add(body);
            var rest = path.Replace($"/api/v1/groups/{Group}", "");
            if (path == $"/api/v1/worlds/islands/{Island}") return Task.FromResult(Ok(new { id = Island, name = "Praça Central" }));
            if (rest == "" && method == "GET") return Task.FromResult(Ok(View()));
            if (rest == "" && method == "PUT") return Task.FromResult(Ok());
            if (rest == "/join")
            {
                var needsFee = method != "DELETE" && Policy == "open" && Fee > 0 && JsonNode.Parse(body!)?["confirmFee"] is null;
                return Task.FromResult(needsFee ? ((long, string))(409, "{\"error\":\"fee_required\"}") : Ok());
            }
            if (rest == "/requests") return Task.FromResult(Ok(Requests.Select(r => new { id = r, userId = Guid.NewGuid(), username = "carla", message = "Quero construir", at = Now.AddHours(-1) })));
            if (rest.StartsWith("/requests/", StringComparison.Ordinal)) { Requests.RemoveAll(r => rest.Contains(r.ToString(), StringComparison.Ordinal)); return Task.FromResult(Ok()); }
            if (rest == "/leave" || rest == "/invites" || rest.StartsWith("/members/", StringComparison.Ordinal)) return Task.FromResult(Ok());
            if (rest == "/members") return Task.FromResult(Ok(new[] { Member(Leader, "lider", OwnerRole, "Líder", 1000), Member(Ana, "ana", AdminRole, "Administrador", 80), Member(Bruno, "bruno", MemberRole, "Membro", 0) }));
            if (rest == "/roles" && method == "GET") return Task.FromResult(Ok(new[] { Role(OwnerRole, "Líder", "owner", 1000, AllPermissions, 1, false), Role(AdminRole, "Administrador", "admin", 80, [.. AdminPermissions], 1, true), Role(MemberRole, "Membro", "member", 0, [], 1, true) }));
            if (rest.StartsWith("/roles", StringComparison.Ordinal)) { if (rest.EndsWith("/title", StringComparison.Ordinal)) return Task.FromResult(Ok()); if (method == "PUT" && JsonNode.Parse(body!)!["permissions"] is JsonArray a) { AdminPermissions.Clear(); AdminPermissions.AddRange(a.Select(x => x!.GetValue<string>())); } return Task.FromResult(Ok(new { id = Guid.NewGuid() })); }
            if (rest == "/announcements" && method == "GET") return Task.FromResult(Ok(Notices.Select(n => new { id = n.Id, title = n.Title, body = n.Body, pinned = false, authorUserId = Leader, author = "lider", at = Now.AddMinutes(-30), expiresAt = Now.AddDays(2), attachmentUrl = "https://exemplo.com/cartaz.png" })));
            if (rest == "/announcements") { var json = JsonNode.Parse(body!)!; Notices.Add((Guid.NewGuid(), json["title"]!.GetValue<string>(), json["body"]!.GetValue<string>())); return Task.FromResult(Ok()); }
            if (rest.StartsWith("/announcements/", StringComparison.Ordinal)) { Notices.RemoveAll(n => rest.EndsWith(n.Id.ToString(), StringComparison.Ordinal)); return Task.FromResult(Ok()); }
            if (rest.StartsWith("/chat", StringComparison.Ordinal)) { if (method == "POST") { Chat.Add(("lider", JsonNode.Parse(body!)!["content"]!.GetValue<string>())); return Task.FromResult(Ok(new { id = Guid.NewGuid() })); } return Task.FromResult(Ok(Chat.Select((c, i) => new { id = Guid.NewGuid(), userId = Ana, username = c.User, content = c.Text, at = Now.AddMinutes(-10 + i) }))); }
            if (rest == "/lands") return Task.FromResult(Ok(new { lands = new[] { new { parcelId = Guid.NewGuid(), islandId = Island, islandName = "Praça Central", name = "Sede", area = 1024.0, maxObjects = 500, accessMode = "public", buildMode = "owner", forSale = false } }, canManage = true }));
            if (rest == "/treasury") return Task.FromResult(Ok(new { canView = true, canTake = true, canFreeze = true, balance = 125430L, totalIn = 130000L, totalOut = 4570L, frozen = false, myContributed = 0L }));
            if (rest.StartsWith("/treasury/ledger", StringComparison.Ordinal)) return Task.FromResult(Ok(new[] { new { id = Guid.NewGuid(), type = "contribution", amount = 5000L, balanceAfter = 125430L, actor = "ana", target = (string?)null, note = "Para a obra", at = Now.AddHours(-2) }, new { id = Guid.NewGuid(), type = "withdrawal", amount = 1200L, balanceAfter = 120430L, actor = "lider", target = (string?)"bruno", note = "Material", at = Now.AddDays(-1) } }));
            if (rest.StartsWith("/treasury/", StringComparison.Ordinal)) return Task.FromResult(Ok());
            if (rest.StartsWith("/proposals", StringComparison.Ordinal))
            {
                if (rest.EndsWith("/vote", StringComparison.Ordinal)) { Voted = true; MyOption = JsonNode.Parse(body!)!["option"]!.GetValue<int>(); return Task.FromResult(Ok()); }
                if (rest == "/proposals" && method == "POST") return Task.FromResult(Ok(new { id = Guid.NewGuid() }));
                var item = new { id = Proposal, title = "Pintar a praça", description = "Qual cor?", authorUserId = Leader, author = "lider", status = "open", createdAt = Now.AddDays(-1), endsAt = Now.AddDays(2), criterion = "majority", quorumPercent = 30, minRank = 0, options = new[] { "Azul", "Verde" }, votes = 4, eligible = 10, myVote = Voted ? (int?)MyOption : null, canVote = !Voted, canManage = true, counts = (int[]?)null, winningOption = (int?)null };
                return Task.FromResult(Ok(rest.StartsWith("/proposals?", StringComparison.Ordinal) || rest == "/proposals" ? new { total = 1, items = new[] { item } } : item));
            }
            return Task.FromResult<(long, string)>((404, "{}"));
        }

        private static object Member(Guid id, string name, Guid role, string roleName, int rank) => new { userId = id, username = name, roleId = role, roleName, rank, joinedAt = Now.AddDays(-20), mutedUntil = (DateTimeOffset?)null };
        private static object Role(Guid id, string name, string key, int rank, string[] permissions, int members, bool canEdit) => new { id, name, systemKey = key, rank, permissions, color = "#A9B3BD", members, canEdit, title = key == "admin" ? "Mestre" : "" };

        private object View() => new
        {
            card = new { id = Group, name = "Construtores LV", tag = "CLV", description = "Quem constrói o mundo junto.", accentColor = "#12F2E0", emblem = "hammer", privacy = "public", joinPolicy = Policy, members = 3, memberLimit = 500, isMember = IsMember, requestPending = Pending, invited = false, banned = false, isActive = false, title = "Líder", category = "construcao", language = "pt-BR", contentRating = "moderado", joinFee = Fee, coverUrl = "", headquartersIslandId = Island },
            myRole = IsMember ? "Líder" : null, myRank = IsMember ? (IsLeader ? 1000 : 0) : -1, myPermissions = IsMember ? AllPermissions : [], isLeader = IsMember && IsLeader, createdAt = Now.AddDays(-30), leaderName = "lider"
        };
    }

    public override async void _Ready()
    {
        try
        {
            CenterView.Now = () => Now; var server = new Server(); var said = new List<string>(); Guid? visited = null; (Guid, int)? admin = null;
            var window = new GroupProfileWindow { Name = "Profile", Said = text => said.Add(text), Visit = id => visited = id, OpenAdmin = (id, tab) => admin = (id, tab), ManageLand = id => admin = (id, 4) }; window.SetTransportForTesting(server.Handle); AddChild(window); await Frames(3);
            var screen = GetViewport().GetVisibleRect().Size;

            // --- Geral, as a member -------------------------------------------------------------------------------------------------------------
            await window.OpenAsync(Group); await Frames(8);
            Require(window.Visible && string.Join(",", window.TabTextsForTesting) == "Geral,Membros,Cargos,Permissões,Avisos,Chat,Terrenos,Economia,Propostas,Configurações", "the ten tabs, in order");
            var text = window.TextForTesting; Require(text.Contains("Construtores LV  [CLV]") && text.Contains("3 membro(s)") && text.Contains("Construção") && text.Contains("Quem constrói o mundo junto.") && text.Contains("Fundador") && text.Contains("lider") && text.Contains("Grátis") && text.Contains("Moderado") && text.Contains("Entrada por aprovação") && text.Contains("Praça Central"), $"identity, founder, entry, rating, fee and the headquarters (got {text})");
            Require(window.HasButtonForTesting("Convidar") && window.HasButtonForTesting("Abrir chat") && window.HasButtonForTesting("Ver membros") && window.HasButtonForTesting("Sair do grupo") && !window.HasButtonForTesting("Participar"), "a member sees the member actions");
            window.PressForTesting("Teleportar"); Require(visited == Island, "Teleportar goes to the headquarters");
            var size = window.SizeForTesting; Require(size.X <= screen.X && size.Y <= screen.Y, $"the window fits the screen (size {size}, screen {screen})");

            // --- Membros ---------------------------------------------------------------------------------------------------------------------------
            window.PressForTesting("Ver membros"); await Frames(10); text = window.TextForTesting;
            Require(window.TabForTesting == "membros" && text.Contains("lider") && text.Contains("ana") && text.Contains("bruno") && text.Contains("Administrador"), $"the members table (got {text})");
            Require(text.Contains("Solicitações para entrar (1)") && window.ContentForTesting.FindChildren("*", "CheckBox", true, false).OfType<CheckBox>().Any(b => b.Text.StartsWith("carla", StringComparison.Ordinal)) && window.HasButtonForTesting("Aprovar selecionados"), $"pending requests show above the members (got {text})");
            window.PressForTesting("Aprovar selecionados"); await Frames(8); Require(said.Last() == "Marque pelo menos uma solicitação." && server.Requests.Count == 1, "nothing is approved without a mark");
            window.ContentForTesting.FindChildren("*", "CheckBox", true, false).OfType<CheckBox>().First(b => b.Text.StartsWith("carla", StringComparison.Ordinal)).ButtonPressed = true; window.PressForTesting("Aprovar selecionados"); await Frames(14);
            Require(server.Requests.Count == 0 && said.Contains("1 pessoa(s) aprovada(s).") && !window.TextForTesting.Contains("Solicitações para entrar"), "a marked request is approved and the box goes away");
            var search = window.ContentForTesting.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().First(l => l.PlaceholderText == "Pesquisar membros..."); search.Text = "bru"; search.EmitSignal(LineEdit.SignalName.TextChanged, "bru"); await Frames(6);
            Require(window.TextForTesting.Contains("bruno") && !window.TextForTesting.Contains("ana") && window.TextForTesting.Contains("1 resultado(s)"), "search narrows the table"); search.Text = ""; search.EmitSignal(LineEdit.SignalName.TextChanged, ""); await Frames(6);
            var menus = window.ContentForTesting.FindChildren("*", "MenuButton", true, false).OfType<MenuButton>().ToList(); Require(menus.Count == 3, $"one actions menu for each member (got {menus.Count})");
            var brunoMenu = menus.Last().GetPopup(); var items = Enumerable.Range(0, brunoMenu.ItemCount).Select(i => brunoMenu.GetItemText(i)).ToList(); Require(items.Contains("Ver perfil") && items.Contains("Atribuir cargo…") && items.Contains("Expulsar") && items.Contains("Banir") && items.Any(i => i.StartsWith("Silenciar")), $"the leader can act on a member (got {string.Join(",", items)})");
            var leaderMenu = menus.First().GetPopup(); Require(leaderMenu.ItemCount == 1, "nobody acts on the leader"); // only "Ver perfil"
            brunoMenu.EmitSignal(PopupMenu.SignalName.IdPressed, items.IndexOf("Expulsar")); await Frames(8); var dialog = window.DialogsForTesting.Last();
            Require(dialog.Size.Y > 100 && dialog.Position.Y + dialog.Size.Y <= screen.Y, $"the confirmation fits the screen (size {dialog.Size})"); dialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed); await Frames(12);
            Require(server.Calls.Any(c => c == $"POST /api/v1/groups/{Group}/members/{Bruno}/kick") && said.Last() == "bruno foi expulso.", $"kicking asks first and then does it (said {said.LastOrDefault()})");

            // --- Cargos ----------------------------------------------------------------------------------------------------------------------------
            await window.OpenAsync(Group, "cargos"); await Frames(10); await window.SelectRoleForTesting("Administrador"); await Frames(8);
            text = window.TextForTesting; Require(text.Contains("Líder") && text.Contains("Administrador") && text.Contains("Membro") && text.Contains("Permissões") && window.HasButtonForTesting("Novo cargo") && window.HasButtonForTesting("Duplicar cargo") && window.HasButtonForTesting("Salvar"), $"roles and the editor (got {text})");
            window.PressForTesting("Salvar"); await Frames(14); Require(server.Calls.Any(c => c == $"PUT /api/v1/groups/{Group}/roles/{AdminRole}") && server.Calls.Any(c => c == $"PUT /api/v1/groups/{Group}/roles/{AdminRole}/title") && said.Last() == "Cargo salvo.", "saving a cargo sends its permissions and title");
            await window.SelectRoleForTesting("Líder"); await Frames(8); Require(!window.ButtonEnabledForTesting("Excluir") && window.TextForTesting.Contains("nunca é editado"), "the leader cargo is never edited or deleted");
            window.PressForTesting("Novo cargo"); await Frames(8); dialog = window.DialogsForTesting.Last(); Require(dialog.Size.Y > 100 && dialog.Position.Y + dialog.Size.Y <= screen.Y, "the new cargo dialog fits the screen");
            dialog.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().First().Text = "Construtor"; dialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed); await Frames(14);
            Require(server.Calls.Any(c => c == $"POST /api/v1/groups/{Group}/roles") && server.Bodies.Any(b => b.Contains("Construtor")), "a new cargo is created");

            // --- Permissões ---------------------------------------------------------------------------------------------------------------------------
            await window.OpenAsync(Group, "permissoes"); await Frames(10); text = window.TextForTesting;
            Require(text.Contains("Matriz de permissões") && text.Contains("Convidar pessoas") && text.Contains("Publicar anúncios") && text.Contains("Administrador"), "the matrix");
            var boxes = window.ContentForTesting.FindChildren("*", "CheckBox", true, false).OfType<CheckBox>().Where(b => !b.Disabled).ToList(); Require(boxes.Count > 0, "the editable boxes of the matrix"); var before = server.AdminPermissions.Count; boxes.First(b => !b.ButtonPressed).ButtonPressed = true;
            window.PressForTesting("Salvar alterações"); await Frames(14); Require(server.AdminPermissions.Count == before + 1 || server.AdminPermissions.Count != before, $"saving the matrix sends the new permissions (before {before}, now {server.AdminPermissions.Count})");

            // --- Avisos -----------------------------------------------------------------------------------------------------------------------------
            await window.OpenAsync(Group, "avisos"); await Frames(10); text = window.TextForTesting;
            Require(text.Contains("Reunião sábado") && text.Contains("No Mirante às 20h") && text.Contains("há 30 minutos") && text.Contains("expira em") && window.HasButtonForTesting("+ Novo aviso"), $"the notices with date and expiry (got {text})");
            window.PressForTesting("+ Novo aviso"); await Frames(8); dialog = window.DialogsForTesting.Last(); Require(dialog.Size.Y > 100 && dialog.Position.Y + dialog.Size.Y <= screen.Y, $"the notice dialog fits the screen (size {dialog.Size})");
            dialog.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().First().Text = "Mutirão"; dialog.FindChildren("*", "TextEdit", true, false).OfType<TextEdit>().First().Text = "Domingo, 10h."; dialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed); await Frames(14);
            Require(server.Notices.Any(n => n.Title == "Mutirão") && window.TextForTesting.Contains("Mutirão") && said.Last().StartsWith("Aviso publicado"), "publishing adds the notice and tells the members");

            // --- Chat -------------------------------------------------------------------------------------------------------------------------------
            await window.OpenAsync(Group, "chat"); await Frames(10); Require(window.TextForTesting.Contains("Oi pessoal"), "the chat history");
            var input = window.ContentForTesting.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().First(l => l.PlaceholderText == "Digite uma mensagem..."); input.Text = "Bom dia, grupo!"; input.EmitSignal(LineEdit.SignalName.TextSubmitted, input.Text); await Frames(14);
            Require(server.Chat.Any(c => c.Text == "Bom dia, grupo!") && window.TextForTesting.Contains("Bom dia, grupo!") && input.Text == "", "Enter sends and the message shows");

            // --- Terrenos ---------------------------------------------------------------------------------------------------------------------------
            await window.OpenAsync(Group, "terrenos"); await Frames(10); Require(window.TextForTesting.Contains("Sede") && window.TextForTesting.Contains("Praça Central") && window.TextForTesting.Contains("1.024 m²") && window.TextForTesting.Contains("Aberto"), $"the land table (got {window.TextForTesting})");
            window.PressForTesting("Gerenciar terrenos"); Require(admin == (Group, 4), "Gerenciar terrenos opens the land administration");

            // --- Economia ---------------------------------------------------------------------------------------------------------------------------
            await window.OpenAsync(Group, "economia"); await Frames(10); text = window.TextForTesting;
            Require(text.Contains("L$ 125.430") && text.Contains("Contribuição") && text.Contains("Retirada") && text.Contains("+ L$ 5.000") && text.Contains("− L$ 1.200") && window.ButtonEnabledForTesting("Receber") && window.ButtonEnabledForTesting("Pagar"), $"balance, history in green and red, and the two buttons (got {text})");
            window.PressForTesting("Receber"); await Frames(8); dialog = window.DialogsForTesting.Last(); dialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed); await Frames(12);
            Require(server.Calls.Any(c => c.EndsWith("/treasury/contribute", StringComparison.Ordinal)) && said.Last().StartsWith("Você contribuiu"), "contributing sends the coins");

            // --- Propostas ---------------------------------------------------------------------------------------------------------------------------
            await window.OpenAsync(Group, "propostas"); await Frames(10); text = window.TextForTesting; Require(text.Contains("Pintar a praça") && text.Contains("Em votação") && text.Contains("4 voto(s) de 10") && text.Contains("você ainda não votou"), $"the proposals (got {text})");
            await window.OpenProposalAsync(server.Proposal); await Frames(8); dialog = window.DialogsForTesting.Last(); var shown = string.Join("|", dialog.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
            Require(shown.Contains("Azul") && shown.Contains("Verde") && shown.Contains("maioria") && shown.Contains("quórum 30%") && shown.Contains("A divisão aparece quando a votação acabar."), $"the vote screen hides the split while it runs (got {shown})");
            dialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed); await Frames(12); Require(server.Voted && said.Last() == "Voto registrado.", "voting sends the option");
            window.PressForTesting("Criar proposta"); await Frames(8); dialog = window.DialogsForTesting.Last(); Require(dialog.Size.Y > 100 && dialog.Position.Y + dialog.Size.Y <= screen.Y, $"the proposal dialog fits the screen (size {dialog.Size})");
            dialog.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().First().Text = "Nova regra"; dialog.FindChildren("*", "TextEdit", true, false).OfType<TextEdit>().Last().Text = "Sim\nNão"; dialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed); await Frames(12);
            Require(server.Calls.Any(c => c == $"POST /api/v1/groups/{Group}/proposals") && server.Bodies.Any(b => b.Contains("Nova regra") && b.Contains("\"Sim\"")), "a proposal is published with its options");

            // --- Configurações -------------------------------------------------------------------------------------------------------------------------
            await window.OpenAsync(Group, "configuracoes"); await Frames(10); text = window.TextForTesting; Require(text.Contains("Taxa de inscrição") && text.Contains("Limite de membros") && text.Contains("Área de risco") && text.Contains("Classificação de conteúdo"), $"the settings form (got {text})");
            window.PressForTesting("Salvar alterações"); await Frames(12); Require(server.Calls.Any(c => c == $"PUT /api/v1/groups/{Group}") && server.Bodies.Any(b => b.Contains("\"category\":\"construcao\"") && b.Contains("\"contentRating\":\"moderado\"")), "saving sends every field"); window.PressForTesting("Dissolver grupo"); Require(admin == (Group, 11), "dissolving opens the guarded administration");

            // --- somebody outside: only the general tab, and how to join -----------------------------------------------------------------------------
            server.IsMember = false; server.Policy = "open"; server.Fee = 50; await window.OpenAsync(Group); await Frames(10);
            Require(window.HasButtonForTesting("Participar") && !window.HasButtonForTesting("Sair do grupo") && !window.TabEnabledForTesting("membros") && !window.TabEnabledForTesting("chat") && !window.TabEnabledForTesting("configuracoes") && window.TabEnabledForTesting("geral") && window.TextForTesting.Contains("L$ 50"), "an outsider reads only the general tab and can join");
            window.PressForTesting("Participar"); await Frames(8); dialog = window.DialogsForTesting.Last(); var offer = string.Join("|", dialog.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text)); Require(offer.Contains("L$ 50"), "the price is shown before joining"); dialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed); await Frames(12);
            Require(server.Bodies.Any(b => b.Contains("\"confirmFee\":50")), "the exact price is confirmed");
            server.Policy = "request"; server.Fee = 0; server.Pending = true; await window.OpenAsync(Group); await Frames(10); Require(window.HasButtonForTesting("Solicitação pendente") && !window.ButtonEnabledForTesting("Solicitação pendente") && window.HasButtonForTesting("Cancelar pedido"), "a pending request shows as pending and can be withdrawn");

            GD.Print("event=group_profile_smoke result=passed"); GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=group_profile_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
