using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// "Informações e gerenciamento da ilha", as in the Godot client: a side menu with the sections (Geral, Acesso, Banimentos, Permissões, Administração, Objetos e prims, Física, Desempenho, Ambiente, Mídia, Terreno,
    /// Reinicialização, Histórico e auditoria, plus Regras, Terrenos and Propriedade), the summary of the island on top (state, visitors, owner, prims used) and the page of the section. Everything is read from
    /// <c>/api/v1/worlds/islands/{id}/management</c> and every change goes there; the API decides who may do what and its refusals are told as they come.
    /// </summary>
    public sealed class IslandManagementWindow : SectionWindow
    {
        public static readonly (string Id, string Text)[] Sections =
        {
            ("geral", "Geral"), ("acesso", "Acesso"), ("banimentos", "Banimentos"), ("permissoes", "Permissões"), ("administracao", "Administração"), ("objetos", "Objetos e prims"), ("fisica", "Física"), ("desempenho", "Desempenho"),
            ("ambiente", "Ambiente"), ("midia", "Mídia"), ("terreno", "Terreno"), ("reinicio", "Reinicialização"), ("auditoria", "Histórico e auditoria"), ("regras", "Regras"), ("terrenos", "Terrenos"), ("propriedade", "Propriedade"),
        };
        private readonly Guid _island; private JsonElement _general; private DateTimeOffset _generalAt, _accessAt, _lookAt, _mediaAt;

        private string Base => "/api/v1/worlds/islands/" + _island + "/management";

        public IslandManagementWindow(ApiContext context, Guid island) : base(context, "island-management", "Gerenciamento da ilha", Sections, 860f, 560f, new Vector2(200, 60))
        {
            _island = island; Show("geral");
        }

        protected override void Fill(string section)
        {
            switch (section)
            {
                case "geral": General(); break; case "acesso": Access(); break; case "banimentos": Bans(); break; case "permissoes": Permissions(); break; case "administracao": Admin(); break;
                case "objetos": Objects(); break; case "fisica": Physics(); break; case "desempenho": Performance(); break; case "ambiente": Environment(); break; case "midia": Media(); break;
                case "terreno": Terrain(); break; case "reinicio": Restart(); break; case "auditoria": Audit(); break;
                case "regras": Rules(); break;
                case "terrenos": _page.Add(UiKit.Row(Btn("Abrir o gerenciamento de terrenos", () => new LandManagementWindow(_c, _island).Open(), "lv-btn-primary"))); _page.Add(new ParcelsPane(_c, _island)); break;
                case "propriedade": Property(); break;
            }
        }

        private void LoadSummary(Action then = null) => Get(Base, root =>
        {
            _general = root.Clone(); _generalAt = Date(root, "updatedAt") ?? default; var usage = Obj(root, "usage");
            _summary.text = S(root, "name") + "  ·  " + StateWords(S(root, "state")) + "  ·  " + I(root, "visitors") + "/" + I(root, "maxPlayers") + " pessoas  ·  dono: " + S(root, "ownerName") + "  ·  " + I(usage, "objects").ToString("N0", Pt) + "/" + I(usage, "maxObjects").ToString("N0", Pt) + " objetos, " + I(usage, "cost").ToString("N0", Pt) + "/" + I(usage, "maxCost").ToString("N0", Pt) + " prims" + (Flag(root, "canEdit") ? "" : "  ·  só leitura");
            then?.Invoke();
        });

        public static string StateWords(string state) => state switch { "online" => "no ar", "offline" => "desligada", "starting" => "ligando", "restarting" => "reiniciando", "stopping" => "desligando", _ => state };

        /// <summary>A duration told in words: "45 s", "12 min", "3 h 05 min", "2 d 4 h".</summary>
        public static string Duration(long seconds)
        {
            if (seconds < 0) seconds = 0; if (seconds < 60) return seconds + " s"; if (seconds < 3600) return (seconds / 60) + " min";
            if (seconds < 86400) return (seconds / 3600) + " h " + (seconds % 3600 / 60).ToString("00") + " min"; return (seconds / 86400) + " d " + (seconds % 86400 / 3600) + " h";
        }

        /// <summary>An expiry typed as a number of days ("" is "never"); null days with ok true means never, ok false means it was not a number from 1 to 3650.</summary>
        public static DateTimeOffset? ExpiryIn(string days, DateTimeOffset now, out bool ok)
        {
            ok = true; if (string.IsNullOrWhiteSpace(days)) return null;
            if (!int.TryParse(days.Trim(), out var n) || n < 1 || n > 3650) { ok = false; return null; } return now.AddDays(n);
        }

        /// <summary>The options of a list the API sends either as words or as objects with an id and a title.</summary>
        public static List<(string Id, string Text)> OptionsOf(JsonElement options, string name)
        {
            var list = new List<(string, string)>();
            foreach (var o in Arr(options, name)) { if (o.ValueKind == JsonValueKind.String) list.Add((o.GetString() ?? "", o.GetString() ?? "")); else if (o.ValueKind == JsonValueKind.Object) { var id = S(o, "id"); if (id.Length == 0) id = S(o, "key"); if (id.Length == 0) id = S(o, "value"); var text = S(o, "title"); if (text.Length == 0) text = S(o, "name"); if (text.Length == 0) text = S(o, "text"); list.Add((id, text.Length > 0 ? text : id)); } }
            return list;
        }

        // ---- Geral ----

        private void General() => LoadSummary(() =>
        {
            var g = _general; var options = Obj(g, "options"); var editable = Flag(g, "canEdit");
            Title("Identidade"); Field("Nome", S(g, "name"), (int)Math.Max(3, I(options, "nameLimit") is var nl && nl > 0 ? nl : 64)); Field("Descrição", S(g, "description"), (int)(I(options, "descriptionLimit") is var dl && dl > 0 ? dl : 1000)); Field("Imagem de capa (https)", S(g, "coverImageUrl"), 512);
            Title("Como aparece"); Choice("Categoria", OptionsOf(options, "categories"), S(g, "category")); Choice("Classificação", OptionsOf(options, "ratings"), S(g, "contentRating")); Choice("Idioma", OptionsOf(options, "languages"), S(g, "language"));
            Check("Aparecer no buscador", Flag(g, "showInSearch")); Check("Aparecer no mapa", Flag(g, "showOnMap"));
            Title("Lotação e chegada"); Field("Pessoas ao mesmo tempo", I(g, "maxPlayers").ToString(), 4); var spawn = Obj(g, "spawn"); Field("Ponto de chegada X", D(spawn, "x").ToString("0.#", CultureInfo.InvariantCulture), 10); Field("Ponto de chegada Z", D(spawn, "z").ToString("0.#", CultureInfo.InvariantCulture), 10);
            Line("Criada em " + When(Date(g, "createdAt")) + "  ·  " + I(g, "adminCount") + " administrador(es)  ·  tamanho " + I(g, "size") + " m", "lv-small", "lv-muted");
            if (editable) _page.Add(Btn("Salvar", SaveGeneral, "lv-btn-primary")); else Line("Só o dono e os administradores mudam estes dados.", "lv-small", "lv-muted");
        });

        /// <summary>The checks before the general data is sent. Null when fine.</summary>
        public static string ValidateGeneral(string name, string maxPlayers, string x, string z, out int max, out float spawnX, out float spawnZ)
        {
            max = 0; spawnX = spawnZ = 0;
            if ((name ?? "").Trim().Length < 3) return "O nome precisa ter pelo menos 3 letras.";
            if (!int.TryParse(maxPlayers, out max) || max < 1 || max > 256) return "Pessoas ao mesmo tempo: de 1 a 256.";
            if (!float.TryParse((x ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out spawnX) || !float.TryParse((z ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out spawnZ)) return "O ponto de chegada precisa de dois números.";
            return null;
        }

        public void SaveGeneral()
        {
            var problem = ValidateGeneral(Fields["Nome"].value, Fields["Pessoas ao mesmo tempo"].value, Fields["Ponto de chegada X"].value, Fields["Ponto de chegada Z"].value, out var max, out var x, out var z); if (problem != null) { Say(problem, true); return; }
            var cover = Fields["Imagem de capa (https)"].value.Trim();
            Send("PUT", Base + "/general", new { name = Fields["Nome"].value.Trim(), description = Fields["Descrição"].value, coverImageUrl = cover.Length == 0 ? null : cover, category = IdOf(Choices["Categoria"]), contentRating = IdOf(Choices["Classificação"]), language = IdOf(Choices["Idioma"]), maxPlayers = max, showInSearch = Toggles["Aparecer no buscador"].value, showOnMap = Toggles["Aparecer no mapa"].value, spawnX = x, spawnZ = z, expectedUpdatedAt = _generalAt }, "Dados da ilha salvos.", () => LoadSummary());
        }

        // ---- Acesso ----

        public static readonly (string Id, string Text)[] AccessModes = { ("public", "Pública: qualquer pessoa entra"), ("invite", "Só convidados (a lista abaixo)"), ("closed", "Fechada temporariamente") };

        private void Access(int page = 1) => Get(Base + "/access?page=" + page + "&q=" + Uri.EscapeDataString(Fields.TryGetValue("Buscar pessoa", out var f) ? f.value : ""), root =>
        {
            Reset(); _accessAt = Date(root, "updatedAt") ?? default;
            Title("Quem entra"); var mode = Choice("Modo de entrada", AccessModes.ToList(), S(root, "mode")); _page.Add(Btn("Salvar o modo", () => Send("PUT", Base + "/access/mode", new { mode = IdOf(mode), expectedUpdatedAt = _accessAt }, "Modo de entrada salvo.", () => Access()), "lv-btn-primary"));
            Title("Pessoas liberadas (" + I(root, "total") + ")"); var who = Field("Nome da pessoa", "", 64); var days = Field("Por quantos dias (vazio = sempre)", "", 4);
            _page.Add(Btn("Liberar", () => { var until = ExpiryIn(days.value, DateTimeOffset.UtcNow, out var ok); if (!ok) { Say("Dias: um número de 1 a 3650, ou vazio.", true); return; } if (who.value.Trim().Length == 0) { Say("Digite o nome da pessoa.", true); return; } Send("POST", Base + "/access/allow", new { name = who.value.Trim(), expiresAt = until }, who.value.Trim() + " foi liberada.", () => Access()); }));
            var search = Field("Buscar pessoa", "", 40); search.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return) Access(); }, TrickleDown.TrickleDown);
            foreach (var p in Arr(root, "items")) { var id = G(p, "permissionId"); Row(id, UiKit.Text(S(p, "name") + " (" + S(p, "username") + ")", "lv-bold"), UiKit.Spacer(), UiKit.Text(Date(p, "expiresAt") is { } e ? "até " + When(e) : "sem prazo", "lv-small", "lv-muted"), Sure("Tirar", () => "Confirmar", () => Send("DELETE", Base + "/access/allow/" + id, null, "Liberação retirada.", () => Access()))); }
            if (Arr(root, "items").Count == 0) Line("Ninguém liberado ainda.", "lv-small", "lv-muted"); Pager(root, Access);
        });

        // ---- Banimentos ----

        private void Bans(int page = 1, string status = "active") => Get(Base + "/bans?page=" + page + "&status=" + status + "&q=", root =>
        {
            Reset(); var counts = Obj(root, "counts");
            Line("Ativos: " + I(counts, "active") + "  ·  vencidos: " + I(counts, "expired") + "  ·  retirados: " + I(counts, "lifted"), "lv-small", "lv-muted");
            _page.Add(UiKit.Row(Btn("Ativos", () => Bans(1, "active")), Btn("Vencidos", () => Bans(1, "expired")), Btn("Retirados", () => Bans(1, "lifted")), Btn("Todos", () => Bans(1, "all"))));
            Title("Banir alguém"); var who = Field("Nome da pessoa", "", 64); var why = Field("Motivo", "", 300); var days = Field("Por quantos dias (vazio = para sempre)", "", 4);
            _page.Add(Sure("Banir", () => "Confirmar o banimento", () => Ban(who.value, why.value, days.value), "lv-btn-primary"));
            Title("Banimentos");
            foreach (var b in Arr(root, "items"))
            {
                var id = G(b, "banId"); var active = S(b, "status") == "active"; var cells = new List<VisualElement> { UiKit.Text(S(b, "name"), "lv-bold"), UiKit.Text(S(b, "reason"), "lv-small"), UiKit.Spacer(), UiKit.Text((active ? "ativo" : S(b, "status") == "expired" ? "vencido" : "retirado") + " · " + (Date(b, "expiresAt") is { } e ? "até " + When(e) : "para sempre") + " · por " + S(b, "bannedBy"), "lv-small", "lv-muted") };
                if (active) cells.Add(Sure("Retirar", () => "Confirmar", () => Send("DELETE", Base + "/bans/" + id, null, "Banimento retirado.", () => Bans(page, status))));
                Row(id, cells.ToArray());
            }
            if (Arr(root, "items").Count == 0) Line("Nenhum banimento aqui.", "lv-small", "lv-muted"); Pager(root, p => Bans(p, status));
        });

        public void Ban(string name, string reason, string days)
        {
            var until = ExpiryIn(days, DateTimeOffset.UtcNow, out var ok); if (!ok) { Say("Dias: um número de 1 a 3650, ou vazio.", true); return; }
            if ((name ?? "").Trim().Length == 0 || (reason ?? "").Trim().Length == 0) { Say("Digite o nome da pessoa e o motivo.", true); return; }
            Send("POST", Base + "/bans", new { name = name.Trim(), reason = reason.Trim(), expiresAt = until }, name.Trim() + " foi banida da ilha.", () => Bans());
        }

        // ---- Permissões ----

        private void Permissions(int page = 1) => Get(Base + "/permissions?page=" + page + "&q=", root =>
        {
            Reset(); var defs = Arr(root, "definitions").Select(d => (Key: S(d, "key"), Title: S(d, "title"), Text: S(d, "text"))).ToList();
            Title("O que cada permissão deixa fazer"); foreach (var d in defs) Line(d.Title + ": " + d.Text, "lv-small", "lv-muted");
            Title("Dar permissões"); var who = Field("Nome da pessoa", "", 64); var picks = new List<(string Key, Toggle Box)>(); foreach (var d in defs) picks.Add((d.Key, Check(d.Title, false)));
            _page.Add(Btn("Dar", () => { var chosen = picks.Where(p => p.Box.value).Select(p => p.Key).ToArray(); if (who.value.Trim().Length == 0 || chosen.Length == 0) { Say("Digite o nome e marque ao menos uma permissão.", true); return; } Send("POST", Base + "/permissions", new { name = who.value.Trim(), permissions = chosen }, "Permissões dadas.", () => Permissions()); }, "lv-btn-primary"));
            Title("Quem tem (" + I(root, "total") + ")");
            foreach (var p in Arr(root, "items"))
            {
                var user = G(p, "userId"); var has = Arr(p, "permissions").Select(x => x.GetString()).ToList(); var names = defs.Where(d => has.Contains(d.Key)).Select(d => d.Title).ToList();
                Row(user, UiKit.Text(S(p, "name") + (S(p, "role").Length > 0 ? " · " + S(p, "role") : ""), "lv-bold"), UiKit.Text(names.Count > 0 ? string.Join(", ", names) : string.Join(", ", has), "lv-small"), UiKit.Spacer(), Sure("Tirar tudo", () => "Confirmar", () => Send("DELETE", Base + "/permissions/" + user, null, "Permissões retiradas.", () => Permissions())));
            }
            Pager(root, Permissions);
        });

        // ---- Administração ----

        private void Admin() => Get(Base + "/server", root =>
        {
            Reset(); var running = Flag(root, "running");
            Line("Servidor: " + StateWords(S(root, "state")) + (running ? "  ·  no ar há " + Duration(I(root, "uptimeSeconds")) : "") + "  ·  " + I(root, "visitors") + "/" + I(root, "maxPlayers") + " pessoas" + (Flag(root, "closed") ? "  ·  fechada" : ""), "lv-bold");
            Title("Aviso para todos"); var text = Field("Texto do aviso", "", 300); _page.Add(Btn("Enviar para todos", () => Notice(text.value, null), "lv-btn-primary"));
            Title("Quem está na ilha"); var reason = Field("Motivo (para expulsar)", "", 200);
            var players = Flag(root, "fresh") || !root.TryGetProperty("fresh", out _) ? Arr(root, "players") : new List<JsonElement>();
            foreach (var p in players)
            {
                var user = G(p, "userId"); var name = S(p, "name");
                var cells = new List<VisualElement> { UiKit.Text(name + (Flag(p, "isStaff") ? " (equipe)" : ""), "lv-bold"), UiKit.Text(D(p, "x").ToString("0") + ", " + D(p, "y").ToString("0") + ", " + D(p, "z").ToString("0") + " · há " + Duration(I(p, "onlineSeconds")), "lv-small", "lv-muted"), UiKit.Spacer(), Btn("Avisar", () => Notice(text.value, user)) };
                if (!Flag(p, "isStaff")) cells.Add(Sure("Expulsar", () => "Confirmar", () => Send("POST", Base + "/server/kick", new { userId = user, reason = reason.value.Trim() }, name + " foi expulsa da ilha.", Admin)));
                Row(user, cells.ToArray());
            }
            if (players.Count == 0) Line(running ? "Ninguém na ilha agora." : "A ilha está desligada.", "lv-small", "lv-muted");
        });

        public void Notice(string text, Guid? user)
        {
            if ((text ?? "").Trim().Length == 0) { Say("Escreva o texto do aviso.", true); return; }
            Send("POST", Base + "/server/notice", new { text = text.Trim(), userId = user }, user == null ? "Aviso enviado para todos." : "Aviso enviado.");
        }

        // ---- Objetos e prims ----

        private void Objects(int page = 1, string sort = "cost", Guid? owner = null) => Get(Base + "/objects?page=" + page + "&sort=" + sort + "&scripts=&type=&q=" + (owner is { } o ? "&owner=" + o : ""), root =>
        {
            Reset(); var usage = Obj(root, "usage");
            Line(I(usage, "objects").ToString("N0", Pt) + " de " + I(usage, "maxObjects").ToString("N0", Pt) + " objetos  ·  " + I(usage, "cost").ToString("N0", Pt) + " de " + I(usage, "maxCost").ToString("N0", Pt) + " prims", "lv-bold");
            _page.Add(UiKit.Row(UiKit.Text("Ordem:", "lv-small"), Btn("Mais pesados", () => Objects(1, "cost", owner)), Btn("Mais novos", () => Objects(1, "recent", owner)), Btn("Nome", () => Objects(1, "name", owner)), Btn("Por dono", ObjectOwners)));
            if (owner != null) _page.Add(Btn("Mostrar de todos os donos", () => Objects(1, sort)));
            var picked = new List<(Guid Id, Toggle Box)>();
            foreach (var x in Arr(root, "items"))
            {
                var id = G(x, "id"); var box = new Toggle(); picked.Add((id, box)); Toggles["obj:" + id] = box;
                Row(id, box, UiKit.Text(S(x, "name"), "lv-bold"), UiKit.Text(S(x, "type") + " · " + S(x, "ownerName") + " · " + I(x, "cost") + " prims" + (I(x, "scripts") > 0 ? " · " + I(x, "scripts") + " script(s)" : "") + (Flag(x, "physics") ? " · físico" : "") + (Flag(x, "locked") ? " · travado" : ""), "lv-small", "lv-muted"));
            }
            if (picked.Count > 0) _page.Add(Sure("Devolver os marcados aos donos", () => "Confirmar a devolução", () => { var ids = picked.Where(p => p.Box.value).Select(p => p.Id).ToArray(); if (ids.Length == 0) { Say("Marque os objetos.", true); return; } Send("POST", Base + "/objects/return", new { objectIds = ids }, ids.Length + " objeto(s) devolvido(s).", () => Objects(page, sort, owner)); }));
            else Line("Nenhum objeto.", "lv-small", "lv-muted");
            Pager(root, p => Objects(p, sort, owner));
        });

        private void ObjectOwners() => Get(Base + "/objects/owners", root =>
        {
            Reset(); Title("Donos de objetos nesta ilha"); _page.Add(Btn("Voltar à lista", () => Objects()));
            foreach (var o in Arr(root, "items"))
            {
                var user = G(o, "userId"); var name = S(o, "name");
                Row(user, UiKit.Text(name, "lv-bold"), UiKit.Text(I(o, "objects") + " objetos · " + I(o, "cost") + " prims" + (I(o, "scripts") > 0 ? " · " + I(o, "scripts") + " script(s)" : ""), "lv-small", "lv-muted"), UiKit.Spacer(), Btn("Ver", () => Objects(1, "cost", user)), Sure("Devolver tudo", () => "Devolver tudo de " + name + "?", () => Send("POST", Base + "/objects/return-owner", new { ownerUserId = user }, "Objetos de " + name + " devolvidos.", ObjectOwners)));
            }
        });

        // ---- Física ----

        private void Physics(int page = 1, string state = "") => Get(Base + "/physics?page=" + page + "&state=" + state + "&q=", root =>
        {
            Reset(); var summary = Obj(root, "summary"); var server = Obj(root, "server");
            Line(I(summary, "simulated") + " objetos simulados  ·  " + I(summary, "active") + " em movimento  ·  " + I(summary, "frozen") + " congelados" + (server.ValueKind == JsonValueKind.Object ? "  ·  corpos no servidor " + I(server, "bodies") + (I(server, "maxBodies") > 0 ? "/" + I(server, "maxBodies") : "") + " (+" + I(server, "fixedBodies") + " fixos)  ·  " + D(server, "physicsMs").ToString("0.##", Pt) + " ms por quadro" : ""), "lv-bold");
            _page.Add(UiKit.Row(Btn("Todos", () => Physics(1, "")), Btn("Em movimento", () => Physics(1, "active")), Btn("Congelados", () => Physics(1, "frozen")), UiKit.Spacer(), Sure("Congelar todos", () => "Confirmar", () => Send("POST", Base + "/physics/freeze", new { objectIds = new Guid[0], all = true }, "Física congelada.", () => Physics(page, state))), Sure("Soltar todos", () => "Confirmar", () => Send("POST", Base + "/physics/release", new { objectIds = new Guid[0], all = true }, "Física solta.", () => Physics(page, state)))));
            foreach (var x in Arr(root, "items"))
            {
                var id = G(x, "id"); var frozen = S(x, "state") == "frozen";
                Row(id, UiKit.Text(S(x, "name"), "lv-bold"), UiKit.Text(S(x, "ownerName") + " · " + I(x, "cost") + " prims · " + (frozen ? "congelado" : "em movimento"), "lv-small", "lv-muted"), UiKit.Spacer(), Btn(frozen ? "Soltar" : "Congelar", () => Send("POST", Base + "/physics/" + (frozen ? "release" : "freeze"), new { objectIds = new[] { id }, all = false }, frozen ? "Solto." : "Congelado.", () => Physics(page, state))));
            }
            if (Arr(root, "items").Count == 0) Line("Nenhum objeto simulado.", "lv-small", "lv-muted"); Pager(root, p => Physics(p, state));
        });

        // ---- Desempenho ----

        private void Performance(int hours = 6) => Get(Base + "/performance?hours=" + hours, root =>
        {
            Reset(); var current = Obj(root, "current"); var scripts = Obj(root, "scripts");
            _page.Add(UiKit.Row(Btn("1 hora", () => Performance(1)), Btn("6 horas", () => Performance(6)), Btn("24 horas", () => Performance(24))));
            if (!Flag(root, "running")) Line("A ilha está desligada: sem medidas de agora.", "lv-bold");
            else if (current.ValueKind == JsonValueKind.Object) Line(D(current, "fps").ToString("0", Pt) + " de " + D(current, "tickRate").ToString("0", Pt) + " quadros/s  ·  " + D(current, "processMs").ToString("0.#", Pt) + " ms por quadro  ·  " + D(current, "memoryMb").ToString("0", Pt) + " MB  ·  " + I(current, "players") + "/" + I(current, "maxPlayers") + " pessoas", "lv-bold");
            Line(I(scripts, "active") + " de " + I(scripts, "total") + " scripts ativos", "lv-small");
            var summary = Obj(root, "summary"); var series = Arr(root, "series");
            if (series.Count == 0) Line("Sem medidas neste período.", "lv-small", "lv-muted");
            else foreach (var key in new[] { ("fps", "quadros/s"), ("processMs", "ms por quadro"), ("memoryMb", "MB"), ("players", "pessoas") }) { var s = Obj(summary, key.Item1); if (s.ValueKind == JsonValueKind.Object) Line(key.Item2 + ": mín " + D(s, "min").ToString("0.#", Pt) + " · média " + D(s, "avg").ToString("0.#", Pt) + " · máx " + D(s, "max").ToString("0.#", Pt) + " (" + series.Count + " medidas)", "lv-small"); }
            Title("Scripts que mais pesam");
            var diag = Obj(root, "diagnostics"); foreach (var s in Arr(diag, "scripts").Take(20)) Line(S(s, "name") + " · " + S(s, "script") + " · " + I(s, "runs") + " execuções · " + I(s, "instructions").ToString("N0", Pt) + " instruções" + (I(s, "errors") > 0 ? " · " + I(s, "errors") + " erro(s)" : ""), "lv-small");
            if (Arr(diag, "scripts").Count == 0) Line("Nenhum script rodou neste período.", "lv-small", "lv-muted");
        });

        // ---- Ambiente ----

        public static readonly (string Id, string Text)[] Weathers = { ("clear", "Limpo"), ("cloudy", "Nublado"), ("fog", "Neblina"), ("rain", "Chuva"), ("storm", "Tempestade") };

        /// <summary>The look of the island as the API keeps it (hour 0–24, weather, fog and light 0–1, sky colour). Null and a reason when something is out of range.</summary>
        public static string LookJson(string hour, string weather, string fog, string light, string sky, out string problem)
        {
            problem = null; float F(string t) => float.TryParse((t ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : float.NaN;
            var h = F(hour); var f = F(fog); var l = F(light);
            if (float.IsNaN(h) || h < 0 || h > 24) { problem = "Hora: de 0 a 24."; return null; } if (float.IsNaN(f) || f < 0 || f > 1) { problem = "Neblina: de 0 a 1."; return null; } if (float.IsNaN(l) || l < 0 || l > 1) { problem = "Luz: de 0 a 1."; return null; }
            if (!Weathers.Any(w => w.Id == weather)) { problem = "Escolha o clima."; return null; }
            var node = new JsonObject { ["hour"] = Math.Round(h, 2), ["weather"] = weather, ["fog"] = Math.Round(f, 2), ["light"] = Math.Round(l, 2) };
            if (!string.IsNullOrWhiteSpace(sky)) { var c = sky.Trim(); if (!System.Text.RegularExpressions.Regex.IsMatch(c, "^#[0-9A-Fa-f]{6}$")) { problem = "Cor do céu: #RRGGBB."; return null; } node["sky"] = c.ToUpperInvariant(); }
            return node.ToJsonString();
        }

        private void Environment() => Get(Base + "/environment", root =>
        {
            Reset(); var look = Obj(root, "look"); _lookAt = Date(root, "updatedAt") ?? default;
            Title("A aparência da ilha inteira"); Line("Vale onde um terreno não define a sua própria.", "lv-small", "lv-muted");
            var hour = Field("Hora (0 a 24)", D(look, "hour").ToString("0.##", CultureInfo.InvariantCulture), 5); var weather = Choice("Clima", Weathers.ToList(), S(look, "weather").Length > 0 ? S(look, "weather") : "clear");
            var fog = Field("Neblina (0 a 1)", D(look, "fog").ToString("0.##", CultureInfo.InvariantCulture), 5); var light = Field("Luz (0 a 1)", (Has(look, "light") ? D(look, "light") : 1).ToString("0.##", CultureInfo.InvariantCulture), 5); var sky = Field("Cor do céu (#RRGGBB, opcional)", S(look, "sky"), 7);
            string Current(out string problem) => LookJson(hour.value, IdOf(weather), fog.value, light.value, sky.value, out problem);
            _page.Add(Btn("Salvar a aparência", () => { var json = Current(out var p); if (json == null) { Say(p, true); return; } Send("PUT", Base + "/environment", "{\"look\":" + json + ",\"expectedUpdatedAt\":\"" + _lookAt.ToString("O") + "\"}", "Aparência salva.", Environment); }, "lv-btn-primary"));
            Title("Predefinições"); var presetName = Field("Nome da predefinição", "", 40);
            _page.Add(Btn("Guardar a aparência como predefinição", () => { var json = Current(out var p); if (json == null) { Say(p, true); return; } if (presetName.value.Trim().Length == 0) { Say("Dê um nome à predefinição.", true); return; } Send("POST", Base + "/environment/presets", "{\"name\":" + JsonSerializer.Serialize(presetName.value.Trim()) + ",\"look\":" + json + "}", "Predefinição guardada.", Environment); }));
            void Preset(JsonElement p, bool custom)
            {
                var name = custom ? S(p, "name") : S(p, "title"); var pl = Obj(p, "look"); var cells = new List<VisualElement> { UiKit.Text(name, "lv-bold"), UiKit.Text(Weathers.FirstOrDefault(w => w.Id == S(pl, "weather")).Text + " · " + D(pl, "hour").ToString("0.#", Pt) + " h", "lv-small", "lv-muted"), UiKit.Spacer() };
                cells.Add(Btn("Usar", () => { hour.value = D(pl, "hour").ToString("0.##", CultureInfo.InvariantCulture); var wi = Array.FindIndex(Weathers, w => w.Id == S(pl, "weather")); if (wi >= 0) weather.index = wi; fog.value = D(pl, "fog").ToString("0.##", CultureInfo.InvariantCulture); light.value = (Has(pl, "light") ? D(pl, "light") : 1).ToString("0.##", CultureInfo.InvariantCulture); sky.value = S(pl, "sky"); Say("Predefinição carregada: salve para aplicar.", false); }));
                if (custom) cells.Add(Sure("Apagar", () => "Confirmar", () => Send("DELETE", Base + "/environment/presets/" + Uri.EscapeDataString(name), null, "Predefinição apagada.", Environment)));
                Row(Guid.Empty, cells.ToArray());
            }
            foreach (var p in Arr(root, "builtIn")) Preset(p, false); foreach (var p in Arr(root, "custom")) Preset(p, true);
        });

        // ---- Mídia ----

        /// <summary>Whether a music address may be saved: https, an .mp3 or .ogg file, at most 512 characters ("" takes the music away).</summary>
        public static string MusicProblem(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null; var u = url.Trim(); if (u.Length > 512) return "O endereço passa de 512 caracteres.";
            if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Scheme != "https") return "Use um endereço https.";
            var ext = System.IO.Path.GetExtension(uri.AbsolutePath).ToLowerInvariant(); return ext == ".mp3" || ext == ".ogg" ? null : "O arquivo precisa ser .mp3 ou .ogg.";
        }

        private void Media() => Get(Base + "/media", root =>
        {
            Reset(); var music = Obj(root, "music"); _mediaAt = Date(root, "updatedAt") ?? default;
            Title("Música da ilha inteira"); Line("Toca onde um terreno não toca a sua. Arquivos .mp3 ou .ogg por https, até 12 MB.", "lv-small", "lv-muted");
            var url = Field("Endereço (https)", S(music, "url"), 512); var name = Field("Nome", S(music, "name"), 80); var volume = Field("Volume (0 a 1)", (Has(music, "volume") ? D(music, "volume") : 1).ToString("0.##", CultureInfo.InvariantCulture), 4); var auto = Check("Começa sozinha", music.ValueKind != JsonValueKind.Object || !music.TryGetProperty("autoplay", out var ap) || ap.ValueKind != JsonValueKind.False);
            _page.Add(Btn("Salvar a música", () =>
            {
                var problem = MusicProblem(url.value); if (problem != null) { Say(problem, true); return; }
                if (!float.TryParse(volume.value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0 || v > 1) { Say("Volume: de 0 a 1.", true); return; }
                Send("PUT", Base + "/media", new { musicUrl = url.value.Trim().Length == 0 ? null : url.value.Trim(), volume = v, autoplay = auto.value, name = name.value.Trim(), expectedUpdatedAt = _mediaAt }, url.value.Trim().Length == 0 ? "Música retirada." : "Música salva.", Media);
            }, "lv-btn-primary"));
        });

        // ---- Terreno ----

        private void Terrain() => Get(Base + "/terrain", root =>
        {
            Reset(); var current = Obj(root, "current"); var stats = Obj(current, "stats");
            if (current.ValueKind == JsonValueKind.Object) Line("Versão " + I(current, "version") + " salva " + When(Date(current, "savedAt")) + "  ·  " + D(stats, "sizeMeters").ToString("0", Pt) + " m  ·  altura de " + D(stats, "minHeight").ToString("0.#", Pt) + " a " + D(stats, "maxHeight").ToString("0.#", Pt) + " m (média " + D(stats, "avgHeight").ToString("0.#", Pt) + ")  ·  mar a " + D(stats, "seaLevel").ToString("0.#", Pt) + " m", "lv-bold");
            var restoring = Obj(root, "restoring"); if (restoring.ValueKind == JsonValueKind.Object) Line(S(restoring, "status") == "running" ? "Restauração em andamento: a ilha está reiniciando com o terreno do backup." : "Restauração agendada para " + When(Date(restoring, "scheduledFor")) + ".", "lv-bad");
            var backups = Arr(root, "backups"); var max = I(Obj(root, "limits"), "maxBackups"); if (max <= 0) max = 10;
            Title("Backups (" + backups.Count + " de " + max + ")"); var name = Field("Nome do backup", "", 60);
            var create = Btn("Fazer backup do terreno de agora", () => { if (name.value.Trim().Length == 0) { Say("Dê um nome ao backup.", true); return; } Send("POST", Base + "/terrain/backups", new { name = name.value.Trim() }, "Backup feito.", Terrain); }, "lv-btn-primary"); create.SetEnabled(backups.Count < max); _page.Add(create);
            foreach (var b in backups)
            {
                var id = G(b, "id"); var valid = !b.TryGetProperty("valid", out var vv) || vv.ValueKind == JsonValueKind.True; var cells = new List<VisualElement> { UiKit.Text(S(b, "name"), "lv-bold"), UiKit.Text(When(Date(b, "savedAt")) + (S(b, "by").Length > 0 ? " · " + S(b, "by") : "") + (valid ? "" : " · danificado"), "lv-small", "lv-muted"), UiKit.Spacer() };
                if (valid) { cells.Add(Sure("Restaurar agora", () => "A ilha reinicia. Confirmar?", () => Send("POST", Base + "/terrain/backups/" + id + "/restore", new { mode = "now", minutes = (int?)null }, "Restauração iniciada.", Terrain))); cells.Add(Sure("Em 5 min", () => "Confirmar", () => Send("POST", Base + "/terrain/backups/" + id + "/restore", new { mode = "in", minutes = 5 }, "Restauração agendada.", Terrain))); }
                cells.Add(Sure("Apagar", () => "Confirmar", () => Send("DELETE", Base + "/terrain/backups/" + id, null, "Backup apagado.", Terrain))); Row(id, cells.ToArray());
            }
            if (backups.Count == 0) Line("Nenhum backup ainda.", "lv-small", "lv-muted");
        });

        // ---- Reinicialização ----

        public static string RestartBody(string mode, int? minutes, DateTimeOffset? at, string reason) => JsonSerializer.Serialize(new { mode, minutes, at, reason = (reason ?? "").Trim() }, Options);

        private void Restart() => Get(Base + "/restart", root =>
        {
            Reset(); var running = Flag(root, "running"); var active = Obj(root, "active");
            Line("Servidor: " + StateWords(S(root, "state")) + (running ? "  ·  no ar há " + Duration(I(root, "uptimeSeconds")) : ""), "lv-bold");
            if (active.ValueKind == JsonValueKind.Object)
            {
                var id = G(active, "id"); var scheduled = S(active, "status") == "scheduled";
                Line((scheduled ? "Agendada para " + When(Date(active, "scheduledFor")) + " (faltam " + Duration(I(active, "remainingSeconds")) + ")" : "O servidor está salvando e a ilha vai voltar sozinha") + " · pedida por " + S(active, "requestedBy") + (S(active, "reason").Length > 0 ? " · " + S(active, "reason") : ""), "lv-bad");
                if (scheduled) _page.Add(Sure("Cancelar a reinicialização", () => "Confirmar", () => Send("DELETE", Base + "/restart/" + id, null, "Reinicialização cancelada.", Restart)));
            }
            else
            {
                Title("Reiniciar"); var reason = Field("Motivo (opcional)", "", 200); var at = Field("Num horário (dd/mm/aaaa hh:mm, opcional)", "", 16);
                _page.Add(UiKit.Row(Sure("Agora", () => "A ilha reinicia agora. Confirmar?", () => Send("POST", Base + "/restart", RestartBody("now", null, null, reason.value), "Reinicialização pedida.", Restart)), Btn("Em 1 min", () => Send("POST", Base + "/restart", RestartBody("in", 1, null, reason.value), "Reinicialização agendada.", Restart)), Btn("Em 5 min", () => Send("POST", Base + "/restart", RestartBody("in", 5, null, reason.value), "Reinicialização agendada.", Restart)), Btn("Em 10 min", () => Send("POST", Base + "/restart", RestartBody("in", 10, null, reason.value), "Reinicialização agendada.", Restart)),
                    Btn("No horário", () => { if (!DateTime.TryParseExact(at.value.Trim(), "dd/MM/yyyy HH:mm", Pt, DateTimeStyles.AssumeLocal, out var when)) { Say("Horário: dd/mm/aaaa hh:mm.", true); return; } Send("POST", Base + "/restart", RestartBody("at", null, new DateTimeOffset(when), reason.value), "Reinicialização agendada.", Restart); })));
            }
            Title("Últimas reinicializações");
            foreach (var h in Arr(root, "history")) { var s = S(h, "status"); Line(When(Date(h, "createdAt")) + " · " + S(h, "requestedBy") + " · " + (s == "completed" ? "concluída" : s == "cancelled" ? "cancelada" : "falhou" + (S(h, "result").Length > 0 ? ": " + S(h, "result") : "")) + (h.TryGetProperty("durationSeconds", out var d) && d.ValueKind == JsonValueKind.Number ? " · " + Duration(d.GetInt64()) : "") + (S(h, "reason").Length > 0 ? " · " + S(h, "reason") : ""), "lv-small"); }
            if (Arr(root, "history").Count == 0) Line("Nenhuma ainda.", "lv-small", "lv-muted");
        });

        // ---- Histórico e auditoria ----

        public static readonly (string Id, string Text)[] AuditCategories = { ("", "Tudo"), ("geral", "Geral"), ("acesso", "Acesso"), ("banimento", "Banimentos"), ("permissao", "Permissões"), ("objeto", "Objetos"), ("servidor", "Servidor"), ("ambiente", "Ambiente"), ("midia", "Mídia"), ("fisica", "Física"), ("terreno", "Terreno"), ("construcao", "Construção") };

        private void Audit(int page = 1, string category = "", int hours = 168) => Get(Base + "/audit?page=" + page + "&hours=" + hours + "&category=" + category + "&user=&q=", root =>
        {
            Reset(); var bar = new VisualElement(); bar.style.flexDirection = FlexDirection.Row; bar.style.flexWrap = Wrap.Wrap; foreach (var (id, text) in AuditCategories) { var c = id; bar.Add(Btn(text, () => Audit(1, c, hours), c == category ? "lv-btn-primary" : "lv-btn-flat")); } _page.Add(bar);
            _page.Add(UiKit.Row(Btn("24 horas", () => Audit(1, category, 24)), Btn("7 dias", () => Audit(1, category, 168)), Btn("30 dias", () => Audit(1, category, 720))));
            foreach (var a in Arr(root, "items")) { var id = G(a, "id"); Row(id, UiKit.Text(When(Date(a, "at")), "lv-small", "lv-muted"), UiKit.Text(S(a, "actorName"), "lv-bold"), UiKit.Text(S(a, "summary").Length > 0 ? S(a, "summary") : S(a, "action"), "lv-small"), UiKit.Spacer(), Btn("Detalhes", () => AuditDetail(id))); }
            if (Arr(root, "items").Count == 0) Line("Nada neste período.", "lv-small", "lv-muted"); Pager(root, p => Audit(p, category, hours));
        });

        private void AuditDetail(Guid id) => Get(Base + "/audit/" + id, a =>
        {
            Reset(); _page.Add(Btn("Voltar", () => Audit())); Title(S(a, "summary"));
            Line(When(Date(a, "at")) + " · " + S(a, "actorName") + " · " + S(a, "category") + " · " + S(a, "action") + (S(a, "resource").Length > 0 ? " · " + S(a, "resource") : ""), "lv-small");
            if (a.TryGetProperty("before", out var before) && before.ValueKind != JsonValueKind.Null) { Title("Antes"); Line(before.ToString(), "lv-small", "lv-muted"); }
            if (a.TryGetProperty("after", out var after) && after.ValueKind != JsonValueKind.Null) { Title("Depois"); Line(after.ToString(), "lv-small", "lv-muted"); }
        });

        // ---- Regras (dos terrenos) ----

        private void Rules()
        {
            Title("Regras padrão dos terrenos"); Line("Valem para os terrenos novos; marque a última opção para aplicar também aos que já existem.", "lv-small", "lv-muted");
            var access = Choice("Quem entra", new List<(string, string)> { ("public", "Todos"), ("friends", "Amigos do dono"), ("private", "Só o dono e liberados") }, "public");
            var build = Choice("Quem constrói", new List<(string, string)> { ("owner", "Só o dono"), ("group", "O grupo do terreno"), ("everyone", "Todos") }, "owner");
            var scripts = Check("Permitir scripts", true); var voice = Check("Permitir voz", true); var media = Check("Permitir mídia", true); var minutes = Field("Devolver objetos soltos depois de (minutos, vazio = nunca)", "", 5); var existing = Check("Aplicar também aos terrenos que já existem", false);
            _page.Add(Btn("Salvar as regras", () =>
            {
                int? m = null; if (minutes.value.Trim().Length > 0) { if (!int.TryParse(minutes.value, out var n) || n < 1) { Say("Minutos: um número maior que zero (ou deixe vazio).", true); return; } m = n; }
                Send("PUT", "/api/v1/worlds/islands/" + _island + "/land/rules", new { accessMode = IdOf(access), buildMode = IdOf(build), allowScripts = scripts.value, allowVoice = voice.value, allowMedia = media.value, autoReturnMinutes = m, applyToExisting = existing.value }, "Regras salvas.");
            }, "lv-btn-primary"));
        }

        // ---- Propriedade ----

        private void Property()
        {
            Title("Vender ou alugar a ilha inteira"); Line("O anúncio aparece no Mercado imobiliário. Para os terrenos, use a seção Terrenos ou Minhas propriedades.", "lv-small", "lv-muted");
            var name = S(_general, "name"); _page.Add(UiKit.Row(Btn("Anunciar a venda…", () => new ListingOfferWindow(_c, false, true, _island, Guid.Empty, name, null).Open(), "lv-btn-primary"), Btn("Anunciar o aluguel…", () => new ListingOfferWindow(_c, true, true, _island, Guid.Empty, name, null).Open())));
            var estate = "/api/v1/worlds/islands/" + _island + "/real-estate";
            _page.Add(UiKit.Row(Sure("Tirar da venda", () => "Confirmar", () => Send("DELETE", estate + "/sale", null, "Anúncio de venda retirado.")), Sure("Tirar do aluguel", () => "Confirmar", () => Send("DELETE", estate + "/rental-offer", null, "Anúncio de aluguel retirado."))));
        }
    }
}
