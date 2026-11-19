using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// A small file browser (the game has no native "open file" dialog): the folder you are in with the folders and the files that match the filter, a way up, the places most used (the
    /// game's Photos folder, Pictures, Documents, Desktop, Downloads, the home folder) and a field to type a path. Choosing a file tells the caller its path and closes the window.
    /// </summary>
    public sealed class FilePickerWindow : FloatingWindow
    {
        private readonly string[] _extensions; private readonly Action<string> _chosen; private readonly ScrollView _list; private readonly TextField _path; private readonly Label _status;
        public string Folder { get; private set; }
        public int EntryCount => _list.contentContainer.childCount;
        public const long MaxBytes = 100L * 1024 * 1024;

        public FilePickerWindow(UiRoot ui, string title, string[] extensions, Action<string> chosen, string start = null) : base(ui, "file-picker", title, 520f, 480f, new Vector2(330, 90))
        {
            _extensions = extensions ?? Array.Empty<string>(); _chosen = chosen; _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal;
            var places = UiKit.Row(); places.style.flexWrap = Wrap.Wrap; foreach (var place in Places()) { var folder = place.Path; places.Add(UiKit.Button(place.Label, () => Go(folder))); } Body.Add(places);
            _path = UiKit.Field("", false, 400); _path.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Go(_path.value.Trim()); }, TrickleDown.TrickleDown); Body.Add(_path); Body.Add(_status);
            _list = new ScrollView(); _list.style.flexGrow = 1; Body.Add(_list); Go(start ?? Places().FirstOrDefault().Path ?? Application.persistentDataPath);
        }

        public static IEnumerable<(string Label, string Path)> Places()
        {
            var seen = new HashSet<string>();
            foreach (var (label, path) in new[] { ("Fotos do jogo", Path.Combine(Application.persistentDataPath, "Photos")), ("Imagens", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)), ("Documentos", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                ("Mesa", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)), ("Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")), ("Pasta pessoal", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) })
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && seen.Add(path)) yield return (label, path);
        }

        /// <summary>Whether a file name passes the filter (no filter accepts everything).</summary>
        public static bool Matches(string name, string[] extensions) => extensions == null || extensions.Length == 0 || extensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));

        public void Go(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) { _status.text = "Essa pasta não existe."; _status.AddToClassList("lv-bad"); return; }
            _status.RemoveFromClassList("lv-bad"); Folder = Path.GetFullPath(folder); _path.SetValueWithoutNotify(Folder); _list.Clear();
            try
            {
                var parent = Directory.GetParent(Folder); if (parent != null) _list.Add(Row("⬆  ..", () => Go(parent.FullName)));
                foreach (var dir in Directory.GetDirectories(Folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase)) { var name = Path.GetFileName(dir); if (name.StartsWith(".")) continue; _list.Add(Row("📁  " + name, () => Go(dir))); }
                foreach (var file in Directory.GetFiles(Folder).Where(f => Matches(f, _extensions)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) { var path = file; _list.Add(Row(Path.GetFileName(file) + "   " + Size(new FileInfo(file).Length), () => Choose(path))); }
                _status.text = _extensions.Length > 0 ? "Arquivos: " + string.Join(", ", _extensions) : "";
            }
            catch (UnauthorizedAccessException) { _status.text = "Sem permissão para ler esta pasta."; _status.AddToClassList("lv-bad"); }
            catch (IOException exception) { _status.text = exception.Message; _status.AddToClassList("lv-bad"); }
        }

        private static string Size(long bytes) => bytes < 1024 ? bytes + " B" : bytes < 1024 * 1024 ? (bytes / 1024) + " KB" : (bytes / 1024f / 1024f).ToString("F1") + " MB";

        private static Button Row(string text, Action onClick) { var button = UiKit.Button(text, onClick, "lv-btn-flat"); button.style.unityTextAlign = TextAnchor.MiddleLeft; return button; }

        public void Choose(string path)
        {
            if (!File.Exists(path)) { _status.text = "O arquivo não existe mais."; return; }
            if (new FileInfo(path).Length > MaxBytes) { _status.text = "O arquivo passa de 100 MB, o limite da plataforma."; _status.AddToClassList("lv-bad"); return; }
            Close(); _chosen(path);
        }
    }

    public static class Uploads
    {
        public static readonly string[] ImageFilter = { ".png", ".jpg", ".jpeg", ".webp" };
        public static readonly string[] ModelFilter = { ".glb", ".gltf", ".dae" };

        /// <summary>Lets the person pick a file and sends it to the platform; <paramref name="done"/> gets the identifier of the new asset (and its storage key), or the reason it failed.</summary>
        public static void PickAndUpload(ApiContext context, string title, string[] filter, string category, Action<UploadResult> done, string start = null)
        {
            new FilePickerWindow(context.Ui, title, filter, path => Send(context, path, category, done), start).Open();
        }

        public static void Send(ApiContext context, string path, string category, Action<UploadResult> done)
        {
            byte[] bytes; try { bytes = File.ReadAllBytes(path); } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { done(new UploadResult { Message = "Não foi possível ler o arquivo: " + exception.Message }); return; }
            context.Notices.Show("Enviando " + Path.GetFileName(path) + "…", NoticeKind.Info, 3f);
            context.Ui.Root.Then(context.Api.UploadAsync(Path.GetFileName(path), bytes, category), r => done(Read(r.Status, r.Body)));
        }

        /// <summary>What the platform answered to an upload: the asset id (and the storage key when it gave one), or the reason it refused.</summary>
        public static UploadResult Read(int status, string body)
        {
            if (status >= 200 && status < 300) { var row = Json.One(body); var asset = row.Has("asset") ? new Row(row.Element.GetProperty("asset")) : row; return new UploadResult { Ok = true, AssetId = asset.Id("id", "assetId"), StorageKey = asset.First("storageKey", "key", "storagePath"), Name = asset.First("name") }; }
            var message = status == 409 ? "Seu espaço no inventário está cheio." : status == 400 || status == 422 ? "A plataforma não aceitou o arquivo: " + Reason(body) : UiTasks.Explain(status); return new UploadResult { Message = message };
        }

        private static string Reason(string body)
        {
            try { using var document = System.Text.Json.JsonDocument.Parse(body); var root = document.RootElement; if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == System.Text.Json.JsonValueKind.String) return detail.GetString();
                if (root.TryGetProperty("errors", out var errors)) foreach (var field in errors.EnumerateObject()) foreach (var message in field.Value.EnumerateArray()) return message.GetString(); } catch (System.Text.Json.JsonException) { }
            return "confira o tipo e o tamanho.";
        }
    }

    public sealed class UploadResult { public bool Ok; public Guid AssetId; public string StorageKey = "", Name = "", Message = ""; }
}
