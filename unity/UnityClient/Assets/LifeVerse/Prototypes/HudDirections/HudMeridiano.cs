using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Prototypes.HudDirections
{
    /// <summary>
    /// 2 · Meridiano. The HUD as a navigator's instrument: a heading tape at the top, a tool rail on the left edge, readings aligned in columns on the right and a radar below them.
    /// Panes of dark glass with cut corners; nothing is rounded and nothing glows.
    /// </summary>
    public sealed class HudMeridiano : HudDirection
    {
        public override string Id => "2-meridiano";
        public override string Title => "Meridiano";
        protected override string Sheet => "Meridiano";
        protected override string Prefix => "md";
        protected override string BodyFont => "Bahnschrift";

        private static readonly Color Ice = Kit.Hex("#8FE6FF"), Amber = Kit.Hex("#FFC66B"), Glass = Kit.Hex("#040D15", .88f), Deep = Kit.Hex("#030B12", .97f), Edge = Kit.Hex("#A9DEF5", .34f);

        /// <summary>A pane with cut corners: top left and bottom right by default.</summary>
        private static VisualElement Pane(VisualElement parent, string classes, float cut = 12f, Color? fill = null, Color? accent = null, bool allCorners = false)
        {
            var pane = Kit.El(parent, classes);
            pane.generateVisualContent += context =>
            {
                var w = pane.layout.width; var h = pane.layout.height; if (!(w > 1f) || !(h > 1f)) return; var p = context.painter2D; var c = Mathf.Min(cut, h / 2f); var o = allCorners ? c : 0f;
                var points = new[] { new Vector2(c, .5f), new Vector2(w - o - .5f, .5f), new Vector2(w - .5f, o + .5f), new Vector2(w - .5f, h - c), new Vector2(w - c, h - .5f), new Vector2(o + .5f, h - .5f), new Vector2(.5f, h - o - .5f), new Vector2(.5f, c) };
                Kit.Poly(p, points); p.fillColor = fill ?? Glass; p.Fill(); Kit.Poly(p, points); p.strokeColor = Edge; p.lineWidth = 1f; p.Stroke();
                if (accent is { } a) { Kit.Line(p, new Vector2(.5f, c), new Vector2(c, .5f), a, 2f); Kit.Line(p, new Vector2(w - .5f, h - c), new Vector2(w - c, h - .5f), a, 2f); Kit.Line(p, new Vector2(1f, c), new Vector2(1f, Mathf.Min(h - o, c + 28f)), a, 2f); }
            };
            return pane;
        }

        private static Glyph Icon(VisualElement parent, string id, float size, Color? accent = null) => Glyph.Add(parent, IconSet.Angular, id, size, accent ?? Ice, Deep);

        private static VisualElement Tap(VisualElement parent, string icon, float size, string function, string classes = "md-tap")
        {
            var tap = Kit.El(parent, classes).Fn(function); tap.focusable = true; Icon(tap, icon, size); return tap;
        }

        protected override void Compose(VisualElement root, HudState state, WorldBackdrop.People people, float pictureAspect)
        {
            if (state != HudState.Menu)
            {
                Kit.Plates(root, people, pictureAspect, who =>
                {
                    var plate = Kit.El(null, "md-plate");
                    var tag = Pane(plate, "md-row md-plate-tag", 6f, Kit.Hex("#06121C", .70f));
                    Kit.El(tag, "md-state is-" + who.presence); Kit.Text(tag, who.name.ToUpperInvariant(), "md-plate-name");
                    if (who.name != "Você") Kit.Text(tag, Mathf.RoundToInt(who.distance) + " m", "md-plate-range");
                    Kit.El(plate, "md-leader"); return plate;
                });
            }
            Menus(root, state); Compass(root); Rail(root, state);
            if (state == HudState.Menu) { Command(root); return; }
            Telemetry(root, state); Radar(root, state); Chat(root, state); Toasts(root, state);
        }

        private void Menus(VisualElement root, HudState state)
        {
            var bar = Pane(root, "md-row md-menubar", 10f, accent: Ice).At(left: 24, top: 18).Region("menus");
            Brand(bar);
            foreach (var (title, _) in Model.Menus)
            {
                var tab = Kit.Text(bar, title == "Desenvolver" ? "DEV" : title.ToUpperInvariant(), "md-menu" + (state == HudState.Menu && title == "Mundo" ? " is-on" : "")).Fn("menu:" + title); tab.focusable = true;
            }
            var search = Pane(root, "md-row md-search", 8f).At(left: 24, top: 62).Region("busca").Fn("search"); search.focusable = true;
            Icon(search, "search", 15); Kit.Text(search, "BUSCAR NO LIFEVERSE", "md-label md-grow"); Kit.Text(search, "/", "md-key");
        }

        private static void Brand(VisualElement parent)
        {
            Kit.Canvas(parent, 34, 16, (p, a) =>
            {
                Kit.Poly(p, new Vector2(1, 8), new Vector2(6, 2), new Vector2(11, 2), new Vector2(23, 14), new Vector2(28, 14), new Vector2(33, 8), new Vector2(28, 2), new Vector2(23, 2), new Vector2(11, 14), new Vector2(6, 14));
                p.strokeColor = Ice; p.lineWidth = 1.6f; p.Stroke();
            }, "md-brand");
        }

        private void Compass(VisualElement root)
        {
            var wrap = Kit.El(root, "md-compass-wrap").At(top: 14);
            var block = Kit.El(wrap, "md-compass").Region("rumo");
            var row = Kit.El(block, "md-row");
            Tap(row, "back", 15, "back");
            var tape = Kit.Canvas(row, 360, 34, (p, a) =>
            {
                for (var i = 0; i <= 36; i++)
                {
                    var x = i * 10f; var fade = 1f - Mathf.Abs(x - 180f) / 200f; var major = i % 9 == 0;
                    Kit.Line(p, new Vector2(x, major ? 18f : 23f), new Vector2(x, 28f), new Color(1f, 1f, 1f, .85f * fade), major ? 1.6f : 1f);
                }
                Kit.Line(p, new Vector2(0, 29f), new Vector2(360, 29f), Edge, 1f);
                Kit.Poly(p, new Vector2(174, 33), new Vector2(180, 26), new Vector2(186, 33)); p.fillColor = Ice; p.Fill();
            }, "md-tape");
            foreach (var (letter, x) in new[] { ("O", 0f), ("NO", 90f), ("N", 180f), ("NE", 270f), ("L", 360f) }) Kit.Text(tape, letter, "md-cardinal" + (letter == "N" ? " is-on" : "")).At(left: x - 16f, top: 0f);
            Tap(row, "forward", 15, "forward");
            var name = Kit.El(block, "md-row md-center");
            Kit.Text(name, Model.Island.ToUpperInvariant(), "md-island").Fn("island-name", "open-map");
            Tap(name, "info", 15, "about-island"); Tap(name, "parcel", 15, "about-parcel");
            var coords = Kit.El(block, "md-row md-center").Fn("coords");
            foreach (var (axis, value) in new[] { ("X", "001"), ("Y", "001"), ("Z", "−062") }) { Kit.Text(coords, axis, "md-label"); Kit.Text(coords, value, "md-coord"); }
        }

        private void Rail(VisualElement root, HudState state)
        {
            var wrap = Kit.El(root, "md-rail-wrap").At(left: 24, top: 108, bottom: 16);
            var rail = Pane(wrap, "md-rail", 14f, accent: Ice).Region("trilho").Fn("toolbar-customize");
            string[][] groups = { new[] { "chat", "voice", "gestures" }, new[] { "move", "camera", "photo" }, new[] { "build", "inventory", "map", "minimap" }, new[] { "people", "search", "wallet", "notifications" } };
            for (var g = 0; g < groups.Length; g++)
            {
                if (g > 0) Kit.El(rail, "md-rail-gap");
                foreach (var id in groups[g])
                {
                    var on = state == HudState.Conversando && id == "chat" || state == HudState.Menu && id == "map"; var flag = state == HudState.Conversando && id == "voice";
                    var item = Kit.El(rail, "md-tool" + (on ? " is-on" : "") + (flag ? " is-hover" : "")).Fn("tool:" + id); item.focusable = true; Icon(item, id, 21);
                    if (id == "notifications") Kit.Text(item, Model.Unread.ToString(), "md-badge");
                    var tip = Pane(item, "md-row md-flag", 6f, Deep); tip.pickingMode = PickingMode.Ignore; var (_, label, key) = Model.Tool(id); Kit.Text(tip, label.ToUpperInvariant(), "md-flag-text"); if (key.Length > 0) Kit.Text(tip, key, "md-key");
                }
            }
        }

        private void Telemetry(VisualElement root, HudState state)
        {
            var pane = Pane(root, "md-telemetry", 14f, accent: Ice).At(right: 24, top: 18).Region("leituras");
            VisualElement Reading(string label, string function)
            {
                var row = Kit.El(pane, "md-row md-reading").Fn(function); Kit.Text(row, label, "md-label md-grow"); return row;
            }
            var money = Reading("SALDO", "balance"); Kit.Text(money, Model.Balance, "md-value md-amber"); Tap(money, "plus", 13, "buy", "md-tap md-buy");
            Kit.Text(Reading("NA ILHA", "people-count"), "0" + Model.Here + " pessoas", "md-value");
            var net = Reading("REDE", "connection"); Icon(net, "signal", 14, Ice).AddToClassList("md-good"); Kit.Text(net, "CONECTADO", "md-value md-good");
            Kit.Text(Reading("HORA", "clock"), Model.Clock, "md-value");
            var presence = Reading("ESTADO", "presence"); presence.focusable = true; Kit.El(presence, "md-state is-online"); Kit.Text(presence, Model.Presence.ToUpperInvariant(), "md-value"); Icon(presence, "chevron", 12);
            var alerts = Reading("AVISOS", "tool:notifications"); alerts.focusable = true; Kit.Text(alerts, "0" + Model.Unread, "md-value md-amber"); Icon(alerts, "notifications", 15);
            if (state != HudState.Conversando) return;
            var list = Pane(root, "md-presences", 8f, Deep).At(right: 24, top: 224);
            foreach (var (label, colour) in Model.Presences) { var row = Kit.El(list, "md-row md-option" + (label == Model.Presence ? " is-on" : "")); var dot = Kit.El(row, "md-state"); dot.style.backgroundColor = Kit.Hex(colour); Kit.Text(row, label.ToUpperInvariant(), "md-flag-text"); }
        }

        private void Radar(VisualElement root, HudState state)
        {
            var holder = Kit.El(root, "md-radar-holder md-row").At(right: 24, bottom: 20).Region("radar").Fn("minimap", "tool:minimap");
            var buttons = Kit.El(holder, "md-radar-buttons");
            var zoom = Tap(buttons, "zoomin", 14, "minimap-zoom-in", "md-tap md-square" + (state == HudState.Conversando ? " is-focus" : "")); zoom.tooltip = "Aproximar";
            Tap(buttons, "zoomout", 14, "minimap-zoom-out", "md-tap md-square"); Tap(buttons, "close", 12, "minimap-close", "md-tap md-square");
            var radar = Kit.Canvas(holder, 210, 210, (p, a) =>
            {
                var c = a.center; var cut = 58f; var w = a.width - 1f; var h = a.height - 1f;
                var octagon = new[] { new Vector2(cut, 1), new Vector2(w - cut, 1), new Vector2(w, cut), new Vector2(w, h - cut), new Vector2(w - cut, h), new Vector2(cut, h), new Vector2(1, h - cut), new Vector2(1, cut) };
                Kit.Poly(p, octagon); p.fillColor = Glass; p.Fill();
                p.fillColor = Kit.Hex("#8FE6FF", .12f); p.BeginPath(); p.MoveTo(c); p.Arc(c, 92f, new Angle(-120f), new Angle(-62f)); p.ClosePath(); p.Fill();
                Kit.Island(p, new Rect(26, 26, a.width - 52, a.height - 52), Kit.Hex("#8FE6FF", .13f), Kit.Hex("#8FE6FF", .75f), 1f);
                Kit.Ring(p, c, 46f, Edge, 1f); Kit.Ring(p, c, 90f, Edge, 1f);
                Kit.Line(p, new Vector2(c.x, 8), new Vector2(c.x, a.height - 8), Kit.Hex("#A9DEF5", .18f)); Kit.Line(p, new Vector2(8, c.y), new Vector2(a.width - 8, c.y), Kit.Hex("#A9DEF5", .18f));
                foreach (var (_, at) in Kit.Dots) { var d = new Vector2(26 + at.x * (a.width - 52), 26 + at.y * (a.height - 52)); Kit.Poly(p, d + new Vector2(0, -4), d + new Vector2(4, 0), d + new Vector2(0, 4), d + new Vector2(-4, 0)); p.fillColor = Amber; p.Fill(); }
                var me = new Vector2(26 + Kit.Me.x * (a.width - 52), 26 + Kit.Me.y * (a.height - 52)); Kit.Poly(p, me + new Vector2(0, -8), me + new Vector2(6, 6), me + new Vector2(0, 3), me + new Vector2(-6, 6)); p.fillColor = Color.white; p.Fill();
                Kit.Poly(p, octagon); p.strokeColor = Kit.Hex("#A9DEF5", .6f); p.lineWidth = 1f; p.Stroke();
            }, "md-radar");
            Kit.Text(radar, "N", "md-cardinal is-on").At(left: 89, top: 3); Kit.Text(radar, "100 m", "md-label").At(left: 82, bottom: 4);
        }

        private void Chat(VisualElement root, HudState state)
        {
            var open = state == HudState.Conversando;
            var pane = Pane(root, "md-chat", 16f, accent: Ice).At(left: 104, bottom: 20).Region("conversa");
            var tabs = Kit.El(pane, "md-row md-tabs").Fn("chat-tabs");
            foreach (var (name, unread) in Model.Tabs)
            {
                var on = name == (open ? "Ana" : "Por perto");
                var tab = Kit.El(tabs, "md-row md-tab" + (on ? " is-on" : "")); Kit.Text(tab, name.ToUpperInvariant(), "md-tab-text"); if (unread > 0) Kit.Text(tab, unread.ToString(), "md-badge-inline");
                if (open && on) Tap(tab, "close", 10, "chat-close-tab", "md-tap md-mini");
            }
            Kit.El(tabs, "md-grow"); Tap(tabs, "minimize", 12, "chat-minimize", "md-tap md-mini");
            var log = Kit.El(pane, "md-log").Fn("chat-log");
            var lines = open ? Model.WithAna : Model.Near;
            for (var i = open ? 0 : 1; i < lines.Length; i++)
            {
                var line = lines[i]; var row = Kit.El(log, "md-row md-line"); Kit.Text(row, line.Time, "md-time"); Kit.Text(row, line.Who.ToUpperInvariant(), "md-who" + (line.Mine ? " is-mine" : "")); Kit.Text(row, line.Text, "md-said");
            }
            var field = Kit.El(pane, "md-row md-field" + (open ? " is-active" : "")).Fn("chat-field");
            Kit.Text(field, ">", "md-key"); Kit.Text(field, open ? "te espero na porta do farol" : "Enter para transmitir", open ? "md-typed md-grow" : "md-hint md-grow"); Tap(field, "send", 14, "chat-send", "md-tap md-mini");
        }

        private void Toasts(VisualElement root, HudState state)
        {
            var wrap = Kit.El(root, "md-toasts").At(right: 24, bottom: 244).Region("avisos");
            for (var i = 0; i < (state == HudState.Conversando ? 2 : 1); i++)
            {
                var toast = Pane(wrap, "md-toast", 10f, accent: i == 0 ? Kit.Hex("#7CF0B8") : Ice).Fn("toasts");
                Kit.Text(toast, i == 0 ? "CONFIRMADO" : "GRUPO", "md-label"); Kit.Text(toast, Model.Notices[i].Text, "md-toast-text");
                var timer = Kit.El(toast, "md-timer"); timer.style.width = Length.Percent(i == 0 ? 36 : 82);
            }
        }

        private void Command(VisualElement root)
        {
            var panel = Pane(root, "md-command", 22f, Deep, Ice).At(left: 104, top: 112, right: 24, bottom: 16);
            var head = Kit.El(panel, "md-row md-command-head");
            Kit.Text(head, "COMANDO", "md-command-title"); Kit.Text(head, "tudo da barra de menus, em um painel", "md-label md-grow"); Kit.Text(head, "ESC", "md-key"); Kit.Text(head, "FECHAR", "md-label"); Icon(head, "close", 14);
            var columns = Kit.El(panel, "md-row md-columns");
            VisualElement column = null; var n = 0;
            foreach (var (title, items) in Model.Menus)
            {
                if (title != "Desenvolver") column = Kit.El(columns, "md-column" + (title == "Eu" ? " is-wide" : ""));
                Kit.Text(column, title.ToUpperInvariant(), "md-heading" + (title == "Desenvolver" ? " is-second" : "")).Fn("menu:" + title);
                foreach (var (label, key) in items)
                {
                    var item = Kit.El(column, "md-row md-item" + (label == "Mapa do mundo" ? " is-focus" : "")).Fn(Model.ItemId(title, label)); item.focusable = true;
                    Kit.Text(item, (++n).ToString("00"), "md-index"); Kit.Text(item, label, "md-item-text md-grow"); if (key.Length > 0) Kit.Text(item, key, "md-key");
                }
            }
            var strip = Kit.El(panel, "md-strip");
            Kit.Text(strip, "TRILHO  —  arraste para reordenar", "md-heading").Fn("toolbar-customize");
            var chips = Kit.El(strip, "md-row md-wrap");
            var n2 = 0;
            foreach (var (id, label, _) in Model.Tools)
            {
                var on = n2++ < Model.DefaultShown;
                var chip = Kit.El(chips, "md-row md-chip" + (on ? " is-on" : "")).Fn("tool:" + id); chip.focusable = true; Icon(chip, id, 15, on ? Ice : Kit.Hex("#8FE6FF", .35f)); Kit.Text(chip, label.ToUpperInvariant(), "md-chip-text");
            }
            var options = Kit.El(strip, "md-row md-options");
            Segments(options, "POSIÇÃO", "toolbar-position", "ESQUERDA", "EMBAIXO", "DIREITA"); Segments(options, "INTERFACE", "interface-mode", "COMPLETA", "COMPACTA", "IMERSIVA  F10");
        }

        private static void Segments(VisualElement parent, string title, string function, params string[] options)
        {
            Kit.Text(parent, title, "md-label md-options-title"); var row = Kit.El(parent, "md-row md-segments").Fn(function);
            for (var i = 0; i < options.Length; i++) Kit.Text(row, options[i], "md-segment" + (i == 0 ? " is-on" : ""));
        }
    }
}
