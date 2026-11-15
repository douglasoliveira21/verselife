using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts.Interface;

namespace LifeVerse.Client.Scripts.Settings;

/// <summary>The settings of the remaining categories: Geral e conta, Avatar, Chat, Privacidade, Notificações, Inventário, Construção, Mundo, Scripts, Rede, Interface e HUD, Acessibilidade e Avançado.</summary>
public static partial class SettingsCatalog
{
    private static SettingChoice[] Pick(params (string Id, string Label)[] items) => [.. items.Select(i => new SettingChoice(i.Id, i.Label))];
    private static string Yes(bool value) => value ? "Sim" : "Não";
    private static SceneTree? Tree => Engine.GetMainLoop() as SceneTree;
    private static NetworkManager? Network => Tree?.Root.GetNodeOrNull<NetworkManager>("NetworkManager");
    private static SessionManager? Session => Tree?.Root.GetNodeOrNull<SessionManager>("SessionManager");

    private static SettingDefinition Unavail(string id, SettingCategory category, string tab, string group, int column, string label, string help, SettingKind kind, object @default, string why) =>
        new(id, category, tab, label, help, kind, @default) { Group = group, Column = column, Unavailable = why, Choices = kind == SettingKind.Choice ? Pick((@default.ToString()!, "—")) : [] };

    private static SettingDefinition Action(string id, SettingCategory category, string tab, string group, int column, string label, string help, string button, string? confirm = null) =>
        new(id, category, tab, label, help, SettingKind.Action, "") { Group = group, Column = column, ButtonText = button, Confirm = confirm, Keywords = [label.ToLowerInvariant()] };

    private static SettingDefinition Info(string id, SettingCategory category, string tab, string group, int column, string label, string help, Func<string> source) =>
        new(id, category, tab, label, help, SettingKind.Info, "") { Group = group, Column = column, InfoSource = source, Keywords = [label.ToLowerInvariant()] };

    private static string Updates() => Tree?.Root.GetNodeOrNull<AppManager>("AppManager")?.Updates.Status switch
    {
        LifeVerse.Shared.Updates.UpdateStatus.Available => "Há uma versão nova", LifeVerse.Shared.Updates.UpdateStatus.Required => "Atualização obrigatória", LifeVerse.Shared.Updates.UpdateStatus.UpToDate => "Você está na versão mais recente", _ => "Não foi possível verificar"
    };

    private static string ConnectionText()
    {
        var network = Network; if (network is null) return "Sem conexão";
        return network.IsAuthenticated ? "Conectado a uma ilha" : network.CurrentIslandId != Guid.Empty ? "Conectando…" : "Sem ilha";
    }

