using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts.Settings;

/// <summary>
/// Every setting the window knows. It grows stage by stage: a setting is added here only when the system behind it exists and an applier is registered
/// (<see cref="SettingsAppliers"/>); what the project cannot do is marked <c>Unavailable</c> with the reason (shown disabled, never faked) and listed in
/// docs/CONFIGURACOES_MIGRACAO.md.
/// </summary>
public static partial class SettingsCatalog
{
    private static IReadOnlyList<SettingChoice> Choices(params (string Id, string Label)[] items) => [.. items.Select(i => new SettingChoice(i.Id, i.Label))];

    private static readonly IReadOnlyList<SettingDefinition> Raw =
    [
        // ---- Gráficos e desempenho › Geral
        new("graphics.preset", SettingCategory.Graphics, "Geral", "Predefinição gráfica",
            "Conjunto de efeitos visuais: suavização de bordas, oclusão ambiente, brilho e sombras. Automático escolhe pela placa de vídeo. Níveis mais altos custam mais quadros por segundo. Mudar à mão uma opção que a predefinição controla a torna Personalizada.",
            SettingKind.Choice, "auto")
        {
            Choices = [.. GraphicsQuality.Keys.Select((key, i) => new SettingChoice(key, GraphicsQuality.Labels[i]))],
            Keywords = ["qualidade", "gráficos", "efeitos", "desempenho", "sombras", "antialiasing", "ultra", "baixo", "alto"]
        },
        new("display.monitor", SettingCategory.Graphics, "Geral", "Monitor",
            "Em qual monitor o jogo aparece. A troca pede confirmação e volta sozinha em 15 segundos se você não confirmar.",
            SettingKind.Choice, "auto") { ChoiceSource = DisplayOptions.Monitors, Keywords = ["tela", "display", "monitor"] },
        new("display.resolution", SettingCategory.Graphics, "Geral", "Resolução",
            "Tamanho da janela do jogo, só no modo Janela (em tela cheia vale a resolução do monitor; para aliviar a placa use a escala de renderização). A troca pede confirmação e volta sozinha em 15 segundos.",
            SettingKind.Choice, "auto") { ChoiceSource = () => DisplayOptions.Resolutions(DisplayOptions.CurrentScreen), Keywords = ["tamanho", "janela", "1080p", "720p", "4k"] },
        new("display.mode", SettingCategory.Graphics, "Geral", "Modo de exibição",
            "Janela, tela cheia (sem bordas) ou tela cheia exclusiva. A troca pede confirmação e volta sozinha em 15 segundos.",
            SettingKind.Choice, "auto") { ChoiceSource = () => DisplayOptions.Modes, Keywords = ["tela cheia", "fullscreen", "janela"] },
        new("graphics.vsync", SettingCategory.Graphics, "Geral", "Sincronização vertical (V-Sync)",
            "Sincroniza os quadros com a taxa da tela: evita imagem rasgada, mas pode aumentar o atraso do mouse. Desligado, o jogo pode passar da taxa da tela.",
            SettingKind.Bool, true) { Keywords = ["vsync", "tela", "rasgo", "taxa"] },
        new("graphics.render_scale", SettingCategory.Graphics, "Geral", "Escala de renderização",
            "Resolução com que o mundo 3D é desenhado, em relação à da janela. Abaixo de 100% o jogo desenha menos pixels e amplia com FSR (mais quadros por segundo, imagem mais suave); acima de 100% desenha mais (mais nítido, mais pesado). A interface não é afetada.",
            SettingKind.Int, 100) { Min = 50, Max = 150, Step = 5, Unit = "%", Keywords = ["fsr", "resolução", "supersampling", "desempenho"] },
        new("graphics.dynamic_resolution", SettingCategory.Graphics, "Geral", "Renderização dinâmica",
            "Reduz a escala de renderização enquanto os quadros por segundo estão abaixo da meta e volta a subir quando a placa dá conta. Nunca passa da escala escolhida acima nem fica abaixo de 60% dela.",
            SettingKind.Bool, false) { Keywords = ["resolução dinâmica", "desempenho", "fps", "automático"] },
        new("graphics.fps_limit", SettingCategory.Graphics, "Geral", "Limite de FPS",
            "Número máximo de quadros por segundo. Limitar economiza energia e calor; com o V-Sync ligado, a taxa da tela já impõe um limite.",
            SettingKind.Int, 120) { Min = 30, Max = 360, Step = 10, Unit = " FPS", Keywords = ["fps", "quadros", "taxa", "desempenho"] },
        new("perf.render_distance", SettingCategory.Graphics, "Geral", "Distância de renderização",
            "Até onde a câmera enxerga objetos e terreno. Menos distância alivia a placa em ilhas grandes; o céu não é afetado.",
            SettingKind.Int, 4000) { Min = 100, Max = 4000, Step = 50, Unit = " m", Keywords = ["alcance", "visão", "câmera", "desempenho"] },
        new("graphics.shadow_distance", SettingCategory.Graphics, "Geral", "Distância de sombras",
            "Até onde o sol desenha sombras. Sombras mais longe custam mais; perto delas ficam nítidas em qualquer valor. A predefinição escolhe um valor, e mudá-lo a torna Personalizada.",
            SettingKind.Int, 140) { Min = 20, Max = 400, Step = 5, Unit = " m", PresetControlled = true, Keywords = ["sombra", "sol", "desempenho"] },
        new("avatars.max_full", SettingCategory.Graphics, "Geral", "Limite de avatares completos",
            "Quantos avatares ao mesmo tempo são desenhados com todos os detalhes (corpo e itens personalizados e animações próprias). Os outros aparecem com menos detalhe. Menos avatares completos ajudam em ilhas cheias.",
            SettingKind.Int, 10) { Min = 1, Max = 40, Step = 1, Keywords = ["avatares", "pessoas", "multidão", "lod", "desempenho"] },
        new("perf.object_lod", SettingCategory.Graphics, "Geral", "LOD de objetos (nível de detalhe)",
            "Reduz o detalhe de objetos distantes.", SettingKind.Choice, "high")
        { Choices = Choices(("high", "Alto"), ("medium", "Médio"), ("low", "Baixo")), Unavailable = "O jogo ainda não tem nível de detalhe para objetos." },
        new("avatars.lod", SettingCategory.Graphics, "Geral", "LOD de avatares",
            "A que distância os avatares perdem detalhe: Alto mantém os detalhes mais longe (mais pesado), Baixo reduz mais perto (mais leve).",
            SettingKind.Choice, "medium") { Choices = Choices(("high", "Alto"), ("medium", "Médio"), ("low", "Baixo")), Keywords = ["detalhe", "avatares", "distância"] },
        new("avatars.simplify_distant", SettingCategory.Graphics, "Geral", "Simplificar avatares distantes",
            "Avatares com corpo personalizado longe de você são desenhados com menos triângulos. Desligado, eles passam direto de completos para o corpo simples.",
            SettingKind.Bool, true) { Keywords = ["lod", "avatares", "triângulos", "desempenho"] },
        new("metrics.fps", SettingCategory.Graphics, "Geral", "Mostrar FPS", "Mostra os quadros por segundo sobre o jogo.", SettingKind.Bool, false) { Keywords = ["indicador", "quadros", "fps"] },
        new("metrics.cpu_gpu", SettingCategory.Graphics, "Geral", "Uso de CPU e GPU", "Mostra o quanto o processador e a placa de vídeo estão ocupados.", SettingKind.Bool, false)
            { Unavailable = "O Godot não informa o uso de CPU e GPU em porcentagem. O tempo de renderização (abaixo) mostra o quanto cada um gasta por quadro." },
        new("metrics.memory", SettingCategory.Graphics, "Geral", "Uso de memória (RAM/VRAM)", "Mostra a memória do jogo (RAM) e a de vídeo que ele usa (VRAM).", SettingKind.Bool, false) { Keywords = ["ram", "vram", "memória", "indicador"] },
        new("metrics.frame_time", SettingCategory.Graphics, "Geral", "Tempo de renderização (ms)", "Mostra, em milissegundos, quanto o processador e a placa de vídeo gastam para desenhar cada quadro.", SettingKind.Bool, false) { Keywords = ["ms", "quadro", "indicador", "gpu", "cpu"] },

        // ---- Gráficos e desempenho › Qualidade
        new("graphics.texture_quality", SettingCategory.Graphics, "Qualidade", "Texturas",
            "Detalhe das texturas. Média e Baixa usam versões menores das imagens: economizam memória de vídeo e ajudam placas fracas, com imagem mais borrada de perto.",
            SettingKind.Choice, "high") { Choices = Choices(("high", "Alta"), ("medium", "Média"), ("low", "Baixa")), Keywords = ["textura", "memória de vídeo", "vram", "mipmap"] },
        new("graphics.anisotropic", SettingCategory.Graphics, "Qualidade", "Filtragem anisotrópica",
            "Deixa as texturas nítidas em superfícies vistas de lado, como o chão ao longe. Níveis maiores custam pouco em placas atuais.",
            SettingKind.Choice, "2") { Choices = Choices(("0", "Desligada"), ("2", "2x"), ("4", "4x"), ("8", "8x"), ("16", "16x")), Keywords = ["filtragem", "nitidez", "chão", "textura"] },
        new("graphics.msaa", SettingCategory.Graphics, "Qualidade", "Antialiasing (suavização de bordas)",
            "MSAA suaviza as bordas dos objetos. Não há FXAA nem TAA de propósito: eles borram o nome sobre os avatares em movimento. Mais amostras custam mais quadros por segundo.",
            SettingKind.Choice, "4") { Choices = Choices(("0", "Desligado"), ("2", "MSAA 2x"), ("4", "MSAA 4x"), ("8", "MSAA 8x")), PresetControlled = true, Keywords = ["bordas", "serrilhado", "msaa", "suavização"] },
        new("graphics.shadows_enabled", SettingCategory.Graphics, "Qualidade", "Sombras do sol",
            "Liga as sombras que o sol projeta. É um dos efeitos mais pesados; desligado, o mundo fica mais chapado.",
            SettingKind.Bool, true) { PresetControlled = true, Keywords = ["sombra", "sol", "desempenho"] },
        new("graphics.shadow_quality", SettingCategory.Graphics, "Qualidade", "Qualidade das sombras",
            "Suavidade da borda das sombras: Duras são mais leves e serrilhadas; Suaves ficam mais naturais e custam mais.",
            SettingKind.Choice, "high") { Choices = Choices(("hard", "Duras"), ("low", "Suaves: baixa"), ("medium", "Suaves: média"), ("high", "Suaves: alta"), ("ultra", "Suaves: ultra")), PresetControlled = true, Keywords = ["sombra", "suave", "borda"] },
        new("graphics.shadow_cascades", SettingCategory.Graphics, "Qualidade", "Divisões das sombras (cascatas)",
            "Quantas faixas de distância as sombras do sol usam: mais faixas deixam as sombras nítidas de perto e de longe, mas custam mais.",
            SettingKind.Choice, "4") { Choices = Choices(("1", "1 faixa"), ("2", "2 faixas"), ("4", "4 faixas")), PresetControlled = true, Keywords = ["cascata", "sombra", "distância"] },
        new("graphics.ssao", SettingCategory.Graphics, "Qualidade", "Oclusão ambiente",
            "Escurece cantos, frestas e a base dos objetos, como acontece na luz de verdade. Dá volume à cena e custa processamento de tela.",
            SettingKind.Bool, true) { PresetControlled = true, Keywords = ["ssao", "oclusão", "cantos", "iluminação"] },
        new("graphics.ssao_quality", SettingCategory.Graphics, "Qualidade", "Qualidade da oclusão ambiente",
            "Precisão do cálculo da oclusão ambiente. Níveis altos são mais suaves e mais pesados.",
            SettingKind.Choice, "medium") { Choices = Choices(("verylow", "Muito baixa"), ("low", "Baixa"), ("medium", "Média"), ("high", "Alta"), ("ultra", "Ultra")), PresetControlled = true, Keywords = ["ssao", "oclusão"] },
        new("graphics.ssil", SettingCategory.Graphics, "Qualidade", "Iluminação indireta de tela",
            "Faz a luz dos objetos coloridos \"vazar\" para os vizinhos. Só a predefinição Ultra usa; é pesado.",
            SettingKind.Bool, false) { PresetControlled = true, Keywords = ["ssil", "iluminação", "luz indireta"] },
        new("graphics.glow", SettingCategory.Graphics, "Qualidade", "Brilho (bloom)",
            "Halo suave em volta do sol e das luzes fortes.", SettingKind.Bool, true) { PresetControlled = true, Keywords = ["bloom", "brilho", "pós-processamento"] },
        new("graphics.color_adjust", SettingCategory.Graphics, "Qualidade", "Ajuste de cor",
            "Realça levemente o contraste e a saturação da imagem final.", SettingKind.Bool, true) { PresetControlled = true, Keywords = ["cor", "contraste", "saturação", "pós-processamento"] },
        new("graphics.ssr", SettingCategory.Graphics, "Qualidade", "Reflexos de tela",
            "Superfícies lisas e opacas refletem o que está na tela. Não afeta a água (que é transparente). Custa processamento de tela; as predefinições não mudam isso.",
            SettingKind.Bool, false) { Keywords = ["reflexo", "ssr", "espelho", "iluminação"] },
        new("graphics.volumetric", SettingCategory.Graphics, "Qualidade", "Raios de luz (névoa volumétrica)",
            "Mostra a luz do sol atravessando o ar, com um leve véu de névoa. É pesado em placas fracas; as predefinições não mudam isso.",
            SettingKind.Bool, false) { Keywords = ["volumétrico", "névoa", "raios", "efeitos"] },
        new("graphics.fog", SettingCategory.Graphics, "Qualidade", "Neblina do clima",
            "A névoa de dias nublados, de chuva e de neblina. Desligada, o ar fica limpo em qualquer clima.", SettingKind.Bool, true) { Keywords = ["névoa", "neblina", "clima"] },
        new("graphics.particles", SettingCategory.Graphics, "Qualidade", "Partículas",
            "Quantidade de partículas da chuva e dos efeitos dos objetos (fogo, fumaça, faíscas). Efeitos já criados só mudam quando forem criados de novo.",
            SettingKind.Choice, "full") { Choices = Choices(("full", "Todas"), ("medium", "Médias"), ("low", "Poucas"), ("off", "Desligadas")), Keywords = ["chuva", "fogo", "fumaça", "efeitos"] },
        new("graphics.water_waves", SettingCategory.Graphics, "Qualidade", "Ondas da água",
            "As ondas que movem a superfície do mar. Desligadas, a água fica plana, com o mesmo brilho e espuma.", SettingKind.Bool, true) { Keywords = ["água", "mar", "ondas"] },
        new("graphics.vegetation", SettingCategory.Graphics, "Qualidade", "Vegetação",
            "Nível de detalhe de árvores e grama.", SettingKind.Choice, "high")
        { Choices = Choices(("high", "Alta"), ("medium", "Média"), ("low", "Baixa")), Unavailable = "Árvores e grama são objetos criados pelos jogadores; o jogo não tem vegetação própria com níveis de qualidade." },

        // ---- Gráficos e desempenho › Mundo e avatares
        new("world.object_distance", SettingCategory.Graphics, "Mundo e avatares", "Distância de visibilidade de objetos",
            "Objetos mais longe que isto deixam de ser desenhados (a colisão continua). No máximo (1000 m), nenhum é escondido.",
            SettingKind.Int, 1000) { Min = 50, Max = 1000, Step = 25, Unit = " m", Keywords = ["objetos", "alcance", "distância", "desempenho"] },
        new("world.max_lights", SettingCategory.Graphics, "Mundo e avatares", "Limite de luzes visíveis",
            "Quantas luzes de objetos ficam acesas ao mesmo tempo. As primeiras a aparecer na ilha acendem; as demais ficam apagadas até que outras sumam. Luzes são caras.",
            SettingKind.Int, 16) { Min = 0, Max = 64, Step = 1, Keywords = ["luzes", "iluminação", "objetos"] },
        new("world.max_light_shadows", SettingCategory.Graphics, "Mundo e avatares", "Luzes com sombra",
            "Quantas das luzes acesas projetam sombra. Cada uma custa bastante.", SettingKind.Int, 4) { Min = 0, Max = 16, Step = 1, Keywords = ["luzes", "sombra"] },
        new("avatars.visibility_distance", SettingCategory.Graphics, "Mundo e avatares", "Distância de visibilidade de avatares",
            "Avatares mais longe que isto deixam de ser desenhados. Mais perto aparecem primeiro simples e ganham detalhe ao chegar perto.",
            SettingKind.Int, 120) { Min = 30, Max = 300, Step = 10, Unit = " m", Keywords = ["avatares", "pessoas", "distância", "desempenho"] },
        new("avatars.max_reduced", SettingCategory.Graphics, "Mundo e avatares", "Máximo de avatares com detalhe reduzido",
            "Quantos avatares ao mesmo tempo usam o corpo personalizado simplificado (o degrau entre completo e simples).", SettingKind.Int, 24) { Min = 0, Max = 60, Step = 1, Keywords = ["avatares", "lod", "multidão"] },
        new("avatars.triangle_budget", SettingCategory.Graphics, "Mundo e avatares", "Orçamento de complexidade visual dos avatares",
            "Quantos milhares de triângulos todos os avatares juntos podem custar. Quando o orçamento acaba, os mais distantes perdem detalhe primeiro.",
            SettingKind.Int, 250) { Min = 50, Max = 1000, Step = 50, Unit = " mil triângulos", Keywords = ["triângulos", "avatares", "complexidade", "orçamento", "desempenho"] },
        new("avatars.animation_quality", SettingCategory.Graphics, "Mundo e avatares", "Qualidade das animações",
            "Com que frequência os avatares de detalhe reduzido são animados: Alta anima todo quadro, Normal um a cada dois, Baixa um a cada três. Menos animação alivia o processador em ilhas cheias.",
            SettingKind.Choice, "normal") { Choices = Choices(("high", "Alta"), ("normal", "Normal"), ("low", "Baixa")), Keywords = ["animação", "avatares", "processador"] },
        new("avatars.texture_quality", SettingCategory.Graphics, "Mundo e avatares", "Qualidade das texturas dos avatares",
            "Detalhe das texturas dos avatares.", SettingKind.Choice, "high")
        { Choices = Choices(("high", "Alta"), ("medium", "Média"), ("low", "Baixa")), Unavailable = "As texturas dos avatares seguem a opção Texturas da aba Qualidade; não há um nível só para avatares." },

        // ---- Gráficos e desempenho › Otimização
        new("perf.auto_quality", SettingCategory.Graphics, "Otimização", "Ajuste automático de desempenho",
            "Se os quadros por segundo ficarem bem abaixo da meta por cerca de 15 segundos, baixa a predefinição gráfica um nível (no máximo uma vez por minuto) e avisa. Não mexe em uma predefinição Personalizada.",
            SettingKind.Bool, false) { Keywords = ["automático", "desempenho", "fps", "qualidade"] },
        new("perf.background_fps", SettingCategory.Graphics, "Otimização", "Redução de qualidade em segundo plano",
            "Quando a janela do jogo perde o foco, limita os quadros por segundo para economizar energia e calor. Volta ao limite normal ao voltar.",
            SettingKind.Choice, "off") { Choices = Choices(("off", "Desligada"), ("30", "30 FPS"), ("15", "15 FPS"), ("5", "5 FPS")), Keywords = ["segundo plano", "foco", "economia", "energia"] },
        new("perf.shader_cache", SettingCategory.Graphics, "Otimização", "Cache de shaders", "Guarda os shaders já compilados para a próxima abertura.", SettingKind.Bool, true)
            { Unavailable = "O Godot gerencia o cache de shaders sozinho e a escolha só existe na configuração do projeto, não em tempo de execução." },
        new("perf.resource_cache", SettingCategory.Graphics, "Otimização", "Tamanho do cache de recursos", "Limite de espaço em disco para modelos e texturas baixados.", SettingKind.Int, 2048)
            { Min = 256, Max = 16384, Step = 256, Unit = " MB", Unavailable = "O cache de recursos ainda não tem limite configurável nem remoção dos mais antigos." },
        new("perf.preload_assets", SettingCategory.Graphics, "Otimização", "Pré-carregamento de assets", "Baixa antes de precisar os modelos e texturas das ilhas favoritas.", SettingKind.Bool, false)
            { Unavailable = "O jogo carrega os assets quando entra na ilha; não há pré-carregamento de ilhas." },

        // ---- Áudio (a categoria completa chega na etapa 3)
        new("audio.master", SettingCategory.Audio, "Geral", "Volume geral",
            "Volume de todos os sons do jogo. Zero silencia tudo.",
            SettingKind.Float, 1f) { Min = 0, Max = 1, Step = .05, Keywords = ["volume", "som", "mudo", "áudio"] },
    ];

