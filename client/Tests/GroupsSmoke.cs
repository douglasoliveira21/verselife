using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// The Groups window against canned server answers: lists fill, buttons follow the person's cargo and rank, and each action sends the right request.
public partial class GroupsSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var window = new GroupsWindow(); AddChild(window);
            var requests = new List<string>(); var group = Guid.NewGuid(); var me = Guid.NewGuid(); var other = Guid.NewGuid(); var roleMod = Guid.NewGuid(); var roleMember = Guid.NewGuid(); var roleOwner = Guid.NewGuid();
            var role = "moderator"; var perms = "[\"kick\",\"mute\"]"; var rank = 40; var leader = false; var memberRank = 0;
            window.Transport = (method, path, body) =>
            {
                requests.Add($"{method} {path} {body}");
                if (path == "/mine") return (200, $"[{{\"id\":\"{group}\",\"name\":\"Clube\",\"tag\":\"CLB\",\"description\":\"d\",\"accentColor\":\"#12F2E0\",\"privacy\":\"public\",\"joinPolicy\":\"request\",\"members\":2,\"memberLimit\":500,\"isMember\":true}}]");
                if (path == $"/{group}") return (200, $"{{\"card\":{{\"id\":\"{group}\",\"name\":\"Clube\",\"tag\":\"CLB\",\"description\":\"d\",\"accentColor\":\"#12F2E0\",\"privacy\":\"public\",\"joinPolicy\":\"request\",\"members\":2,\"memberLimit\":500,\"isMember\":true}},\"myRole\":\"{role}\",\"myRank\":{rank},\"myPermissions\":{perms},\"isLeader\":{(leader ? "true" : "false")},\"leaderName\":\"dono\"}}");
                if (path == $"/{group}/members") return (200, $"[{{\"userId\":\"{other}\",\"username\":\"ana\",\"roleId\":\"{roleMember}\",\"roleName\":\"Membro\",\"rank\":{memberRank}}},{{\"userId\":\"{me}\",\"username\":\"eu\",\"roleId\":\"{roleMod}\",\"roleName\":\"Moderador\",\"rank\":40}}]");
                if (path == $"/{group}/roles") return (200, $"[{{\"id\":\"{roleOwner}\",\"name\":\"Líder\",\"systemKey\":\"owner\",\"rank\":1000,\"permissions\":[],\"members\":1,\"canEdit\":false}},{{\"id\":\"{roleMod}\",\"name\":\"Moderador\",\"systemKey\":\"moderator\",\"rank\":40,\"permissions\":[\"kick\"],\"members\":1,\"canEdit\":false}},{{\"id\":\"{roleMember}\",\"name\":\"Membro\",\"systemKey\":\"member\",\"rank\":0,\"permissions\":[],\"members\":1,\"canEdit\":false}}]");
                if (path.StartsWith($"/search")) return (200, $"[{{\"id\":\"{group}\",\"name\":\"Clube\",\"tag\":\"\",\"description\":\"\",\"accentColor\":\"#12F2E0\",\"privacy\":\"public\",\"joinPolicy\":\"request\",\"members\":2,\"memberLimit\":500,\"isMember\":false}}]");
                if (path.EndsWith("/audit?take=100") || path.EndsWith("/requests") || path.EndsWith("/bans")) return (200, "[]");
                return (200, "{}");
            };
            await Frames(2);

            window.Open();
            Require(window.Visible && window.Mine.Count == 1, "opening should load my groups");
            window.SelectMine(0);
            Require(window.Current?.Name == "Clube" && window.InnerTabsVisibleForTesting, "the group should open with its tabs");
            Require(window.OverviewForTesting.Contains("dono"), "overview should show the leader");
            Require(window.Members.Count == 2 && window.Roles.Count == 3, "members and roles should load");

            // a moderator (rank 40, kick+mute) over a plain member
            window.SelectMemberForTesting(0);
            Require(window.ButtonEnabledForTesting("kick") && window.ButtonEnabledForTesting("mute"), "kick and mute should be on");
            Require(!window.ButtonEnabledForTesting("ban") && !window.ButtonEnabledForTesting("assign") && !window.ButtonEnabledForTesting("invite"), "ban, assign and invite need permissions not held");
            Require(!window.ButtonEnabledForTesting("roleCreate") && !window.ButtonEnabledForTesting("transfer") && !window.ButtonEnabledForTesting("dissolve"), "cargo and leader actions must be off");
            window.KickSelected();
            Require(requests.Any(r => r.StartsWith("Post ") && r.Contains($"/members/{other}/kick")), "kick request wrong");
            window.SelectMemberForTesting(0);
            window.MuteSelected(15);
            Require(requests.Any(r => r.Contains($"/members/{other}/mute") && r.Contains("\"minutes\":15")), "mute request wrong");
            window.SelectMemberForTesting(1);
            Require(!window.ButtonEnabledForTesting("kick"), "cannot kick someone of equal rank");

            // the leader gets everything
            role = "owner"; rank = 1000; leader = true; perms = "[]";
            window.OpenGroup(group);
            window.SelectMemberForTesting(0);
            foreach (var key in new[] { "kick", "ban", "mute", "assign", "invite", "roleCreate", "settings", "transfer", "dissolve" })
                Require(window.ButtonEnabledForTesting(key), $"the leader should have {key}");
            window.SelectRoleForTesting(1);
            Require(!window.ButtonEnabledForTesting("roleDelete"), "a system cargo cannot be deleted");
            window.SetRoleFormForTesting("Tesoureiro", 30, "kick", "mute");
            window.CreateRole();
            Require(requests.Any(r => r.StartsWith("Post ") && r.EndsWith($"/{group}/roles {{\"name\":\"Tesoureiro\",\"rank\":30,\"permissions\":[\"kick\",\"mute\"]}}")), $"create role wrong: {requests.LastOrDefault(r => r.Contains("/roles"))}");
            window.SetInviteForTesting("bia");
            window.InviteByName();
            Require(requests.Any(r => r.Contains("/invites") && r.Contains("\"username\":\"bia\"")), "invite request wrong");
            window.ChooseLeaderForTesting("ana"); window.SetConfirmForTesting("Clube");
            window.TransferLeadership();
            Require(requests.Any(r => r.Contains("/transfer") && r.Contains(other.ToString()) && r.Contains("\"confirm\":\"Clube\"")), "transfer request wrong");
            window.SetConfirmForTesting("Clube");
            window.Dissolve();
            Require(requests.Any(r => r.Contains("/dissolve") && r.Contains("\"confirm\":\"Clube\"")), "dissolve request wrong");

            // searching and joining
            window.Search("clu");
            Require(window.Found.Count == 1, "search should fill");
            window.SelectFoundForTesting(0);
            Require(window.ButtonEnabledForTesting("join") && window.JoinTextForTesting.Contains("Pedir"), "a request-policy group offers to ask");
            window.JoinSelected();
            Require(requests.Any(r => r.Contains($"/{group}/join")), "join request wrong");

            // creating
            window.SetCreateForTesting("Novo Clube", "NVO", "bom", 0, 0);
            window.Create();
            Require(requests.Any(r => r.StartsWith("Post  ") && r.Contains("\"name\":\"Novo Clube\"") && r.Contains("\"joinPolicy\":\"open\"") && r.Contains("\"privacy\":\"public\"")), $"create wrong: {requests.LastOrDefault(r => r.Contains("Novo"))}");

            Require(GroupsWindow.Explain(403, "{\"error\":\"outranked\"}").Contains("abaixo"), "errors should be explained");
            GD.Print("event=groups_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=groups_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
