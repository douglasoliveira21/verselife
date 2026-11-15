# Menu de Configurações do LifeVerse — análise e plano

Resposta ao pedido de "inspecionar o projeto antes de implementar". **Nada do menu novo foi implementado ainda.** Este documento diz o que existe, o que falta, o que o projeto não suporta e como proponho entregar em etapas, como foi feito com a HUD (`HUD_MIGRACAO.md`).

## 1. O que existe hoje

| Peça | Estado |
|---|---|
| Janela de preferências (`SettingsPanel`) | Uma caixa simples: largura, altura, limite de FPS, tela cheia, V-Sync, escala, **Qualidade gráfica** (nova). Salva em `user://settings.cfg` |
| Configurações da interface (`InterfaceSettingsWindow`, na `ViewerUI`) | Escala, modo, opacidades, barras. Por conta, em `user://interface_<conta>.cfg` |
| Qualidade gráfica (`GraphicsQuality`) | Baixo / Médio / Alto / Ultra / Automático: MSAA, SSAO, SSIL, glow, tonemap, sombras do sol |
| Volume geral | Menu Eu / Áudio, no bus Master, salvo em `user://viewer.cfg` |
| Notificações (`NotificationPrefs`) | Existe, `user://notification_prefs.cfg` |
| Construção (`ModelImportDialog`) | Preferências em `user://build_prefs.cfg` (tamanho, colisão ao importar) |
| Mídia / música de parcela | `user://media.cfg`, `user://land_media.cfg` |
| Câmera | Distância 1,5–12 m (roda do mouse), sensibilidade e inverter Y vêm do **avatar da conta** (servidor) |
| Controles | 19 ações no `InputMap` do projeto (`project.godot`); **não há tela de remapear** |
| Voz | Falar (push-to-talk), microfone aberto, silenciar tudo, alcance de audição. **Sem escolha de dispositivo, sem teste de entrada, sem sensibilidade** |
| Estatísticas | `PerformanceOverlay` mostra FPS e memória, escondido por padrão. Nenhum uso de `Performance` / `RenderingServer.GetRenderingInfo` fora de teste |
| LOD de avatares | `AvatarDetailPlanner` (Completo / Reduzido / Básico / Oculto) com faixas de distância e orçamento; hoje fixo, sem tela |
| Preferências de conta no servidor | **Só** as do avatar (`GET /api/v1/users/me/avatar`: câmera e aparência). **Não existe** endpoint de preferências gerais |

Resumo: há ~10 arquivos de configuração soltos, cada um com seu formato; só os gráficos e a interface têm tela.

## 2. As 18 categorias: o que dá e o que não dá

Legenda: **Real** = há sistema por trás e a opção funciona; **Parcial** = parte funciona; **Não suportado** = o projeto não tem o recurso (será mostrado como "indisponível", não simulado).