    private static readonly Dictionary<string, (string Group, int Column)> Layout = new()
    {
        ["graphics.preset"] = ("Predefinição gráfica", 0), ["display.monitor"] = ("Tela e exibição", 0), ["display.resolution"] = ("Tela e exibição", 0), ["display.mode"] = ("Tela e exibição", 0),
        ["graphics.vsync"] = ("Tela e exibição", 0), ["graphics.render_scale"] = ("Tela e exibição", 0), ["graphics.dynamic_resolution"] = ("Tela e exibição", 0),
        ["graphics.fps_limit"] = ("Desempenho", 1), ["perf.render_distance"] = ("Desempenho", 1), ["graphics.shadow_distance"] = ("Desempenho", 1), ["avatars.max_full"] = ("Desempenho", 1),
        ["perf.object_lod"] = ("Desempenho", 1), ["avatars.lod"] = ("Desempenho", 1), ["avatars.simplify_distant"] = ("Desempenho", 1),
        ["metrics.fps"] = ("Indicadores na tela", 1), ["metrics.cpu_gpu"] = ("Indicadores na tela", 1), ["metrics.memory"] = ("Indicadores na tela", 1), ["metrics.frame_time"] = ("Indicadores na tela", 1),
        ["graphics.texture_quality"] = ("Imagem", 0), ["graphics.anisotropic"] = ("Imagem", 0), ["graphics.msaa"] = ("Imagem", 0),
        ["graphics.shadows_enabled"] = ("Sombras e iluminação", 1), ["graphics.shadow_quality"] = ("Sombras e iluminação", 1), ["graphics.shadow_cascades"] = ("Sombras e iluminação", 1),
        ["graphics.ssao"] = ("Sombras e iluminação", 1), ["graphics.ssao_quality"] = ("Sombras e iluminação", 1), ["graphics.ssil"] = ("Sombras e iluminação", 1),
        ["graphics.glow"] = ("Pós-processamento e efeitos", 0), ["graphics.color_adjust"] = ("Pós-processamento e efeitos", 0), ["graphics.ssr"] = ("Pós-processamento e efeitos", 0), ["graphics.volumetric"] = ("Pós-processamento e efeitos", 0),
        ["graphics.fog"] = ("Ambiente", 1), ["graphics.particles"] = ("Ambiente", 1), ["graphics.water_waves"] = ("Ambiente", 1), ["graphics.vegetation"] = ("Ambiente", 1),
        ["world.object_distance"] = ("Objetos", 0), ["world.max_lights"] = ("Objetos", 0), ["world.max_light_shadows"] = ("Objetos", 0),
        ["avatars.visibility_distance"] = ("Avatares", 1), ["avatars.max_reduced"] = ("Avatares", 1), ["avatars.triangle_budget"] = ("Avatares", 1), ["avatars.animation_quality"] = ("Avatares", 1), ["avatars.texture_quality"] = ("Avatares", 1),
        ["perf.auto_quality"] = ("Adaptação automática", 0), ["perf.background_fps"] = ("Adaptação automática", 0),
        ["perf.shader_cache"] = ("Carregamento e cache", 1), ["perf.resource_cache"] = ("Carregamento e cache", 1), ["perf.preload_assets"] = ("Carregamento e cache", 1),
        ["audio.master"] = ("Volume", 0)
    };

