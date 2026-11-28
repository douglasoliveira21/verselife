using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using LifeVerse.Shared.Building;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>The top of the window: the five tools, what each offers, the line about the selection and the first-use tutorial beside the window.</summary>
    public sealed partial class BuildWindow
    {
        private LifeVerse.Shared.Camera.CameraGesture _focus = LifeVerse.Shared.Camera.CameraGesture.OrbitDolly;
        /// <summary>What a drag of the left button does while the Focus tool is chosen.</summary>
        public LifeVerse.Shared.Camera.CameraGesture FocusGesture => _focus;

        private Button _align, _import, _package; private VisualElement _tutorialBox; private Label _tutorialText; private Button _tutorialButton; private string _limitsShort = "";
        private Label _limits; private Guid _lastCreatedShape;

        /// <summary>The shapes of the Create tool that the island can make, by the button of the viewer.</summary>
        public static readonly (string Button, string Type)[] Shapes =
        {
            ("ToolCube", "cube"), ("ToolPrism", "prism"), ("ToolPyramid", "pyramid"), ("ToolCylinder", "cylinder"), ("ToolCone", "cone"), ("ToolSphere", "sphere"), ("ToolHemiSphere", "hemisphere"),
            ("ToolTorus", "torus"), ("ToolTube", "tube"), ("ToolRing", "ring"), ("ToolTree", "tree"), ("ToolGrass", "grass"),
        };

        private Button ShapeButton(BuildSlot slot)
        {
            var type = Shapes.FirstOrDefault(s => s.Button == slot.Name).Type;
            var button = new Button(() => CreateShape(type)) { tooltip = type != null ? BuildCommands.LabelOf(type) : "" }; button.AddToClassList("lv-btn"); button.AddToClassList("bt-btn"); button.AddToClassList("bt-icon-btn"); button.AddToClassList("bt-shape");
            button.Add(new BuildShapeIcon(slot.Name.Substring(4), BuildLayout.Place(slot, 10000f).width - 8f)); return button;
        }

        /// <summary>Asks the island for a new object of a shape, in front of the person.</summary>
        public void CreateShape(string type)
        {
            if (type == null) return; LifeVerse.Unity.Audio.UiSounds.Click();
            if (!_b.Create(type, null, made => _lastCreatedShape = made.Id)) Say("Entre numa ilha antes de construir.", true); else Say("Criando: " + BuildCommands.LabelOf(type) + "…", false);
        }

        private void BuildTop()
        {
            // Focus
            foreach (var (name, gesture) in new[] { ("radio zoom", LifeVerse.Shared.Camera.CameraGesture.OrbitDolly), ("radio orbit", LifeVerse.Shared.Camera.CameraGesture.Orbit), ("radio pan", LifeVerse.Shared.Camera.CameraGesture.Pan) })
                OnOption(name, () => { _focus = gesture; if (_links.Camera?.Invoke() is { } camera) camera.ToolGesture = gesture; });
            if (_ui.Slider("slider zoom") is { } zoom) { zoom.tooltip = "Aproximar e afastar a câmera"; zoom.RegisterValueChangedCallback(e => { if (!_filling) SetZoom(e.newValue); }); }
            // Move: dragging objects comes with the 3D editor
            foreach (var name in new[] { "radio move", "radio lift", "radio spin" }) _ui.Unavailable(name, "arrastar o próprio objeto chega com o editor 3D (próxima fase). Em Editar, as setas já movem.");
            // Edit
            OnOption("radio position", () => ChooseHandles(BuildEditor.GizmoMode.Move, false)); OnOption("radio rotate", () => ChooseHandles(BuildEditor.GizmoMode.Rotate, false)); OnOption("radio stretch", () => ChooseHandles(BuildEditor.GizmoMode.Scale, false)); OnOption("radio select face", () => ChooseHandles(_b.Mode, true));
            _ui.Unavailable("checkbox edit linked parts", "selecionar o conjunto inteiro e as suas partes chega com o editor 3D (próxima fase).");
            _ui.Unavailable("checkbox uniform", "esticar de um lado só chega com o editor 3D; hoje o objeto estica sempre dos dois."); _ui.Unavailable("checkbox stretch textures", "a textura acompanha o tamanho; ajustar as repetições chega com o editor 3D.");
            OnCheck("checkbox snap to grid", v => { _b.SnapToGrid = v; _links.Remember?.Invoke("build.snap", v ? "true" : "false"); });
            OnPick("combobox grid mode", i => { _b.LocalAxes = i == 1; _links.Remember?.Invoke("build.space", i == 1 ? "local" : "world"); });
            if (_ui.Combo("combobox grid mode") is { } grid) grid.tooltip = "De que lado as setas de mover apontam: do mundo ou do objeto";
            OnPress("link_btn", () => { if (!_b.Link(true)) Say("Selecione pelo menos dois objetos (o primeiro é o pai).", true); }); OnPress("unlink_btn", () => _b.Link(false));
            if (_ui.Button("link_btn") is { } join) { join.AddToClassList("lv-btn-primary"); join.tooltip = "Vincular a seleção (o primeiro escolhido é o pai)"; }
            _align = Extra("build-align", "Alinhar…", new Rect(302, 228, 112, 27), OpenAlign);
            // Create
            foreach (var name in new[] { "ToolTetrahedron", "ToolHemiCylinder", "ToolHemiCone" }) _ui.Unavailable(name, "esta forma precisa ser criada na API e no servidor de ilha.");
            _ui.Unavailable("checkbox sticky", "a ferramenta de criar sempre continua escolhida."); _ui.Check("checkbox sticky")?.SetValueWithoutNotify(true);
            foreach (var name in new[] { "checkbox copy selection", "checkbox copy centers", "checkbox copy rotates" }) _ui.Unavailable(name, "criar cópias clicando no mundo chega com o editor 3D. Para copiar agora: menu ⋯ › Copiar a seleção (Ctrl+D).");
            _import = Extra("build-import", "Importar modelo…", new Rect(14, 226, 160, 29), () => new ModelImportWindow(_c, _b).Open());
            _package = Extra("build-place", "Do inventário…", new Rect(182, 226, 150, 29), PickPackage);

            // the first-use tutorial, beside the window
            _tutorialBox = new VisualElement { name = "build-tutorial" }; _tutorialBox.AddToClassList("lv-window"); _tutorialBox.style.left = _ui.Sheet.Size.x + 12f; _tutorialBox.style.top = 0; _tutorialBox.style.width = 300f; _tutorialBox.style.paddingLeft = _tutorialBox.style.paddingRight = 12; _tutorialBox.style.paddingTop = _tutorialBox.style.paddingBottom = 8;
            _tutorialText = UiKit.Text("", "lv-small"); _tutorialText.style.whiteSpace = WhiteSpace.Normal; _tutorialBox.Add(_tutorialText);
            _tutorialButton = UiKit.Button("Pular", () => { _b.Tutorial.Skip(); RefreshTutorial(); }); _tutorialButton.style.alignSelf = UnityEngine.UIElements.Align.FlexEnd; _tutorialBox.Add(_tutorialButton); Add(_tutorialBox);
        }

        private Button Extra(string name, string text, Rect area, Action run)
        {
            var button = UiKit.Button(text, run); button.AddToClassList("bt-btn"); _ui.Put(name, button, area, _top); return button;
        }

        private void ChooseHandles(BuildEditor.GizmoMode mode, bool face)
        {
            _b.Mode = mode; _b.FacePick = face; _b.Handles = !face; _message = "";
            if (face && CurrentTab != "Textura") Show("Textura");
        }

        private void SetZoom(float value)
        {
            if (_links.Camera?.Invoke() is not { } camera) return;
            var distance = Mathf.Exp(Mathf.Lerp(Mathf.Log(FollowCamera.MaxDistance), Mathf.Log(FollowCamera.MinDistance + .3f), value));
            if (camera.ZoomTarget.HasValue) camera.ZoomDistance = distance; else camera.Distance = Mathf.Clamp(distance, FollowCamera.MinDistance, FollowCamera.MaxDistance);
        }

        private float ZoomValue()
        {
            if (_links.Camera?.Invoke() is not { } camera) return .5f; var distance = camera.ZoomTarget.HasValue ? camera.ZoomDistance : camera.Distance;
            return Mathf.InverseLerp(Mathf.Log(FollowCamera.MaxDistance), Mathf.Log(FollowCamera.MinDistance + .3f), Mathf.Log(Mathf.Max(.1f, distance)));
        }

        private void RefreshTop(List<WorldObjectState> all, WorldObjectState first)
        {
            foreach (var (slot, mode, _, _) in ModeButtons) _ui.Button(slot)?.EnableInClassList("lv-btn-selected", Mode == mode);
            foreach (var slot in _ui.Sheet.Slots) if (slot.Mode != null) _ui.Visible(slot.Name, ModeName(Mode) == slot.Mode && !slot.Hidden);
            Options(new[] { "radio zoom", "radio orbit", "radio pan" }, n => n == (_focus == LifeVerse.Shared.Camera.CameraGesture.Orbit ? "radio orbit" : _focus == LifeVerse.Shared.Camera.CameraGesture.Pan ? "radio pan" : "radio zoom"));
            _ui.Slider("slider zoom")?.SetValueWithoutNotify(ZoomValue()); _ui.Enable("slider zoom", _links.Camera != null);
            Options(new[] { "radio move" }, _ => true);
            Options(new[] { "radio position", "radio rotate", "radio stretch", "radio select face" }, n => n == (_b.FacePick ? "radio select face" : _b.Mode == BuildEditor.GizmoMode.Rotate ? "radio rotate" : _b.Mode == BuildEditor.GizmoMode.Scale ? "radio stretch" : "radio position"));
            _ui.Check("checkbox snap to grid")?.SetValueWithoutNotify(_b.SnapToGrid); Choice("combobox grid mode", new[] { "Mundo", "Local" }, _b.LocalAxes ? 1 : 0, true);
            _ui.Enable("link_btn", all.Count >= 2); _ui.Enable("unlink_btn", all.Any(s => s.ParentObjectId != null || s.GroupId != null));
            _align.style.display = Mode == BuildMode.Edit && all.Count >= 2 ? DisplayStyle.Flex : DisplayStyle.None;
            var creating = Mode == BuildMode.Create && all.Count == 0; _import.style.display = _package.style.display = creating ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var (button, type) in Shapes) _ui.Button(button)?.EnableInClassList("lv-btn-selected", first != null && first.Id == _lastCreatedShape && first.PrimitiveType == type);
            // the line about the selection
            var land = Mode == BuildMode.Land;
            _ui.Visible("cost_text_border", !land);
            _ui.Visible("selection_empty", !land && all.Count == 0 && !creating);
            _ui.Visible("selection_faces", Mode == BuildMode.Edit && _b.FacePick && first != null);
            if (_ui.Words("selection_faces") is { } faces) faces.text = _b.PickedFace >= 0 ? "Face escolhida: " + FaceName(_b.PickedFace) : "Nenhuma face escolhida: clique numa";
            _ui.Visible("selection_count", !land && all.Count > 0);
            if (_ui.Button("selection_count") is { } count) count.text = SelectionLine(all, first);
        }

        private static string ModeName(BuildMode mode) => mode == BuildMode.Focus ? "Foco" : mode == BuildMode.Move ? "Mover" : mode == BuildMode.Edit ? "Editar" : mode == BuildMode.Create ? "Criar" : "Terreno";

        private string SelectionLine(List<WorldObjectState> all, WorldObjectState first)
        {
            if (first == null) return "";
            var what = all.Count == 1 ? BuildCommands.LabelOf(first.PrimitiveType) + " · 1 objeto" : all.Count + " objetos";
            if (all.Count == 1 && _b.HeldByOther(first.Id, out var who)) return what + " · em edição por " + who;
            if (all.Count == 1 && first.EditLocked) return what + " · protegido";
            return what + (_limitsShort.Length > 0 ? " · " + _limitsShort : "");
        }

        /// <summary>What the window says about the selection.</summary>
        public string SelectionText => _b.Selection.Count == 0 ? _ui.Words("selection_empty").text : _ui.Button("selection_count").text;

        // ---- the limits of the island and of the parcel (real numbers of the platform) ----

        private string _limitsText = "";
        public string LimitsText => _limitsText;

        private void AskLimits(bool force)
        {
            var key = string.Join(",", _b.Selection) + "|" + _b.Created; if (!force && (_b.Selection.Count == 0 || key == _askedFor || Time.realtimeSinceStartup - _asked < 2f)) return;
            _askedFor = key; _asked = Time.realtimeSinceStartup; var at = _b.PlacePoint(0f);
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/worlds/islands/" + _c.CurrentIsland() + "/build-limits?x=" + at.x.ToString(CultureInfo.InvariantCulture) + "&z=" + at.z.ToString(CultureInfo.InvariantCulture), null), r =>
            {
                if (r.Status != 200) { _limitsText = "Não foi possível ler os limites (" + UiTasks.Explain(r.Status) + ")."; _limitsShort = ""; }
                else
                {
                    var row = Json.One(r.Body); var parcel = row.Child("parcel");
                    _limitsText = "Ilha: " + row.Number("objects") + " de " + row.Number("maxObjects") + " objetos; custo " + row.Number("cost") + " de " + row.Number("maxCost") + "." + (row.Flag("canBuildHere") ? "\nVocê pode construir aqui." : "\nVocê não pode construir aqui.")
                        + (parcel.Has("name") ? "\nTerreno \"" + parcel.Text("name") + "\": " + parcel.Number("objects") + " de " + parcel.Number("maxObjects") + " objetos; custo " + parcel.Number("cost") + " de " + parcel.Number("maxCost") + "." : "");
                    _limitsShort = "ilha: custo " + row.Number("cost") + " de " + row.Number("maxCost");
                }
                if (_ui.Button("selection_count") is { } count) count.text = SelectionLine(Selected(), First());
                LimitsArrived?.Invoke();
            });
        }

        private event Action LimitsArrived;

        // ---- tutorial ----

        public static string TutorialStepText(TutorialStep step)
        {
            switch (step)
            {
                case TutorialStep.Create: return "1/8 · Crie uma caixa: no modo Criar, clique no cubo.";
                case TutorialStep.Move: return "2/8 · Mova a caixa: no modo Editar, arraste uma seta, ou use as setas do teclado.";
                case TutorialStep.Resize: return "3/8 · Mude o tamanho: escolha Escala e arraste uma alça, ou digite o tamanho na aba Objeto.";
                case TutorialStep.Rotate: return "4/8 · Gire a caixa: escolha Rotação e arraste uma alça, ou digite a rotação na aba Objeto.";
                case TutorialStep.Texture: return "5/8 · Dê cor ou textura: na aba Textura, clique na cor ou na imagem.";
                case TutorialStep.Duplicate: return "6/8 · Copie a caixa: com ela escolhida, aperte Ctrl+D (ou menu ⋯ › Copiar a seleção).";
                case TutorialStep.Link: return "7/8 · Vincule duas peças: escolha as duas (Shift soma) e use Vincular.";
                case TutorialStep.Save: return "8/8 · Guarde no inventário: menu ⋯ › Guardar construção.";
                default: return "Tutorial concluído! Agora é com você. Dá para repetir pelo menu ⋯.";
            }
        }

        public string TutorialText => _tutorialText.text;

        private void RefreshTutorial()
        {
            var tutorial = _b.Tutorial; _tutorialText.text = tutorial.Skipped ? "Tutorial pulado." : TutorialStepText(tutorial.Current);
            _tutorialBox.style.display = tutorial.Active && !Minimized && Mode != BuildMode.Land ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
