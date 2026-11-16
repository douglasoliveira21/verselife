using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>The profile window: my profile is editable and saved with what I typed, another person profile is read-only, the type of account and the marriage are shown, groups can be hidden, and a proposal can be answered.</summary>
public partial class ProfileSmoke : Node3D
{
    private static readonly Guid Me = Guid.Parse("11111111-1111-1111-1111-111111111111"), Other = Guid.Parse("22222222-2222-2222-2222-222222222222"), GroupId = Guid.Parse("33333333-3333-3333-3333-333333333333"), Proposal = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Parcel1 = Guid.Parse("55555555-5555-5555-5555-555555555555"), Island1 = Guid.Parse("66666666-6666-6666-6666-666666666666"), ItemA = Guid.Parse("77777777-7777-7777-7777-777777777777"), ItemB = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private static readonly List<(Guid Id, bool Main)> Gal = []; private static bool HasCover;
    private static bool HideStats;
    private static string Profile(Guid id, bool self, string account = "standard", string? spouse = null, bool privateProfile = false, string bio = "Construtora", string birthday = "07/09") => JsonSerializer.Serialize(new
    {
        id, username = self ? "ana" : "bia", displayName = self ? "Ana Souza" : "Bia Lima", isSelf = self, privateProfile, joinedAt = "2026-01-15T10:00:00Z", characterAgeYears = 1, accountType = account, donorSince = account == "donor" ? "2026-06-01T00:00:00Z" : null, presence = "online",
        bio = privateProfile ? "" : bio, firstLifeBio = privateProfile ? "" : "Recife", interests = privateProfile ? "" : "música", hasPhoto = Gal.Any(g => g.Main) && !privateProfile, gallery = privateProfile ? Array.Empty<object>() : Gal.Select(g => (object)new { id = g.Id, main = g.Main }).ToArray(), hasCover = HasCover && !privateProfile,
        favorites = privateProfile ? Array.Empty<object>() : [new { kind = "island", label = "Ilha", id = Island1, title = "Ilha Esmeralda", subtitle = "Social", islandId = Island1 }],
        achievements = privateProfile ? Array.Empty<object>() : [new { key = "first_parcel", title = "Primeiro terreno", description = "Tornou-se dono de um terreno.", icon = "home", unlockedAt = "2025-01-10T00:00:00Z" }],
        stats = privateProfile || HideStats ? null : new { joinedAt = "2026-01-15T10:00:00Z", daysMember = 400, groups = 1, parcels = 1, creations = 0, events = 2, pictures = 0 }, photoUrl = (string?)null, birthday = privateProfile ? null : birthday,
        married = spouse is not null, marriage = spouse is null ? null : new { id = Guid.NewGuid(), spouseId = Other, spouseName = spouse, since = "2026-08-20T12:00:00Z" },
        groups = privateProfile ? Array.Empty<object>() : [new { id = GroupId, name = "Os Exploradores", tag = "EXP", role = "member", color = "#12F2E0", shown = true }],
        parcels = privateProfile ? Array.Empty<object>() : [new { id = Parcel1, islandId = Island1, islandName = "Ilha Esmeralda", name = "Casa da Bia", category = "residencial", area = 400, code = "L-03", shown = true }],
        settings = self ? new { hideGroups = false, hideMarriage = false, privateProfile, hideParcels = false, showFavorites = false, hideAnniversary = false, hideStats = false, hideAchievements = false } : null
    });

