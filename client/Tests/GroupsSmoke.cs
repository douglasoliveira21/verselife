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
            var chatJson = "[]"; var annJson = "[]"; var evJson = "[]"; var noticeJson = "{\"unread\":0,\"items\":[]}"; var mutedJson = "null"; var island = Guid.NewGuid();
            var landsJson = "{}"; var availJson = "[]"; var grantsJson = "[]"; var parcel = Guid.NewGuid(); var islandId = Guid.NewGuid(); var friends = Guid.NewGuid();
            var treasuryJson = "{}"; var ledgerJson = "[]"; var productsJson = "[]"; var storesJson = "[]"; var inventoryJson = "{}";
            var invitedTo = Guid.NewGuid(); var invite = Guid.NewGuid(); var pendingGroup = Guid.NewGuid(); var auditJson = "[]";
            var role = "moderator"; var perms = "[\"kick\",\"mute\"]"; var rank = 40; var leader = false; var memberRank = 0;
            window.Transport = (method, path, body) =>
            {
                requests.Add($"{method} {path} {body}");
                if (path == "/mine") return (200, $"[{{\"id\":\"{friends}\",\"name\":\"Amigos\",\"tag\":\"\",\"description\":\"\",\"accentColor\":\"#12F2E0\",\"privacy\":\"public\",\"joinPolicy\":\"open\",\"members\":3,\"memberLimit\":500,\"isMember\":true}},{{\"id\":\"{group}\",\"name\":\"Clube\",\"tag\":\"CLB\",\"description\":\"d\",\"accentColor\":\"#12F2E0\",\"privacy\":\"public\",\"joinPolicy\":\"request\",\"members\":2,\"memberLimit\":500,\"isMember\":true}}]");
                if (path == $"/{group}") return (200, $"{{\"card\":{{\"id\":\"{group}\",\"name\":\"Clube\",\"tag\":\"CLB\",\"description\":\"d\",\"accentColor\":\"#12F2E0\",\"privacy\":\"public\",\"joinPolicy\":\"request\",\"members\":2,\"memberLimit\":500,\"isMember\":true,\"emblem\":\"star\",\"imageUrl\":\"https://x.test/a.png\"}},\"myRole\":\"{role}\",\"myRank\":{rank},\"myPermissions\":{perms},\"isLeader\":{(leader ? "true" : "false")},\"leaderName\":\"dono\",\"mutedUntil\":{mutedJson},\"notificationsMuted\":false}}");
                if (path == $"/{group}/members") return (200, $"[{{\"userId\":\"{other}\",\"username\":\"ana\",\"roleId\":\"{roleMember}\",\"roleName\":\"Membro\",\"rank\":{memberRank}}},{{\"userId\":\"{me}\",\"username\":\"eu\",\"roleId\":\"{roleMod}\",\"roleName\":\"Moderador\",\"rank\":40}}]");
                if (path == $"/{group}/roles") return (200, $"[{{\"id\":\"{roleOwner}\",\"name\":\"Líder\",\"systemKey\":\"owner\",\"rank\":1000,\"permissions\":[],\"members\":1,\"canEdit\":false}},{{\"id\":\"{roleMod}\",\"name\":\"Moderador\",\"systemKey\":\"moderator\",\"rank\":40,\"permissions\":[\"kick\"],\"members\":1,\"canEdit\":false}},{{\"id\":\"{roleMember}\",\"name\":\"Membro\",\"systemKey\":\"member\",\"rank\":0,\"permissions\":[],\"members\":1,\"canEdit\":false}}]");
                if (path.StartsWith($"/search")) return (200, $"[{{\"id\":\"{group}\",\"name\":\"Clube\",\"tag\":\"\",\"description\":\"\",\"accentColor\":\"#12F2E0\",\"privacy\":\"public\",\"joinPolicy\":\"request\",\"members\":2,\"memberLimit\":500,\"isMember\":false}},{{\"id\":\"{pendingGroup}\",\"name\":\"Clube do Pedido\",\"tag\":\"\",\"description\":\"\",\"accentColor\":\"#12F2E0\",\"privacy\":\"public\",\"joinPolicy\":\"request\",\"members\":4,\"memberLimit\":500,\"isMember\":false,\"requestPending\":true}}]");
                if (path == "/api/v1/social/groups/invites") return (200, $"[{{\"inviteId\":\"{invite}\",\"groupId\":\"{invitedTo}\",\"groupName\":\"Outro Clube\",\"fromUserId\":\"{other}\",\"fromUsername\":\"bia\",\"at\":\"2026-11-05T12:00:00Z\"}}]");
                if (path.StartsWith($"/{group}/audit?take=100&before=")) return (200, "[{\"id\":\"" + Guid.NewGuid() + "\",\"at\":\"2026-01-01T00:00:00Z\",\"action\":\"group_created\",\"actor\":\"dono\",\"target\":null}]");
                if (path == $"/{group}/audit?take=100") return (200, auditJson);
                if (path == $"/{group}/treasury") return (200, treasuryJson);
                if (path == $"/{group}/treasury/ledger?take=50") return (200, ledgerJson);
                if (path == $"/{group}/products") return (200, productsJson);
                if (path == $"/{group}/stores") return (200, storesJson);
                if (path == "/api/v1/inventory/items?page=1&pageSize=100") return (200, inventoryJson);
                if (path == $"/{group}/lands") return (200, landsJson);
                if (path == $"/{group}/lands/available") return (200, availJson);
                if (path == $"/api/v1/worlds/islands/{islandId}/land/parcels/{parcel}/group-access") return (200, grantsJson);
                if (path == $"/{group}/chat?take=100") return (200, chatJson);
                if (path == $"/{group}/announcements") return (200, annJson);
                if (path == $"/{group}/events") return (200, evJson);
                if (path == "/notifications?take=50") return (200, noticeJson);
                if (path.StartsWith("/api/v1/discovery/islands")) return (200, $"{{\"items\":[{{\"id\":\"{island}\",\"name\":\"Praça\"}}]}}");
                if (path.EndsWith("/audit?take=100") || path.EndsWith("/requests") || path.EndsWith("/bans")) return (200, "[]");
                return (200, "{}");
            };
            await Frames(2);

            window.Open();
            Require(window.Invites.Count == 1 && !window.InviteButtonEnabledForTesting("accept"), "an invitation should load and wait for a selection");
            window.SelectInviteForTesting(0); window.AnswerInvite(true); Require(requests.Any(r => r.StartsWith("Post ") && r.Contains($"/groups/invites/{invite}/accept")), "accepting an invitation is sent");
            Require(window.Visible && window.Mine.Count == 2, "opening should load my groups");
            window.SelectMine(1);
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

            // chat, announcements, events and notices (as the leader)
            var msg1 = Guid.NewGuid(); var msg2 = Guid.NewGuid(); var ann = Guid.NewGuid(); var ev = Guid.NewGuid(); var notice = Guid.NewGuid(); var other2 = Guid.NewGuid();
            var future = DateTimeOffset.UtcNow.AddHours(2).ToString("o");
            chatJson = $"[{{\"id\":\"{msg1}\",\"userId\":\"{other}\",\"username\":\"ana\",\"content\":\"oi\",\"at\":\"{future}\"}},{{\"id\":\"{msg2}\",\"userId\":\"{me}\",\"username\":\"eu\",\"content\":\"tudo bem\",\"at\":\"{future}\"}}]";
            annJson = $"[{{\"id\":\"{ann}\",\"title\":\"Reunião\",\"body\":\"hoje\",\"pinned\":true,\"author\":\"dono\",\"at\":\"{future}\"}}]";
            evJson = $"[{{\"id\":\"{ev}\",\"name\":\"Caça\",\"description\":\"d\",\"islandName\":\"Praça\",\"startsAt\":\"{future}\",\"endsAt\":\"{future}\",\"capacity\":5,\"attending\":1,\"joined\":false,\"organizerUserId\":\"{other}\"}}]";
            noticeJson = $"{{\"unread\":1,\"items\":[{{\"id\":\"{notice}\",\"groupId\":\"{group}\",\"groupName\":\"Clube\",\"type\":\"group_announcement\",\"title\":\"Reunião\",\"body\":\"x\",\"at\":\"{future}\",\"read\":false}}]}}";
            window.SetUserForTesting(me); window.OpenGroup(group); window.LoadNotices();
            Require(window.Chat.Count == 2 && window.Announcements.Count == 1 && window.Events.Count == 1 && window.Notices.Count == 1, "chat, announcements, events and notices should load");
            Require(window.CommButtonEnabledForTesting("send") && window.CommButtonEnabledForTesting("post") && window.CommButtonEnabledForTesting("evCreate"), "the leader can talk, announce and make events");
            window.SetChatInputForTesting("ola grupo"); window.SendChat();
            Require(requests.Any(r => r.StartsWith("Post ") && r.Contains($"/{group}/chat") && r.Contains("\"content\":\"ola grupo\"") && r.Contains("clientMessageId")), "chat send wrong");
            window.SelectChatForTesting(0); Require(window.CommButtonEnabledForTesting("chatDelete"), "the leader can delete a message of someone else");
            window.DeleteChatSelected(); Require(requests.Any(r => r.StartsWith("Delete ") && r.Contains($"/chat/{msg1}")), "chat delete wrong");
            window.SelectAnnouncementForTesting(0); Require(window.CommButtonEnabledForTesting("pin") && window.AnnouncementBodyForTesting.Contains("hoje"), "announcement should show");
            window.TogglePinned(); Require(requests.Any(r => r.StartsWith("Put ") && r.Contains($"/announcements/{ann}") && r.Contains("\"pinned\":false")), "unpin wrong");
            window.SetAnnouncementFormForTesting("Aviso", "corpo", true); window.PostAnnouncement();
            Require(requests.Any(r => r.StartsWith("Post ") && r.Contains("/announcements") && r.Contains("\"title\":\"Aviso\"") && r.Contains("\"pinned\":true")), "announcement post wrong");
            window.SelectEventForTesting(0); Require(window.EventJoinTextForTesting == "Participar" && window.CommButtonEnabledForTesting("evCancel"), "event join/cancel state wrong");
            window.ToggleEvent(); Require(requests.Any(r => r.StartsWith("Post ") && r.Contains($"/events/{ev}/join")), "event join wrong");
            window.SetEventFormForTesting("Festa", "05/11/2030 20:00", 90, 10); window.CreateEvent();
            Require(requests.Any(r => r.StartsWith("Post ") && r.Contains("/events {") && r.Contains("\"name\":\"Festa\"") && r.Contains("\"capacity\":10") && r.Contains(island.ToString())), "event create wrong");
            window.SetEventFormForTesting("Festa", "amanha", 90, 10); var before = requests.Count; window.CreateEvent(); Require(requests.Count == before && window.StatusText.Contains("Data inválida"), "a bad date must not be sent");
            window.MarkNoticesRead(); Require(requests.Any(r => r.StartsWith("Post ") && r.Contains("/notifications/read")), "mark read wrong");
            // live: the open chat grows, another group counts unread
            window.OnLiveEvent($"{{\"type\":\"group_message\",\"data\":{{\"groupId\":\"{group}\",\"id\":\"{Guid.NewGuid()}\",\"userId\":\"{other}\",\"username\":\"ana\",\"content\":\"ao vivo\",\"at\":\"{future}\"}}}}");
            Require(window.Chat.Count == 3 && window.Chat[^1].Text == "ao vivo", "a live message should join the open chat");
            window.OnLiveEvent($"{{\"type\":\"group_message\",\"data\":{{\"groupId\":\"{other2}\",\"id\":\"{Guid.NewGuid()}\",\"content\":\"x\"}}}}");
            Require(window.UnreadChatForTesting(other2) == 1, "a message for another group should count as unread");
            before = requests.Count; window.OnLiveEvent($"{{\"type\":\"group_announcement\",\"data\":{{\"groupId\":\"{group}\"}}}}"); Require(requests.Count > before, "a new announcement should reload the list");
            window.OnLiveEvent("not json"); window.OnLiveEvent("{}");

            // land of the group (as the leader)
            var spare = Guid.NewGuid(); var grantId = Guid.NewGuid();
            landsJson = $"{{\"lands\":[{{\"parcelId\":\"{parcel}\",\"islandId\":\"{islandId}\",\"islandName\":\"Ilha\",\"name\":\"Praca\",\"area\":32,\"maxObjects\":100,\"accessMode\":\"private\",\"buildMode\":\"owner\",\"forSale\":false}}],\"canManage\":true,\"canBuild\":true,\"canRelease\":true,\"limit\":50}}";
            availJson = $"[{{\"parcelId\":\"{spare}\",\"name\":\"Casinha\",\"islandName\":\"Ilha\"}}]";
            grantsJson = $"[{{\"id\":\"{grantId}\",\"groupId\":\"{friends}\",\"groupName\":\"Amigos\",\"kind\":\"allow\"}}]";
            window.OpenGroup(group);
            Require(window.Lands.Count == 1 && window.Lands[0].Name == "Praca" && window.LandNoteForTesting.Contains("Terrenos do grupo"), "the land of the group should load");
            Require(window.LandButtonEnabledForTesting("donate") && !window.LandButtonEnabledForTesting("release") && !window.LandButtonEnabledForTesting("grant"), "donating is on, the rest wait for a selection");
            window.SelectLandForTesting(0);
            Require(window.Grants.Count == 1 && window.LandButtonEnabledForTesting("release") && window.LandButtonEnabledForTesting("grant") && !window.LandButtonEnabledForTesting("revoke"), "selecting land shows its grants and turns the buttons on");
            window.DonateLand(); Require(requests.Any(r => r.StartsWith("Post ") && r.Contains($"/{group}/lands {{") && r.Contains(spare.ToString())), "donate wrong");
            window.SelectLandForTesting(0); window.ChooseHeirForTesting("ana"); window.ReleaseLand(); Require(requests.Any(r => r.StartsWith("Post ") && r.Contains($"/lands/{parcel}/release") && r.Contains(other.ToString())), "release wrong");
            window.SelectLandForTesting(0); window.ChooseGrantGroupForTesting("Amigos", true); window.GrantAccess();
            Require(requests.Any(r => r.StartsWith("Post ") && r.Contains($"/land/parcels/{parcel}/group-access") && r.Contains(friends.ToString()) && r.Contains("\"kind\":\"build\"")), "grant wrong");
            window.SelectGrantForTesting(0); Require(window.LandButtonEnabledForTesting("revoke"), "revoke should turn on");
            window.RevokeAccess(); Require(requests.Any(r => r.StartsWith("Delete ") && r.Contains($"/group-access/{grantId}")), "revoke wrong");

            // the chest and the shop (as the leader)
            var prod = Guid.NewGuid(); var assetId = Guid.NewGuid(); var storeId = Guid.NewGuid();
            treasuryJson = "{\"canView\":true,\"canTake\":true,\"canFreeze\":true,\"balance\":1500,\"totalIn\":2000,\"totalOut\":500,\"frozen\":false,\"myContributed\":300,\"top\":[]}";
            ledgerJson = $"[{{\"id\":\"{Guid.NewGuid()}\",\"type\":\"contribution\",\"amount\":300,\"balanceAfter\":1500,\"actor\":\"eu\",\"target\":null,\"note\":\"festa\",\"at\":\"{future}\"}}]";
            productsJson = $"[{{\"id\":\"{prod}\",\"name\":\"Cadeira\",\"description\":\"\",\"category\":\"x\",\"priceCoins\":100,\"status\":\"active\",\"publisher\":\"eu\",\"sold\":2}}]";
            storesJson = $"[{{\"id\":\"{storeId}\",\"islandId\":\"{islandId}\",\"islandName\":\"Praca\",\"productId\":\"{prod}\",\"productName\":\"Cadeira\",\"displayName\":\"Banca\"}}]";
            inventoryJson = $"{{\"items\":[{{\"item\":{{\"id\":\"{Guid.NewGuid()}\"}},\"asset\":{{\"id\":\"{assetId}\",\"name\":\"Cadeira Rara\",\"ownerUserId\":\"{me}\"}}}},{{\"item\":{{\"id\":\"{Guid.NewGuid()}\"}},\"asset\":{{\"id\":\"{Guid.NewGuid()}\",\"name\":\"Comprado\",\"ownerUserId\":\"{other}\"}}}}]}}";
            window.OpenGroup(group);
            Require(window.Treasury is { CanView: true, Balance: 1500, CanFreeze: true } && window.Ledger.Count == 1 && window.ChestInfoForTesting.Contains("1.500"), "the chest and its extract should load");
            Require(window.EconomyButtonEnabledForTesting("give") && window.EconomyButtonEnabledForTesting("take") && window.EconomyButtonEnabledForTesting("freeze"), "the leader gives, takes and freezes");
            window.SetGiveForTesting("250", "valeu"); window.Contribute();
            Require(requests.Any(r => r.StartsWith("Post ") && r.Contains($"/{group}/treasury/contribute") && r.Contains("\"amount\":250") && r.Contains("idempotencyKey")), "contribute wrong");
            window.SetGiveForTesting("0", ""); before = requests.Count; window.Contribute(); Require(requests.Count == before, "a zero gift must not be sent");
            window.SetTakeForTesting("100", "reembolso", "ana"); window.Withdraw();
            Require(requests.Any(r => r.StartsWith("Post ") && r.Contains("/treasury/withdraw") && r.Contains("\"amount\":100") && r.Contains(other.ToString()) && r.Contains("reembolso")), "withdraw wrong");
            window.SetFreezeForTesting(true); Require(requests.Any(r => r.StartsWith("Put ") && r.Contains("/treasury/frozen") && r.Contains("\"frozen\":true")), "freeze wrong");
            Require(window.Products.Count == 1 && window.Stores.Count == 1 && window.EconomyButtonEnabledForTesting("publish") && window.EconomyButtonEnabledForTesting("openStore"), "the shop should load with the items of the person");
            window.SetProductFormForTesting("Mesa", "500", ""); window.PublishProduct();
            Require(requests.Any(r => r.StartsWith("Post ") && r.Contains($"/{group}/products {{") && r.Contains("\"name\":\"Mesa\"") && r.Contains("\"priceCoins\":500") && r.Contains(assetId.ToString())), "publish wrong");
            window.SelectProductForTesting(0); Require(window.EconomyButtonEnabledForTesting("reprice") && window.EconomyButtonEnabledForTesting("unpublish"), "an active product can be repriced or taken off");
            window.SetProductFormForTesting("", "", "250"); window.Reprice(); Require(requests.Any(r => r.StartsWith("Put ") && r.Contains($"/products/{prod}") && r.Contains("\"priceCoins\":250")), "reprice wrong");
            window.SelectProductForTesting(0); window.Unpublish(); Require(requests.Any(r => r.StartsWith("Delete ") && r.Contains($"/products/{prod}")), "unpublish wrong");
            window.SetStoreFormForTesting("Banca 2"); window.OpenStore();
            Require(requests.Any(r => r.StartsWith("Post ") && r.Contains($"/{group}/stores {{") && r.Contains("\"displayName\":\"Banca 2\"") && r.Contains(prod.ToString()) && r.Contains(island.ToString())), "open store wrong");
            window.SelectStoreForTesting(0); Require(window.EconomyButtonEnabledForTesting("closeStore"), "a store can be closed");
            window.CloseStore(); Require(requests.Any(r => r.StartsWith("Delete ") && r.Contains($"/stores/{storeId}")), "close store wrong");

            // overview, identity and the paged history (as the leader)
            auditJson = "[" + string.Join(",", Enumerable.Range(0, 100).Select(i => "{\"id\":\"" + Guid.NewGuid() + "\",\"at\":\"2026-11-0" + (1 + i % 5) + "T10:00:00Z\",\"action\":\"member_joined\",\"actor\":\"u" + i + "\",\"target\":null}")) + "]";
            window.OpenGroup(group);
            Require(window.PanelTextForTesting.Contains("Estrela · Clube") && window.PanelTextForTesting.Contains("Membros: 2/500") && window.PanelTextForTesting.Contains("Terrenos: 1") && window.PanelTextForTesting.Contains("Cofre: 1.500"), $"the overview panel summarises the group: {window.PanelTextForTesting}");
            Require(window.OverviewTextForTesting.Contains("Emblema: Estrela") && window.SettingsEmblemForTesting == 1 && window.SettingsImageForTesting == string.Empty, "the identity of the group is shown and legacy external images are not reused");
            window.SetSettingsIdentityForTesting(2, ""); window.SetSettingsForTesting("Clube", "d"); window.SaveSettings();
            Require(requests.Any(r => r.StartsWith("Put ") && r.Contains($"/{group} {{") && r.Contains("\"emblem\":\"hammer\"") && r.Contains("\"imageUrl\":\"\"")), "saving the identity is sent");
            window.OpenGroup(group);
            Require(window.AuditCountForTesting == 100 && window.AuditMoreVisibleForTesting, "the first page of the history has a button for more");
            window.LoadMoreAuditForTesting(); Require(window.AuditCountForTesting == 101 && !window.AuditMoreVisibleForTesting, "older entries are added to the list");

            // a plain member who is muted
            treasuryJson = "{\"canView\":false,\"canTake\":false,\"canFreeze\":false,\"frozen\":false,\"myContributed\":300}";
            landsJson = landsJson.Replace("\"canManage\":true", "\"canManage\":false").Replace("\"canRelease\":true", "\"canRelease\":false"); availJson = "[]";
            role = "member"; rank = 0; leader = false; perms = "[]"; mutedJson = $"\"{future}\"";
            window.OpenGroup(group);
            Require(!window.CommButtonEnabledForTesting("send") && !window.CommButtonEnabledForTesting("chatInput") && window.ChatPlaceholderForTesting.Contains("silenciado"), "a muted member cannot type");
            Require(!window.CommButtonEnabledForTesting("post") && !window.CommButtonEnabledForTesting("evCreate"), "a plain member cannot announce or make events");
            window.SelectChatForTesting(0); Require(!window.CommButtonEnabledForTesting("chatDelete"), "a plain member cannot delete a message of someone else");
            window.SelectChatForTesting(1); Require(window.CommButtonEnabledForTesting("chatDelete"), "but can delete their own");
            Require(window.EconomyButtonEnabledForTesting("give") && !window.EconomyButtonEnabledForTesting("take") && !window.EconomyButtonEnabledForTesting("freeze") && !window.EconomyButtonEnabledForTesting("publish") && window.ChestInfoForTesting.Contains("Você já deu"), "a plain member gives but runs nothing");
            Require(!window.LandButtonEnabledForTesting("donate") && !window.LandButtonEnabledForTesting("release") && !window.LandButtonEnabledForTesting("grant"), "a plain member runs no land");
            mutedJson = "null"; window.OpenGroup(group); Require(window.CommButtonEnabledForTesting("send"), "talking again once the mute ends");

            // searching and joining
            window.Search("clu");
            Require(window.Found.Count == 2, "search should fill");
            window.SelectFoundForTesting(0);
            Require(window.ButtonEnabledForTesting("join") && window.JoinTextForTesting.Contains("Pedir"), "a request-policy group offers to ask");
            window.JoinSelected();
            window.SelectFoundForTesting(1); Require(window.CancelRequestEnabledForTesting && !window.ButtonEnabledForTesting("join"), "a pending request can be cancelled, not repeated");
            window.CancelRequestSelected(); Require(requests.Any(r => r.StartsWith("Delete ") && r.Contains($"/{pendingGroup}/join")), "cancelling a request is sent");
            window.SelectFoundForTesting(0);
            Require(requests.Any(r => r.Contains($"/{group}/join")), "join request wrong");

            // creating
            window.SetCreateForTesting("Novo Clube", "NVO", "bom", 0, 0); window.SetCreateIdentityForTesting(1, "asset://11111111-1111-1111-1111-111111111111");
            window.Create();
            Require(requests.Any(r => r.StartsWith("Post  ") && r.Contains("\"name\":\"Novo Clube\"") && r.Contains("\"joinPolicy\":\"open\"") && r.Contains("\"privacy\":\"public\"") && r.Contains("\"emblem\":\"star\"") && r.Contains("\"imageUrl\":\"asset://11111111-1111-1111-1111-111111111111\"")), $"create wrong: {requests.LastOrDefault(r => r.Contains("Novo"))}");

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