| Categoria | Situação |
|---|---|
| Geral e conta | Real: idioma da interface (só pt-BR existe → seletor com uma opção), janela (tela cheia/ancorada), ilha inicial (`StartPlace`), atualizações, nome da conta. **Não suportado:** região, formato de data configurável, listar/encerrar outras sessões (não há API) |
| Gráficos e desempenho | Real: predefinições, MSAA, SSAO, SSIL, glow, sombras, monitor/resolução/modo/taxa (`DisplayServer`), V-Sync, escala de renderização (`Scaling3DScale`, FSR), limite de FPS, LOD de avatares, estatísticas do renderizador (`RenderingServer.GetRenderingInfo`: objetos, draw calls, primitivas, memória de vídeo/texturas). **Parcial:** distância de renderização/sombras (sombras sim; "renderização" só para objetos do jogo), renderização dinâmica (precisa de controlador novo). **Não suportado:** % de uso de CPU/GPU (o Godot não expõe) — aparece "indisponível"; reflexos de tela (SSR) e volumétrico só se eu ligar no ambiente; partículas/vegetação/água em níveis (hoje sem níveis) |
| Áudio | Parcial: hoje só existe o bus Master. Preciso **criar buses** (Ambiente, Efeitos, Música, Mídia, Interface, Voz) e direcionar os sistemas atuais para eles. Saída de áudio: real (`AudioServer`). Silenciar em segundo plano: real. Áudio espacial: já é 3D, falta só o parâmetro |
| Comunicação por voz | Parcial: push-to-talk, microfone aberto, alcance e volume já existem. **Faltam** seleção de dispositivos (`AudioServer` lista), teste de entrada (medidor real) e sensibilidade. **Não suportado:** redução de ruído e cancelamento de eco (o Godot não tem; dependeriam do LiveKit, que segue **não verificado** nos docs) |
| Controles e movimentação | Real e novo: remapear as 19 ações com detecção de conflito, salvar e restaurar. Sensibilidade e inverter eixos (hoje no avatar da conta) |
| Câmera e visualização | Real: distância, FOV, sensibilidade, suavização, primeira/terceira/livre. **Parcial:** altura/deslocamento, colisão (existe `SpringArm3D`), modo foto (existe `TakeSnapshot`, sem opções) |
| Avatar e aparência | Parcial: exibição de nomes, indicador de voz, simplificação, LOD (reais); animações/postura/expressões dependem do que o avatar já oferece |
| Chat e social | Parcial: transparência do chat e histórico (existem na HUD). **Não suportado:** tradução (não há serviço), cores/fontes só se eu implementar |
| Privacidade e segurança | Depende do **servidor**: visibilidade online, mensagens, convites, bloqueios existem em partes do módulo social; permissões de scripts e mídia externa precisam de regra + endpoint. Troca de senha/sessões: não há tela |
| Notificações | Real: `NotificationPrefs` já existe; falta tela e "não perturbe" |
| Inventário e conteúdo | Parcial: ordenação/visualização existem no `InventoryPanel`; confirmações e cache dependem de cada um |
| Construção e edição | Real: grade, snapping, eixos (já existem no editor); as preferências de importação já estão em `build_prefs.cfg` |
| Mundo, ilhas e teleportes | Parcial: minimapa (HUD), favoritos (HUD), coordenadas, convites de teleporte (depende do servidor) |
| Scripts e interações | Parcial: depende do editor Lua (`LuaScriptEditor`): tema, fonte e indentação teriam de ser implementados nele |
| Rede e conectividade | Parcial: estado da conexão é real; **ping/jitter/perda** só se o ENet do cliente expuser (preciso verificar); limite de download não existe |
| Interface e HUD | Real: o que já fiz na HUD (escala, modo, opacidades, barras) migra para cá; "tamanho dos botões" e "perfis de HUD" são novos |
| Acessibilidade | Parcial: escala de texto e alto contraste dá para fazer no tema; redução de movimento/flashes dependem dos efeitos existentes; legendas, leitor de tela e tecnologias assistivas: **não suportado hoje** (o Godot 4.5+ tem acessibilidade nativa, mas não está ligada) |
| Avançado e diagnóstico | Real: versão, logs, limpar cache, abrir pasta de dados, restaurar padrões, exportar diagnóstico. "Verificação de arquivos" e "relatório de erros" dependem de serviço |

**Regra que vou seguir:** nenhum controle sem efeito real. Quando o sistema não existir, a linha aparece desabilitada com "Indisponível neste cliente" e o motivo, e entra numa lista no fim deste arquivo.

## 3. Arquitetura proposta

1. **Esquema tipado** (`SettingDefinition`): id, categoria, aba, rótulo, descrição, palavras-chave, tipo (bool, inteiro, decimal, escolha, texto, tecla), padrão, faixa, "exige reinício", "aplicador" e "controlado pela predefinição". Uma tabela só alimenta a **interface, a busca, a validação, os padrões, o Descartar e os perfis** — por isso a busca acha configurações reais.
2. **Armazenamento** (`SettingsStore`): valores atuais + valores pendentes (para Aplicar/Descartar); persistência local **por perfil** em `user://config/perfis/<nome>.cfg`, com `schema_version` e migração. Na primeira abertura importa os arquivos soltos (`settings.cfg`, volume do `viewer.cfg`, `interface_<conta>.cfg`, `notification_prefs.cfg`).
3. **Aplicadores**: cada configuração chama o sistema existente (`GraphicsQuality`, `DisplayServer`, `AudioServer`, `InputMap`, `ViewerUI`, `NotificationPrefs`…). Não duplico controladores.
4. **Janela**: `FloatingWindow`/overlay próprio com cabeçalho, barra lateral com rolagem, busca (Ctrl+F), abas internas, rodapé fixo; componentes reutilizáveis (botão, dropdown, slider, toggle, campo, aba, tooltip, modal) sobre `UiStyle`/`ViewerTheme`.
5. **Conta**: etapa à parte — endpoint novo `GET/PUT /api/v1/users/me/preferences` (JSON validado e com tamanho limitado, autenticado) + migração de banco; só valores seguros (nada de credenciais). O servidor continua mandando nas regras (limites de LOD, física, voz).
6. **Perfis**: criar, renomear, duplicar, excluir (nunca o Padrão), importar/exportar `.json`.

