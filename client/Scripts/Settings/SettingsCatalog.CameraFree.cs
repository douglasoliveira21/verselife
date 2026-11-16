namespace LifeVerse.Client.Scripts.Settings;

/// <summary>Câmera e visualização › Câmera livre: the controls of the free camera (orbit, zoom, pan, focus, detached), the ones of Second Life and the simple CTRL mode.</summary>
public static partial class SettingsCatalog
{
    public const string FreeCameraTab = "Câmera livre";

    /// <summary>The ids of the options of the free camera: what "Restaurar câmera livre" puts back.</summary>
    public static readonly string[] FreeCameraIds =
    [
        "camera.classic_controls", "camera.ctrl_simple", "camera.keep_after_ctrl", "camera.restore_auto", "camera.restore_after_edit", "camera.focus_double_click",
        "camera.sens_h", "camera.sens_v", "camera.invert_h", "camera.invert_v", "camera.pitch_limit", "camera.zoom_speed", "camera.precision_zoom_speed", "camera.free_smoothing",
        "camera.min_distance", "camera.max_distance", "camera.allow_detached", "camera.detached_range", "camera.detached_speed"
    ];

    private static IEnumerable<SettingDefinition> CameraFree()
    {
        const SettingCategory C = SettingCategory.Camera; const string T = FreeCameraTab;
        // ---- controls
        yield return new("camera.classic_controls", C, T, "Controles clássicos do Second Life", "ALT + botão esquerdo gira e aproxima; CTRL + ALT gira para todos os lados; CTRL + ALT + SHIFT desliza a câmera. ALT e um clique, sem arrastar, olha para o que está sob o mouse.", SettingKind.Bool, true)
            { Group = "Controles", Column = 0, Keywords = ["second life", "alt", "ctrl", "órbita", "clássico"] };
        yield return new("camera.ctrl_simple", C, T, "Modo CTRL simplificado", "Segurar CTRL e arrastar com o botão esquerdo gira a câmera; CTRL + SHIFT desliza; CTRL + roda é o zoom de precisão.", SettingKind.Bool, false)
            { Group = "Controles", Column = 0, Keywords = ["ctrl", "simplificado", "órbita", "girar"] };
        yield return new("camera.keep_after_ctrl", C, T, "Manter a câmera ao soltar CTRL", "Ligado, o enquadramento fica onde você deixou (ESC ou andar o desfaz). Desligado, a câmera volta para trás do avatar quando você solta o CTRL.", SettingKind.Bool, true)
            { Group = "Controles", Column = 0, Keywords = ["ctrl", "manter", "soltar", "enquadramento"] };
        yield return new("camera.restore_auto", C, T, "Restaurar a câmera automaticamente", "Quando você anda, a câmera livre volta sozinha para trás do avatar. Desligado, ela fica onde está até você apertar ESC.", SettingKind.Bool, true)
            { Group = "Controles", Column = 0, Keywords = ["restaurar", "automático", "andar", "esc"] };
        yield return new("camera.restore_after_edit", C, T, "Restaurar a câmera ao fechar um editor", "Ao terminar de editar um anexo, a câmera volta ao enquadramento de antes. Desligado, ela fica onde a edição a deixou.", SettingKind.Bool, true)
            { Group = "Controles", Column = 0, Keywords = ["restaurar", "editor", "anexo", "enquadramento"] };
        yield return new("camera.focus_double_click", C, T, "Duplo clique foca em pessoas e objetos", "Um duplo clique em uma pessoa ou objeto põe a câmera nele. No chão, o duplo clique continua levando o avatar até o ponto.", SettingKind.Bool, false)
            { Group = "Controles", Column = 0, Keywords = ["duplo clique", "foco", "objeto", "pessoa"] };

        // ---- turn and zoom
        yield return new("camera.sens_h", C, T, "Sensibilidade horizontal", "Quanto a câmera livre gira de um lado ao outro por movimento do mouse.", SettingKind.Float, 1f)
            { Min = .25, Max = 3, Step = .05, Unit = "×", Group = "Giro e zoom", Column = 1, Keywords = ["sensibilidade", "horizontal", "girar"] };
        yield return new("camera.sens_v", C, T, "Sensibilidade vertical", "Quanto a câmera livre gira para cima e para baixo por movimento do mouse.", SettingKind.Float, 1f)
            { Min = .25, Max = 3, Step = .05, Unit = "×", Group = "Giro e zoom", Column = 1, Keywords = ["sensibilidade", "vertical", "girar"] };
        yield return new("camera.invert_h", C, T, "Inverter o eixo horizontal", "Arrastar para a direita gira a câmera para o outro lado.", SettingKind.Bool, false)
            { Group = "Giro e zoom", Column = 1, Keywords = ["inverter", "horizontal", "eixo"] };
        yield return new("camera.invert_v", C, T, "Inverter o eixo vertical", "Arrastar para baixo passa a olhar de baixo em vez de olhar de cima.", SettingKind.Bool, false)
            { Group = "Giro e zoom", Column = 1, Keywords = ["inverter", "vertical", "eixo"] };
        yield return new("camera.pitch_limit", C, T, "Limite do giro vertical", "Até quantos graus a câmera livre pode olhar para cima ou para baixo. Perto de 90° ela fica de cabeça para baixo.", SettingKind.Int, 85)
            { Min = 30, Max = 89, Step = 1, Unit = "°", Group = "Giro e zoom", Column = 1, Keywords = ["limite", "vertical", "ângulo", "giro"] };
        yield return new("camera.zoom_speed", C, T, "Velocidade do zoom", "Quanto cada giro da roda aproxima ou afasta a câmera livre (é proporcional à distância: de perto anda pouco, de longe anda muito).", SettingKind.Float, 1f)
            { Min = .25, Max = 3, Step = .05, Unit = "×", Group = "Giro e zoom", Column = 1, Keywords = ["zoom", "velocidade", "roda"] };
        yield return new("camera.precision_zoom_speed", C, T, "Velocidade do zoom de precisão", "A roda com CTRL: que parte de um passo normal ela dá. Bom para olhar acessórios e detalhes.", SettingKind.Float, .25f)
            { Min = .05, Max = 1, Step = .05, Unit = "×", Group = "Giro e zoom", Column = 1, Keywords = ["zoom", "precisão", "ctrl", "detalhe"] };
        yield return new("camera.free_smoothing", C, T, "Suavização da câmera livre", "Com que pressa a câmera chega aonde você mandou (giro, zoom e deslocamento). Valores menores são mais lentos e suaves.", SettingKind.Float, 14f)
            { Min = 2, Max = 30, Step = 1, Group = "Giro e zoom", Column = 1, Keywords = ["suavização", "suave", "movimento"] };

        // ---- limits
        yield return new("camera.min_distance", C, T, "Limite de aproximação", "O mais perto que a câmera livre chega do que está olhando.", SettingKind.Float, .2f)
            { Min = .05, Max = 3, Step = .05, Unit = " m", Group = "Limites", Column = 0, Keywords = ["aproximação", "mínimo", "perto"] };
        yield return new("camera.max_distance", C, T, "Limite de afastamento", "O mais longe que a câmera livre fica do que está olhando.", SettingKind.Float, 40f)
            { Min = 5, Max = 100, Step = 1, Unit = " m", Group = "Limites", Column = 0, Keywords = ["afastamento", "máximo", "longe"] };

        // ---- detached
        yield return new("camera.allow_detached", C, T, "Permitir a câmera desacoplada", "ALT + V solta a câmera do avatar: as teclas de andar a movem pelo mundo e o avatar fica parado. Ela não passa do alcance abaixo, nem sai da ilha.", SettingKind.Bool, false)
            { Group = "Câmera desacoplada", Column = 1, Keywords = ["desacoplada", "livre", "voar", "alt v"] };
        yield return new("camera.detached_range", C, T, "Alcance da câmera desacoplada", "A que distância do avatar a câmera desacoplada pode ir.", SettingKind.Int, 60)
            { Min = 10, Max = 200, Step = 5, Unit = " m", Group = "Câmera desacoplada", Column = 1, Keywords = ["alcance", "distância", "desacoplada"] };
        yield return new("camera.detached_speed", C, T, "Velocidade da câmera desacoplada", "Metros por segundo (com a tecla de correr, três vezes mais).", SettingKind.Float, 8f)
            { Min = 1, Max = 40, Step = 1, Unit = " m/s", Group = "Câmera desacoplada", Column = 1, Keywords = ["velocidade", "desacoplada"] };

        yield return Action("camera.restore_defaults", C, T, "Restaurar", 0, "Restaurar a câmera livre", "Todos os controles, sensibilidades, limites e a câmera desacoplada voltam ao padrão.", "Restaurar", "Restaurar todas as opções da câmera livre aos valores padrão?");
    }
}