    public static readonly IReadOnlyList<SettingDefinition> All = [.. Raw.Concat(Stage3()).Concat(Stage4()).Concat(CameraFree()).Select(d => Layout.TryGetValue(d.Id, out var place) ? d with { Group = place.Group, Column = place.Column } : d)];

    public static SettingDefinition? Find(string id) => All.FirstOrDefault(d => d.Id == id);

    /// <summary>What each quality preset gives to the settings it controls, taken from <see cref="GraphicsQuality"/> (one source of truth). "Automático" uses the preset picked for this machine.</summary>
    public static readonly SettingsPresetGroup Presets = BuildPresets();

    private static string SoftKey(RenderingServer.ShadowQuality q) => q switch { RenderingServer.ShadowQuality.Hard => "hard", RenderingServer.ShadowQuality.SoftVeryLow or RenderingServer.ShadowQuality.SoftLow => "low", RenderingServer.ShadowQuality.SoftMedium => "medium", RenderingServer.ShadowQuality.SoftHigh => "high", _ => "ultra" };
    private static string SsaoKey(RenderingServer.EnvironmentSsaoQuality q) => q switch { RenderingServer.EnvironmentSsaoQuality.VeryLow => "verylow", RenderingServer.EnvironmentSsaoQuality.Low => "low", RenderingServer.EnvironmentSsaoQuality.Medium => "medium", RenderingServer.EnvironmentSsaoQuality.High => "high", _ => "ultra" };
    private static string CascadesKey(DirectionalLight3D.ShadowMode m) => m switch { DirectionalLight3D.ShadowMode.Orthogonal => "1", DirectionalLight3D.ShadowMode.Parallel2Splits => "2", _ => "4" };

