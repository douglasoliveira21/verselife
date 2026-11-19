using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LifeVerse.Shared.Avatar;
using LifeVerse.Unity.Avatar;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// What the person is building in the character creator: name, base body, colours, hair and beard, the body and face sliders, and what is worn in each slot with its tint. It is
    /// checked with the very same rules the API applies (the shared <see cref="AvatarRules"/>: names 3 to 64 characters, colours #RRGGBB, sliders within their ranges, the required
    /// slots, what covers what), so most mistakes are told before anything is sent. <see cref="ToRequest"/> is the body of PUT /api/v1/users/me/avatar and <see cref="ToAppearanceJson"/>
    /// the JSON the avatar draws from (the same fields the island sends other players).
    /// </summary>
    public sealed class CharacterDraft
    {
        public string DisplayName = "", BaseModel = "human_a", SkinColor = "#C98F65", HairColor = "#2B1B14", EyeColor = "#4A3326", HairStyle = "short", BeardStyle = "none";
        public float CameraSensitivity = .003f; public bool InvertCameraY;
        public Dictionary<string, float> Body = AvatarRules.ReadSettings("{}", AvatarRules.BodyRanges), Face = AvatarRules.ReadSettings("{}", AvatarRules.FaceRanges);
        public Dictionary<string, (string Item, string Tint)> Worn = new Dictionary<string, (string, string)>();
        /// <summary>Kept as the API sent them: the custom body and wearables, outfits and attachments are not edited here.</summary>
        public string OutfitsJson = "[]", AttachmentsJson = "[]"; public Guid? CustomModelId;

        public static readonly string[] SkinPresets = { "#F2D2B6", "#E0AC82", "#C98F65", "#A56B45", "#7A4B2D", "#4E2F1D" };
        public static readonly string[] HairPresets = { "#0F0A08", "#2B1B14", "#5A3620", "#9A6B3A", "#D6B25A", "#B83A2E", "#8A8F96" };

        public static CharacterDraft Starter(string name)
        {
            var draft = new CharacterDraft { DisplayName = name }; draft.UseStarterOutfit(); return draft;
        }

        public void UseStarterOutfit()
        {
            Worn.Clear(); foreach (var (slot, key, tint) in AvatarRules.StarterOutfit(BaseModel)) Worn[slot] = (key, tint);
        }

        /// <summary>The wearable items that fit the base body in a slot (the catalog of the game).</summary>
        public IEnumerable<WearableItem> ItemsFor(string slot) => AvatarRules.ItemsFor(slot, BaseModel);

        public static CharacterDraft FromResponse(string json)
        {
            var draft = new CharacterDraft(); var a = AvatarAppearance.Parse(json);
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            string Text(string name, string fallback) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
            draft.DisplayName = Text("displayName", ""); draft.BaseModel = Text("baseModel", "human_a"); draft.SkinColor = Text("skinColor", draft.SkinColor); draft.HairColor = Text("hairColor", draft.HairColor); draft.EyeColor = Text("eyeColor", draft.EyeColor);
            draft.HairStyle = Text("hairStyle", "short"); draft.BeardStyle = Text("beardStyle", "none"); draft.Body = a.Body; draft.Face = a.Face; draft.OutfitsJson = Text("outfitsJson", "[]"); draft.AttachmentsJson = Text("attachmentsJson", "[]"); draft.CustomModelId = a.CustomModelId;
            if (root.TryGetProperty("cameraSensitivity", out var cs) && cs.TryGetSingle(out var sens)) draft.CameraSensitivity = sens; if (root.TryGetProperty("invertCameraY", out var inv) && inv.ValueKind == JsonValueKind.True) draft.InvertCameraY = true;
            foreach (var pair in a.Worn) draft.Worn[pair.Key] = (pair.Value.Item, "#" + ColorUtility.ToHtmlStringRGB(pair.Value.Tint));
            return draft;
        }

        public string BodyJson => AvatarRules.WriteSettings(Body, AvatarRules.BodyRanges);
        public string FaceJson => AvatarRules.WriteSettings(Face, AvatarRules.FaceRanges);

        public string ToRequest() => JsonSerializer.Serialize(new
        {
            displayName = DisplayName.Trim(), baseModel = BaseModel, skinColor = SkinColor, hairColor = HairColor, hairStyle = HairStyle, cameraSensitivity = CameraSensitivity, invertCameraY = InvertCameraY,
            equippedItems = Worn.Select(w => new { slot = w.Key, itemKey = w.Value.Item, tintColor = w.Value.Tint }).ToArray(), eyeColor = EyeColor, beardStyle = BeardStyle, bodySettingsJson = BodyJson, faceSettingsJson = FaceJson, outfitsJson = OutfitsJson, attachmentsJson = AttachmentsJson,
        });

        public string ToAppearanceJson() => JsonSerializer.Serialize(new
        {
            displayName = DisplayName.Trim().Length == 0 ? "Resident" : DisplayName.Trim(), baseModel = BaseModel, skinColor = SkinColor, hairColor = HairColor, eyeColor = EyeColor, hairStyle = HairStyle, beardStyle = BeardStyle, bodySettingsJson = BodyJson, faceSettingsJson = FaceJson,
            equippedItems = Worn.Select(w => new { slot = w.Key, itemKey = w.Value.Item, tintColor = w.Value.Tint }).ToArray(),
        });

        /// <summary>The problems the API would find, in Portuguese. Empty when the character can be saved.</summary>
        public List<string> Problems()
        {
            var problems = new List<string>(); var name = DisplayName?.Trim() ?? "";
            if (name.Length < 3 || name.Length > 64) problems.Add("O nome precisa ter de 3 a 64 caracteres.");
            if (!AvatarRules.BaseModels.Contains(BaseModel)) problems.Add("Escolha um corpo.");
            if (!AvatarRules.HairStyles.Contains(HairStyle)) problems.Add("Escolha um estilo de cabelo."); if (!AvatarRules.BeardStyles.Contains(BeardStyle)) problems.Add("Escolha um estilo de barba.");
            if (!Controls.IsColour(SkinColor) || !Controls.IsColour(HairColor) || !Controls.IsColour(EyeColor)) problems.Add("As cores precisam estar no formato #RRGGBB.");
            if (CameraSensitivity < .001f || CameraSensitivity > .01f) problems.Add("A sensibilidade da câmera precisa ficar entre 0,001 e 0,01.");
            if (AvatarRules.CheckSettings(BodyJson, AvatarRules.BodyRanges) is { } body) problems.Add("Medidas do corpo fora do limite (" + body + ").");
            if (AvatarRules.CheckSettings(FaceJson, AvatarRules.FaceRanges) is { } face) problems.Add("Medidas do rosto fora do limite (" + face + ").");
            foreach (var problem in AvatarRules.CheckEquipment(Worn.Select(w => (w.Key, w.Value.Item)).ToList(), null, BaseModel))
                problems.Add(problem.Code switch
                {
                    "duplicate_slot" => "Cada espaço de roupa só pode ter uma peça.", "missing_required" => "Camiseta, calça e sapatos são obrigatórios.", "unknown_item" => "Há uma peça que não existe ou não serve neste corpo (" + problem.Slot + ").",
                    "slot_covered" => "O espaço " + problem.Slot + " está coberto por " + problem.Detail + ": tire um dos dois.", "not_covered" => "O espaço " + problem.Slot + " está marcado como coberto, mas nada o cobre.", "nothing_under" => problem.Detail + " é uma camada de fora e precisa de uma camiseta por baixo.", _ => "A roupa não é válida.",
                });
            foreach (var worn in Worn) if (!Controls.IsColour(worn.Value.Tint)) { problems.Add("A cor de " + worn.Key + " precisa estar no formato #RRGGBB."); break; }
            return problems;
        }
    }

    /// <summary>
    /// The character creator: the form on the left (name, body, colours, hair, beard, the sliders of body and face, what is worn and its colour, the camera) and a live picture of the
    /// avatar turning slowly on the right (the real avatar, in a small stage with its own camera and light). It opens by itself the first time (the API asks to create the avatar
    /// before entering an island) and from the settings button afterwards. Saving sends the whole character; the API has the last word.
    /// </summary>
    public sealed class CharacterWindow : FloatingWindow
    {
        public const int PreviewWidth = 240, PreviewHeight = 340;
        private readonly ApiContext _c; private readonly Label _status; private readonly CharacterPreview _preview; private readonly bool _first;
        public CharacterDraft Draft { get; private set; }
        public event Action Saved;
        public bool IsSaving { get; private set; }
        public CharacterPreview Preview => _preview;
        private readonly VisualElement _wearBox; private readonly DropdownField _base;

        public CharacterWindow(ApiContext context, CharacterDraft draft, bool first = false) : base(context.Ui, "character", first ? "Crie o seu personagem" : "Personagem", 700f, 620f, new Vector2(240, 50))
        {
            _c = context; Draft = draft; _first = first;
            var columns = UiKit.Row(); columns.style.flexGrow = 1; columns.style.alignItems = Align.FlexStart; Body.Add(columns);
            var form = new ScrollView(); form.style.flexGrow = 1; form.style.height = 520; columns.Add(form);
            var side = new VisualElement(); side.style.width = PreviewWidth + 10; side.style.marginLeft = 10; columns.Add(side);
            _preview = new CharacterPreview(PreviewWidth, PreviewHeight); side.Add(_preview.Image); _status = UiKit.Text("", "lv-small", "lv-bad"); _status.style.whiteSpace = WhiteSpace.Normal; side.Add(_status);
            side.Add(UiKit.Button(first ? "Criar e entrar" : "Salvar", Save, "lv-btn-primary")); side.Add(UiKit.Button("Roupas iniciais", () => { Draft.UseStarterOutfit(); BuildWear(); Changed(); }));

            var name = UiKit.Field("Nome do personagem", false, 64, Draft.DisplayName); name.RegisterValueChangedCallback(e => { Draft.DisplayName = e.newValue; Changed(); }); form.Add(name);
            _base = new DropdownField("Corpo", AvatarRules.BaseModels.ToList(), Math.Max(0, Array.IndexOf(AvatarRules.BaseModels, Draft.BaseModel))); _base.RegisterValueChangedCallback(e => { Draft.BaseModel = e.newValue; BuildWear(); Changed(); }); form.Add(_base);
            form.Add(UiKit.Text("Cores", "lv-bold", "lv-accent")); form.Add(Controls.ColourField("Pele", Draft.SkinColor, v => { Draft.SkinColor = v; Changed(); }, CharacterDraft.SkinPresets)); form.Add(Controls.ColourField("Cabelo", Draft.HairColor, v => { Draft.HairColor = v; Changed(); }, CharacterDraft.HairPresets));
            form.Add(Controls.ColourField("Olhos", Draft.EyeColor, v => { Draft.EyeColor = v; Changed(); }));
            var hair = new DropdownField("Cabelo", AvatarRules.HairStyles.ToList(), Math.Max(0, Array.IndexOf(AvatarRules.HairStyles, Draft.HairStyle))); hair.RegisterValueChangedCallback(e => { Draft.HairStyle = e.newValue; Changed(); }); form.Add(hair);
            var beard = new DropdownField("Barba", AvatarRules.BeardStyles.ToList(), Math.Max(0, Array.IndexOf(AvatarRules.BeardStyles, Draft.BeardStyle))); beard.RegisterValueChangedCallback(e => { Draft.BeardStyle = e.newValue; Changed(); }); form.Add(beard);
            form.Add(UiKit.Text("Corpo", "lv-bold", "lv-accent")); foreach (var pair in AvatarRules.BodyRanges) AddSlider(form, SliderLabel(pair.Key), pair.Key, pair.Value, Draft.Body);
            form.Add(UiKit.Text("Rosto", "lv-bold", "lv-accent")); foreach (var pair in AvatarRules.FaceRanges) AddSlider(form, SliderLabel(pair.Key), pair.Key, pair.Value, Draft.Face);
            form.Add(UiKit.Text("Roupas e acessórios", "lv-bold", "lv-accent")); _wearBox = new VisualElement(); form.Add(_wearBox); BuildWear();
            form.Add(UiKit.Text("Câmera", "lv-bold", "lv-accent")); form.Add(UiKit.Slide("Sensibilidade", .001f, .01f, Draft.CameraSensitivity, v => { Draft.CameraSensitivity = v; })); form.Add(UiKit.Check("Inverter o eixo vertical", Draft.InvertCameraY, v => Draft.InvertCameraY = v));
            Changed(); Closed += _preview.Dispose;
        }

        private static string SliderLabel(string key) => key switch
        {
            "height" => "Altura", "shoulder" => "Ombros", "torso" => "Tronco", "arms" => "Braços", "legs" => "Pernas", "head" => "Cabeça", "hips" => "Quadril", "eyeScale" => "Tamanho dos olhos", "eyeSpacing" => "Distância entre os olhos",
            "noseSize" => "Nariz", "mouthWidth" => "Boca", "jawWidth" => "Mandíbula", "browHeight" => "Sobrancelhas", "headWidth" => "Largura da cabeça", _ => key,
        };

        private void AddSlider(VisualElement form, string label, string key, AvatarRules.Range range, Dictionary<string, float> store)
        {
            var slider = UiKit.Slide(label, range.Min, range.Max, store.TryGetValue(key, out var v) ? v : range.Default, value => { store[key] = value; Changed(); }); slider.name = "slider-" + key; form.Add(slider);
        }

        private void BuildWear()
        {
            _wearBox.Clear();
            foreach (var slot in new[] { "top", "jacket", "bottom", "shoes", "hat", "glasses", "neck", "ears", "gloves", "back", "accessory" })
            {
                var items = Draft.ItemsFor(slot).ToList(); if (items.Count == 0) continue; var captured = slot;
                var current = Draft.Worn.TryGetValue(slot, out var worn) ? worn.Item : items[0].Key; var index = Math.Max(0, items.FindIndex(i => i.Key == current));
                var dropdown = new DropdownField(SlotLabel(slot), items.Select(i => i.Label).ToList(), index) { name = "wear-" + slot };
                var tint = Controls.ColourField("Cor", worn.Tint ?? "#FFFFFF", v => { var key = Draft.Worn.TryGetValue(captured, out var w) ? w.Item : items[0].Key; Draft.Worn[captured] = (key, v); Changed(); });
                dropdown.RegisterValueChangedCallback(e => { var picked = items[Math.Max(0, dropdown.index)]; Draft.Worn[captured] = (picked.Key, Draft.Worn.TryGetValue(captured, out var w) ? w.Tint : "#FFFFFF"); Changed(); });
                _wearBox.Add(dropdown); _wearBox.Add(tint);
            }
        }

        private static string SlotLabel(string slot) => slot switch { "top" => "Camiseta", "jacket" => "Casaco", "bottom" => "Calça", "shoes" => "Sapatos", "hat" => "Chapéu", "glasses" => "Óculos", "neck" => "Pescoço", "ears" => "Orelhas", "gloves" => "Luvas", "back" => "Costas", _ => "Acessório" };

        /// <summary>Redraws the picture and the line of problems after any change.</summary>
        public void Changed()
        {
            _preview.Show(Draft.ToAppearanceJson()); var problems = Draft.Problems(); _status.text = problems.Count == 0 ? "" : string.Join("\n", problems.Take(3));
        }

        public void Save()
        {
            if (IsSaving) return; var problems = Draft.Problems(); if (problems.Count > 0) { _status.text = string.Join("\n", problems.Take(3)); return; }
            IsSaving = true; _status.text = "Salvando…"; _status.RemoveFromClassList("lv-bad");
            this.Then(_c.Api.ApiAsync("PUT", "/api/v1/users/me/avatar", Draft.ToRequest()), r =>
            {
                IsSaving = false;
                if (r.Status >= 200 && r.Status < 300) { _c.Notices.Show("Personagem salvo.", NoticeKind.Good); _status.text = ""; Saved?.Invoke(); if (!_first) Close(); }
                else { _status.AddToClassList("lv-bad"); _status.text = r.Status == 400 || r.Status == 422 ? "A API não aceitou o personagem: " + FirstError(r.Body) : UiTasks.Explain(r.Status); }
            });
        }

        private static string FirstError(string body)
        {
            try { using var document = JsonDocument.Parse(body); if (document.RootElement.TryGetProperty("errors", out var errors)) foreach (var field in errors.EnumerateObject()) foreach (var message in field.Value.EnumerateArray()) return message.GetString(); } catch (JsonException) { }
            return "confira os campos.";
        }
    }

    /// <summary>The picture of the character: the real avatar standing on a small stage far below the world, filmed by its own camera into a texture the window shows; it turns slowly.</summary>
    public sealed class CharacterPreview : IDisposable
    {
        public readonly Image Image = new Image(); public AvatarView View { get; private set; } public RenderTexture Texture { get; private set; } public Camera Camera { get; private set; }
        private GameObject _stage; private Light _light; private AvatarAppearance _shown;
        public const float StageY = -3000f;

        public CharacterPreview(int width, int height)
        {
            Image.style.width = width; Image.style.height = height; Image.style.borderTopLeftRadius = Image.style.borderTopRightRadius = Image.style.borderBottomLeftRadius = Image.style.borderBottomRightRadius = 12; Image.style.backgroundColor = new Color(.1f, .15f, .18f);
            Texture = new RenderTexture(width * 2, height * 2, 24, RenderTextureFormat.ARGB32) { name = "Character preview" }; Image.image = Texture;
            _stage = new GameObject("Character preview stage"); _stage.transform.position = new Vector3(0, StageY, 0);
            View = AvatarView.Create(0, "preview", Color.gray); View.transform.SetParent(_stage.transform, false); View.External = true; View.PlaceAt(Vector3.zero); View.transform.position = new Vector3(0, StageY, 0);
            var cam = new GameObject("Camera"); cam.transform.SetParent(_stage.transform, false); cam.transform.position = new Vector3(0, StageY + 1.0f, 4.4f); cam.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Camera = cam.AddComponent<Camera>(); Camera.targetTexture = Texture; Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = new Color(.1f, .15f, .18f, 1f); Camera.fieldOfView = 30f; Camera.nearClipPlane = .1f; Camera.farClipPlane = 30f; var hdCamera = cam.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>(); hdCamera.clearColorMode = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.Color; hdCamera.backgroundColorHDR = new Color(.1f, .15f, .18f, 1f);
            var lightObject = new GameObject("Key light"); lightObject.transform.SetParent(_stage.transform, false); lightObject.transform.rotation = Quaternion.Euler(35f, 150f, 0f); _light = lightObject.AddComponent<Light>(); _light.type = LightType.Directional; _light.intensity = 3.5f;
            var hd = lightObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>(); hd.SetIntensity(30000f, UnityEngine.Rendering.LightUnit.Lux);
            Image.schedule.Execute(() => { if (View != null) View.transform.rotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * .7f) * 40f, 0f); }).Every(30);
        }

        public void Show(string appearanceJson) { _shown = AvatarAppearance.Parse(appearanceJson); View.ApplyAppearance(_shown); }
        public AvatarAppearance Shown => _shown;

        public void Dispose()
        {
            if (_stage != null) UnityEngine.Object.Destroy(_stage); if (Texture != null) { Texture.Release(); UnityEngine.Object.Destroy(Texture); }
        }
    }
}