## 4. Etapas

- **Etapa 0 — Base:** `SettingsSchema` + `SettingsStore` + perfis + migração dos arquivos atuais + testes.
- **Etapa 1 — Janela e componentes:** cabeçalho, barra lateral (18 categorias), busca, rodapé (Restaurar padrão, Descartar, Aplicar), modais de confirmação, responsividade (uma coluna em telas pequenas), teclado. Primeira categoria completa: **Gráficos e desempenho › Geral** como no mockup.
- **Etapa 2 — Gráficos (5 abas):** predefinições com a miniatura vinda de **captura real** de cada nível, monitor/resolução/modo/taxa com **confirmação e reversão em 15 s**, escala de renderização, renderização dinâmica, indicadores na tela e aba **Estatísticas** com dados reais do renderizador.
- **Etapa 3 — Áudio, Voz, Controles, Câmera:** buses de áudio, dispositivos e teste de microfone, remapeamento de teclas com conflitos, câmera.
- **Etapa 4 — Demais categorias:** Avatar, Chat, Notificações, Inventário, Construção, Mundo, Scripts, Rede, **Interface e HUD** (migra o que já existe), Acessibilidade, Avançado.
- **Etapa 5 — Conta e validação:** endpoint + migração de preferências de conta, importar/exportar perfis, testes nas resoluções 1280×720 a 3840×2160 (e ultrawide), regressão dos sistemas integrados.

Cada etapa termina compilando, com os smokes passando e o documento atualizado; a seguinte só começa depois.

## 5. Riscos e decisões

1. **Escopo.** São 18 categorias, ~150 opções. Muitas dependem de sistemas que não existem (tradução, redução de ruído, leitor de tela, ping detalhado). Vou implementar o que for real e listar o resto.
2. **Substituir ou conviver.** Proponho que a janela nova **substitua** o `SettingsPanel` e a janela "Interface" (os itens de menu passam a abri-la na categoria certa). Mantê-las em paralelo duplicaria a fonte da verdade.
3. **Servidor.** Preferências de conta exigem endpoint e migração de banco (as migrações Phase88/89 ainda não foram aplicadas em banco real — este é mais um motivo para fazer a conta por último).
4. **Mockup.** Uso como referência de layout e paleta. A imagem do mundo na predefinição será captura real; o fundo de cena do mockup é arte e não requisito.
5. **Escala/DPI.** A janela usa os tokens de `UiStyle` e a escala atual da HUD; o "1770×1010 a 1920×1080" do pedido vale como proporção.

## 6. Não suportado hoje (será mostrado como indisponível)

Uso de CPU e GPU em %, redução de ruído e cancelamento de eco do microfone, tradução de chat, listar e encerrar outras sessões, região e formato de data, leitor de tela, legendas, limite de download, limpeza/verificação de arquivos pelo servidor. (Lista será atualizada a cada etapa.)

## 7. Andamento