    private static SettingsPresetGroup BuildPresets()
    {
        var map = new Dictionary<string, IReadOnlyDictionary<string, object>>();
        foreach (var level in new[] { GraphicsQuality.Level.VeryLow, GraphicsQuality.Level.Low, GraphicsQuality.Level.Medium, GraphicsQuality.Level.High, GraphicsQuality.Level.Ultra })
        {
            var p = GraphicsQuality.PresetOf(level);
            map[GraphicsQuality.Keys[(int)level]] = new Dictionary<string, object>
            {
                ["graphics.shadow_distance"] = (int)p.ShadowDistance, ["graphics.msaa"] = p.Msaa.ToString(), ["graphics.shadows_enabled"] = p.Shadows, ["graphics.shadow_quality"] = SoftKey(p.SoftShadows),
                ["graphics.shadow_cascades"] = CascadesKey(p.ShadowMode), ["graphics.ssao"] = p.Ssao, ["graphics.ssao_quality"] = SsaoKey(p.SsaoQuality), ["graphics.ssil"] = p.Ssil, ["graphics.glow"] = p.Glow, ["graphics.color_adjust"] = p.Adjustment
            };
        }
        return new SettingsPresetGroup("graphics.preset", "custom", map) { Resolve = key => key == "auto" ? GraphicsQuality.Keys[(int)GraphicsQuality.Resolve(GraphicsQuality.Level.Auto)] : key };
    }
}

