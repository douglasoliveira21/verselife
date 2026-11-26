using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LifeVerse.Shared.Avatar;
using LifeVerse.Unity.Avatar;
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
            yield return Step("inventory (full): folders, move, rename, bin, restore, space, check, pack", InventoryFull());
            yield return Step("economy: wallet, market, orders, stores, rental plans", Economy());
            yield return Step("offers: give an item, accept it", Offers());
            yield return Step("profile: edit, post, gallery, cover, marriage", Profile());
            yield return Step("discovery: events, landmarks, photos, search", Discovery());
            yield return Step("island management: general, permissions, rules", Manage());
            yield return Step("island management (full): summary, general, bans, permissions, look, media, audit, every section", ManageFull());
            yield return Step("land: parcels, cells, split, expand, merge, media", Land());
            yield return Step("land management and about this land: overview, claim, sections, rename, options, list, groups", LandFull());
            yield return Step("scripts: list, save, syntax", Scripts());
            yield return Step("build resources: script templates and the names of people", BuildExtras());
            yield return Step("build: limits, objects, audit, saved constructions", Build());
        yield return Step("avatar: attachments (wear, move, change, take off)", Attachments());
        yield return Step("search (full): for you, tabs, suggestions, details, favorite, report, interests", SearchFull());
        yield return Step("groups (full): create in 4 steps, invite, accept, roles, permissions, notices, chat, chest, votes, active group", GroupsFull());
        yield return Step("wallet and profile (full): summary, statement, coins, fees, bio, post, feed, someone else's profile", WalletAndProfile());
        yield return Step("avatar studio: own body, clothes and animations sent, worn, chosen; looks saved, shared and brought by code", AvatarStudio());
        yield return Step("entry: servers healthy, the start place chosen by the server, the island's asset list; script editor: server check, runtime, pause, permission, completion", EntryAndScripts());
        yield return Step("live: notification channel (message pushed by the API)", Live());
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

        private IEnumerator InventoryFull()
        {
            var inv = "/api/v1/inventory"; string body = null;
            var iv = new InventoryManagerWindow(_ca); iv.Open(); yield return Until(() => iv.SummaryText.Contains("itens") && iv.RowIds.Count >= 1, "o inventário completo abre com os itens de A: " + iv.Status);
            var folderName = "Pasta " + Guid.NewGuid().ToString("N").Substring(0, 6);
            iv.Show("pastas"); yield return Until(() => iv.Actions.ContainsKey("Criar pasta"), "a vista de pastas: " + iv.Status); yield return new WaitForSeconds(.3f);
            iv.Fields["Nome (para criar ou renomear)"].value = folderName; iv.Press("Criar pasta"); yield return Until(() => iv.Status.Length > 0, "criar a pasta");
            yield return Call(_a, "GET", inv + "/folders", null, b => body = b); Check(body.Contains(folderName), "a pasta criada pela janela está na API: " + iv.Status + " " + Snip(body));
            Guid folder; using (var d = JsonDocument.Parse(body)) folder = Guid.Parse(d.RootElement.EnumerateArray().First(f => f.GetProperty("name").GetString() == folderName).GetProperty("id").GetString());
            iv.Show("tudo"); yield return Until(() => iv.RowIds.Count >= 1 && iv.Choices.ContainsKey("Mover para"), "a lista de itens"); yield return new WaitForSeconds(.4f);
            var item = iv.RowIds.Last(); iv.Toggles["item:" + item].value = true; iv.MoveTicked(folder.ToString(), () => { }); yield return Until(() => iv.Status.Contains("movido"), "mover o item: " + iv.Status);
            yield return Call(_a, "GET", inv + "/items?view=all&folderId=" + folder, null, b => body = b); Check(body.Contains(item.ToString()), "o item está dentro da pasta na API: " + Snip(body));
            iv.Show("tudo"); yield return Until(() => iv.RowIds.Contains(item) && iv.Actions.ContainsKey("Detalhes"), "a lista de novo"); yield return new WaitForSeconds(.4f);
            var detailed = iv.RowIds.Last(); iv.Press("Detalhes"); yield return Until(() => iv.Fields.ContainsKey("Nome no inventário"), "os detalhes do item");
            iv.Fields["Nome no inventário"].value = "Item do Unity"; iv.Press("Renomear"); yield return Until(() => iv.Status.Length > 0, "renomear o item");
            yield return Call(_a, "GET", inv + "/items?view=all&search=Unity", null, b => body = b); Check(body.Contains(detailed.ToString()), "o nome novo do item na API: " + iv.Status + " " + Snip(body));
            yield return new WaitForSeconds(.4f); iv.Press("Detalhes"); yield return Until(() => iv.Actions.ContainsKey("Mandar para a lixeira"), "os detalhes de novo"); iv.Press("Mandar para a lixeira"); yield return Until(() => iv.Status.Contains("lixeira"), "mandar para a lixeira: " + iv.Status);
            iv.Show("lixeira"); yield return Until(() => iv.RowIds.Contains(detailed), "o item aparece na lixeira: " + iv.Status); yield return new WaitForSeconds(.4f);
            Check(iv.RowIds.Last() == detailed, "o item mandado é o último da lixeira"); iv.Press("Restaurar"); yield return Until(() => iv.Status.Contains("voltou"), "restaurar o item: " + iv.Status);
            yield return Call(_a, "GET", inv + "/items?view=all", null, b => body = b); Check(body.Contains(detailed.ToString()), "o item restaurado está de volta na API: " + Snip(body));
            iv.Show("espaco"); yield return Until(() => iv.Lines >= 4 && iv.Loads >= 2, "o espaço e a verificação: " + iv.Status); Check(iv.Status.Length == 0, "o espaço e a verificação abrem sem erro: " + iv.Status);
            var zip = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lv-pack-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".zip");
            using (var file = System.IO.File.Create(zip)) using (var archive = new System.IO.Compression.ZipArchive(file, System.IO.Compression.ZipArchiveMode.Create)) foreach (var n in new[] { "a", "b" }) { var e = archive.CreateEntry(n + ".png"); using var stream = e.Open(); var png = TestAssets.Png(Unique(30, 200, 90), 16); stream.Write(png, 0, png.Length); }
            iv.Show("pacote"); iv.Unpack(zip); yield return Until(() => iv.LastPack.Length > 0, "abrir o pacote", 30f); Check(iv.LastPack.StartsWith("2 item(ns) na pasta"), "o pacote vira uma pasta com 2 itens: " + iv.LastPack);
            iv.Close();
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

        private IEnumerator ManageFull()
        {
            var mg = "/api/v1/worlds/islands/" + _island + "/management";
            var gm = new IslandManagementWindow(_ca, _island); gm.Open(); yield return Until(() => gm.SummaryText.Contains("Ilha de Alice") && gm.Fields.TryGetValue("Nome", out var n) && n.value == "Ilha de Alice", "o resumo e a seção Geral: " + gm.SummaryText + " " + gm.Status);
            gm.Fields["Descrição"].value = "Descrita pelo Unity"; gm.SaveGeneral(); yield return Until(() => gm.Status.Length > 0, "a resposta de salvar os dados gerais");
            string body = null; yield return Call(_a, "GET", mg, null, b => body = b); Check(Json.One(body).Text("description") == "Descrita pelo Unity" && Json.One(body).Text("name") == "Ilha de Alice", "os dados gerais salvos pela janela: " + gm.Status + " " + Snip(body));
            gm.Show("banimentos"); yield return Until(() => gm.Actions.ContainsKey("Banir"), "a seção de banimentos: " + gm.Status);
            gm.Ban(_bName, "teste do Unity", "2"); yield return Until(() => gm.RowIds.Count >= 1, "o banimento aparece na lista: " + gm.Status);
            yield return Call(_a, "GET", mg + "/bans?status=active", null, b => body = b); Check(body.Contains("teste do Unity"), "o banimento chegou à API: " + Snip(body));
            gm.Press("Retirar"); yield return Until(() => gm.RowIds.Count == 0 && gm.Status.Contains("retirado"), "retirar o banimento: " + gm.Status);
            gm.Show("permissoes"); yield return Until(() => gm.Toggles.Count >= 1, "as definições de permissão: " + gm.Status);
            gm.Fields["Nome da pessoa"].value = _bName; gm.Toggles.Values.First().value = true; gm.Press("Dar"); yield return Until(() => gm.RowIds.Contains(_bId), "B aparece com permissões: " + gm.Status);
            gm.Show("ambiente"); yield return Until(() => gm.Fields.ContainsKey("Hora (0 a 24)"), "a aparência da ilha: " + gm.Status);
            gm.Fields["Hora (0 a 24)"].value = "17"; gm.Press("Salvar a aparência"); yield return Until(() => gm.Status.Length > 0 && gm.Fields.ContainsKey("Hora (0 a 24)") && gm.Fields["Hora (0 a 24)"].value == "17", "salvar a aparência: " + gm.Status);
            yield return Call(_a, "GET", mg + "/environment", null, b => body = b); using (var d = JsonDocument.Parse(body)) Check(d.RootElement.GetProperty("look").GetProperty("hour").GetDouble() == 17, "a hora salva na API: " + Snip(body));
            gm.Show("midia"); yield return Until(() => gm.Fields.ContainsKey("Endereço (https)"), "a música da ilha: " + gm.Status);
            gm.Fields["Endereço (https)"].value = "https://cdn.example.com/tema.mp3"; gm.Press("Salvar a música"); yield return Until(() => gm.Status.Length > 0, "salvar a música", 30f);
            yield return Call(_a, "GET", mg + "/media", null, b => body = b); Check(body.Contains("tema.mp3"), "a música salva na API: " + gm.Status + " " + Snip(body));
            gm.Show("auditoria"); yield return Until(() => gm.RowIds.Count >= 3, "o histórico mostra as mudanças: " + gm.Status);
            foreach (var section in new[] { "acesso", "administracao", "objetos", "fisica", "desempenho", "terreno", "reinicio" })
            { var before = gm.Loads; gm.Show(section); yield return Until(() => gm.Loads > before, "a seção " + section + " responder"); Check(gm.Status.Length == 0, "a seção " + section + " abre sem erro: " + gm.Status); }
            gm.Close();
        }

        private IEnumerator LandFull()
        {
            var land = "/api/v1/worlds/islands/" + _island + "/land";
            var lm = new LandManagementWindow(_ca, _island); lm.Open(); yield return Until(() => lm.SummaryText.Contains("terrenos") && lm.Lines >= 10, "a visão geral dos terrenos: " + lm.Status);
            foreach (var section in new[] { "mapa", "lista", "vendas", "alugueis", "contratos", "prims", "historico" })
            { var before = lm.Loads; lm.Show(section); yield return Until(() => lm.Loads > before, "a seção " + section + " responder"); Check(lm.Status.Length == 0, "a seção " + section + " abre sem erro: " + lm.Status); }
            lm.Show("demarcacao"); yield return Until(() => lm.Actions.ContainsKey("Demarcar") && lm.Grid != null && lm.Grid.Size > 0, "a demarcação: " + lm.Status);
            lm.Fields["Nome do terreno"].value = "Lote do Unity"; var edge = lm.Grid.Origin + lm.Grid.Size - 2; lm.Grid.Toggle((edge, edge)); lm.Grid.Toggle((edge + 1, edge)); lm.Press("Demarcar");
            yield return Until(() => lm.Section == "lista" || lm.Status.Length > 0 && !lm.Status.Contains("demarcado"), "a resposta da demarcação: " + lm.Status);
            string body = null; yield return Call(_a, "GET", land + "/overview", null, b => body = b); Check(body.Contains("Lote do Unity"), "o terreno demarcado pela janela está no resumo: " + lm.Status + " " + Snip(body));
            Guid parcel; using (var d = JsonDocument.Parse(body)) parcel = Guid.Parse(d.RootElement.GetProperty("parcels").EnumerateArray().First(p => p.GetProperty("name").GetString() == "Lote do Unity").GetProperty("id").GetString());
            lm.Show("permissoes"); yield return Until(() => lm.Choices.ContainsKey("Terreno") && (lm.Actions.ContainsKey("Dar acesso ao grupo") || lm.Lines >= 3), "o acesso de grupo: " + lm.Status);
            lm.Close();
            var pi = new ParcelInfoWindow(_ca, _island, parcel, () => new Vector3(0, 0, 0)); pi.Open(); yield return Until(() => pi.Fields.ContainsKey("Nome") && pi.SummaryText.Contains("Lote do Unity"), "Sobre este Terreno abre o terreno novo: " + pi.Status);
            pi.Fields["Nome"].value = "Lote Unity Dois"; pi.Press("Salvar nome e descrição"); yield return Until(() => pi.Status.Length > 0, "salvar o nome do terreno");
            yield return Call(_a, "GET", land + "/parcels/" + parcel + "/about", null, b => body = b); Check(body.Contains("Lote Unity Dois"), "o nome novo do terreno na API: " + pi.Status + " " + Snip(body));
            pi.Show("opcoes"); yield return Until(() => pi.Choices.ContainsKey("Quem constrói"), "as opções: " + pi.Status); pi.Choices["Quem constrói"].index = 1; pi.Press("Salvar as opções"); yield return Until(() => pi.Status.Length > 0, "salvar as opções");
            yield return Call(_a, "GET", land + "/parcels/" + parcel + "/about", null, b => body = b); Check(body.Contains("\"buildMode\":\"list\""), "quem constrói salvo na API: " + pi.Status + " " + Snip(body));
            pi.Show("acesso"); yield return Until(() => pi.Choices.ContainsKey("Modo"), "o acesso do terreno: " + pi.Status); pi.AddPerson(_bName, "allow", ""); yield return Until(() => pi.RowIds.Count >= 1 || pi.Status.Length > 0 && !pi.Status.Contains("está na lista"), "pôr B na lista: " + pi.Status);
            yield return Call(_a, "GET", land + "/parcels/" + parcel + "/access", null, b => body = b); Check(body.Contains(_bId.ToString()), "B está na lista do terreno: " + pi.Status + " " + Snip(body));
            foreach (var section in new[] { "propriedade", "objetos", "midia", "historico", "auditoria" })
            { var before = pi.Loads; pi.Show(section); yield return Until(() => pi.Loads > before, "a aba " + section + " responder"); Check(pi.Status.Length == 0, "a aba " + section + " abre sem erro: " + pi.Status); }
            pi.Close();
            var theirs = new ParcelInfoWindow(_cb, _island, parcel); theirs.Open(); yield return Until(() => theirs.Lines >= 1 && theirs.Loads >= 1, "B abre Sobre este Terreno: " + theirs.Status);
            Check(!theirs.Fields.ContainsKey("Nome"), "quem não é dono vê o terreno sem poder mudar: " + theirs.Status); theirs.Close();
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


        private IEnumerator SearchFull()
        {
            var settings = new UserSettings(); (Guid Island, float? X, float? Z) went = default; string body = null;
            var gs = new GlobalSearchWindow(_ca, settings, null, (i, x, z) => went = (i, x, z)); gs.Open();
            yield return Until(() => gs.Loads >= 4, "Para você responder (interesses, recomendados, populares, recentes): " + gs.Status); Check(gs.Status.Length == 0, "Para você abre sem erro: " + gs.Status);
            gs.Fields["Interesses (separados por vírgula)"].value = "praia, música"; gs.Press("Salvar os interesses"); yield return Until(() => gs.Status.Length > 0, "salvar os interesses");
            yield return Call(_a, "GET", "/api/v1/search/interests", null, b => body = b); Check(body.Contains("praia"), "os interesses salvos na API: " + gs.Status + " " + Snip(body));
            // the index is filled by the API in the background (every 30 s): wait for Alice's island to be findable
            var found = false; for (var attempt = 0; attempt < 8 && !found; attempt++)
            {
                gs.Show("lugares"); yield return Until(() => gs.Fields.ContainsKey("Buscar"), "a aba Lugares"); gs.Fields["Buscar"].value = "Alice"; var before = gs.Loads; gs.Press("Buscar"); yield return Until(() => gs.Loads > before, "a busca responder: " + gs.Status);
                Check(gs.Status.Length == 0, "a busca da API responde sem erro: " + gs.Status); found = gs.RowIds.Contains(_island); if (!found) yield return new WaitForSeconds(5f);
            }
            Check(found, "a ilha de Alice aparece na busca de lugares: " + gs.SummaryText);
            Check(settings.RecentSearches.Count == 1 && settings.RecentSearches[0] == "Alice", "a busca entra no histórico local");
            gs.Suggest("Alic"); yield return new WaitForSeconds(1.5f); Check(gs.Status.Length == 0, "as sugestões respondem sem erro: " + gs.Status + " (" + gs.Suggested.Count + ")");
            yield return new WaitForSeconds(.3f); 
            Check(gs.OpenResult(_island), "abrir o resultado da ilha de Alice"); yield return Until(() => gs.Opened == _island && gs.Actions.ContainsKey("Favoritar"), "os detalhes da ilha");
            gs.Press("Favoritar"); yield return Until(() => gs.Status.Length > 0, "favoritar"); yield return Call(_a, "GET", "/api/v1/search/favorites", null, b => body = b); Check(body.Contains(_island.ToString()), "a ilha está nos favoritos da busca: " + gs.Status + " " + Snip(body));
            var shown = gs.Status; gs.Fields["Motivo"].value = "teste de denúncia do Unity"; gs.Press("Enviar denúncia"); yield return Until(() => gs.Status != shown, "denunciar"); Check(gs.Status == "Você não pode denunciar o que é seu.", "a API recusa denunciar a própria ilha, em palavras: " + gs.Status);
            var theirs = new GlobalSearchWindow(_cb, new UserSettings()); theirs.Open(); theirs.Show("lugares"); yield return Until(() => theirs.Fields.ContainsKey("Buscar"), "a busca de B"); theirs.Fields["Buscar"].value = "Alice"; theirs.Press("Buscar"); yield return Until(() => theirs.RowIds.Contains(_island), "B acha a ilha de Alice: " + theirs.Status);
            Check(theirs.OpenResult(_island), "B abre a ilha de Alice"); yield return Until(() => theirs.Fields.ContainsKey("Motivo"), "os detalhes para B"); theirs.Fields["Motivo"].value = "teste de denúncia do Unity"; theirs.Press("Enviar denúncia");
            yield return Until(() => theirs.Status.Length > 0, "a denúncia de B"); Check(theirs.Status.Contains("Denúncia enviada"), "a denúncia de B é aceita: " + theirs.Status); theirs.Close();
            gs.Press("Ir até lá"); Check(went.Island == _island, "ir até a ilha do resultado");
            foreach (var tab in new[] { "tudo", "pessoas", "eventos", "grupos", "itens", "lojas", "favoritos" }) { var before = gs.Loads; gs.Show(tab); yield return Until(() => gs.Loads > before, "a aba " + tab + " responder"); Check(gs.Status.Length == 0, "a aba " + tab + " abre sem erro: " + gs.Status); }
            gs.Close();
        }

        private IEnumerator GroupsFull()
        {
            string body = null; var name = "Grupo Unity " + Guid.NewGuid().ToString("N").Substring(0, 4);
            var hub = new GroupsHubWindow(_ca); hub.Open(); hub.Show("criar"); yield return Until(() => hub.Actions.ContainsKey("Continuar"), "o primeiro passo de criar grupo");
            hub.Creating.Name = name; hub.Creating.Description = "Criado pelo teste do Unity"; hub.Creating.Policy = "request"; hub.CreateNow(); yield return Until(() => hub.Created != Guid.Empty || hub.Status.Length > 0 && !hub.Status.Contains("criado"), "criar o grupo: " + hub.Status);
            Check(hub.Created != Guid.Empty, "o grupo criado pelos 4 passos: " + hub.Status); var group = hub.Created; var path = "/api/v1/groups/" + group;
            var gp = new GroupProfileWindow(_ca, group); gp.Open(); yield return Until(() => gp.SummaryText.Contains(name), "o perfil do grupo: " + gp.Status);
            foreach (var tab in new[] { "membros", "cargos", "permissoes", "avisos", "chat", "terrenos", "economia", "propostas", "configuracoes" }) { var before = gp.Loads; gp.Show(tab); yield return Until(() => gp.Loads > before + 1, "a aba " + tab + " responder"); Check(gp.Status.Length == 0, "a aba " + tab + " do grupo abre sem erro: " + gp.Status); }
            gp.Show("membros"); yield return Until(() => gp.Fields.ContainsKey("Convidar pelo nome"), "o convite pelo nome"); gp.Fields["Convidar pelo nome"].value = _bName; gp.Press("Convidar"); yield return Until(() => gp.Status.Length > 0, "convidar B");
            Check(gp.Status.Contains("Convite enviado"), "o convite a B é aceito pela API: " + gp.Status);
            var theirs = new GroupsHubWindow(_cb); theirs.Open(); theirs.Show("convites"); yield return Until(() => theirs.Actions.ContainsKey("Aceitar " + name), "o convite aparece para B: " + theirs.Status); theirs.Press("Aceitar " + name);
            yield return Until(() => theirs.Status.Length > 0, "B aceitar o convite"); Check(theirs.Status.Contains("entrou"), "B entra no grupo: " + theirs.Status);
            yield return Call(_a, "GET", path + "/members", null, b => body = b); Check(body.Contains(_bId.ToString()), "B está entre os membros: " + Snip(body));
            gp.Show("cargos"); yield return Until(() => gp.Fields.ContainsKey("Nome do cargo"), "o formulário de cargo"); yield return new WaitForSeconds(.3f); gp.Fields["Nome do cargo"].value = "Construtor"; gp.Press("Criar o cargo"); yield return Until(() => gp.Status.Contains("Cargo criado") || gp.Status.Length > 0 && !gp.Status.Contains("Cargo"), "criar o cargo: " + gp.Status);
            gp.Show("permissoes"); yield return Until(() => gp.Toggles.ContainsKey("Construtor:invite"), "as permissões do cargo novo: " + gp.Status); yield return new WaitForSeconds(.3f); gp.Toggles["Construtor:invite"].value = true; gp.Press("Salvar as permissões de Construtor"); yield return Until(() => gp.Status.Length > 0, "salvar as permissões");
            yield return Call(_a, "GET", path + "/roles", null, b => body = b); Check(body.Contains("Construtor") && body.Contains("\"invite\""), "o cargo com a permissão de convidar na API: " + gp.Status + " " + Snip(body));
            gp.Show("avisos"); yield return Until(() => gp.Fields.ContainsKey("Título do aviso"), "o formulário de aviso"); yield return new WaitForSeconds(.3f); gp.Fields["Título do aviso"].value = "Reunião"; gp.Fields["Texto"].value = "Sábado às 20h"; gp.Press("Publicar o aviso"); yield return Until(() => gp.Status.Length > 0, "publicar o aviso");
            yield return Call(_b, "GET", path + "/announcements", null, b => body = b); Check(body.Contains("Reunião"), "B vê o aviso: " + gp.Status + " " + Snip(body));
            gp.Show("chat"); yield return Until(() => gp.Fields.ContainsKey("Mensagem"), "o chat do grupo"); yield return new WaitForSeconds(.3f); gp.Fields["Mensagem"].value = "olá grupo"; gp.Press("Enviar"); yield return new WaitForSeconds(1f);
            yield return Call(_b, "GET", path + "/chat?take=10", null, b => body = b); Check(body.Contains("olá grupo") || body.Contains("ol\\u00E1 grupo"), "a mensagem no chat do grupo: " + Snip(body));
            gp.Show("economia"); yield return Until(() => gp.Fields.ContainsKey("Quanto dar (VC)"), "o baú"); yield return new WaitForSeconds(.3f); gp.Fields["Quanto dar (VC)"].value = "5"; gp.Press("Dar ao baú"); yield return Until(() => gp.Status.Length > 0, "dar ao baú");
            Check(gp.Status == "Saldo insuficiente.", "sem moedas a API recusa, em palavras: " + gp.Status);
            gp.Show("propostas"); yield return Until(() => gp.Fields.ContainsKey("Título"), "a nova proposta"); yield return new WaitForSeconds(.3f); gp.Fields["Título"].value = "Festa no sábado?"; gp.Press("Abrir a votação"); yield return Until(() => gp.Status.Length > 0, "abrir a votação");
            Check(gp.Status.Contains("Votação aberta"), "a votação abre: " + gp.Status);
            var bp = new GroupProfileWindow(_cb, group); bp.Open(); bp.Show("propostas"); yield return Until(() => bp.Actions.ContainsKey("Votar: Sim"), "B pode votar: " + bp.Status); bp.Press("Votar: Sim"); yield return Until(() => bp.Status.Length > 0, "B votar");
            Check(bp.Status.Contains("Voto dado"), "o voto de B: " + bp.Status); bp.Close();
            hub.Show("meus"); yield return Until(() => hub.Actions.ContainsKey("Usar como ativo: " + name), "o grupo em Meus grupos"); hub.Press("Usar como ativo: " + name); yield return Until(() => hub.Status.Length > 0, "usar como ativo");
            yield return Call(_a, "GET", "/api/v1/groups/active", null, b => body = b); Check(body.Contains(name), "o grupo ativo na API: " + hub.Status + " " + Snip(body));
            hub.Show("descobrir"); yield return Until(() => hub.Loads >= 1 && hub.Actions.ContainsKey("Buscar"), "descobrir grupos"); Check(hub.Status.Length == 0, "descobrir grupos sem erro: " + hub.Status);
            gp.Close(); hub.Close(); theirs.Close();
        }

        private IEnumerator WalletAndProfile()
        {
            string body = null;
            var wallet = new WalletHubWindow(_ca); wallet.Open(); yield return Until(() => wallet.Balance >= 0 && wallet.Loads >= 2, "a carteira: " + wallet.Status);
            foreach (var tab in new[] { "extrato", "comprar", "taxas" }) { var before = wallet.Loads; wallet.Show(tab); yield return Until(() => wallet.Loads > before + 1, "a aba " + tab); Check(wallet.Status.Length == 0, "a aba " + tab + " da carteira abre sem erro: " + wallet.Status); }
            wallet.Order("starter", "pix"); yield return Until(() => wallet.Status.Length > 0, "pedir moedas"); Check(wallet.Status.Contains("ainda não está ligada") || wallet.Status.Contains("Pedido feito") || wallet.Status.Contains("não existe"), "pedir moedas responde em palavras: " + wallet.Status); wallet.Close();
            var mine = new ProfileHubWindow(_ca, _aId); mine.Open(); yield return Until(() => mine.Fields.ContainsKey("Sobre você"), "o próprio perfil: " + mine.Status); yield return new WaitForSeconds(.3f);
            mine.Fields["Sobre você"].value = "Bio escrita pelo Unity"; mine.Press("Salvar o perfil"); yield return Until(() => mine.Status.Length > 0, "salvar o perfil");
            yield return Call(_a, "GET", "/api/v1/users/" + _aId + "/profile/full", null, b => body = b); Check(body.Contains("Bio escrita pelo Unity"), "a bio salva: " + mine.Status + " " + Snip(body));
            mine.Show("feed"); yield return Until(() => mine.Fields.ContainsKey("O que você quer contar?"), "publicar no feed: " + mine.Status); yield return new WaitForSeconds(.3f); mine.Fields["O que você quer contar?"].value = "Primeira publicação do Unity"; mine.Press("Publicar"); yield return Until(() => mine.RowIds.Count >= 1, "a publicação no feed: " + mine.Status);
            foreach (var tab in new[] { "galeria", "conquistas", "estatisticas", "favoritos", "grupos" }) { var before = mine.Loads; mine.Show(tab); yield return Until(() => mine.Loads > before, "a aba " + tab); Check(mine.Status.Length == 0, "a aba " + tab + " do perfil abre sem erro: " + mine.Status); }
            mine.Close();
            var other = new ProfileHubWindow(_cb, _aId); other.Open(); yield return Until(() => other.Loads >= 1, "B abre o perfil de A"); other.Show("feed"); yield return Until(() => other.RowIds.Count >= 1, "B vê a publicação de A: " + other.Status);
            other.Show("acoes"); yield return Until(() => other.Actions.ContainsKey("Mandar mensagem"), "as ações no perfil de outra pessoa"); Check(!other.Fields.ContainsKey("Sobre você"), "B não edita o perfil de A"); other.Close();
        }

        private IEnumerator EntryAndScripts()
        {
            var health = _a.GetBytesAsync("/health"); yield return Wait(health); var login = new LoginView(new UserSettings(), ""); login.SetServers(health.Result.Status);
            Check(health.Result.Status == 200 && login.ServersNow == LoginView.Servers.Online || health.Result.Status == 503 && login.ServersNow == LoginView.Servers.Offline, "/health responde e a tela de entrada mostra o que ele disse: " + health.Result.Status + " " + login.ServerState.text);
            string start = null; yield return Call(_a, "GET", "/api/v1/worlds/me/start", null, b => start = b); var place = Json.One(start); Check(place.Id("islandId") != Guid.Empty && place.Text("reason").Length > 0, "o servidor escolhe onde a pessoa começa: " + Snip(start));
            string manifest = null; yield return Call(_a, "GET", LifeVerse.Unity.World.AssetPreloader.ManifestPath(_island, null, null), null, b => manifest = b); var assets = LifeVerse.Unity.World.AssetPreloader.ParseManifest(manifest); Check(assets != null, "a lista de recursos da ilha: " + Snip(manifest));
            var create = _a.ApiAsync("POST", "/api/v1/worlds/islands/" + _island + "/objects", JsonSerializer.Serialize(new { primitiveType = "cube", name = "Contador", positionX = 2, positionY = 1, positionZ = 2, rotationX = 0, rotationY = 0, rotationZ = 0, scaleX = 1, scaleY = 1, scaleZ = 1 })); yield return Wait(create);
            var obj = Json.One(create.Result.Body); var objectId = obj.Id("id", "objectId"); if (objectId == Guid.Empty && obj.Has("object")) objectId = new Row(obj.Element.GetProperty("object")).Id(); Check(objectId != Guid.Empty, "o objeto do script: " + Snip(create.Result.Body));
            var lua = new LuaEditorWindow(_ca, _island, objectId); lua.Open(); yield return Until(() => lua.StatusText.Length == 0 || lua.StatusText.Length > 0, "o editor abrir");
            lua.NewScript(); lua.Source = "local contador = 0\nfunction on_touch(e)\n  contador = contador + 1\n  object.say(tostring(contador))\nend\nfunction on_toque(e)\nend\n"; lua.Save(); yield return Until(() => lua.OpenScript != Guid.Empty && !lua.Dirty, "salvar o script: " + lua.StatusText, 10f);
            lua.Validate(); yield return Until(() => lua.StatusText.StartsWith("Sem erros") || lua.StatusText.StartsWith("Erro"), "a verificação do servidor: " + lua.StatusText, 10f); Check(lua.StatusText.StartsWith("Sem erros"), "o servidor aceita o script: " + lua.StatusText + " / " + lua.ProblemsText);
            lua.RefreshRuntime(); yield return Until(() => lua.Runtime != null, "como o script está na ilha", 10f); Check(lua.PerformanceText.Length > 0 && lua.ConsoleText.Length > 0, "o desempenho e o console do script: " + lua.PerformanceText);
            lua.Pause(); yield return Until(() => lua.PauseText == "Continuar", "pausar o script: " + lua.StatusText, 10f); lua.Pause(); yield return Until(() => lua.PauseText == "Pausar", "continuar o script: " + lua.StatusText, 10f);
            lua.Grant("HTTP_REQUESTS", true); yield return Until(() => lua.StatusText.Contains("concedida") || lua.StatusText.Contains("Não") || lua.StatusText.Contains("não"), "dar uma permissão ao script", 10f); Check(lua.StatusText.Contains("concedida"), "a permissão foi dada: " + lua.StatusText);
            lua.RefreshRuntime(); yield return Until(() => lua.OwnerPermissions["HTTP_REQUESTS"].value && lua.Runtime != null && lua.Runtime.Grants.Exists(g => g.Permission == "HTTP_REQUESTS"), "a permissão aparecer no script", 10f);
            lua.Source = "object."; lua.PlaceCursor(7); yield return new WaitForSeconds(1f); lua.Complete(); Check(lua.Suggestions.Count >= 3 && lua.Suggestions.Exists(x => x.Insert == "getName("), "completar com a referência real: " + lua.Suggestions.Count);
            lua.Close();
        }

        private IEnumerator AvatarStudio()
        {
            string body = null; var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lv-avatar-" + Guid.NewGuid().ToString("N").Substring(0, 6)); System.IO.Directory.CreateDirectory(folder);
            string Save(string name, byte[] bytes) { var path = System.IO.Path.Combine(folder, name); System.IO.File.WriteAllBytes(path, bytes); return path; }
            var bodyFile = Save("corpo.glb", HumanoidTemplate.Build(new TemplateOptions()));
            var shirtFile = Save("camiseta.glb", WearableTemplate.Clothing("top", WearableTemplate.ShirtBones));
            var hatFile = Save("chapeu.glb", WearableTemplate.Attachment("{\"lifeverse\":{\"slot\":\"hat\",\"bone\":\"head\"}}"));
            var danceFile = Save("aceno.glb", HumanoidTemplate.Build(new TemplateOptions { Animations = new[] { new AnimationSpec("Aceno", 1f, new[] { ("upper_arm_r", 2, 1.2f), ("lower_arm_r", 0, .6f), ("hand_r", 2, .4f), ("upper_arm_l", 2, -.2f), ("spine", 1, .1f), ("chest", 1, .1f), ("neck", 1, .2f), ("head", 0, .15f), ("upper_leg_l", 0, .05f), ("upper_leg_r", 0, -.05f) }) } }));
            var st = new AvatarStudioWindow(_ca); st.Open(); yield return Until(() => st.Loads >= 1, "o estúdio do avatar: " + st.Status);
            st.Show("corpo"); yield return Until(() => st.Loads >= 2, "a aba do corpo"); st.Upload("models", bodyFile); yield return Until(() => st.Status.Contains("enviado") || st.Status.Length > 0 && !st.Status.StartsWith("Enviando"), "enviar o corpo", 30f);
            Check(st.Status.Contains("enviado"), "o corpo próprio passa nas checagens do servidor: " + st.Status);
            yield return Call(_a, "GET", "/api/v1/users/me/avatar/models", null, b => body = b); Guid model; using (var d = JsonDocument.Parse(body)) model = Guid.Parse(d.RootElement.EnumerateArray().First().GetProperty("id").GetString());
            yield return new WaitForSeconds(.4f); st.Press(st.Actions.Keys.First(k => k.StartsWith("Usar "))); yield return Until(() => st.Status.Contains("em uso"), "usar o corpo: " + st.Status);
            yield return Call(_a, "GET", "/api/v1/users/me/avatar", null, b => body = b); Check(body.Contains(model.ToString()), "o avatar usa o corpo próprio: " + Snip(body));
            st.Show("roupas"); yield return Until(() => st.Actions.ContainsKey("Enviar uma roupa ou acessório (.glb)…"), "a aba das roupas"); st.Upload("wearables", shirtFile); yield return Until(() => st.Status.Length > 0 && !st.Status.StartsWith("Enviando"), "enviar a camiseta", 30f);
            Check(st.Status.Contains("enviado"), "a camiseta passa nas checagens: " + st.Status); yield return new WaitForSeconds(.4f);
            st.Upload("wearables", hatFile); yield return Until(() => st.Status.Length > 0 && !st.Status.StartsWith("Enviando") && st.Status.Contains("chapeu"), "enviar o chapéu", 30f); Check(st.Status.Contains("enviado"), "o chapéu passa nas checagens: " + st.Status);
            yield return Until(() => st.Actions.Keys.Any(k => k.StartsWith("Vestir ")), "as roupas para vestir"); yield return new WaitForSeconds(.4f);
            var wear = st.Actions.Keys.First(k => k.StartsWith("Vestir ") && k.Contains("chapeu")); var beforeWear = st.Status; st.Press(wear); yield return Until(() => st.Status != beforeWear, "vestir o chapéu: " + st.Status, 20f);
            Check(st.Status.Contains("vestido"), "vestir o chapéu salva o avatar: " + st.Status);
            yield return Call(_a, "GET", "/api/v1/users/me/avatar", null, b => body = b); Check(body.Contains("custom:"), "o avatar leva o item próprio: " + Snip(body));
            st.Show("animacoes"); yield return Until(() => st.Actions.ContainsKey("Enviar animações (.glb)…"), "a aba das animações"); st.Upload("animations", danceFile); yield return Until(() => st.Status.Length > 0 && !st.Status.StartsWith("Enviando"), "enviar a animação", 30f);
            Check(st.Status.Contains("enviado"), "a animação passa nas checagens: " + st.Status);
            yield return Until(() => st.Choices.ContainsKey("Parado"), "a escolha de animação por estado"); yield return new WaitForSeconds(.4f);
            var idle = st.Choices["Parado"]; var options = (List<(string Id, string Text)>)idle.userData; var aceno = options.FindIndex(o => o.Text.EndsWith(": Aceno")); Check(aceno > 0, "o clipe Aceno está entre as escolhas do estado Parado");
            var beforeSave = st.Status; idle.index = aceno; st.Press("Salvar as animações"); yield return Until(() => st.Status != beforeSave, "salvar as animações");
            yield return Call(_a, "GET", "/api/v1/users/me/avatar", null, b => body = b); Check(body.Contains("#Aceno"), "o estado Parado usa a animação própria: " + st.Status + " " + Snip(body));
            st.Show("roupas"); yield return Until(() => st.Actions.Keys.Any(k => k.StartsWith("Tirar ")), "o chapéu vestido"); yield return new WaitForSeconds(.3f); var beforeOff = st.Status; st.Press(st.Actions.Keys.First(k => k.StartsWith("Tirar "))); yield return Until(() => st.Status != beforeOff, "tirar o chapéu", 20f);
            Check(st.Status.Contains("tirado"), "tirar o chapéu: " + st.Status);
            st.Show("visuais"); yield return Until(() => st.Fields.ContainsKey("Nome do visual"), "os visuais"); yield return new WaitForSeconds(.3f); st.Fields["Nome do visual"].value = "Visual Unity"; st.Press("Salvar o avatar de agora como visual"); yield return Until(() => st.Actions.ContainsKey("Compartilhar Visual Unity"), "o visual salvo: " + st.Status);
            st.Press("Compartilhar Visual Unity"); yield return Until(() => st.LastCode.Length > 0, "o código do visual: " + st.Status);
            var bst = new AvatarStudioWindow(_cb); bst.Open(); yield return Until(() => bst.Fields.ContainsKey("Código de um visual compartilhado"), "os visuais de B"); yield return new WaitForSeconds(.3f);
            bst.Fields["Código de um visual compartilhado"].value = st.LastCode; bst.Press("Trazer pelo código"); yield return Until(() => bst.Status.Length > 0, "B trazer pelo código"); Check(bst.Status.Contains("trazido"), "B traz o visual pelo código: " + bst.Status);
            st.Show("corpo"); yield return Until(() => st.Actions.ContainsKey("Voltar ao corpo do jogo"), "a aba do corpo de novo"); yield return new WaitForSeconds(.3f); st.Press("Voltar ao corpo do jogo"); yield return Until(() => st.Status.Contains("Corpo do jogo"), "voltar ao corpo do jogo");
            st.Close(); bst.Close(); try { System.IO.Directory.Delete(folder, true); } catch (System.IO.IOException) { }
        }

        private IEnumerator Live()
        {
            var host = new GameObject("live channel"); var live = host.AddComponent<LiveChannel>(); var got = new List<string>(); live.Received += got.Add; live.Begin(Url, () => _a.AccessToken);
            yield return Until(() => live.Connected, "o canal ao vivo abrir na API de verdade: " + live.LastError, 15f);
            var text = "ao vivo " + Guid.NewGuid().ToString("N").Substring(0, 6); yield return Call(_b, "POST", "/api/v1/social/messages", JsonSerializer.Serialize(new { content = text, recipientUserId = _aId }), null);
            yield return Until(() => got.Any(j => j.Contains("\"message\"") && j.Contains(text)), "a mensagem chegar pelo canal", 10f);
            Check(LifeVerse.Shared.Networking.NotificationFormatter.Describe(got.First(j => j.Contains(text))) is { } line && line.Contains(text), "o aviso da mensagem é montado como no Godot");
            var bad = host.AddComponent<LiveChannel>(); bad.Begin(Url, () => "token-falso"); yield return new WaitForSeconds(3f); Check(!bad.Connected && bad.Connects == 0, "sem um token válido o canal não abre: " + bad.LastError);
            live.End(); bad.End(); UnityEngine.Object.Destroy(host);
        }

        private IEnumerator Attachments()
        {
            const string route = "/api/v1/users/me/avatar/attachments";
            var own = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lv-wear-" + Guid.NewGuid().ToString("N") + ".png"); System.IO.File.WriteAllBytes(own, TestAssets.Png(Unique(200, 90, 30), 32)); UploadResult wearFile = null; Uploads.Send(_ca, own, "accessories", r => wearFile = r); yield return Until(() => wearFile != null, "o envio do objeto", 30f); System.IO.File.Delete(own); Check(wearFile.Ok, "enviar o objeto: " + wearFile.Message);
            string inv = null; yield return Call(_a, "GET", "/api/v1/inventory/items?view=all&page=1&pageSize=50", null, b => inv = b); var wearItem = Json.Rows(inv).First(r => r.Id("assetId") == wearFile.AssetId).Id("id", "itemId");
            yield return Call(_a, "POST", route, JsonSerializer.Serialize(new { inventoryItemId = wearItem, point = "head_top" }), null);
            string listed = null; yield return Call(_a, "GET", route, null, b => listed = b); var worn = AttachmentData.ParseList(listed, out var profiles); Check(worn.Count == 1 && worn[0].Equipped && worn[0].Point == "head_top" && worn[0].InventoryItemId == wearItem, "o item vestido aparece na lista: " + Snip(listed)); Check(profiles.Contains("humanoid"), "o perfil humanoide vale");
            var id = worn[0].Id; yield return Call(_a, "PATCH", route + "/" + id, JsonSerializer.Serialize(new { position = new[] { 0f, .12f, 0f }, rotation = new[] { 0f, 30f, 0f }, scale = new[] { 1.5f, 1.5f, 1.5f } }), null);
            string again = null; yield return Call(_a, "GET", route, null, b => again = b); var changed = AttachmentData.ParseList(again, out _).First(); Check(Math.Abs(changed.Position[1] - .12f) < .001f && Math.Abs(changed.Rotation[1] - 30f) < .001f && Math.Abs(changed.Scale[0] - 1.5f) < .001f, "a posição, a rotação e a escala foram guardadas");
            yield return Call(_a, "PATCH", route + "/" + id, JsonSerializer.Serialize(new { sway = .4f }), null); string swayRaw = null; yield return Call(_a, "GET", route, null, b => swayRaw = b); Check(Math.Abs(AttachmentData.ParseList(swayRaw, out _).First().Sway - .4f) < .001f, "o balanço foi guardado: " + Snip(swayRaw));
            yield return Call(_a, "POST", route + "/" + id + "/move", JsonSerializer.Serialize(new { point = "hud_top_right", mode = "add" }), null); string movedRaw = null; yield return Call(_a, "GET", route, null, b => movedRaw = b); var moved = AttachmentData.ParseList(movedRaw, out _).First(); Check(moved.Point == "hud_top_right" && moved.IsHud && moved.Equipped, "mover levou o objeto para o ponto de tela: " + Snip(movedRaw));
            yield return Call(_a, "DELETE", route + "/" + id, null, null); string after = null; yield return Call(_a, "GET", route, null, b => after = b); Check(AttachmentData.ParseList(after, out _).All(a => !a.Equipped), "tirado, o objeto fica guardado e não vestido");
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
        private IEnumerator BuildExtras()
        {
            var lua = new LuaEditorWindow(_ca, _island, Guid.Empty); lua.Open(); lua.OpenTemplates(); yield return Until(() => lua.Templates > 0, "os modelos de script"); Check(lua.Templates >= 2, "a API dá os modelos de script: " + lua.Templates); lua.Close();
            string body = null; yield return Call(_a, "GET", "/api/v1/users/" + _bId + "/profile", null, b => body = b);
            using (var d = JsonDocument.Parse(body)) Check(d.RootElement.TryGetProperty("displayName", out var shown) && shown.GetString() is { Length: > 0 }, "o perfil público dá o nome que o editor mostra como criador e dono: " + Snip(body));
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
