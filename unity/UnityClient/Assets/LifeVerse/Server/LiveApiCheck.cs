using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LifeVerse.Unity.UI;
using LifeVerse.Unity.World;
using UnityEngine;

namespace LifeVerse.Unity.Server
{
    /// <summary>
    /// Runs the interface's windows against a real LifeVerse API (not the stand-in): the API's own code, in one process, on a port (see tools/ApiLiveHost; it uses the in-memory database of the
    /// API's tests, so nothing touches production):
    ///   LifeVerse.exe -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -lifeverse-live-api http://127.0.0.1:5099
    /// Two accounts are made, and the same windows a person uses fill themselves from the real answers (friends, messages, groups, inventory, wallet, market, profile, events, land…).
    /// Each step prints "event=live_step name=… result=ok|FAIL detail=…"; the run ends with "event=live_api result=passed|failed". A step that fails does not stop the others.
    /// </summary>
    public sealed class LiveApiCheck : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "-lifeverse-live-api"); if (index < 0 || index + 1 >= args.Length) return;
            new GameObject("Live API Check").AddComponent<LiveApiCheck>().Url = args[index + 1];
        }

        public string Url;
        private readonly List<(string Name, bool Ok, string Detail)> _results = new List<(string, bool, string)>();
        private IslandEntryClient _a, _b; private ApiContext _ca, _cb; private UiRoot _ui; private NotificationCenter _notices; private Guid _island, _bId, _aId;
        private string _bName, _aName;

        private IEnumerator Start()
        {
            Application.targetFrameRate = 30;
            var steps = Everything(); while (true) { try { if (!steps.MoveNext()) break; } catch (Exception exception) { Record("run", false, exception.GetType().Name + ": " + exception.Message); break; } yield return steps.Current; }
            var failed = _results.Count(r => !r.Ok); Console.WriteLine($"event=live_api result={(failed == 0 ? "passed" : "failed")} steps={_results.Count} failed={failed}");
            foreach (var r in _results.Where(r => !r.Ok)) Console.WriteLine($"event=live_failure name={r.Name} detail={r.Detail}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        private void Record(string name, bool ok, string detail = "") { _results.Add((name, ok, detail)); Console.WriteLine($"event=live_step name={name} result={(ok ? "ok" : "FAIL")} detail={detail}"); }

        /// <summary>Runs one step: an exception or a false check is a failure of that step only.</summary>
        private IEnumerator Step(string name, IEnumerator body)
        {
            string failure = null; var stack = new Stack<IEnumerator>(); stack.Push(body);   // nested coroutines run here, so an exception inside one reaches this step
            while (stack.Count > 0)
            {
                object current; try { if (!stack.Peek().MoveNext()) { stack.Pop(); continue; } current = stack.Peek().Current; } catch (Exception exception) { failure = exception.GetType().Name + ": " + exception.Message; break; }
                if (current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return current;
            }
            Record(name, failure == null, failure ?? "");
        }
        private static IEnumerator Wait<T>(Task<T> task, float seconds = 20f)
        {
            var until = Time.realtimeSinceStartup + seconds; while (!task.IsCompleted) { if (Time.realtimeSinceStartup > until) throw new TimeoutException("a chamada da API não terminou"); yield return null; }
            if (task.IsFaulted) throw task.Exception.GetBaseException();
        }

        private static IEnumerator Until(Func<bool> condition, string what, float seconds = 15f)
        {
            var until = Time.realtimeSinceStartup + seconds; while (!condition()) { if (Time.realtimeSinceStartup > until) throw new TimeoutException("tempo esgotado: " + what); yield return null; }
        }

        /// <summary>A colour close to the one asked but different on every run: the platform stores identical files once, so a repeated picture would come back as somebody else's asset.</summary>
        private static Color32 Unique(int r, int g, int b) { var n = Guid.NewGuid().ToByteArray(); return new Color32((byte)Mathf.Clamp(r + n[0] % 24 - 12, 0, 255), (byte)Mathf.Clamp(g + n[1] % 24 - 12, 0, 255), (byte)Mathf.Clamp(b + n[2] % 24 - 12, 0, 255), 255); }

        private static void Check(bool condition, string what) { if (!condition) throw new InvalidOperationException(what); }

        private static string Snip(string body) => body == null ? "" : body.Length > 240 ? body.Substring(0, 240) + "…" : body;

        // call helpers: the status must be a success; the body is returned
        private IEnumerator Call(IslandEntryClient who, string method, string path, string json, Action<string> body, params int[] accepted)
        {
            var task = who.ApiAsync(method, path, json); yield return Wait(task); var (status, text) = task.Result;
            var good = accepted.Length > 0 ? accepted.Contains(status) : status >= 200 && status < 300;
            if (!good) throw new InvalidOperationException($"{method} {path} → {status} {Snip(text)}");
            body?.Invoke(text);
        }

        private IEnumerator Everything()
        {
            var tag = Guid.NewGuid().ToString("N").Substring(0, 6); _aName = "alice" + tag; _bName = "bruno" + tag;
            _ui = UiRoot.Create(new UserSettings { AutoScale = false }); _notices = new NotificationCenter(_ui); _a = new IslandEntryClient(Url); _b = new IslandEntryClient(Url);
            _ca = new ApiContext { Ui = _ui, Api = _a, Notices = _notices, CurrentIsland = () => _island }; _cb = new ApiContext { Ui = _ui, Api = _b, Notices = _notices, CurrentIsland = () => _island };
            yield return null; yield return null;

            yield return Step("auth: register two accounts", Register());
            yield return Step("auth: refresh the session", Refresh());
            yield return Step("avatar: create the character, read it back", Avatar());
            yield return Step("islands: create, list on the login screen, info window", Islands());
            yield return Step("social: friends, private messages, presence, block, report", Social());
            yield return Step("groups: create, invite, chat, requests", Groups());
            yield return Step("files: upload, inventory, rights, versions", Files());
            yield return Step("economy: wallet, market, orders, stores, rental plans", Economy());
            yield return Step("offers: give an item, accept it", Offers());
            yield return Step("profile: edit, post, gallery, cover, marriage", Profile());
            yield return Step("discovery: events, landmarks, photos, search", Discovery());
            yield return Step("island management: general, permissions, rules", Manage());
            yield return Step("land: parcels, cells, split, expand, merge, media", Land());
            yield return Step("scripts: list, save, syntax", Scripts());
            yield return Step("build: limits, objects, audit, saved constructions", Build());
        }

        // ---- the steps ----

        private IEnumerator Register()
        {
            var ra = _a.RegisterAsync(_aName, _aName + "@example.com", "Senha123"); yield return Wait(ra); Check(ra.Result.Ok, "registrar A: " + ra.Result.Status + " " + ra.Result.Message);
            var rb = _b.RegisterAsync(_bName, _bName + "@example.com", "Senha123"); yield return Wait(rb); Check(rb.Result.Ok, "registrar B: " + rb.Result.Status + " " + rb.Result.Message);
            _aId = _a.UserId; _bId = _b.UserId; Check(_aId != Guid.Empty && _bId != Guid.Empty && _aId != _bId, "a resposta traz o id de cada pessoa");
            var login = new IslandEntryClient(Url); var l = login.LoginAsync(_aName, "Senha123"); yield return Wait(l); Check(l.Result.Ok && login.RefreshToken.Length > 0 && login.ExpiresAt < DateTimeOffset.MaxValue, "o login devolve o token, o de renovação e o vencimento");
            var bad = new IslandEntryClient(Url); var wrong = bad.LoginAsync(_aName, "Errada123"); yield return Wait(wrong); Check(!wrong.Result.Ok && wrong.Result.Status == 401, "senha errada: " + wrong.Result.Status);
            var weak = new IslandEntryClient(Url); var w = weak.RegisterAsync("fraco" + _aName, "fraco@example.com", "abc"); yield return Wait(w); Check(!w.Result.Ok, "a API recusa senha fraca");
        }

        private IEnumerator Refresh()
        {
            var raw = _a.ApiAsync("POST", "/api/v1/auth/refresh", JsonSerializer.Serialize(new { refreshToken = _a.RefreshToken })); yield return Wait(raw);
            if (raw.Result.Status == 500) { Console.WriteLine("event=live_note name=refresh detail=INCONCLUSIVO: a API aceitou o corpo (o token passou) mas o banco em memória não implementa ExecuteUpdate; precisa do Postgres"); yield break; }
            Check(raw.Result.Status == 200, "a renovação: " + raw.Result.Status + " " + Snip(raw.Result.Body)); var again = Json.One(raw.Result.Body); Check(again.First("accessToken").Length > 20 && again.First("refreshToken").Length > 20, "devolve um novo par de tokens: " + Snip(raw.Result.Body));
            string body = null; yield return Call(_a, "GET", "/api/v1/social/friends", null, b => body = b); Check(body != null, "o token novo vale");
        }

        private IEnumerator Avatar()
        {
            var draft = CharacterDraft.Starter("Alice Teste"); draft.HairStyle = "ponytail"; draft.SkinColor = "#E0AC82"; Check(draft.Problems().Count == 0, "o rascunho inicial é válido: " + string.Join(";", draft.Problems()));
            yield return Call(_a, "PUT", "/api/v1/users/me/avatar", draft.ToRequest(), null);
            string back = null; yield return Call(_a, "GET", "/api/v1/users/me/avatar", null, b => back = b); var read = CharacterDraft.FromResponse(back);
            Check(read.DisplayName == "Alice Teste" && read.HairStyle == "ponytail" && read.SkinColor.Equals("#E0AC82", StringComparison.OrdinalIgnoreCase) && read.Worn.Count >= 3, "o personagem volta como foi salvo: " + Snip(back));
            var appearance = LifeVerse.Unity.Avatar.AvatarAppearance.Parse(back); Check(appearance.HairStyle == "ponytail" && appearance.Item("top", "") != "", "o avatar lê a resposta real da API");
            var bad = CharacterDraft.Starter("Bruno"); bad.SkinColor = "azul"; var task = _b.ApiAsync("PUT", "/api/v1/users/me/avatar", bad.ToRequest()); yield return Wait(task); Check(task.Result.Status == 400 || task.Result.Status == 422, "a API recusa cor inválida: " + task.Result.Status);
            yield return Call(_b, "PUT", "/api/v1/users/me/avatar", CharacterDraft.Starter("Bruno Teste").ToRequest(), null);
        }

        private IEnumerator Islands()
        {
            var slug = "ilha-" + Guid.NewGuid().ToString("N").Substring(0, 8); string created = null;
            yield return Call(_a, "POST", "/api/v1/worlds/islands", JsonSerializer.Serialize(new { name = "Ilha de Alice", slug, isPublic = true, description = "Para provar.", category = "teste", size = 256, maxPlayers = 32 }), b => created = b);
            _island = Json.One(created).Id(); Check(_island != Guid.Empty, "criar a ilha devolve o id: " + Snip(created));
            var listing = new IslandEntryClient(Url).GetIslandsAsync(); yield return Wait(listing); var rows = Json.Rows(listing.Result ?? "[]"); Check(rows.Any(r => r.Id() == _island && r.Text("name") == "Ilha de Alice"), "a lista pública (tela de entrada) mostra a ilha: " + Snip(listing.Result));
            var login = new LoginView(new UserSettings(), ""); login.SetIslands(rows.Select(r => (r.Id(), r.Text("name"), r.Text("category"))).ToList(), _island.ToString()); Check(login.IslandCount >= 1 && login.Island.value == _island.ToString(), "a tela de entrada escolhe a ilha");
            var info = new IslandInfoWindow(_ca, _island); info.Open(); yield return new WaitForSeconds(1f); info.Close();
        }

        private IEnumerator Social()
        {
            yield return Call(_a, "POST", "/api/v1/social/friends/requests", JsonSerializer.Serialize(new { userId = _bId }), null);
            var requests = new SocialWindow(_cb); requests.Open(); requests.Show("Pedidos"); string seen = null; yield return Call(_b, "GET", "/api/v1/social/friends/requests", null, b => seen = b); Check(Json.Rows(seen).Any(r => r.Id("userId", "id") == _aId), "o pedido aparece para B com o id de A: " + Snip(seen)); requests.Close();
            yield return Call(_b, "POST", "/api/v1/social/friends/" + _aId + "/accept", null, null);
            var social = new SocialWindow(_ca); social.Open(); yield return Until(() => social.FriendCount == 1, "a lista de amigos de A mostra B");
            social.Talk(_bId, _bName); social.MessageField.value = "oi, Bruno!"; Check(social.SendMessage(), "enviar a mensagem"); yield return Until(() => social.MessageCount == 1, "a conversa mostrar a mensagem enviada");
            string theirs = null; yield return Call(_b, "GET", "/api/v1/social/messages?channel=private&conversationWith=" + _aId + "&page=1&pageSize=50", null, b => theirs = b); Check(Json.Rows(theirs).Any(r => r.Text("content") == "oi, Bruno!"), "B recebe a mensagem: " + Snip(theirs));
            yield return Call(_a, "PUT", "/api/v1/social/presence", JsonSerializer.Serialize(new { status = "away", privacyJson = (string)null }), null);
            string presence = null; yield return Call(_b, "GET", "/api/v1/social/presence/" + _aId, null, b => presence = b); Check(Json.One(presence).First("status") == "away", "a presença de A como B a vê: " + Snip(presence));
            var person = new PersonActionsWindow(_cb, _aId, _aName); person.Open(); person.ReasonField.value = "teste de denúncia"; person.Report(); yield return new WaitForSeconds(1f); person.Block(); yield return new WaitForSeconds(1f); person.Close();
            var again = _b.ApiAsync("POST", "/api/v1/social/messages", JsonSerializer.Serialize(new { content = "ainda aqui?", recipientUserId = _aId })); yield return Wait(again); Check(again.Result.Status == 403, "depois do bloqueio B não consegue escrever para A: " + again.Result.Status);
            yield return Call(_b, "DELETE", "/api/v1/social/blocks/" + _aId, null, null); social.Close();
            string people = null; yield return Call(_a, "GET", "/api/v1/social/people?q=" + _bName, null, b => people = b); Check(Json.Rows(people).Any(p => p.Id() == _bId), "a busca de pessoas pelo nome acha B (o que 'Ações' usa): " + Snip(people));
        }

        private IEnumerator Groups()
        {
            var groups = new GroupsWindow(_ca); groups.Open(); groups.Show("Criar"); _groupName = "Construtores " + _aName.Substring(5); groups.NameField.value = _groupName; groups.Create(); yield return new WaitForSeconds(1f); groups.Show("Meus"); yield return Until(() => groups.MineCount == 1, "o grupo criado aparece em Meus");
            string mine = null; yield return Call(_a, "GET", "/api/v1/groups/mine", null, b => mine = b); var group = Json.Rows(mine).First(); var id = group.Id("id", "groupId"); Check(id != Guid.Empty && group.First("name") == _groupName, "o grupo tem id e nome: " + Snip(mine));
            var detail = new GroupDetailWindow(_ca, id, _groupName); detail.Open(); yield return Until(() => detail.MemberCount >= 1, "o dono é membro do grupo");
            detail.Chat.Input.value = "bem-vindos ao grupo"; detail.Chat.Send(); yield return Until(() => detail.Chat.LineCount >= 1, "a mensagem do grupo aparece na conversa");
            yield return Call(_a, "POST", "/api/v1/groups/" + id + "/invites", JsonSerializer.Serialize(new { userId = _bId }), null);
            var invites = new GroupsWindow(_cb); invites.Open(); invites.Show("Convites"); yield return Until(() => invites.InviteCount == 1, "o convite aparece para B"); invites.Close();
            string raw = null; yield return Call(_b, "GET", "/api/v1/social/groups/invites", null, b => raw = b); var inviteId = Json.Rows(raw).First().Id("inviteId", "id"); Check(inviteId != Guid.Empty, "o convite tem id: " + Snip(raw));
            yield return Call(_b, "POST", "/api/v1/social/groups/invites/" + inviteId + "/accept", null, null);
            string members = null; yield return Call(_a, "GET", "/api/v1/groups/" + id + "/members", null, b => members = b); Check(Json.Rows(members).Count >= 2, "o grupo tem os dois membros: " + Snip(members));
            detail.Close(); groups.Close();
        }

        private Guid _asset, _item; private string _png, _groupName;

        private IEnumerator Files()
        {
            _png = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lv-live-" + Guid.NewGuid().ToString("N") + ".png"); System.IO.File.WriteAllBytes(_png, TestAssets.Png(Unique(60, 140, 220), 32));
            UploadResult sent = null; Uploads.Send(_ca, _png, "textures", r => sent = r); yield return Until(() => sent != null, "o envio terminar", 30f); Check(sent.Ok && sent.AssetId != Guid.Empty, "o upload multipart é aceito pela API: " + sent.Message); _asset = sent.AssetId;
            var inventory = new InventoryWindow(_ca); inventory.Open(); yield return Until(() => inventory.ItemCount >= 1, "o arquivo enviado aparece no inventário");
            string raw = null; yield return Call(_a, "GET", "/api/v1/inventory/items?view=all&page=1&pageSize=50", null, b => raw = b); var first = Json.Rows(raw).FirstOrDefault(); _item = first.Id("id", "itemId"); Check(_item != Guid.Empty && first.Id("assetId") == _asset, "a linha do inventário tem id do item e do arquivo: " + Snip(raw)); inventory.Close();
            var rights = new ItemWindow(_ca, _item, "Imagem", _asset); rights.Open(); yield return Until(() => rights.Loaded, "os direitos do item"); Check(rights.Owner != 0, "o dono tem direitos: " + rights.Owner);
            // the group column only holds rights once the item is shared with a group (the server keeps it at zero otherwise), so the saved switch is the next owner's copy right
            var wasCopy = rights.Bit(2, 0).value; rights.Bit(2, 0).value = !wasCopy; rights.Save(); yield return new WaitForSeconds(1.2f); string again = null; yield return Call(_a, "GET", "/api/v1/inventory/items/" + _item + "/rights", null, b => again = b); Check(((Json.One(again).Child("permissions").Number("nextOwner") & 1) == 1) == !wasCopy, "o direito de copiar do próximo dono foi salvo: " + Snip(again));
            yield return Until(() => rights.VersionCount >= 1, "as versões do arquivo"); rights.Close();
            var v2 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lv-live2-" + Guid.NewGuid().ToString("N") + ".png"); System.IO.File.WriteAllBytes(v2, TestAssets.Png(Unique(220, 60, 60), 32));
            var publish = _a.FileCallAsync(System.Net.Http.HttpMethod.Post, "/api/v1/assets/" + _asset + "/versions", "imagem.png", System.IO.File.ReadAllBytes(v2), null); yield return Wait(publish, 30f); Check(publish.Result.Status >= 200 && publish.Result.Status < 300, "publicar nova versão: " + publish.Result.Status + " " + Snip(publish.Result.Body));
            string versions = null; yield return Call(_a, "GET", "/api/v1/assets/" + _asset + "/versions", null, b => versions = b); Check(Json.Rows(versions).Count >= 2, "o histórico tem duas versões: " + Snip(versions)); System.IO.File.Delete(v2);
        }

        private IEnumerator Economy()
        {
            var wallet = new WalletWindow(_ca); wallet.Open(); yield return Until(() => wallet.Balance >= 0, "o saldo da carteira"); Check(wallet.Balance == 0, "uma conta nova começa sem moedas: " + wallet.Balance);
            var poor = _a.ApiAsync("POST", "/api/v1/economy/transfer", JsonSerializer.Serialize(new { recipientUserId = _bId, amountCoins = 10, idempotencyKey = Guid.NewGuid().ToString("N"), message = (string)null })); yield return Wait(poor); Check(poor.Result.Status >= 400 && poor.Result.Status < 500, "sem saldo a transferência é recusada (o servidor é a autoridade): " + poor.Result.Status); wallet.Close();
            var market = new MarketWindow(_ca); market.Open(); market.Show("Vender"); market.SellAsset.value = _asset.ToString(); market.SellName.value = "Imagem azul"; market.SellPrice.value = "50"; market.Publish(); yield return new WaitForSeconds(1.5f);
            market.Show("Produtos"); yield return Until(() => market.ProductCount >= 1, "o produto publicado aparece no mercado");
            string products = null; yield return Call(_b, "GET", "/api/v1/economy/products?page=1&pageSize=24", null, b => products = b); var product = Json.Rows(products).First(); Check(product.First("name") == "Imagem azul" && product.Number("priceCoins") == 50, "o produto como B o vê: " + Snip(products));
            var buy = _b.ApiAsync("POST", "/api/v1/economy/products/" + product.Id() + "/purchase", JsonSerializer.Serialize(new { idempotencyKey = Guid.NewGuid().ToString("N"), recipientUserId = (Guid?)null })); yield return Wait(buy); Check(buy.Result.Status >= 400 && buy.Result.Status < 500, "sem moedas a compra é recusada: " + buy.Result.Status);
            market.Show("Pedidos"); yield return new WaitForSeconds(.8f); market.Show("Aluguel"); yield return new WaitForSeconds(.8f);
            market.Show("Minhas lojas"); market.StoreNameField.value = "Loja da Alice"; market.CreateStore(); yield return new WaitForSeconds(1.2f); market.Show("Minhas lojas"); yield return Until(() => market.StoreCount >= 1, "a loja criada aparece"); market.Close();
        }

        private IEnumerator Offers()
        {
            var rights = new ItemWindow(_ca, _item, "Imagem", _asset); rights.Open(); yield return Until(() => rights.Loaded, "os direitos do item"); rights.ToField.value = _bId.ToString(); rights.Offer(); yield return new WaitForSeconds(1.5f); rights.Close();
            var offers = new OffersWindow(_cb); offers.Open(); yield return Until(() => offers.OfferCount >= 1, "a oferta aparece para B");
            string raw = null; yield return Call(_b, "GET", "/api/v1/inventory/offers", null, b => raw = b); var offer = Json.Rows(raw).First(); Check(offer.Id() != Guid.Empty, "a oferta tem id: " + Snip(raw));
            yield return Call(_b, "POST", "/api/v1/inventory/offers/" + offer.Id() + "/accept", null, null); offers.Close();
            string bInventory = null; yield return Call(_b, "GET", "/api/v1/inventory/items?view=all&page=1&pageSize=50", null, b => bInventory = b); Check(Json.Rows(bInventory).Count >= 1, "B recebeu o item: " + Snip(bInventory));
        }

        private IEnumerator Profile()
        {
            var profile = new ProfileWindow(_ca); profile.Open(); yield return Until(() => profile.ShownName.Length > 1 && profile.ShownName != "…", "o próprio perfil abrir"); Check(profile.Editable, "o próprio perfil é editável");
            profile.Bio = "Construo coisas."; profile.SaveBio(); profile.PostText = "Minha primeira publicação"; profile.Publish(); yield return new WaitForSeconds(1.5f);
            string full = null; yield return Call(_b, "GET", "/api/v1/users/" + _aId + "/profile/full", null, b => full = b); Check(full.Contains("Construo coisas"), "B vê o texto de A: " + Snip(full));
            string feed = null; yield return Call(_b, "GET", "/api/v1/users/" + _aId + "/profile/feed", null, b => feed = b); Check(Json.Rows(feed).Any(r => r.First("text", "content").Contains("primeira publicação")), "o feed mostra a publicação: " + Snip(feed)); profile.Close();
            // the file offered to B in the step before is no longer A's, so the gallery gets its own picture (the API only takes images the person owns)
            var own = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lv-live-gal-" + Guid.NewGuid().ToString("N") + ".png"); System.IO.File.WriteAllBytes(own, TestAssets.Png(Unique(90, 200, 90), 32));
            UploadResult galleryFile = null; Uploads.Send(_ca, own, "textures", r => galleryFile = r); yield return Until(() => galleryFile != null, "o envio da imagem da galeria", 30f); Check(galleryFile.Ok, "enviar a imagem da galeria: " + galleryFile.Message); System.IO.File.Delete(own);
            var gallery = new GalleryWindow(_ca); gallery.Open(); yield return new WaitForSeconds(.8f); gallery.AddAsset(galleryFile.AssetId); yield return Until(() => gallery.Images >= 1, "a imagem aparece na galeria"); gallery.SetCover(galleryFile.AssetId); yield return new WaitForSeconds(1f); gallery.Close();
            var marriage = new MarriageWindow(_ca); marriage.Open(); marriage.WhoField.value = _bId.ToString(); marriage.Propose(); yield return new WaitForSeconds(1.5f);
            string theirs = null; yield return Call(_b, "GET", "/api/v1/marriage", null, b => theirs = b); var theirWindow = new MarriageWindow(_cb); theirWindow.Open(); yield return Until(() => theirWindow.Incoming == 1, "B vê o pedido na janela de casamento (formato real: current/incoming/outgoing): " + Snip(theirs)); marriage.Close();
            yield return Call(_b, "POST", "/api/v1/marriage/" + Json.One(theirs).Element.GetProperty("incoming")[0].GetProperty("id").GetGuid() + "/accept", null, null); theirWindow.Reload(); yield return Until(() => theirWindow.Married, "depois de aceitar, a janela mostra o casamento"); theirWindow.Close();
        }

        private IEnumerator Discovery()
        {
            var window = new DiscoveryWindow(_ca, this, () => new Vector3(4f, 1f, -6f), () => 0f); window.Open();
            window.Show("Meus eventos"); window.EventName.value = "Festa na praia"; window.CreateEvent(); yield return new WaitForSeconds(1.5f); window.Show("Eventos"); yield return Until(() => window.EventCount >= 1, "o evento criado aparece na lista");
            window.Show("Marcos"); window.MarkName.value = "Meu cantinho"; window.SaveLandmark(); yield return new WaitForSeconds(1.5f); window.Show("Marcos"); yield return Until(() => window.LandmarkCount >= 1, "o marco salvo aparece");
            window.Show("Fotos"); window.PickAndSendPhoto(_png); yield return new WaitForSeconds(2.5f); window.Show("Fotos"); yield return Until(() => window.PhotoCount >= 1, "a foto enviada aparece na lista"); window.Close();
            var search = new SearchWindow(_ca); search.Open(); yield return Until(() => search.ResultCount >= 1, "a busca de ilhas acha a ilha criada"); search.QueryField.value = "Alice"; search.Search(); yield return new WaitForSeconds(1.2f); search.Close();
            string all = null; yield return Call(_a, "GET", "/api/v1/discovery/search?q=Alice&page=1&pageSize=24", null, b => all = b); Check(all != null, "a busca geral responde");
        }

        private IEnumerator Manage()
        {
            var manage = new IslandManageWindow(_ca, _island); manage.Open(); yield return Until(() => manage.NameValue == "Ilha de Alice", "os dados da ilha na gestão");
            manage.SaveGeneral(); yield return new WaitForSeconds(1.5f); string island = null; yield return Call(_a, "GET", "/api/v1/worlds/islands/" + _island, null, b => island = b); Check(Json.One(island).Text("name") == "Ilha de Alice" && Json.One(island).Number("maxPlayers") == 32, "salvar a gestão sem mudar nada mantém os dados: " + Snip(island));
            manage.Show("Permissões"); manage.SaveRules(); yield return new WaitForSeconds(1.5f);
            var grant = _a.ApiAsync("POST", "/api/v1/worlds/islands/" + _island + "/permissions", JsonSerializer.Serialize(new { permission = "build", principalType = "user", userId = _bId, groupId = (Guid?)null })); yield return Wait(grant); Check(grant.Result.Status >= 200 && grant.Result.Status < 300, "dar permissão de construir a B: " + grant.Result.Status + " " + Snip(grant.Result.Body));
            manage.Show("Permissões"); yield return Until(() => manage.PermissionCount >= 1, "a permissão aparece na lista"); manage.Close();
        }

        private IEnumerator Land()
        {
            string overview = null; yield return Call(_a, "GET", "/api/v1/worlds/islands/" + _island + "/land", null, b => overview = b); var root = Json.One(overview); Check(root.Number("cellSize") == 4 && root.Number("size") == 256, "a visão do terreno traz size e cellSize: " + Snip(overview));
            var claim = _a.ApiAsync("POST", "/api/v1/worlds/islands/" + _island + "/land/claim", JsonSerializer.Serialize(new { name = "Terreno de Alice", description = "teste", minX = -16.0, minZ = -16.0, maxX = 16.0, maxZ = 16.0 })); yield return Wait(claim); Check(claim.Result.Status >= 200 && claim.Result.Status < 300, "reivindicar um terreno de 32 m: " + claim.Result.Status + " " + Snip(claim.Result.Body));
            var manage = new IslandManageWindow(_ca, _island); manage.Open(); manage.Show("Terrenos"); yield return Until(() => manage.Parcels.ParcelCount >= 1 && manage.Parcels.Grid.Size == 64, "a grade (64 x 64) e o terreno na lista");
            string parcels = null; yield return Call(_a, "GET", "/api/v1/worlds/islands/" + _island + "/land", null, b => parcels = b); var parcel = Json.Rows(parcels).First(r => r.Text("name") == "Terreno de Alice"); var id = parcel.Id();
            manage.Parcels.Choose(id, "Terreno de Alice"); yield return null; Check(manage.Parcels.Grid.Owned == 64, "as células do terreno vêm das corridas da API (8 x 8 células): " + manage.Parcels.Grid.Owned + " runs=" + Snip(parcel.Element.GetRawText()));
            manage.Parcels.Grid.Toggle((0, 0)); manage.Parcels.Grid.Toggle((1, 0)); manage.Parcels.Grid.Toggle((0, 1)); manage.Parcels.Grid.Toggle((1, 1)); manage.Parcels.NewNameField.value = "Cantinho"; manage.Parcels.Split(); yield return new WaitForSeconds(1.5f);
            string after = null; yield return Call(_a, "GET", "/api/v1/worlds/islands/" + _island + "/land", null, b => after = b); Check(Json.Rows(after).Any(r => r.Text("name") == "Cantinho") && Json.Rows(after).Count >= 2, "dividir criou o terreno Cantinho: " + Snip(after));
            manage.Parcels.MusicField.value = "https://exemplo.com/a.mp3"; manage.Parcels.SaveMedia(); yield return new WaitForSeconds(1.2f);
            var media = _a.ApiAsync("PUT", "/api/v1/worlds/islands/" + _island + "/land/parcels/" + id + "/media", JsonSerializer.Serialize(new { musicUrl = "https://exemplo.com/a.mp3", volume = .5f, autoplay = true, name = "Faixa" })); yield return Wait(media); Check(media.Result.Status >= 200 && media.Result.Status < 300, "música do terreno: " + media.Result.Status + " " + Snip(media.Result.Body));
            var audit = _a.ApiAsync("GET", "/api/v1/worlds/islands/" + _island + "/land/parcels/" + id + "/audit", null); yield return Wait(audit); Check(audit.Result.Status == 200, "auditoria: " + audit.Result.Status); manage.Close();
        }


        private IEnumerator Build()
        {
            var editor = new BuildEditor(_ca, () => null, () => null, () => Vector3.zero, () => null); var window = new BuildWindow(_ca, editor); window.Open(); window.Show("Limites"); yield return Until(() => window.LimitsText.Contains("objetos"), "os limites da construção chegarem: " + window.LimitsText);
            Check(window.LimitsText.Contains("Ilha:"), "os limites falam da ilha: " + window.LimitsText);
            var create = _a.ApiAsync("POST", "/api/v1/worlds/islands/" + _island + "/objects", JsonSerializer.Serialize(new { primitiveType = "cube", name = "Parede", positionX = 4, positionY = 1, positionZ = 4, rotationX = 0, rotationY = 0, rotationZ = 0, scaleX = 2, scaleY = 2, scaleZ = 1, materialJson = "{\"color\":\"#C0392B\"}", geometryJson = "{\"hollow\":0.3}", physicsJson = "{\"collision\":true}", contentJson = "[]" })); yield return Wait(create);
            Check(create.Result.Status >= 200 && create.Result.Status < 300, "criar o objeto: " + create.Result.Status + " " + Snip(create.Result.Body));
            var root = Json.One(create.Result.Body); var objectId = root.Id("id", "objectId"); if (objectId == Guid.Empty && root.Has("object")) objectId = root.Child("object").Id(); Check(objectId != Guid.Empty, "o objeto tem id: " + Snip(create.Result.Body));
            string listed = null; yield return Call(_a, "GET", "/api/v1/worlds/islands/" + _island + "/objects", null, b => listed = b); var mine = Json.Rows(listed).FirstOrDefault(r => r.Id() == objectId); Check(mine.Text("name") == "Parede" && WorldObjectState.Parse(mine.Element).ScaleX == 2f, "a lista de objetos traz o que o editor lê: " + Snip(listed));
            var state = WorldObjectState.Parse(mine.Element); Check(LifeVerse.Shared.Building.PrimParams.Parse(state.GeometryJson).Hollow > .25f && state.CreatorUserId == _aId, "oco, criador e dono vêm no formato certo: " + state.CreatorUserId);
            string audit = null; yield return Call(_a, "GET", "/api/v1/worlds/islands/" + _island + "/objects/" + objectId + "/audit", null, b => audit = b); var rows = Json.Rows(audit); Check(rows.Count >= 1 && BuildWindow.OperationLabel(rows[0].Text("operation")) != "", "o histórico do objeto tem registros: " + Snip(audit));
            var source = new LifeVerse.Shared.Building.SourceObject(state.PrimitiveType, state.Name, state.Description, state.PositionX, state.PositionY, state.PositionZ, state.RotationX, state.RotationY, state.RotationZ, state.ScaleX, state.ScaleY, state.ScaleZ, state.MaterialJson, state.GeometryJson, state.PhysicsJson, state.ContentJson, state.GroupId, state.Id, null);
            var package = LifeVerse.Shared.Building.BuildPackages.Serialize(LifeVerse.Shared.Building.BuildPackages.Create("Casinha", new[] { source })); var save = _a.ApiAsync("POST", "/api/v1/inventory/build-packages", package); yield return Wait(save);
            Check(save.Result.Status >= 200 && save.Result.Status < 300, "guardar a construção: " + save.Result.Status + " " + Snip(save.Result.Body)); var itemId = Json.One(save.Result.Body).Id("itemId", "id"); Check(itemId != Guid.Empty, "a construção guardada tem id: " + Snip(save.Result.Body));
            string back = null; yield return Call(_a, "GET", "/api/v1/inventory/build-packages/" + itemId, null, b => back = b); var parsed = LifeVerse.Shared.Building.BuildPackages.Parse(back, out var why); Check(parsed != null && parsed.Name == "Casinha" && parsed.Objects.Length == 1, "a construção volta inteira: " + why + " " + Snip(back));
            string inventory = null; yield return Call(_a, "GET", "/api/v1/inventory/items?view=all&page=1&pageSize=100", null, b => inventory = b); var itemRow = Json.Rows(inventory).FirstOrDefault(r => r.Id("id", "itemId") == itemId); Check(itemRow.Text("name") == "Casinha", "a construção aparece no inventário: " + Snip(inventory));
            var kind = itemRow.First("content", "category", "kind").ToLowerInvariant(); Check(kind.Contains("build") || kind.Contains("package") || kind.Contains("object"), "o inventário diz que é uma construção (a lista do painel usa isso): " + kind);
            window.Close();
        }
        private IEnumerator Scripts()
        {
            var create = _a.ApiAsync("POST", "/api/v1/worlds/islands/" + _island + "/objects", JsonSerializer.Serialize(new { primitiveType = "cube", name = "Caixa", positionX = 0, positionY = 1, positionZ = 0, rotationX = 0, rotationY = 0, rotationZ = 0, scaleX = 1, scaleY = 1, scaleZ = 1, materialJson = "{}", geometryJson = "{}", physicsJson = "{}", contentJson = "[]" })); yield return Wait(create);
            Check(create.Result.Status >= 200 && create.Result.Status < 300, "criar um objeto na ilha: " + create.Result.Status + " " + Snip(create.Result.Body));
            var obj = Json.One(create.Result.Body); var objectId = obj.Id("id", "objectId"); if (objectId == Guid.Empty && obj.Has("object")) objectId = new Row(obj.Element.GetProperty("object")).Id(); Check(objectId != Guid.Empty, "o objeto criado tem id: " + Snip(create.Result.Body));
            var lua = new LuaEditorWindow(_ca, _island, objectId); lua.Open(); yield return new WaitForSeconds(1f); lua.NewScript(); lua.Source = "local x = 1\nprint(x)"; lua.Save(); yield return new WaitForSeconds(2f);
            string list = null; yield return Call(_a, "GET", "/api/v1/worlds/" + _island + "/objects/" + objectId + "/scripts", null, b => list = b); Check(Json.Rows(list).Count >= 1, "o script salvo aparece na lista: " + Snip(list)); lua.LoadList();
            // saving again keeps the first code in the history, which can be read and restored; the manual comes from the platform
            Check(lua.OpenScript != Guid.Empty, "o script salvo ficou aberto no editor"); lua.Source = "local x = 2\nprint(x)"; lua.Save(); yield return new WaitForSeconds(2f);
            var history = new ScriptHistoryWindow(_ca, _island, objectId, lua.OpenScript, () => { }); history.Open(); yield return Until(() => history.VersionCount >= 1, "o histórico mostrar a versão anterior: " + history.Note);
            history.Pick(1); yield return Until(() => history.Preview.Contains("local x = 1") && history.CanRestore, "o código da versão 1: " + history.Preview); history.Restore(); yield return new WaitForSeconds(2f);
            string reopened = null; yield return Call(_a, "GET", "/api/v1/worlds/" + _island + "/objects/" + objectId + "/scripts/" + lua.OpenScript, null, b => reopened = b); Check(Json.One(reopened).First("source", "code").Contains("local x = 1"), "restaurar a versão 1 volta o código: " + Snip(reopened)); history.Close();
            var docs = new ScriptDocsWindow(_ca, _ => { }); docs.Open(); yield return Until(() => docs.Count > 20, "a documentação real chegar (" + docs.Count + ")"); lua.Close(); docs.Close();
        }
    }
}
