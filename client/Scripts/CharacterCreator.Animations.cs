using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Models;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Client.Scripts;

/// <summary>One animation file as the server lists it.</summary>
public sealed record AnimationFileEntry(Guid Id, string Name, string Hash, string[] Clips);

/// <summary>
/// The "Animações" section of the character editor: a table with every state of the avatar and what plays for it (the default, an alternative of
/// the game's library, or an animation of the person's own files), a preview button for each, the import of animation files and a button that
/// saves the choices. The preview shows the choice at once on the 3D avatar of the editor.
/// </summary>
public partial class CharacterCreator
{
    private static readonly string[] AnimationOrder = ["Idle", "Walk", "Run", "Jump", "Fall", "Landing", "FlyIdle", "FlyMove", "Sit", "SitFloor", "StandUp", "Swim", "Dance", "Wave", "Applaud", "Point", "Greet", "Laugh"];
    private static readonly Dictionary<string, string> StateNames = new()
    {
        ["Idle"] = "Parado", ["Walk"] = "Andar", ["Run"] = "Correr", ["Jump"] = "Pular", ["Fall"] = "Cair", ["Landing"] = "Aterrissar", ["FlyIdle"] = "Voar parado", ["FlyMove"] = "Voar",
        ["Sit"] = "Sentar", ["SitFloor"] = "Sentar no chão", ["StandUp"] = "Levantar", ["Swim"] = "Nadar", ["Dance"] = "Dançar", ["Wave"] = "Acenar", ["Applaud"] = "Aplaudir", ["Point"] = "Apontar", ["Greet"] = "Cumprimentar", ["Laugh"] = "Rir"
    };
    private static readonly Dictionary<string, string> BuiltinNames = new()
    {
        ["relaxed"] = "Relaxado", ["alert"] = "Atento", ["happy"] = "Alegre", ["tired"] = "Cansado", ["sprint"] = "Disparada", ["groove"] = "Balançado", ["spin"] = "Giro", ["big"] = "Grande", ["bow"] = "Reverência", ["doubled"] = "Gargalhada"
    };

    private readonly Dictionary<string, OptionButton> _animationMenus = [];
    private readonly Dictionary<string, string> _animationChoice = [];
    private readonly List<AnimationFileEntry> _animationFiles = [];
    private ItemList _animationList = null!;
    private Label _animationMessage = null!;
    private FileDialog? _animationOpen, _animationSave;

    public IReadOnlyList<AnimationFileEntry> AnimationFiles => _animationFiles;
    public IReadOnlyDictionary<string, string> AnimationChoices => _animationChoice;

