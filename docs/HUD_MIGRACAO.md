# Redesenho da HUD do LifeVerse — análise e plano de migração

Este documento responde ao pedido de "analisar a implementação atual, apresentar o plano de migração e identificar as integrações a preservar" **antes** de mexer na interface. Nada da HUD foi alterado ainda.

## 1. O que existe hoje

A HUD atual (descrita em `docs/VIEWER_UI.md` e `docs/PHASE28_INTERFACE_UX.md`) já tem boa parte do desenho do pedido. Ela vive principalmente em `client/Scripts/ViewerUI.cs` (630 linhas) e `ViewerUI.Bars.cs` (606), com o tema em `ViewerTheme.cs` e `UiStyle.cs` (cores, fonte Inter, ícones vetoriais SVG).

| Parte do pedido | Estado hoje |
|---|---|
| Barra superior (Eu, Comunicar, Mundo, Conteúdo, Ajuda, Desenvolver) | **Existe** (`BuildTopBar`, menus `PopupMenu`). À direita: saldo, coordenadas, hora, FPS, estado da conexão. Falta o botão "Comprar L$" separado, "Loja" e "Áudio" como no mockup, e a altura de 36 px |
| Barra de localização (voltar, avançar, início, ilha, coordenadas, favoritar, "Conectado · 47 avatares") | **Não existe como barra própria.** Coordenadas e conexão estão misturadas na barra superior; não há voltar/avançar/favoritar |
| Barra inferior (Falar, Mic, Gestos, Mover, Ver, Foto, Construir, Pessoas, Mapa, Inventário) | **Existe** (`BuildBottomBar`, `ToolButton`) com ícones e tooltips, mas com texto sempre visível, sem modo compacto, sem reordenar/ocultar/reposicionar, e com campo de chat embutido |
| Janelas independentes | **35 janelas** usam `FloatingWindow` (arrastar, fechar; 7 com minimizar; **só 8 redimensionáveis**, apenas pelo canto). Painéis antigos (inventário, social, marketplace, construção…) só se arrastam "pelo fundo" |
| Gerenciador de janelas (foco, persistência, limites, restaurar layout) | **Não existe.** Só há `Clamp` ao arrastar. Nada é salvo (posição, tamanho, aberta/fechada), nada reposiciona após mudar a resolução. A própria `PHASE28` registra isso como limitação |
| Chat local | **Existe** (janela flutuante com abas "Chat Local", "Conversas" e "Nova conversa", e campo na barra inferior). Falta: abas Local/Grupo/IM como no mockup, indicador de não lidas ao minimizar, tamanho/posição salvos, transparência |
| Mini-mapa | **Existe** (`MiniMap`, zoom pela roda, janela). Falta redimensionar com mínimo, botões de zoom +/−, opacidade e "abrir mapa completo" |
| Pessoas | **Existe** (`PeoplePanel` com abas Buscar, Amigos e Grupos). Faltam as abas Perto e Recente do mockup e o rodapé Perfil/IM/Teleportar |
| Barra lateral de atalhos | **Não existe** |
| Escala da interface | **Existe de forma simples:** Eu → Escala (80% a 150%) via `Window.ContentScaleFactor`, mais `ClientSettings.UiScale`. Falta Automático, 75%/85%, escala por DPI/resolução e recalcular tamanhos mínimos |
| Modos completo/compacto/imersivo | **Não existem** (há só ocultar a HUD no modo foto) |
| Configurações → Interface | **Não existe** uma seção: escala, volume e tutorial ficam no `viewer.cfg` e no menu Eu |
| Tema / paleta | Existe um tema escuro com Inter e ícones. As cores atuais foram medidas de outras referências: a paleta do pedido (`#1B262E`, `#12F2E0`, `#F4A340`…) precisa ser trazida para o tema |
| Persistência por usuário | `user://viewer.cfg` é **por máquina**, não por conta |

## 2. Integrações que precisam ser preservadas

Nada disso pode ser recriado, só reaproveitado:

- **Rede e sessão:** `NetworkManager` (conexão, chat, `ChatReceived`, estado, teleporte), `SessionManager` (usuário), `AppManager` (URL da API).
- **Economia:** saldo via `RequestWallet` / `OnWallet` (API), carteira e marketplace (`MarketplacePanel`).
- **Chat:** envio (`OnChatSubmit`), histórico, chat temporário na tela, grupos e IM (`GroupsWindow.Communication`, notificações).
- **Social:** `PeoplePanel`, perfis (`ProfileWindow`), amigos, grupos, teleporte para pessoas.
- **Mundo:** inventário (`InventoryPanel`), mapa/busca (`DiscoveryPanel`, `LandMapWindow`, `SearchWindow`), construção (`BuildHost`), terreno, gestos e menu radial, câmera (modos de Ver), foto (`TakeSnapshot`), voz (`ToggleVoice`, `ToggleOpenMic`), clima e ambiente.
- **Outros:** HUDs de scripts Lua (`ScriptHudLayer`, `HudEditor` — atenção ao nome: é outra coisa), notificações (`NotificationClient`), tutorial, atualização do cliente, ajuda de controles.
- **Persistência existente:** `viewer.cfg` (escala, volume, tutorial) e `ClientSettings` (resolução, janela, FPS, V-Sync, mouse, volume): migrar, não perder.
- **Testes:** smokes de UI do cliente que montam a `ViewerUI` e as janelas (`FloatingWindow`); não podem quebrar.

## 3. Plano em etapas (a HUD atual segue funcionando até a nova estar validada)

**Decisão de arquitetura (recomendada):** evoluir a `ViewerUI` atual **no lugar** e extrair para a estrutura modular (`client/Scripts/Interface/…`) aos poucos, em vez de reescrever tudo de uma vez. Há 35 janelas e dezenas de integrações; uma reescrita total é a forma mais provável de perder funções. A pasta sugerida no pedido (`UI/HUD/...`) fica como destino; o nome "HUD" já é usado pelos HUDs de scripts Lua (`HudEditor`, `ScriptHudLayer`), então proponho `Interface` para evitar confusão. As cenas `.tscn` do pedido viram classes C# montadas em código (como o resto do cliente).

### Etapa 0 — Base (sem mudança visual grande)
- `InterfaceSettings`: configurações **por conta** (`user://interface_{usuario}.cfg`), migrando `viewer.cfg`.
- `InterfaceScale`: escala **Automática** (por altura da janela e DPI) e manual 75/85/90/100/110/125/150%, recalculando tamanhos mínimos; sem zoom borrado.
- Tema com a paleta e as medidas do pedido (36 px na barra superior, bordas finas, raio 8–12, animações 100–180 ms, opacidade configurável).

### Etapa 1 — Gerenciador de janelas
- `WindowManager` central: registro por id das 35 janelas, **foco/trazer para frente, posição, tamanho, aberta/minimizada, salvos**, limites (cabeçalho sempre acessível), reposicionamento ao mudar a resolução e **Restaurar layout padrão**.
- Redimensionar por **todas as bordas e cantos** com mínimos, em todas as janelas, não só em 8.

### Etapa 2 — Barras
- `TopMenuBar` (36 px, Comprar L$ / Loja / Áudio / Configurações), `LocationBar` nova (voltar/avançar/início, ilha, coordenadas, favoritar, conexão e nº de avatares), `BottomToolbar` compacta (botões 38–44 px, modo só ícones, reordenar/ocultar/reposicionar, restaurar), `SideToolbar` recolhível.

### Etapa 3 — Janelas principais
- Chat (abas Local/Grupo/IM, minimizar para botão com não lidas, transparência), Mini-mapa (zoom +/−, redimensionar, opacidade), Pessoas (Perto/Amigos/Grupos/Recente, Perfil/IM/Teleportar).

### Etapa 4 — Modos e configurações
- Modos **completo / compacto / imersivo** (atalho para mostrar temporariamente; notificações críticas continuam), e **Configurações → Interface** com tudo o que o pedido lista.

### Etapa 5 — Validação
- Smokes em **1280×720, 1366×768, 1600×900, 1920×1080, 2560×1440, 3440×1440 e 3840×2160**, com janelas abertas e troca de resolução; testes das janelas (arrastar, redimensionar, minimizar, restaurar, salvar) e regressão dos botões que chamam serviços reais.

Cada etapa termina com o cliente compilando, os smokes passando e um commit, e a etapa seguinte só começa depois.

## 4. Decisões e riscos