    public override async void _Ready()
    {
        try
        {
            var opened = new List<string>(); var window = new ProfileWindow { Name = "ProfileWindow", MyIdOverride = () => Me }; AddChild(window);
            var calls = new List<(string Method, string Path, string? Body)>(); var profiles = new Dictionary<string, string>(); var mine = "{\"current\":null,\"incoming\":[],\"outgoing\":[]}";
            var relation = "none"; var teleportError = false; var parcelLimit = false; var galleryFull = false; Gal.Clear(); HasCover = false; var fetched = new List<string>();
            var feedPosts = new List<(Guid Id, string Text, bool Friends, bool Image)>(); var feedMode = "ok"; var feedAchievement = false; var feedMore = false;
            object FeedItem((Guid Id, string Text, bool Friends, bool Image) p, int minutes) => new { kind = "post", key = p.Id, at = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero).AddMinutes(-minutes), title = "", text = p.Text, icon = (string?)null, postId = p.Id, hasImage = p.Image, visibility = p.Friends ? "friends" : "public" };
            object[] Ach() => feedAchievement ? [new { kind = "achievement", key = "community", at = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), title = "Faz parte de uma comunidade", text = "Entrou em um grupo.", icon = "people", postId = (Guid?)null, hasImage = false, visibility = (string?)null }] : [];
            string FeedPage(bool self, bool more, params object[] extra) => System.Text.Json.JsonSerializer.Serialize(new { items = feedPosts.Select((p, i) => FeedItem(p, i)).Concat(extra).ToList(), more, hidden = (string?)null, canPost = self });
            window.UseTransportForTesting((method, path, body) =>
            {
                calls.Add((method, path, body));
                if (method == "GET" && path == $"/api/v1/users/{Me}/profile/full") return Task.FromResult<(long, string)>((200, profiles.GetValueOrDefault("me", Profile(Me, true))));
                if (method == "GET" && path == $"/api/v1/users/{Other}/profile/full") return Task.FromResult<(long, string)>((200, profiles.GetValueOrDefault("other", Profile(Other, false))));
                if (method == "GET" && path.StartsWith("/api/v1/users/profile?name=bia")) return Task.FromResult<(long, string)>((200, Profile(Other, false)));
                if (method == "GET" && path.StartsWith("/api/v1/users/profile?name=")) return Task.FromResult<(long, string)>((404, "{}"));
                if (method == "PUT" && path == "/api/v1/users/me/profile/details")
                {
                    var sent = JsonNode.Parse(body!)!.AsObject();
                    if (sent["bio"]?.GetValue<string>() == "ruim") return Task.FromResult<(long, string)>((400, "{\"error\":\"too_long\",\"message\":\"O texto passou do limite.\"}"));
                    return Task.FromResult<(long, string)>((200, Profile(Me, true, bio: sent["bio"]!.GetValue<string>(), birthday: "12/03")));
                }
                if (method == "PUT" && path.Contains("/groups/")) return Task.FromResult<(long, string)>((204, ""));
                if (method == "GET" && path.StartsWith("/api/v1/social/people?q=")) return Task.FromResult<(long, string)>((200, "[{\"id\":\"" + Other + "\",\"username\":\"bia\",\"displayName\":\"Bia Lima\",\"relation\":\"" + relation + "\"}]"));
                if (method == "POST" && path == "/api/v1/social/friends/requests") { relation = "pending_out"; return Task.FromResult<(long, string)>((200, "{}")); }
                if (method == "DELETE" && path == $"/api/v1/social/friends/{Other}") { relation = "none"; return Task.FromResult<(long, string)>((204, "")); }
                if (method == "POST" && (path == "/api/v1/social/messages" || path == $"/api/v1/social/blocks/{Other}" || path == "/api/v1/social/reports")) return Task.FromResult<(long, string)>((200, "{}"));
                if (method == "POST" && path == "/api/v1/social/teleports") return Task.FromResult<(long, string)>(teleportError ? (400, "{\"error\":\"not_on_island\"}") : (200, "{}"));
                if (method == "GET" && path.StartsWith("/api/v1/inventory/items")) return Task.FromResult<(long, string)>((200, "{\"items\":[{\"itemId\":\"" + ItemA + "\",\"name\":\"Espada\",\"status\":\"active\",\"ownerRights\":7,\"deleted\":false},{\"itemId\":\"" + ItemB + "\",\"name\":\"Fixa\",\"status\":\"active\",\"ownerRights\":3,\"deleted\":false}]}"));
                if (method == "POST" && path == $"/api/v1/inventory/items/{ItemA}/offers") return Task.FromResult<(long, string)>((201, "{}"));
                if (method == "PUT" && path.StartsWith("/api/v1/users/me/parcels/")) return Task.FromResult<(long, string)>(parcelLimit ? (400, "{\"error\":\"too_many\",\"message\":\"Você pode destacar até 6 terrenos.\"}") : (204, ""));
                if (path.StartsWith("/api/v1/users/me/profile/gallery"))
                {
                    if (method == "POST") { if (galleryFull) return Task.FromResult<(long, string)>((400, "{\"error\":\"gallery_full\",\"message\":\"A galeria aceita até 12 imagens.\"}")); Gal.Add((Guid.NewGuid(), Gal.Count == 0)); }
                    else if (method == "PUT" && path.EndsWith("/order")) { var ids = JsonNode.Parse(body!)!.AsObject()["ids"]!.AsArray().Select(n => Guid.Parse(n!.GetValue<string>())).ToList(); var byId = Gal.ToDictionary(g => g.Id); Gal.Clear(); Gal.AddRange(ids.Select(i => byId[i])); }
                    else if (method == "PUT" && path.EndsWith("/main")) { var id = Guid.Parse(path.Split('/')[^2]); for (var i = 0; i < Gal.Count; i++) Gal[i] = (Gal[i].Id, Gal[i].Id == id); }
                    else if (method == "DELETE") { var id = Guid.Parse(path.Split('/')[^1]); var was = Gal.FirstOrDefault(g => g.Id == id); Gal.RemoveAll(g => g.Id == id); if (was.Main && Gal.Count > 0) Gal[0] = (Gal[0].Id, true); }
                    return Task.FromResult<(long, string)>((200, Profile(Me, true)));
                }
                if (method == "PUT" && path == "/api/v1/users/me/profile/cover") { HasCover = JsonNode.Parse(body!)!.AsObject()["assetId"] is not null; return Task.FromResult<(long, string)>((200, Profile(Me, true))); }
                if (method == "GET" && path.Contains("/profile/feed"))
                {
                    if (feedMode == "error") return Task.FromResult<(long, string)>((500, "{}"));
                    if (feedMode == "private") return Task.FromResult<(long, string)>((200, "{\"items\":[],\"more\":false,\"hidden\":\"private\",\"canPost\":false}"));
                    var self = path.Contains(Me.ToString());
                    if (feedMore && path.Contains("before=")) return Task.FromResult<(long, string)>((200, System.Text.Json.JsonSerializer.Serialize(new { items = new[] { FeedItem((Guid.NewGuid(), "Post antigo", false, false), 500) }, more = false, hidden = (string?)null, canPost = self })));
                    var extra = feedAchievement ? new object[] { new { kind = "achievement", key = "community", at = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), title = "Faz parte de uma comunidade", text = "Entrou em um grupo.", icon = "people", postId = (Guid?)null, hasImage = false, visibility = (string?)null } } : [];
                    return Task.FromResult<(long, string)>((200, FeedPage(self, feedMore, extra)));
                }
                if (method == "POST" && path == "/api/v1/users/me/profile/posts")
                {
                    var sent = JsonNode.Parse(body!)!.AsObject(); if (string.IsNullOrWhiteSpace(sent["text"]?.GetValue<string>()) && sent["galleryImageId"] is null) return Task.FromResult<(long, string)>((400, "{\"error\":\"empty_post\",\"message\":\"Escreva algo ou escolha uma imagem.\"}"));
                    feedPosts.Insert(0, (Guid.NewGuid(), sent["text"]!.GetValue<string>(), sent["visibility"]!.GetValue<string>() == "friends", sent["galleryImageId"] is not null)); return Task.FromResult<(long, string)>((200, FeedPage(true, false, Ach())));
                }
                if (method == "DELETE" && path.Contains("/me/profile/posts/")) { var id = Guid.Parse(path.Split('/')[^1]); feedPosts.RemoveAll(p => p.Id == id); return Task.FromResult<(long, string)>((200, FeedPage(true, false, Ach()))); }
                if (method == "GET" && path == "/api/v1/marriage") return Task.FromResult<(long, string)>((200, mine));
                if (method == "POST" && path == "/api/v1/marriage/propose") return Task.FromResult<(long, string)>((200, "{\"id\":\"" + Guid.NewGuid() + "\",\"status\":\"proposed\"}"));
                if (method == "POST" && path == $"/api/v1/marriage/{Proposal}/accept") { mine = "{\"current\":{\"id\":\"" + Guid.NewGuid() + "\",\"spouseId\":\"" + Other + "\",\"spouseName\":\"Bia Lima\",\"since\":\"2026-11-10T12:00:00Z\"},\"incoming\":[],\"outgoing\":[]}"; profiles["me"] = Profile(Me, true, spouse: "Bia Lima"); return Task.FromResult<(long, string)>((204, "")); }
                if (method == "POST" && path == "/api/v1/marriage/divorce") { mine = "{\"current\":null,\"incoming\":[],\"outgoing\":[]}"; profiles.Remove("me"); return Task.FromResult<(long, string)>((204, "")); }
                return Task.FromResult<(long, string)>((404, "{}"));
            });
            await Frames(3);
            Require(window.TabsForTesting.SequenceEqual(["SOBRE", "FEED", "GRUPOS", "TERRENOS", "FAVORITOS", "CONQUISTAS", "ESTATÍSTICAS", "VIDA REAL", "CASAMENTO"]), "the tabs of the profile");

            // --- mine ------------------------------------------------------------------------------------------------------
            await window.OpenSelfAsync(); await Frames(2);
            Require(window.IsOpen && window.NameForTesting == "Ana Souza", "the profile opens with the name");
            Require(window.AccountForTesting.Contains("Conta padrão"), "a standard account is shown as such");
            Require(window.MarriageForTesting == "Solteiro(a)", "not married is shown");
            Require(window.EditableForTesting && window.SaveVisibleForTesting && !window.ProposeVisibleForTesting, "mine can be edited and saved, and there is no proposal to myself");
            Require(window.BioForTesting == "Construtora" && window.FirstLifeForTesting == "Recife" && window.InterestsForTesting == "música", "the texts come from the server");
            Require(window.GroupRowsForTesting == 1, "my groups are listed");

            window.BioForTesting = "Arquiteta de ilhas"; window.SetHideMarriageForTesting(true);
            Require(await window.SaveAsync(), "saving works");
            var saved = JsonNode.Parse(calls.Last(c => c.Method == "PUT" && c.Path.EndsWith("details")).Body!)!.AsObject();
            Require(saved["bio"]!.GetValue<string>() == "Arquiteta de ilhas", "the text is sent");
            Require(saved["hideMarriage"]!.GetValue<bool>() && !saved["hideGroups"]!.GetValue<bool>(), "what I hide is sent");
            Require(window.StatusForTesting == "Perfil salvo." && window.BioForTesting == "Arquiteta de ilhas", "the saved profile comes back");

            window.BioForTesting = "ruim"; Require(!await window.SaveAsync() && window.StatusForTesting.Contains("passou do limite"), "a refusal shows the server message");
            var puts = calls.Count(c => c.Method == "PUT" && c.Path.EndsWith("details"));
            window.BioForTesting = "x";

            await window.ToggleGroupForTesting(0, false);
            Require(calls.Any(c => c.Method == "PUT" && c.Path == $"/api/v1/users/me/groups/{GroupId}/profile" && c.Body!.Contains("false")), "hiding a group is sent");

            // --- the photo ---------------------------------------------------------------------------------------------------
            var image = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8); image.Fill(new Color("#12F2E0"));
            window.ShowPhoto(image.SavePngToBuffer()); Require(window.PhotoShownForTesting, "a PNG sent by the server becomes the photo");
            window.ShowPhoto([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13]);

            // --- another person ----------------------------------------------------------------------------------------------
            profiles["other"] = Profile(Other, false, account: "donor", spouse: "Caio Rocha", bio: "Viajante");
            await window.OpenByIdAsync(Other); await Frames(2);
            Require(window.NameForTesting == "Bia Lima" && window.BioForTesting == "Viajante", "another profile shows their texts");
            Require(!window.EditableForTesting && !window.SaveVisibleForTesting, "and it cannot be edited");
            Require(window.AccountForTesting.Contains("Doador") && window.AccountForTesting.Contains("06/2026"), "a donor is shown with since when");
            Require(window.MarriageForTesting.Contains("Casado(a) com Caio Rocha") && window.MarriageForTesting.Contains("20/08/2026"), "the marriage is shown with the date");
            Require(window.BirthdayForTesting == "07/09", "the birthday is the creation day the server sent"); Require(window.JoinedForTesting.Contains("15/01/2026") && window.JoinedForTesting.Contains("ano(s) de LifeVerse"), "member since, with how long the character exists");
            Require(window.TitleForTesting == "Perfil" && window.PartnerForTesting == "Caio Rocha" && window.VisibilityForTesting == "Público" && window.PresenceForTesting == "Online", "another person: title, partner, visibility and presence cards");
            Require(!window.SidebarVisibleForTesting, "the navigation column is only for my own profile");
            Require(!window.ProposeVisibleForTesting, "no proposal to somebody who is already married");

            profiles["other"] = Profile(Other, false); await window.OpenByIdAsync(Other);
            Require(window.ProposeVisibleForTesting, "a free person can be proposed to");
            window.SetProposeMessageForTesting("Aceita?"); await window.ProposeAsync();
            Require(calls.Last(c => c.Method == "POST").Body!.Contains(Other.ToString()) && calls.Last(c => c.Method == "POST").Body!.Contains("Aceita?") && window.StatusForTesting.Contains("enviado"), "the proposal goes with its message");

            profiles["other"] = Profile(Other, false, privateProfile: true); await window.OpenByIdAsync(Other);
            Require(window.BioForTesting == "" && window.StatusForTesting.Contains("privado"), "a private profile shows only the name");

            await window.OpenByNameAsync("bia"); Require(window.NameForTesting == "Bia Lima" && window.Viewing == Other, "a profile opens by name");
            await window.OpenByNameAsync("ninguem"); Require(window.StatusForTesting.Contains("Não encontrei"), "an unknown name says so");

            // --- marriage: accept a proposal, then divorce ---------------------------------------------------------------------
            mine = "{\"current\":null,\"incoming\":[{\"id\":\"" + Proposal + "\",\"fromId\":\"" + Other + "\",\"fromName\":\"Bia Lima\",\"message\":\"Oi\",\"createdAt\":\"2026-11-09T10:00:00Z\"}],\"outgoing\":[]}";
            await window.OpenSelfAsync(); await Frames(2);
            Require(window.MarriageRowsForTesting >= 2, "an incoming proposal is listed with its buttons");
            await window.AnswerForTesting(0, "accept"); await Frames(2);
            Require(window.MarriageForTesting.Contains("Casado(a) com Bia Lima"), "after accepting I am married to her");
            await window.DivorceForTesting(); await Frames(2);
            Require(window.MarriageForTesting == "Solteiro(a)", "after a divorce I am single again");

            // --- stage 2: about tab, tags, featured groups and land, action bar -------------------------------------------------
            await window.OpenSelfAsync(); await Frames(2);
            window.InterestsForTesting = "música, arte, Música, "; Require(window.TagsForTesting.SequenceEqual(["música", "arte"]), "interests become tags without repeats");
            Require(window.AddTagForTesting("fotografia") && !window.AddTagForTesting("ARTE") && !window.AddTagForTesting("  "), "a tag is added once and empty ones are refused");
            window.RemoveTagForTesting(0); Require(window.InterestsForTesting == "arte, fotografia", "a tag can be removed");
            Require(window.TagInputVisibleForTesting && window.ManageParcelsVisibleForTesting, "I can add tags and choose my land");
            Require(window.FeaturedGroupsForTesting == 1 && window.FeaturedParcelsForTesting == 1, "featured groups and land are drawn");
            await window.SaveAsync(); Require(JsonNode.Parse(calls.Last(c => c.Method == "PUT" && c.Path.EndsWith("details")).Body!)!.AsObject()["interests"]!.GetValue<string>() == "arte, fotografia" && JsonNode.Parse(calls.Last(c => c.Method == "PUT" && c.Path.EndsWith("details")).Body!)!.AsObject().ContainsKey("hideParcels"), "tags are saved as the text and the land privacy goes with it");
            await window.OpenSelfAsync(); window.ResizeForTesting(new Vector2(1350, 760)); await Frames(3); Require(window.AboutColumnsForTesting == 2, "the about tab has two columns when there is room");
            window.ResizeForTesting(new Vector2(900, 600)); await Frames(3); Require(window.AboutColumnsForTesting == 1, "and one when there is not");
            Require(window.ActionsForTesting.SequenceEqual(["Editar perfil", "Compartilhar perfil", "Privacidade"]), "my bar has no actions for others");
            window.Shortcuts["appearance"] = () => opened.Add("appearance"); await window.OpenSelfAsync(); window.PressActionForTesting("Alterar aparência"); Require(opened.Contains("appearance"), "Alterar aparência opens the mirror");
            var visited = Guid.Empty; var groupOpened = Guid.Empty; window.VisitParcel = (island, parcel) => visited = parcel; window.OpenGroup = id => groupOpened = id;
            profiles["other"] = Profile(Other, false); await window.OpenByIdAsync(Other); await Frames(3); window.PressVisitForTesting(0); Require(visited == Parcel1, "Visitar takes the person to the parcel");
            window.PressFeaturedGroupForTesting(0); Require(groupOpened == GroupId, "a featured group opens its page");
            Require(!window.ManageParcelsVisibleForTesting && !window.TagInputVisibleForTesting, "somebody else's profile cannot be edited");
            Require(window.ActionsForTesting.SequenceEqual(["IM / Chat", "Teletransportar", "Adicionar Amigo", "Enviar Presente", "Bloquear", "Mais"]), "the bar of somebody else's profile");
            Require(window.ActionEnabledForTesting("Adicionar Amigo"), "a stranger can be asked to be a friend");
            window.PressActionForTesting("Adicionar Amigo"); await Frames(2); Require(calls.Any(c => c.Method == "POST" && c.Path == "/api/v1/social/friends/requests" && c.Body!.Contains(Other.ToString())) && window.RelationForTesting == "pending_out" && !window.ActionEnabledForTesting("Pedido enviado"), "the request is sent to the real person and the button waits");
            relation = "friend"; await window.OpenByIdAsync(Other); await Frames(3); Require(window.ActionsForTesting.Contains("Amigo ✓"), "a friend is shown as such");
            window.PressActionForTesting("Amigo ✓"); Require(window.DialogsForTesting == 1, "undoing a friendship asks first"); window.AnswerDialogForTesting("", true); await Frames(2); Require(calls.Any(c => c.Method == "DELETE" && c.Path.EndsWith(Other.ToString())) && window.ActionsForTesting.Contains("Adicionar Amigo"), "after confirming the friendship ends");
            relation = "pending_in"; await window.OpenByIdAsync(Other); await Frames(3); Require(window.ActionsForTesting.Contains("Aceitar amizade"), "a request received can be accepted");
            relation = "none"; await window.OpenByIdAsync(Other); await Frames(3);
            window.PressActionForTesting("IM / Chat"); Require(window.DialogsForTesting == 1, "IM opens a box"); window.AnswerDialogForTesting("   ", true); Require(window.DialogsForTesting == 1 && !calls.Any(c => c.Path == "/api/v1/social/messages"), "an empty message is not sent");
            window.AnswerDialogForTesting("Oi, Bia!", true); await Frames(2); Require(calls.Last(c => c.Path == "/api/v1/social/messages").Body!.Contains("Oi, Bia!") && calls.Last(c => c.Path == "/api/v1/social/messages").Body!.Contains(Other.ToString()) && window.StatusForTesting.Contains("Mensagem enviada"), "the message goes to that person");
            window.PressActionForTesting("Bloquear"); Require(window.DialogsForTesting == 1 && !calls.Any(c => c.Path.StartsWith("/api/v1/social/blocks")), "blocking asks first"); window.AnswerDialogForTesting("", false); Require(!calls.Any(c => c.Path.StartsWith("/api/v1/social/blocks")), "cancelling does not block");
            window.PressActionForTesting("Bloquear"); window.AnswerDialogForTesting("", true); await Frames(2); Require(calls.Any(c => c.Method == "POST" && c.Path == $"/api/v1/social/blocks/{Other}") && window.StatusForTesting.Contains("bloqueado"), "confirming blocks the person");
            window.PressActionForTesting("Teletransportar"); Require(window.TeleportForTesting.Count == 2, "teleport offers two ways");
            window.ChooseTeleportForTesting(0); Require(window.StatusForTesting.Contains("Entre em uma ilha"), "without an island there is no teleport");
            var island = Guid.NewGuid(); window.CurrentIsland = () => island; window.ChooseTeleportForTesting(0); await Frames(2); Require(calls.Last(c => c.Path == "/api/v1/social/teleports").Body!.Contains("request") && calls.Last(c => c.Path == "/api/v1/social/teleports").Body!.Contains(island.ToString()), "asking to go to the person");
            window.ChooseTeleportForTesting(1); await Frames(2); Require(calls.Last(c => c.Path == "/api/v1/social/teleports").Body!.Contains("offer"), "inviting the person");
            teleportError = true; window.ChooseTeleportForTesting(1); await Frames(2); Require(window.StatusForTesting.Contains("mesma ilha"), "the server refusal is explained");
            window.PressActionForTesting("Enviar Presente"); await Frames(3); Require(window.GiftItemsForTesting.SequenceEqual(["Espada"]), "only items that can be transferred are offered as a gift");
            window.AnswerDialogForTesting("", true); Require(window.DialogsForTesting == 1 && !calls.Any(c => c.Path.EndsWith("/offers")), "a gift needs an item chosen");
            window.PickGiftForTesting(0); window.AnswerDialogForTesting("Feliz aniversario", true); await Frames(2); var gift = calls.Last(c => c.Path.EndsWith("/offers")); Require(gift.Body!.Contains(Other.ToString()) && gift.Body.Contains("Feliz aniversario") && gift.Body.Contains("gift") && window.StatusForTesting.Contains("Espada"), "the gift is offered to that person with the message");
            Require(window.MoreForTesting.Count == 0, "the menu is built when opened"); window.PressActionForTesting("Mais"); Require(window.MoreForTesting.Contains("Copiar nome") && window.MoreForTesting.Contains("Denunciar…"), "the more menu");
            window.ChooseMoreForTesting(2); Require(window.SelectedTabForTesting == 2, "Ver grupos goes to the groups tab"); window.ChooseMoreForTesting(5); Require(window.DialogsForTesting == 1, "reporting asks for the reason"); window.AnswerDialogForTesting("spam", true); await Frames(2); Require(calls.Any(c => c.Path == "/api/v1/social/reports" && c.Body!.Contains("spam")), "the report is sent");

            // --- stage 3: gallery, main picture and cover --------------------------------------------------------------------------
            var png = Image.CreateEmpty(16, 24, false, Image.Format.Rgba8); png.Fill(new Color("#12F2E0")); var bytes = png.SavePngToBuffer();
            window.ImageFetcher = path => { fetched.Add(path); return Task.FromResult<byte[]?>(bytes); };
            await window.OpenSelfAsync(); await Frames(3); Require(window.GalleryCountForTesting == 0 && window.ThumbsForTesting == 1 && !window.GalleryToolsVisibleForTesting, "an empty gallery has only the add card");
            Require(window.CoverFrameVisibleForTesting && !window.CoverShownForTesting, "I see the place of the cover");
            window.PressAddForTesting(); Require(window.AddMenuForTesting.Count == 1 && window.AddMenuForTesting[0].Contains("inventário"), "without a camera the add menu offers the inventory");
            await window.AddImageForTesting(Guid.NewGuid()); await Frames(3); await window.AddImageForTesting(Guid.NewGuid()); await window.AddImageForTesting(Guid.NewGuid()); await Frames(3);
            Require(window.GalleryCountForTesting == 3 && window.ThumbsForTesting == 4 && window.MainShownForTesting && window.PhotoShownForTesting, "three images: the first is the main one and is drawn");
            Require(fetched.Count(p => p.Contains("/profile/gallery/")) >= 3, "each thumbnail comes from the server");
            window.SelectImageForTesting(2); Require(window.SelectedImageForTesting == 2 && !window.MainShownForTesting && window.SetMainEnabledForTesting && !window.MoveEnabledForTesting(false) && window.MoveEnabledForTesting(true), "selecting the last: it can go left or become main");
            window.PressImageToolForTesting("main"); await Frames(3); Require(calls.Last(c => c.Path.EndsWith("/main")).Method == "PUT" && window.MainShownForTesting, "the main picture changes on the server");
            window.SelectImageForTesting(2); window.PressImageToolForTesting("left"); await Frames(3); Require(calls.Last(c => c.Path.EndsWith("/order")).Body!.Contains("ids") && window.SelectedImageForTesting == 1, "moving saves the order and follows the image");
            window.PressImageToolForTesting("zoom"); Require(window.DialogsForTesting == 1, "an image opens enlarged"); window.AnswerDialogForTesting("", false);
            window.PressImageToolForTesting("remove"); Require(window.DialogsForTesting == 1 && window.GalleryCountForTesting == 3, "removing asks first"); window.AnswerDialogForTesting("", true); await Frames(3); Require(window.GalleryCountForTesting == 2 && calls.Any(c => c.Method == "DELETE" && c.Path.Contains("/profile/gallery/")), "after confirming the image is gone");
            galleryFull = true; await window.AddImageForTesting(Guid.NewGuid()); Require(window.StatusForTesting.Contains("até 12") && window.GalleryCountForTesting == 2, "a full gallery explains the refusal"); galleryFull = false;
            await window.SetCoverForTesting(Guid.NewGuid()); await Frames(3); Require(window.CoverShownForTesting && fetched.Any(p => p.EndsWith("/profile/cover")), "the cover is set and drawn");
            window.PressCoverForTesting(); Require(window.CoverMenuForTesting.Contains("Remover capa"), "a cover can be removed"); window.ChooseCoverForTesting(window.CoverMenuForTesting.Count - 1); await Frames(3); Require(!window.CoverShownForTesting && window.StatusForTesting.Contains("removida"), "after removing there is no cover");
            window.CaptureWorld = () => Task.FromResult<string?>(null); window.PressAddForTesting(); Require(window.AddMenuForTesting.Count == 2, "with a camera the add menu offers the capture"); window.ChooseAddForTesting(1); await Frames(3); Require(window.StatusForTesting.Contains("Não foi possível capturar") && window.IsOpen, "a failed capture is explained and the window comes back");
            await window.SetCoverForTesting(Guid.NewGuid()); profiles["other"] = Profile(Other, false); await window.OpenByIdAsync(Other); await Frames(3);
            Require(window.GalleryCountForTesting == 2 && window.CoverFrameVisibleForTesting && !window.TagInputVisibleForTesting && window.ThumbsForTesting == 2, "another profile shows the pictures and the cover but no add card");
            Gal.Clear(); HasCover = false; profiles["other"] = Profile(Other, false); await window.OpenByIdAsync(Other); await Frames(3); Require(!window.CoverFrameVisibleForTesting && window.ThumbsForTesting == 0, "somebody else without pictures shows no cover, no add card");
            window.ImageFetcher = null;

            // --- stage 4: tabs, privacy toggles --------------------------------------------------------------------------------
            var island1 = Guid.Empty; window.VisitIsland = id => island1 = id; profiles["other"] = Profile(Other, false); await window.OpenByIdAsync(Other); await Frames(3);
            Require(window.FavoriteRowsForTesting == 1 && window.AchievementRowsForTesting == 1 && window.ParcelRowsForTesting == 1, "each tab lists what the server sent");
            window.PressFavoriteForTesting(0); Require(island1 == Island1, "a favorite takes the person to its island");
            var stats = window.StatsForTesting; Require(stats.Count == 6 && stats[0].Contains("Tempo de participação") && stats[0].Contains("1 ano(s)") && stats[1].Contains("Grupos") && stats[2].Contains("1"), "the statistics show the real counts");
            Require(window.EmptyTextForTesting(4).Contains("Ilha Esmeralda") && !window.EmptyTextForTesting(4).Contains("Privacidade"), "somebody else's favorites do not talk about my settings");
            HideStats = true; profiles["other"] = Profile(Other, false); await window.OpenByIdAsync(Other); await Frames(3); Require(window.EmptyTextForTesting(6).Contains("ocultas"), "hidden statistics say so"); HideStats = false;
            profiles["other"] = Profile(Other, false, privateProfile: true); await window.OpenByIdAsync(Other); await Frames(3);
            Require(window.FavoriteRowsForTesting == 0 && window.AchievementRowsForTesting == 0 && window.ParcelRowsForTesting == 0 && window.EmptyTextForTesting(5).Contains("privado") && window.EmptyTextForTesting(6).Contains("privado"), "a private profile has empty tabs that say why");
            profiles.Remove("other"); await window.OpenSelfAsync(); await Frames(3);
            Require(window.EmptyTextForTesting(4).Contains("só aparecem para você"), "my favorites say they are private by default");
            window.PressNavByNameForTesting("Favoritos"); Require(window.SelectedTabForTesting == 4, "Favoritos in the menu opens its tab"); window.PressNavByNameForTesting("Estatísticas"); Require(window.SelectedTabForTesting == 6, "Estatísticas too");
            window.SetPrivacyForTesting(true, true, true, true); await window.SaveAsync(); var privacy = JsonNode.Parse(calls.Last(c => c.Method == "PUT" && c.Path.EndsWith("details")).Body!)!.AsObject();
            Require(privacy["showFavorites"]!.GetValue<bool>() && privacy["hideAnniversary"]!.GetValue<bool>() && privacy["hideStats"]!.GetValue<bool>() && privacy["hideAchievements"]!.GetValue<bool>(), "the new privacy choices are saved");
            window.ToggleParcelRowForTesting(0, false); await Frames(3); Require(calls.Any(c => c.Method == "PUT" && c.Path.StartsWith("/api/v1/users/me/parcels/") && c.Body!.Contains("false")), "un-featuring land from the tab reaches the server");
            window.SelectTabForTesting(0);

            // --- feed --------------------------------------------------------------------------------------------------
            feedPosts.Add((Guid.NewGuid(), "Meu primeiro post", false, false)); feedPosts.Add((Guid.NewGuid(), "Só para amigos", true, false)); feedAchievement = true;
            await window.OpenSelfAsync(); await Frames(5);
            Require(window.ComposerVisibleForTesting, "I can write on my own feed");
            Require(window.FeedRowsForTesting == 3 && window.FeedStateForTesting == "", "the feed shows posts and the achievement");
            Require(window.FeedRowTextForTesting(0).Contains("Meu primeiro post") && window.FeedRowTextForTesting(1).Contains("só amigos") && window.FeedRowTextForTesting(2).Contains("Conquista: Faz parte de uma comunidade") && window.FeedRowTextForTesting(2).Contains("01/10/2026"), "posts and the achievement with its real date");
            var postsBefore = calls.Count(c => c.Method == "POST" && c.Path == "/api/v1/users/me/profile/posts");
            await window.PublishForTesting("   ", false, false); Require(calls.Count(c => c.Method == "POST" && c.Path == "/api/v1/users/me/profile/posts") == postsBefore, "an empty post is not even sent");
            await window.PublishForTesting("Novo post", true, false); await Frames(3);
            Require(calls.Any(c => c.Method == "POST" && c.Path == "/api/v1/users/me/profile/posts" && c.Body!.Contains("friends") && c.Body.Contains("Novo post")) && window.FeedRowsForTesting == 4 && window.FeedRowTextForTesting(0).Contains("Novo post"), "publishing sends the text and who may see it, and shows it first");
            window.PressDeletePostForTesting(0); Require(window.DialogsForTesting == 1 && window.FeedRowsForTesting == 4, "deleting asks first"); window.AnswerDialogForTesting("", true); await Frames(3);
            Require(calls.Any(c => c.Method == "DELETE" && c.Path.Contains("/me/profile/posts/")) && window.FeedRowsForTesting == 3, "after confirming the post is gone");
            feedMore = true; await window.OpenSelfAsync(); await Frames(5);
            Require(window.FeedMoreVisibleForTesting && window.FeedRowsForTesting == 3, "a feed with more pages offers to load more");
            window.PressFeedMoreForTesting(); await Frames(4); Require(window.FeedRowsForTesting == 4 && !window.FeedMoreVisibleForTesting && calls.Any(c => c.Path.Contains("profile/feed") && c.Path.Contains("before=")), "the next page is appended using the cursor");
            feedMore = false; feedPosts.Clear(); feedAchievement = false; await window.OpenSelfAsync(); await Frames(4);
            Require(window.FeedRowsForTesting == 0 && window.FeedStateForTesting.Contains("ainda não publicou"), "an empty feed says so");
            feedMode = "private"; await window.OpenByIdAsync(Other); await Frames(4);
            Require(!window.ComposerVisibleForTesting && window.FeedStateForTesting.Contains("privado") && window.FeedRowsForTesting == 0, "a private feed is hidden and has no composer");
            feedMode = "error"; await window.OpenByIdAsync(Other); await Frames(4);
            Require(window.FeedStateForTesting.Contains("Não foi possível carregar") && window.FeedRetryVisibleForTesting, "a failed feed says so and offers to retry");
            feedMode = "ok"; feedPosts.Add((Guid.NewGuid(), "Olá da Bia", false, false)); window.PressFeedRetryForTesting(); await Frames(4);
            Require(window.FeedRowsForTesting == 1 && !window.FeedRetryVisibleForTesting && !window.ComposerVisibleForTesting, "retrying loads the feed; others cannot post on it");
            feedPosts.Clear(); window.SelectTabForTesting(0);

            // --- layout: navigation, responsiveness, window controls ------------------------------------------------------------
            window.Shortcuts["inventory"] = () => opened.Add("inventory"); window.Shortcuts["settings"] = () => opened.Add("settings");
            await window.OpenSelfAsync(); await Frames(2);
            Require(window.TitleForTesting == "Meu Perfil", "my window is titled Meu Perfil");
            Require(window.NavForTesting.SequenceEqual(["Perfil", "Aparência", "Inventário", "Atividades", "Favoritos", "Conquistas", "Estatísticas", "Privacidade", "Configurações"]), "the navigation lists only what the game can open"); window.PressNavByNameForTesting("Atividades"); Require(window.SelectedTabForTesting == 1, "Atividades in the menu opens the feed"); window.SelectTabForTesting(0);
            window.ResizeForTesting(new Vector2(1350, 760)); Require(window.SidebarVisibleForTesting && !window.SidebarCollapsedForTesting && window.PhotoColumnVisibleForTesting, "a wide window shows navigation, picture and information");
            window.ResizeForTesting(new Vector2(900, 600)); Require(window.SidebarCollapsedForTesting && window.PhotoColumnVisibleForTesting, "a medium window folds the navigation to icons");
            window.ResizeForTesting(new Vector2(700, 500)); Require(!window.PhotoColumnVisibleForTesting, "a narrow window drops the picture column");
            window.ResizeForTesting(new Vector2(1350, 760)); window.ToggleSidebarForTesting(); Require(window.SidebarCollapsedForTesting, "the menu can be collapsed by hand");
            opened.Clear(); window.PressNavForTesting(2); window.PressNavForTesting(8); Require(opened.SequenceEqual(["inventory", "settings"]), "the entries open the existing modules");
            window.BioForTesting = "mudou"; await window.DiscardForTesting(); Require(window.BioForTesting == "Construtora", "discarding brings back what the server has");
            window.ToggleExpandForTesting(); var big = window.WindowSizeForTesting; window.ToggleExpandForTesting(); Require(big.X > window.WindowSizeForTesting.X || big.Y >= window.WindowSizeForTesting.Y, "expanding enlarges and restoring gives the size back");

            GD.Print("event=profile_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=profile_smoke result=failed error={exception}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
