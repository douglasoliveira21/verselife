using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using LifeVerse.Shared.Land;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// "Sobre este Terreno", as in the Godot client: one parcel, for whoever is on it (from Mais › Sobre este terreno, or from the lists of the land management). A header with the name, code, island, area and owner,
    /// and eight sections: Geral (name, description, purpose, picture, arrival point), Propriedade (buy, rent, renew, end, offer, transfer), Acesso (who enters, the list of people and the groups), Opções (who builds,
    /// scripts, voice, media, returning objects), Objetos, Mídia, Histórico and Auditoria. It reads <c>/land/parcels/{id}/about</c>; what a person may change is decided by the API (<c>me</c>, <c>rights</c>).
    /// </summary>
    public sealed class ParcelInfoWindow : SectionWindow
    {
        public static readonly (string Id, string Text)[] Sections =
        {
            ("geral", "Geral"), ("propriedade", "Propriedade"), ("acesso", "Acesso"), ("opcoes", "Opções"), ("objetos", "Objetos"), ("midia", "Mídia"), ("historico", "Histórico"), ("auditoria", "Auditoria"),
        };
        public static readonly (string Id, string Text)[] AccessModes = { ("public", "Todos entram"), ("list", "Só quem está na lista"), ("private", "Só o dono") };
        public static readonly (string Id, string Text)[] EntryKinds = { ("allow", "Pode entrar"), ("build", "Pode entrar e construir"), ("ban", "Proibido de entrar") };
        public static readonly (string Id, string Text)[] BuildModes = { ("owner", "Só o dono"), ("list", "O dono e a lista"), ("everyone", "Todos"), ("nobody", "Ninguém") };
        public static readonly (string Id, string Text)[] ArrivalModes = { ("free", "Livre (chega onde quiser)"), ("forced", "Sempre no ponto de chegada"), ("disabled", "Teleporte desligado") };
        private readonly Guid _island, _parcel; private readonly Func<Vector3?> _where; private JsonElement _about;
        public Guid Parcel => _parcel;
        public JsonElement About => _about;

        private string Land => "/api/v1/worlds/islands/" + _island + "/land";
        private string P => Land + "/parcels/" + _parcel;

        public ParcelInfoWindow(ApiContext context, Guid island, Guid parcel, Func<Vector3?> where = null) : base(context, "parcel-info", "Sobre este Terreno", Sections, 760f, 540f, new Vector2(260, 70), 150f)
        {
            _island = island; _parcel = parcel; _where = where; Show("geral");
        }

        protected override void Fill(string section)
        {
            switch (section)
            {
                case "geral": WithAbout(General); break; case "propriedade": WithAbout(Property); break; case "acesso": Access(); break; case "opcoes": WithAbout(Options); break;
                case "objetos": Objects(); break; case "midia": WithAbout(Media); break; case "historico": WithAbout(History); break; case "auditoria": Audit(); break;
            }
        }

        private void WithAbout(Action<JsonElement> fill) => Get(P + "/about", root =>
        {
            _about = root.Clone(); var p = Obj(root, "parcel"); var owner = Obj(root, "owner"); var me = Obj(root, "me");
            _summary.text = S(p, "code") + " · " + S(p, "name") + "  ·  " + S(Obj(root, "island"), "name") + "  ·  " + D(p, "area").ToString("N0", Pt) + " m²  ·  " + LandManagementWindow.CategoryWords(S(p, "category")) + "  ·  " + (S(owner, "kind") == "group" ? "do grupo " : "de ") + S(owner, "name") + (Flag(me, "isTenant") ? "  ·  você aluga" : Flag(me, "isOwner") ? "  ·  é seu" : "");
            fill(root);
        });

        private bool CanManage => Flag(Obj(_about, "me"), "canManage") || Flag(Obj(_about, "me"), "isStaff");

        /// <summary>The parcel under a spot of the island (meters from its centre), from the land list (<c>GET /land</c>); empty when the spot is no parcel's.</summary>
        public static Guid ParcelAt(string landJson, float x, float z)
        {
            try
            {
                using var d = JsonDocument.Parse(landJson ?? ""); var key = LandMath.Key(LandMath.CellOf(x), LandMath.CellOf(z));
                foreach (var item in Arr(d.RootElement, "parcels"))
                {
                    var runs = CellGrid.RunsOf(item); if (runs == null) continue;
                    if (LandMath.FromRuns(runs).Contains(key)) return G(item, "id");
                }
            }
            catch (Exception e) when (e is JsonException || e is InvalidOperationException || e is FormatException) { }
            return Guid.Empty;
        }

        // ---- Geral ----

        private void General(JsonElement root)
        {
            var p = Obj(root, "parcel"); var usage = Obj(root, "usage");
            Line((S(p, "description").Length > 0 ? S(p, "description") : "Sem descrição.") + "  ·  criado em " + When(Date(p, "createdAt")) + (Has(root, "acquiredAt") ? "  ·  adquirido em " + When(Date(root, "acquiredAt")) : ""), "lv-small", "lv-muted");
            Line(I(usage, "objects") + "/" + I(usage, "maxObjects") + " objetos · " + I(usage, "cost") + "/" + I(usage, "maxCost") + " prims" + (I(usage, "others") > 0 ? " · " + I(usage, "others") + " de outras pessoas" : ""), "lv-small");
            if (!CanManage) { Line("Só o dono do terreno e a equipe da ilha mudam estes dados.", "lv-small", "lv-muted"); return; }
            Title("Nome e descrição"); var name = Field("Nome", S(p, "name"), 60); var about = Field("Descrição", S(p, "description"), 400);
            Buttons(Btn("Salvar nome e descrição", () => { if (name.value.Trim().Length < 2) { Say("O nome precisa de pelo menos 2 letras.", true); return; } Send("PATCH", "/api/v1/worlds/islands/" + _island + "/parcels/" + _parcel, new { name = name.value.Trim(), description = about.value, expectedRevision = I(p, "revision") }, "Terreno salvo.", () => Show("geral"), LandManagementWindow.Words); }, "lv-btn-primary"));
            Title("Finalidade"); var category = Choice("Finalidade", LandManagementWindow.Categories.ToList(), S(p, "category"));
            Buttons(Btn("Salvar a finalidade", () => Send("PUT", P + "/category", new { category = IdOf(category) }, "Finalidade salva.", () => Show("geral"), LandManagementWindow.Words)));
            Title("Imagem"); var image = Field("Imagem (identificador de uma imagem sua; vazio tira)", S(p, "imageUrl"), 200);
            Buttons(Btn("Salvar a imagem", () => Send("PUT", P + "/image", new { image = image.value.Trim() }, image.value.Trim().Length == 0 ? "Imagem retirada." : "Imagem salva.", () => Show("geral"), LandManagementWindow.Words)));
            Title("Chegada"); var mode = Choice("Teleporte para o terreno", ArrivalModes.ToList(), S(p, "teleportMode").Length > 0 ? S(p, "teleportMode") : "free");
            var x = Field("Ponto de chegada X (vazio = o centro)", Has(p, "spawnX") ? D(p, "spawnX").ToString("0.#", CultureInfo.InvariantCulture) : "", 10); var z = Field("Ponto de chegada Z", Has(p, "spawnZ") ? D(p, "spawnZ").ToString("0.#", CultureInfo.InvariantCulture) : "", 10); var yaw = Field("Virado para (graus)", D(p, "yaw").ToString("0", CultureInfo.InvariantCulture), 6);
            Buttons(Btn("Usar onde estou", () => { var at = _where?.Invoke(); if (at == null) { Say("Não sei onde você está agora.", true); return; } x.value = at.Value.x.ToString("0.#", CultureInfo.InvariantCulture); z.value = at.Value.z.ToString("0.#", CultureInfo.InvariantCulture); }),
                Btn("Salvar a chegada", () => { var problem = ArrivalProblem(x.value, z.value, yaw.value, out var ax, out var az, out var ay); if (problem != null) { Say(problem, true); return; } Send("PUT", P + "/arrival", new { x = ax, z = az, yaw = ay, mode = IdOf(mode) }, "Chegada salva.", () => Show("geral"), LandManagementWindow.Words); }, "lv-btn-primary"));
        }

        /// <summary>Checks the arrival fields: X and Z both empty (the centre) or both numbers; the heading 0 to 360.</summary>
        public static string ArrivalProblem(string x, string z, string yaw, out double? ax, out double? az, out double ay)
        {
            ax = az = null; ay = 0; bool Num(string t, out double v) => double.TryParse((t ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            var noX = string.IsNullOrWhiteSpace(x); var noZ = string.IsNullOrWhiteSpace(z);
            if (noX != noZ) return "Preencha X e Z, ou deixe os dois vazios.";
            if (!noX) { if (!Num(x, out var vx) || !Num(z, out var vz)) return "X e Z precisam ser números."; ax = vx; az = vz; }
            if (string.IsNullOrWhiteSpace(yaw)) return null; if (!Num(yaw, out ay) || ay < 0 || ay > 360) return "Direção: de 0 a 360 graus."; return null;
        }

        // ---- Propriedade ----

        private void Property(JsonElement root)
        {
            var rights = Obj(root, "rights"); var me = Obj(root, "me"); var sale = Obj(root, "sale"); var offer = Obj(root, "rentOffer"); var rental = Obj(root, "rental"); var transfer = Obj(root, "transfer"); var name = S(Obj(root, "parcel"), "name");
            if (sale.ValueKind == JsonValueKind.Object)
            {
                Title("À venda"); var price = I(sale, "price"); Line(Coins(price) + (Flag(sale, "exclusive") ? " · reservado para uma pessoa" : "") + (Flag(sale, "includeObjects") ? " · com os objetos" : " · sem os objetos") + (S(sale, "description").Length > 0 ? " · " + S(sale, "description") : ""));
                if (!Flag(me, "isOwner")) Buttons(Sure("Comprar por " + Coins(price), () => "Comprar " + name + " por " + Coins(price) + "?", () => Send("POST", P + "/buy", new { expectedPrice = price, idempotencyKey = Guid.NewGuid().ToString() }, "Terreno comprado.", () => Show("propriedade"), LandManagementWindow.Words), "lv-btn-primary"));
                if (Flag(rights, "canResell")) Buttons(Sure("Tirar da venda", () => "Confirmar", () => Send("DELETE", P + "/sale", null, "Anúncio de venda retirado.", () => Show("propriedade"), LandManagementWindow.Words)));
            }
            if (offer.ValueKind == JsonValueKind.Object)
            {
                Title("Para alugar"); var price = I(offer, "price"); Line(Coins(price) + " a cada " + I(offer, "periodDays") + " dias · de " + I(offer, "minPeriods") + " a " + I(offer, "maxPeriods") + " períodos" + (I(offer, "deposit") > 0 ? " · caução " + Coins(I(offer, "deposit")) : "") + " · tolerância de " + I(offer, "graceDays") + " dias");
                if (!Flag(me, "isOwner"))
                {
                    var periods = Field("Períodos", I(offer, "minPeriods").ToString(), 3); var auto = Check("Renovar sozinho", false); auto.SetEnabled(Flag(offer, "autoRenewAllowed"));
                    Buttons(Sure("Alugar", () => "Alugar " + name + " por " + periods.value + " período(s) de " + Coins(price) + "?", () => { if (!int.TryParse(periods.value, out var n) || n < 1) { Say("Períodos: um número a partir de 1.", true); return; } Send("POST", P + "/rent", new { periods = n, autoRenew = auto.value, expectedPrice = price, idempotencyKey = Guid.NewGuid().ToString() }, "Terreno alugado.", () => Show("propriedade"), LandManagementWindow.Words); }, "lv-btn-primary"));
                }
                if (Flag(rights, "canLease")) Buttons(Sure("Tirar do aluguel", () => "Confirmar", () => Send("DELETE", P + "/rental-offer", null, "Anúncio de aluguel retirado.", () => Show("propriedade"), LandManagementWindow.Words)));
            }
            if (rental.ValueKind == JsonValueKind.Object)
            {
                var id = G(rental, "id"); Title("Aluguel em vigor"); Line(S(rental, "landlord") + " → " + S(rental, "tenant") + " · " + Coins(I(rental, "price")) + " a cada " + I(rental, "periodDays") + " dias · " + LandManagementWindow.ContractWords(S(rental, "status")) + " · até " + When(Date(rental, "expiresAt")) + (Flag(rental, "autoRenew") ? " · renova sozinho" : ""));
                if (Flag(me, "isTenant")) { var more = Field("Renovar por quantos períodos", "1", 3); Buttons(Btn("Renovar", () => { if (!int.TryParse(more.value, out var n) || n < 1) { Say("Períodos: um número a partir de 1.", true); return; } Send("POST", Land + "/rentals/" + id + "/renew", new { periods = n, idempotencyKey = Guid.NewGuid().ToString() }, "Aluguel renovado.", () => Show("propriedade"), LandManagementWindow.Words); }, "lv-btn-primary")); }
                Buttons(Sure("Encerrar o aluguel", () => "Encerrar o aluguel?", () => Send("POST", Land + "/rentals/" + id + "/end", "{}", "Aluguel encerrado.", () => Show("propriedade"), LandManagementWindow.Words)));
            }
            if (transfer.ValueKind == JsonValueKind.Object)
            {
                Title("Transferência"); Line("Para " + S(transfer, "to") + ", até " + When(Date(transfer, "expiresAt")) + ".");
                if (Flag(transfer, "forMe")) Buttons(Btn("Aceitar o terreno", () => Send("POST", P + "/transfer/accept", "{}", "Terreno recebido.", () => Show("propriedade"), LandManagementWindow.Words), "lv-btn-primary"), Btn("Recusar", () => Send("POST", P + "/transfer/decline", "{}", "Transferência recusada.", () => Show("propriedade"), LandManagementWindow.Words)));
                else Buttons(Sure("Cancelar a transferência", () => "Confirmar", () => Send("DELETE", P + "/transfer", null, "Transferência cancelada.", () => Show("propriedade"), LandManagementWindow.Words)));
            }
            if (Flag(rights, "canResell") && sale.ValueKind != JsonValueKind.Object || Flag(rights, "canLease") && offer.ValueKind != JsonValueKind.Object)
            {
                Title("Anunciar"); var buttons = new List<VisualElement>();
                if (Flag(rights, "canResell") && sale.ValueKind != JsonValueKind.Object) buttons.Add(Btn("Anunciar a venda…", () => new ListingOfferWindow(_c, false, false, _island, _parcel, name, () => Show("propriedade")).Open()));
                if (Flag(rights, "canLease") && offer.ValueKind != JsonValueKind.Object) buttons.Add(Btn("Anunciar o aluguel…", () => new ListingOfferWindow(_c, true, false, _island, _parcel, name, () => Show("propriedade")).Open()));
                Buttons(buttons.ToArray());
            }
            if (Flag(rights, "canTransfer"))
            {
                Title("Dar o terreno a outra pessoa"); Line("A pessoa tem um prazo para aceitar; até lá você pode cancelar.", "lv-small", "lv-muted"); var who = Field("Nome da pessoa", "", 64);
                Buttons(Sure("Transferir", () => "Transferir " + name + " para " + who.value.Trim() + "?", () => { if (who.value.Trim().Length == 0) { Say("Digite o nome da pessoa.", true); return; } Send("POST", P + "/transfer", new { name = who.value.Trim() }, "Transferência enviada.", () => Show("propriedade"), LandManagementWindow.Words); }));
            }
            if (_page.childCount == 0) Line("Nada à venda nem para alugar aqui.", "lv-small", "lv-muted");
        }

        // ---- Acesso ----

        private void Access() => Get(P + "/access", root =>
        {
            Title("Quem entra"); var mode = Choice("Modo", AccessModes.ToList(), S(root, "mode"));
            Buttons(Btn("Salvar o modo", () => Send("PUT", P + "/access-mode", new { mode = IdOf(mode) }, "Modo de entrada salvo.", () => Show("acesso"), LandManagementWindow.Words), "lv-btn-primary"));
            Title("Pessoas"); var entries = Arr(root, "entries");
            foreach (var e in entries)
            {
                var id = G(e, "id"); var who = S(e, "userName");
                Row(id, UiKit.Text(who, "lv-bold"), UiKit.Text(EntryKinds.FirstOrDefault(k => k.Id == S(e, "kind")).Text + (Date(e, "expiresAt") is { } until ? " · até " + When(until) : "") + (S(e, "reason").Length > 0 ? " · " + S(e, "reason") : ""), "lv-small", "lv-muted"), UiKit.Spacer(), Sure("Tirar", () => "Tirar " + who + " da lista?", () => Send("DELETE", P + "/access/" + id, null, "Retirado da lista.", () => Show("acesso"), LandManagementWindow.Words)));
            }
            if (entries.Count == 0) Line("Ninguém na lista.", "lv-small", "lv-muted");
            var name = Field("Nome da pessoa", "", 64); var kind = Choice("Tipo", EntryKinds.ToList(), "allow"); var minutes = Field("Por quantos minutos (vazio = sempre)", "", 6);
            Buttons(Btn("Pôr na lista", () => AddPerson(name.value, IdOf(kind), minutes.value), "lv-btn-primary"));
            var area = new VisualElement(); _page.Add(area); GroupAccess(area, P, () => Show("acesso"));
        });

        /// <summary>Puts a person on the list of the parcel: the name is found among the platform's people first (the API takes an id).</summary>
        public void AddPerson(string name, string kind, string minutes)
        {
            int? m = null; if (!string.IsNullOrWhiteSpace(minutes)) { if (!int.TryParse(minutes.Trim(), out var n) || n < 1) { Say("Minutos: um número a partir de 1, ou vazio.", true); return; } m = n; }
            var wanted = (name ?? "").Trim(); if (wanted.Length == 0) { Say("Digite o nome da pessoa.", true); return; }
            Get("/api/v1/social/people?q=" + Uri.EscapeDataString(wanted), people =>
            {
                var match = Items(people).FirstOrDefault(p => string.Equals(S(p, "username"), wanted, StringComparison.OrdinalIgnoreCase) || string.Equals(S(p, "displayName"), wanted, StringComparison.OrdinalIgnoreCase));
                if (match.ValueKind != JsonValueKind.Object) { Say("Não encontrei ninguém chamado " + wanted + ".", true); return; }
                Send("POST", P + "/access", new { userId = G(match, "id"), kind, minutes = m, reason = "" }, wanted + " está na lista.", () => Show("acesso"), LandManagementWindow.Words);
            });
        }

        // ---- Opções ----

        private void Options(JsonElement root)
        {
            var p = Obj(root, "parcel"); if (!CanManage) { Line("Constrói: " + LandManagementWindow.BuildWords(S(p, "buildMode")) + " · scripts " + (Flag(p, "allowScripts") ? "sim" : "não") + " · voz " + (Flag(p, "allowVoice") ? "sim" : "não") + " · mídia " + (Flag(p, "allowMedia") ? "sim" : "não")); return; }
            var build = Choice("Quem constrói", BuildModes.ToList(), S(p, "buildMode")); var scripts = Check("Permitir scripts", Flag(p, "allowScripts")); var voice = Check("Permitir voz", Flag(p, "allowVoice")); var media = Check("Permitir mídia", Flag(p, "allowMedia"));
            var back = Field("Devolver objetos soltos depois de (minutos, vazio = nunca)", Has(p, "autoReturnMinutes") ? I(p, "autoReturnMinutes").ToString() : "", 5);
            Buttons(Btn("Salvar as opções", () =>
            {
                int? m = null; if (back.value.Trim().Length > 0) { if (!int.TryParse(back.value.Trim(), out var n) || n < 1) { Say("Minutos: um número maior que zero (ou vazio).", true); return; } m = n; }
                Send("PUT", P + "/options", new { buildMode = IdOf(build), allowScripts = scripts.value, autoReturnMinutes = m, allowVoice = voice.value, allowMedia = media.value }, "Opções salvas.", () => Show("opcoes"), LandManagementWindow.Words);
            }, "lv-btn-primary"));
        }

        // ---- Objetos ----

        private void Objects() => Get(P + "/objects", root =>
        {
            var items = Items(root); Line(items.Count + " objeto(s) neste terreno · " + items.Count(o => !Flag(o, "authorized")) + " de quem não tem permissão", "lv-small", "lv-muted");
            foreach (var o in items)
            {
                var id = G(o, "id"); var box = new Toggle(); Toggles["obj:" + id] = box;
                Row(id, box, UiKit.Text(S(o, "name"), "lv-bold"), UiKit.Text(S(o, "ownerName") + " · " + S(o, "primitiveType") + " · " + I(o, "cost") + " prims" + (Flag(o, "hasScripts") ? " · script" : "") + (Flag(o, "authorized") ? "" : " · sem permissão"), "lv-small", Flag(o, "authorized") ? "lv-muted" : "lv-bad"));
            }
            if (items.Count == 0) { Line("Nenhum objeto.", "lv-small", "lv-muted"); return; }
            Buttons(Sure("Devolver os marcados", () => "Devolver os objetos marcados aos donos?", () => { var ids = items.Select(o => G(o, "id")).Where(i => Toggles.TryGetValue("obj:" + i, out var t) && t.value).ToArray(); if (ids.Length == 0) { Say("Marque os objetos.", true); return; } Send("POST", P + "/objects/return", new { scope = "ids", objectIds = ids, reason = "" }, ids.Length + " objeto(s) devolvido(s).", () => Show("objetos"), LandManagementWindow.Words); }),
                Sure("Devolver os de quem não tem permissão", () => "Devolver todos os objetos de quem não tem permissão aqui?", () => Send("POST", P + "/objects/return", new { scope = "others", objectIds = (Guid[])null, reason = "" }, "Objetos devolvidos.", () => Show("objetos"), LandManagementWindow.Words)));
        });

        // ---- Mídia ----

        private void Media(JsonElement root)
        {
            var media = Obj(Obj(root, "parcel"), "media"); Line("Música do terreno: toca para quem está nele (no lugar da música da ilha). Arquivos .mp3 ou .ogg por https.", "lv-small", "lv-muted");
            if (!CanManage) { Line(S(media, "musicUrl").Length > 0 ? "Tocando: " + (S(media, "name").Length > 0 ? S(media, "name") : S(media, "musicUrl")) : "Sem música."); return; }
            var url = Field("Endereço (https; vazio tira a música)", S(media, "musicUrl"), 512); var name = Field("Nome", S(media, "name"), 80); var volume = Field("Volume (0 a 1)", (Has(media, "volume") ? D(media, "volume") : .6).ToString("0.##", CultureInfo.InvariantCulture), 4); var auto = Check("Começa sozinha", media.ValueKind != JsonValueKind.Object || Flag(media, "autoplay"));
            Buttons(Btn("Salvar a música", () =>
            {
                var problem = IslandManagementWindow.MusicProblem(url.value); if (problem != null) { Say(problem, true); return; }
                if (!float.TryParse(volume.value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0 || v > 1) { Say("Volume: de 0 a 1.", true); return; }
                Send("PUT", P + "/media", new { musicUrl = url.value.Trim().Length == 0 ? null : url.value.Trim(), volume = v, autoplay = auto.value, name = name.value.Trim() }, url.value.Trim().Length == 0 ? "Música retirada." : "Música salva.", () => Show("midia"), LandManagementWindow.Words);
            }, "lv-btn-primary"));
        }

        // ---- Histórico ----

        private void History(JsonElement root)
        {
            Title("Donos e negócios");
            foreach (var h in Arr(root, "history")) Row(Guid.Empty, UiKit.Text(When(Date(h, "at")), "lv-small", "lv-muted"), UiKit.Text(LandManagementWindow.WhatWords(S(h, "what")), "lv-bold"), UiKit.Text((S(h, "from").Length > 0 ? S(h, "from") + " → " : "") + S(h, "to") + (Has(h, "amount") ? " · " + Coins(I(h, "amount")) : ""), "lv-small"));
            if (Arr(root, "history").Count == 0) Line("Nada ainda.", "lv-small", "lv-muted");
            if (!CanManage && !Flag(Obj(root, "me"), "isTenant")) return;
            Get(P + "/history", money =>
            {
                Title("Dinheiro");
                foreach (var t in Items(money)) Row(Guid.Empty, UiKit.Text(When(Date(t, "at")), "lv-small", "lv-muted"), UiKit.Text(LandManagementWindow.WhatWords(S(t, "type")), "lv-bold"), UiKit.Text(Coins(I(t, "amount")), "lv-small"));
                if (Items(money).Count == 0) Line("Nenhum pagamento.", "lv-small", "lv-muted");
            });
        }

        // ---- Auditoria ----

        private void Audit() => Get(P + "/audit", root =>
        {
            var items = Items(root);
            foreach (var a in items) Row(Guid.Empty, UiKit.Text(When(Date(a, "at")), "lv-small", "lv-muted"), UiKit.Text(S(a, "actorName"), "lv-bold"), UiKit.Text(S(a, "summary").Length > 0 ? S(a, "summary") : LandManagementWindow.WhatWords(S(a, "operation")), "lv-small"));
            if (items.Count == 0) Line("Nada registrado.", "lv-small", "lv-muted");
        });
    }
}
