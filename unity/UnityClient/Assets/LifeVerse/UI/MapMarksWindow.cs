using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// Manages the landmarks of the world map: create, rename and delete folders, and for each landmark its name, its folder and whether it is shared with everybody. Every change goes to the platform and the map reloads its places after it.
    /// </summary>
    public sealed class MapMarksWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly WorldMapWindow _map; private readonly ScrollView _list = new ScrollView(); private readonly Label _status; private readonly TextField _newFolder;
        public int Count => _list.contentContainer.childCount;
        public string Notice => _status.text;
        public TextField NewFolderField => _newFolder;
        public const int MaxFolders = 20;

        public MapMarksWindow(ApiContext context, WorldMapWindow map) : base(context.Ui, "map-marks", "Gerenciar marcos", 520f, 520f, new Vector2(300, 90))
        {
            _c = context; _map = map; _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal; Body.Add(_status);
            _newFolder = UiKit.Field("Nome da nova pasta", false, 40); Body.Add(UiKit.Row(_newFolder, UiKit.Button("Criar pasta", CreateFolder, "lv-btn-primary"))); _list.style.flexGrow = 1; _list.style.minHeight = 260; Body.Add(_list); Fill();
        }

        /// <summary>The words for what the platform answered to a change of landmarks or folders.</summary>
        public static string Words(int status, string body)
        {
            try { using var d = JsonDocument.Parse(body); var code = d.RootElement.TryGetProperty("error", out var e) ? e.GetString() : d.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
                var text = code switch { "too_many_folders" => "Você chegou ao limite de pastas.", "too_many_landmarks" => "Você chegou ao limite de marcos.", "name_taken" => "Já existe uma pasta com esse nome.", "folder_not_found" => "Essa pasta não existe mais.", "island_not_found" => "Esta ilha não está mais disponível.", _ => null }; if (text != null) return text; }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException) { }
            return UiTasks.Explain(status, body);
        }

        public void Fill()
        {
            _list.Clear(); _list.Add(UiKit.Text("Pastas (" + _map.Folders.Count + "/" + MaxFolders + ")", "lv-bold", "lv-accent"));
            foreach (var f in _map.Folders)
            {
                var folder = f; var name = UiKit.Field("Nome", false, 40, folder.Name);
                _list.Add(UiKit.Row(name, UiKit.Button("Renomear", () => Change("PATCH", "/api/v1/map/landmark-folders/" + folder.Id, JsonSerializer.Serialize(new { name = name.value.Trim() }), "Pasta renomeada.")), Controls.Confirm("Excluir", () => "Os marcos ficam soltos. Confirmar?", () => Change("DELETE", "/api/v1/map/landmark-folders/" + folder.Id, null, "Pasta excluída; os marcos ficaram soltos."))));
            }
            _list.Add(UiKit.Text("Marcos (" + _map.Marks.Count + "/" + _map.MaxLandmarks + ")", "lv-bold", "lv-accent")); if (_map.Marks.Count == 0) _list.Add(UiKit.Text("Nenhum marco ainda.", "lv-muted", "lv-small"));
            foreach (var m in _map.Marks)
            {
                var mark = m; var box = new VisualElement(); box.AddToClassList("lv-row"); box.style.flexDirection = FlexDirection.Column; box.style.marginBottom = 6;
                var name = UiKit.Field("Nome", false, 60, mark.Name); var choices = new List<string> { "Sem pasta" }; choices.AddRange(_map.Folders.Select(f => f.Name)); var at = mark.Folder != null ? _map.Folders.FindIndex(f => f.Id == mark.Folder) + 1 : 0;
                var folder = new DropdownField("Pasta", choices, Math.Max(0, at)); var share = new Toggle("Compartilhar com todos (quem tiver o marco pode ir)") { value = mark.IsPublic };
                box.Add(UiKit.Text(mark.Name + "  ·  " + mark.IslandName, "lv-small", "lv-muted")); box.Add(name); box.Add(folder); box.Add(share);
                box.Add(UiKit.Row(UiKit.Button("Salvar", () => Save(mark, name.value, folder.index, share.value), "lv-btn-primary"), Controls.Confirm("Excluir", () => "Excluir de vez? Confirmar", () => Change("DELETE", "/api/v1/map/landmarks/" + mark.Id, null, "Marco excluído."))));
                _list.Add(box);
            }
        }

        public void CreateFolder()
        {
            var name = _newFolder.value.Trim(); if (name.Length == 0) { Say("Dê um nome à pasta.", true); return; } if (_map.Folders.Count >= MaxFolders) { Say("Você chegou ao limite de pastas.", true); return; }
            Change("POST", "/api/v1/map/landmark-folders", JsonSerializer.Serialize(new { name }), "Pasta criada."); _newFolder.value = "";
        }

        /// <summary>Saves a landmark's name, folder (0 is none) and sharing.</summary>
        public void Save(MapMark mark, string name, int folderIndex, bool isPublic)
        {
            if (name.Trim().Length == 0) { Say("O marco precisa de um nome.", true); return; } Guid? folder = folderIndex > 0 && folderIndex <= _map.Folders.Count ? _map.Folders[folderIndex - 1].Id : (Guid?)null;
            Change("PATCH", "/api/v1/map/landmarks/" + mark.Id, JsonSerializer.Serialize(new { name = name.Trim(), folderId = folder, clearFolder = folder == null, isPublic }), "Marco atualizado.");
        }

        private void Say(string text, bool bad) { _status.text = text; _status.EnableInClassList("lv-bad", bad); }

        private void Change(string method, string path, string json, string done) => this.Then(_c.Api.ApiAsync(method, path, json), r =>
        {
            if (r.Status >= 200 && r.Status < 300) { Say(done, false); _map.LoadPanels(); this.schedule.Execute(Fill).ExecuteLater(500); } else Say(Words(r.Status, r.Body), true);
        });
    }
}