/// <summary>What each setting does in the game: it calls the system that already owns it (no controller is duplicated).</summary>
public static partial class SettingsAppliers
{
    public static SceneTree? Tree => Engine.GetMainLoop() as SceneTree;

    private static readonly string[] QualityIds = ["graphics.preset", "graphics.shadow_distance", "graphics.msaa", "graphics.shadows_enabled", "graphics.shadow_quality", "graphics.shadow_cascades", "graphics.ssao", "graphics.ssao_quality", "graphics.ssil", "graphics.glow", "graphics.color_adjust"];

    private static RenderingServer.ShadowQuality SoftOf(string key) => key switch { "hard" => RenderingServer.ShadowQuality.Hard, "low" => RenderingServer.ShadowQuality.SoftLow, "medium" => RenderingServer.ShadowQuality.SoftMedium, "high" => RenderingServer.ShadowQuality.SoftHigh, _ => RenderingServer.ShadowQuality.SoftUltra };
    private static RenderingServer.EnvironmentSsaoQuality SsaoOf(string key) => key switch { "verylow" => RenderingServer.EnvironmentSsaoQuality.VeryLow, "low" => RenderingServer.EnvironmentSsaoQuality.Low, "medium" => RenderingServer.EnvironmentSsaoQuality.Medium, "high" => RenderingServer.EnvironmentSsaoQuality.High, _ => RenderingServer.EnvironmentSsaoQuality.Ultra };
    private static DirectionalLight3D.ShadowMode CascadesOf(string key) => key switch { "1" => DirectionalLight3D.ShadowMode.Orthogonal, "2" => DirectionalLight3D.ShadowMode.Parallel2Splits, _ => DirectionalLight3D.ShadowMode.Parallel4Splits };

