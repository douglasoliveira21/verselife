using System;
using System.Collections.Generic;
using System.Linq;
using LifeVerse.Shared.Building;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>What only LifeVerse has, in windows of its own: the menu (constructions of the inventory, copy, delete, undo, return, lost objects), aligning, the limits with the history, and the grid.</summary>
    public sealed partial class BuildWindow
    {
        private Vector2 Beside(float down = 0f) => new Vector2(DefaultX + _ui.Sheet.Size.x + 14f, DefaultY + down);

        private void OpenGrid() => Child("build-grid", () => new BuildGridWindow(Ui, _b, _links.Remember));

        /// <summary>The menu of the window.</summary>
        private void OpenMenu() => Child("build-menu", () =>
        {
            var window = new FloatingWindow(Ui, "build-menu", "Mais ações", 270f, -1f, Beside()); var any = _b.Selection.Count > 0;
            Button Item(string name, string text, Action run, bool enabled = true)
            {
                var button = UiKit.Button(text, () => { window.Close(); run(); }); button.name = name; button.SetEnabled(enabled); window.Body.Add(button); return button;
            }
            Button Sure(string name, string text, Action run)
            {
                var button = Controls.Confirm(text, () => "Confirmar: " + text.ToLowerInvariant(), () => { window.Close(); run(); }, "lv-btn-secondary"); button.name = name; button.SetEnabled(any); window.Body.Add(button); return button;
            }
            Item("menu-save", "Guardar construção…", OpenSave, any);
            Item("menu-place", "Colocar do inventário…", PickPackage);
            Item("menu-import", "Importar modelo…", () => new ModelImportWindow(_c, _b).Open());
            window.Body.Add(UiKit.Divider());
            Item("menu-copy", "Copiar a seleção (Ctrl+D)", () => _b.Duplicate(), any);
            Item("menu-undo", "Desfazer (Ctrl+Z)", () => { if (!_b.Undo()) Say("Nada para desfazer.", false); });
            Item("menu-redo", "Refazer (Ctrl+Y)", () => { if (!_b.Redo()) Say("Nada para refazer.", false); });
            Sure("menu-delete", "Apagar", () => _b.Delete(_b.Selection.ToList()));
            Sure("menu-return", "Devolver ao dono", () => { if (!_b.Return(_b.Selection.ToList())) Say("Selecione o que devolver.", true); });
            window.Body.Add(UiKit.Divider());
            Item("menu-align", "Alinhar e distribuir…", OpenAlign, _b.Selection.Count >= 2);
            Item("menu-info", "Limites e histórico…", OpenInfo);
            Item("menu-lost", "Objetos apagados…", () => new LostObjectsWindow(_c).Open());
            Item("menu-tutorial", "Repetir o tutorial", () => { _b.Tutorial.Restart(); RefreshTutorial(); });
            return window;
        });

        private void OpenSave() => Child("build-save", () =>
        {
            var window = new FloatingWindow(Ui, "build-save", "Guardar construção", 340f, -1f, Beside(40f));
            var name = UiKit.Field("Nome da construção", false, 160, First()?.Name ?? ""); name.name = "build-save-name"; window.Body.Add(name);
            var note = UiKit.Text("Guarda no inventário os objetos escolhidos (só os seus).", "lv-small", "lv-muted"); note.style.whiteSpace = WhiteSpace.Normal; window.Body.Add(note);
            window.Body.Add(UiKit.Button("Guardar no inventário", () => { SaveConstruction(name.value, false); window.Close(); }, "lv-btn-primary"));
            window.Body.Add(Controls.Confirm("Guardar e retirar do mundo", () => "Confirmar", () => { SaveConstruction(name.value, true); window.Close(); }, "lv-btn-secondary"));
            return window;
        });

        /// <summary>Saves the selection as a construction of the inventory (and takes it out of the world when asked).</summary>
        public void SaveConstruction(string name, bool take) => _b.SavePackage(name, take, message => Say(message, false));

        private void PickPackage() => Child("build-packages", () =>
        {
            var window = new FloatingWindow(Ui, "build-packages", "Construções do inventário", 380f, 420f, Beside(40f)); var list = new ScrollView(); list.style.flexGrow = 1; window.Body.Add(list);
            window.Then(_c.Api.ApiAsync("GET", "/api/v1/inventory/items?view=all&page=1&pageSize=100", null), r =>
            {
                if (r.Status != 200) { list.Add(UiKit.Text(UiTasks.Explain(r.Status, r.Body), "lv-bad")); return; }
                var rows = Json.Rows(r.Body).Where(x => { var kind = x.First("content", "category", "kind").ToLowerInvariant(); return kind.Contains("build") || kind.Contains("package") || kind.Contains("object"); }).ToList();
                if (rows.Count == 0) list.Add(UiKit.Text("Nenhuma construção guardada.", "lv-muted"));
                foreach (var row in rows) { var item = row.Id("id", "itemId"); list.Add(UiKit.Button(row.First("name", "displayName"), () => { _b.PlacePackage(item, m => Say(m, false)); window.Close(); })); }
            });
            return window;
        });

        // ---- limits and history ----

        /// <summary>The window with the limits of the island and of the parcel, and the history of the chosen object.</summary>
        public void OpenInfo() => Child("build-info", () =>
        {
            var window = new FloatingWindow(Ui, "build-info", "Limites e histórico", 420f, 420f, Beside(60f)); var scroll = new ScrollView(); scroll.style.flexGrow = 1; window.Body.Add(scroll);
            var limits = UiKit.Text(_limitsText.Length > 0 ? _limitsText : "Lendo…", "lv-body"); limits.name = "build-limits"; limits.style.whiteSpace = WhiteSpace.Normal; scroll.Add(limits);
            var history = UiKit.Text("", "lv-small"); history.name = "build-history"; history.style.whiteSpace = WhiteSpace.Normal; history.style.marginTop = 10; scroll.Add(history);
            void Arrived() => limits.text = _limitsText;
            LimitsArrived += Arrived; window.Closed += () => LimitsArrived -= Arrived;
            void Load() { AskLimits(true); LoadHistory(window, history); }
            window.Body.Add(UiKit.Button("Atualizar", Load)); Load(); return window;
        });

        private void LoadHistory(VisualElement owner, Label into)
        {
            if (_b.Selection.Count == 0) { into.text = "Escolha um objeto para ver o histórico dele."; return; }
            var id = _b.Selection[0]; into.text = "Lendo o histórico…";
            owner.Then(_c.Api.ApiAsync("GET", "/api/v1/worlds/islands/" + _c.CurrentIsland() + "/objects/" + id + "/audit", null), r =>
            {
                if (r.Status != 200) { into.text = "Não foi possível ler o histórico (" + UiTasks.Explain(r.Status) + ")."; return; }
                var rows = Json.Rows(r.Body); into.text = rows.Count == 0 ? "Sem registros." : "Histórico\n" + string.Join("\n", rows.Select(e => DiscoveryWindow.When(e.Text("at")) + "  ·  " + OperationLabel(e.Text("operation")) + " por " + (e.Id("actorUserId") == _c.Api.UserId ? "você" : e.Text("actorUserId").Length >= 8 ? e.Text("actorUserId").Substring(0, 8) : "?") + "  ·  revisão " + e.Number("revision")));
            });
        }

        public static string OperationLabel(string operation)
        {
            switch (operation) { case "create": return "Criado"; case "update": return "Alterado"; case "delete": return "Excluído"; case "return": return "Devolvido"; case "structure": return "Vínculo alterado"; case "protect": return "Proteção alterada"; case "properties": return "Propriedades alteradas"; case "buy": return "Comprado"; case "rez": return "Colocado"; default: return operation; }
        }

        // ---- aligning ----

        private void OpenAlign() => Child("build-align", () =>
        {
            var window = new FloatingWindow(Ui, "build-align", "Alinhar e distribuir", 430f, -1f, Beside(80f));
            var note = UiKit.Text("Escolha dois ou mais objetos (três para distribuir). Os eixos são os da aba Objeto.", "lv-small", "lv-muted"); note.style.whiteSpace = WhiteSpace.Normal; window.Body.Add(note);
            // the person reads the viewer's axes: Y (north) runs against the game's z, and Z (height) is the game's y
            foreach (var (title, axis, low, high) in new[] { ("X (leste)", AlignAxis.X, AlignEdge.Min, AlignEdge.Max), ("Y (norte)", AlignAxis.Z, AlignEdge.Max, AlignEdge.Min), ("Z (altura)", AlignAxis.Y, AlignEdge.Min, AlignEdge.Max) })
            {
                window.Body.Add(UiKit.Text(title, "lv-bold"));
                window.Body.Add(UiKit.Row(UiKit.Button("Menor", () => Align(axis, low)), UiKit.Button("Centro", () => Align(axis, AlignEdge.Center)), UiKit.Button("Maior", () => Align(axis, high)), UiKit.Button("Distribuir", () => Distribute(axis))));
            }
            return window;
        });

        private List<KeyValuePair<Guid, UnityEngine.Bounds>> BoxesOfSelection() => _b.Selection.Select(id => _b.BoundsOf(id, out var box) ? new KeyValuePair<Guid, UnityEngine.Bounds>(id, box) : default).Where(p => p.Key != Guid.Empty).ToList();

        /// <summary>Lines the selection up along an axis of the game (x east, y height, z south).</summary>
        public void Align(AlignAxis axis, AlignEdge edge)
        {
            var boxes = BoxesOfSelection(); if (boxes.Count < 2) { Say("Selecione dois ou mais objetos.", true); return; }
            var moves = AlignMath.Align(boxes.Select(b => BuildEditor.ToGameBounds(b.Value)).ToList(), axis, edge); ApplyMoves(boxes.Select(b => b.Key).ToList(), moves, axis, "alinhar");
        }

        /// <summary>Spreads the selection evenly along an axis of the game.</summary>
        public void Distribute(AlignAxis axis)
        {
            var boxes = BoxesOfSelection(); if (boxes.Count < 3) { Say("Distribuir pede três ou mais objetos.", true); return; }
            var moves = AlignMath.Distribute(boxes.Select(b => BuildEditor.ToGameBounds(b.Value)).ToList(), axis); ApplyMoves(boxes.Select(b => b.Key).ToList(), moves, axis, "distribuir");
        }

        private void ApplyMoves(List<Guid> ids, float[] moves, AlignAxis axis, string label)
        {
            for (var i = 0; i < ids.Count && i < moves.Length; i++) { var delta = moves[i]; if (Mathf.Abs(delta) < .0005f) continue; _b.Edit(new[] { ids[i] }, s => { if (axis == AlignAxis.X) s.PositionX += delta; else if (axis == AlignAxis.Y) s.PositionY += delta; else s.PositionZ += delta; }, label); }
        }
    }
}