1. **Evoluir no lugar (recomendado) ou reescrever?** Reescrever dá uma estrutura limpa mais rápido no papel, mas arrisca quebrar integrações sem aviso.
2. **Nome da pasta/namespace:** `Interface` em vez de `HUD` (evita colisão com os HUDs Lua).
3. **O mockup tem elementos fora do pedido** (etiquetas de nome com indicador de voz sobre os avatares, fundo de cena renderizado, lista com fotos). Tratar como referência de estilo, não como requisito.
4. **Medidas em pixels** do pedido valem a 1920×1080 e escala 100%; hoje a escala padrão do jogo é **80%**, então a interface atual já é mais compacta que 100% em 1080p. É preciso decidir se "100%" novo é o tamanho de referência do pedido (mais fiel) ou o tamanho atual (menos surpresa para quem já joga).
5. **Escala por zoom do Godot** (`ContentScaleFactor`) é o que existe; trocar por escala própria dos componentes é trabalho da Etapa 0 e pode exigir ajustes em janelas que usam tamanhos fixos.
6. **Ícones:** existem ícones SVG (`UiStyle.Icon`), mas faltam alguns do mockup (microfone com barra, trava, cadeado, estrela, setas de navegação).
7. Não verifiquei nada visualmente ainda: a conferência visual depende de abrir o jogo logado.

## 5. Andamento

### Etapa 0 — feita (parcial: paleta já existia)
- `client/Scripts/Interface/InterfaceSettings.cs`: preferências **por conta** em `user://interface_<conta>.cfg` (escala automática/manual, modo, opacidade das janelas). Na primeira vez, a escala do antigo `viewer.cfg` é copiada como escolha manual — ninguém perde a que tinha. O `viewer.cfg` segue sendo usado para volume e tutorial.
- `client/Scripts/Interface/InterfaceScale.cs`: **Automático** (80% em 1080p, proporcional à altura da janela, entre 75% e 150%) e manuais 75/85/90/100/110/125/150%. A `ViewerUI` reaplica a escala quando a janela muda de tamanho. Menu Eu → "Escala da interface".
- **Paleta:** conferida em `UiStyle` — `#1B262E`, `#12F2E0` e `#F4A340` do pedido já são as cores do tema; nada a trocar.
- **Não feito ainda:** escala por DPI do monitor; o "100%" continua sendo o zoom atual (padrão antigo era 80%), decisão do tamanho de referência segue em aberto.
- Teste: `InterfaceSettingsSmoke` (escala nas alturas 720/1080/1440/2160, menu, arquivo por conta, migração, nomes seguros).

### Etapa 1 — feita (gerenciador de janelas)
- `client/Scripts/Interface/WindowManager.cs`: toda `FloatingWindow` se registra sozinha ao entrar na árvore (as 35 janelas, sem mexer em cada uma). Guarda **posição e tamanho por conta** (seção `[windows]` de `interface_<conta>.cfg`, até 200 janelas), reaplica ao abrir, **mantém tudo dentro da tela** quando a resolução ou a escala mudam (`KeepAll`) e **restaura o layout padrão** (Eu → "Restaurar layout das janelas").
- `FloatingWindow`: **8 alças** (4 bordas + 4 cantos) em todas as janelas, com mínimo de 180×120 e sem sair da tela; salva ao soltar o mouse (arrasto, bordas e a alça antiga do canto). Foco/trazer para frente já existia ao clicar.
- Não guardado de propósito: se a janela estava aberta ou minimizada (abrir sozinha ao iniciar seria surpresa).
- Teste: `WindowManagerSmoke` (bordas, mínimos, limites da tela, salvar/voltar, janela fora da tela, restaurar).

### Etapa 2 — feita (barras)
- **Barra de localização** (nova, 34 px sob o menu; `ViewerUI.Interface.cs`): voltar / avançar (histórico das últimas 30 ilhas visitadas), início (Ilha de Boas-vindas), nome da ilha (clique abre o gerenciamento), coordenadas, **favoritar** (estrela, por conta, até 50) com lista de favoritos, e à direita "Conectado · N avatares" (conta você). O nome da ilha, o indicador e o estado da conexão saíram da barra superior para cá.
- **Barra superior:** "+" virou **Comprar L$**; novos **Loja**, **Áudio** (volume geral) e **Configurações**. Comprar L$ e Loja abrem hoje o mesmo painel (Marketplace e carteira).
- **Barra inferior:** novo botão **Gestos**; menu "⋯" para **personalizar**: só ícones, mostrar/ocultar cada botão (sempre resta um), mover para a esquerda/direita, restaurar a barra padrão. Salvo por conta. Se o texto não cabe na largura da tela, a barra passa sozinha a só ícones.
- **Barra lateral** (esquerda, centralizada): Inventário, Pessoas, Mapa, Construir, Foto, Configurações; recolhe e lembra o estado.
- Janelas deslocadas para não cobrir as barras (mini-mapa, avisos, editor de terreno; `WindowManager.TopLimit` = 72).
- **Não feito:** reposicionar a barra inferior para outro lado da tela; arrastar botões para reordenar (a ordem é pelo menu); nome do botão "Falar" e microfone continuam como já eram.
- Teste: `InterfaceBarsSmoke`. Captura de tela conferida pelo `TerrainSmoke` em 1600×900.