    /// <summary>
    /// The preset and the options it controls reach the game together: a preset decides by itself; Personalizada uses what the person set for each option.
    /// </summary>
    public static void ApplyQuality(SettingsStore store)
    {
        var preset = (string)store.Value("graphics.preset"); var custom = preset == "custom";
        GraphicsQuality.Tweak = custom ? new GraphicsQuality.Tweaks
        {
            Msaa = int.Parse((string)store.Value("graphics.msaa")), Ssao = store.Get<bool>("graphics.ssao"), SsaoQuality = SsaoOf((string)store.Value("graphics.ssao_quality")), Ssil = store.Get<bool>("graphics.ssil"),
            Glow = store.Get<bool>("graphics.glow"), Adjustment = store.Get<bool>("graphics.color_adjust"), Shadows = store.Get<bool>("graphics.shadows_enabled"), SoftShadows = SoftOf((string)store.Value("graphics.shadow_quality")),
            ShadowMode = CascadesOf((string)store.Value("graphics.shadow_cascades"))
        } : new();
        GraphicsQuality.ShadowDistanceOverride = custom ? store.Get<int>("graphics.shadow_distance") : null;
        GraphicsQuality.Choose(GraphicsQuality.Parse(preset));
    }

    public static void Register(SettingsStore store)
    {
        RegisterStage3(store);
        RegisterStage4(store);
        RegisterCameraFree(store);
        foreach (var id in QualityIds) store.RegisterApplier(id, _ => ApplyQuality(store));
        store.RegisterApplier("graphics.texture_quality", value => { GraphicsQuality.TextureMipmapBias = (string)value switch { "medium" => 1f, "low" => 2f, _ => 0f }; GraphicsQuality.Reapply(); });
        store.RegisterApplier("graphics.anisotropic", value => { GraphicsQuality.Anisotropic = (string)value switch { "0" => Viewport.AnisotropicFiltering.Disabled, "4" => Viewport.AnisotropicFiltering.Anisotropy4X, "8" => Viewport.AnisotropicFiltering.Anisotropy8X, "16" => Viewport.AnisotropicFiltering.Anisotropy16X, _ => Viewport.AnisotropicFiltering.Anisotropy2X }; GraphicsQuality.Reapply(); });
        store.RegisterApplier("graphics.ssr", value => { GraphicsQuality.Reflections = (bool)value; GraphicsQuality.Reapply(); });
        store.RegisterApplier("graphics.volumetric", value => { GraphicsQuality.VolumetricFog = (bool)value; GraphicsQuality.Reapply(); });
        store.RegisterApplier("graphics.fog", value => GraphicsQuality.WeatherFog = (bool)value);
        store.RegisterApplier("graphics.particles", value => ParticleQuality.Factor = (string)value switch { "medium" => .6f, "low" => .3f, "off" => 0f, _ => 1f });
        store.RegisterApplier("graphics.water_waves", value => OceanSurface.WavesEnabled = (bool)value);
        store.RegisterApplier("graphics.vsync", value => DisplayServer.WindowSetVsyncMode((bool)value ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled));
        store.RegisterApplier("graphics.fps_limit", value => Engine.MaxFps = Math.Clamp((int)value, 30, 360));
        store.RegisterApplier("graphics.render_scale", value => { if (Tree?.Root is { } root) RenderScale.SetBase((int)value / 100f, root); });
        store.RegisterApplier("graphics.dynamic_resolution", value => { RenderScale.Dynamic = (bool)value; if (!(bool)value && Tree?.Root is { } root) RenderScale.Apply(root, RenderScale.Base); });
        store.RegisterApplier("perf.render_distance", value => { if (Tree?.Root.GetCamera3D() is { } camera) camera.Far = (int)value; });
        store.RegisterApplier("perf.auto_quality", value => AutoQuality.Enabled = (bool)value);
        foreach (var id in new[] { "display.monitor", "display.resolution", "display.mode" })
            store.RegisterApplier(id, _ => DisplayOptions.Apply((string)store.Value("display.monitor"), (string)store.Value("display.resolution"), (string)store.Value("display.mode")));
        foreach (var id in new[] { "avatars.max_full", "avatars.lod", "avatars.simplify_distant", "avatars.visibility_distance", "avatars.max_reduced", "avatars.triangle_budget" }) store.RegisterApplier(id, _ => ApplyAvatarBudget(store));
        store.RegisterApplier("avatars.animation_quality", value => AvatarRig.AnimationDivisor = (string)value switch { "high" => 1, "low" => 3, _ => 2 });
        store.RegisterApplier("world.object_distance", value => ObjectCuller.Range = (int)value >= 1000 ? 0f : (int)value);
        foreach (var id in new[] { "world.max_lights", "world.max_light_shadows" }) store.RegisterApplier(id, _ => BuildObjectView.ObjectLight.SetLimits(store.Get<int>("world.max_lights"), store.Get<int>("world.max_light_shadows")));
        store.RegisterApplier("audio.master", value =>
        {
            var volume = Mathf.Clamp((float)value, 0f, 1f);
            AudioServer.SetBusVolumeDb(0, volume <= 0f ? -80f : Mathf.LinearToDb(volume)); AudioServer.SetBusMute(0, volume <= 0f);
        });
    }

