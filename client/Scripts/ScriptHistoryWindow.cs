using Godot;

namespace LifeVerse.Client.Scripts;

/// <summary>"Histórico" of a script: the versions of its code that were kept (each save keeps the one it replaced), a preview of the code of the one picked and a button to go back to it.</summary>
public partial class ScriptHistoryWindow : Node
{
    private FloatingWindow _window = null!;
    private ItemList _list = null!;
    private TextEdit _preview = null!;
    private Button _restore = null!;
    private Label _note = null!;
    private ScriptApi? _api;
    private Guid _object, _script;
    private IReadOnlyList<ScriptVersionView> _versions = [];

    /// <summary>Called after a version was restored, so the editor can show the code that is now current.</summary>
    public Func<Task>? Restored { get; set; }
    public bool IsOpen => _window is not null && _window.Visible;
    public int VersionCountForTesting => _versions.Count;
    public string PreviewForTesting => _preview.Text;
    public string NoteForTesting => _note.Text;
    public bool CanRestoreForTesting => !_restore.Disabled;
    public Task PickForTesting(int index) { if (index < 0 || index >= _versions.Count) return Task.CompletedTask; _list.Select(index); return ShowAsync(index); }
    public Task PressRestoreForTesting() => RestoreAsync();

    public void UseApi(ScriptApi api) => _api = api;
    public override void _Ready() => Build();

    private void Build()
    {
        _window = new FloatingWindow("Histórico do script", icon: "code") { Name = "ScriptHistoryWindow", Visible = false }; _window.Place(.5f, .5f, -420, -280, 420, 280); _window.MakeResizable(); _window.CloseRequested += () => _window.Visible = false; AddChild(_window);
        var content = _window.Content; content.AddThemeConstantOverride("separation", 8);
        _note = new Label { Text = "", AutowrapMode = TextServer.AutowrapMode.WordSmart }; _note.AddThemeFontSizeOverride("font_size", 13); content.AddChild(_note);
        var row = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; row.AddThemeConstantOverride("separation", 8); content.AddChild(row);
        _list = new ItemList { CustomMinimumSize = new Vector2(260, 360), SizeFlagsVertical = Control.SizeFlags.ExpandFill }; _list.ItemSelected += index => _ = ShowAsync((int)index); row.AddChild(_list);
        var side = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; side.AddThemeConstantOverride("separation", 6); row.AddChild(side);
        _preview = new TextEdit { Editable = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(420, 300) }; side.AddChild(_preview);
        _restore = new Button { Text = "Restaurar esta versão", FocusMode = Control.FocusModeEnum.None, Disabled = true, TooltipText = "O código desta versão passa a ser o atual; o código de agora também fica no histórico" }; UiStyle.StyleButton(_restore, UiStyle.ButtonKind.Primary, 14, 6, 14); _restore.Pressed += () => _ = RestoreAsync(); side.AddChild(_restore);
    }

    public async Task OpenAsync(Guid objectId, Guid scriptId)
    {
        _object = objectId; _script = scriptId; _window.Visible = true; _window.MoveToFront(); _preview.Text = ""; _restore.Disabled = true; _list.Clear();
        var found = _api is null ? null : await _api.VersionsAsync(objectId, scriptId);
        if (found is not { } history) { _versions = []; _note.Text = "Não foi possível ler o histórico."; return; }
        _versions = history.Versions;
        _note.Text = _versions.Count == 0 ? $"Versão atual: {history.Current}. Ainda não há versões anteriores: cada vez que você salva, o código que estava antes fica guardado aqui (as últimas 20)." : $"Versão atual: {history.Current}. Escolha uma versão anterior para ver o código e voltar a ele.";
        foreach (var v in _versions) _list.AddItem($"Versão {v.Version} · {v.Lines} linhas · {Stamp(v.SavedAt)}");
    }

    private static string Stamp(string iso) => DateTimeOffset.TryParse(iso, out var at) ? at.ToLocalTime().ToString("dd/MM HH:mm") : "";

    private async Task ShowAsync(int index)
    {
        if (_api is null || index < 0 || index >= _versions.Count) return;
        _restore.Disabled = true; var source = await _api.VersionSourceAsync(_object, _script, _versions[index].Version);
        if (!_list.IsSelected(index)) return;
        _preview.Text = source ?? "Não foi possível ler esta versão."; _restore.Disabled = source is null;
    }

    private async Task RestoreAsync()
    {
        var picked = _list.GetSelectedItems(); if (_api is null || picked.Length == 0) return;
        var version = _versions[picked[0]].Version; _restore.Disabled = true;
        var result = await _api.RestoreVersionAsync(_object, _script, version); _note.Text = result.Message;
        if (!result.Ok) { _restore.Disabled = false; return; }
        if (Restored is not null) await Restored();
        await OpenAsync(_object, _script); _note.Text = result.Message;
    }
}