### Etapa 3 — feita (chat, mini-mapa, Pessoas)
- **Chat:** abas **Local / Grupo / IM** (+ nova conversa). Local é o chat de verdade; Grupo e IM abrem as janelas de Grupos e de Mensagens privadas (as conversas de grupo e privadas continuam nelas, não dentro do painel). **Minimizar** vira um botão "Chat" que conta as mensagens novas dos outros ("Chat · 2 novas"); clicar restaura. **Transparência** (100/80/60/40%) salva por conta.
- **Mini-mapa:** controle de **opacidade** (30–100%, salvo por conta), botão **Mapa completo** (abre o mapa de terrenos) e mínimo menor (220) — a janela agora redimensiona pelas bordas. Zoom +/− e bússola já existiam.
- **Pessoas:** abas **Buscar / Perto / Amigos / Grupos / Recente**. **Perto** lista os avatares da ilha por distância (atualiza a cada 1,5 s); **Recente** guarda até 20 pessoas vistas por perto (por conta). Botões **Perfil**, **IM** e **Ir até** (caminha até a pessoa; não teletransporta, e só funciona se ela estiver por perto). IM por enquanto só avisa para usar a aba IM do chat.
- **Não feito:** o painel do chat ainda não é arrastável nem guarda tamanho (só altura 250/480 pelo botão de expandir); não há indicador de não lidas para Grupo/IM; o rodapé das abas Amigos/Buscar segue como antes.
- Teste: `InterfaceWindowsSmoke`.

### Etapa 4 — feita (modos e Configurações → Interface)
- **Modos** (salvos por conta): **Completo** (tudo), **Compacto** (só menu superior e barra de ferramentas, com ícones; sem barra de localização e sem barra lateral) e **Imersivo** (nenhuma barra nem mini-mapa; avisos e o botão do chat continuam). No imersivo, **F10** mostra ou esconde a interface, e **Enter** mostra a barra inferior só até enviar a mensagem ou sair do campo.
- **Configurações → Interface** (nova janela; abre pelo ícone de engrenagem da barra superior ou por Eu → Interface…): escala (Automático e manuais), modo, opacidade das janelas / do chat / do mini-mapa, barra inferior só com ícones, barra de atalhos aberta, e os botões **Restaurar layout das janelas**, **Restaurar a barra de ferramentas** e **Restaurar toda a interface ao padrão** (favoritos e pessoas recentes não são apagados).
- A engrenagem do topo agora é um menu: "Gráficos, controles e som…" (a janela de preferências de antes) e "Interface…".
- **Não feito:** a janela Interface não tem "tamanho dos textos" nem "esconder o chat"; o chat não some no modo imersivo (só vira o botão se minimizado); não conferi o visual da nova janela no jogo aberto.
- Teste: `InterfaceModesSmoke`.

### Etapa 5 — feita (validação por resolução)
- `InterfaceResolutionSmoke`: abre **28 janelas ao mesmo tempo** e troca a janela do jogo por **1280×720, 1366×768, 1600×900, 1920×1080, 2560×1440, 3440×1440 e 3840×2160** (janela de verdade, não headless: o modo headless não reflete o tamanho). Em cada uma confere: a barra de menu ocupa a largura, a de localização fica logo abaixo, a barra inferior cabe na tela, a lateral cabe entre as duas, e **nenhuma janela fica fora da tela** nem sob as barras; depois confere os modos compacto/completo nas resoluções extremas. Passou nas sete. Rodar com: `Godot --path client --resolution 1280x720 res://Tests/InterfaceResolutionSmoke.tscn`.
- **Correção achada aqui:** o jogo já estica a interface a partir de 1920×1080 e o fator de escala multiplica isso. O "Automático" da Etapa 0 usava só a altura e *encolhia* a interface em telas pequenas. Agora ele sobe o fator até o tamanho efetivo nunca passar de 72% para baixo (1280×720 → 108%, 1366×768 → 101%, 1600×900 → 86%; de 1080p para cima fica em 80%). A barra inferior vira só ícones sozinha quando o texto não cabe (acontece em 1280×720).
- Os botões da barra inferior guardam o nome no `meta "label"` para os testes acharem mesmo em modo só ícones.
- **Limites desta validação:** ela mede posições e tamanhos, não o visual; não foi feita em monitor 3440×1440 ou 4K reais (a janela de teste pode ser limitada pelo monitor); a janela Interface e o fluxo logado (chat com gente de verdade, favoritos, Perto) continuam precisando de conferência humana no jogo.