### Etapa 0 — feita (base)
Código em `client/Scripts/Settings/`:
- **`SettingDefinition`** (+ `SettingCategory`, `SettingCategories`, `SettingsSearch`): a tabela de uma configuração (categoria, aba, rótulo, descrição, palavras-chave, tipo, padrão, faixa/passo, "exige reinício", "controlada pela predefinição", "indisponível: motivo"). `Normalize` valida e limita qualquer valor (número fora da faixa, texto com caracteres de controle, escolha inexistente, NaN → padrão). A **busca** ignora maiúsculas e acentos, exige todas as palavras e ordena por onde achou (nome > palavra-chave > aba > categoria > descrição); `Highlights` devolve os trechos para destacar.
- **`SettingsStore`**: valores em uso × **pendentes** (`Set` só propõe; `Apply` valida, grava o perfil e roda o aplicador de cada sistema; `Discard` descarta); `ResetCategory`/`ResetAll`; **predefinições** (escolher uma propõe os valores dela; mudar à mão uma opção controlada vira "Personalizada"); **perfis** em `user://config/perfis/*.cfg` (criar, copiar do atual, duplicar, renomear, excluir — o Padrão nunca —, trocar, limite de 30, nomes únicos); **exportar/importar** JSON (formato próprio, 64 KB no máximo, valores validados, chaves desconhecidas descartadas, nome nunca repetido); **versão de esquema** (`SchemaVersion = 2`) com renomeações (perfil antigo é lido com os nomes novos e regravado); **importação dos arquivos antigos** (`settings.cfg` e `viewer.cfg`) uma única vez, com o volume do menu da `ViewerUI` valendo sobre o do `settings.cfg`.
- **`SettingsCatalog`**: por enquanto 4 configurações reais — predefinição gráfica, V-Sync, limite de FPS, volume geral — cada uma com um aplicador de uma linha que chama o sistema dono (`GraphicsQuality`, `DisplayServer`, `Engine`, `AudioServer`). Entram mais conforme as etapas.
- **`SettingsService`**: carrega o perfil ativo na inicialização (`ClientSettings._Ready`) e recebe os "espelhos" do `ClientSettings` (janela de preferências antiga) e do menu de volume da `ViewerUI`, para os dois caminhos concordarem enquanto a janela nova não substitui a antiga (Etapa 1).
- Nada sensível é gravado: só ids do catálogo, só os tipos que o catálogo permite.
- Teste: `SettingsStoreSmoke` (regras de valor, busca, pendente/aplicar/descartar, predefinições, perfis, exportar/importar com arquivos hostis, migração de esquema, arquivos antigos, sanidade do catálogo real).
- **Ainda não existe janela**: a Etapa 1 monta a interface sobre esta base.

### Etapa 1 — feita (janela e componentes; Gráficos › Geral)
- **Janela** (`SettingsWindow`, aberta pela engrenagem do topo → "Todas as configurações…", por Eu → "Configurações…" e pelo atalho da barra lateral): ~92% da tela, centralizada, sobre o mundo; cabeçalho (logo, "LifeVerse", separador, engrenagem, título, **minimizar / maximizar-restaurar / fechar**; arrastar pelo cabeçalho, duplo clique maximiza; nunca sai da tela); barra lateral com **busca** (Ctrl + F, ignora acentos, destaca os trechos, clique leva à opção e a pisca), as **18 categorias** (ícone, seleção com borda luminosa, teclado, rolagem até a selecionada) e **perfis** (trocar, +, renomear, duplicar, excluir, exportar/importar JSON); conteúdo com título, subtítulo, seletor de escopo ("Aplicação: Local"; "Conta" desabilitado até a etapa de conta) e abas; **rodapé** com Restaurar padrão (pede confirmação), Descartar e Aplicar alterações (ligados só com mudanças pendentes). Fechar com mudanças pendentes pergunta (Aplicar e fechar / Descartar e fechar / Continuar editando). Enquanto aberta, o personagem não anda e o Enter não abre o chat.
- **Responsivo:** duas colunas de painéis a partir de 1400 unidades de largura de página; abaixo disso empilha em uma coluna com rolagem. Verificado de 1100×650 a 3225×1350 unidades (que cobrem de 1280×720 a 3840×2160 e ultrawide com escala Automática).
- **Componentes** (`SettingsUi`, `SettingsDialog`): painel, linha com ajuda (ⓘ com o efeito, o custo e se exige reinício), toggle, dropdown, slider com valor, campo, botões, aba, modal de confirmação (com contagem regressiva). Compartilham os tokens do `UiStyle`/`ViewerTheme`.
- **Gráficos e desempenho › Geral** (as outras 4 abas dizem "ainda sem opções", sem simular nada):
  - *Predefinição gráfica:* Automático (mostra a recomendada para a máquina), Muito baixa, Baixa, Média, Alta, Ultra, Personalizada, com **miniatura real** (captura do mundo de cada nível, `Assets/ui/presets`, gerada por `Tests/PresetThumbnails`) e descrição. Mudar à mão a distância de sombras torna a predefinição Personalizada; escolher uma predefinição devolve os valores dela.
  - *Tela e exibição:* monitor, resolução (só em modo Janela; em tela cheia vale a do monitor), modo (Janela / Tela cheia / Tela cheia exclusiva) — **com confirmação e reversão automática em 15 s** —, V-Sync, **escala de renderização** 50–150% (FSR abaixo de 100%), **renderização dinâmica** (baixa a escala se os FPS caem abaixo da meta, nunca abaixo de 60% dela) e a taxa de atualização do monitor (informação; o jogo não a altera).
  - *Desempenho:* limite de FPS, distância de renderização (câmera), distância de sombras, limite de avatares completos, LOD de avatares e "simplificar avatares distantes" (os três mexem no orçamento de detalhe do `NetworkManager`). **LOD de objetos** aparece desabilitado: o jogo não tem LOD de objetos.
  - *Indicadores na tela:* FPS, memória (RAM e VRAM em uso pelo jogo) e tempo de renderização (CPU e GPU em ms, medidos pelo renderizador). **Uso de CPU e GPU em %** aparece desabilitado: o Godot não informa isso.
