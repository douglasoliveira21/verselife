using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The profile of a person, as in the Godot client: Sobre (photo, bio, the person outside the game, interests, how long here, marriage), Feed (posts with a picture, for everybody or only friends), Galeria (order,
    /// main photo, cover), Conquistas, Estatísticas, Favoritos, Grupos e terrenos (and, on one's own profile, which of them show), and on somebody else's profile the actions: message, teleport, friendship, a gift,
    /// block. All of it from <c>/api/v1/users/{id}/profile/full</c>; what is private stays out (the API decides).
    /// </summary>
    public sealed class ProfileHubWindow : SectionWindow
    {
        public static readonly (string Id, string Text)[] Sections = { ("sobre", "Sobre"), ("feed", "Feed"), ("galeria", "Galeria"), ("conquistas", "Conquistas"), ("estatisticas", "Estatísticas"), ("favoritos", "Favoritos"), ("grupos", "Grupos e terrenos"), ("acoes", "Ações") };
        private readonly Guid _user; private JsonElement _profile; private readonly Dictionary<string, Texture2D> _images = new Dictionary<string, Texture2D>();
        public Guid User => _user;
        public bool IsSelf => Flag(_profile, "isSelf");
        private string U => "/api/v1/users/" + _user;
        private const string Me = "/api/v1/users/me/profile";

        public ProfileHubWindow(ApiContext context, Guid user) : base(context, "profile-" + user.ToString("N").Substring(0, 8), "Perfil", Sections, 760f, 560f, new Vector2(320, 60), 150f) { _user = user; Show("sobre"); }

        protected override void Fill(string section) => Get(U + "/profile/full", p =>
        {
            _profile = p.Clone();
            _summary.text = S(p, "displayName") + " (@" + S(p, "username") + ")  ·  " + PresenceWords(S(p, "presence")) + (S(p, "accountType").Length > 0 && S(p, "accountType") != "standard" ? "  ·  " + S(p, "accountType") : "") + (Flag(p, "privateProfile") ? "  ·  perfil privado" : "");
            if (Flag(p, "privateProfile") && !IsSelf && section != "acoes") { Line("Este perfil é privado: só as ações aparecem.", "lv-small", "lv-muted"); Actions_(); return; }
            switch (section) { case "sobre": About(p); break; case "feed": Feed(); break; case "galeria": Gallery(p); break; case "conquistas": Achievements(p); break; case "estatisticas": Stats(p); break; case "favoritos": Favorites(p); break; case "grupos": GroupsAndLand(p); break; case "acoes": Actions_(); break; }
        });

        public static string PresenceWords(string presence) => presence switch { "online" => "online", "away" => "ausente", "busy" => "ocupado", "invisible" or "offline" => "offline", _ => presence };

        /// <summary>The checks of a post: some text or a picture, at most 2000 letters, visible to everybody or only friends. Null when fine.</summary>
        public static string PostProblem(string text, Guid? image, string visibility)
        {
            if ((text ?? "").Trim().Length == 0 && image == null) return "Escreva algo ou escolha uma imagem."; if ((text ?? "").Length > 2000) return "A publicação tem até 2000 letras.";
            return visibility is "public" or "friends" ? null : "Escolha quem pode ver: todos ou só amigos.";
        }

        /// <summary>A gallery order after moving one picture up (-1) or down (+1).</summary>
        public static List<Guid> Moved(List<Guid> order, Guid id, int step)
        {
            var list = order.ToList(); var at = list.IndexOf(id); var to = at + step; if (at < 0 || to < 0 || to >= list.Count) return list; list.RemoveAt(at); list.Insert(to, id); return list;
        }

        private void Picture(string path, float size)
        {
            var image = new Image { scaleMode = ScaleMode.ScaleToFit }; image.style.width = size; image.style.height = size; image.style.marginBottom = 4; _page.Add(image);
            if (_images.TryGetValue(path, out var known)) { image.image = known; return; }
            this.Then(_c.Api.GetBytesAsync(path), r => { if (r.Status != 200 || r.Bytes == null) return; var t = new Texture2D(2, 2); if (t.LoadImage(r.Bytes)) { _images[path] = t; image.image = t; } });
        }

        // ---- Sobre ----

        private void About(JsonElement p)
        {
            if (Flag(p, "hasCover")) Picture(U + "/profile/cover", 220); if (Flag(p, "hasPhoto")) Picture(U + "/profile/photo", 120);
            Title(S(p, "displayName")); if (S(p, "bio").Length > 0) Line(S(p, "bio"));
            Line("No LifeVerse desde " + When(Date(p, "joinedAt")) + (Has(p, "characterAgeYears") ? " · " + I(p, "characterAgeYears") + " ano(s) de personagem" : "") + (S(p, "birthday").Length > 0 ? " · aniversário " + S(p, "birthday") : "") + (Flag(p, "married") ? " · casado(a)" : ""), "lv-small", "lv-muted");
            if (S(p, "firstLifeBio").Length > 0) { Title("Fora do jogo"); Line(S(p, "firstLifeBio")); } if (S(p, "interests").Length > 0) { Title("Interesses"); Line(S(p, "interests")); }
            if (!IsSelf) return;
            Title("Editar"); var bio = Field("Sobre você", S(p, "bio"), 1000); var outside = Field("Fora do jogo", S(p, "firstLifeBio"), 1000); var interests = Field("Interesses", S(p, "interests"), 300);
            var settings = Obj(p, "settings"); var privateProfile = Check("Perfil privado", Flag(settings, "privateProfile")); var hideStats = Check("Esconder as estatísticas", Flag(settings, "hideStats")); var hideAchievements = Check("Esconder as conquistas", Flag(settings, "hideAchievements")); var showFavorites = Check("Mostrar os favoritos", Flag(settings, "showFavorites"));
            Buttons(Btn("Salvar o perfil", () => Send("PUT", Me + "/details", new { bio = bio.value, firstLifeBio = outside.value, interests = interests.value, privateProfile = privateProfile.value, hideStats = hideStats.value, hideAchievements = hideAchievements.value, showFavorites = showFavorites.value }, "Perfil salvo.", () => Show("sobre")), "lv-btn-primary"));
        }

        // ---- Feed ----

        private void Feed() => Get(U + "/profile/feed", root =>
        {
            if (S(root, "hidden").Length > 0) { Line("O feed desta pessoa não está à vista.", "lv-small", "lv-muted"); return; }
            if (Flag(root, "canPost"))
            {
                Title("Publicar"); var text = Field("O que você quer contar?", "", 2000); var who = Choice("Quem vê", new List<(string, string)> { ("public", "Todos"), ("friends", "Só amigos") }, "public");
                var gallery = Arr(_profile, "gallery"); var image = Choice("Imagem (da galeria)", new[] { ("", "Sem imagem") }.Concat(gallery.Select((g, i) => (S(g, "id"), "Imagem " + (i + 1) + (Flag(g, "main") ? " (foto do perfil)" : "")))).ToList(), "");
                Buttons(Btn("Publicar", () => { Guid? pic = Guid.TryParse(IdOf(image), out var gi) ? gi : (Guid?)null; var problem = PostProblem(text.value, pic, IdOf(who)); if (problem != null) { Say(problem, true); return; } Send("POST", Me + "/posts", new { text = text.value.Trim(), galleryImageId = pic, visibility = IdOf(who) }, "Publicado.", () => Show("feed")); }, "lv-btn-primary"));
            }
            var items = Arr(root, "items");
            foreach (var item in items)
            {
                var post = G(item, "postId"); var line = (S(item, "title").Length > 0 ? S(item, "title") + " · " : "") + S(item, "text");
                var cells = new List<VisualElement> { UiKit.Text(When(Date(item, "at")), "lv-small", "lv-muted"), UiKit.Text(line.Length > 0 ? line : "(imagem)", "lv-small"), UiKit.Spacer() };
                if (S(item, "visibility") == "friends") cells.Insert(1, UiKit.Text("só amigos", "lv-small", "lv-accent"));
                if (S(item, "kind") == "achievement") cells.Insert(0, Medal(item, 26f));
                if (IsSelf && post != Guid.Empty) cells.Add(Sure("Apagar a publicação", () => "Apagar esta publicação?", () => Send("DELETE", Me + "/posts/" + post, null, "Publicação apagada.", () => Show("feed"))));
                Row(post, cells.ToArray()); if (Flag(item, "hasImage") && post != Guid.Empty) Picture(U + "/profile/posts/" + post + "/image", 160);
            }
            if (items.Count == 0) Line("Nada no feed ainda.", "lv-small", "lv-muted");
        });

        // ---- Galeria ----

        private void Gallery(JsonElement p)
        {
            var gallery = Arr(p, "gallery"); var order = gallery.Select(g => G(g, "id")).ToList();
            if (IsSelf)
            {
                Buttons(Btn("Pôr uma imagem do inventário…", () => new TexturePickerWindow(_c, (builtin, id) => { if (builtin) { Say("Escolha uma imagem do inventário (não uma do sistema).", true); return; } Send("POST", Me + "/gallery", new { assetId = Guid.Parse(id) }, "Imagem posta na galeria.", () => Show("galeria")); }, "Imagem para a galeria").Open(), "lv-btn-primary"),
                    Btn("Escolher a capa…", () => new TexturePickerWindow(_c, (builtin, id) => { if (builtin) return; Send("PUT", Me + "/cover", new { assetId = Guid.Parse(id) }, "Capa salva.", () => Show("galeria")); }, "Capa do perfil").Open()),
                    Btn("Tirar a capa", () => Send("PUT", Me + "/cover", new { assetId = (Guid?)null }, "Capa retirada.", () => Show("galeria"))));
            }
            foreach (var g in gallery)
            {
                var id = G(g, "id"); Picture(U + "/profile/gallery/" + id, 140);
                var cells = new List<VisualElement> { UiKit.Text(Flag(g, "main") ? "foto do perfil" : "", "lv-small", "lv-accent"), UiKit.Spacer() };
                if (IsSelf)
                {
                    if (!Flag(g, "main")) cells.Add(Btn("Usar como foto", () => Send("PUT", Me + "/gallery/" + id + "/main", "{}", "Foto do perfil mudada.", () => Show("galeria"))));
                    cells.Add(Btn("Subir", () => Send("PUT", Me + "/gallery/order", new { ids = Moved(order, id, -1) }, null, () => Show("galeria")))); cells.Add(Btn("Descer", () => Send("PUT", Me + "/gallery/order", new { ids = Moved(order, id, 1) }, null, () => Show("galeria"))));
                    cells.Add(Sure("Tirar da galeria", () => "Tirar esta imagem da galeria?", () => Send("DELETE", Me + "/gallery/" + id, null, "Imagem tirada.", () => Show("galeria"))));
                }
                Row(id, cells.ToArray());
            }
            if (gallery.Count == 0) Line("A galeria está vazia.", "lv-small", "lv-muted");
        }

        // ---- Conquistas, Estatísticas, Favoritos ----

        private void Achievements(JsonElement p)
        {
            var list = Arr(p, "achievements"); Title(list.Count + " conquista(s)");
            foreach (var a in list) Row(Guid.Empty, Medal(a, 30f), UiKit.Text(S(a, "title"), "lv-bold"), UiKit.Text(S(a, "description") + " · " + When(Date(a, "unlockedAt")), "lv-small", "lv-muted"));
            if (list.Count == 0) Line("Nenhuma conquista à vista.", "lv-small", "lv-muted");
        }

        private static readonly Color MedalColour = new Color32(0xF4, 0xA3, 0x40, 0xFF);

        /// <summary>The icon of an achievement: the one the server names, or a star when this client does not have it (as the Godot client does).</summary>
        public static string AchievementIcon(JsonElement achievement) => IconShapes.Has(S(achievement, "icon")) ? S(achievement, "icon") : "star";
        private static Icon Medal(JsonElement achievement, float size) => new Icon(AchievementIcon(achievement), size, MedalColour);

        private void Stats(JsonElement p)
        {
            var s = Obj(p, "stats"); if (s.ValueKind != JsonValueKind.Object) { Line("As estatísticas não estão à vista.", "lv-small", "lv-muted"); return; }
            Title("Estatísticas"); Line(I(s, "daysMember") + " dias no LifeVerse (desde " + When(Date(s, "joinedAt")) + ")", "lv-bold");
            foreach (var (key, text) in new[] { ("groups", "grupos"), ("parcels", "terrenos"), ("creations", "criações à venda"), ("events", "eventos"), ("pictures", "fotos") }) Line(I(s, key) + " " + text);
        }

        private void Favorites(JsonElement p)
        {
            var list = Arr(p, "favorites"); Title(list.Count + " favorito(s)");
            foreach (var f in list) Row(Guid.Empty, UiKit.Text(S(f, "title").Length > 0 ? S(f, "title") : S(f, "name"), "lv-bold"), UiKit.Text(GlobalSearchWindow.KindWords(S(f, "kind")) + (S(f, "subtitle").Length > 0 ? " · " + S(f, "subtitle") : ""), "lv-small", "lv-muted"));
            if (list.Count == 0) Line(IsSelf ? "Seus favoritos (da Busca) aparecem aqui quando você liga 'Mostrar os favoritos' em Sobre." : "Nenhum favorito à vista.", "lv-small", "lv-muted");
        }

        // ---- Grupos e terrenos ----

        private void GroupsAndLand(JsonElement p)
        {
            Title("Grupos"); var groups = Arr(p, "groups");
            foreach (var g in groups)
            {
                var id = G(g, "id"); var name = S(g, "name"); var shown = Flag(g, "shown");
                var cells = new List<VisualElement> { UiKit.Text((S(g, "tag").Length > 0 ? "[" + S(g, "tag") + "] " : "") + name, "lv-bold"), UiKit.Text(S(g, "role"), "lv-small", "lv-muted"), UiKit.Spacer(), Btn("Ver " + name, () => new GroupProfileWindow(_c, id).Open()) };
                if (IsSelf) cells.Add(Btn((shown ? "Esconder " : "Mostrar ") + name, () => Send("PUT", "/api/v1/users/me/groups/" + id + "/profile", new { shown = !shown }, shown ? name + " some do perfil." : name + " aparece no perfil.", () => Show("grupos"))));
                Row(id, cells.ToArray());
            }
            if (groups.Count == 0) Line("Nenhum grupo à vista.", "lv-small", "lv-muted");
            Title("Terrenos"); var parcels = Arr(p, "parcels");
            foreach (var t in parcels)
            {
                var id = G(t, "id"); var name = S(t, "name"); var shown = Flag(t, "shown");
                var cells = new List<VisualElement> { UiKit.Text(S(t, "code") + " · " + name, "lv-bold"), UiKit.Text(S(t, "islandName") + " · " + D(t, "area").ToString("N0", Pt) + " m²", "lv-small", "lv-muted"), UiKit.Spacer(), Btn("Sobre " + name, () => new ParcelInfoWindow(_c, G(t, "islandId"), id).Open()) };
                if (IsSelf) cells.Add(Btn((shown ? "Esconder " : "Mostrar ") + name, () => Send("PUT", "/api/v1/users/me/parcels/" + id + "/profile", new { shown = !shown }, shown ? name + " some do perfil." : name + " aparece no perfil.", () => Show("grupos"))));
                Row(id, cells.ToArray());
            }
            if (parcels.Count == 0) Line("Nenhum terreno à vista.", "lv-small", "lv-muted");
        }

        // ---- Ações (somebody else's profile) ----

        private void Actions_()
        {
            if (IsSelf) { Line("Estas são as ações que os outros têm no seu perfil.", "lv-small", "lv-muted"); return; }
            var name = S(_profile, "displayName");
            Buttons(Btn("Mandar mensagem", () => { var social = new SocialWindow(_c); social.Open(); social.Talk(_user, name); }, "lv-btn-primary"), Btn("Amizade, teleporte, bloquear…", () => new PersonActionsWindow(_c, _user, name).Open()));
            Title("Enviar um presente"); Line("Escolha um item do seu inventário: a pessoa recebe a oferta e aceita ou recusa.", "lv-small", "lv-muted");
            Get("/api/v1/inventory/items?view=all&page=1&pageSize=100", root =>
            {
                var items = Arr(root, "items").Where(i => (I(i, "ownerRights") & LifeVerse.Shared.Items.ItemRights.Transfer) != 0).ToList(); if (items.Count == 0) { Line("Você não tem itens que possam ser dados.", "lv-small", "lv-muted"); return; }
                var pick = Choice("Item", items.Select(i => (S(i, "itemId"), S(i, "name"))).ToList(), S(items[0], "itemId")); var note = Field("Recado", "", 200);
                Buttons(Sure("Dar o presente", () => "Dar " + pick.value + " para " + name + "?", () => Send("POST", "/api/v1/inventory/items/" + IdOf(pick) + "/offers", new { toUserId = _user, kind = "gift", message = note.value.Trim(), keepCopy = false, idempotencyKey = Guid.NewGuid().ToString() }, "Presente oferecido a " + name + ".", null), "lv-btn-primary"));
            });
        }
    }
}
