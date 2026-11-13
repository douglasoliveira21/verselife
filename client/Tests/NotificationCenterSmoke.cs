using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts.Notifications;

namespace LifeVerse.Client.Tests;

/// <summary>
/// The bell of the top bar and the notification center against a pretend server: the counter on the bell, the panel with its tabs, answering an invitation with one click (and the server refusing), marking as read,
/// paging, the full window with search, empty and error states, and the panel fitting the screen.
/// </summary>
public partial class NotificationCenterSmoke : Control
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Group = Guid.NewGuid();

    private sealed record Item(string Source, Guid Id, string Category, string Kind, string Title, string Body, int MinutesAgo, bool Read = false, string[]? Actions = null);

    private sealed class Server
    {
        public readonly List<Item> Items = [];
        public readonly List<string> Calls = [];
        public bool Down, Full;
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        private int Unread(string? category) => Items.Count(i => (category is null || i.Category == category) && (i.Source == "invite" || !i.Read));

        public Task<(long, string)> Handle(string method, string path, string? body)
        {
            Calls.Add($"{method} {path}");
            if (Down) return Task.FromResult<(long, string)>((0, ""));
            if (path.StartsWith("/api/v1/notifications/center?", StringComparison.Ordinal))
            {
                var query = System.Web.HttpUtility.ParseQueryString(path[(path.IndexOf('?') + 1)..]);
                var category = query["category"] is { Length: > 0 } c && c != "todas" ? c : null; var unread = query["unread"] == "true"; var q = query["q"]; var skip = int.Parse(query["skip"] ?? "0"); var take = int.Parse(query["take"] ?? "20");
                var rows = Items.Where(i => (category is null || i.Category == category) && (!unread || i.Source == "invite" || !i.Read) && (string.IsNullOrEmpty(q) || i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Body.Contains(q, StringComparison.OrdinalIgnoreCase))).OrderBy(i => i.MinutesAgo).ToList();
                var page = rows.Skip(skip).Take(take).Select(i => new { source = i.Source, id = i.Id, category = i.Category, kind = i.Kind, title = i.Title, body = i.Body, at = Now.AddMinutes(-i.MinutesAgo), read = i.Read, groupId = i.Source == "property" ? (Guid?)null : Group, groupName = i.Source == "property" ? null : "Construtores LV", actions = i.Actions ?? [] });
                return Task.FromResult<(long, string)>((200, JsonSerializer.Serialize(new { unread = Unread(null), unreadByCategory = new { grupos = Unread("grupos"), convites = Unread("convites"), sistema = Unread("sistema") }, skip, take, hasMore = rows.Count > skip + take, items = page }, Web)));
            }
            if (path == "/api/v1/notifications/center/read")
            {
                var json = System.Text.Json.Nodes.JsonNode.Parse(body!)!.AsObject(); var all = json["all"]!.GetValue<bool>(); var category = json["category"]!.GetValue<string>();
                var ids = json["items"] is System.Text.Json.Nodes.JsonArray a ? a.Select(x => Guid.Parse(x!["id"]!.GetValue<string>())).ToHashSet() : [];
                for (var i = 0; i < Items.Count; i++) if (Items[i].Source != "invite" && (all ? (category == "todas" || Items[i].Category == category) : ids.Contains(Items[i].Id))) Items[i] = Items[i] with { Read = true };
                return Task.FromResult<(long, string)>((200, "{}"));
            }
            if (path.StartsWith("/api/v1/social/groups/invites/", StringComparison.Ordinal))
            {
                var id = Guid.Parse(path.Split('/')[^2]); if (Full && path.EndsWith("/accept", StringComparison.Ordinal)) return Task.FromResult<(long, string)>((409, "{\"error\":\"group_full\"}"));
                Items.RemoveAll(i => i.Source == "invite" && i.Id == id); return Task.FromResult<(long, string)>((200, "{}"));
            }
            return Task.FromResult<(long, string)>((404, "{}"));
        }
    }

    public override async void _Ready()
    {
        try
        {
            CenterView.Now = () => Now; var server = new Server(); var said = new List<string>(); var opened = new List<CenterEntry>();
            var invite = Guid.NewGuid(); var notice = Guid.NewGuid();
            server.Items.Add(new Item("invite", invite, "convites", "group_invite", "Convite para o grupo", "Construtores LV · convidado por chefe", 2, Actions: ["accept", "decline"]));
            server.Items.Add(new Item("group", Guid.NewGuid(), "grupos", "group_announcement", "Reunião sábado", "No Mirante às 20h", 30));
            server.Items.Add(new Item("property", notice, "sistema", "sold", "Sua ilha foi vendida", "Comprador: Bruno", 180));
            server.Items.Add(new Item("group", Guid.NewGuid(), "grupos", "group_event", "Festa de inauguração", "", 3000, Read: true));

            var row = new HBoxContainer { Name = "Row" }; AddChild(row); var overlay = new Control { Name = "Overlay" }; overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); overlay.MouseFilter = MouseFilterEnum.Ignore; AddChild(overlay);
            var center = new NotificationCenterUi { Name = "Center", PollEvery = 3600 }; center.UseTransportForTesting(server.Handle); AddChild(center); center.AttachTo(row, overlay); center.Said = text => said.Add(text); center.Open = entry => opened.Add(entry);
            await Frames(3);

            // --- the counter on the bell ------------------------------------------------------------------------------------------------------
            Require(center.BadgeTextForTesting == "", "no number before the server was asked"); await center.RefreshCountAsync(); await Frames(2);
            Require(center.BadgeTextForTesting == "3" && center.Count == 3, $"the bell shows the unread ones: an invitation, a notice and a sale (got {center.BadgeTextForTesting})");

            // --- the panel ---------------------------------------------------------------------------------------------------------------------
            center.OpenPanel(); await Frames(10);
            Require(center.PanelOpen && center.View.RowsForTesting == 4, $"the panel lists everything (got {center.View.RowsForTesting})");
            var text = center.View.TextForTesting;
            Require(text.Contains("Convite para o grupo") && text.Contains("Construtores LV · convidado por chefe") && text.Contains("há 2 minutos") && text.Contains("há 3 horas") && text.Contains("há 2 dias") && text.Contains("3 não lidas"), $"titles, bodies and relative times (got {text})");
            Require(center.View.HasButtonForTesting("Aceitar") && center.View.HasButtonForTesting("Recusar"), "an invitation has accept and refuse buttons");
            Require(string.Join(",", center.View.ItemsForTesting.Select(i => i.Title)) == "Convite para o grupo,Reunião sábado,Sua ilha foi vendida,Festa de inauguração", "newest first");
            var panel = overlay.GetChildren().OfType<PanelContainer>().First(); var view = GetViewport().GetVisibleRect().Size;
            Require(panel.Size.X <= 360 && panel.Size.Y <= 520 && panel.GlobalPosition.X + panel.Size.X <= view.X && panel.GlobalPosition.Y >= 38 && panel.GlobalPosition.Y + panel.Size.Y <= view.Y, $"the panel hangs below the bar at the right edge and fits the screen (size {panel.Size}, at {panel.GlobalPosition}, screen {view})");

            if (OS.GetEnvironment("LIFEVERSE_SHOT") is { Length: > 0 } shot) { GetWindow().Size = new Vector2I(1536, 1024); await Frames(8); GetViewport().GetTexture().GetImage().SavePng(shot); }

            // --- tabs --------------------------------------------------------------------------------------------------------------------------
            center.View.PressForTesting("Convites (1)"); await Frames(8);
            Require(center.View.TabForTesting == "convites" && center.View.RowsForTesting == 1 && server.Calls.Last().Contains("category=convites"), "the Convites tab asks the server for invitations only");
            center.View.PressForTesting("Grupos (1)"); await Frames(8); Require(center.View.RowsForTesting == 2 && server.Calls.Last().Contains("category=grupos"), "the Grupos tab");
            center.View.PressForTesting("Sistema (1)"); await Frames(8); Require(center.View.RowsForTesting == 1, "the Sistema tab");
            center.View.PressForTesting("Todas"); await Frames(8);

            // --- answering an invitation -------------------------------------------------------------------------------------------------------
            server.Full = true; center.View.PressForTesting("Aceitar"); await Frames(12);
            Require(said.Last() == "O grupo está cheio." && center.View.RowsForTesting == 4, $"the server refused: the person is told in words and the invitation stays (said {said.Last()})");
            server.Full = false; center.View.PressForTesting("Aceitar"); await Frames(12);
            Require(server.Calls.Any(c => c.EndsWith($"/invites/{invite}/accept", StringComparison.Ordinal)) && said.Last() == "Você entrou em Construtores LV." && center.View.RowsForTesting == 3 && center.Count == 2, $"accepting joins, drops the line and the bell goes down (rows {center.View.RowsForTesting}, bell {center.Count}, said {said.Last()})");
            server.Items.Add(new Item("invite", Guid.NewGuid(), "convites", "group_invite", "Convite para o grupo", "Outro grupo · convidado por ana", 1, Actions: ["accept", "decline"])); await center.RefreshCountAsync(); await Frames(8);
            center.View.PressForTesting("Recusar"); await Frames(12); Require(said.Last() == "Convite recusado." && center.View.RowsForTesting == 3, "refusing drops the line");

            // --- reading ---------------------------------------------------------------------------------------------------------------------
            var announcement = center.View.ItemsForTesting.First(i => i.Source == "group"); await center.View.ClickForTesting(announcement); await Frames(8);
            Require(opened.Count == 1 && opened[0].Title == "Reunião sábado" && center.View.ItemsForTesting.First(i => i.Title == "Reunião sábado").Read && center.Count == 1, $"a click marks it read and opens its place (bell {center.Count})");
            await center.View.MarkAllReadAsync(); await Frames(8); Require(center.Count == 0 && center.BadgeTextForTesting == "" && server.Calls.Any(c => c == "POST /api/v1/notifications/center/read"), "mark all as read clears the bell");
            center.ClosePanel(); await Frames(2); Require(!center.PanelOpen, "the panel closes");

            // --- paging, the full window, search -------------------------------------------------------------------------------------------------
            for (var i = 0; i < 24; i++) server.Items.Add(new Item("property", Guid.NewGuid(), "sistema", "sold", $"Aviso {i:00}", "", 10 + i, Read: true));
            center.OpenWindow(); await Frames(12); Require(center.WindowOpen && center.WindowView!.RowsForTesting == 20 && center.WindowView.MoreVisibleForTesting, "the full center loads a page of 20 and offers more");
            await center.WindowView!.LoadMoreAsync(); await Frames(6); Require(center.WindowView.RowsForTesting >= 25 && !center.WindowView.MoreVisibleForTesting, $"the next page is added (rows {center.WindowView.RowsForTesting})");
            center.WindowView.SearchForTesting("aviso 07"); await Frames(10); Require(center.WindowView.RowsForTesting == 1 && server.Calls.Last().Contains("q=aviso%2007"), "search asks the server");
            center.WindowView.SearchForTesting("zzzz"); await Frames(10); Require(center.WindowView.RowsForTesting == 0 && center.WindowView.NoteForTesting == "Nenhuma notificação.", "an empty result says so");
            center.WindowView.SearchForTesting(""); await Frames(10);

            // --- trouble ---------------------------------------------------------------------------------------------------------------------------
            server.Down = true; var before = center.Count; await center.RefreshCountAsync(); await Frames(2); Require(center.Count == before, "no connection leaves the bell as it was");
            server.Items.Clear(); server.Down = false; center.OpenPanel(); await Frames(10); Require(center.View.NoteForTesting == "Nenhuma notificação.", "an empty center says so"); center.ClosePanel();
            for (var i = 0; i < 120; i++) server.Items.Add(new Item("property", Guid.NewGuid(), "sistema", "sold", $"N{i}", "", i)); await center.RefreshCountAsync(); await Frames(2);
            Require(center.BadgeTextForTesting == "99+", $"more than 99 shows 99+ (got {center.BadgeTextForTesting})");

            // which kinds pop up as a toast
            NotificationPrefs.Path = "user://notification_prefs_smoke.cfg"; DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(NotificationPrefs.Path));
            var liveNotice = "{\"type\":\"group_announcement\",\"data\":{}}"; var liveInvite = "{\"type\":\"group_invite\",\"data\":{}}"; var liveFriend = "{\"type\":\"friend_request\",\"data\":{}}";
            Require(NotificationPrefs.AllowsToast(liveNotice) && NotificationPrefs.AllowsToast(liveInvite) && NotificationPrefs.AllowsToast(liveFriend) && NotificationPrefs.AllowsToast("not json"), "everything pops up by default");
            center.OpenSettings(); await Frames(6); var settings = center.SettingsForTesting!; var boxes = settings.FindChildren("*", "CheckBox", true, false).OfType<CheckBox>().ToList();
            Require(boxes.Count == 3 && boxes.All(b => b.ButtonPressed), "the settings list the three kinds, all on"); boxes.First(b => b.Name == "grupos").ButtonPressed = false; boxes.First(b => b.Name == "sistema").ButtonPressed = false;
            Require(!NotificationPrefs.AllowsToast(liveNotice) && NotificationPrefs.AllowsToast(liveInvite) && !NotificationPrefs.AllowsToast(liveFriend), "muting a kind stops only its toasts (and an liveInvite is not a group notice)");
            var fits = settings.Size; Require(fits.Y > 80 && fits.Y <= GetViewport().GetVisibleRect().Size.Y, "the settings dialog fits the screen"); NotificationPrefs.SetToasts("grupos", true); NotificationPrefs.SetToasts("sistema", true);
            GD.Print("event=notification_center_smoke result=passed"); GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=notification_center_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
