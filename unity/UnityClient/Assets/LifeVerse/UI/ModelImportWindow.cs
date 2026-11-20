using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LifeVerse.Unity.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>A model shown turning in a small picture, in a corner of the world far from everything (its own camera and light, nothing of the island in the frame).</summary>
    public sealed class ModelPreview : IDisposable
    {
        public readonly Image Image = new Image(); public RenderTexture Texture { get; private set; }
        private readonly GameObject _stage; private GameObject _model; private int _generation;
        public const float StageY = -3600f;
        public bool Loaded { get; private set; }
        public string Problem { get; private set; } = "";

        public ModelPreview(int width, int height)
        {
            Image.style.width = width; Image.style.height = height; Image.style.borderTopLeftRadius = Image.style.borderTopRightRadius = Image.style.borderBottomLeftRadius = Image.style.borderBottomRightRadius = 12; Image.style.backgroundColor = new Color(.1f, .15f, .18f);
            Texture = new RenderTexture(width * 2, height * 2, 24, RenderTextureFormat.ARGB32) { name = "Model preview" }; Image.image = Texture;
            _stage = new GameObject("Model preview stage"); _stage.transform.position = new Vector3(0, StageY, 0);
            var cam = new GameObject("Camera"); cam.transform.SetParent(_stage.transform, false); cam.transform.localPosition = new Vector3(0f, .25f, -3.2f); cam.transform.localRotation = Quaternion.Euler(5f, 0f, 0f);
            var camera = cam.AddComponent<Camera>(); camera.targetTexture = Texture; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.1f, .15f, .18f); camera.fieldOfView = 35f; camera.nearClipPlane = .1f; camera.farClipPlane = 30f; cam.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
            var lightObject = new GameObject("Key light"); lightObject.transform.SetParent(_stage.transform, false); lightObject.transform.rotation = Quaternion.Euler(35f, 150f, 0f); var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 3.5f;
            var hd = lightObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>(); hd.SetIntensity(30000f, UnityEngine.Rendering.LightUnit.Lux);
            Image.schedule.Execute(() => { if (_model != null) _model.transform.Rotate(0f, 40f * Time.unscaledDeltaTime * .03f * 30f, 0f, Space.World); }).Every(30);
        }

        /// <summary>Shows a GLB (the bytes the platform would get), scaled to fit the picture.</summary>
        public async Task Show(byte[] glb)
        {
            var generation = ++_generation; Clear(); Loaded = false; Problem = "";
            try
            {
                var gltf = new GLTFast.GltfImport(); var ok = await gltf.LoadGltfBinary(glb, new Uri("file:///preview.glb")); if (generation != _generation) return; if (!ok) { Problem = "Não foi possível desenhar o modelo."; return; }
                var holder = new GameObject("Model"); holder.transform.SetParent(_stage.transform, false); var content = new GameObject("Content"); content.transform.SetParent(holder.transform, false);
                if (!await gltf.InstantiateMainSceneAsync(content.transform) || generation != _generation) { UnityEngine.Object.Destroy(holder); return; }
                var renderers = content.GetComponentsInChildren<Renderer>(true); if (renderers.Length == 0) { UnityEngine.Object.Destroy(holder); Problem = "O modelo não tem nada para desenhar."; return; }
                var bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds); var longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)); var factor = longest > 1e-5f ? 1.7f / longest : 1f;
                holder.transform.localScale = Vector3.one * factor; holder.transform.localPosition = -(bounds.center - _stage.transform.position) * factor; _model = holder; Loaded = true;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException)) { Problem = "Não foi possível desenhar o modelo: " + exception.Message; }
        }

        private void Clear() { if (_model != null) UnityEngine.Object.Destroy(_model); _model = null; }

        public void Dispose() { _generation++; if (_stage != null) UnityEngine.Object.Destroy(_stage); if (Texture != null) { Texture.Release(); UnityEngine.Object.Destroy(Texture); } }
    }

    /// <summary>
    /// Importing a 3D model: pick a file (GLB, glTF with its pictures and data beside it, or COLLADA .dae), see what is in it (size, triangles, materials, pictures, parts, animations) and whether it is within the limits, see it turn, choose how big it
    /// is in the world and what its collision is, and place it. A glTF and a DAE are made into one self-contained GLB here, reading only files beside the one chosen; the platform checks the file again when it arrives.
    /// </summary>
    public sealed class ModelImportWindow : FloatingWindow
    {
        public static readonly (string Id, string Label)[] Shapes = { ("triangle", "Exata (a pessoa entra nas casas)"), ("convex", "Convexa (um pacote em volta)"), ("box", "Caixa"), ("none", "Sem colisão") };
        private readonly ApiContext _c; private readonly BuildEditor _b; private readonly Label _file, _size, _triangles, _materials, _textures, _parts, _animations, _problem, _status; private readonly TextField _worldSize; private readonly DropdownField _shape; private readonly Button _place; private readonly ModelPreview _preview;
        public ModelReport Report { get; private set; }
        public byte[] Glb { get; private set; }
        public string FileName { get; private set; } = "";
        public IReadOnlyList<string> Warnings { get; private set; } = new List<string>();
        public string ProblemText => _problem.text;
        public string StatusText => _status.text;
        public string SizeText => _size.text;
        public string TrianglesText => _triangles.text;
        public bool CanPlace => _place.enabledSelf;
        public ModelPreview Preview => _preview;
        public TextField WorldSizeField => _worldSize;
        public UploadResult Uploaded { get; private set; }
        public Guid PlacedAsset { get; private set; }
        public static float PlacementSize = 2f; public static string CollisionShape = "triangle";

        public ModelImportWindow(ApiContext context, BuildEditor editor) : base(context.Ui, "model-import", "Importar um modelo 3D", 460f, 640f, new Vector2(120, 40))
        {
            _c = context; _b = editor; Body.style.flexGrow = 1; Body.style.minHeight = 0; style.overflow = Overflow.Hidden;
            Body.Add(UiKit.Text("GLB, glTF (com as imagens e os dados ao lado) ou COLLADA (.dae). O modelo vira um arquivo só, que a plataforma confere de novo ao receber.", "lv-muted", "lv-small")); Body.Add(UiKit.Button("Escolher o arquivo…", () => new FilePickerWindow(_c.Ui, "Escolha o modelo", Uploads.ModelFilter, Load).Open(), "lv-btn-primary"));
            _file = UiKit.Text("Nenhum arquivo escolhido.", "lv-bold"); Body.Add(_file); _preview = new ModelPreview(300, 220); _preview.Image.style.alignSelf = Align.Center; _preview.Image.style.marginTop = 6; _preview.Image.style.flexShrink = 0; Body.Add(_preview.Image);
            _size = Row("Medidas"); _triangles = Row("Triângulos"); _materials = Row("Materiais"); _textures = Row("Imagens"); _parts = Row("Partes"); _animations = Row("Animações");
            _problem = UiKit.Text("", "lv-small", "lv-bad"); _problem.style.whiteSpace = WhiteSpace.Normal; _problem.style.flexShrink = 0; Body.Add(_problem);
            _worldSize = UiKit.Field("Tamanho no mundo (metros, a maior medida)", false, 6, PlacementSize.ToString("0.##", CultureInfo.InvariantCulture)); Body.Add(_worldSize);
            _shape = new DropdownField("Colisão", Shapes.Select(s => s.Label).ToList(), Math.Max(0, Array.FindIndex(Shapes, s => s.Id == CollisionShape))); Body.Add(_shape);
            _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal; _status.style.flexShrink = 0; Body.Add(_status);
            _place = UiKit.Button("Colocar na ilha", Place, "lv-btn-primary"); _place.SetEnabled(false); Body.Add(UiKit.Row(_place, UiKit.Button("Fechar", Close)));
            Closed += () => _preview.Dispose();
        }

        private Label Row(string name) { var value = UiKit.Text("—", "lv-body"); var row = UiKit.Row(UiKit.Text(name, "lv-muted"), UiKit.Spacer(), value); row.style.flexShrink = 0; Body.Add(row); return value; }

        /// <summary>Reads a file of the person's computer: GLB as it is, glTF gathered with what it points to, DAE converted.</summary>
        public void Load(string path)
        {
            byte[] bytes; try { bytes = File.ReadAllBytes(path); } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { Fail("Não foi possível ler o arquivo: " + exception.Message); return; }
            Show(path, bytes);
        }

        public void Show(string path, byte[] bytes)
        {
            FileName = Path.GetFileName(path); _file.text = FileName; _status.text = ""; Warnings = new List<string>(); var extension = Path.GetExtension(path).ToLowerInvariant(); byte[] glb; string error;
            if (extension == ".glb") { glb = bytes; error = null; }
            else if (extension == ".gltf") { if (!GltfUploadPackager.TryPack(path, bytes, out glb, out error)) { Fail(error); return; } }
            else if (extension == ".dae")
            {
                var folder = Path.GetFullPath(Path.GetDirectoryName(path) ?? ".");
                Func<string, byte[]> readImage = name => { try { var full = Path.GetFullPath(Path.Combine(folder, Uri.UnescapeDataString(name ?? "").Replace('/', Path.DirectorySeparatorChar))); return full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? File.ReadAllBytes(full) : null; } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) { return null; } };   // only pictures beside the model
                if (!ColladaConverter.TryConvert(bytes, readImage, out glb, out error, out var warnings)) { Fail(error); return; } Warnings = warnings;
            }
            else { Fail("Use um arquivo .glb, .gltf ou .dae."); return; }
            Glb = glb; var report = ModelInspector.Inspect(glb); Report = report; var pt = CultureInfo.GetCultureInfo("pt-BR");
            _size.text = ModelInspector.SizeText(report.Size); _triangles.text = report.Triangles.ToString("N0", pt); _materials.text = report.Materials.ToString(pt); _textures.text = report.Textures.ToString(pt); _parts.text = report.Parts.ToString(pt); _animations.text = report.Animations.ToString(pt);
            _problem.text = report.Error ?? (Warnings.Count > 0 ? "Avisos: " + string.Join(" ", Warnings.Take(3)) : ""); _problem.EnableInClassList("lv-bad", report.Error != null); _place.SetEnabled(report.Valid);
            if (report.Valid) { _ = _preview.Show(glb); }
        }

        private void Fail(string text) { Report = null; Glb = null; _problem.text = text; _problem.AddToClassList("lv-bad"); _place.SetEnabled(false); _status.text = ""; }

        /// <summary>Checks the size the person typed: null when fine.</summary>
        public static string SizeProblem(string text, out float meters)
        {
            meters = 0; if (!float.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out meters) || float.IsNaN(meters) || float.IsInfinity(meters)) return "Digite o tamanho em metros.";
            return meters < .1f || meters > 64f ? "O tamanho vai de 0,1 a 64 metros." : null;
        }

        /// <summary>The scale of the object that makes the longest side of the model the size chosen (the world draws a model one unit long on its longest side, then the object's scale stretches it).</summary>
        public static float ScaleFor(float meters) => Mathf.Clamp(meters, .1f, 64f);

        /// <summary>Uploads the model and, once the platform has it, asks the island to place it in front of the person.</summary>
        public void Place()
        {
            if (Glb == null || Report == null || !Report.Valid) return; var problem = SizeProblem(_worldSize.value, out var meters); if (problem != null) { _status.text = problem; _status.AddToClassList("lv-bad"); return; }
            PlacementSize = meters; CollisionShape = Shapes[Math.Max(0, _shape.index)].Id; _place.SetEnabled(false); _status.text = "Enviando…"; _status.RemoveFromClassList("lv-bad");
            var name = Path.GetFileNameWithoutExtension(FileName); this.Then(_c.Api.UploadAsync(name + ".glb", Glb, "models"), r =>
            {
                Uploaded = Uploads.Read(r.Status, r.Body); if (!Uploaded.Ok) { _status.text = Uploaded.Message; _status.AddToClassList("lv-bad"); _place.SetEnabled(true); return; }
                PlacedAsset = Uploaded.AssetId; _status.text = "Modelo enviado. Pedindo à ilha para colocá-lo…";
                var state = BuildCommands.NewState("mesh", _b != null ? _b.PlacePoint(0f) : Vector3.zero); state.Name = name.Length == 0 ? "Modelo" : name; state.MaterialJson = "{}"; state.PhysicsJson = System.Text.Json.JsonSerializer.Serialize(new { collision = CollisionShape != "none", shape = CollisionShape });
                state.GeometryJson = System.Text.Json.JsonSerializer.Serialize(new { assetId = PlacedAsset.ToString().ToLowerInvariant() }); state.ScaleX = state.ScaleY = state.ScaleZ = ScaleFor(meters); state.PositionY += meters / 2f;
                if (_b == null || !_b.CreateState(state, made => { _status.text = "\"" + made.Name + "\" colocado."; _status.RemoveFromClassList("lv-bad"); })) { _status.text = "O modelo foi guardado no inventário, mas não deu para colocá-lo agora."; _place.SetEnabled(true); }
            });
        }
    }
}