- **Integração:** cada configuração chama o sistema dono (`DisplayServer`, `Engine`, `Viewport`, `Camera3D`, `NetworkManager.AvatarBudget`, `GraphicsQuality`); o perfil é reaplicado quando o mundo carrega. Um perfil com predefinição que não é Personalizada passa a ter os valores dela (correção feita aqui: antes a sombra podia ficar com o padrão).
- **Ainda não:** abas Qualidade, Mundo e avatares, Otimização e Estatísticas (Etapa 2); as outras 16 categorias (etapas 3 e 4); sincronização de conta (etapa 5). A janela "Interface" antiga continua em Eu › Interface… até a Etapa 4.
- Testes: `SettingsWindowSmoke` (janela, categorias, abas, busca, rodapé, perfis, diálogos com contagem, teclado, estados da janela, 7 tamanhos) e `SettingsStoreSmoke`/`GraphicsQualitySmoke` atualizados. Capturas: `Tests/SettingsCapture`.

### Etapa 2 — feita (as quatro abas que faltavam em Gráficos e desempenho)
Todas as opções abaixo mudam o jogo de verdade (teste `SettingsEffectsSmoke` confere cada uma contra o motor). Quando a predefinição não é Personalizada, ela decide os valores das opções que controla; em Personalizada vale o que a pessoa pôs.
- **Qualidade** — *Imagem:* texturas (Alta/Média/Baixa, pelo viés de mipmap), filtragem anisotrópica (desligada a 16x), antialiasing (MSAA 2x/4x/8x; FXAA e TAA não existem de propósito, borram o nome dos avatares). *Sombras e iluminação:* sombras do sol, qualidade das sombras (duras a suaves ultra), divisões/cascatas (1, 2, 4), oclusão ambiente e sua qualidade, iluminação indireta de tela. *Pós-processamento e efeitos:* brilho (bloom), ajuste de cor, reflexos de tela (SSR), raios de luz (névoa volumétrica). *Ambiente:* neblina do clima, partículas (Todas/Médias/Poucas/Desligadas: chuva e efeitos novos de objetos), ondas da água. **Vegetação** aparece desabilitada: árvores e grama são objetos dos jogadores, o jogo não tem vegetação com níveis.
- **Mundo e avatares** — distância de visibilidade de objetos (esconde o que passa da distância; a colisão continua), limite de luzes visíveis e de luzes com sombra (valem na hora para as luzes que já existem), distância de visibilidade de avatares, máximo de avatares com detalhe reduzido, orçamento de triângulos dos avatares, qualidade das animações (detalhe reduzido animado a cada 1, 2 ou 3 quadros). **Qualidade das texturas dos avatares** aparece desabilitada (segue "Texturas"). *Limite de avatares completos, LOD de avatares e simplificação continuam na aba Geral, como no mockup.*
- **Otimização** — ajuste automático de desempenho (se os FPS ficam bem abaixo da meta por ~15 s, baixa a predefinição um nível, no máximo 1 vez por minuto, avisa com um aviso na tela e nunca mexe em Personalizada) e redução em segundo plano (limita os FPS quando a janela perde o foco). **Cache de shaders**, **tamanho do cache de recursos** e **pré-carregamento de assets** aparecem desabilitados com o motivo (o Godot gerencia o cache de shaders sozinho; o cache de recursos não tem limite nem remoção; não há pré-carregamento de ilhas). A renderização dinâmica está na aba Geral.
- **Estatísticas** — só leituras do motor, atualizadas a cada 0,5 s: FPS atual e médio (5 s), tempo de quadro, processamento, física, renderização CPU e GPU (medidas pelo renderizador só enquanto a aba está aberta), RAM, VRAM (total, texturas, buffers), objetos, triângulos, chamadas de desenho, nós, recursos, nós órfãos, avatares desenhados e na ilha por nível de detalhe, placa de vídeo e renderizador.
- Detalhes: as predefinições do `GraphicsQuality` são a única fonte dos valores (o catálogo as lê); uma opção que nenhuma predefinição controla (texturas, reflexos, volumétrico, neblina…) vale sempre.
- **Limites conhecidos:** efeitos de objetos já criados só mudam de quantidade quando são criados de novo; reflexos de tela não afetam a água (transparente); o tempo de CPU/GPU só aparece depois de 1–2 quadros com a aba aberta; não conferi no jogo logado em ilha cheia.

