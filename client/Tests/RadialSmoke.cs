using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>The radial menu on its own: opening, hovering, submenus and going back, long lists, closing, and what each kind of target offers.</summary>
public partial class RadialSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var menu = new RadialMenu(); var layer = new CanvasLayer(); AddChild(layer); layer.AddChild(menu);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var log = new List<string>();
            RadialItem Leaf(string name) => new() { Label = name, Run = () => log.Add(name) };

            menu.Open(new Vector2(400, 300), "Alvo", "user", [Leaf("A"), new RadialItem { Label = "B", Children = [Leaf("B1"), Leaf("B2")] }, Leaf("C")]);
            Require(menu.IsOpen && menu.Depth == 1 && menu.Labels.SequenceEqual(["A", "B", "C"]) && menu.Title == "Alvo", "the menu opens with its slices");
            menu.HoverForTesting(new Vector2(0, -150));
            Require(menu.HoverIndexForTesting == 0, "straight up is the first slice");
            menu.HoverForTesting(new Vector2(0, 0));
            Require(menu.HoverIndexForTesting == -1, "the middle disc is no slice");
            menu.PickForTesting(1);
            Require(menu.Depth == 2 && menu.Labels.SequenceEqual(["B1", "B2"]) && menu.Title == "B", "a slice with choices opens a smaller circle");
            menu.BackForTesting();
            Require(menu.Depth == 1 && menu.IsOpen, "the middle goes back one circle");
            menu.PickForTesting(2);
            Require(!menu.IsOpen && log.SequenceEqual(["C"]), "a plain slice runs and closes the menu");

            menu.Open(new Vector2(400, 300), "Muitos", "dots", Enumerable.Range(1, 16).Select(i => Leaf($"I{i}")).ToList());
            Require(menu.Labels.Count == 12 && menu.Labels[11] == "Mais", $"sixteen entries become a circle of eleven plus Mais: {string.Join(",", menu.Labels)}");
            menu.PickForTesting(11);
            Require(menu.Labels.SequenceEqual(["I12", "I13", "I14", "I15", "I16"]), "Mais opens the rest, nothing is lost");
            menu.BackForTesting(); menu.BackForTesting();
            Require(!menu.IsOpen, "going back from the first circle closes the menu");

            menu.Open(new Vector2(2, 2), "Canto", "map", [Leaf("X")]);
            Require(menu.CenterForTesting.X > 150 && menu.CenterForTesting.Y > 150, $"a menu opened in a corner is moved onto the screen: {menu.CenterForTesting}");
            menu.PickForTesting(5);
            Require(menu.IsOpen, "a slice that does not exist does nothing");
            menu.Close();
            menu.Open(new Vector2(400, 300), "Fora", "map", [new RadialItem { Label = "Desligado", Enabled = false, Run = () => log.Add("nunca") }]);
            menu.PickForTesting(0);
            Require(menu.IsOpen && !log.Contains("nunca"), "a disabled slice cannot be used");
            menu.Close();

            // what each target offers, using only real actions
            var mineId = Guid.NewGuid(); var services = new ContextServices { Owns = view => view.State.OwnerUserId == mineId, Actor = () => new LifeVerse.Shared.Interaction.ContextActor(mineId) };
            var mine = new BuildObjectView(); AddChild(mine); mine.Apply(new BuildObjectState { Id = Guid.NewGuid(), Name = "Mesa", PrimitiveType = "cube", OwnerUserId = mineId });
            var theirs = new BuildObjectView(); AddChild(theirs); theirs.Apply(new BuildObjectState { Id = Guid.NewGuid(), Name = "Vaso", PrimitiveType = "cube", OwnerUserId = Guid.NewGuid() });
            var own = InteractionRegistry.Build(new ObjectTarget(mine), services); var other = InteractionRegistry.Build(new ObjectTarget(theirs), services);
            Require(own.Single(x => x.Label == "Editar").Enabled && !other.Single(x => x.Label == "Editar").Enabled && other.Single(x => x.Label == "Editar").Tooltip!.Contains("proprietário"), "only your own objects can be edited; for others the slice stays grey and says why");
            Require(own.Count <= 8 && other.Any(x => x.Label == "Tocar" && x.Enabled) && !own.Any(x => x.Label == "Fechar"), "everyone can touch an object; there is no Fechar slice");
            var edit = own.Single(x => x.Label == "Editar").Children!;
            Require(edit.Select(x => x.Label).SequenceEqual(["Mover", "Girar", "Esticar", "Textura", "Duplicar", "Excluir"]), "the edit circle has the editing tools");
            Require(InteractionRegistry.Build(new GroundTarget(Vector3.Zero), services).Select(x => x.Label).SequenceEqual(["Ir até aqui", "Editar terreno", "Comprar", "Gerenciar", "Sobre a parcela", "Construir", "Criar objeto", "Mais opções"]), "the ground offers walking, terrain, land and building");
            var avatar = InteractionRegistry.Build(new AvatarTarget(2, "Lucas", Vector3.Zero), services);
            Require(avatar.Any(x => x.Label == "Interagir" && x.Children!.Count > 0) && avatar.Any(x => x.Label == "Mensagem") && avatar.Single(x => x.Label == "Moderação").Children!.Any(x => x.Label == "Denunciar"), "another avatar offers talking, gestures and moderation");

            var invited = ""; var inviteServices = new ContextServices { InviteToGroup = name => invited = name, Actor = () => new LifeVerse.Shared.Interaction.ContextActor(Guid.NewGuid()) }; InteractionRegistry.Build(new AvatarTarget(2, "Lucas", Vector3.Zero), inviteServices).Single(x => x.Label == "Grupos").Children!.Single(x => x.Label == "Convidar para grupo").Run!.Invoke();
            Require(invited == "Lucas", "another avatar can be invited to a group from the menu");

            // the actions on a person: find the account by name, then ask the API
            var person = new PersonActions(); AddChild(person);
            var lucas = Guid.NewGuid(); var calls = new List<string>(); var said = new List<string>(); person.Said += said.Add;
            person.Transport = (method, path, body) =>
            {
                calls.Add($"{method} {path} {body}");
                if (path.StartsWith("/api/v1/social/people")) return (200, $"[{{\"id\":\"{lucas}\",\"username\":\"lucas\",\"displayName\":\"Lucas Ferreira\",\"relation\":\"none\"}},{{\"id\":\"{Guid.NewGuid()}\",\"username\":\"lucasf2\",\"displayName\":\"Outro\",\"relation\":\"none\"}}]");
                return (200, "{}");
            };
            person.AddFriend("Lucas Ferreira");
            Require(calls.Count == 2 && calls[1].StartsWith("Post /api/v1/social/friends/requests") && calls[1].Contains(lucas.ToString()) && said[^1].Contains("Pedido de amizade"), $"a friend request goes to the account found by name: {string.Join(" | ", calls)} / {string.Join(" | ", said)}");
            person.SendMessage("Lucas Ferreira", "Oi!");
            Require(calls.Count == 3 && calls[2].Contains("/social/messages") && calls[2].Contains("Oi!") && calls[2].Contains(lucas.ToString()), "the second action reuses the account already found: no second search");
            person.Block("Lucas Ferreira"); person.Report("Lucas Ferreira", "spam");
            Require(calls[3].Contains($"/social/blocks/{lucas}") && calls[4].Contains("/social/reports") && calls[4].Contains("spam"), "blocking and reporting use the account id");
            person.Block("Ninguém Assim");
            Require(said[^1].Contains("Não encontrei"), "an unknown name is said plainly and nothing is sent");
            Require(!calls[^1].Contains("/blocks/"), "no block was sent for an unknown name");
            Require(InteractionRegistry.Build(new SelfTarget("Eu"), services).Any(x => x.Label == "Inventário"), "your own avatar offers your things");
            var car = new VehicleView(); AddChild(car); car.Setup(7, "Carro do Rafa", "car");
            var carFree = InteractionRegistry.Build(new VehicleTarget(car), new ContextServices { IsDriving = _ => false }); var carDriven = InteractionRegistry.Build(new VehicleTarget(car), new ContextServices { IsDriving = id => id == 7 });
            Require(carFree.Select(x => x.Label).SequenceEqual(["Entrar", "Inspecionar"]) && carDriven[0].Label == "Sair", "a car offers entering, or leaving when you are driving it");
            var entered = -1; InteractionRegistry.Build(new VehicleTarget(car), new ContextServices { EnterVehicle = id => entered = id })[0].Run!();
            Require(entered == 7, "Entrar asks for this very vehicle");

            // ---- stage 2: permissions drive the slices; inventory, folders and script panels get their own menus
            var meId = Guid.NewGuid(); var actions = new List<string>();
            var stage2 = new ContextServices
            {
                Actor = () => new LifeVerse.Shared.Interaction.ContextActor(meId), InventoryAction = id => actions.Add($"inv{id}"), CloseHud = id => actions.Add("hud"),
                TakeObject = (id, name, remove) => actions.Add($"take:{name}:{remove}"), DeleteObject = id => actions.Add("delete"), Tell = t => actions.Add("tell")
            };
            var ownView = new BuildObjectView(); AddChild(ownView); ownView.Apply(new BuildObjectState { Id = Guid.NewGuid(), Name = "Mesa", OwnerUserId = meId, CreatorUserId = meId, ClickAction = "buy" });
            var protectedView = new BuildObjectView(); AddChild(protectedView); protectedView.Apply(new BuildObjectState { Id = Guid.NewGuid(), Name = "Caixa", OwnerUserId = meId, EditLocked = true });
            var noCopyView = new BuildObjectView(); AddChild(noCopyView); noCopyView.Apply(new BuildObjectState { Id = Guid.NewGuid(), Name = "Raro", OwnerUserId = meId, OwnerRights = LifeVerse.Shared.Items.ItemRights.Modify | LifeVerse.Shared.Items.ItemRights.Transfer });
            var forSale = InteractionRegistry.Build(new ObjectTarget(ownView), stage2);
            Require(forSale[0].Label == "Comprar" && forSale[0].Enabled, "a for-sale object leads with Comprar");
            var locked = InteractionRegistry.Build(new ObjectTarget(protectedView), stage2);
            Require(!locked.Single(x => x.Label == "Editar").Enabled && locked.Single(x => x.Label == "Editar").Tooltip!.Contains("protegido") && !locked.Single(x => x.Label == "Pegar").Enabled, "a protected object keeps its slices grey with the reason");
            var raro = InteractionRegistry.Build(new ObjectTarget(noCopyView), stage2);
            Require(!raro.Single(x => x.Label == "Pegar cópia").Enabled && raro.Single(x => x.Label == "Pegar").Enabled, "rights without Copy grey out Pegar cópia only");
            raro.Single(x => x.Label == "Pegar").Run!(); menu.Open(new Vector2(500, 400), "Raro", "cube", raro); menu.PickForTesting(raro.FindIndex(x => x.Label == "Pegar cópia")); var stillOpen = menu.IsOpen; menu.Close();
            Require(actions.SequenceEqual(["take:Raro:True"]) && stillOpen, "Pegar takes the object out of the world; the grey slice cannot be used");
            var moreOptions = forSale.Single(x => x.Label == "Mais opções");
            Require(moreOptions.SidePanel && moreOptions.Children!.Any(x => x.Label == "Excluir" && x.Destructive && x.Confirm is not null), "deleting an object asks first");
            var tools = new Control(); AddChild(tools); var radial = new RadialMenu(); AddChild(radial);

            var invItem = InteractionRegistry.Build(new InventoryItemTarget(Guid.NewGuid(), "Casa", "models", LifeVerse.Shared.Items.ItemRights.All), stage2);
            Require(invItem.Select(x => x.Label).SequenceEqual(["Pré-visualizar", "Colocar no mundo", "Vestir", "Copiar", "Enviar", "Favoritar", "Mais opções"]) && invItem.Count <= 8, "an inventory item gets the real actions of the inventory");
            invItem.Single(x => x.Label == "Colocar no mundo").Run!(); invItem.Single(x => x.Label == "Copiar").Run!();
            Require(actions.TakeLast(2).SequenceEqual(["inv3", "inv10"]), "inventory slices run the inventory's own actions");
            var texture = InteractionRegistry.Build(new InventoryItemTarget(Guid.NewGuid(), "Areia", "textures", LifeVerse.Shared.Items.ItemRights.Copy), stage2);
            Require(!texture.Single(x => x.Label == "Colocar no mundo").Enabled && !texture.Single(x => x.Label == "Enviar").Enabled && texture.Single(x => x.Label == "Copiar").Enabled, "type and rights decide what an item offers");
            var trash = InteractionRegistry.Build(new InventoryItemTarget(Guid.NewGuid(), "Velho", "models", LifeVerse.Shared.Items.ItemRights.All, InTrash: true), stage2);
            Require(trash.Select(x => x.Label).SequenceEqual(["Restaurar", "Excluir de vez", "Direitos", "Pré-visualizar"]) && trash.Single(x => x.Label == "Excluir de vez").Confirm is not null, "the trash restores or deletes for good (with a question)");
            var folder = InteractionRegistry.Build(new InventoryFolderTarget(Guid.NewGuid(), "Móveis"), stage2);
            Require(folder.Select(x => x.Label).SequenceEqual(["Nova subpasta", "Renomear", "Excluir"]) && folder[2].Confirm!.Contains("Móveis"), "a folder can get a subfolder, a new name, or be deleted after a question");
            var panelId = Guid.NewGuid();
            var hudMenu = InteractionRegistry.Build(new HudTarget(panelId, "Loja"), stage2);
            hudMenu[0].Run!();
            Require(hudMenu.Select(x => x.Label).SequenceEqual(["Fechar painel", "Sobre o painel"]) && actions[^1] == "hud", "a script panel can be closed from its menu");
            Require(InteractionRegistry.Build(new SelfTarget("Eu"), stage2).Concat(InteractionRegistry.Build(new GroundTarget(Vector3.Zero), stage2)).All(x => x.Label != "Fechar"), "no menu has a Fechar slice any more");
            tools.QueueFree(); radial.QueueFree();

            // ---- stage 4: another player's menu follows the relation, pays, and keeps local choices local
            var peerLog = new List<string>(); var hiddenSet = new HashSet<int>(); var mutedSet = new HashSet<int>(); var relationNow = "";
            var social = new ContextServices
            {
                Actor = () => new LifeVerse.Shared.Interaction.ContextActor(meId), Relation = _ => relationNow, IsHidden = hiddenSet.Contains, IsMuted = mutedSet.Contains,
                Pay = n => peerLog.Add($"pay:{n}"), AddFriend = n => peerLog.Add($"add:{n}"), RemoveFriend = n => peerLog.Add($"remove:{n}"), AcceptFriend = n => peerLog.Add($"accept:{n}"),
                Block = n => peerLog.Add($"block:{n}"), ToggleHidden = p => { if (!hiddenSet.Remove(p)) hiddenSet.Add(p); }, ToggleMuted = p => { if (!mutedSet.Remove(p)) mutedSet.Add(p); }, CopyPersonId = n => peerLog.Add($"id:{n}")
            };
            List<RadialItem> Person() => InteractionRegistry.Build(new AvatarTarget(7, "Rafa", Vector3.Zero), social);
            Require(Person().Select(x => x.Label).SequenceEqual(["Perfil", "Mensagem", "Amizade", "Pagar", "Interagir", "Grupos", "Teleporte", "Moderação", "Mais opções"]), "another player's menu has the slices of the design (nine, so the text is compact)");
            Require(Person().Single(x => x.Label == "Amizade").Children![0].Label == "Adicionar amigo", "unknown or no relation offers Adicionar amigo");
            relationNow = "friend"; var friendMenu = Person().Single(x => x.Label == "Amizade").Children!;
            Require(friendMenu[0].Label == "Remover amizade" && friendMenu[0].Confirm!.Contains("Rafa") && friendMenu[0].Destructive, "a friend offers Remover amizade, after a question");
            relationNow = "pending_in"; Require(Person().Single(x => x.Label == "Amizade").Children![0].Label == "Aceitar pedido", "a pending request offers Aceitar pedido");
            relationNow = "pending_out"; Require(!Person().Single(x => x.Label == "Amizade").Children![0].Enabled, "a request already sent cannot be sent again");
            Person().Single(x => x.Label == "Pagar").Run!();
            Require(peerLog[^1] == "pay:Rafa", "Pagar opens the payment for that person");
            var mod = () => Person().Single(x => x.Label == "Moderação").Children!;
            Require(mod().Select(x => x.Label).SequenceEqual(["Bloquear", "Silenciar voz", "Ocultar avatar", "Denunciar"]) && mod()[0].Confirm is not null, "moderation: block (asks first), mute, hide and report");
            mod()[1].Run!(); mod()[2].Run!();
            Require(mutedSet.Contains(7) && hiddenSet.Contains(7) && mod()[1].Label == "Reativar voz" && mod()[2].Label == "Mostrar avatar", "muting and hiding are local choices and the slices turn into their opposites");
            var signedOut = InteractionRegistry.Build(new AvatarTarget(7, "Rafa", Vector3.Zero), new ContextServices());
            Require(!signedOut.Single(x => x.Label == "Pagar").Enabled && !signedOut.Single(x => x.Label == "Mensagem").Enabled && signedOut.Single(x => x.Label == "Moderação").Enabled, "signed out you cannot pay or write, but you can still hide or silence");
            // a menu that is open learns about the relation without closing
            relationNow = ""; menu.Open(new Vector2(500, 400), "Rafa", "user", Person()); menu.PickForTesting(2);
            relationNow = "friend"; var replaced = menu.ReplaceRoot(Person());
            Require(replaced && menu.SidePanelOpen && menu.PanelLabels[0] == "Remover amizade", "the friendship panel updates in place when the relation arrives");
            menu.Close();

            // the actions behind them, against a stand-in API
            var friendsApi = new PersonActions(); AddChild(friendsApi); var apiCalls = new List<string>(); var told = new List<string>(); friendsApi.Said += told.Add; var rafa = Guid.NewGuid(); var known = new List<string>(); friendsApi.Known += known.Add;
            friendsApi.Transport = (method, path, body) =>
            {
                apiCalls.Add($"{method} {path} {body}");
                if (path.StartsWith("/api/v1/social/people")) return (200, $"[{{\"id\":\"{rafa}\",\"username\":\"rafa\",\"displayName\":\"Rafa\",\"relation\":\"friend\"}}]");
                if (path.EndsWith("/economy/transfer")) return body!.Contains("\"amountCoins\":999") ? (409, "{}") : (200, "{\"status\":\"completed\"}");
                return (200, "{}");
            };
            friendsApi.Prefetch("Rafa");
            Require(friendsApi.RelationOf("Rafa") == "friend" && known.SequenceEqual(["Rafa"]), "looking a person up remembers the relation and tells the menu");
            friendsApi.Prefetch("Rafa"); Require(apiCalls.Count == 1, "a person already known is not searched again");
            friendsApi.RemoveFriend("Rafa");
            Require(apiCalls[^1] == $"Delete /api/v1/social/friends/{rafa} " && friendsApi.RelationOf("Rafa") == "none", "removing a friend calls the API and updates the relation");
            friendsApi.AcceptFriend("Rafa"); Require(apiCalls[^1].StartsWith($"Post /api/v1/social/friends/{rafa}/accept") && friendsApi.RelationOf("Rafa") == "friend", "accepting a request calls the API");
            var paid = false; friendsApi.PaymentMade += () => paid = true;
            friendsApi.Pay("Rafa", 50, "chave-unica-1");
            Require(apiCalls[^1].Contains("/economy/transfer") && apiCalls[^1].Contains(rafa.ToString()) && apiCalls[^1].Contains("\"amountCoins\":50") && apiCalls[^1].Contains("chave-unica-1") && paid && told[^1].Contains("VC$ 50"), $"a payment sends the amount with its own key: {apiCalls[^1]}");
            friendsApi.Pay("Rafa", 0, "k2"); Require(!apiCalls[^1].Contains("\"amountCoins\":0") && told[^1].Contains("maior que zero"), "a zero payment is refused before anything is sent");
            friendsApi.Pay("Rafa", 999, "k3"); Require(told[^1].Contains("saldo insuficiente"), "a refused payment says why (insufficient balance or blocked wallet)");
            friendsApi.CopyId("Rafa"); Require(DisplayServer.GetName() == "headless" || DisplayServer.ClipboardGet() == rafa.ToString(), "Copiar identificador copies the account id");

            var tpLog = new List<string>(); var tpMenu = InteractionRegistry.Build(new AvatarTarget(7, "Rafa", Vector3.Zero), new ContextServices { Actor = () => new LifeVerse.Shared.Interaction.ContextActor(meId), Teleport = (n, k) => tpLog.Add($"{k}:{n}") }).Single(x => x.Label == "Teleporte");
            Require(tpMenu.SidePanel && tpMenu.Children!.Select(x => x.Label).SequenceEqual(["Oferecer teleporte", "Pedir teleporte"]), "the Teleporte slice offers an offer and a request");
            tpMenu.Children![0].Run!(); tpMenu.Children![1].Run!();
            Require(tpLog.SequenceEqual(["offer:Rafa", "request:Rafa"]), "each choice sends its own kind");
            Require(InteractionRegistry.Build(new AvatarTarget(7, "Rafa", Vector3.Zero), new ContextServices()).Single(x => x.Label == "Teleporte").Children!.All(x => !x.Enabled), "signed out, teleports are grey");
            // ---- stage 5: moderation of a parcel or the island, only for whoever may do it
            var modLog = new List<string>(); var adminNow = false; var onMyParcel = false;
            var modServices = new ContextServices
            {
                Actor = () => new LifeVerse.Shared.Interaction.ContextActor(meId), IsIslandAdmin = () => adminNow, Parcel = _ => new LifeVerse.Shared.Interaction.ParcelFacts(true, "own", onMyParcel, false, "owner"),
                EjectFromParcel = (n, _) => modLog.Add($"eject:{n}"), BanFromParcel = (n, _, m) => modLog.Add($"ban:{n}:{m}"), KickFromRegion = n => modLog.Add($"kick:{n}")
            };
            List<RadialItem> ModOf() => InteractionRegistry.Build(new AvatarTarget(9, "Lucas", Vector3.Zero), modServices).Single(x => x.Label == "Moderação").Children!;
            Require(!ModOf().Any(x => x.Label.Contains("terreno") || x.Label.Contains("ilha")), "an ordinary person does not see the parcel and island tools");
            onMyParcel = true; var ownerMod = ModOf();
            Require(ownerMod.Any(x => x.Label == "Expulsar do terreno" && x.Enabled) && ownerMod.Any(x => x.Label == "Banir do terreno" && x.Enabled) && !ownerMod.Single(x => x.Label == "Expulsar da ilha").Enabled, "a parcel owner may eject and ban, not kick from the island");
            Require(ownerMod.Single(x => x.Label == "Expulsar da ilha").Tooltip!.Contains("administradores"), "the island kick says who may use it");
            var banLengths = ownerMod.Single(x => x.Label == "Banir do terreno").Children!;
            Require(banLengths.Select(x => x.Label).SequenceEqual(["1 hora", "1 dia", "7 dias", "Para sempre"]) && banLengths.All(x => x.Confirm is not null && x.Destructive), "a ban asks for the length and for confirmation");
            banLengths[0].Run!(); banLengths[3].Run!(); ownerMod.Single(x => x.Label == "Expulsar do terreno").Run!();
            Require(modLog.SequenceEqual(["ban:Lucas:60", "ban:Lucas:", "eject:Lucas"]), $"the choices reach the actions: {string.Join(",", modLog)}");
            onMyParcel = false; adminNow = true;
            Require(ModOf().All(x => !x.Label.Contains("terreno") && !x.Label.Contains("ilha") || x.Enabled), "an island administrator may use every tool");
            ModOf().Single(x => x.Label == "Expulsar da ilha").Run!(); Require(modLog[^1] == "kick:Lucas", "an administrator can kick from the island");

            var modApi = new PersonActions(); AddChild(modApi); var modCalls = new List<string>(); var modTold = new List<string>(); modApi.Said += modTold.Add; var island = Guid.NewGuid(); var parcelId = Guid.NewGuid(); var lucasId = Guid.NewGuid(); var adminAnswer = 200;
            modApi.Transport = (method, path, body) =>
            {
                modCalls.Add($"{method} {path} {body}");
                if (path.StartsWith("/api/v1/social/people")) return (200, $"[{{\"id\":\"{lucasId}\",\"username\":\"lucas\",\"displayName\":\"Lucas\",\"relation\":\"none\"}}]");
                if (path.EndsWith("/management/server")) return (adminAnswer, "{}");
                if (path.EndsWith("/management/server/kick")) return (409, "{\"error\":\"not_online\"}");
                if (path.EndsWith("/access")) return (403, "{\"error\":\"forbidden\"}");
                return (200, "{}");
            };
            var adminChanged = 0; modApi.AdminKnown += () => adminChanged++;
            modApi.ProbeAdmin(island); modApi.ProbeAdmin(island);
            Require(modApi.IsIslandAdmin(island) && adminChanged == 1 && modCalls.Count(c => c.Contains("/management/server ")) == 1, "the administrator probe asks once per island and remembers the answer");
            var strangerIsland = Guid.NewGuid(); adminAnswer = 403; modApi.ProbeAdmin(strangerIsland);
            Require(!modApi.IsIslandAdmin(strangerIsland), "a 403 means not an administrator");
            modApi.EjectFromParcel("Lucas", island, parcelId);
            Require(modCalls[^1] == $"Post /api/v1/worlds/islands/{island}/land/parcels/{parcelId}/eject {{\"userId\":\"{lucasId}\"}}" && modTold[^1].Contains("expulso do terreno"), $"eject goes to the parcel route: {modCalls[^1]}");
            modApi.BanFromParcel("Lucas", island, parcelId, 1440);
            Require(modCalls[^1].Contains("/access") && modCalls[^1].Contains("\"kind\":\"ban\"") && modCalls[^1].Contains("\"minutes\":1440") && modTold[^1].Contains("permissão"), $"a refused ban says why: {modTold[^1]}");
            modApi.KickFromRegion("Lucas", island, "teste");
            Require(modCalls[^1].Contains("/management/server/kick") && modTold[^1].Contains("não está mais na ilha"), $"a kick of someone who left says so: {modTold[^1]}");
            Require(PersonActions.DescribeMinutes(60) == "1 h" && PersonActions.DescribeMinutes(10080) == "7 dia(s)" && PersonActions.DescribeMinutes(5) == "5 min", "ban lengths are written in plain words");

            // ---- stage 3: the ground follows the parcel under the cursor
            LifeVerse.Shared.Interaction.ParcelFacts Facts(string state, bool mine = false, bool terrain = false, string build = "owner") => new(true, state, mine, terrain, build);
            List<RadialItem> Ground(LifeVerse.Shared.Interaction.ParcelFacts facts) => InteractionRegistry.Build(new GroundTarget(Vector3.Zero), new ContextServices { Parcel = _ => facts, Actor = () => new LifeVerse.Shared.Interaction.ContextActor(meId), StartBuilding = () => actions.Add("build"), EditTerrain = () => actions.Add("terrain"), ManageLand = () => actions.Add("manage"), AboutLand = _ => actions.Add("about") });
            var unknown = Ground(LifeVerse.Shared.Interaction.ParcelFacts.Unknown);
            Require(unknown.Single(x => x.Label == "Sobre a parcela").Enabled && !unknown.Single(x => x.Label == "Gerenciar").Enabled && unknown.Single(x => x.Label == "Gerenciar").Tooltip!.Contains("ainda não chegaram"), "before the land data arrives only 'about' works, and the rest says why");
            var onSale = Ground(Facts("sale"));
            Require(onSale.Single(x => x.Label == "Comprar").Enabled && !onSale.Single(x => x.Label == "Gerenciar").Enabled && !onSale.Single(x => x.Label == "Construir").Enabled, "land for sale can be bought, not managed or built on");
            var forRent = Ground(Facts("rent"));
            Require(forRent.Any(x => x.Label == "Alugar" && x.Enabled) && !forRent.Any(x => x.Label == "Comprar"), "land for rent leads with Alugar");
            var mineGround = Ground(Facts("own", mine: true, terrain: true));
            Require(mineGround.Single(x => x.Label == "Gerenciar").Enabled && mineGround.Single(x => x.Label == "Editar terreno").Enabled && mineGround.Single(x => x.Label == "Construir").Enabled && !mineGround.Single(x => x.Label == "Comprar").Enabled, "your own land can be managed, edited and built on");
            mineGround.Single(x => x.Label == "Gerenciar").Run!(); mineGround.Single(x => x.Label == "Editar terreno").Run!(); mineGround.Single(x => x.Label == "Construir").Run!(); mineGround.Single(x => x.Label == "Sobre a parcela").Run!();
            Require(actions.TakeLast(4).SequenceEqual(["manage", "terrain", "build", "about"]), "the ground slices run the game's own tools");
            Require(Ground(Facts("public", build: "everyone")).Single(x => x.Label == "Construir").Enabled && !Ground(Facts("public", build: "none")).Single(x => x.Label == "Construir").Enabled, "the build mode of the parcel decides whether Construir is open");
            Require(mineGround.Single(x => x.Label == "Mais opções").SidePanel && mineGround.Single(x => x.Label == "Mais opções").Children!.Any(x => x.Label == "Ver proprietário"), "more options open in the side panel");

            // ---- stage 1: the redesigned menu
            RadialStyle.Size = 100; RadialStyle.Tooltips = true;
            menu.Open(new Vector2(600, 400), "Pessoa", "user", Enumerable.Range(1, 12).Select(i => Leaf($"S{i}")).ToList());
            Require(menu.Labels.Count == 12 && menu.Compact && Mathf.IsEqualApprox(menu.OuterRadius, 150f) && Mathf.IsEqualApprox(menu.InnerRadius, 55f), $"twelve slices are compact, on the 300 px circle of the spec: {menu.OuterRadius}/{menu.InnerRadius}");
            menu.HoverForTesting(new Vector2(0, -100));
            Require(menu.HoverIndexForTesting == 0, "hover reads the real ring: straight up, between the radii, is slice 0");
            menu.HoverForTesting(new Vector2(0, -170));
            Require(menu.HoverIndexForTesting == -1, "outside the ring is no slice");
            menu.Close();

            var panelLog = new List<string>();
            menu.Open(new Vector2(500, 400), "Alvo", "user", [Leaf("A"), new RadialItem { Label = "Interagir", SidePanel = true, Children = [new RadialItem { Label = "Cumprimentar", Run = () => panelLog.Add("cumprimentar") }, new RadialItem { Label = "Abraçar", Enabled = false }, new RadialItem { Label = "Dançar", Run = () => panelLog.Add("dancar") }] }, Leaf("C"), Leaf("D")]);
            menu.PickForTesting(1);
            Require(menu.SidePanelOpen && menu.Depth == 1 && menu.PanelLabels.SequenceEqual(["Cumprimentar", "Abraçar", "Dançar"]) && menu.Labels.SequenceEqual(["A", "Interagir", "C", "D"]), "a side panel lists the choices and leaves the circle as it was");
            var panel = menu.PanelRectForTesting; var screen = menu.GetViewportRect().Size;
            Require(panel.Position.X >= 0 && panel.End.X <= screen.X && panel.Position.Y >= 0 && panel.End.Y <= screen.Y, "the side panel stays on the screen");
            menu.PickPanelForTesting(1);
            Require(menu.IsOpen && menu.SidePanelOpen && panelLog.Count == 0, "a disabled row does nothing");
            menu.BackForTesting();
            Require(!menu.SidePanelOpen && menu.IsOpen, "Esc closes the panel first and keeps the circle");
            menu.PickForTesting(1); menu.PickPanelForTesting(2);
            Require(!menu.IsOpen && panelLog.SequenceEqual(["dancar"]), "a panel row runs and closes the menu");
            menu.Open(new Vector2(screen.X - 20, 300), "Borda", "user", [Leaf("A"), new RadialItem { Label = "Lado", SidePanel = true, Children = [Leaf("x"), Leaf("y")] }]);
            menu.PickForTesting(1); panel = menu.PanelRectForTesting;
            Require(panel.End.X <= screen.X && panel.Position.X >= 0, "near the right edge the panel opens on the left side");
            menu.Close();

            var deleted = 0;
            menu.Open(new Vector2(500, 400), "Alvo", "cube", [new RadialItem { Label = "Excluir", Icon = "trash", Destructive = true, Confirm = "Excluir o objeto?", Run = () => deleted++ }, Leaf("B")]);
            menu.PickForTesting(0);
            Require(menu.IsOpen && deleted == 0 && menu.Labels.SequenceEqual(["Confirmar", "Cancelar"]) && menu.Title == "Excluir o objeto?", "a dangerous slice asks before it runs");
            menu.PickForTesting(1);
            Require(menu.IsOpen && deleted == 0 && menu.Labels.Count == 2 && menu.Depth == 1, "Cancelar goes back and nothing is deleted");
            menu.PickForTesting(0); menu.PickForTesting(0);
            Require(!menu.IsOpen && deleted == 1, "Confirmar runs it once");

            RadialStyle.Size = 60; menu.Open(new Vector2(500, 400), "Pequeno", "user", [Leaf("A"), Leaf("B")]);
            Require(Mathf.IsEqualApprox(menu.OuterRadius, 90f), "the size setting scales the circle"); menu.Close();
            RadialStyle.Size = 150; menu.Open(new Vector2(500, 400), "Grande", "user", [Leaf("A"), Leaf("B")]);
            Require(menu.OuterRadius > 150f && menu.OuterRadius <= screen.Y * .4f + .1f, "the circle never grows past what fits the screen"); menu.Close();
            RadialStyle.Size = 100;
            Require(RadialStyle.Fit(new Vector2(1280, 300), 100) < 1f, "a very small screen shrinks the circle");

            GD.Print("event=radial_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=radial_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
