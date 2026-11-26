using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using LifeVerse.Shared.Avatar;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// "Avatar", what the Godot client spreads over its character creator and appearance window: saved looks (save the avatar of now, put one back, rename, copy, share by a code and bring one by a code), the person's
    /// own body (send a GLB, see its triangles, bones and height, use it or go back to the game's), their own clothes (send, see slot, layer and kind, wear, take off), their own animations (send, see the clips, choose
    /// one for each state), and the editor of the character with a mirror view (the camera in front of the avatar). Everything lives at <c>/api/v1/users/me/avatar</c>.
    /// </summary>
    public sealed class AvatarStudioWindow : SectionWindow
    {
        public static readonly (string Id, string Text)[] Sections = { ("visuais", "Visuais salvos"), ("corpo", "Corpo próprio"), ("roupas", "Roupas próprias"), ("animacoes", "Animações próprias"), ("editor", "Editor e espelho") };
        private const string A = "/api/v1/users/me/avatar";
        private readonly Action _openEditor; private readonly Action<bool> _mirror; private readonly Action _changed;
        /// <summary>The share code shown last (for the tests).</summary>
        public string LastCode { get; private set; } = "";

        public AvatarStudioWindow(ApiContext context, Action openEditor = null, Action<bool> mirror = null, Action changed = null) : base(context, "avatar-studio", "Avatar", Sections, 760f, 540f, new Vector2(280, 70), 160f)
        {
            _openEditor = openEditor; _mirror = mirror; _changed = changed; _summary.text = "Seu avatar: visuais salvos, corpo, roupas e animações próprias."; Show("visuais");
        }

        protected override void Fill(string section)
        {
            switch (section) { case "visuais": Looks(); break; case "corpo": Bodies(); break; case "roupas": Clothes(); break; case "animacoes": Animations(); break; case "editor": Editor(); break; }
        }

        public static string Words(int status, string body)
        {
            string code = ""; try { using var d = JsonDocument.Parse(body ?? ""); if (d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) code = e.GetString() ?? ""; } catch (JsonException) { }
            return code switch
            {
                "too_many" or "limit" or "too_many_looks" => "Você chegou ao limite.", "look_invalid" => "Esse visual usa roupas ou itens próprios de quem o fez, que não vêm pelo código (ou não servem mais no seu avatar).", "name_taken" => "Você já tem um visual com esse nome.", "bad_code" or "code_not_found" => "Não existe visual com esse código.", "bad_name" => "Dê um nome de 1 a 40 letras.", "not_found" => "Não encontrado.",
                "bad_slot" => "Esse item não tem um encaixe do avatar.", "too_big" or "too_large" => "O arquivo é grande demais.", "too_many_triangles" => "O modelo tem triângulos demais.", "no_skeleton" or "no_skin" => "O arquivo não tem esqueleto.",
                "invalid_file" or "bad_file" or "invalid_glb" => "O arquivo não é um GLB válido.", "invalid_wearable" or "invalid_model" or "invalid_animation" => "O arquivo não passou nas checagens do servidor:", "bad_override" or "bad_state" => "Essa animação não serve para esse estado.",
                _ => UiTasks.Explain(status, body),
            };
        }

        private void Change(string method, string path, object body, string done, string reload) => Send(method, path, body, done, () => { _changed?.Invoke(); Show(reload); }, Words);

        /// <summary>Sends a GLB to one of the avatar routes (models, wearables, animations) as a multipart file and tells how it went.</summary>
        public void Upload(string kind, string path)
        {
            byte[] bytes; try { bytes = File.ReadAllBytes(path); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) { Say("Não consegui ler o arquivo: " + e.Message, true); return; }
            Say("Enviando " + Path.GetFileName(path) + "…", false); var section = Section;
            this.Then(_c.Api.FileCallAsync(HttpMethod.Post, A + "/" + kind, Path.GetFileName(path), bytes, null), r =>
            {
                if (r.Status < 200 || r.Status >= 300) { Say(Words(r.Status, r.Body) + Problems(r.Body), true); return; }
                _changed?.Invoke(); Show(section); Say(Path.GetFileName(path) + " enviado.", false);
            });
        }

        /// <summary>The problems the server's check found in a file, in one line.</summary>
        public static string Problems(string body)
        {
            try
            {
                using var d = JsonDocument.Parse(body ?? ""); var root = d.RootElement; var report = root.TryGetProperty("report", out var r) ? r : root;
                if (report.ValueKind == JsonValueKind.String) { using var inner = JsonDocument.Parse(report.GetString() ?? "{}"); return IssuesOf(inner.RootElement.Clone()); }
                return IssuesOf(report);
            }
            catch (JsonException) { return ""; }
        }

        /// <summary>The errors of a check report (issues: code, message, error), in one line.</summary>
        public static string IssuesOf(JsonElement report)
        {
            var lines = Arr(report, "issues").Where(i => Flag(i, "error")).Select(i => S(i, "message")).Where(t => t.Length > 0).Take(3).ToList();
            if (lines.Count == 0) foreach (var key in new[] { "errors", "problems" }) lines.AddRange(Arr(report, key).Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : S(e, "message")).Where(t => !string.IsNullOrEmpty(t)).Take(3));
            return lines.Count == 0 ? "" : " " + string.Join(" ", lines);
        }

        private void PickFile(string kind, string title) => new FilePickerWindow(_c.Ui, title, new[] { ".glb" }, path => Upload(kind, path)).Open();

        // ---- Visuais salvos ----

        private void Looks() => Get(A + "/looks", root =>
        {
            var name = Field("Nome do visual", "", 40); Buttons(Btn("Salvar o avatar de agora como visual", () => { if (name.value.Trim().Length == 0) { Say("Dê um nome ao visual.", true); return; } Change("POST", A + "/looks", new { name = name.value.Trim() }, "Visual salvo.", "visuais"); }, "lv-btn-primary"));
            var code = Field("Código de um visual compartilhado", "", 20); Buttons(Btn("Trazer pelo código", () => { if (code.value.Trim().Length == 0) { Say("Digite o código.", true); return; } Change("POST", A + "/looks/import", new { code = code.value.Trim(), name = (string)null }, "Visual trazido.", "visuais"); }));
            var looks = Items(root); Title(looks.Count + " visual(is)");
            foreach (var l in looks)
            {
                var id = G(l, "id"); var lname = S(l, "name"); var shared = S(l, "shareCode");
                var traits = new List<string> { S(l, "hairStyle") }; if (Flag(l, "customBody")) traits.Add("corpo próprio"); if (I(l, "animationOverrides") > 0) traits.Add(I(l, "animationOverrides") + " animação(ões)"); if (shared.Length > 0) traits.Add("código " + shared);
                Row(id, UiKit.Text(lname, "lv-bold"), UiKit.Text(string.Join(" · ", traits.Where(t => t.Length > 0)) + " · " + When(Date(l, "updatedAt")), "lv-small", "lv-muted"), UiKit.Spacer(),
                    Btn("Vestir " + lname, () => Change("POST", A + "/looks/" + id + "/apply", "{}", lname + " vestido.", "visuais"), "lv-btn-primary"), Btn("Copiar " + lname, () => Change("POST", A + "/looks/" + id + "/duplicate", "{}", "Cópia feita.", "visuais")),
                    shared.Length > 0 ? Btn("Parar de compartilhar " + lname, () => Change("DELETE", A + "/looks/" + id + "/share", null, "Código desfeito.", "visuais")) : Btn("Compartilhar " + lname, () => Share(id)),
                    Sure("Apagar " + lname, () => "Apagar o visual " + lname + "?", () => Change("DELETE", A + "/looks/" + id, null, "Visual apagado.", "visuais")));
            }
            if (looks.Count == 0) Line("Nenhum visual salvo ainda.", "lv-small", "lv-muted");
        });

        private void Share(Guid id) => this.Then(_c.Api.ApiAsync("POST", A + "/looks/" + id + "/share", "{}"), r =>
        {
            if (r.Status < 200 || r.Status >= 300) { Say(Words(r.Status, r.Body), true); return; }
            try { using var d = JsonDocument.Parse(r.Body); LastCode = S(d.RootElement, "shareCode"); } catch (JsonException) { }
            GUIUtility.systemCopyBuffer = LastCode; Show("visuais"); Say("Código " + LastCode + " copiado: quem tiver o código traz uma cópia deste visual.", false);
        });

        // ---- Corpo próprio ----

        private void Bodies() => Get(A + "/models", root =>
        {
            Line("Um corpo próprio é um GLB com esqueleto humano (as checagens do servidor dizem o que falta). Fica no lugar do corpo do jogo para todos.", "lv-small", "lv-muted");
            Buttons(Btn("Enviar um corpo (.glb)…", () => PickFile("models", "Escolher o corpo"), "lv-btn-primary"), Btn("Voltar ao corpo do jogo", () => Change("DELETE", A + "/models/use", null, "Corpo do jogo em uso.", "corpo")));
            var models = Items(root);
            foreach (var m in models)
            {
                var id = G(m, "id"); var name = S(m, "name"); var cells = new List<VisualElement> { UiKit.Text((Flag(m, "active") ? "● " : "") + name, "lv-bold"), UiKit.Text(I(m, "triangles").ToString("N0", Pt) + " triângulos · " + I(m, "joints") + " ossos · " + D(m, "height").ToString("0.00", Pt) + " m · " + InventoryManagerWindow.Size(I(m, "sizeBytes")), "lv-small", "lv-muted"), UiKit.Spacer() };
                if (!Flag(m, "active")) cells.Add(Btn("Usar " + name, () => Change("POST", A + "/models/" + id + "/use", "{}", name + " em uso.", "corpo"), "lv-btn-primary"));
                cells.Add(Btn("Relatório de " + name, () => Report("models", id, name))); cells.Add(Sure("Apagar " + name, () => "Apagar o corpo " + name + "?", () => Change("DELETE", A + "/models/" + id, null, "Corpo apagado.", "corpo")));
                Row(id, cells.ToArray());
            }
            if (models.Count == 0) Line("Nenhum corpo enviado.", "lv-small", "lv-muted");
        });

        private void Report(string kind, Guid id, string name) => Get(A + "/" + kind + "/" + id + "/report", r =>
        {
            Reset(); Buttons(Btn("Voltar", () => Show(Section))); Title("Relatório de " + name);
            var report = r.TryGetProperty("report", out var inner) && inner.ValueKind == JsonValueKind.Object ? inner : r;
            Line(string.Join(" · ", new[] { ("triangles", "triângulos"), ("joints", "ossos"), ("vertices", "vértices") }.Where(k => Has(report, k.Item1)).Select(k => I(report, k.Item1).ToString("N0", Pt) + " " + k.Item2)), "lv-small", "lv-muted");
            foreach (var issue in Arr(report, "issues")) Line((Flag(issue, "error") ? "✗ " : "• ") + S(issue, "message"), Flag(issue, "error") ? "lv-bad" : "lv-small");
            if (Lines <= 2) Line(r.ToString().Length > 600 ? r.ToString().Substring(0, 600) + "…" : r.ToString(), "lv-small", "lv-muted");
        });

        // ---- Roupas próprias ----

        public static string SlotWords(string slot) => slot switch
        {
            "top" => "parte de cima", "jacket" => "casaco", "bottom" => "parte de baixo", "shoes" => "calçados", "gloves" => "luvas", "hat" => "chapéu", "glasses" => "óculos", "neck" => "pescoço", "ears" => "orelhas", "back" => "costas", "accessory" => "acessório", "hair" => "cabelo", "eyes" => "olhos", _ => slot,
        };

        private void Clothes() => Get(A + "/wearables", root => Get(A, avatar =>
        {
            Line("Roupas e acessórios seus em GLB. Roupa (parte de cima, de baixo, calçados…) dobra com o corpo; acessório (chapéu, óculos, mochila…) fica preso a um osso.", "lv-small", "lv-muted");
            Buttons(Btn("Enviar uma roupa ou acessório (.glb)…", () => PickFile("wearables", "Escolher a roupa"), "lv-btn-primary"));
            var draft = CharacterDraft.FromResponse(avatar.GetRawText()); var items = Items(root);
            foreach (var w in items)
            {
                var id = G(w, "id"); var name = S(w, "name"); var slot = S(w, "slot"); var key = S(w, "key").Length > 0 ? S(w, "key") : AvatarRules.CustomKey(id); var worn = draft.Worn.TryGetValue(slot, out var on) && on.Item == key;
                var cells = new List<VisualElement> { UiKit.Text((worn ? "● " : "") + name, "lv-bold"), UiKit.Text(SlotWords(slot) + " · " + S(w, "kind") + " · camada " + S(w, "layer") + " · " + I(w, "triangles").ToString("N0", Pt) + " triângulos" + (Arr(w, "covers").Count > 0 ? " · cobre " + string.Join(", ", Arr(w, "covers").Select(c => SlotWords(c.GetString()))) : ""), "lv-small", "lv-muted"), UiKit.Spacer() };
                cells.Add(worn ? Btn("Tirar " + name, () => Wear(slot, null, name)) : Btn("Vestir " + name, () => Wear(slot, key, name), "lv-btn-primary"));
                cells.Add(Btn("Relatório de " + name, () => Report("wearables", id, name))); cells.Add(Sure("Apagar " + name, () => "Apagar " + name + "?", () => Change("DELETE", A + "/wearables/" + id, null, name + " apagado.", "roupas")));
                Row(id, cells.ToArray());
            }
            if (items.Count == 0) Line("Nenhuma roupa enviada.", "lv-small", "lv-muted");
        }));

        /// <summary>Wears an item in its slot (or takes off what is in the slot, with null), saving the avatar as it is otherwise.</summary>
        public void Wear(string slot, string key, string name) => this.Then(_c.Api.ApiAsync("GET", A), r =>
        {
            if (r.Status != 200) { Say(Words(r.Status, r.Body), true); return; }
            var draft = CharacterDraft.FromResponse(r.Body); if (key == null) draft.Worn.Remove(slot); else draft.Worn[slot] = (key, draft.Worn.TryGetValue(slot, out var was) ? was.Tint : "#FFFFFF");
            Send("PUT", A, draft.ToRequest(), key == null ? name + " tirado." : name + " vestido.", () => { _changed?.Invoke(); Show("roupas"); }, Words);
        });

        // ---- Animações próprias ----

        private void Animations() => Get(A + "/animations", files => Get(A + "/animation-library", library => Get(A, avatar =>
        {
            Line("Envie GLBs com animações para o esqueleto humano e escolha uma para cada estado (parado, andando, correndo…). Valem para todos que veem você.", "lv-small", "lv-muted");
            Buttons(Btn("Enviar animações (.glb)…", () => PickFile("animations", "Escolher as animações"), "lv-btn-primary"));
            var list = Items(files);
            foreach (var f in list)
            {
                var id = G(f, "id"); var name = S(f, "name");
                Row(id, UiKit.Text(name, "lv-bold"), UiKit.Text(string.Join(", ", Arr(f, "clips").Select(c => S(c, "name") + " (" + D(c, "duration").ToString("0.0", Pt) + " s)")), "lv-small", "lv-muted"), UiKit.Spacer(), Sure("Apagar " + name, () => "Apagar " + name + "?", () => Change("DELETE", A + "/animations/" + id, null, name + " apagado.", "animacoes")));
            }
            if (list.Count == 0) Line("Nenhum arquivo de animação enviado.", "lv-small", "lv-muted");
            var current = new Dictionary<string, string>(); try { using var o = JsonDocument.Parse(S(avatar, "animationOverridesJson").Length > 0 ? S(avatar, "animationOverridesJson") : "{}"); foreach (var p in o.RootElement.EnumerateObject()) current[p.Name] = p.Value.GetString(); } catch (Exception e) when (e is JsonException || e is InvalidOperationException) { }
            Title("Animação de cada estado"); var choices = new Dictionary<string, DropdownField>(); var states = Obj(library, "states");
            foreach (var state in (states.ValueKind == JsonValueKind.Object ? states.EnumerateObject().Select(p => p.Name) : AnimationStates.All.Keys).ToList())
            {
                var options = new List<(string, string)> { ("", "A do jogo") };
                foreach (var b in Arr(Obj(states, state), "builtin")) options.Add((AnimationStates.BuiltinPrefix + b.GetString(), "Do jogo: " + b.GetString()));
                foreach (var f in list) foreach (var c in Arr(f, "clips")) options.Add((AnimationStates.CustomPrefix + G(f, "id") + "#" + S(c, "name"), S(f, "name") + ": " + S(c, "name")));
                choices[state] = Choice(StateWords(state), options, current.TryGetValue(state, out var v) ? v : "");
            }
            Buttons(Btn("Salvar as animações", () => Change("PUT", A + "/animation-overrides", new { overrides = choices.Where(c => IdOf(c.Value).Length > 0).ToDictionary(c => c.Key, c => IdOf(c.Value)) }, "Animações salvas.", "animacoes"), "lv-btn-primary"));
        })));

        public static string StateWords(string state) => state switch
        {
            "Idle" => "Parado", "Walk" => "Andando", "Run" => "Correndo", "Jump" => "Pulando", "Fall" => "Caindo", "Landing" => "Aterrissando", "FlyIdle" => "Voando parado", "FlyMove" => "Voando", "Sit" => "Sentado", "SitFloor" => "Sentado no chão",
            "StandUp" => "Levantando", "Swim" => "Nadando", "Dance" => "Dançando", "Wave" => "Acenando", "Applaud" => "Aplaudindo", "Point" => "Apontando", "Greet" => "Cumprimentando", "Laugh" => "Rindo", _ => state,
        };

        // ---- Editor e espelho ----

        private void Editor()
        {
            Line("O editor do personagem (rosto, corpo, cabelo, cores e roupas do jogo). O espelho põe a câmera de frente para o seu avatar enquanto você muda.", "lv-small", "lv-muted");
            Buttons(Btn("Abrir o editor do personagem", () => _openEditor?.Invoke(), "lv-btn-primary"));
            var mirror = Check("Espelho (câmera de frente)", false); mirror.RegisterValueChangedCallback(e => _mirror?.Invoke(e.newValue)); Closed += () => _mirror?.Invoke(false);
        }
    }
}