    private static IEnumerable<SettingDefinition> Stage4()
    {
        // ---- Geral e conta
        yield return new("general.keep_on_top", SettingCategory.General, "Geral", "Janela sempre no topo", "Mantém a janela do jogo acima das outras, mesmo quando você usa outro programa.", SettingKind.Bool, false) { Group = "Janela", Column = 0, Keywords = ["janela", "topo", "sobrepor"] };
        yield return Unavail("general.language", SettingCategory.General, "Geral", "Idioma e região", 0, "Idioma", "Idioma do jogo.", SettingKind.Choice, "pt-BR", "O jogo só existe em Português (Brasil) por enquanto.");
        yield return Unavail("general.date_format", SettingCategory.General, "Geral", "Idioma e região", 0, "Região e formato de data", "Como datas e números aparecem.", SettingKind.Choice, "pt-BR", "O jogo usa o formato do Brasil e não tem outras regiões.");
        yield return Unavail("general.start_place", SettingCategory.General, "Geral", "Ao entrar", 1, "Ilha inicial", "Onde você aparece ao entrar.", SettingKind.Choice, "a", "A ilha inicial é escolhida pelo servidor (a sua, ou a Ilha de Boas-vindas).");
        yield return Info("general.updates", SettingCategory.General, "Geral", "Ao entrar", 1, "Atualizações", "Se há uma versão nova do cliente. O jogo verifica ao abrir.", Updates);
        yield return Action("general.download_update", SettingCategory.General, "Geral", "Ao entrar", 1, "Baixar atualização", "Abre a página de download da versão mais recente, se houver.", "Abrir download");
        yield return Info("account.user", SettingCategory.General, "Conta", "Conta", 0, "Usuário", "O nome da conta com a qual você entrou.", () => Session?.Username is { Length: > 0 } name ? name : "—");
        yield return Info("account.session", SettingCategory.General, "Conta", "Conta", 0, "Sessão neste dispositivo", "Se há uma sessão aberta aqui. A senha e o acesso não são guardados nas configurações.", () => Yes(Session?.IsAuthenticated == true) is var yes && yes == "Sim" ? "Aberta" : "Fechada");
        yield return Info("account.server", SettingCategory.General, "Conta", "Conta", 0, "Servidor da conta", "O endereço do servidor da conta a que este cliente se conecta.", () => Tree?.Root.GetNodeOrNull<AppManager>("AppManager")?.ApiBaseUrl ?? "—");
        yield return Info("account.connection", SettingCategory.General, "Conta", "Conta", 0, "Conexão", "Se o seu personagem está numa ilha agora.", ConnectionText);
        yield return Action("account.leave_island", SettingCategory.General, "Conta", "Sessão", 1, "Sair da ilha", "Desconecta você da ilha atual e volta à lista de ilhas.", "Sair da ilha", "Sair da ilha atual? Você pode entrar de novo pela lista de ilhas.");
        yield return Action("account.quit", SettingCategory.General, "Conta", "Sessão", 1, "Sair do LifeVerse", "Fecha o jogo.", "Sair do jogo", "Fechar o LifeVerse agora?");
        yield return Unavail("account.sessions", SettingCategory.General, "Conta", "Sessão", 1, "Outras sessões", "Ver e encerrar as sessões abertas em outros dispositivos.", SettingKind.Bool, false, "A conta ainda não lista nem encerra sessões de outros dispositivos.");

        // ---- Avatar e aparência
        yield return new("avatar.show_names", SettingCategory.Avatar, "Geral", "Mostrar nomes", "O nome sobre a cabeça dos outros avatares.", SettingKind.Bool, true) { Group = "Nomes", Column = 0, Keywords = ["nome", "avatares", "etiqueta"] };
        yield return new("avatar.show_own_name", SettingCategory.Avatar, "Geral", "Mostrar o meu nome", "O nome sobre a cabeça do seu próprio avatar.", SettingKind.Bool, true) { Group = "Nomes", Column = 0, Keywords = ["nome", "meu"] };
        yield return new("avatar.show_titles", SettingCategory.Avatar, "Geral", "Mostrar títulos", "O título do grupo ou cargo embaixo do nome.", SettingKind.Bool, true) { Group = "Nomes", Column = 0, Keywords = ["título", "grupo", "cargo"] };
        yield return new("avatar.footsteps", SettingCategory.Avatar, "Geral", "Som de passos", "O som dos passos quando um avatar anda ou corre.", SettingKind.Bool, true) { Group = "Movimento", Column = 1, Keywords = ["passos", "som", "andar"] };
        yield return new("avatar.default_animations", SettingCategory.Avatar, "Geral", "Animações padrão nos corpos personalizados", "Corpos enviados por jogadores usam as animações do jogo (andar, correr, pular, voar, nadar…). Desligado, eles usam só o movimento básico do corpo.", SettingKind.Bool, true)
            { Group = "Movimento", Column = 1, Keywords = ["animações", "corpo personalizado", "andar", "correr"] };
        yield return Action("avatar.open_mirror", SettingCategory.Avatar, "Geral", "Aparência", 0, "Espelho", "Abre o espelho para mudar o visual aqui, sem sair do jogo.", "Abrir o espelho");
        yield return Action("avatar.open_editor", SettingCategory.Avatar, "Geral", "Aparência", 0, "Editor de aparência", "Abre o editor de aparência em tela cheia (corpo, rosto, roupas, animações).", "Abrir o editor");

        // ---- Chat e social
        yield return new("chat.opacity", SettingCategory.Chat, "Conversa", "Transparência do chat", "Quão opaco é o painel da conversa. Menos opaco deixa ver o mundo atrás.", SettingKind.Int, 80) { Min = 30, Max = 100, Step = 5, Unit = "%", Group = "Aparência", Column = 0, Keywords = ["transparência", "opacidade", "chat"] };
        yield return new("chat.font_size", SettingCategory.Chat, "Conversa", "Tamanho do texto do chat", "Tamanho das letras das mensagens.", SettingKind.Int, 17) { Min = 12, Max = 28, Step = 1, Unit = " pt", Group = "Aparência", Column = 0, Keywords = ["fonte", "letra", "tamanho"] };
        yield return new("chat.show_time", SettingCategory.Chat, "Conversa", "Mostrar a hora nas mensagens", "A hora antes do nome em cada mensagem.", SettingKind.Bool, true) { Group = "Aparência", Column = 0, Keywords = ["hora", "horário"] };
        yield return new("chat.visible_seconds", SettingCategory.Chat, "Conversa", "Tempo na tela depois de uma mensagem", "Por quantos segundos o chat fica visível depois de chegar uma mensagem (se você não estiver escrevendo).", SettingKind.Int, 6) { Min = 3, Max = 30, Step = 1, Unit = " s", Group = "Comportamento", Column = 1, Keywords = ["histórico", "tempo", "esconder"] };
        yield return Unavail("chat.translate", SettingCategory.Chat, "Conversa", "Comportamento", 1, "Tradução das mensagens", "Traduz as mensagens recebidas.", SettingKind.Bool, false, "O jogo não tem serviço de tradução.");
        yield return Action("chat.open_people", SettingCategory.Chat, "Conversa", "Pessoas", 1, "Pessoas, amigos e grupos", "Abre a janela de Pessoas (buscar, amigos, grupos, vistos recentemente).", "Abrir Pessoas");

        // ---- Privacidade e segurança
        yield return new("privacy.external_media", SettingCategory.Privacy, "Geral", "Mídia externa (imagens e vídeos)", "Mostra imagens e vídeos que objetos e terrenos carregam de endereços da internet. Desligado, nada é baixado nem tocado e você não revela o seu endereço a esses sites.", SettingKind.Bool, true)
            { Group = "Conteúdo", Column = 0, Keywords = ["mídia", "vídeo", "imagem", "internet", "privacidade"] };
        yield return Unavail("privacy.online_visibility", SettingCategory.Privacy, "Geral", "Quem vê você", 0, "Visibilidade online", "Quem vê que você está online.", SettingKind.Choice, "a", "Depende de uma preferência de conta no servidor, que ainda não existe.");
        yield return Unavail("privacy.private_messages", SettingCategory.Privacy, "Geral", "Quem fala com você", 1, "Mensagens privadas", "Quem pode lhe mandar mensagens.", SettingKind.Choice, "a", "Depende de uma preferência de conta no servidor, que ainda não existe.");
        yield return Unavail("privacy.group_invites", SettingCategory.Privacy, "Geral", "Quem fala com você", 1, "Convites de grupo", "Quem pode convidar você para grupos.", SettingKind.Choice, "a", "Depende de uma preferência de conta no servidor, que ainda não existe.");
        yield return Unavail("privacy.friend_requests", SettingCategory.Privacy, "Geral", "Quem fala com você", 1, "Pedidos de amizade", "Quem pode pedir amizade.", SettingKind.Choice, "a", "Depende de uma preferência de conta no servidor, que ainda não existe.");
        yield return Unavail("privacy.blocked", SettingCategory.Privacy, "Geral", "Quem fala com você", 1, "Lista de bloqueados", "Ver e desbloquear pessoas.", SettingKind.Bool, false, "Você pode bloquear pelo menu da pessoa, mas o jogo ainda não lista nem desbloqueia pessoas.");
        yield return Unavail("privacy.script_permissions", SettingCategory.Privacy, "Geral", "Conteúdo", 0, "Permissões de scripts", "O que scripts podem pedir a você.", SettingKind.Bool, false, "As autorizações de scripts são dadas e revogadas por objeto, no editor de scripts do objeto.");

        // ---- Notificações
        yield return new("notifications.toast_grupos", SettingCategory.Notifications, "Avisos", "Grupos (cargos, avisos, pedidos)", "Mostra um aviso na tela. O sino continua guardando tudo.", SettingKind.Bool, true) { Group = "Quais avisos aparecem", Column = 0, Keywords = ["grupos", "aviso", "toast"] };
        yield return new("notifications.toast_convites", SettingCategory.Notifications, "Avisos", "Convites", "Mostra um aviso na tela. O sino continua guardando tudo.", SettingKind.Bool, true) { Group = "Quais avisos aparecem", Column = 0, Keywords = ["convites", "aviso"] };
        yield return new("notifications.toast_sistema", SettingCategory.Notifications, "Avisos", "Sistema, amigos e mensagens", "Mostra um aviso na tela. O sino continua guardando tudo.", SettingKind.Bool, true) { Group = "Quais avisos aparecem", Column = 0, Keywords = ["sistema", "amigos", "mensagens", "aviso"] };
        yield return new("notifications.dnd", SettingCategory.Notifications, "Avisos", "Não perturbe", "Só avisos de erro e de atenção aparecem na tela; o resto fica quieto (ainda vai para o sino).", SettingKind.Bool, false) { Group = "Como aparecem", Column = 1, Keywords = ["silencioso", "não perturbe", "quieto"] };
        yield return new("notifications.toast_seconds", SettingCategory.Notifications, "Avisos", "Duração dos avisos", "Quanto tempo um aviso comum fica na tela (avisos de erro ficam pelo menos 10 s).", SettingKind.Int, 4) { Min = 2, Max = 15, Step = 1, Unit = " s", Group = "Como aparecem", Column = 1, Keywords = ["tempo", "aviso"] };
        yield return new("notifications.max_toasts", SettingCategory.Notifications, "Avisos", "Avisos ao mesmo tempo", "Quantos avisos aparecem juntos; os outros esperam a vez.", SettingKind.Int, 3) { Min = 1, Max = 6, Step = 1, Group = "Como aparecem", Column = 1, Keywords = ["quantidade", "aviso"] };
        yield return Unavail("notifications.sound", SettingCategory.Notifications, "Avisos", "Como aparecem", 1, "Som dos avisos", "Toca um som quando chega um aviso.", SettingKind.Bool, false, "O jogo ainda não tem sons de notificação.");

        // ---- Inventário e conteúdo
        yield return new("inventory.default_sort", SettingCategory.Inventory, "Geral", "Ordem da lista", "Como o inventário ordena os itens ao abrir.", SettingKind.Choice, "0")
            { Choices = [.. Enumerable.Range(0, InventoryPanel.SortCount).Select(i => new SettingChoice(i.ToString(), InventoryPanel.SortLabel(i)))], Group = "Lista", Column = 0, Keywords = ["ordenar", "ordem", "nome", "data"] };
        yield return new("inventory.subfolders", SettingCategory.Inventory, "Geral", "Incluir subpastas", "Ao escolher uma pasta, mostra também o que está nas pastas dentro dela.", SettingKind.Bool, true) { Group = "Lista", Column = 0, Keywords = ["pasta", "subpastas"] };

        // ---- Construção e edição
        yield return new("build.snap", SettingCategory.Building, "Geral", "Encaixar na grade", "Posição, giro e tamanho caem em múltiplos dos passos abaixo.", SettingKind.Bool, false) { Group = "Editor", Column = 0, Keywords = ["grade", "snap", "encaixe"] };
        yield return new("build.space", SettingCategory.Building, "Geral", "Eixos", "Mundo: as setas seguem o mundo. Objeto: seguem os lados do objeto.", SettingKind.Choice, "world") { Choices = Pick(("world", "Mundo"), ("local", "Objeto")), Group = "Editor", Column = 0, Keywords = ["eixos", "local", "global"] };
        yield return new("build.move_step", SettingCategory.Building, "Geral", "Passo de movimento", "Distância de cada passo ao mover um objeto.", SettingKind.Float, .5f) { Min = .1, Max = 5, Step = .1, Unit = " m", Group = "Passos", Column = 1, Keywords = ["passo", "mover"] };
        yield return new("build.rotate_step", SettingCategory.Building, "Geral", "Passo de giro", "Ângulo de cada passo ao girar um objeto.", SettingKind.Float, 15f) { Min = 5, Max = 90, Step = 5, Unit = "°", Group = "Passos", Column = 1, Keywords = ["passo", "girar"] };
        yield return new("build.scale_step", SettingCategory.Building, "Geral", "Passo de tamanho", "Quanto muda o tamanho a cada passo.", SettingKind.Float, .1f) { Min = .05, Max = 1, Step = .05, Unit = " m", Group = "Passos", Column = 1, Keywords = ["passo", "tamanho", "escala"] };
        yield return new("build.import_collision", SettingCategory.Building, "Geral", "Colisão de modelos importados", "Forma de colisão que um modelo importado recebe ao ser colocado. Malha exata deixa entrar em casas e túneis, mas só vale para objetos parados.", SettingKind.Choice, "triangle")
            { Choices = Pick(("none", "Nenhuma"), ("box", "Caixa"), ("convex", "Casco convexo"), ("triangle", "Malha exata (só parado)")), Group = "Importação", Column = 0, Keywords = ["colisão", "importar", "modelo", "casa"] };

        // ---- Mundo, ilhas e teleportes
        yield return new("world.minimap_open", SettingCategory.World, "Geral", "Mini-mapa aberto", "Mostra o mini-mapa na tela.", SettingKind.Bool, true) { Group = "Mapa", Column = 0, Keywords = ["minimapa", "mapa"] };
        yield return new("world.show_coords", SettingCategory.World, "Geral", "Mostrar coordenadas", "As coordenadas do seu personagem na barra de localização.", SettingKind.Bool, true) { Group = "Mapa", Column = 0, Keywords = ["coordenadas", "posição"] };
        yield return Unavail("world.teleport_invites", SettingCategory.World, "Geral", "Teleportes", 1, "Convites de teleporte", "Aceitar convites para ir até alguém.", SettingKind.Bool, false, "O jogo ainda não tem convites de teleporte.");
        yield return Unavail("world.restricted_warnings", SettingCategory.World, "Geral", "Teleportes", 1, "Avisos de regiões restritas", "Avisa antes de entrar em áreas restritas.", SettingKind.Bool, false, "As regras de cada terreno já avisam sozinhas; não há opção para ligar ou desligar.");

        // ---- Scripts e interações
        yield return new("scripts.font_size", SettingCategory.Scripts, "Editor", "Tamanho do texto do editor", "Tamanho das letras do editor de scripts Lua.", SettingKind.Int, 16) { Min = 10, Max = 32, Step = 1, Unit = " pt", Group = "Editor Lua", Column = 0, Keywords = ["fonte", "lua", "editor"] };
        yield return new("scripts.indent", SettingCategory.Scripts, "Editor", "Largura da indentação", "Quantos espaços vale uma tabulação no editor.", SettingKind.Int, 4) { Min = 2, Max = 8, Step = 1, Unit = " espaços", Group = "Editor Lua", Column = 0, Keywords = ["indentação", "tab", "lua"] };
        yield return new("scripts.autocomplete", SettingCategory.Scripts, "Editor", "Sugestões de código", "Mostra sugestões de funções e eventos ao digitar.", SettingKind.Bool, true) { Group = "Editor Lua", Column = 0, Keywords = ["autocompletar", "sugestões", "lua"] };
        yield return Unavail("scripts.theme", SettingCategory.Scripts, "Editor", "Editor Lua", 0, "Tema do editor", "Cores do editor.", SettingKind.Choice, "a", "O editor de scripts tem um tema só, sem opção de trocar.");
        yield return Unavail("scripts.revoke", SettingCategory.Scripts, "Editor", "Autorizações", 1, "Revogar autorizações de scripts", "Tira as permissões que você deu a scripts.", SettingKind.Bool, false, "A revogação é feita por objeto, no painel de permissões do editor de scripts do objeto.");

        // ---- Rede e conectividade
        yield return new("network.auto_reconnect", SettingCategory.Network, "Conexão", "Reconectar sozinho", "Se a conexão com a ilha cair, o jogo tenta voltar sozinho (até três vezes).", SettingKind.Bool, true) { Group = "Conexão", Column = 0, Keywords = ["reconexão", "cair", "conexão"] };
        yield return Info("network.state", SettingCategory.Network, "Conexão", "Estado", 1, "Conexão com a ilha", "Se o seu personagem está conectado a uma ilha.", ConnectionText);
        yield return Info("network.avatars", SettingCategory.Network, "Conexão", "Estado", 1, "Pessoas na ilha", "Quantas pessoas estão na ilha com você.", () => Network is { IsAuthenticated: true } network ? (network.RemoteAvatarCount + 1).ToString() : "—");
        yield return Info("network.protocol", SettingCategory.Network, "Conexão", "Estado", 1, "Versão do protocolo", "A versão da conversa entre o jogo e o servidor da ilha. Precisa ser a mesma dos dois lados.", () => LifeVerse.Shared.Networking.NetworkProtocol.Version.ToString());
        yield return Unavail("network.ping", SettingCategory.Network, "Conexão", "Qualidade", 1, "Ping, jitter e perda de pacotes", "Mede a qualidade da conexão.", SettingKind.Bool, false, "O cliente ainda não mede ping, jitter nem perda de pacotes.");
        yield return Unavail("network.download_limit", SettingCategory.Network, "Conexão", "Qualidade", 1, "Limite de download", "Limita a velocidade com que o jogo baixa conteúdo.", SettingKind.Int, 0, "O jogo não limita a velocidade de download.");

        // ---- Interface e HUD
        yield return new("interface.scale", SettingCategory.Interface, "Geral", "Escala da interface", "Tamanho de tudo que é interface (menus, janelas, botões, textos). Automático acompanha o tamanho da janela do jogo.", SettingKind.Choice, "auto")
            { Choices = [.. ViewerUI.ScaleIds.Select((id, i) => new SettingChoice(id, InterfaceScale.MenuLabels()[i]))], Group = "Tamanho e modo", Column = 0, Keywords = ["escala", "zoom", "tamanho", "dpi"] };
        yield return new("interface.mode", SettingCategory.Interface, "Geral", "Modo da interface", "Completo: tudo. Compacto: só menu e barra de ferramentas, com ícones. Imersivo: só o mundo (F10 mostra a interface).", SettingKind.Choice, "full")
            { Choices = Pick(("full", "Completo"), ("compact", "Compacto"), ("immersive", "Imersivo")), Group = "Tamanho e modo", Column = 0, Keywords = ["modo", "imersivo", "compacto"] };
        yield return new("interface.window_opacity", SettingCategory.Interface, "Geral", "Opacidade das janelas", "Quão opacas são as janelas do jogo.", SettingKind.Int, 92) { Min = 30, Max = 100, Step = 1, Unit = "%", Group = "Transparência", Column = 0, Keywords = ["transparência", "janelas"] };
        yield return new("interface.map_opacity", SettingCategory.Interface, "Geral", "Opacidade do mini-mapa", "Quão opaca é a janela do mini-mapa.", SettingKind.Int, 92) { Min = 30, Max = 100, Step = 1, Unit = "%", Group = "Transparência", Column = 0, Keywords = ["transparência", "minimapa"] };
        yield return new("interface.compact_toolbar", SettingCategory.Interface, "Geral", "Barra inferior só com ícones", "Esconde os nomes dos botões da barra inferior.", SettingKind.Bool, false) { Group = "Barras", Column = 1, Keywords = ["barra", "ícones", "compacta"] };
        yield return new("interface.side_bar", SettingCategory.Interface, "Geral", "Barra de atalhos aberta", "A barra de atalhos na borda esquerda da tela.", SettingKind.Bool, true) { Group = "Barras", Column = 1, Keywords = ["barra lateral", "atalhos"] };
        yield return Action("interface.restore_layout", SettingCategory.Interface, "Geral", "Restaurar", 1, "Layout das janelas", "Cada janela volta ao lugar e ao tamanho originais.", "Restaurar janelas");
        yield return Action("interface.restore_toolbar", SettingCategory.Interface, "Geral", "Restaurar", 1, "Barra de ferramentas", "Ordem, botões escondidos e modo só ícones voltam ao padrão.", "Restaurar a barra");
        yield return Action("interface.restore_all", SettingCategory.Interface, "Geral", "Restaurar", 1, "Toda a interface", "Escala, modo, opacidades, barras e janelas voltam ao padrão (favoritos e pessoas recentes ficam).", "Restaurar tudo", "Restaurar toda a interface ao padrão?");

        // ---- Acessibilidade
        yield return new("accessibility.reduce_motion", SettingCategory.Accessibility, "Geral", "Reduzir movimento da interface", "Os avisos aparecem e somem sem esmaecer, e a janela de configurações não pisca a opção que você abriu.", SettingKind.Bool, false) { Group = "Movimento", Column = 0, Keywords = ["movimento", "animação", "reduzir"] };
        yield return Info("accessibility.keyboard", SettingCategory.Accessibility, "Geral", "Teclado", 0, "Navegação por teclado", "As janelas de configurações funcionam só com o teclado: Tab e as setas movem o foco, Enter ativa, Esc fecha, Ctrl + F busca.", () => "Disponível");
        yield return Unavail("accessibility.text_scale", SettingCategory.Accessibility, "Geral", "Texto e contraste", 1, "Escala de texto", "Aumenta só o texto.", SettingKind.Choice, "a", "Use a Escala da interface (Interface e HUD): ela aumenta texto e botões juntos.");
        yield return Unavail("accessibility.contrast", SettingCategory.Accessibility, "Geral", "Texto e contraste", 1, "Alto contraste", "Cores com mais contraste.", SettingKind.Bool, false, "O jogo ainda não tem um tema de alto contraste.");
        yield return Unavail("accessibility.flashes", SettingCategory.Accessibility, "Geral", "Texto e contraste", 1, "Reduzir flashes", "Evita luzes piscando.", SettingKind.Bool, false, "O jogo não tem efeitos que piscam: não há o que reduzir.");
        yield return Unavail("accessibility.captions", SettingCategory.Accessibility, "Geral", "Som", 0, "Legendas", "Mostra em texto os sons e a fala.", SettingKind.Bool, false, "O jogo ainda não tem legendas.");
        yield return Unavail("accessibility.screen_reader", SettingCategory.Accessibility, "Geral", "Som", 0, "Leitor de tela", "Fala os elementos da interface.", SettingKind.Bool, false, "O cliente ainda não habilita o suporte a tecnologias assistivas do Godot.");

        // ---- Avançado e diagnóstico
        yield return Info("advanced.version", SettingCategory.Advanced, "Diagnóstico", "Este cliente", 0, "Versão do cliente", "A versão do LifeVerse que está aberta.", () => AppManager.Version);
        yield return Info("advanced.godot", SettingCategory.Advanced, "Diagnóstico", "Este cliente", 0, "Versão do Godot", "O motor em que o jogo roda.", () => Engine.GetVersionInfo()["string"].AsString());
        yield return Info("advanced.system", SettingCategory.Advanced, "Diagnóstico", "Este cliente", 0, "Sistema", "O sistema operacional.", () => $"{OS.GetName()} {OS.GetVersion()}");
        yield return Info("advanced.gpu", SettingCategory.Advanced, "Diagnóstico", "Este cliente", 0, "Placa de vídeo", "A placa de vídeo que o sistema informa.", () => RenderingServer.GetVideoAdapterName());
        yield return Info("advanced.renderer", SettingCategory.Advanced, "Diagnóstico", "Este cliente", 0, "Renderizador", "O método e o driver de desenho.", () => $"{RenderingServer.GetCurrentRenderingMethod()} · {RenderingServer.GetCurrentRenderingDriverName()}");
        yield return Info("advanced.profile", SettingCategory.Advanced, "Diagnóstico", "Este cliente", 0, "Perfil de configurações", "O perfil de configurações em uso.", () => SettingsService.Store.ActiveProfile);
        yield return Info("advanced.data_folder", SettingCategory.Advanced, "Diagnóstico", "Arquivos", 1, "Pasta de dados", "Onde o jogo guarda configurações, caches e fotos neste computador.", () => ProjectSettings.GlobalizePath("user://"));
        yield return Action("advanced.open_data", SettingCategory.Advanced, "Diagnóstico", "Arquivos", 1, "Abrir a pasta de dados", "Abre a pasta de dados do jogo no explorador de arquivos.", "Abrir pasta");
        yield return Action("advanced.open_logs", SettingCategory.Advanced, "Diagnóstico", "Arquivos", 1, "Abrir a pasta de logs", "Abre a pasta com os registros do jogo (úteis para quem for ajudar).", "Abrir pasta");
        yield return Action("advanced.clear_cache", SettingCategory.Advanced, "Diagnóstico", "Manutenção", 1, "Limpar o cache de recursos", "Apaga modelos, texturas e miniaturas baixados; eles são baixados de novo quando precisar. Não apaga fotos nem configurações.", "Limpar cache", "Apagar os recursos baixados (modelos, texturas e miniaturas)? Eles serão baixados de novo quando o jogo precisar.");
        yield return Action("advanced.export_diagnostics", SettingCategory.Advanced, "Diagnóstico", "Manutenção", 1, "Exportar diagnóstico", "Grava um arquivo de texto com versão, sistema, placa de vídeo, desempenho e configurações (sem senhas nem dados da conta) para você mandar a quem for ajudar.", "Exportar");
        yield return Action("advanced.restore_all", SettingCategory.Advanced, "Diagnóstico", "Manutenção", 1, "Restaurar todas as configurações", "Volta todas as configurações do perfil atual ao padrão (não apaga a conta, as fotos nem os outros perfis).", "Restaurar tudo", "Restaurar todas as configurações do perfil atual ao padrão? Nada é apagado da conta; o perfil volta ao que o jogo traz.");
        yield return Unavail("advanced.verify_files", SettingCategory.Advanced, "Diagnóstico", "Manutenção", 1, "Verificar arquivos do jogo", "Confere se os arquivos do jogo estão íntegros.", SettingKind.Bool, false, "O jogo ainda não tem verificação de integridade dos arquivos.");
        yield return Unavail("advanced.error_report", SettingCategory.Advanced, "Diagnóstico", "Manutenção", 1, "Enviar relatório de erros", "Envia um relatório automático quando o jogo falha.", SettingKind.Bool, false, "Não há serviço de relatórios de erro; use Exportar diagnóstico e mande o arquivo.");
    }
}
