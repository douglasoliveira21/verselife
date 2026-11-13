using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts.Groups;
using LifeVerse.Client.Scripts.Notifications;

namespace LifeVerse.Client.Tests;

/// <summary>
/// The "Grupos" window against a pretend server: the list of my groups with search, the summary of the chosen one, wearing a group (Ativar / Desativar), Chat and Informações, the latest announcements,
/// the empty states and the buttons that make and find groups.
/// </summary>
public partial class GroupsHubSmoke : Control
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Builders = Guid.NewGuid(), Explorers = Guid.NewGuid(), Cooks = Guid.NewGuid();

    private sealed class Server
    {
        public readonly List<(Guid Id, string Name, string Tag, string Description, string Emblem, string Policy, int Members, string Title, bool Active)> Groups = [];
        public readonly List<string> Calls = [];
        public bool Down, Refuse;
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        public Task<(long, string)> Handle(string method, string path, string? body)
        {
            Calls.Add($"{method} {path}");
            if (Down) return Task.FromResult<(long, string)>((0, ""));
            if (path == "/api/v1/groups/mine") return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(Groups.Select(g => new { id = g.Id, name = g.Name, tag = g.Tag, description = g.Description, accentColor = "#12F2E0", emblem = g.Emblem, privacy = "public", joinPolicy = g.Policy, members = g.Members, memberLimit = 500, isMember = true, isActive = g.Active, title = g.Title }), Web)));
            if (path.StartsWith("/api/v1/groups/notifications", StringComparison.Ordinal)) return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new { unread = 2, items = new[] { new { id = Guid.NewGuid(), groupId = Builders, read = false }, new { id = Guid.NewGuid(), groupId = Builders, read = false }, new { id = Guid.NewGuid(), groupId = Explorers, read = true } } }, Web)));
            if (path.EndsWith("/announcements", StringComparison.Ordinal))
            {
                var id = Guid.Parse(path.Split('/')[^2]);
                var rows = id == Builders ? new[] { ("Reunião sábado", false, 30), ("Regras novas", true, 9000), ("Mutirão", false, 200), ("Antigo", false, 20000) } : id == Explorers ? [] : new[] { ("Receita da semana", false, 5) };
                return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(rows.Select(r => new { id = Guid.NewGuid(), title = r.Item1, body = "", pinned = r.Item2, author = "chefe", at = Now.AddMinutes(-r.Item3) }), Web)));
            }
            if (path.EndsWith("/activate", StringComparison.Ordinal))
            {
                if (Refuse) return Task.FromResult<(long, string)>((403, "{\"error\":\"forbidden\"}"));
                var id = Guid.Parse(path.Split('/')[^2]); for (var i = 0; i < Groups.Count; i++) Groups[i] = Groups[i] with { Active = Groups[i].Id == id }; return Task.FromResult<(long, string)>((200, "{}"));
            }
            if (path == "/api/v1/groups/active" && method == "DELETE") { for (var i = 0; i < Groups.Count; i++) Groups[i] = Groups[i] with { Active = false }; return Task.FromResult<(long, string)>((200, "{}")); }
            return Task.FromResult<(long, string)>((404, "{}"));
        }
    }

    public override async void _Ready()
    {
        try
        {
            CenterView.Now = () => Now; var server = new Server(); var said = new List<string>(); var opened = new List<(Guid, int)>(); var created = 0; var found = 0;
            var hub = new GroupsHubWindow { Name = "Hub", Said = text => said.Add(text), OpenGroup = (id, tab) => opened.Add((id, tab)), CreateGroup = () => created++, FindGroups = () => found++ };
            hub.UseTransportForTesting(server.Handle); AddChild(hub); await Frames(3);

            // --- nothing yet ----------------------------------------------------------------------------------------------------------------------
            await hub.OpenAsync(); await Frames(6);
            Require(hub.Visible && hub.TextForTesting.Contains("Você ainda não participa de nenhum grupo."), $"no groups says so (got {hub.TextForTesting})");
            Require(hub.ButtonEnabledForTesting("Criar Grupo") && hub.ButtonEnabledForTesting("Procurar Grupos"), "making and finding groups are always there");
            hub.PressForTesting("Criar Grupo"); hub.PressForTesting("Procurar Grupos"); Require(created == 1 && found == 1, "the two buttons call their windows");

            // --- my groups --------------------------------------------------------------------------------------------------------------------------
            server.Groups.Add((Builders, "Construtores LV", "CLV", "Quem constrói o mundo junto.", "hammer", "request", 128, "Administrador", true));
            server.Groups.Add((Explorers, "Os Exploradores", "", "", "map", "open", 1, "Membro", false));
            server.Groups.Add((Cooks, "Cozinheiros", "COZ", "Receitas e festas.", "bag", "invite", 42, "Moderador", false));
            await hub.LoadAsync(); await Frames(6);
            Require(string.Join(",", hub.CardNamesForTesting) == "Construtores LV,Os Exploradores,Cozinheiros", $"the cards (got {string.Join(",", hub.CardNamesForTesting)})");
            var text = hub.TextForTesting;
            Require(text.Contains("Meus Grupos") && text.Contains("(3)") && text.Contains("Administrador") && text.Contains("128 membros") && text.Contains("1 membro") && text.Contains("Ativo"), $"title, counter, role, members and the active mark (got {text})");
            Require(hub.Selected == Builders, "the group in use is the one shown first");
            Require(text.Contains("Construtores LV  [CLV]") && text.Contains("128 membros · Público · Entrada por aprovação · seu cargo: Administrador") && text.Contains("Quem constrói o mundo junto."), $"the summary (got {text})");
            Require(text.Contains("Avisos recentes") && text.Contains("Regras novas") && text.Contains("Reunião sábado") && text.Contains("Mutirão") && !text.Contains("Antigo") && text.Contains("há 30 minutos") && text.Contains("há 3 horas"), $"the three latest announcements, pinned first (got {text})");
            Require(text.Contains("2") && hub.HasButtonForTesting("Desativar") && !hub.HasButtonForTesting("Ativar"), "the unread count is on the card and the active group offers to be turned off");
            var size = hub.SizeForTesting; var view = GetViewport().GetVisibleRect().Size; Require(size.X <= view.X && size.Y <= view.Y, $"the window fits the screen (size {size}, screen {view})");

            if (OS.GetEnvironment("LIFEVERSE_SHOT") is { Length: > 0 } shot) { GetWindow().Size = new Vector2I(1536, 1024); await Frames(8); GetViewport().GetTexture().GetImage().SavePng(shot); }

            // --- search -----------------------------------------------------------------------------------------------------------------------------
            hub.SearchForTesting("explo"); await Frames(4); Require(string.Join(",", hub.CardNamesForTesting) == "Os Exploradores", "search narrows the list");
            hub.SearchForTesting("coz"); await Frames(4); Require(string.Join(",", hub.CardNamesForTesting) == "Cozinheiros", "by tag too");
            hub.SearchForTesting("zzz"); await Frames(4); Require(hub.CardNamesForTesting.Count == 0 && hub.TextForTesting.Contains("Nenhum grupo encontrado."), "nothing found says so");
            hub.SearchForTesting(""); await Frames(4);

            // --- choosing and wearing --------------------------------------------------------------------------------------------------------------
            await hub.SelectForTesting(Explorers); await Frames(6);
            Require(hub.TextForTesting.Contains("Este grupo ainda não tem descrição.") && hub.TextForTesting.Contains("Nenhum aviso publicado.") && hub.HasButtonForTesting("Ativar") && !hub.HasButtonForTesting("Desativar"), "a group without description or announcements says so, and offers Ativar");
            hub.PressForTesting("Ativar"); await Frames(12);
            Require(server.Calls.Any(c => c == $"POST /api/v1/groups/{Explorers}/activate") && said.Last() == "Grupo ativo: Os Exploradores." && server.Groups.Single(g => g.Active).Id == Explorers && hub.HasButtonForTesting("Desativar"), $"Ativar wears the group and tells the person (said {said.LastOrDefault()})");
            Require(hub.Groups.Count(g => g.IsActive) == 1 && hub.Groups.Single(g => g.IsActive).Id == Explorers, "only one group is active");
            hub.PressForTesting("Desativar"); await Frames(12); Require(server.Calls.Any(c => c == "DELETE /api/v1/groups/active") && said.Last() == "Você não usa mais nenhum grupo." && hub.Groups.All(g => !g.IsActive), "Desativar stops wearing it");
            server.Refuse = true; hub.PressForTesting("Ativar"); await Frames(12); Require(said.Last().Length > 0 && said.Last() != "Você não usa mais nenhum grupo." && hub.Groups.All(g => !g.IsActive), $"a refusal is told and nothing changes (said {said.Last()})"); server.Refuse = false;

            // --- chat, information, announcements ----------------------------------------------------------------------------------------------------
            await hub.SelectForTesting(Builders); await Frames(6); hub.PressForTesting("Chat"); hub.PressForTesting("Informações");
            Require(opened.Contains((Builders, 1)) && opened.Contains((Builders, 0)), "Chat and Informações open the administration window of the group on their tabs");

            // --- trouble -----------------------------------------------------------------------------------------------------------------------------
            server.Down = true; await hub.LoadAsync(true); await Frames(4); Require(hub.Groups.Count == 3, "a lost connection leaves the list as it was");
            // the title over the avatar: the server writes it into the appearance and the nameplate shows it under the name
            var worn = LifeVerse.Client.Autoloads.AvatarManager.ParseAppearance("{\"displayName\":\"Ana\",\"groupLabel\":\"[CLV] Mestre\"}")!; Require(worn.GroupLabel == "[CLV] Mestre", "the appearance carries the group label");
            var plate = new LifeVerse.Client.Scripts.NamePlate(worn.DisplayName, null, worn.GroupLabel); AddChild(plate); await Frames(4);
            Require(plate.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text).SequenceEqual(["Ana", "[CLV] Mestre"]), "the nameplate shows the name and, under it, the group and title");
            var bare = new LifeVerse.Client.Scripts.NamePlate("Bia", null, ""); AddChild(bare); await Frames(4); Require(bare.FindChildren("*", "Label", true, false).OfType<Label>().Count() == 1, "without a group the nameplate is as before");
            GD.Print("event=groups_hub_smoke result=passed"); GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=groups_hub_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
