using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The panel of the build mode. Criar: the shapes (a click makes one in front of you). Objeto: exact position, turn and size of the selected object, and nudges. Aparência: colour, opacity, glow, shine, picture. Geral: name,
    /// description, protection, linking, copy, delete, undo and redo, and saving the selection as a construction of the inventory (or placing one). Limites: how much of the island's budget is used. Every button sends a command; the
    /// island and the API decide, and the numbers shown are always what they last confirmed.
    /// </summary>
    public sealed partial class BuildWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly BuildEditor _b; private readonly Dictionary<string, VisualElement> _tabs = new Dictionary<string, VisualElement>(); private readonly Label _status, _selection;
        private readonly TextField[] _numbers = new TextField[9]; private readonly TextField _name, _about, _gridField, _packageName, _textureId; private readonly Label _limits; private bool _filling;
        private Label _tutorial; private VisualElement _tutorialBox; private Toggle _protect, _double, _fullBright; private Slider _opacity, _rough, _metal, _glow; private VisualElement _colour, _emission;
        public string CurrentTab { get; private set; }
        public TextField NumberField(int index) => _numbers[index];
        public TextField NameField => _name;
        public string StatusText => _status.text;
        public string SelectionText => _selection.text;
        public string LimitsText => _limits.text;
        public TextField PackageName => _packageName;
        public TextField TextureId => _textureId;

        public BuildWindow(ApiContext context, BuildEditor editor) : base(context.Ui, "build", "Construir", 440f, 590f, new Vector2(20, 80))
        {
            _c = context; _b = editor; var bar = UiKit.Row(); bar.style.flexWrap = Wrap.Wrap; bar.style.flexShrink = 0; _selection = UiKit.Text("Nada selecionado.", "lv-small", "lv-muted"); _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal;
            foreach (var tab in new[] { "Criar", "Objeto", "Forma", "Aparência", "Física", "Dados", "Conteúdo", "Alinhar", "Geral", "Auditoria", "Limites" }) { var captured = tab; bar.Add(UiKit.Button(tab, () => Show(captured))); var panel = new ScrollView(); panel.style.flexGrow = 1; panel.style.flexShrink = 1; panel.style.minHeight = 0; panel.style.display = DisplayStyle.None; _tabs[tab] = panel; }
            Body.style.flexGrow = 1; Body.style.minHeight = 0; style.overflow = Overflow.Hidden; _selection.style.flexShrink = 0; _status.style.flexShrink = 0;
            _tutorial = UiKit.Text("", "lv-small", "lv-accent"); _tutorial.style.whiteSpace = WhiteSpace.Normal; _tutorial.style.flexShrink = 1; _tutorialBox = UiKit.Row(_tutorial); _tutorialBox.style.flexShrink = 0; _tutorialBox.style.flexWrap = Wrap.Wrap;
            Body.Add(_tutorialBox); Body.Add(bar); Body.Add(_selection);
            foreach (var panel in _tabs.Values) Body.Add(panel); Body.Add(_status);

            // Criar
            var grid = new VisualElement(); grid.style.flexDirection = FlexDirection.Row; grid.style.flexWrap = Wrap.Wrap;
            foreach (var entry in BuildCommands.Entries) { var type = entry.Type; var button = UiKit.Button(entry.Label, () => { if (!_b.Create(type)) Say("Entre numa ilha antes de construir.", true); else Say("Criando: " + BuildCommands.LabelOf(type) + "…", false); }); button.style.width = 112; grid.Add(button); }
            _tabs["Criar"].Add(UiKit.Text("Clique numa forma: ela aparece alguns metros à sua frente.", "lv-muted", "lv-small")); _tabs["Criar"].Add(grid);
            _tabs["Criar"].Add(UiKit.Button("Importar um modelo 3D…", () => new ModelImportWindow(_c, _b).Open())); _tabs["Criar"].Add(UiKit.Divider()); _tabs["Criar"].Add(UiKit.Text("As setas coloridas na cena", "lv-bold")); _tabs["Criar"].Add(UiKit.Row(UiKit.Button("Mover", () => { _b.Mode = BuildEditor.GizmoMode.Move; Say("Arraste uma seta para mover.", false); }), UiKit.Button("Girar", () => { _b.Mode = BuildEditor.GizmoMode.Rotate; Say("Arraste uma seta para girar em torno do eixo (de 15 em 15 graus; Alt solta).", false); }), UiKit.Button("Esticar", () => { _b.Mode = BuildEditor.GizmoMode.Scale; Say("Arraste uma seta para esticar naquele eixo.", false); })));
            _tabs["Criar"].Add(UiKit.Divider()); _gridField = UiKit.Field("Grade do movimento (metros, 0 = livre)", false, 6, "0.25"); _gridField.RegisterValueChangedCallback(e => { if (float.TryParse(e.newValue.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var g) && g >= 0f && g <= 10f) _b.Grid = g; }); _tabs["Criar"].Add(_gridField);
            _tabs["Criar"].Add(UiKit.Text("Setas e Page Up/Down movem a seleção; as setas coloridas na cena arrastam; Alt solta da grade. Delete apaga, Ctrl+D copia, Ctrl+Z desfaz.", "lv-muted", "lv-small"));

            // Objeto
            var labels = new[] { "Posição X", "Posição Y", "Posição Z", "Giro X", "Giro Y", "Giro Z", "Tamanho X", "Tamanho Y", "Tamanho Z" };
            for (var i = 0; i < 9; i++) { _numbers[i] = UiKit.Field(labels[i], false, 12); _tabs["Objeto"].Add(_numbers[i]); }
            _tabs["Objeto"].Add(UiKit.Button("Aplicar valores", ApplyNumbers, "lv-btn-primary"));
            _tabs["Objeto"].Add(UiKit.Row(UiKit.Button("Girar −15°", () => _b.Turn(-15f)), UiKit.Button("Girar +15°", () => _b.Turn(15f)), UiKit.Button("Girar 90°", () => _b.Turn(90f))));
            _tabs["Objeto"].Add(UiKit.Row(UiKit.Button("← X", () => _b.Move(new Vector3(-Step(), 0, 0))), UiKit.Button("X →", () => _b.Move(new Vector3(Step(), 0, 0))), UiKit.Button("↓ Y", () => _b.Move(new Vector3(0, -Step(), 0))), UiKit.Button("Y ↑", () => _b.Move(new Vector3(0, Step(), 0)))));
            _tabs["Objeto"].Add(UiKit.Row(UiKit.Button("← Z", () => _b.Move(new Vector3(0, 0, -Step()))), UiKit.Button("Z →", () => _b.Move(new Vector3(0, 0, Step()))), UiKit.Button("Pousar no chão", DropToGround)));

            // Aparência
            _colour = Controls.ColourField("Cor", "#FFFFFF", value => EditMaterial(m => m["color"] = value), "#FFFFFF", "#A8C7D8", "#C0392B", "#27AE60", "#2980B9", "#F4A340", "#222222"); _tabs["Aparência"].Add(_colour);
            _opacity = UiKit.Slide("Opacidade", 0f, 1f, 1f, v => EditMaterial(m => m["transparency"] = Math.Round(v, 2)), true); _rough = UiKit.Slide("Aspereza", 0f, 1f, .7f, v => EditMaterial(m => m["roughness"] = Math.Round(v, 2)), true); _metal = UiKit.Slide("Metal", 0f, 1f, 0f, v => EditMaterial(m => m["metallic"] = Math.Round(v, 2)), true);
            _emission = Controls.ColourField("Brilho (cor)", "#000000", value => EditMaterial(m => { if (value == "#000000") { m.Remove("emission"); m.Remove("emissionStrength"); } else { m["emission"] = value; if (!m.ContainsKey("emissionStrength")) m["emissionStrength"] = 1.0; } }), "#000000", "#12F2E0", "#F4A340", "#FFFFFF");
            _glow = UiKit.Slide("Força do brilho", 0f, 8f, 1f, v => EditMaterial(m => m["emissionStrength"] = Math.Round(v, 2)));
            _double = UiKit.Check("Dois lados", false, v => EditMaterial(m => m["doubleSided"] = v)); _fullBright = UiKit.Check("Luz total (não escurece)", false, v => EditMaterial(m => m["fullBright"] = v));
            _textureId = UiKit.Field("Textura (identificador do arquivo)", false, 40); _tabs["Aparência"].Add(_opacity); _tabs["Aparência"].Add(_rough); _tabs["Aparência"].Add(_metal); _tabs["Aparência"].Add(_emission); _tabs["Aparência"].Add(_glow); _tabs["Aparência"].Add(_double); _tabs["Aparência"].Add(_fullBright); _tabs["Aparência"].Add(_textureId);
            _tabs["Aparência"].Add(UiKit.Row(UiKit.Button("Usar textura", ApplyTexture, "lv-btn-primary"), UiKit.Button("Escolher do inventário…", PickTexture), UiKit.Button("Tirar textura", () => EditMaterial(m => m.Remove("texture")))));

            // Geral
            _name = UiKit.Field("Nome", false, 160); _about = UiKit.Field("Descrição", false, 400); _tabs["Geral"].Add(_name); _tabs["Geral"].Add(_about); _tabs["Geral"].Add(UiKit.Button("Salvar nome e descrição", () => { var n = _name.value.Trim(); var d = _about.value; if (n.Length == 0) { Say("O nome não pode ficar vazio.", true); return; } _b.Edit(_b.Selection.ToList(), s => { s.Name = n; s.Description = d; }, "renomear"); }));
            _protect = UiKit.Check("Proteger contra edição acidental", false, v => { if (!_filling) _b.Protect(v); }); _tabs["Geral"].Add(_protect);
            _tabs["Geral"].Add(UiKit.Row(UiKit.Button("Vincular (o 1º é o pai)", () => { if (!_b.Link(true)) Say("Selecione pelo menos dois objetos (o primeiro é o pai).", true); }), UiKit.Button("Desvincular", () => _b.Link(false))));
            _tabs["Geral"].Add(UiKit.Row(UiKit.Button("Copiar", () => _b.Duplicate()), UiKit.Button("Desfazer", () => { if (!_b.Undo()) Say("Nada para desfazer.", false); }), UiKit.Button("Refazer", () => { if (!_b.Redo()) Say("Nada para refazer.", false); }), Controls.Confirm("Apagar", () => "Confirmar", () => _b.Delete(_b.Selection.ToList()), "lv-btn-secondary")));
            _tabs["Geral"].Add(UiKit.Divider()); _tabs["Geral"].Add(UiKit.Text("Guardar como construção", "lv-bold")); _packageName = UiKit.Field("Nome da construção", false, 160);
            _tabs["Geral"].Add(_packageName); _tabs["Geral"].Add(UiKit.Row(UiKit.Button("Guardar no inventário", () => _b.SavePackage(_packageName.value, false, m => Say(m, false)), "lv-btn-primary"), Controls.Confirm("Guardar e retirar", () => "Confirmar", () => _b.SavePackage(_packageName.value, true, m => Say(m, false)), "lv-btn-secondary")));
            _tabs["Geral"].Add(UiKit.Button("Colocar uma construção do inventário…", PickPackage)); _tabs["Geral"].Add(UiKit.Button("Objetos apagados…", () => new LostObjectsWindow(_c).Open()));

            // Limites
            _limits = UiKit.Text("", "lv-body"); _limits.style.whiteSpace = WhiteSpace.Normal; _tabs["Limites"].Add(_limits); _tabs["Limites"].Add(UiKit.Button("Atualizar", LoadLimits));

            BuildExtraTabs();
            _b.Changed += Refresh; _b.Said += text => Say(text, text.Contains("recusa") || text.Contains("não") || text.Contains("Não")); Closed += () => { _b.Changed -= Refresh; _b.SetActive(false); };
            Show("Criar"); Refresh();
        }

        private float Step() => _b.Grid > 0f ? _b.Grid : .25f;

        public void Show(string tab) { CurrentTab = tab; foreach (var pair in _tabs) pair.Value.style.display = pair.Key == tab ? DisplayStyle.Flex : DisplayStyle.None; if (tab == "Limites") LoadLimits(); else if (tab == "Auditoria") LoadAudit(); Refresh(); }
        private void Say(string text, bool error) { _status.text = text; _status.EnableInClassList("lv-bad", error); }

        // ---- reading what the island confirmed ----

        private WorldObjectState First() => _b.Selection.Count > 0 ? _b.StateOf(_b.Selection[0]) : null;

        public void Refresh()
        {
            var first = First(); var count = _b.Selection.Count;
            RefreshTutorial();
            _selection.text = count == 0 ? "Nada selecionado. Clique num objeto." : count == 1 ? first.Name + " (" + BuildCommands.LabelOf(first.PrimitiveType) + ")" + (_b.HeldByOther(first.Id, out var who) ? "  ·  em edição por " + who : first.EditLocked ? "  ·  protegido" : "") : count + " objetos selecionados";
            _filling = true;
            try
            {
                RefreshExtra(first);
                if (first != null)
                {
                    float[] v = { first.PositionX, first.PositionY, first.PositionZ, first.RotationX, first.RotationY, first.RotationZ, first.ScaleX, first.ScaleY, first.ScaleZ };
                    for (var i = 0; i < 9; i++) if (_numbers[i].focusController?.focusedElement != _numbers[i]) _numbers[i].SetValueWithoutNotify(v[i].ToString("0.###", CultureInfo.InvariantCulture));
                    _name.SetValueWithoutNotify(first.Name); _about.SetValueWithoutNotify(first.Description ?? ""); _protect.SetValueWithoutNotify(first.EditLocked);
                    var m = ParseMaterial(first.MaterialJson); _opacity.SetValueWithoutNotify(m["transparency"] is JsonValue t ? t.GetValue<float>() : 1f); _rough.SetValueWithoutNotify(m["roughness"] is JsonValue r ? r.GetValue<float>() : .7f); _metal.SetValueWithoutNotify(m["metallic"] is JsonValue mm ? mm.GetValue<float>() : 0f);
                    _double.SetValueWithoutNotify(m["doubleSided"] is JsonValue d && d.GetValue<bool>()); _fullBright.SetValueWithoutNotify(m["fullBright"] is JsonValue f && f.GetValue<bool>()); _glow.SetValueWithoutNotify(m["emissionStrength"] is JsonValue g ? g.GetValue<float>() : 1f);
                    if (_textureId.focusController?.focusedElement != _textureId) _textureId.SetValueWithoutNotify(m["texture"]?.ToString() ?? "");
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is FormatException) { }
            finally { _filling = false; }
        }

        private static JsonObject ParseMaterial(string json) { try { return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? new JsonObject(); } catch (JsonException) { return new JsonObject(); } }

        // ---- editing ----

        private bool Parse(string text, out float value) => float.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>Checks the nine numbers; returns the first problem (null when fine) and the values.</summary>
        public string ReadNumbers(out float[] values)
        {
            values = new float[9]; var names = new[] { "Posição X", "Posição Y", "Posição Z", "Giro X", "Giro Y", "Giro Z", "Tamanho X", "Tamanho Y", "Tamanho Z" };
            for (var i = 0; i < 9; i++) if (!Parse(_numbers[i].value, out values[i])) return names[i] + " não é um número.";
            for (var i = 0; i < 3; i++) if (Mathf.Abs(values[i]) > 5000f) return "Posição fora da ilha (até 5000 m).";
            for (var i = 6; i < 9; i++) if (values[i] < .01f || values[i] > 64f) return "O tamanho vai de 0,01 a 64 metros.";
            return null;
        }

        public void ApplyNumbers()
        {
            if (_b.Selection.Count != 1) { Say(_b.Selection.Count == 0 ? "Selecione um objeto." : "Valores exatos valem para um objeto de cada vez; use as setas para vários.", true); return; }
            var problem = ReadNumbers(out var v); if (problem != null) { Say(problem, true); return; }
            _b.Edit(_b.Selection.ToList(), s => { s.PositionX = v[0]; s.PositionY = v[1]; s.PositionZ = v[2]; s.RotationX = v[3]; s.RotationY = v[4]; s.RotationZ = v[5]; s.ScaleX = v[6]; s.ScaleY = v[7]; s.ScaleZ = v[8]; }, "valores"); Say("Enviado.", false);
        }

        private void DropToGround() => _b.Edit(_b.Selection.ToList(), s => { var ground = _b.GroundAt(s.PositionX, s.PositionZ); if (ground != null) s.PositionY = ground.Value + s.ScaleY / 2f; }, "pousar");

        public void EditMaterial(Action<JsonObject> change)
        {
            if (_filling) return; if (_b.Selection.Count == 0) { Say("Selecione um objeto.", true); return; }
            _b.Edit(_b.Selection.ToList(), s => { var m = ParseMaterial(s.MaterialJson); change(m); s.MaterialJson = m.ToJsonString(); }, "aparência");
        }

        private void ApplyTexture()
        {
            if (!Guid.TryParse(_textureId.value.Trim(), out var id)) { Say("Digite o identificador do arquivo (ou escolha do inventário).", true); return; }
            EditMaterial(m => m["texture"] = id.ToString());
        }

        private void PickTexture()
        {
            var window = new FloatingWindow(_c.Ui, "build-texture", "Escolha uma textura", 360f, 420f, new Vector2(440, 120)); var list = new ScrollView(); list.style.flexGrow = 1; window.Body.Add(list); window.Open();
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/inventory/items?category=textures&page=1&pageSize=100", null), r =>
            {
                if (r.Status != 200) { list.Add(UiKit.Text(UiTasks.Explain(r.Status, r.Body), "lv-bad")); return; }
                var rows = Json.Rows(r.Body); if (rows.Count == 0) list.Add(UiKit.Text("Você não tem texturas no inventário.", "lv-muted"));
                foreach (var row in rows) { var asset = row.Id("assetId"); var label = row.First("name", "displayName"); list.Add(UiKit.Button(label.Length > 0 ? label : asset.ToString().Substring(0, 8), () => { _textureId.value = asset.ToString(); ApplyTexture(); window.Close(); })); }
            });
        }

        private void PickPackage()
        {
            var window = new FloatingWindow(_c.Ui, "build-packages", "Construções do inventário", 380f, 420f, new Vector2(440, 160)); var list = new ScrollView(); list.style.flexGrow = 1; window.Body.Add(list); window.Open();
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/inventory/items?view=all&page=1&pageSize=100", null), r =>
            {
                if (r.Status != 200) { list.Add(UiKit.Text(UiTasks.Explain(r.Status, r.Body), "lv-bad")); return; }
                var rows = Json.Rows(r.Body).Where(x => x.First("content", "category", "kind").ToLowerInvariant().Contains("build") || x.First("content", "category", "kind").ToLowerInvariant().Contains("package") || x.First("content", "category", "kind").ToLowerInvariant().Contains("object")).ToList();
                if (rows.Count == 0) list.Add(UiKit.Text("Nenhuma construção guardada.", "lv-muted"));
                foreach (var row in rows) { var item = row.Id("id", "itemId"); list.Add(UiKit.Button(row.First("name", "displayName"), () => { _b.PlacePackage(item, m => Say(m, false)); window.Close(); })); }
            });
        }

        private void LoadLimits()
        {
            var island = _c.CurrentIsland(); var at = _b.PlacePoint(0f); _limits.text = "Lendo…";
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/worlds/islands/" + island + "/build-limits?x=" + at.x.ToString(CultureInfo.InvariantCulture) + "&z=" + at.z.ToString(CultureInfo.InvariantCulture), null), r =>
            {
                if (r.Status != 200) { _limits.text = "Não foi possível ler os limites (" + UiTasks.Explain(r.Status) + ")."; return; }
                var row = Json.One(r.Body); var parcel = row.Child("parcel");
                _limits.text = "Ilha: " + row.Number("objects") + " de " + row.Number("maxObjects") + " objetos; custo " + row.Number("cost") + " de " + row.Number("maxCost") + "." + (row.Flag("canBuildHere") ? "\nVocê pode construir aqui." : "\nVocê não pode construir aqui.")
                    + (parcel.Has("name") ? "\nTerreno \"" + parcel.Text("name") + "\": " + parcel.Number("objects") + " de " + parcel.Number("maxObjects") + " objetos; custo " + parcel.Number("cost") + " de " + parcel.Number("maxCost") + "." : "");
            });
        }
    }
}
