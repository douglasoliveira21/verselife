using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeVerse.Shared.Building;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>The second half of the build panel: the shape of the solid (hollow, taper, twist, cuts), its physics, who may do what with it and what it sells for, what it carries, aligning several objects, and its history.</summary>
    public sealed partial class BuildWindow
    {
        public static readonly (string Id, string Label)[] ClickActions = { ("touch", "Tocar (padrão)"), ("sit", "Sentar no objeto"), ("buy", "Comprar objeto"), ("pay", "Pagar objeto"), ("open", "Abrir objeto"), ("zoom", "Zoom no objeto"), ("ignore", "Ignorar objeto"), ("none", "Nenhuma") };
        public static readonly (string Id, string Label)[] SaleModes = { ("original", "Vender objeto original"), ("copy", "Vender cópia"), ("contents", "Vender conteúdo") };

        private readonly Dictionary<string, Slider> _shape = new Dictionary<string, Slider>(); private Toggle _collision, _physical, _trigger, _temporary; private Slider _mass, _friction, _bounce; private DropdownField _shapeKind, _material, _click, _sale;
        private TextField _price, _contentAsset; private Toggle _anyoneMove, _anyoneCopy, _nextCopy, _nextModify, _nextTransfer; private Label _audit, _contentList;

        private readonly Dictionary<string, TextField> _mapFields = new Dictionary<string, TextField>(); private Slider _normalStrength, _aoStrength, _offsetX, _offsetY;
        public TextField MapField(string key) => _mapFields[key];
        public Slider NormalStrengthSlider => _normalStrength;
        public Slider OffsetXSlider => _offsetX;

        /// <summary>Puts a picture of the inventory on the surface as one of its maps (normal, ORM or emissive). The identifier must be a file the person has.</summary>
        public void ApplyMap(string key)
        {
            if (!Guid.TryParse(_mapFields[key].value.Trim(), out var id)) { Say("Digite o identificador do arquivo (ou escolha do inventário).", true); return; }
            EditMaterial(m => m[key] = id.ToString());
        }

        private void PickAsset(string key)
        {
            var window = new FloatingWindow(_c.Ui, "build-map", "Escolha uma imagem", 360f, 420f, new Vector2(440, 120)); var list = new ScrollView(); list.style.flexGrow = 1; window.Body.Add(list); window.Open();
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/inventory/items?category=textures&page=1&pageSize=100", null), r =>
            {
                if (r.Status != 200) { list.Add(UiKit.Text(UiTasks.Explain(r.Status, r.Body), "lv-bad")); return; }
                var rows = Json.Rows(r.Body); if (rows.Count == 0) list.Add(UiKit.Text("Você não tem imagens no inventário.", "lv-muted"));
                foreach (var row in rows) { var asset = row.Id("assetId"); var label = row.First("name", "displayName"); list.Add(UiKit.Button(label.Length > 0 ? label : asset.ToString().Substring(0, 8), () => { _mapFields[key].value = asset.ToString(); ApplyMap(key); window.Close(); })); }
            });
        }

        private Slider _repeatX, _repeatY; private DropdownField _face, _mediaType; private TextField _mediaUrl; private TextField _group;
        private int[] _faceIds = new int[0];
        public string TutorialText => _tutorial.text;
        public DropdownField FaceList => _face;

        public static string TutorialStepText(LifeVerse.Shared.Building.TutorialStep step)
        {
            switch (step)
            {
                case LifeVerse.Shared.Building.TutorialStep.Create: return "1/8 · Crie uma caixa: na aba Criar clique em Caixa.";
                case LifeVerse.Shared.Building.TutorialStep.Move: return "2/8 · Mova a caixa: arraste a seta colorida (Mover) ou use as setas do teclado.";
                case LifeVerse.Shared.Building.TutorialStep.Resize: return "3/8 · Mude o tamanho: em Criar escolha Esticar e arraste uma seta, ou digite o tamanho na aba Objeto.";
                case LifeVerse.Shared.Building.TutorialStep.Rotate: return "4/8 · Gire a caixa: em Criar escolha Girar e arraste uma seta, ou use Girar +15° na aba Objeto.";
                case LifeVerse.Shared.Building.TutorialStep.Texture: return "5/8 · Dê cor ou textura: na aba Aparência escolha uma cor.";
                case LifeVerse.Shared.Building.TutorialStep.Duplicate: return "6/8 · Copie a caixa: com ela selecionada, aperte Ctrl+D (ou Copiar na aba Geral).";
                case LifeVerse.Shared.Building.TutorialStep.Link: return "7/8 · Vincule duas peças: selecione as duas (Shift soma) e use Vincular na aba Geral.";
                case LifeVerse.Shared.Building.TutorialStep.Save: return "8/8 · Guarde no inventário: na aba Geral dê um nome e use Guardar no inventário.";
                default: return "Tutorial concluído! Agora é com você. Dá para repetir pelo botão Tutorial.";
            }
        }

        private void RefreshTutorial()
        {
            var tut = _b.Tutorial; _tutorialBox.style.display = DisplayStyle.Flex; _tutorial.text = tut.Skipped ? "Tutorial pulado." : TutorialStepText(tut.Current);
            _tutorialBox.Clear(); _tutorialBox.Add(_tutorial); if (tut.Active) _tutorialBox.Add(UiKit.Button("Pular", () => { tut.Skip(); RefreshTutorial(); })); else _tutorialBox.Add(UiKit.Button("Tutorial", () => { tut.Restart(); RefreshTutorial(); }));
        }

        public Slider ShapeSlider(string key) => _shape[key];
        public TextField MediaUrl => _mediaUrl;
        private int FaceIndex() => _faceIds.Length > 0 && _face.index >= 0 && _face.index < _faceIds.Length ? _faceIds[_face.index] : Mathf.Max(0, _face.index);

        /// <summary>Changes the entry of one face inside the material (the faces keep their own colour, picture or glow; the rest of the object is untouched).</summary>
        public void EditFace(Action<JsonObject> change) => EditMaterial(m =>
        {
            var faces = m["faces"] as JsonArray ?? new JsonArray(); m["faces"] = faces; var index = FaceIndex();
            var entry = faces.OfType<JsonObject>().FirstOrDefault(x => x["index"]?.GetValue<int>() == index); if (entry == null) { entry = new JsonObject { ["index"] = index }; faces.Add(entry); } change(entry);
        });

        public static string FaceName(int id) => id == 0 ? "Topo (0)" : id == 1 ? "Base (1)" : "Lado " + (id - 1) + " (" + id + ")";

        /// <summary>The same rules the island applies to media: https, a public host name, no user name or port tricks.</summary>
        public static string MediaProblem(string type, string url)
        {
            if (type != "image" && type != "video") return "Escolha imagem ou vídeo.";
            if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return "O endereço precisa começar com https://.";
            if (!string.IsNullOrEmpty(uri.UserInfo)) return "O endereço não pode ter usuário e senha.";
            var host = uri.Host.ToLowerInvariant(); if (host == "localhost" || !host.Contains(".") || host.EndsWith(".local") || host.EndsWith(".internal") || System.Net.IPAddress.TryParse(host, out _)) return "Use um endereço de site público (não um número de rede nem um nome local).";
            return null;
        }

        private void ApplyMedia()
        {
            var type = _mediaType.value; var problem = MediaProblem(type, _mediaUrl.value); if (problem != null) { Say(problem, true); return; }
            EditMaterial(m => m["media"] = new JsonObject { ["type"] = type, ["url"] = _mediaUrl.value.Trim(), ["playing"] = true, ["loop"] = true });
        }
        public Toggle PhysicalToggle => _physical;
        public DropdownField ClickAction => _click;
        public TextField PriceField => _price;
        public string AuditText => _audit.text;
        public string ContentText => _contentList.text;
        public TextField ContentAssetField => _contentAsset;

        private void BuildExtraTabs()
        {
            // Forma: the parameters of the solid
            var forma = _tabs["Forma"]; forma.Add(UiKit.Text("Mexa nos controles: a forma muda para todos quando a ilha confirma.", "lv-muted", "lv-small"));
            foreach (var spec in new[] { ("hollow", "Oco", 0f, .95f, 0f), ("taperX", "Afunilar X", -1f, 1f, 0f), ("taperY", "Afunilar Y", -1f, 1f, 0f), ("twist", "Torção (graus)", -180f, 180f, 0f), ("cutStart", "Corte: início", 0f, .95f, 0f), ("cutEnd", "Corte: fim", .05f, 1f, 1f), ("pathBegin", "Caminho: início", 0f, .95f, 0f), ("pathEnd", "Caminho: fim", .05f, 1f, 1f), ("shearX", "Cisalhar X", -.5f, .5f, 0f), ("shearZ", "Cisalhar Z", -.5f, .5f, 0f), ("skew", "Inclinar", -.95f, .95f, 0f), ("revolutions", "Voltas (anéis)", 1f, 3f, 1f) })
            { var key = spec.Item1; var slider = UiKit.Slide(spec.Item2, spec.Item3, spec.Item4, spec.Item5, v => EditGeometry(g => g[key] = Math.Round(v, 3))); _shape[key] = slider; forma.Add(slider); }
            forma.Add(UiKit.Button("Voltar à forma inteira", () => EditGeometry(g => { foreach (var k in g.Select(p => p.Key).ToList()) g.Remove(k); })));

            // Física
            var fisica = _tabs["Física"]; _collision = UiKit.Check("Colide (sólido)", true, v => EditPhysics(p => p with { Collision = v })); _physical = UiKit.Check("Físico (cai e é empurrado)", false, v => EditPhysics(p => p with { Physical = v })); _trigger = UiKit.Check("Gatilho (atravessável, avisa os scripts)", false, v => EditPhysics(p => p with { Trigger = v })); _temporary = UiKit.Check("Temporário", false, v => EditPhysics(p => p with { Temporary = v }));
            _shapeKind = new DropdownField("Forma da colisão", new List<string> { "box", "convex", "triangle", "sphere", "cylinder" }, 0); _shapeKind.RegisterValueChangedCallback(e => { if (!_filling) EditPhysics(p => p with { CollisionShape = e.newValue }); });
            _material = new DropdownField("Superfície", new List<string> { "(nenhuma)" }.Concat(PhysicsRules.Materials.Select(m => m.Value.Label)).ToList(), 0); _material.RegisterValueChangedCallback(e => { if (_filling) return; var id = PhysicsRules.Materials.FirstOrDefault(m => m.Value.Label == e.newValue).Key ?? ""; EditPhysics(p => p with { Material = id }); });
            _mass = UiKit.Slide("Massa (kg)", PhysicsRules.MinMass, 100f, 1f, v => EditPhysics(p => p with { Mass = v })); _friction = UiKit.Slide("Atrito", 0f, 1f, .5f, v => EditPhysics(p => p with { Friction = v }), true); _bounce = UiKit.Slide("Quique", 0f, 1f, 0f, v => EditPhysics(p => p with { Bounce = v }), true);
            foreach (var e in new VisualElement[] { _collision, _physical, _trigger, _temporary, _shapeKind, _material, _mass, _friction, _bounce }) fisica.Add(e); fisica.Add(UiKit.Text("Um objeto físico precisa de colisão sólida e não pode ser gatilho; a ilha recusa o resto.", "lv-muted", "lv-small"));

            // Dados: click action, sale, who may move or copy it, rights of the next owner
            var dados = _tabs["Dados"]; _click = new DropdownField("Ao clicar", ClickActions.Select(c => c.Label).ToList(), 0); _click.RegisterValueChangedCallback(e => { if (_filling) return; var id = ClickActions.First(c => c.Label == e.newValue).Id; SendProperties(new JsonObject { ["clickAction"] = id }); }); dados.Add(_click);
            _sale = new DropdownField("Venda", SaleModes.Select(s => s.Label).ToList(), 0); _price = UiKit.Field("Preço (VerseCoin)", false, 8, "0"); dados.Add(_sale); dados.Add(_price);
            dados.Add(UiKit.Row(UiKit.Button("Pôr à venda", PutOnSale, "lv-btn-primary"), UiKit.Button("Tirar da venda", () => SendProperties(new JsonObject { ["saleType"] = "none" }))));
            _anyoneMove = UiKit.Check("Qualquer pessoa pode mover", false, v => { if (!_filling) SendProperties(new JsonObject { ["anyoneMove"] = v }); }); _anyoneCopy = UiKit.Check("Qualquer pessoa pode copiar", false, v => { if (!_filling) SendProperties(new JsonObject { ["anyoneCopy"] = v }); }); dados.Add(_anyoneMove); dados.Add(_anyoneCopy);
            dados.Add(UiKit.Text("O que o próximo dono poderá fazer", "lv-bold")); _nextCopy = UiKit.Check("Copiar", true, _ => SendNextRights()); _nextModify = UiKit.Check("Modificar", true, _ => SendNextRights()); _nextTransfer = UiKit.Check("Transferir", true, _ => SendNextRights()); dados.Add(_nextCopy); dados.Add(_nextModify); dados.Add(_nextTransfer);
            dados.Add(UiKit.Text("A ilha confere quem pode: você não dá direitos que não tem.", "lv-muted", "lv-small"));
            dados.Add(UiKit.Divider()); _group = UiKit.Field("Grupo do objeto (identificador, vazio = nenhum)", false, 40); dados.Add(_group); dados.Add(UiKit.Button("Dar ao grupo", () => { var text = _group.value.Trim(); if (text.Length == 0) SendProperties(new JsonObject { ["groupId"] = null }); else if (Guid.TryParse(text, out var g)) SendProperties(new JsonObject { ["groupId"] = g.ToString() }); else Say("Digite o identificador do grupo.", true); }));
            dados.Add(UiKit.Divider()); dados.Add(Controls.Confirm("Devolver ao dono (inventário)", () => "Confirmar", () => { if (!_b.Return(_b.Selection.ToList())) Say("Selecione o que devolver.", true); }, "lv-btn-secondary"));

            // Conteúdo: references the object carries (sounds, textures, animations, objects…) and its scripts
            var conteudo = _tabs["Conteúdo"]; _contentList = UiKit.Text("", "lv-body"); _contentList.style.whiteSpace = WhiteSpace.Normal; _contentList.style.display = DisplayStyle.None; conteudo.Add(_contentList); _contentTree = new VisualElement(); conteudo.Add(_contentTree);
            _contentAsset = UiKit.Field("Identificador do item do inventário", false, 40); conteudo.Add(_contentAsset);
            conteudo.Add(UiKit.Row(UiKit.Button("Pôr no objeto", () => AddContent(), "lv-btn-primary"), UiKit.Button("Escolher do inventário…", PickContent), UiKit.Button("Tirar", RemoveContent)));
            conteudo.Add(UiKit.Divider()); conteudo.Add(UiKit.Button("Scripts do objeto…", () => { if (_b.Selection.Count == 1) new LuaEditorWindow(_c, _c.CurrentIsland(), _b.Selection[0]).Open(); else Say("Selecione um só objeto.", true); }));

            // Alinhar: the shared align arithmetic, one axis at a time
            var alinhar = _tabs["Alinhar"]; alinhar.Add(UiKit.Text("Selecione dois ou mais objetos.", "lv-muted", "lv-small"));
            foreach (var axis in new[] { AlignAxis.X, AlignAxis.Y, AlignAxis.Z })
            {
                var a = axis; alinhar.Add(UiKit.Text("Eixo " + a, "lv-bold"));
                alinhar.Add(UiKit.Row(UiKit.Button("Início", () => Align(a, AlignEdge.Min)), UiKit.Button("Centro", () => Align(a, AlignEdge.Center)), UiKit.Button("Fim", () => Align(a, AlignEdge.Max)), UiKit.Button("Distribuir", () => Distribute(a))));
            }

            // Aparência (more): the picture's repeat, one face of the solid, and what plays on it
            var look = _tabs["Aparência"]; look.Add(UiKit.Divider()); look.Add(UiKit.Text("Repetição da imagem", "lv-bold")); _repeatX = UiKit.Slide("Repetir X", .1f, 16f, 1f, v => EditMaterial(m => m["uvRepeatX"] = Math.Round(v, 2))); _repeatY = UiKit.Slide("Repetir Y", .1f, 16f, 1f, v => EditMaterial(m => m["uvRepeatY"] = Math.Round(v, 2))); look.Add(_repeatX); look.Add(_repeatY);
            look.Add(UiKit.Divider()); look.Add(UiKit.Text("Outros mapas da superfície", "lv-bold")); look.Add(UiKit.Text("Cada um é um arquivo de imagem do inventário (o mundo já os desenha).", "lv-muted", "lv-small"));
            foreach (var slot in new[] { ("normalMap", "Relevo (normal)"), ("ormMap", "Oclusão, aspereza e metal (ORM)"), ("emissiveMap", "Brilho (emissivo)") }) { var key = slot.Item1; var field = UiKit.Field(slot.Item2 + " (identificador)", false, 40); _mapFields[key] = field; look.Add(field); look.Add(UiKit.Row(UiKit.Button("Usar", () => ApplyMap(key), "lv-btn-primary"), UiKit.Button("Escolher do inventário…", () => PickAsset(key)), UiKit.Button("Tirar", () => EditMaterial(m => m.Remove(key))))); }
            _normalStrength = UiKit.Slide("Força do relevo", 0f, 2f, 1f, v => EditMaterial(m => m["normalStrength"] = Math.Round(v, 2))); _aoStrength = UiKit.Slide("Força da oclusão", 0f, 1f, 1f, v => EditMaterial(m => m["aoStrength"] = Math.Round(v, 2))); _offsetX = UiKit.Slide("Deslocar X", -1f, 1f, 0f, v => EditMaterial(m => m["uvOffsetX"] = Math.Round(v, 3))); _offsetY = UiKit.Slide("Deslocar Y", -1f, 1f, 0f, v => EditMaterial(m => m["uvOffsetY"] = Math.Round(v, 3)));
            look.Add(_normalStrength); look.Add(_aoStrength); look.Add(_offsetX); look.Add(_offsetY);
            look.Add(UiKit.Divider()); look.Add(UiKit.Text("Uma face só", "lv-bold")); _face = new DropdownField("Face", Enumerable.Range(0, 6).Select(i => "Face " + i).ToList(), 0); look.Add(_face); look.Add(UiKit.Check("Escolher a face clicando no objeto", false, v => _b.FacePick = v));
            look.Add(Controls.ColourField("Cor da face", "#FFFFFF", value => EditFace(f => f["color"] = value), "#FFFFFF", "#C0392B", "#27AE60", "#2980B9", "#F4A340", "#222222")); look.Add(UiKit.Button("Tirar o que é só desta face", () => EditMaterial(m => { if (m["faces"] is JsonArray faces) { var index = FaceIndex(); var entry = faces.OfType<JsonObject>().FirstOrDefault(x => x["index"]?.GetValue<int>() == index); if (entry != null) faces.Remove(entry); if (faces.Count == 0) m.Remove("faces"); } })));
            look.Add(UiKit.Divider()); look.Add(UiKit.Text("Mídia na superfície", "lv-bold")); _mediaType = new DropdownField("Tipo", new List<string> { "image", "video" }, 0); _mediaUrl = UiKit.Field("Endereço (https://…)", false, 500); look.Add(_mediaType); look.Add(_mediaUrl);
            look.Add(UiKit.Row(UiKit.Button("Aplicar mídia", ApplyMedia, "lv-btn-primary"), UiKit.Button("Reproduzir / pausar", () => EditMaterial(m => { if (m["media"] is JsonObject media) media["playing"] = !(media["playing"]?.GetValue<bool>() ?? false); })), UiKit.Button("Tirar mídia", () => EditMaterial(m => m.Remove("media")))));

            // Auditoria
            _audit = UiKit.Text("", "lv-body"); _audit.style.whiteSpace = WhiteSpace.Normal; _tabs["Auditoria"].Add(_audit); _tabs["Auditoria"].Add(UiKit.Button("Atualizar", LoadAudit));
        }

        // ---- reading the selected object ----

        private void RefreshExtra(WorldObjectState first)
        {
            if (first == null) { if (_contentList != null) _contentList.text = ""; return; }
            var ids = _b.FacesOfSelection(); if (!ids.SequenceEqual(_faceIds)) { _faceIds = ids; if (ids.Length > 0) _face.choices = ids.Select(i => FaceName(i)).ToList(); }
            RefreshResources(first);
            if (_b.PickedFace >= 0) { var at = Array.IndexOf(_faceIds, _b.PickedFace); if (at >= 0 && _face.index != at) _face.SetValueWithoutNotify(_face.choices.ElementAt(at)); }
            var g = PrimParams.Parse(first.GeometryJson); var values = new Dictionary<string, float> { ["hollow"] = g.Hollow, ["taperX"] = g.TaperX, ["taperY"] = g.TaperY, ["twist"] = g.Twist, ["cutStart"] = g.CutStart, ["cutEnd"] = g.CutEnd, ["pathBegin"] = g.PathBegin, ["pathEnd"] = g.PathEnd, ["shearX"] = g.ShearX, ["shearZ"] = g.ShearZ, ["skew"] = g.Skew, ["revolutions"] = g.Revolutions };
            foreach (var pair in values) _shape[pair.Key].SetValueWithoutNotify(pair.Value);
            var p = PhysicsRules.Read(first.PhysicsJson); _collision.SetValueWithoutNotify(p.Collision); _physical.SetValueWithoutNotify(p.Physical); _trigger.SetValueWithoutNotify(p.Trigger); _temporary.SetValueWithoutNotify(p.Temporary); _shapeKind.SetValueWithoutNotify(p.CollisionShape == "none" ? "box" : p.CollisionShape);
            _material.SetValueWithoutNotify(PhysicsRules.Materials.TryGetValue(p.Material ?? "", out var mat) ? mat.Label : "(nenhuma)"); _mass.SetValueWithoutNotify(p.Mass); _friction.SetValueWithoutNotify(p.Friction); _bounce.SetValueWithoutNotify(p.Bounce);
            _nextCopy.SetValueWithoutNotify((first.NextOwnerRights & LifeVerse.Shared.Items.ItemRights.Copy) != 0); _nextModify.SetValueWithoutNotify((first.NextOwnerRights & LifeVerse.Shared.Items.ItemRights.Modify) != 0); _nextTransfer.SetValueWithoutNotify((first.NextOwnerRights & LifeVerse.Shared.Items.ItemRights.Transfer) != 0);
            _repeatX.SetValueWithoutNotify(first.MaterialJson.Contains("uvRepeatX") ? (float)(JsonNode.Parse(first.MaterialJson)?["uvRepeatX"]?.GetValue<double>() ?? 1) : 1f); _repeatY.SetValueWithoutNotify(first.MaterialJson.Contains("uvRepeatY") ? (float)(JsonNode.Parse(first.MaterialJson)?["uvRepeatY"]?.GetValue<double>() ?? 1) : 1f);
            { JsonObject mm = null; try { mm = JsonNode.Parse(string.IsNullOrWhiteSpace(first.MaterialJson) ? "{}" : first.MaterialJson) as JsonObject; } catch (JsonException) { }
              foreach (var pair in _mapFields) if (pair.Value.focusController?.focusedElement != pair.Value) pair.Value.SetValueWithoutNotify(mm?[pair.Key]?.ToString() ?? "");
              float Num(string k, float d) => mm != null && mm[k] is JsonValue v && v.TryGetValue<double>(out var n) ? (float)n : d; _normalStrength.SetValueWithoutNotify(Num("normalStrength", 1f)); _aoStrength.SetValueWithoutNotify(Num("aoStrength", 1f)); _offsetX.SetValueWithoutNotify(Num("uvOffsetX", 0f)); _offsetY.SetValueWithoutNotify(Num("uvOffsetY", 0f)); }
            var entries = ContentOf(first); _contentList.text = entries.Count == 0 ? "O objeto não leva nada." : string.Join("\n", entries.Select(e => "• " + e.Name + "  (" + e.Category + ")")); DrawContentTree(entries.Select(e => (e.Name, e.Category)).ToList()); RefreshPeople(first);
        }

        private sealed class ContentEntry { public Guid Asset; public string Name, Category; }
        private static List<ContentEntry> ContentOf(WorldObjectState s)
        {
            var list = new List<ContentEntry>();
            try { foreach (var item in (JsonNode.Parse(string.IsNullOrWhiteSpace(s.ContentJson) ? "[]" : s.ContentJson) as JsonArray ?? new JsonArray()).OfType<JsonObject>()) if (Guid.TryParse(item["assetId"]?.GetValue<string>(), out var asset)) list.Add(new ContentEntry { Asset = asset, Name = item["name"]?.GetValue<string>() ?? "Item", Category = item["category"]?.GetValue<string>() ?? "" }); }
            catch (Exception exception) when (exception is JsonException || exception is InvalidOperationException) { }
            return list;
        }

        // ---- editing ----

        private void EditGeometry(Action<JsonObject> change)
        {
            if (_filling || _b.Selection.Count == 0) return;
            _b.Edit(_b.Selection.ToList(), s => { JsonObject g; try { g = JsonNode.Parse(string.IsNullOrWhiteSpace(s.GeometryJson) ? "{}" : s.GeometryJson) as JsonObject ?? new JsonObject(); } catch (JsonException) { g = new JsonObject(); } change(g); s.GeometryJson = g.ToJsonString(); }, "forma");
        }

        private void EditPhysics(Func<PhysicsSettings, PhysicsSettings> change)
        {
            if (_filling || _b.Selection.Count == 0) return;
            _b.Edit(_b.Selection.ToList(), s => s.PhysicsJson = PhysicsRules.Write(s.PhysicsJson, change(PhysicsRules.Read(s.PhysicsJson))), "física");
        }

        /// <summary>The "properties" command: only what is in the object changes, and the island decides who may.</summary>
        public bool SendProperties(JsonObject properties)
        {
            if (_b.Selection.Count == 0) { Say("Selecione um objeto.", true); return false; }
            var ok = _b.SendProperties(_b.Selection.ToList(), properties); if (ok) Say("Enviado.", false); return ok;
        }

        private void SendNextRights() { if (_filling) return; SendProperties(new JsonObject { ["nextOwnerRights"] = (_nextModify.value ? LifeVerse.Shared.Items.ItemRights.Modify : 0) | (_nextCopy.value ? LifeVerse.Shared.Items.ItemRights.Copy : 0) | (_nextTransfer.value ? LifeVerse.Shared.Items.ItemRights.Transfer : 0) }); }

        /// <summary>Checks the sale: null when fine, otherwise why not.</summary>
        public static string SaleProblem(string mode, string price, out long amount)
        {
            amount = 0; if (!SaleModes.Any(m => m.Id == mode)) return "Escolha o que vende: o original, uma cópia ou o conteúdo.";
            if (!long.TryParse((price ?? "").Trim(), out amount) || amount < 0 || amount > 10_000_000) return "Preço inválido: use um número inteiro de 0 a 10.000.000.";
            return null;
        }

        public void PutOnSale()
        {
            var mode = SaleModes.First(s => s.Label == _sale.value).Id; var problem = SaleProblem(mode, _price.value, out var amount); if (problem != null) { Say(problem, true); return; }
            SendProperties(new JsonObject { ["saleType"] = mode, ["salePrice"] = (int)amount });
        }

        private void AddContent()
        {
            if (_b.Selection.Count != 1) { Say("Selecione um só objeto para mudar o conteúdo.", true); return; } if (!Guid.TryParse(_contentAsset.value.Trim(), out var asset)) { Say("Digite o identificador do item (ou escolha do inventário).", true); return; }
            AddContentEntry(asset, "Item", "objects");
        }

        public void AddContentEntry(Guid asset, string name, string category)
        {
            _b.Edit(_b.Selection.ToList(), s =>
            {
                JsonArray array; try { array = JsonNode.Parse(string.IsNullOrWhiteSpace(s.ContentJson) ? "[]" : s.ContentJson) as JsonArray ?? new JsonArray(); } catch (JsonException) { array = new JsonArray(); }
                if (array.Count >= 64 || array.OfType<JsonObject>().Any(x => x["assetId"]?.GetValue<string>() == asset.ToString())) return;
                array.Add(new JsonObject { ["assetId"] = asset.ToString(), ["name"] = name.Length > 120 ? name.Substring(0, 120) : name, ["category"] = category }); s.ContentJson = array.ToJsonString();
            }, "conteúdo");
        }

        private void RemoveContent()
        {
            if (!Guid.TryParse(_contentAsset.value.Trim(), out var asset)) { Say("Digite o identificador do item que sai.", true); return; }
            _b.Edit(_b.Selection.ToList(), s => { var array = JsonNode.Parse(string.IsNullOrWhiteSpace(s.ContentJson) ? "[]" : s.ContentJson) as JsonArray ?? new JsonArray(); var entry = array.OfType<JsonObject>().FirstOrDefault(x => x["assetId"]?.GetValue<string>() == asset.ToString()); if (entry != null) { array.Remove(entry); s.ContentJson = array.ToJsonString(); } }, "conteúdo");
        }

        private void PickContent()
        {
            var window = new FloatingWindow(_c.Ui, "build-content", "Itens do inventário", 380f, 420f, new Vector2(470, 140)); var list = new ScrollView(); list.style.flexGrow = 1; window.Body.Add(list); window.Open();
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/inventory/items?view=all&page=1&pageSize=100", null), r =>
            {
                if (r.Status != 200) { list.Add(UiKit.Text(UiTasks.Explain(r.Status, r.Body), "lv-bad")); return; }
                foreach (var row in Json.Rows(r.Body)) { var asset = row.Id("assetId"); var name = row.First("name", "displayName"); var category = row.First("category", "content"); list.Add(UiKit.Button(name + (category.Length > 0 ? "  (" + category + ")" : ""), () => { _contentAsset.value = asset.ToString(); AddContentEntry(asset, name, category.Length > 0 ? category : "objects"); window.Close(); })); }
                if (list.childCount == 0) list.Add(UiKit.Text("O inventário está vazio.", "lv-muted"));
            });
        }

        // ---- aligning ----

        private List<KeyValuePair<Guid, UnityEngine.Bounds>> BoxesOfSelection() => _b.Selection.Select(id => _b.BoundsOf(id, out var box) ? new KeyValuePair<Guid, UnityEngine.Bounds>(id, box) : default).Where(p => p.Key != Guid.Empty).ToList();

        private static LifeVerse.Shared.Building.Bounds ToShared(UnityEngine.Bounds b) => new LifeVerse.Shared.Building.Bounds(new System.Numerics.Vector3(b.min.x, b.min.y, -b.max.z), new System.Numerics.Vector3(b.max.x, b.max.y, -b.min.z));   // Unity to game space: z is mirrored

        public void Align(AlignAxis axis, AlignEdge edge)
        {
            var boxes = BoxesOfSelection(); if (boxes.Count < 2) { Say("Selecione dois ou mais objetos.", true); return; }
            var moves = AlignMath.Align(boxes.Select(b => ToShared(b.Value)).ToList(), axis, edge); ApplyMoves(boxes.Select(b => b.Key).ToList(), moves, axis, "alinhar");
        }

        public void Distribute(AlignAxis axis)
        {
            var boxes = BoxesOfSelection(); if (boxes.Count < 3) { Say("Distribuir pede três ou mais objetos.", true); return; }
            var moves = AlignMath.Distribute(boxes.Select(b => ToShared(b.Value)).ToList(), axis); ApplyMoves(boxes.Select(b => b.Key).ToList(), moves, axis, "distribuir");
        }

        private void ApplyMoves(List<Guid> ids, float[] moves, AlignAxis axis, string label)
        {
            for (var i = 0; i < ids.Count && i < moves.Length; i++) { var delta = moves[i]; if (Mathf.Abs(delta) < .0005f) continue; _b.Edit(new[] { ids[i] }, s => { if (axis == AlignAxis.X) s.PositionX += delta; else if (axis == AlignAxis.Y) s.PositionY += delta; else s.PositionZ += delta; }, label); }
        }

        // ---- the history of the object ----

        private void LoadAudit()
        {
            if (_b.Selection.Count == 0) { _audit.text = "Selecione um objeto."; return; } var id = _b.Selection[0]; _audit.text = "Lendo…";
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/worlds/islands/" + _c.CurrentIsland() + "/objects/" + id + "/audit", null), r =>
            {
                if (r.Status != 200) { _audit.text = "Não foi possível ler o histórico (" + UiTasks.Explain(r.Status) + ")."; return; }
                var rows = Json.Rows(r.Body); _audit.text = rows.Count == 0 ? "Sem registros." : string.Join("\n", rows.Select(e => DiscoveryWindow.When(e.Text("at")) + "  ·  " + OperationLabel(e.Text("operation")) + " por " + (e.Id("actorUserId") == _c.Api.UserId ? "você" : e.Text("actorUserId").Length >= 8 ? e.Text("actorUserId").Substring(0, 8) : "?") + "  ·  revisão " + e.Number("revision")));
            });
        }

        public static string OperationLabel(string operation)
        {
            switch (operation) { case "create": return "Criado"; case "update": return "Alterado"; case "delete": return "Excluído"; case "return": return "Devolvido"; case "structure": return "Vínculo alterado"; case "protect": return "Proteção alterada"; case "properties": return "Propriedades alteradas"; case "buy": return "Comprado"; case "rez": return "Colocado"; default: return operation; }
        }
    }
}