using Godot;

namespace LifeVerse.Client.Scripts.Settings;

/// <summary>The settings of Áudio, Comunicação por voz, Controles e movimentação and Câmera e visualização.</summary>
public static partial class SettingsCatalog
{
    private static SettingDefinition Volume(string id, string label, string help, string group = "Volume", int column = 0) =>
        new(id, SettingCategory.Audio, "Geral", label, help, SettingKind.Float, 1f) { Min = 0, Max = 1, Step = .05, Group = group, Column = column, Keywords = ["volume", "som", "áudio"] };

    private static IEnumerable<SettingDefinition> Stage3()
    {
        // ---- Áudio
        yield return Volume("audio.ambient", "Ambiente", "Volume do som do mundo: vento, água e a atmosfera da ilha.");
        yield return Volume("audio.effects", "Efeitos", "Volume dos passos e dos sons que os objetos tocam.");
        yield return Volume("audio.music", "Música", "Volume da música de entrada e das músicas de terrenos.");
        yield return Volume("audio.media", "Mídia", "Volume do áudio e da mídia que objetos e terrenos tocam.");
        yield return new("audio.interface", SettingCategory.Audio, "Geral", "Interface", "Volume dos sons de botões e janelas.", SettingKind.Float, 1f)
            { Min = 0, Max = 1, Step = .05, Group = "Volume", Unavailable = "O jogo ainda não tem sons de interface." };
        yield return new("audio.output_device", SettingCategory.Audio, "Geral", "Saída de áudio", "Dispositivo por onde todos os sons saem. Padrão do sistema acompanha o que o Windows usa; um dispositivo desconectado volta ao padrão.",
            SettingKind.Choice, "auto") { ChoiceSource = AudioBuses.OutputDevices, Group = "Saída", Column = 1, Keywords = ["alto-falante", "fone", "dispositivo", "saída"] };
        yield return new("audio.mute_background", SettingCategory.Audio, "Geral", "Silenciar em segundo plano", "Silencia o jogo quando a janela perde o foco e devolve o som ao voltar.",
            SettingKind.Bool, false) { Group = "Saída", Column = 1, Keywords = ["foco", "segundo plano", "mudo"] };

        // ---- Comunicação por voz › Microfone
        yield return new("voice.enabled", SettingCategory.Voice, "Microfone", "Voz ligada ao entrar", "Se você fala e ouve os outros ao entrar numa ilha. Você ainda pode silenciar tudo pelo menu Comunicar durante o jogo.",
            SettingKind.Bool, true) { Group = "Voz", Column = 0, Keywords = ["voz", "falar", "ouvir", "silenciar"] };
        yield return new("voice.open_mic", SettingCategory.Voice, "Microfone", "Microfone aberto (ativação por voz)", "O microfone fica aberto sem segurar o botão Falar: sua voz é enviada quando passa da sensibilidade escolhida. Desligado, você fala segurando o botão ou a tecla de falar.",
            SettingKind.Bool, false) { Group = "Voz", Column = 0, Keywords = ["cadeado", "push-to-talk", "ativação por voz", "microfone"] };
        yield return new("voice.input_device", SettingCategory.Voice, "Microfone", "Dispositivo de entrada", "Microfone usado para falar. Padrão do sistema acompanha o que o Windows usa. (A saída de áudio é a mesma de todo o jogo: veja Áudio.)",
            SettingKind.Choice, "auto") { ChoiceSource = AudioBuses.InputDevices, Group = "Voz", Column = 0, Keywords = ["microfone", "entrada", "dispositivo"] };
        yield return new("voice.sensitivity", SettingCategory.Voice, "Microfone", "Sensibilidade do microfone", "Só vale com o microfone aberto: quanto maior, mais baixo você pode falar para ser ouvido (e mais ruído de fundo passa). Use o teste abaixo para achar o ponto.",
            SettingKind.Int, 83) { Min = 0, Max = 100, Step = 1, Unit = "%", Group = "Voz", Column = 0, Keywords = ["sensibilidade", "limiar", "ruído"] };
        yield return new("voice.noise_suppression", SettingCategory.Voice, "Microfone", "Redução de ruído", "Tira o ruído de fundo da sua voz antes de enviar.", SettingKind.Bool, false)
            { Group = "Voz", Column = 0, Unavailable = "O cliente não tem redução de ruído (o Godot não oferece e o serviço de voz atual não a faz)." };
        yield return new("voice.echo_cancel", SettingCategory.Voice, "Microfone", "Cancelamento de eco", "Evita que o som dos alto-falantes volte pelo microfone.", SettingKind.Bool, false)
            { Group = "Voz", Column = 0, Unavailable = "O cliente não tem cancelamento de eco: use fones de ouvido." };

        // ---- Comunicação por voz › Audição
        yield return new("voice.users_volume", SettingCategory.Voice, "Audição", "Volume das vozes", "Volume de todas as pessoas que você ouve, sem mexer nos outros sons.",
            SettingKind.Float, 1f) { Min = 0, Max = 1, Step = .05, Group = "Vozes", Column = 0, Keywords = ["volume", "vozes", "pessoas"] };
        yield return new("voice.hearing_distance", SettingCategory.Voice, "Audição", "Distância de audição", "Até onde as vozes chegam a você. O servidor da ilha só envia vozes a até 25 m; um valor menor faz as vozes sumirem mais cedo.",
            SettingKind.Int, 25) { Min = 5, Max = 25, Step = 1, Unit = " m", Group = "Vozes", Column = 0, Keywords = ["distância", "alcance", "vozes"] };
        yield return new("voice.spatial", SettingCategory.Voice, "Audição", "Voz espacial", "A voz vem da direção do avatar e some com a distância. Desligada, todas as vozes soam iguais, sem direção.",
            SettingKind.Bool, true) { Group = "Vozes", Column = 0, Keywords = ["espacial", "3d", "direção"] };
        yield return new("voice.indicators", SettingCategory.Voice, "Audição", "Indicadores de fala", "A bolinha e as ondas ao lado do nome de quem fala.",
            SettingKind.Bool, true) { Group = "Indicadores", Column = 1, Keywords = ["nome", "indicador", "falando"] };

        // ---- Controles e movimentação › Teclas
        foreach (var action in KeyBindings.Actions)
        {
            var column = action.Group == "Movimentação" ? 0 : 1;
            yield return new(KeyBindings.IdOf(action.Action, 1), SettingCategory.Controls, "Teclas", action.Label, action.Help, SettingKind.Key, action.Main)
                { Group = action.Group, Column = column, Keywords = ["tecla", "atalho", action.Label.ToLowerInvariant()] };
            yield return new(KeyBindings.IdOf(action.Action, 2), SettingCategory.Controls, "Teclas", action.Label + " (alternativa)", action.Help, SettingKind.Key, action.Alternative)
                { Group = action.Group, Column = column, Keywords = ["tecla", "atalho"] };
        }

        // ---- Controles e movimentação › Mouse e movimento
        yield return new("controls.mouse_sensitivity", SettingCategory.Controls, "Mouse e movimento", "Sensibilidade do mouse", "Quanto a câmera gira por movimento do mouse, somado à sensibilidade do seu avatar.",
            SettingKind.Float, 1f) { Min = .25, Max = 3, Step = .05, Unit = "×", Group = "Mouse", Column = 0, Keywords = ["mouse", "câmera", "giro"] };
        yield return new("controls.invert_y", SettingCategory.Controls, "Mouse e movimento", "Inverter o eixo vertical", "Inverte o olhar para cima e para baixo, além do que estiver marcado no seu avatar.",
            SettingKind.Bool, false) { Group = "Mouse", Column = 0, Keywords = ["inverter", "eixo", "mouse"] };
        yield return new("controls.always_run", SettingCategory.Controls, "Mouse e movimento", "Correr sempre", "O personagem corre sem segurar a tecla de correr (segurar passa a andar).",
            SettingKind.Bool, false) { Group = "Movimento", Column = 1, Keywords = ["correr", "andar"] };
        yield return new("controls.double_tap_run", SettingCategory.Controls, "Mouse e movimento", "Correr com duas batidas", "Duas batidas rápidas na tecla de andar para a frente fazem o personagem correr.",
            SettingKind.Bool, true) { Group = "Movimento", Column = 1, Keywords = ["correr", "duas batidas"] };
        yield return new("controls.hold_to_fly", SettingCategory.Controls, "Mouse e movimento", "Voar segurando o pulo", "Segurar a tecla de pular por meio segundo liga o voo. Desligado, só a tecla de voar faz isso.",
            SettingKind.Bool, true) { Group = "Movimento", Column = 1, Keywords = ["voar", "pular", "voo"] };

        // ---- Câmera e visualização › Câmera
        yield return new("camera.default_mode", SettingCategory.Camera, "Câmera", "Modo ao entrar", "Em que modo a câmera começa ao entrar numa ilha.", SettingKind.Choice, "third")
            { Choices = [new("third", "Terceira pessoa"), new("first", "Primeira pessoa")], Group = "Visão", Column = 0, Keywords = ["primeira pessoa", "terceira pessoa", "modo"] };
        yield return new("camera.fov", SettingCategory.Camera, "Câmera", "Campo de visão", "Abertura da câmera em graus. Mais aberto mostra mais dos lados e deforma as bordas; mais fechado aproxima.", SettingKind.Int, 75)
            { Min = 50, Max = 110, Step = 1, Unit = "°", Group = "Visão", Column = 0, Keywords = ["fov", "campo de visão", "zoom"] };
        yield return new("camera.wheel_first_person", SettingCategory.Camera, "Câmera", "Roda do mouse entra na primeira pessoa", "Aproximar a câmera até a distância mínima com a roda entra na primeira pessoa; afastar a roda de dentro dela volta para a terceira.", SettingKind.Bool, true)
            { Group = "Visão", Column = 0, Keywords = ["roda", "primeira pessoa", "zoom"] };
        yield return new("camera.fp_fov", SettingCategory.Camera, "Câmera", "Campo de visão da primeira pessoa", "Abertura da câmera quando você vê pelos olhos do personagem (60° a 100°). Mais aberto mostra mais dos lados e dos pés; mais fechado aproxima.", SettingKind.Int, 75)
            { Min = 60, Max = 100, Step = 1, Unit = "°", Group = "Visão", Column = 0, Keywords = ["fov", "campo de visão", "primeira pessoa"] };
        // ---- Primeira pessoa
        yield return new("camera.fp_bob", SettingCategory.Camera, "Câmera", "Balanço da cabeça ao andar", "Um leve sobe e desce e balanço lateral da visão enquanto você anda e corre na primeira pessoa.", SettingKind.Bool, true)
            { Group = "Primeira pessoa", Column = 1, Keywords = ["head bob", "balanço", "andar", "primeira pessoa"] };
        yield return new("camera.fp_bob_strength", SettingCategory.Camera, "Câmera", "Intensidade do balanço", "Quanto a cabeça balança ao andar (1 é o normal).", SettingKind.Float, 1f)
            { Min = .25, Max = 2, Step = .25, Unit = "×", Group = "Primeira pessoa", Column = 1, Keywords = ["head bob", "balanço", "intensidade"] };
        yield return new("camera.fp_sway", SettingCategory.Camera, "Câmera", "Inclinação ao girar", "A visão inclina um pouco ao girar e ao andar de lado, sem exageros.", SettingKind.Bool, true)
            { Group = "Primeira pessoa", Column = 1, Keywords = ["sway", "inclinação", "girar", "primeira pessoa"] };
        yield return new("camera.fp_sway_strength", SettingCategory.Camera, "Câmera", "Intensidade da inclinação", "Quanto a visão inclina ao girar (1 é o normal).", SettingKind.Float, 1f)
            { Min = .25, Max = 2, Step = .25, Unit = "×", Group = "Primeira pessoa", Column = 1, Keywords = ["sway", "inclinação", "intensidade"] };
        yield return new("camera.fp_stabilization", SettingCategory.Camera, "Câmera", "Estabilização da cabeça", "Tira da câmera os tremores que as animações do esqueleto põem na cabeça. 0 segue a cabeça exatamente; 1 é o mais firme.", SettingKind.Float, .6f)
            { Min = 0, Max = 1, Step = .05, Group = "Primeira pessoa", Column = 1, Keywords = ["estabilização", "tremor", "cabeça", "primeira pessoa"] };
        yield return new("camera.fp_look_smoothing", SettingCategory.Camera, "Câmera", "Suavização do olhar", "Quão macio é o giro da visão. 0 acompanha o mouse na hora; valores maiores deixam o giro mais lento e suave. O padrão não tem atraso perceptível.", SettingKind.Float, .35f)
            { Min = 0, Max = 1, Step = .05, Group = "Primeira pessoa", Column = 1, Keywords = ["suavização", "mouse", "olhar", "primeira pessoa"] };
        yield return new("camera.fp_collision", SettingCategory.Camera, "Câmera", "Colisão na primeira pessoa", "A câmera não atravessa paredes e objetos, nem quando o rosto encosta neles.", SettingKind.Bool, true)
            { Group = "Primeira pessoa", Column = 1, Keywords = ["colisão", "parede", "primeira pessoa"] };
        yield return new("camera.default_distance", SettingCategory.Camera, "Câmera", "Distância da câmera", "Quão atrás do personagem a câmera fica na terceira pessoa. A roda do mouse continua ajustando durante o jogo.", SettingKind.Float, 5.2f)
            { Min = 1.5, Max = 12, Step = .1, Unit = " m", Group = "Visão", Column = 0, Keywords = ["distância", "zoom", "terceira pessoa"] };
        yield return new("camera.zoom_step", SettingCategory.Camera, "Câmera", "Passo da roda do mouse", "Quanto cada giro da roda aproxima ou afasta a câmera.", SettingKind.Float, .5f)
            { Min = .25, Max = 2, Step = .25, Unit = " m", Group = "Visão", Column = 0, Keywords = ["zoom", "roda", "mouse"] };
        yield return new("camera.height", SettingCategory.Camera, "Câmera", "Altura", "Sobe ou desce o ponto para onde a câmera olha, em relação à cabeça do personagem.", SettingKind.Float, 0f)
            { Min = -.5, Max = 1, Step = .05, Unit = " m", Group = "Posição e movimento", Column = 1, Keywords = ["altura", "deslocamento"] };
        yield return new("camera.shoulder", SettingCategory.Camera, "Câmera", "Deslocamento lateral", "Desloca a imagem para um dos lados (negativo, esquerda), como numa câmera por cima do ombro.", SettingKind.Float, 0f)
            { Min = -1, Max = 1, Step = .05, Unit = " m", Group = "Posição e movimento", Column = 1, Keywords = ["ombro", "lado", "deslocamento"] };
        yield return new("camera.smoothing", SettingCategory.Camera, "Câmera", "Suavização do giro", "Com que pressa a câmera acompanha o personagem quando ele vira. Valores menores são mais lentos e suaves; maiores, mais colados.", SettingKind.Float, 3.5f)
            { Min = .5, Max = 10, Step = .5, Group = "Posição e movimento", Column = 1, Keywords = ["suavização", "giro", "seguir"] };
        yield return new("camera.collision", SettingCategory.Camera, "Câmera", "Colisão da câmera", "A câmera se aproxima quando uma parede ou objeto ficaria entre ela e o personagem. Desligada, ela atravessa tudo.", SettingKind.Bool, true)
            { Group = "Posição e movimento", Column = 1, Keywords = ["colisão", "parede", "atravessar"] };

        // ---- Câmera e visualização › Foto
        yield return new("camera.photo_format", SettingCategory.Camera, "Foto", "Formato da foto", "PNG guarda a imagem sem perdas (arquivo maior); JPG é bem menor, com uma perda leve de qualidade.", SettingKind.Choice, "png")
            { Choices = [new("png", "PNG"), new("jpg", "JPG")], Group = "Modo foto", Column = 0, Keywords = ["foto", "captura", "imagem"] };
        yield return new("camera.photo_include_ui", SettingCategory.Camera, "Foto", "Incluir a interface na foto", "Por padrão a foto sai sem barras e janelas. Ligado, ela mostra a tela como você a vê.", SettingKind.Bool, false)
            { Group = "Modo foto", Column = 0, Keywords = ["foto", "interface", "captura"] };
    }
}