### Etapa 3 — feita (Áudio, Comunicação por voz, Controles e movimentação, Câmera e visualização)
Teste: `SettingsControlsSmoke` (cada opção contra o jogo, mais as teclas, conflitos e o teste do microfone na janela).
- **Áudio:** criados os canais **Ambiente, Efeitos, Música, Mídia e Voz** (todos para o Master) e cada som do jogo foi enviado ao seu: som do mundo → Ambiente; passos e sons de objetos → Efeitos; música de entrada e de terrenos → Música; áudio de mídia de objetos → Mídia; vozes → Voz. Volume por canal (zero silencia), **saída de áudio** (lista real do sistema; um dispositivo desligado volta ao padrão) e **silenciar em segundo plano**. **Interface** aparece desabilitada: o jogo não tem sons de interface.
- **Voz › Microfone:** voz ligada ao entrar, microfone aberto, **dispositivo de entrada** (lista real), **sensibilidade** (vira o limiar do microfone aberto) e o **teste do microfone** (liga o microfone só enquanto você pede e a aba está aberta, mostra um medidor de volume com a linha onde o microfone aberto começa a enviar; nada é gravado nem enviado). **Redução de ruído** e **cancelamento de eco** aparecem desabilitados (o jogo não tem; use fones). **Voz › Audição:** volume das vozes, distância de audição (5–25 m; o servidor só envia vozes até 25 m), voz espacial (ligada: direção e queda com a distância) e indicadores de fala (a bolinha e as ondas ao lado do nome).
- **Controles › Teclas:** as **20 ações** do jogo, cada uma com tecla principal e alternativa, em Português (Seta ↑, Espaço…), aplicadas ao mapa de entrada do motor. Clicar no botão espera a próxima tecla (Esc cancela; Backspace/Delete limpa a alternativa). **Conflitos:** uma tecla já usada pergunta se quer **trocar** (a outra ação fica com a sua tecla antiga); o conflito também aparece em vermelho. **Teclas reservadas** (Esc, Enter, F10, Delete — menus, chat, modo da interface e apagar objetos) são recusadas. Nova ação **Falar (apertar para falar)**, tecla X por padrão: funciona como o botão Falar da barra inferior. **Mouse e movimento:** sensibilidade do mouse (×0,25 a ×3 somado à do avatar), inverter o eixo vertical, correr sempre, correr com duas batidas e voar segurando o pulo.
- **Câmera › Câmera:** modo ao entrar (1ª/3ª pessoa), campo de visão (50–110°), distância, passo da roda do mouse, altura, deslocamento lateral, suavização do giro e colisão da câmera. **Foto:** formato (PNG/JPG) e incluir ou não a interface na foto.
- **Limites:** o volume geral de música/mídia só afeta o que o jogo manda para esses canais (o áudio da pré-visualização de assets continua no Master); a voz ligada/microfone aberto valem **ao entrar na ilha** (os botões da barra continuam valendo para a sessão e não gravam); mudar a distância de audição só encurta, não alcança além do que o servidor envia; a câmera livre e o modo foto não têm ajustes próprios além dos acima.

