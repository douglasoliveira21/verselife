using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Prototypes.HudDirections
{
    /// <summary>
    /// 1 · Horizonte. No boxes: the interface is type set straight on the world, in the four corners, over a single line near the bottom — the sea horizon, where the actions stand.
    /// Everything else lives in the "Índice", a typographic page over the world.
    /// </summary>
    public sealed class HudHorizonte : HudDirection
    {
        public override string Id => "1-horizonte";
        public override string Title => "Horizonte";
        protected override string Sheet => "Horizonte";
        protected override string Prefix => "hz";
        protected override string BodyFont => "Century Gothic";

        private static FontDefinition Bold => Kit.Font("Century Gothic", "Bold");
        private static readonly Color Foam = Kit.Hex("#9FF2E1");

        private static Glyph Icon(VisualElement parent, string id, float size, string classes = null)
        {
            var glyph = Glyph.Add(parent, IconSet.Hairline, id, size, Foam); if (classes != null) glyph.AddToClassList(classes); return glyph;
        }

        private static VisualElement Tap(VisualElement parent, string icon, float size, string function, string classes = "hz-tap")
        {
            var tap = Kit.El(parent, classes).Fn(function); tap.focusable = true; Icon(tap, icon, size); return tap;
        }

        protected override void Compose(VisualElement root, HudState state, WorldBackdrop.People people, float pictureAspect)
        {
            if (state != HudState.Menu)
            {
                Kit.Fill(root, Kit.Shade(Kit.Hex("#041018"), 0f, .64f)).AddToClassList("hz-shade-top");
                Kit.Fill(root, Kit.Shade(Kit.Hex("#041018"), .62f, 0f)).AddToClassList("hz-shade-bottom");
                Kit.Plates(root, people, pictureAspect, who =>
                {
                    var plate = Kit.El(null, "hz-plate"); Kit.El(plate, "hz-dot " + "is-" + who.presence); Kit.Text(plate, who.name, "hz-plate-name", who.name == "Você" ? Bold : (FontDefinition?)null); return plate;
                });
                Place(root); Status(root, state); Map(root, state); Chat(root, state); Line(root, state); Toasts(root, state);
            }
            else Index(root);
        }

        private void Place(VisualElement root)
        {
            var place = Kit.El(root, "hz-place").At(left: 40, top: 26).Region("lugar");
            var head = Kit.El(place, "hz-row");
            Tap(head, "back", 18, "back");
            Kit.Text(head, Model.Island, "hz-island").Fn("island-name", "open-map");
            Tap(head, "forward", 18, "forward");
            var meta = Kit.El(place, "hz-row hz-meta");
            Kit.Text(meta, Model.Coordinates, "hz-coords").Fn("coords"); Kit.El(meta, "hz-tick");
            Kit.Text(meta, Model.Here + " aqui", "hz-soft").Fn("people-count"); Kit.El(meta, "hz-tick");
            var about = Kit.El(meta, "hz-link").Fn("about-island"); Icon(about, "info", 15); Kit.Text(about, "ilha", "hz-soft");
            var parcel = Kit.El(meta, "hz-link").Fn("about-parcel"); Icon(parcel, "parcel", 15); Kit.Text(parcel, "terreno", "hz-soft");
        }

        private void Status(VisualElement root, HudState state)
        {
            var bar = Kit.El(root, "hz-row hz-status").At(right: 40, top: 32).Region("estado");
            Tap(bar, "search", 20, "search");
            var money = Kit.El(bar, "hz-group"); Icon(money, "coin", 17, "hz-gold"); Kit.Text(money, Model.Balance, "hz-value hz-gold", Bold).Fn("balance"); Tap(money, "plus", 14, "buy", "hz-tap hz-gold");
            Kit.Text(Kit.El(bar, "hz-group"), Model.Clock, "hz-value").Fn("clock");
            var presence = Kit.El(bar, "hz-group hz-link").Fn("presence"); presence.focusable = true; Kit.El(presence, "hz-dot is-online"); Kit.Text(presence, Model.Presence, "hz-value"); Icon(presence, "chevron", 13);
            var bell = Kit.El(bar, "hz-group hz-link").Fn("tool:notifications"); Icon(bell, "notifications", 20); Kit.Text(bell, Model.Unread.ToString(), "hz-count", Bold);
            var net = Kit.El(bar, "hz-group").Fn("connection"); Icon(net, "signal", 15); Kit.Text(net, "conectado", "hz-soft");
            if (state != HudState.Conversando) return;
            var list = Kit.El(root, "hz-presences").At(right: 150, top: 64);
            foreach (var (label, colour) in Model.Presences) { var row = Kit.El(list, "hz-row hz-option" + (label == Model.Presence ? " is-on" : "")); var dot = Kit.El(row, "hz-dot"); dot.style.backgroundColor = Kit.Hex(colour); Kit.Text(row, label, "hz-option-text"); }
        }

        private void Map(VisualElement root, HudState state)
        {
            var holder = Kit.El(root, "hz-map").At(right: 40, top: state == HudState.Conversando ? 220 : 80).Region("minimapa").Fn("minimap", "tool:minimap");
            var disc = Kit.Canvas(holder, 168, 168, (p, area) =>
            {
                var c = area.center; var r = area.width / 2f - 2f;
                Kit.Disc(p, c, r, Kit.Hex("#07202C", .52f));
                Kit.Island(p, new Rect(area.x + 22, area.y + 22, area.width - 44, area.height - 44), Kit.Hex("#FFFFFF", .10f), Kit.Hex("#FFFFFF", .55f), 1f);
                foreach (var (_, at) in Kit.Dots) Kit.Disc(p, new Vector2(22 + at.x * (area.width - 44), 22 + at.y * (area.height - 44)), 2.6f, Color.white);
                var me = new Vector2(22 + Kit.Me.x * (area.width - 44), 22 + Kit.Me.y * (area.height - 44));
                Kit.Disc(p, me, 4f, Foam); Kit.Ring(p, me, 9f, Kit.Hex("#9FF2E1", .6f), 1f);
                Kit.Ring(p, c, r, Kit.Hex("#FFFFFF", .75f), 1.2f); Kit.Line(p, new Vector2(c.x, c.y - r), new Vector2(c.x, c.y - r + 9f), Color.white, 1.4f);
            });
            Kit.Text(disc, "N", "hz-north", Bold).At(left: 78, top: 11);
            if (state != HudState.Conversando) return;
            var controls = Kit.El(holder, "hz-row hz-map-controls");
            Tap(controls, "zoomin", 16, "minimap-zoom-in", "hz-tap is-focus"); Tap(controls, "zoomout", 16, "minimap-zoom-out"); Tap(controls, "close", 14, "minimap-close");
        }

        private void Chat(VisualElement root, HudState state)
        {
            var chat = Kit.El(root, "hz-chat").At(left: 40, bottom: 30).Region("conversa");
            if (state == HudState.Explorando)
            {
                var log = Kit.El(chat, "hz-log").Fn("chat-log");
                for (var i = 1; i < Model.Near.Length; i++) Say(log, Model.Near[i], i == 1);
                var hint = Kit.El(chat, "hz-field").Fn("chat-field"); Kit.Text(hint, "Enter", "hz-key", Bold); Kit.Text(hint, "conversar por perto", "hz-soft");
                return;
            }
            var tabs = Kit.El(chat, "hz-row hz-tabs").Fn("chat-tabs");
            foreach (var (name, unread) in Model.Tabs)
            {
                var tab = Kit.El(tabs, "hz-tab" + (name == "Ana" ? " is-on" : ""));
                Kit.Text(tab, name, "hz-tab-text", name == "Ana" ? Bold : (FontDefinition?)null); if (unread > 0) Kit.Text(tab, unread.ToString(), "hz-count", Bold);
                if (name == "Ana") Tap(tab, "close", 11, "chat-close-tab", "hz-tap hz-small");
            }
            Kit.El(tabs, "hz-grow"); Tap(tabs, "minimize", 14, "chat-minimize", "hz-tap hz-small");
            var full = Kit.El(chat, "hz-log").Fn("chat-log"); foreach (var line in Model.WithAna) Say(full, line, false);
            var field = Kit.El(chat, "hz-field is-active").Fn("chat-field"); Kit.Text(field, "te espero na porta do farol", "hz-typed"); Kit.El(field, "hz-caret"); Kit.El(field, "hz-grow"); Tap(field, "send", 17, "chat-send", "hz-tap hz-small");
        }

        private static void Say(VisualElement log, (string Time, string Who, string Text, bool Mine) line, bool faded)
        {
            var row = Kit.El(log, "hz-row hz-line" + (faded ? " is-faded" : "")); Kit.Text(row, line.Time, "hz-time"); Kit.Text(row, line.Who, "hz-who" + (line.Mine ? " is-mine" : ""), Bold); Kit.Text(row, line.Text, "hz-said");
        }

        private void Line(VisualElement root, HudState state)
        {
            var wrap = Kit.El(root, "hz-horizon").At(bottom: 24); var line = Kit.El(wrap, "hz-line-box").Region("linha").Fn("toolbar-customize");
            Kit.El(line, "hz-rule");   // never wider than the bar: on a small screen it ran into the talk
            foreach (var id in new[] { "chat", "voice", "gestures", "build", "people", "map", "inventory", "more" })
            {
                var on = state == HudState.Conversando && id == "chat"; var hover = state == HudState.Conversando && id == "voice"; var first = state == HudState.Explorando && id == "more";
                var item = Kit.El(line, "hz-action" + (on ? " is-on" : "") + (hover || first ? " is-hover" : "")).Fn("tool:" + id); item.focusable = true;
                Icon(item, id == "more" ? "menu" : id, 24); Kit.El(item, "hz-pip"); Kit.Text(item, id == "more" ? "Índice" : Model.Tool(id).Label, "hz-label");
            }
        }

        private void Toasts(VisualElement root, HudState state)
        {
            var wrap = Kit.El(root, "hz-toasts").At(top: 36);
            for (var i = 0; i < (state == HudState.Conversando ? 2 : 1); i++)
            {
                var toast = Kit.El(wrap, "hz-row hz-toast").Fn("toasts"); Kit.El(toast, "hz-dash"); Kit.Text(toast, Model.Notices[i].Text, "hz-toast-text");
            }
        }

        private void Index(VisualElement root)
        {
            var veil = Kit.El(root, "hz-veil").At(0, 0, 0, 0);
            var top = Kit.El(veil, "hz-row hz-index-top");
            Kit.Text(top, "Índice", "hz-index-title");
            var search = Kit.El(top, "hz-field hz-index-search").Fn("search", "tool:search"); Icon(search, "search", 17); Kit.Text(search, "Buscar no LifeVerse", "hz-soft");
            Kit.El(top, "hz-grow");
            var close = Kit.El(top, "hz-row hz-link"); Kit.Text(close, "Esc", "hz-key", Bold); Kit.Text(close, "fechar", "hz-soft"); Icon(close, "close", 16);

            var columns = Kit.El(veil, "hz-row hz-columns");
            VisualElement column = null;
            foreach (var (title, items) in Model.Menus)
            {
                if (title != "Desenvolver") column = Kit.El(columns, "hz-column" + (title == "Eu" ? " is-wide" : ""));
                Kit.Text(column, title.ToUpperInvariant(), "hz-heading" + (title == "Desenvolver" ? " is-second" : ""), Bold).Fn("menu:" + title);
                foreach (var (label, key) in items)
                {
                    var item = Kit.El(column, "hz-row hz-item" + (label == "Mapa do mundo" ? " is-focus" : "")).Fn(Model.ItemId(title, label)); item.focusable = true;
                    Kit.Text(item, label, "hz-item-text"); if (key.Length > 0) Kit.Text(item, key, "hz-item-key");
                }
            }

            var own = Kit.El(columns, "hz-column hz-own");
            Kit.Text(own, "NA SUA LINHA", "hz-heading", Bold).Fn("toolbar-customize");
            var grid = Kit.El(own, "hz-row hz-wrap");
            var onLine = new[] { "chat", "voice", "gestures", "build", "people", "map", "inventory", "more" };
            foreach (var (id, label, _) in Model.Tools)
            {
                var on = System.Array.IndexOf(onLine, id) >= 0;
                var chip = Kit.El(grid, "hz-row hz-choice" + (on ? " is-on" : "")).Fn("tool:" + id); chip.focusable = true; Icon(chip, id, 17); Kit.Text(chip, label, "hz-choice-text"); if (on) Icon(chip, "check", 12, "hz-foam");
            }
            Choice(own, "Posição da linha", "toolbar-position", "Embaixo", "Esquerda", "Direita");
            Choice(own, "Interface", "interface-mode", "Completa", "Compacta", "Imersiva");
            Kit.Text(own, "Arraste para reordenar. F10 mostra a interface no modo imersivo.", "hz-note");
        }

        private static void Choice(VisualElement parent, string title, string function, params string[] options)
        {
            Kit.Text(parent, title, "hz-note hz-choice-title");
            var row = Kit.El(parent, "hz-row").Fn(function);
            for (var i = 0; i < options.Length; i++) Kit.Text(row, options[i], "hz-segment" + (i == 0 ? " is-on" : ""));
        }
    }
}