    /// <summary>
    /// How far each avatar detail reaches, from the avatar settings: LOD moves the full and reduced ranges, "simplificar" removes the reduced step, the visibility distance is where avatars stop being drawn,
    /// and the limits cap how many are full or reduced and how many triangles all of them may cost.
    /// </summary>
    public static DetailBudget BudgetFor(int maxFull, string lod, bool simplify, int visibility = 120, int maxReduced = 24, int triangleBudgetThousands = 250)
    {
        var baseline = new DetailBudget(); var k = lod switch { "high" => 1.5f, "low" => .6f, _ => 1f };
        var reduced = (simplify ? baseline.ReducedRange : baseline.FullRange) * k;
        return baseline with
        {
            MaxFull = Math.Clamp(maxFull, 1, 40), FullRange = baseline.FullRange * k, ReducedRange = reduced, BasicRange = Math.Max(visibility, reduced + 1f),
            MaxReduced = simplify ? Math.Clamp(maxReduced, 0, 60) : 0, Triangles = Math.Clamp(triangleBudgetThousands, 50, 1000) * 1000
        };
    }

    private static void ApplyAvatarBudget(SettingsStore store)
    {
        if (Tree?.Root.GetNodeOrNull<NetworkManager>("NetworkManager") is { } network)
            network.AvatarBudget = BudgetFor(store.Get<int>("avatars.max_full"), (string)store.Value("avatars.lod"), store.Get<bool>("avatars.simplify_distant"), store.Get<int>("avatars.visibility_distance"), store.Get<int>("avatars.max_reduced"), store.Get<int>("avatars.triangle_budget"));
    }
}

