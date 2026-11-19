using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// Events, places and photos. Eventos: the public events (join, leave), the person's own (cancel, with a second touch) and a form to create one (name, description, when it starts and
    /// ends, capacity). Marcos: the saved places (go there, delete) and a button that saves where the person is now. Fotos: take a picture of the game (the interface is hidden for the
    /// shot) saved on this computer, and the list of photos the platform has. Uploading a photo to the platform needs the asset upload and comes with the item tools.
    /// </summary>
    public sealed class DiscoveryWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly Label _status; private readonly Dictionary<string, VisualElement> _tabs = new Dictionary<string, VisualElement>();
        private readonly ApiList _events, _mine, _landmarks, _photos; private readonly TextField _evName, _evAbout, _evCapacity, _evHours, _markName;
        private readonly Func<Vector3> _where; private readonly Func<float> _heading; private readonly MonoBehaviour _runner;
        public string CurrentTab { get; private set; }
        public int EventCount => _events.Count;
        public int MineCount => _mine.Count;
        public int LandmarkCount => _landmarks.Count;
        public int PhotoCount => _photos.Count;
        public string LastPhotoPath { get; private set; }

        public DiscoveryWindow(ApiContext context, MonoBehaviour runner, Func<Vector3> where, Func<float> heading) : base(context.Ui, "discovery", "Eventos, marcos e fotos", 480f, 540f, new Vector2(380, 70))
        {
            _c = context; _runner = runner; _where = where; _heading = heading; var bar = UiKit.Row(); _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal;
            foreach (var name in new[] { "Eventos", "Meus eventos", "Marcos", "Fotos" }) { var captured = name; bar.Add(UiKit.Button(name, () => Show(captured))); var tab = new VisualElement(); tab.style.flexGrow = 1; tab.style.display = DisplayStyle.None; _tabs[name] = tab; }
            Body.Add(bar); Body.Add(_status);
            _events = new ApiList(context, () => "/api/v1/discovery/events", row => { var id = row.Id(); return UiKit.Row(Info(row), UiKit.Button("Participar", () => Act("POST", "/api/v1/discovery/events/" + id + "/join", null, "Você vai ao evento.", () => _events.Reload()), "lv-btn-primary"), UiKit.Button("Sair", () => Act("POST", "/api/v1/discovery/events/" + id + "/leave", null, null, () => _events.Reload()))); }, "Nenhum evento marcado.", false);
            _tabs["Eventos"].Add(_events);
            _mine = new ApiList(context, () => "/api/v1/discovery/events/mine", row => { var id = row.Id(); return UiKit.Row(Info(row), Controls.Confirm("Cancelar", () => "Confirmar o cancelamento", () => Act("POST", "/api/v1/discovery/events/" + id + "/cancel", null, "Evento cancelado.", () => _mine.Reload()), "lv-btn-secondary")); }, "Você não marcou eventos.", false);
            _evName = UiKit.Field("Nome do evento", false, 80); _evAbout = UiKit.Field("Descrição", false, 300); _evCapacity = UiKit.Field("Lotação", false, 5, "50"); _evHours = UiKit.Field("Começa daqui a (horas)", false, 4, "1");
            _tabs["Meus eventos"].Add(_mine); foreach (var f in new VisualElement[] { _evName, _evAbout, _evCapacity, _evHours, UiKit.Button("Marcar evento nesta ilha", CreateEvent, "lv-btn-primary") }) _tabs["Meus eventos"].Add(f);
            _landmarks = new ApiList(context, () => "/api/v1/discovery/landmarks", row => { var id = row.Id(); var island = row.Id("islandId"); return UiKit.Row(UiKit.Text(row.First("name"), "lv-bold"), UiKit.Spacer(), UiKit.Button("Ir", () => GoTo(island), "lv-btn-primary"), Controls.Confirm("Apagar", () => "Confirmar", () => Act("DELETE", "/api/v1/discovery/landmarks/" + id, null, null, () => _landmarks.Reload()))); }, "Nenhum marco salvo.", false);
            _markName = UiKit.Field("Nome do marco", false, 60); _tabs["Marcos"].Add(_landmarks); _tabs["Marcos"].Add(_markName); _tabs["Marcos"].Add(UiKit.Button("Salvar onde estou", SaveLandmark, "lv-btn-primary"));
            _photos = new ApiList(context, () => "/api/v1/discovery/photos", row => UiKit.Row(UiKit.Text(row.First("metadataJson", "storageKey", "id"), "lv-muted", "lv-small"), UiKit.Spacer(), UiKit.Text(row.Flag("isPublic") ? "pública" : "privada", "lv-dim", "lv-small")), "A plataforma não tem fotos suas.", false);
            _tabs["Fotos"].Add(UiKit.Button("Tirar uma foto", TakePhoto, "lv-btn-primary")); _tabs["Fotos"].Add(UiKit.Button("Abrir a pasta das fotos", () => Application.OpenURL(PhotoFolder))); _tabs["Fotos"].Add(UiKit.Button("Enviar uma foto à plataforma…", () => PickAndSendPhoto(), "lv-btn-secondary")); _tabs["Fotos"].Add(_photos);
            foreach (var tab in _tabs.Values) Body.Add(tab); Show("Eventos");
        }

        public static string PhotoFolder => Path.Combine(Application.persistentDataPath, "Photos");

        private static VisualElement Info(Row row)
        {
            var box = new VisualElement(); box.style.flexGrow = 1; box.Add(UiKit.Text(row.First("name", "title"), "lv-bold")); box.Add(UiKit.Text(When(row.Text("startsAt")) + (row.Number("capacity") > 0 ? "  ·  lotação " + row.Number("capacity") : ""), "lv-muted", "lv-small")); return box;
        }

        public static string When(string iso) => DateTimeOffset.TryParse(iso, out var when) ? when.ToLocalTime().ToString("dd/MM HH:mm") : "";

        public void Show(string tab)
        {
            CurrentTab = tab; foreach (var pair in _tabs) pair.Value.style.display = pair.Key == tab ? DisplayStyle.Flex : DisplayStyle.None;
            if (tab == "Eventos") _events.Reload(); else if (tab == "Meus eventos") _mine.Reload(); else if (tab == "Marcos") _landmarks.Reload(); else _photos.Reload();
        }

        private void Say(string text, bool error) { _status.text = text; _status.EnableInClassList("lv-bad", error); }
        private void Act(string method, string path, string json, string success, Action then = null) => this.Then(_c.Api.ApiAsync(method, path, json), r => { if (r.Status >= 200 && r.Status < 300) { if (success != null) _c.Notices.Show(success, NoticeKind.Good); Say("", false); then?.Invoke(); } else Say(UiTasks.Explain(r.Status), true); });

        /// <summary>The checks before an event is sent. Null when fine.</summary>
        public static string ValidateEvent(string name, string capacity, string hours, out int people, out double startInHours)
        {
            people = 0; startInHours = 0;
            if ((name ?? "").Trim().Length < 3) return "O evento precisa de um nome (pelo menos 3 letras).";
            if (!int.TryParse(capacity, out people) || people < 1 || people > 500) return "Lotação: de 1 a 500 pessoas.";
            if (!double.TryParse((hours ?? "").Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out startInHours) || startInHours < 0 || startInHours > 24 * 60) return "Começa daqui a: de 0 a 1440 horas.";
            return null;
        }

        public void CreateEvent()
        {
            var problem = ValidateEvent(_evName.value, _evCapacity.value, _evHours.value, out var people, out var hours); if (problem != null) { Say(problem, true); return; }
            var start = DateTimeOffset.UtcNow.AddHours(hours); var body = JsonSerializer.Serialize(new { name = _evName.value.Trim(), description = _evAbout.value, islandId = _c.CurrentIsland(), startsAt = start, endsAt = start.AddHours(2), capacity = people, isPublic = true, category = (string)null, imageUrl = (string)null, priceCoins = 0L });
            Act("POST", "/api/v1/discovery/events", body, "Evento marcado.", () => { _evName.value = ""; _mine.Reload(); });
        }

        public void SaveLandmark()
        {
            var name = _markName.value.Trim(); if (name.Length < 2) { Say("Dê um nome ao marco.", true); return; }
            var p = _where();
            Act("POST", "/api/v1/discovery/landmarks", JsonSerializer.Serialize(new { name, islandId = _c.CurrentIsland(), positionX = p.x, positionY = p.y, positionZ = p.z, rotationY = _heading(), isPublic = false }), "Marco salvo.", () => { _markName.value = ""; _landmarks.Reload(); });
        }

        private void GoTo(Guid island) { if (island == Guid.Empty) return; if (island == _c.CurrentIsland()) { _c.Notices.Show("Você já está nesta ilha.", NoticeKind.Info); return; } _c.EnterIsland(island); }

        /// <summary>Sends a photo to the platform: the file goes up as a texture and is then registered as a photo of the island (private until the person makes it public).</summary>
        public void PickAndSendPhoto(string path = null)
        {
            void Done(UploadResult result)
            {
                if (!result.Ok) { Say(result.Message, true); return; }
                Act("POST", "/api/v1/discovery/photos", JsonSerializer.Serialize(new { islandId = _c.CurrentIsland(), storageKey = result.StorageKey.Length > 0 ? result.StorageKey : result.AssetId.ToString(), metadataJson = JsonSerializer.Serialize(new { name = result.Name, assetId = result.AssetId, at = DateTimeOffset.UtcNow }), isPublic = false }), "Foto enviada.", () => _photos.Reload());
            }
            if (path != null) Uploads.Send(_c, path, "textures", Done); else Uploads.PickAndUpload(_c, "Escolha uma foto", Uploads.ImageFilter, "textures", Done, Directory.Exists(PhotoFolder) ? PhotoFolder : null);
        }

        /// <summary>Hides the interface for one frame, takes the picture and saves it in the Photos folder (name by the time).</summary>
        public void TakePhoto() => _runner.StartCoroutine(Shoot());

        private IEnumerator Shoot()
        {
            var root = _c.Ui.Root; root.style.display = DisplayStyle.None; yield return null; yield return new WaitForEndOfFrame();
            try { Directory.CreateDirectory(PhotoFolder); LastPhotoPath = Path.Combine(PhotoFolder, "LifeVerse-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png"); ScreenCapture.CaptureScreenshot(LastPhotoPath); }
            catch (IOException exception) { Say("Não foi possível salvar a foto: " + exception.Message, true); }
            yield return null; yield return null; root.style.display = DisplayStyle.Flex; if (LastPhotoPath != null) _c.Notices.Show("Foto salva: " + Path.GetFileName(LastPhotoPath), NoticeKind.Good);
        }

        public TextField EventName => _evName; public TextField MarkName => _markName;
    }
}
