using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Prototypes.HudDirections
{
    /// <summary>
    /// 3 · Varanda. The place where people meet: warm paper, deck wood and sea colours. The person's own face is the centre of the bar, with company on one side and the world on the
    /// other; the place is a signboard with who is there, the map a postcard and the talk comes in speech bubbles.
    /// </summary>
    public sealed class HudVaranda : HudDirection
    {
        public override string Id => "3-varanda";
        public override string Title => "Varanda";
        protected override string Sheet => "Varanda";
        protected override string Prefix => "vr";
        protected override string BodyFont => "Corbel";

        private static FontDefinition Serif => Kit.Font("Sitka", "Text");
        private static FontDefinition Bold => Kit.Font("Corbel", "Bold");
        private static readonly Color Paper = Kit.Hex("#F8F1E4"), Ink = Kit.Hex("#2E2420"), Clay = Kit.Hex("#B4472F"), Sea = Kit.Hex("#146B64"), Sun = Kit.Hex("#E39A2D"), Wood = Kit.Hex("#9C6B45");

        private static Color Shirt(string name) => name switch { "Ana" => Sea, "Beto" => Kit.Hex("#9A6410"), "Marina" => Kit.Hex("#5E4B91"), "Caio" => Kit.Hex("#2F5791"), _ => Clay };
        private static Color Presence(string presence) => presence switch { "away" => Sun, "busy" => Kit.Hex("#D6453D"), _ => Kit.Hex("#1F9560") };

        private static Glyph Icon(VisualElement parent, string id, float size, Color? accent = null) => Glyph.Add(parent, IconSet.Solid, id, size, accent ?? Clay, Paper);

        private static VisualElement Tap(VisualElement parent, string icon, float size, string function, string classes = "vr-tap", Color? accent = null)
        {
            var tap = Kit.El(parent, classes).Fn(function); tap.focusable = true; Icon(tap, icon, size, accent); return tap;
        }

        private static VisualElement Face(VisualElement parent, string name, string classes = "vr-face")
        {
            var face = Kit.El(parent, classes); face.style.backgroundColor = Shirt(name); Kit.Text(face, name.Substring(0, 1), "vr-initial", Bold); return face;
        }

        protected override void Compose(VisualElement root, HudState state, WorldBackdrop.People people, float pictureAspect)
        {
            Kit.Plates(root, people, pictureAspect, who =>
            {
                var plate = Kit.El(null, "vr-plate"); var tag = Kit.El(plate, "vr-row vr-plate-tag"); var face = Face(tag, who.name, "vr-face is-tiny"); face.style.borderTopColor = face.style.borderBottomColor = face.style.borderLeftColor = face.style.borderRightColor = Presence(who.presence);
                Kit.Text(tag, who.name, "vr-plate-name", Bold); Kit.El(plate, "vr-plate-tail"); return plate;
            });
            Sign(root); TopBar(root); Postcard(root, state); Chat(root, state); Dock(root, state);
            if (state == HudState.Menu) Sheet_(root); else Toasts(root, state);
        }

        private void Sign(VisualElement root)
        {
            var sign = Kit.El(root, "vr-paper vr-sign").At(left: 24, top: 22).Region("placa");
            var head = Kit.El(sign, "vr-row");
            Kit.Text(head, Model.Island, "vr-place", Serif).Fn("island-name", "open-map");
            var who = Kit.El(sign, "vr-row vr-who-here").Fn("people-count");
            foreach (var name in new[] { "Ana", "Beto", "Marina", "Caio", "Você" }) Face(who, name, "vr-face is-stacked");
            Kit.Text(who, Model.Here + " pessoas aqui", "vr-strong", Bold); Kit.Text(who, Model.Coordinates, "vr-soft vr-coords").Fn("coords");
            var links = Kit.El(sign, "vr-row vr-links");
            Tap(links, "back", 16, "back", "vr-tap is-small"); Tap(links, "forward", 16, "forward", "vr-tap is-small");
            foreach (var (icon, label, function) in new[] { ("info", "Sobre a ilha", "about-island"), ("parcel", "Este terreno", "about-parcel") })
            {
                var link = Kit.El(links, "vr-row vr-link").Fn(function); link.focusable = true; Icon(link, icon, 17, Sea); Kit.Text(link, label, "vr-link-text", Bold);
            }
        }

        private void TopBar(VisualElement root)
        {
            var bar = Kit.El(root, "vr-row vr-top").At(right: 24, top: 22).Region("topo");
            var search = Kit.El(bar, "vr-row vr-paper vr-pill vr-search").Fn("search"); search.focusable = true; Icon(search, "search", 18, Clay); Kit.Text(search, "Procurar pessoas e lugares", "vr-soft");
            var money = Kit.El(bar, "vr-row vr-paper vr-pill"); Icon(money, "coin", 22, Sun); Kit.Text(money, Model.Balance, "vr-money", Bold).Fn("balance");
            var buy = Kit.El(money, "vr-row vr-buy").Fn("buy"); buy.focusable = true; Kit.Text(buy, "Comprar", "vr-buy-text", Bold);
            var info = Kit.El(bar, "vr-row vr-paper vr-pill");
            var bell = Tap(info, "notifications", 22, "tool:notifications", "vr-tap vr-bell", Clay); Kit.Text(bell, Model.Unread.ToString(), "vr-badge", Bold);
            Kit.Text(info, Model.Clock, "vr-clock", Bold).Fn("clock");
            var net = Kit.El(info, "vr-row").Fn("connection"); Kit.El(net, "vr-online"); Kit.Text(net, "conectado", "vr-soft");
        }

        private void Postcard(VisualElement root, HudState state)
        {
            var card = Kit.El(root, "vr-postcard").At(right: 24, bottom: 20).Region("cartao").Fn("minimap", "tool:minimap");
            var picture = Kit.Canvas(card, 224, 176, (p, a) =>
            {
                Kit.Poly(p, new Vector2(0, 0), new Vector2(a.width, 0), new Vector2(a.width, a.height), new Vector2(0, a.height)); p.fillColor = Kit.Hex("#5FB3B8"); p.Fill();
                foreach (var y in new[] { 26f, 74f, 122f, 160f }) for (var x = 14f; x < a.width; x += 54f) Kit.Line(p, new Vector2(x + y % 31f, y), new Vector2(x + y % 31f + 14f, y), Kit.Hex("#FFFFFF", .35f), 2f);
                var area = new Rect(30, 6, 164, 164);
                Kit.Island(p, area, Kit.Hex("#8DBF6A"), Kit.Hex("#F2DDA8"), 6f);
                var deck = new Vector2(area.x + .49f * area.width, area.y + .43f * area.height); Kit.Poly(p, deck + new Vector2(-18, -9), deck + new Vector2(18, -9), deck + new Vector2(18, 9), deck + new Vector2(-18, 9)); p.fillColor = Kit.Hex("#C08A5C"); p.Fill();
                var light = new Vector2(area.x + .28f * area.width, area.y + .30f * area.height); Kit.Disc(p, light, 5.5f, Color.white); Kit.Disc(p, light, 2.6f, Clay);
                foreach (var (name, at) in Kit.Dots) { var d = new Vector2(area.x + at.x * area.width, area.y + at.y * area.height); Kit.Disc(p, d, 6f, Color.white); Kit.Disc(p, d, 4f, Shirt(name)); }
                var me = new Vector2(area.x + Kit.Me.x * area.width, area.y + Kit.Me.y * area.height); Kit.Disc(p, me, 9f, Color.white); Kit.Disc(p, me, 6.5f, Clay); Kit.Ring(p, me, 14f, Kit.Hex("#B4472F", .55f), 2f);
            }, "vr-picture");
            Kit.Text(picture, "N", "vr-north", Bold).At(left: 8, top: 4);
            var foot = Kit.El(card, "vr-row vr-postcard-foot");
            Kit.Text(foot, "Você está aqui", "vr-caption vr-grow", Serif);
            Tap(foot, "zoomin", 14, "minimap-zoom-in", "vr-tap is-round" + (state == HudState.Conversando ? " is-focus" : "")); Tap(foot, "zoomout", 14, "minimap-zoom-out", "vr-tap is-round"); Tap(foot, "close", 12, "minimap-close", "vr-tap is-round");
        }

        private void Chat(VisualElement root, HudState state)
        {
            var open = state == HudState.Conversando;
            var chat = Kit.El(root, "vr-chat").At(left: 24, bottom: 128).Region("conversa");
            var heads = Kit.El(chat, "vr-row vr-heads").Fn("chat-tabs");
            foreach (var (name, unread) in Model.Tabs)
            {
                var on = name == (open ? "Ana" : "Por perto");
                var head = Kit.El(heads, "vr-row vr-head" + (on ? " is-on" : "")); head.focusable = true;
                if (name == "Por perto") Icon(head, "people", 20, on ? Sun : Clay); else Face(head, name == "Construtores" ? "Caio" : name, "vr-face is-tiny");
                Kit.Text(head, name, "vr-head-text", Bold);
                if (unread > 0) Kit.Text(head, unread.ToString(), "vr-badge is-inline", Bold);
                if (open && on) Tap(head, "close", 11, "chat-close-tab", "vr-tap is-tiny", Paper);
            }
            Tap(heads, "minimize", 14, "chat-minimize", "vr-tap is-round vr-fold");
            var log = Kit.El(chat, "vr-log").Fn("chat-log");
            var lines = open ? Model.WithAna : Model.Near;
            for (var i = open ? 0 : 1; i < lines.Length; i++)
            {
                var line = lines[i]; var row = Kit.El(log, "vr-row vr-said-row" + (line.Mine ? " is-mine" : ""));
                if (!line.Mine) Face(row, line.Who, "vr-face is-small");
                var bubble = Kit.El(row, "vr-bubble" + (line.Mine ? " is-mine" : ""));
                if (!line.Mine) { var by = Kit.Text(bubble, line.Who, "vr-by", Bold); by.style.color = Shirt(line.Who); }
                Kit.Text(bubble, line.Text, "vr-said"); Kit.Text(bubble, line.Time, "vr-when");
            }
            var field = Kit.El(chat, "vr-row vr-paper vr-field" + (open ? " is-active" : "")).Fn("chat-field");
            Kit.Text(field, open ? "te espero na porta do farol" : "Diga olá…  (Enter)", (open ? "vr-typed" : "vr-soft") + " vr-grow"); Tap(field, "send", 17, "chat-send", "vr-tap vr-send", Paper);
        }

        private void Dock(VisualElement root, HudState state)
        {
            var wrap = Kit.El(root, "vr-dock-wrap").At(bottom: 12);
            var dock = Kit.El(wrap, "vr-row vr-dock").Region("varanda").Fn("toolbar-customize");
            Kit.El(dock, "vr-deck");
            foreach (var id in new[] { "chat", "voice", "gestures", "people" }) Action(dock, id, state);
            var me = Kit.El(dock, "vr-me").Fn("presence"); me.focusable = true;
            var medal = Kit.El(me, "vr-medal"); Face(medal, "Você", "vr-face is-big");
            var label = Kit.El(me, "vr-row"); Kit.Text(label, Model.Presence, "vr-me-text", Bold); Icon(label, "chevron", 13, Clay);
            foreach (var id in new[] { "map", "build", "inventory", "photo" }) Action(dock, id, state);
            Kit.El(dock, "vr-divider");
            Action(dock, "more", state);
            if (state != HudState.Conversando) return;
            var pop = Kit.El(wrap, "vr-paper vr-presences").At(bottom: 118);
            Kit.Text(pop, "Como estou", "vr-pop-title", Serif);
            foreach (var (text, colour) in Model.Presences) { var row = Kit.El(pop, "vr-row vr-option" + (text == Model.Presence ? " is-on" : "")); var dot = Kit.El(row, "vr-online"); dot.style.backgroundColor = Kit.Hex(colour); Kit.Text(row, text, "vr-option-text", Bold); }
        }

        private void Action(VisualElement dock, string id, HudState state)
        {
            var on = state == HudState.Menu && id == "more" || state == HudState.Conversando && id == "chat"; var hover = state == HudState.Conversando && id == "gestures"; var focus = state == HudState.Conversando && id == "build";
            var action = Kit.El(dock, "vr-action" + (on ? " is-on" : "") + (hover ? " is-hover" : "") + (focus ? " is-focus" : "")).Fn("tool:" + id); action.focusable = true;
            Icon(action, id, 30, on ? Sun : Clay); var (_, label, key) = Model.Tool(id); Kit.Text(action, label, "vr-action-text", Bold);
            if (hover && key.Length > 0) { var tip = Kit.El(action, "vr-tip"); Kit.Text(tip, label + "  ·  tecla " + key, "vr-tip-text"); }
        }

        private void Toasts(VisualElement root, HudState state)
        {
            var wrap = Kit.El(root, "vr-toasts").At(right: 24, top: 90).Region("bilhetes");
            for (var i = 0; i < (state == HudState.Conversando ? 2 : 1); i++)
            {
                var toast = Kit.El(wrap, "vr-row vr-toast").Fn("toasts"); var stamp = Kit.El(toast, "vr-stamp"); stamp.style.backgroundColor = i == 0 ? Sea : Wood; Icon(stamp, i == 0 ? "check" : "people", 18, Paper).AddToClassList("vr-on-colour");
                Kit.Text(toast, Model.Notices[i].Text, "vr-toast-text");
            }
        }

        private void Sheet_(VisualElement root)
        {
            var veil = Kit.El(root, "vr-veil").At(0, 0, 0, 0);
            var holder = Kit.El(veil, "vr-sheet-holder").At(bottom: 118);
            var sheet = Kit.El(holder, "vr-paper vr-sheet");
            var head = Kit.El(sheet, "vr-row vr-sheet-head"); Kit.Text(head, "Tudo da ilha", "vr-sheet-title", Serif); Kit.Text(head, "o menu completo, agrupado pelo que você quer fazer", "vr-soft vr-grow"); Kit.Text(head, "Esc fecha", "vr-soft"); Icon(head, "close", 16, Clay).AddToClassList("vr-close");
            var columns = Kit.El(sheet, "vr-row vr-columns");
            VisualElement column = null;
            foreach (var (title, items) in Model.Menus)
            {
                if (title != "Desenvolver") column = Kit.El(columns, "vr-column" + (title == "Eu" ? " is-wide" : "") + (title == "Mundo" ? " is-double" : ""));
                Kit.Text(column, title, "vr-heading" + (title == "Desenvolver" ? " is-second" : ""), Serif).Fn("menu:" + title);
                var list = Kit.El(column, title == "Mundo" ? "vr-row vr-wrap" : null);
                foreach (var (label, key) in items)
                {
                    var item = Kit.El(list, "vr-row vr-item" + (title == "Mundo" ? " is-half" : "") + (label == "Mapa do mundo" ? " is-focus" : "")).Fn(Model.ItemId(title, label)); item.focusable = true;
                    Kit.Text(item, label, "vr-item-text", Bold); if (key.Length > 0) Kit.Text(item, key, "vr-item-key", Bold);
                }
            }
            var own = Kit.El(sheet, "vr-own");
            var title2 = Kit.El(own, "vr-row"); Kit.Text(title2, "Sua varanda", "vr-heading", Serif).Fn("toolbar-customize"); Kit.Text(title2, "toque para mostrar ou guardar; arraste para mudar a ordem", "vr-soft vr-own-hint");
            var chips = Kit.El(own, "vr-row vr-wrap");
            var onDock = new[] { "chat", "voice", "gestures", "people", "map", "build", "inventory", "photo", "more" };
            foreach (var (id, label, _) in Model.Tools)
            {
                var on = System.Array.IndexOf(onDock, id) >= 0;
                var chip = Kit.El(chips, "vr-row vr-chip" + (on ? " is-on" : "")).Fn("tool:" + id); chip.focusable = true; Icon(chip, id, 18, on ? Sun : Clay); Kit.Text(chip, label, "vr-chip-text", Bold);
            }
            var options = Kit.El(own, "vr-row vr-options");
            Segments(options, "Onde fica", "toolbar-position", "Embaixo", "À esquerda", "À direita"); Segments(options, "Interface", "interface-mode", "Completa", "Compacta", "Imersiva (F10)");
        }

        private static void Segments(VisualElement parent, string title, string function, params string[] options)
        {
            Kit.Text(parent, title, "vr-strong vr-options-title", Bold); var row = Kit.El(parent, "vr-row vr-segments").Fn(function);
            for (var i = 0; i < options.Length; i++) Kit.Text(row, options[i], "vr-segment" + (i == 0 ? " is-on" : ""), Bold);
        }
    }
}
