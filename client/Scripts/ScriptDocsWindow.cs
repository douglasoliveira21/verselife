using Godot;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// "Documentação" of the Lua scripts: the functions and events of the language as the server documents them (the same list the editor completes from), with a search and a filter by category. For each one: what it does, what it
/// takes and returns, the permission it needs, its limits, how it fails and an example that can be put into the script.
/// </summary>
public partial class ScriptDocsWindow : Node
{
    private sealed record Entry(string Title, string Category, string Detail, string Example);

    private FloatingWindow _window = null!;
    private LineEdit _search = null!;
    private OptionButton _category = null!;
    private ItemList _list = null!;
    private RichTextLabel _detail = null!;
    private Button _insert = null!;
    private ScriptApi? _api;
    private ScriptReference? _reference;
    private readonly List<Entry> _entries = [], _shown = [];

    /// <summary>Called with the example of the selected entry when the player asks to put it into the script.</summary>
    public Action<string>? InsertExample { get; set; }
    public bool IsOpen => _window is not null && _window.Visible;
    public IReadOnlyList<string> EntriesForTesting => [.. _shown.Select(e => e.Title)];
    public IReadOnlyList<string> CategoriesForTesting => [.. Enumerable.Range(0, _category.ItemCount).Select(i => _category.GetItemText(i))];
    public string DetailForTesting => _detail.GetParsedText();
    public Vector2 SizeForTesting => _window.Size;
    public void SearchForTesting(string text) { _search.Text = text; Apply(); }
    public void CategoryForTesting(string text) { for (var i = 0; i < _category.ItemCount; i++) if (_category.GetItemText(i) == text) { _category.Select(i); Apply(); return; } }
    public void SelectForTesting(int index) { if (index >= 0 && index < _shown.Count) { _list.Select(index); OnPicked(index); } }
    public void PressInsertForTesting() => _insert.EmitSignal(BaseButton.SignalName.Pressed);

    public void UseApi(ScriptApi api) => _api = api;

    public override void _Ready() => Build();

    private void Build()
    {
        _window = new FloatingWindow("Documentação do Lua", icon: "doc") { Name = "ScriptDocsWindow", Visible = false }; _window.Place(.5f, .5f, -440, -300, 440, 300); _window.MakeResizable(); _window.CloseRequested += () => _window.Visible = false; AddChild(_window);
        var content = _window.Content; content.AddThemeConstantOverride("separation", 8);
        var bar = new HBoxContainer(); bar.AddThemeConstantOverride("separation", 8); content.AddChild(bar);
        _search = new LineEdit { PlaceholderText = "Buscar função ou evento…", ClearButtonEnabled = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 34) }; _search.TextChanged += _ => Apply(); bar.AddChild(_search);
        _category = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(180, 34) }; _category.ItemSelected += _ => Apply(); bar.AddChild(_category);
        var row = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; row.AddThemeConstantOverride("separation", 8); content.AddChild(row);
        _list = new ItemList { CustomMinimumSize = new Vector2(280, 380), SizeFlagsVertical = Control.SizeFlags.ExpandFill }; _list.ItemSelected += index => OnPicked((int)index); row.AddChild(_list);
        var side = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; side.AddThemeConstantOverride("separation", 6); row.AddChild(side);
        _detail = new RichTextLabel { BbcodeEnabled = true, FitContent = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(420, 300), SelectionEnabled = true }; side.AddChild(_detail);
        _insert = new Button { Text = "Inserir exemplo no script", FocusMode = Control.FocusModeEnum.None, Disabled = true }; UiStyle.StyleButton(_insert, UiStyle.ButtonKind.Primary, 14, 6, 14); _insert.Pressed += () => { if (_list.GetSelectedItems() is { Length: > 0 } sel && sel[0] < _shown.Count && _shown[sel[0]].Example.Length > 0) InsertExample?.Invoke(_shown[sel[0]].Example); }; side.AddChild(_insert);
    }

    public async Task OpenAsync(ScriptReference? known = null)
    {
        _reference = known ?? (_api is not null ? await _api.ReferenceAsync() : null);
        _window.Visible = true; _window.MoveToFront();
        _entries.Clear();
        if (_reference is null) { _shown.Clear(); _list.Clear(); _detail.Text = "Não foi possível carregar a documentação agora. Tente de novo em instantes."; _insert.Disabled = true; return; }
        foreach (var f in _reference.Functions) _entries.Add(new Entry(f.Signature.Length > 0 ? f.Signature : f.Namespace + "." + f.Name, f.Category, FunctionDetail(f), f.Example));
        foreach (var e in _reference.Events) _entries.Add(new Entry(e.Name, "Eventos", $"[b]{e.Name}[/b]\n\n{e.When}\n\n[b]O que recebe (a tabela event):[/b] {e.Fields}\n\n[b]Exemplo[/b]\n[code]{Escape(e.Example)}[/code]", e.Example));
        _category.Clear(); _category.AddItem("Todas as categorias"); foreach (var category in _entries.Select(e => e.Category).Distinct()) _category.AddItem(category);
        Apply();
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");

    private static string FunctionDetail(ScriptDocFunction f) =>
        $"[b]{Escape(f.Signature)}[/b]\n\n{f.Description}\n\n[b]Retorna:[/b] {f.Returns}\n[b]Permissão:[/b] {f.Permission}\n[b]Limites:[/b] {f.Limits}\n[b]Erros possíveis:[/b] {f.Errors}\n\n[b]Exemplo[/b]\n[code]{Escape(f.Example)}[/code]";

    private void Apply()
    {
        var query = _search.Text.Trim(); var category = _category.ItemCount > 0 && _category.Selected > 0 ? _category.GetItemText(_category.Selected) : "";
        _shown.Clear(); _list.Clear(); _detail.Text = ""; _insert.Disabled = true;
        foreach (var entry in _entries.Where(e => (category.Length == 0 || e.Category == category) && (query.Length == 0 || e.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Detail.Contains(query, StringComparison.OrdinalIgnoreCase)))) { _shown.Add(entry); _list.AddItem(entry.Title); }
        if (_shown.Count == 0) _detail.Text = "Nada encontrado com essa busca.";
    }

    private void OnPicked(int index) { if (index < 0 || index >= _shown.Count) return; _detail.Text = _shown[index].Detail; _insert.Disabled = _shown[index].Example.Length == 0; }
}
