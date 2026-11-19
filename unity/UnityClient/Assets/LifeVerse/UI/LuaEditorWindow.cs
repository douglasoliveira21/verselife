using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The editor of the Lua scripts of an object: the scripts it has (listed from the API), a source field, a name and the event, a syntax check made here with the same Lua
    /// interpreter the island runs (the line and the message of the first mistake), and save (a new script, or an update of the one open). The island still validates and limits the
    /// script when it is saved; what runs, with which permissions, is its decision.
    /// </summary>
    public sealed class LuaEditorWindow : FloatingWindow
    {
        public const int MaxSourceLength = 64 * 1024;
        private readonly ApiContext _c; private readonly Guid _island; private readonly Guid _object;
        private readonly ScrollView _scripts; private readonly TextField _name, _eventName, _source; private readonly Label _status;
        private Guid _open;
        public string Source { get => _source.value; set => _source.value = value; }
        public Guid OpenScript => _open;
        public string StatusText => _status.text;

        public LuaEditorWindow(ApiContext context, Guid island, Guid obj) : base(context.Ui, "lua-" + obj.ToString("N").Substring(0, 8), "Script do objeto", 560f, 560f, new Vector2(300, 90))
        {
            _c = context; _island = island; _object = obj;
            _scripts = new ScrollView(); _scripts.style.maxHeight = 90; _scripts.style.minHeight = 40; Body.Add(_scripts);
            var head = UiKit.Row(); _name = UiKit.Field("Nome", false, 60, "script"); _eventName = UiKit.Field("Evento", false, 40, "on_start"); _name.style.flexGrow = 1; _eventName.style.width = 150; head.Add(_name); head.Add(_eventName); Body.Add(head);
            _source = UiKit.Field("", false, MaxSourceLength); _source.multiline = true; _source.style.flexGrow = 1; _source.style.minHeight = 260; _source.AddToClassList("lv-mono"); Body.Add(_source);
            _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal; Body.Add(_status);
            Body.Add(UiKit.Row(UiKit.Button("Verificar", () => Check()), UiKit.Button("Salvar", Save, "lv-btn-primary"), UiKit.Button("Novo", NewScript), UiKit.Button("Recarregar", LoadList)));
            LoadList();
        }

        private string Path => "/api/v1/worlds/" + _island + "/objects/" + _object + "/scripts";

        public void LoadList() => this.Then(_c.Api.ApiAsync("GET", Path), r =>
        {
            _scripts.Clear(); if (r.Status != 200) { Say(UiTasks.Explain(r.Status), true); return; }
            foreach (var row in Json.Rows(r.Body)) { var id = row.Id(); var name = row.First("name", "title"); _scripts.Add(UiKit.Button(name.Length > 0 ? name : id.ToString().Substring(0, 8), () => Open(id))); }
            if (_scripts.childCount == 0) _scripts.Add(UiKit.Text("Este objeto não tem scripts.", "lv-muted"));
        });

        public void Open(Guid id) => this.Then(_c.Api.ApiAsync("GET", Path + "/" + id), r =>
        {
            if (r.Status != 200) { Say(UiTasks.Explain(r.Status), true); return; }
            var row = Json.One(r.Body); _open = id; _name.value = row.Text("name"); _eventName.value = row.First("eventName", "event"); _source.value = row.First("source", "code", "body"); Say("Aberto: " + _name.value, false);
        });

        public void NewScript() { _open = Guid.Empty; _name.value = "novo_script"; _eventName.value = "on_start"; _source.value = "-- seu script em Lua\n"; Say("Novo script.", false); }

        /// <summary>The syntax check with the island's Lua interpreter. Null when the source loads; otherwise the first mistake with its line.</summary>
        public static string SyntaxProblem(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return "O script está vazio.";
            if (source.Length > MaxSourceLength) return "O script passa de " + MaxSourceLength / 1024 + " KB.";
            try { new MoonSharp.Interpreter.Script(MoonSharp.Interpreter.CoreModules.None).LoadString(source); return null; }
            catch (MoonSharp.Interpreter.SyntaxErrorException exception) { return exception.DecoratedMessage ?? exception.Message; }
            catch (MoonSharp.Interpreter.ScriptRuntimeException exception) { return exception.DecoratedMessage ?? exception.Message; }
        }

        public bool Check()
        {
            var problem = SyntaxProblem(_source.value); if (problem != null) { Say("Erro: " + problem, true); return false; }
            Say("Sintaxe correta.", false); return true;
        }

        public void Save()
        {
            if (!Check()) return;
            var body = JsonSerializer.Serialize(new { name = _name.value.Trim().Length == 0 ? "script" : _name.value.Trim(), source = _source.value, eventName = _eventName.value.Trim().Length == 0 ? "on_start" : _eventName.value.Trim() });
            this.Then(_c.Api.ApiAsync(_open == Guid.Empty ? "POST" : "PUT", _open == Guid.Empty ? Path : Path + "/" + _open, body), r =>
            {
                if (r.Status >= 200 && r.Status < 300) { var row = Json.One(r.Body); if (_open == Guid.Empty && row.Id() != Guid.Empty) _open = row.Id(); _c.Notices.Show("Script salvo.", NoticeKind.Good); Say("Salvo.", false); LoadList(); } else Say(UiTasks.Explain(r.Status), true);
            });
        }

        private void Say(string text, bool error) { _status.text = text; _status.EnableInClassList("lv-bad", error); _status.EnableInClassList("lv-muted", !error); }
    }
}