### Etapa 4 — feita (as demais categorias)
Agora as **18 categorias** têm conteúdo. Cada linha é uma de quatro coisas: uma **opção** (vale no jogo), um **botão** (faz algo uma vez), um **valor de leitura** (mostra uma informação real) ou uma linha **desabilitada com o motivo**. Botões e valores de leitura nunca são gravados no perfil. Teste: `SettingsMoreSmoke`.
- **Geral e conta:** janela sempre no topo; atualizações (estado + botão para abrir o download); aba Conta com usuário, sessão, servidor da conta e conexão, e os botões *Sair da ilha* e *Sair do LifeVerse* (com confirmação). *Desabilitados:* idioma e região (só Português do Brasil), ilha inicial (o servidor escolhe) e outras sessões (a conta não lista).
- **Avatar e aparência:** mostrar nomes dos outros, o meu nome e os títulos; som de passos; animações padrão nos corpos personalizados; botões para o espelho e o editor de aparência.
- **Chat e social:** transparência, tamanho do texto, hora nas mensagens e quanto tempo o chat fica na tela; botão para Pessoas. *Desabilitado:* tradução.
- **Privacidade e segurança:** mídia externa (imagens e vídeos de objetos e terrenos: liga/desliga o download e a reprodução). *Desabilitados, com o motivo:* visibilidade online, mensagens privadas, convites de grupo e pedidos de amizade (dependem de preferências de conta no servidor, que não existem), lista de bloqueados e permissões de scripts (feitas por objeto).
- **Notificações:** quais avisos aparecem (grupos, convites, sistema/amigos/mensagens — o sino guarda tudo), **Não perturbar**, duração e quantidade de avisos. *Desabilitado:* som dos avisos.
- **Inventário e conteúdo:** ordem da lista e incluir subpastas. **Construção e edição:** encaixar na grade, eixos (mundo/objeto), passos de movimento, giro e tamanho (valem no editor aberto na hora) e a **colisão padrão de modelos importados**.
- **Mundo, ilhas e teleportes:** mini-mapa aberto e coordenadas na barra de localização. *Desabilitados:* convites de teleporte e avisos de regiões restritas.
- **Scripts e interações:** tamanho do texto, indentação e sugestões de código do editor Lua. *Desabilitados:* tema do editor e revogar autorizações (a revogação é por objeto, no painel de permissões do editor).
- **Rede e conectividade:** reconectar sozinho; estado da conexão, pessoas na ilha e versão do protocolo. *Desabilitados:* ping/jitter/perda de pacotes e limite de download (o cliente não mede nem limita).
- **Interface e HUD:** escala, modo, opacidade das janelas e do mini-mapa, barra inferior só com ícones, barra de atalhos e os botões de restaurar (janelas, barra, tudo). **Substitui a antiga janela "Interface"**: os menus agora abrem aqui; o que a pessoa já tinha escolhido naquela conta é trazido uma vez para o perfil, e o que os menus da barra mudam por conta própria aparece aqui.
- **Acessibilidade:** reduzir movimento da interface (avisos sem esmaecer, sem piscar a opção aberta) e a nota da navegação por teclado. *Desabilitados:* escala só do texto (use a Escala da interface), alto contraste, reduzir flashes (não há efeitos que piscam), legendas e leitor de tela.
- **Avançado e diagnóstico:** versão do cliente, do Godot, sistema, placa de vídeo, renderizador, perfil e pasta de dados; botões para abrir a pasta de dados e a de logs, **limpar o cache de recursos**, **exportar diagnóstico** (texto com versões, sistema, vídeo, desempenho e todas as configurações; sem senhas nem dados da conta) e **restaurar todas as configurações** (com confirmação). *Desabilitados:* verificar arquivos e relatório de erros.
- **Infra:** `SettingKind.Action` e `SettingKind.Info`; `SettingsActions` (botões por id); `ImportOnce` traz uma vez os valores de arquivos antigos (notificações, mídia, colisão de modelos, interface da conta); **testes e capturas agora usam uma pasta de perfis própria** (`perfis_teste`), então nenhum smoke mexe nas suas configurações reais.
- **Limites:** as preferências de privacidade e o idioma só ganham sentido com a sincronização de conta (Etapa 5); alguns valores só se aplicam ao abrir a janela correspondente (inventário e editor de scripts aplicam na hora se já abertos); "Sair da ilha" e os botões do mundo só funcionam com o mundo aberto.