/// <summary>The settings of this run: loaded from the active profile at start-up and shared by the window and the systems that still keep their own file.</summary>
public static class SettingsService
{
    /// <summary>The profiles live in <c>user://config/perfis</c>. A test run (a scene of res://Tests given on the command line) uses a folder of its own, so a smoke test never touches the person's real settings.</summary>
    public static bool IsTestRun => OS.GetCmdlineArgs().Any(a => a.StartsWith("res://Tests/", StringComparison.Ordinal));
    public static string Root => IsTestRun ? "user://config/perfis_teste" : "user://config/perfis";
    private static SettingsStore? _store;
    public static SettingsStore Store => _store ??= Create();

    private static SettingsStore Create()
    {
        var store = new SettingsStore(Root, SettingsCatalog.All, SettingsCatalog.Presets);
        SettingsAppliers.Register(store); SettingsAppliers.RegisterActions();
        return store;
    }

    /// <summary>Called once at start-up: brings in the old files (first time only) and applies the active profile.</summary>
    public static void Boot()
    {
        try
        {
            Store.ImportLegacy("user://settings.cfg", "user://viewer.cfg");
            // what the person chose in the places that kept their own files, brought in once
            Store.ImportOnce("notificacoes", new Dictionary<string, object> { ["notifications.toast_grupos"] = Notifications.NotificationPrefs.ToastsOn("grupos"), ["notifications.toast_convites"] = Notifications.NotificationPrefs.ToastsOn("convites"), ["notifications.toast_sistema"] = Notifications.NotificationPrefs.ToastsOn("sistema") });
            Store.ImportOnce("midia", new Dictionary<string, object> { ["privacy.external_media"] = BuildObjectView.MediaSettings.Enabled });
            Store.ImportOnce("construcao", new Dictionary<string, object> { ["build.import_collision"] = ModelImportDialog.ImportedCollisionShape });
            Store.ApplyAll();
        }
        catch (Exception exception) { GD.PushWarning($"As configurações não puderam ser carregadas: {exception.Message}"); }
    }

    /// <summary>The world was just built (a camera, the avatars of an island): what depends on it is applied again.</summary>
    public static void ApplyWorld() { Callable.From(() => { try { Store.ApplyAll(); SettingsAppliers.StartupDone = true; AccountSyncService.Instance.PullIfEnabled(); } catch (Exception exception) { GD.PushWarning($"As configurações não puderam ser reaplicadas: {exception.Message}"); } }).CallDeferred(); }

    /// <summary>A system that saved a value of its own (the old preferences window, the volume menu) tells the store, so both agree.</summary>
    public static void Mirror(string id, object value) { try { Store.SetAndApply(id, value); } catch (Exception exception) { GD.PushWarning($"Configuração {id}: {exception.Message}"); } }
}
