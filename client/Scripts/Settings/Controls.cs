using Godot;

namespace LifeVerse.Client.Scripts.Settings;

/// <summary>One thing the keyboard does: the input action of the game, how it is called in the window and the keys it starts with.</summary>
public sealed record KeyAction(string Action, string Label, string Help, string Group, string Main, string Alternative = "");

/// <summary>The keys of the game that can be changed (Configurações › Controles). Every one is a pair of settings, <c>controls.&lt;action&gt;.1</c> and <c>.2</c>, applied to the engine's input map.</summary>
public static class KeyBindings
{
    /// <summary>Keys the game itself needs and nobody may take: the menus (Esc), the chat (Enter), the interface mode (F10) and deleting a selected object.</summary>
    public static readonly string[] Reserved = ["Escape", "Enter", "Kp Enter", "F10", "Delete"];

    public static readonly IReadOnlyList<KeyAction> Actions =
    [
        new("move_forward", "Andar para frente", "Anda para a frente. Duas batidas rápidas correm (se estiver ligado em Mouse e movimento).", "Movimentação", "W", "Up"),
        new("move_back", "Andar para trás", "Anda para trás, de frente para onde olha.", "Movimentação", "S", "Down"),
        new("move_left", "Girar para a esquerda", "Gira o personagem para a esquerda.", "Movimentação", "A", "Left"),
        new("move_right", "Girar para a direita", "Gira o personagem para a direita.", "Movimentação", "D", "Right"),
        new("strafe_left", "Passo para a esquerda", "Anda de lado para a esquerda sem girar.", "Movimentação", "Q"),
        new("strafe_right", "Passo para a direita", "Anda de lado para a direita sem girar.", "Movimentação", "Z"),
        new("jump", "Pular", "Pula. Segurar começa o voo, se estiver ligado. Em água, sobe.", "Movimentação", "Space"),
        new("run", "Correr", "Segure para correr (inverte a opção de correr sempre).", "Movimentação", "Shift"),
        new("fly_toggle", "Voar", "Liga e desliga o voo.", "Movimentação", "F"),
        new("descend", "Descer", "Desce ao voar ou ao nadar.", "Movimentação", "Ctrl"),
        new("interact", "Interagir", "Interage com o objeto ou o lugar à sua frente.", "Ações", "E"),
        new("emote_wave", "Gesto: acenar", "Faz o gesto de acenar.", "Ações", "G"),
        new("emote_dance", "Gesto: dançar", "Faz o gesto de dançar.", "Ações", "H"),
        new("cursor_toggle", "Liberar o cursor", "Solta o mouse da câmera para clicar na tela.", "Ações", "Alt"),
        new("first_person", "Primeira pessoa", "Alterna entre primeira e terceira pessoa.", "Câmera", "C"),
        new("camera_mode", "Câmera livre", "Alterna a câmera livre.", "Câmera", "V"),
        new("camera_reset", "Redefinir câmera", "Volta a câmera para trás do personagem.", "Câmera", "R"),
        new("build_mode", "Construir", "Abre as ferramentas de construção.", "Construção", "B"),
        new("terrain_mode", "Editar terreno", "Liga as ferramentas de terreno (donos de terreno).", "Construção", "T"),
        new("voice_talk", "Falar (apertar para falar)", "Segure para falar, como o botão Falar da barra inferior.", "Voz", "X"),
    ];

    public static string IdOf(string action, int slot) => $"controls.{action}.{slot}";

    public static (string Action, int Slot)? Parse(string id)
    {
        if (!id.StartsWith("controls.", StringComparison.Ordinal)) return null;
        var rest = id["controls.".Length..]; var dot = rest.LastIndexOf('.');
        if (dot <= 0 || !int.TryParse(rest[(dot + 1)..], out var slot) || slot is not (1 or 2)) return null;
        var action = rest[..dot]; return Actions.Any(a => a.Action == action) ? (action, slot) : null;
    }

    /// <summary>The name stored for a key (the engine's own name, so it can be read back): "W", "Up", "Shift"...</summary>
    public static string NameOf(Key key) => key == Key.None ? "" : OS.GetKeycodeString(key);

    /// <summary>The key a stored name stands for, or None when it is empty, unknown or reserved.</summary>
    public static Key KeyOf(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Reserved.Contains(name, StringComparer.OrdinalIgnoreCase)) return Key.None;
        return OS.FindKeycodeFromString(name);
    }

    public static bool IsValid(string name) => name.Length == 0 || KeyOf(name) != Key.None;

    /// <summary>How a key is shown to people.</summary>
    public static string Pretty(string name) => name switch
    {
        "" => "—", "Up" => "Seta ↑", "Down" => "Seta ↓", "Left" => "Seta ←", "Right" => "Seta →", "Space" => "Espaço", "Escape" => "Esc", "Ctrl" => "Ctrl", "Tab" => "Tab", "Backspace" => "Backspace", _ => name
    };

    public static void Apply(string action, string main, string alternative)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action, .2f);
        InputMap.ActionEraseEvents(action);
        foreach (var name in new[] { main, alternative })
            if (KeyOf(name) is var key and not Key.None) InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }

    /// <summary>The first key an action has right now (tests).</summary>
    public static IReadOnlyList<string> KeysOf(string action) => InputMap.HasAction(action) ? [.. InputMap.ActionGetEvents(action).OfType<InputEventKey>().Select(e => NameOf(e.PhysicalKeycode != Key.None ? e.PhysicalKeycode : e.Keycode))] : [];
}

/// <summary>A button that waits for the next key and reports it (Esc cancels, Backspace or Delete clears an optional key).</summary>
public partial class KeyCaptureButton : Button
{
    private string _name = "";
    private bool _capturing;
    public bool AllowClear { get; init; }
    public bool Capturing => _capturing;
    /// <summary>A key is being waited for somewhere: the window leaves Esc and Ctrl+F alone then.</summary>
    public static bool AnyCapturing { get; private set; }
    public string KeyName { get => _name; set { _name = value; if (!_capturing) Text = KeyBindings.Pretty(value); } }
    /// <summary>The person pressed a key (its name, "" to clear).</summary>
    public event Action<string>? Captured;

    public KeyCaptureButton()
    {
        ToggleMode = false; FocusMode = FocusModeEnum.All; CustomMinimumSize = new Vector2(130, 42); ClipText = true; TooltipText = "Clique e pressione a tecla. Esc cancela.";
        UiStyle.StyleButton(this, UiStyle.ButtonKind.Field, 10, 6, 10); AddThemeFontSizeOverride("font_size", 17);
        Pressed += Begin;
    }

    public void Begin() { _capturing = true; AnyCapturing = true; Text = "Pressione uma tecla…"; AddThemeColorOverride("font_color", UiStyle.Orange); }
    public void Cancel() { _capturing = false; AnyCapturing = false; RemoveThemeColorOverride("font_color"); Text = KeyBindings.Pretty(_name); }

    public override void _Input(InputEvent input)
    {
        if (!_capturing || input is not InputEventKey { Pressed: true, Echo: false } key) return;
        GetViewport().SetInputAsHandled();
        var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
        if (code == Key.Escape) { Cancel(); return; }
        if (AllowClear && code is Key.Backspace or Key.Delete) { _capturing = false; AnyCapturing = false; RemoveThemeColorOverride("font_color"); Captured?.Invoke(""); Text = KeyBindings.Pretty(_name); return; }
        _capturing = false; AnyCapturing = false; RemoveThemeColorOverride("font_color");
        Captured?.Invoke(KeyBindings.NameOf(code)); Text = KeyBindings.Pretty(_name);
    }
}