    private void BuildAnimationsManager(VBoxContainer fields)
    {
        fields.AddChild(new Label { Text = "Animações" });
        var hint = new Label { Text = "Escolha o que toca em cada estado. “Ver” mostra no boneco. Animações de arquivos próprios só aparecem em corpo personalizado.", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(280, 0) };
        hint.AddThemeFontSizeOverride("font_size", 14); fields.AddChild(hint);
        foreach (var state in AnimationOrder)
        {
            var row = new HBoxContainer { Name = "Animation_" + state }; fields.AddChild(row);
            row.AddChild(new Label { Text = StateNames[state], CustomMinimumSize = new Vector2(110, 0) });
            var menu = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill }; row.AddChild(menu); _animationMenus[state] = menu;
            var captured = state; menu.ItemSelected += _ => OnAnimationChosen(captured);
            var see = new Button { Text = "Ver" }; see.Pressed += () => PreviewAnimation(captured); row.AddChild(see);
        }
        var buttons = new HBoxContainer(); fields.AddChild(buttons);
        Button Add(string text, System.Action pressed) { var b = new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill }; b.Pressed += pressed; buttons.AddChild(b); return b; }
        Add("Salvar animações", SaveAnimationChoices); Add("Tudo padrão", ClearAnimationChoices);
        var files = new HBoxContainer(); fields.AddChild(files);
        Button AddFile(string text, System.Action pressed) { var b = new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill }; b.Pressed += pressed; files.AddChild(b); return b; }
        AddFile("Importar arquivo…", PickAnimationFile); AddFile("Salvar exemplo…", PickAnimationExample); AddFile("Excluir arquivo", DeleteAnimationFile);
        _animationList = new ItemList { Name = "AnimationFiles", CustomMinimumSize = new Vector2(0, 80), SizeFlagsHorizontal = SizeFlags.ExpandFill }; fields.AddChild(_animationList);
        _animationMessage = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(280, 0) }; fields.AddChild(_animationMessage);
        RefreshAnimationMenus();
    }

    private static string SourceLabel(string source, IReadOnlyList<AnimationFileEntry> files)
    {
        if (source == "default") return "Padrão do jogo";
        if (source.StartsWith(AnimationStates.BuiltinPrefix, StringComparison.Ordinal)) { var key = source[AnimationStates.BuiltinPrefix.Length..]; return "Biblioteca: " + BuiltinNames.GetValueOrDefault(key, key); }
        return AnimationStates.TryParseCustom(source, out var id, out var clip) ? $"★ {files.FirstOrDefault(f => f.Id == id)?.Name ?? "arquivo"} / {clip}" : source;
    }

    /// <summary>Fills every menu with the default, the library alternatives of its state and the animations of the person's files, and selects the current choice.</summary>
    private void RefreshAnimationMenus()
    {
        _syncing = true;
        foreach (var state in AnimationOrder)
        {
            var menu = _animationMenus[state]; menu.Clear();
            var sources = new List<string> { "default" };
            sources.AddRange(AnimationStates.BuiltinFor(state).Select(n => AnimationStates.BuiltinPrefix + n));
            sources.AddRange(_animationFiles.SelectMany(f => f.Clips.Select(c => $"{AnimationStates.CustomPrefix}{f.Id}#{c}")));
            foreach (var source in sources) { menu.AddItem(SourceLabel(source, _animationFiles)); menu.SetItemMetadata(menu.ItemCount - 1, source); }
            var chosen = _animationChoice.GetValueOrDefault(state, "default"); var index = sources.IndexOf(chosen);
            if (index < 0) { _animationChoice.Remove(state); index = 0; } // a file that is gone: back to the default
            menu.Select(index);
        }
        _syncing = false;
    }

    private void OnAnimationChosen(string state)
    {
        if (_syncing) return;
        var source = Value(_animationMenus[state]);
        if (source == "default") _animationChoice.Remove(state); else _animationChoice[state] = source;
        RefreshPreview(); PreviewAnimation(state);
    }

    private void PreviewAnimation(string state)
    {
        if (IsInstanceValid(_preview) && Enum.TryParse<AvatarMotionState>(state, out var motion)) _preview.Preview(motion);
    }

    /// <summary>The choices on the form as the text the server stores.</summary>
    private string OverridesJson() => JsonSerializer.Serialize(AnimationOrder.Where(_animationChoice.ContainsKey).ToDictionary(s => s, s => _animationChoice[s]));

    /// <summary>The files the chosen animations come from, as the server describes them to everybody (so the preview downloads and plays the same ones).</summary>
    private string AnimationFilesJsonForForm()
    {
        var used = _animationChoice.Values.Select(v => AnimationStates.TryParseCustom(v, out var id, out _) ? id : Guid.Empty).Where(id => id != Guid.Empty).Distinct();
        return JsonSerializer.Serialize(used.Select(id => _animationFiles.FirstOrDefault(f => f.Id == id)).Where(f => f is not null).Select(f => new { id = f!.Id, hash = f.Hash }));
    }

    private void ReadAnimationChoices(AvatarAppearanceData value) { _animationChoice.Clear(); foreach (var pair in value.AnimationOverrides) _animationChoice[pair.Key] = pair.Value; }

    public void LoadAnimations() => _manager.Call(Godot.HttpClient.Method.Get, "/animations", null, (status, body) => { if (status is >= 200 and < 300) ReadAnimationFiles(body); });

    private void ReadAnimationFiles(string body)
    {
        _animationFiles.Clear(); _animationList.Clear();
        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                string Text(string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
                if (!Guid.TryParse(Text("id"), out var id)) continue;
                var clips = item.TryGetProperty("clips", out var c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray().Select(x => x.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty).Where(n => n.Length > 0).ToArray() : [];
                _animationFiles.Add(new AnimationFileEntry(id, Text("name"), Text("hash"), clips));
                _animationList.AddItem($"{Text("name")} · {clips.Length} animação(ões): {string.Join(", ", clips)}");
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { _animationFiles.Clear(); }
        RefreshAnimationMenus(); RefreshPreview();
    }

    private void SaveAnimationChoices()
    {
        _manager.Call(Godot.HttpClient.Method.Put, "/animation-overrides", JsonSerializer.Serialize(new { overrides = _animationChoice }), (status, body) =>
        {
            _animationMessage.Text = status is >= 200 and < 300 ? "Animações salvas." : status == 400 ? "Alguma escolha não é válida (o arquivo pode ter sido apagado)." : AnimationMessage(status, body);
        });
    }

    private void ClearAnimationChoices() { _animationChoice.Clear(); RefreshAnimationMenus(); RefreshPreview(); _animationMessage.Text = "Tudo voltou ao padrão. Toque em Salvar para guardar."; }

    private AnimationFileEntry? SelectedAnimationFile() { var picked = _animationList.GetSelectedItems(); return picked.Length == 0 || picked[0] >= _animationFiles.Count ? null : _animationFiles[picked[0]]; }

    private void DeleteAnimationFile()
    {
        if (SelectedAnimationFile() is not { } file) { _animationMessage.Text = "Escolha um arquivo na lista."; return; }
        _manager.Call(Godot.HttpClient.Method.Delete, $"/animations/{file.Id}", null, (status, body) =>
        {
            if (status is < 200 or >= 300) { _animationMessage.Text = AnimationMessage(status, body); return; }
            foreach (var state in _animationChoice.Where(p => AnimationStates.TryParseCustom(p.Value, out var id, out _) && id == file.Id).Select(p => p.Key).ToList()) _animationChoice.Remove(state);
            _animationMessage.Text = $"Arquivo “{file.Name}” excluído; os estados que o usavam voltaram ao padrão."; LoadAnimations();
        });
    }

    private void PickAnimationFile()
    {
        _animationOpen ??= MakeDialog(FileDialog.FileModeEnum.OpenFile, "Importar animações (.glb)", path => { try { ImportAnimations(System.IO.Path.GetFileName(path), System.IO.File.ReadAllBytes(path)); } catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { _animationMessage.Text = "Não foi possível ler esse arquivo."; } });
        _animationOpen.PopupCentered(new Vector2I(900, 560));
    }

    private void PickAnimationExample()
    {
        _animationSave ??= MakeDialog(FileDialog.FileModeEnum.SaveFile, "Salvar exemplo de animações", path =>
        {
            try { System.IO.File.WriteAllBytes(path, ExampleAnimationFile()); _animationMessage.Text = "Exemplo salvo: o esqueleto padrão com duas animações (“Passada” e “Acenar”) para ver como o arquivo deve ser."; }
            catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { _animationMessage.Text = "Não foi possível salvar nesse lugar."; }
        });
        _animationSave.CurrentFile = "lifeverse-exemplo-animacoes.glb"; _animationSave.PopupCentered(new Vector2I(900, 560));
    }

    private static byte[] ExampleAnimationFile() => HumanoidTemplate.Build(new TemplateOptions
    {
        Animations =
        [
            new AnimationSpec("Passada", 1f, [("upper_leg_l", 0, .6f), ("upper_leg_r", 0, -.6f), ("lower_leg_l", 0, .4f), ("lower_leg_r", 0, .4f), ("upper_arm_l", 0, -.4f), ("upper_arm_r", 0, .4f), ("spine", 1, .1f), ("chest", 1, .1f), ("head", 1, -.1f)], HipsBob: .04f),
            new AnimationSpec("Acenar", 1.4f, [("upper_arm_r", 2, 1.6f), ("lower_arm_r", 0, -1f), ("spine", 1, .1f), ("chest", 1, .1f), ("head", 1, .2f), ("hips", 1, .05f), ("upper_arm_l", 2, -.2f), ("lower_arm_l", 0, .1f)])
        ]
    });

    /// <summary>Sends a GLB with animations to the server and shows what came back.</summary>
    public void ImportAnimations(string fileName, byte[] bytes)
    {
        if (!fileName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) { _animationMessage.Text = "Use um arquivo .glb com as animações."; return; }
        _animationMessage.Text = "Enviando e conferindo as animações…";
        _manager.Upload("/animations", fileName, bytes, new Dictionary<string, string> { ["name"] = System.IO.Path.GetFileNameWithoutExtension(fileName) }, (status, body) =>
        {
            if (status is >= 200 and < 300) { _animationMessage.Text = DescribeAnimations("Animações aceitas.", body); LoadAnimations(); return; }
            _animationMessage.Text = status == 422 ? DescribeAnimations("O arquivo foi recusado:", body) : AnimationMessage(status, body);
        });
    }

    private static string DescribeAnimations(string title, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body); var root = document.RootElement; var report = root.TryGetProperty("report", out var r) ? r : root;
            var lines = new List<string> { title };
            if (report.ValueKind == JsonValueKind.Object && report.TryGetProperty("issues", out var issues))
                foreach (var issue in issues.EnumerateArray()) lines.Add((issue.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.True ? "✖ " : "• ") + AnimationIssue(issue.TryGetProperty("code", out var c) ? c.GetString() ?? string.Empty : string.Empty, issue.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty));
            return string.Join("\n", lines);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return title; }
    }

    private static string AnimationIssue(string code, string message) => code switch
    {
        "no_animations" => "O arquivo não tem animações.",
        "too_few_bones" => "A animação mexe em poucos ossos do humanoide. " + message,
        "bad_duration" => "Duração fora do limite. " + message,
        "too_many_keys" or "too_many_clips" => message,
        "duplicate_name" => "Duas animações com o mesmo nome. " + message,
        "root_motion" => "A animação move o quadril para longe; o jogo toca no lugar.",
        "not_loopable" => "A animação não termina onde começa e vai dar um pulo ao repetir.",
        "ignored_tracks" => "Algumas trilhas são de ossos que o jogo não usa (dedos, por exemplo) e são ignoradas.",
        "external_reference" => "O arquivo aponta para outros arquivos. Exporte um único .glb.",
        _ => message
    };

    private static string AnimationMessage(int status, string body)
    {
        var error = string.Empty;
        try { using var document = JsonDocument.Parse(body); error = document.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? string.Empty : string.Empty; } catch (JsonException) { }
        return error switch
        {
            "too_many_files" => "O limite é de 20 arquivos. Exclua algum antes.",
            "too_big" => $"O arquivo passa de {AnimationValidator.MaxBytes / 1024 / 1024} MB.",
            "bad_file" => "Use um arquivo .glb.",
            "not_found" => "Arquivo não encontrado.",
            _ => status == 0 ? "Sem conexão com o servidor." : $"Não foi possível concluir (HTTP {status})."
        };
    }

    // ---- hooks for tests ------------------------------------------------------------------------------------------------------------

    public string AnimationMessageForTesting => _animationMessage.Text;
    public void SelectAnimationFileForTesting(int index) => _animationList.Select(index);
    public void DeleteAnimationFileForTesting() => DeleteAnimationFile();
    public void SaveAnimationChoicesForTesting() => SaveAnimationChoices();
    public void ClearAnimationChoicesForTesting() => ClearAnimationChoices();
    public void ChooseAnimationForTesting(string state, string source) { var menu = _animationMenus[state]; Select(menu, source); OnAnimationChosen(state); }
    public IReadOnlyList<string> AnimationSourcesForTesting(string state) { var menu = _animationMenus[state]; return [.. Enumerable.Range(0, menu.ItemCount).Select(i => menu.GetItemMetadata(i).AsString())]; }
    public string OverridesJsonForTesting => OverridesJson();
}
