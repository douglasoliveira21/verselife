using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// The People window against canned server answers: lists fill, buttons follow the selection, and each action sends
/// the right request. Nothing touches the network.
public partial class PeopleSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var screen = new Control(); AddChild(screen);
            var panel = new PeoplePanel(); AddChild(panel); panel.AttachTo(screen);
            var requests = new List<string>(); var messages = new List<string>();
            panel.RequestSink = (method, path, body) => { requests.Add($"{method} {path} {body}"); return true; };
            panel.Message += messages.Add;
            await Frames(2);

            panel.Toggle();
            Require(panel.Visible && requests.Count == 4, $"opening should load four lists: {requests.Count}");
            Require(requests.Any(r => r.EndsWith("/api/v1/social/friends/requests ")) && requests.Any(r => r.Contains("/groups/invites")), "missing a list request");

            panel.QueryForTesting.Text = "a";
            panel.PressForTesting("search");
            Require(messages.Count == 1 && requests.Count == 4, "a one-letter search must not be sent");
            panel.QueryForTesting.Text = "ma ria";
            panel.PressForTesting("search");
            Require(requests[^1].Contains("people?q=ma%20ria"), $"search request wrong: {requests[^1]}");

            var friend = Guid.NewGuid(); var received = Guid.NewGuid(); var sent = Guid.NewGuid(); var stranger = Guid.NewGuid();
            panel.DeliverForTesting("Search", $"[{{\"id\":\"{stranger}\",\"username\":\"maria\",\"displayName\":\"Maria\",\"relation\":\"none\"}},{{\"id\":\"{friend}\",\"username\":\"mario\",\"displayName\":\"Mario\",\"relation\":\"friend\"}},{{\"id\":\"{received}\",\"username\":\"marta\",\"displayName\":\"Marta\",\"relation\":\"pending_in\"}}]");
            Require(panel.ResultsForTesting.ItemCount == 3, "three results expected");
            panel.ResultsForTesting.Select(0); panel.ResultsForTesting.EmitSignal(ItemList.SignalName.ItemSelected, 0);
            panel.PressForTesting("searchAction");
            Require(requests[^1].StartsWith("Post ") && requests[^1].Contains("/friends/requests") && requests[^1].Contains(stranger.ToString()), $"add friend wrong: {requests[^1]}");
            var before = requests.Count;
            panel.ResultsForTesting.Select(1); panel.ResultsForTesting.EmitSignal(ItemList.SignalName.ItemSelected, 1);
            panel.PressForTesting("searchAction");
            Require(requests.Count == before, "an existing friend must not get another request");
            panel.ResultsForTesting.Select(2); panel.ResultsForTesting.EmitSignal(ItemList.SignalName.ItemSelected, 2);
            panel.PressForTesting("searchAction");
            Require(requests[^1].Contains($"/friends/{received}/accept"), $"accept wrong: {requests[^1]}");

            panel.DeliverForTesting("Requests", $"[{{\"userId\":\"{received}\",\"username\":\"marta\",\"direction\":\"in\"}},{{\"userId\":\"{sent}\",\"username\":\"paulo\",\"direction\":\"out\"}}]");
            Require(panel.RequestsForTesting.ItemCount == 2, "two requests expected");
            panel.RequestsForTesting.Select(1); panel.RequestsForTesting.EmitSignal(ItemList.SignalName.ItemSelected, 1);
            before = requests.Count;
            panel.PressForTesting("accept");
            Require(requests.Count == before, "an outgoing request cannot be accepted by me");
            panel.PressForTesting("decline");
            Require(requests[^1].Contains($"/friends/{sent}/decline"), $"decline wrong: {requests[^1]}");

            panel.DeliverForTesting("Friends", $"[{{\"id\":\"{friend}\",\"username\":\"mario\"}}]");
            panel.FriendsForTesting.Select(0); panel.FriendsForTesting.EmitSignal(ItemList.SignalName.ItemSelected, 0);
            panel.PressForTesting("remove");
            Require(requests[^1].StartsWith("Delete ") && requests[^1].Contains($"/friends/{friend}"), $"remove wrong: {requests[^1]}");

            var invite = Guid.NewGuid(); var group = Guid.NewGuid();
            panel.DeliverForTesting("Invites", $"[{{\"inviteId\":\"{invite}\",\"groupId\":\"{group}\",\"groupName\":\"Clube\",\"fromUserId\":\"{friend}\",\"fromUsername\":\"mario\"}}]");
            panel.InvitesForTesting.Select(0); panel.InvitesForTesting.EmitSignal(ItemList.SignalName.ItemSelected, 0);
            panel.PressForTesting("inviteAccept");
            Require(requests[^1].Contains($"/groups/invites/{invite}/accept"), $"invite wrong: {requests[^1]}");

            panel.DeliverForTesting("Groups", $"[{{\"id\":\"{group}\",\"name\":\"Clube\",\"description\":\"\",\"role\":\"member\",\"members\":3}}]");
            Require(panel.GroupsForTesting.GetItemText(0).Contains("3 membros"), "group row should show the member count");
            panel.GroupsForTesting.Select(0); panel.GroupsForTesting.EmitSignal(ItemList.SignalName.ItemSelected, 0);
            panel.PressForTesting("leave");
            Require(requests[^1].Contains($"/groups/{group}/leave"), $"leave wrong: {requests[^1]}");

            panel.PressForTesting("create");
            Require(!requests[^1].Contains("\"name\""), "an empty group name must not be sent");
            GD.Print("event=people_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=people_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