### Etapa 5 — feita (sincronização com a conta e validação final)
Testes: `AccountPreferencesTests` (backend, 14 casos) e `AccountSyncSmoke` (cliente, contra um servidor de mentira que aplica as regras reais).
- **Servidor:** `GET/PUT/DELETE /api/v1/users/me/preferences` (ASP.NET Core, exige login; o usuário vem do token). Tabela `identity.user_preferences` (uma linha por pessoa, `jsonb`), migração **`Phase90AccountPreferences`** — **ainda não aplicada a nenhum banco** (como as Phase88 e Phase89; aplique ao publicar o servidor).
- **Regras compartilhadas** (`shared/Settings/AccountPreferenceRules.cs`, usadas pelo servidor e pelo cliente): só as chaves da lista são aceitas (interface, chat, notificações, avatar, controles e teclas, câmera, construção, scripts, inventário, acessibilidade, privacidade de mídia, voz); cada valor é conferido em tipo e faixa; no máximo 160 chaves e 16 KB. Chave desconhecida, tipo errado ou fora da faixa = 400 e **nada é salvo**. `null` esquece a chave.
- **Concorrência:** cada gravação leva a revisão que o aparelho viu; se outro aparelho mudou antes, o servidor responde 409 com os valores atuais e não grava.
- **Fica só no computador (nunca sai):** gráficos, tela, dispositivos de áudio, desempenho, métricas, escala da interface, limites de avatares e de luzes. Nenhuma senha, token ou dado pessoal é enviado ou guardado; o arquivo local `perfis_conta.json` guarda só "ligado", a revisão e o id da conta.
- **Janela:** o seletor **Aplicação: Local / Conta** agora funciona. "Conta" fica desabilitado sem login. Ao escolher, a janela explica o que sincroniza; se a conta está vazia, envia os valores deste aparelho; se já tem valores, pergunta **usar as da conta** ou **substituir pelas deste dispositivo**. Com a sincronização ligada, **Aplicar alterações** envia só o que mudou e é sincronizável; um conflito abre a mesma pergunta; sem conexão o valor local fica e o rodapé avisa. Ao carregar o mundo, as preferências da conta são buscadas e aplicadas. A ajuda (?) de cada opção diz se ela segue a conta.
- **Consistência:** o `AccountSyncSmoke` confere que toda regra do servidor existe no catálogo do cliente, com o mesmo tipo, faixa dentro da do servidor, opções aceitas e padrão válido.
- **Validação final:** `InterfaceResolutionSmoke` (28 janelas × 7 resoluções), `SettingsWindowSmoke` (7 tamanhos, 1280×720 a ultrawide), `SettingsStore/More/Controls/Effects/GraphicsQuality/InterfaceSettings` passaram.
- **Não suportado / limites:** alto contraste, leitor de tela, tradução, idioma, ping/jitter, limite de download, redução de ruído e eco, ações por objeto (revogar scripts) continuam desabilitados com o motivo. Não há resolução de conflito por chave (a escolha é do conjunto todo). Não testado contra o servidor real (migração não aplicada) nem com duas contas em aparelhos diferentes.
