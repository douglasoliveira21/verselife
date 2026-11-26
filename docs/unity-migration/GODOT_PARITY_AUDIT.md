# Levantamento: o que o cliente Godot tem e o cliente Unity não tem

Feito em 25/11/2026, sobre o commit `a4cf1d7`.

## Como foi feito

1. Listei os 294 arquivos do cliente Godot (`client/Scripts` e `client/Autoloads`) com a descrição de cada um.
2. Extraí as rotas da API que cada cliente chama: o Godot usa 189; **95 não aparecem em lugar nenhum do Unity** (lista no fim).
3. Procurei no código Unity cada função descrita no Godot (telas, abas, operações da ilha, configurações).
4. Medi o tamanho de cada área nos dois clientes.

Ressalva: a busca é por código, não por uso. Um item marcado como "existe" pode estar mais simples que no Godot; um item "falta" foi confirmado pela ausência de rota, tela ou tratamento.

## O quadro geral

| | Godot | Unity |
|---|---|---|
| Código do cliente | 3,9 MB (52 mil linhas) | 1,0 MB (15 mil linhas, mas linhas mais longas) |
| Rotas da API usadas | 189 | 94 do Godot cobertas, 95 faltando |
| Configurações | 220, em 18 categorias | cerca de 30, em 6 abas |

Em tamanho, o cliente Unity tem perto de **um quarto** do cliente Godot. O mundo, o avatar, a construção básica, a rede e o áudio estão equivalentes; o que falta está quase todo nas **janelas de gestão e de plataforma**, que no Godot são grandes e no Unity são versões curtas.

## Por área

| Área | Godot | Unity | Estado | Prioridade |
|---|---|---|---|---|
| Gestão da ilha | 13 seções, 355 KB | feita em 25/11: 16 seções (ver abaixo) | Quase equivalente | Baixa |
| Gestão de terrenos e "Sobre este terreno" | 323 KB | feita em 25/11: 10 seções e 8 abas (ver abaixo) | Quase equivalente | Baixa |
| Configurações | 18 categorias, 196 opções no catálogo, 272 KB | feito em 25 e 26/11: o mesmo catálogo; 128 das 196 agem no Unity, 33 têm o mesmo motivo do Godot para não agir e 43 ainda faltam (ver abaixo) | Quase equivalente | Baixa |
| Notificações ao vivo (websocket) | sino, central, socket ao vivo | feito em 25/11 (ver abaixo) | Equivalente | — |
| Busca global | 7 abas, 166 KB | feita em 25/11: 9 abas no serviço de busca (ver abaixo) | Quase equivalente | Baixa |
| Inventário | 87 KB: pastas, lixeira, mover, copiar, pacotes | feito em 25/11: 10 vistas (ver abaixo) | Quase equivalente | Baixa |
| Grupos | 2 janelas grandes, 10 abas, 245 KB | feito em 25/11: central e perfil com as 10 abas (ver abaixo) | Quase equivalente | Baixa |
| Carteira e compra de VerseCoin | 545 linhas: comprar moedas, extrato, taxas | feito em 25/11: resumo, extrato com recibo, comprar, taxas, saldo na barra | Equivalente | — |
| Perfil | 7 abas, feed, galeria, conquistas | feito em 25/11: 8 abas (ver abaixo) | Quase equivalente | Baixa |
| Editor de avatar | corpo próprio, roupas próprias, animações próprias, visuais salvos, espelho no mundo | feito em 26/11: estúdio do avatar e o guarda-roupa no corpo (ver abaixo) | Quase equivalente | Baixa |
| Editor de anexos | gizmo 3D, câmera no ponto do corpo | feito em 26/11: editor no corpo com setas, anéis e caixas, imagens e construções vestidas, pontos por região (ver abaixo) | Equivalente | — |
| Construção | 37 arquivos, 494 KB | editor com 12 abas; luzes, reflexos, texto flutuante e texturas do sistema feitos em 25/11 | Quase equivalente | Baixa |
| Editor de scripts | completar, depurar com pontos de parada, modelos | feitos em 26/11: completar, verificação do servidor, console, desempenho, permissões, parar, pausar e depurar com linhas de parada (ver abaixo) | Quase equivalente | Baixa |
| Tela de entrada | fundo, luzes, música, notícias, estado do servidor, "esqueci a senha" | feitos em 26/11: fundo, música, notícias, estado dos servidores, esqueci a senha, lugar escolhido pelo servidor e pré-carregamento da ilha (ver abaixo) | Quase equivalente | Baixa |
| Câmera e jogador | câmera livre (ALT, CTRL), roda para primeira pessoa, balanço da cabeça | feitos em 26/11: câmera livre, foco e roda (ver abaixo); falta a câmera solta | Quase equivalente | Baixa |
| Interface (casca) | modos compacto e imersivo, barra configurável, histórico de ilhas, menu de clique direito | feitos em 26/11: modos, barra configurável, chat minimizado, histórico de ilhas, menu do botão direito e foto F12 (ver abaixo) | Equivalente | — |
| Mapa-múndi | 10 arquivos | mapa, marcos, pastas, imagens, terrenos | Quase equivalente | Baixa |
| Mercado imobiliário e propriedades | 6 arquivos | 2 janelas | Quase equivalente | Baixa |
| Mundo, avatar, rede, áudio, voz | | | Equivalente (fases 7 a 11) | — |

## O que falta, área por área

### 1. Gestão da ilha (Godot: `IslandManagement/`, 19 arquivos)
O Unity tem geral, permissões, regras, terrenos e mídia numa janela só. Faltam:
- **Acesso:** modo de entrada (pública, só convidados, fechada) e lista de pessoas liberadas com validade (`management/access`, `access/allow`, `access/mode`).
- **Banimentos:** contadores, banir com motivo e duração, histórico de banimentos (`management/bans`).
- **Administração:** quem está na ilha agora, expulsar, aviso para todos ou para uma pessoa (`management/server`, `server/kick`, `server/notice`).
- **Histórico e auditoria** da gestão e da construção (`management/audit`).
- **Ambiente:** predefinições salvas de hora, clima e céu (`environment/presets`).
- **Objetos e prims:** orçamento, lista com dono e custo, devolver objetos de um dono (`management/objects`, `objects/return`, `objects/return-owner`, `objects/owners`).
- **Desempenho:** estado do servidor e gráficos das últimas horas (`management/performance`).
- **Física:** objetos simulados e congelar ou liberar (`management/physics/freeze`, `release`).
- **Reinicialização:** agora, em minutos ou num horário, com contagem e cancelamento (`management/restart`).
- **Terreno:** desenho do solo e cópias de segurança com restauração (`management/terrain/backups`).
- **Propriedade:** venda e aluguel da ilha inteira dentro da gestão.
- **Geral completo:** capa, classificação, idioma, uso de prims.

### 2. Terrenos (Godot: `LandManagement/`, `LandManager.*`, `ParcelInfo/`, `LandMapWindow`)
- **Janela "Gerenciamento de Terrenos"** com 10 seções: visão geral, mapa de parcelas, demarcação, lista, vendas, aluguéis, contratos, prims e recursos, permissões, histórico.
- **"Sobre este Terreno"** com 8 abas, para qualquer pessoa no terreno onde está.
- **Mapa de território** desenhado das células, colorido por situação.
- **Acesso de grupo** a um terreno (`land/parcels/{id}/group-access`).
- **Presença no terreno:** aviso ao entrar, voz desligada pelo terreno (o Godot mostra o nome do terreno que desligou a voz).
- **Avisos imobiliários** (`properties/notices`).

### 3. Configurações (Godot: `Settings/`, 16 arquivos)
O Godot tem 220 opções em 18 categorias; o Unity tem gráficos, áudio, controles, interface, acessibilidade e conta. Faltam, entre outras:
- **Câmera** (39 opções): câmera livre, órbita, zoom, deslocamento, colisão, campo de visão, distância, altura, ombro, suavidade.
- **Gráficos** (23): antialiasing, oclusão, brilho, sombras finas, escala de renderização, "Automático" pela placa de vídeo, ondas do mar.
- **Tela:** monitor, resolução e modo de janela de verdade (`DisplayOptions`).
- **Mundo** (18), **avançado** (14), **voz** (10), **privacidade** (8), **avatares** (8), **notificações** (7), **desempenho** (7), **rede** (6), **chat** (6), **construção** (6), **scripts** (5), **inventário** (2).
- **Busca na janela, perfis de configuração** e restaurar padrão por categoria.

### 4. Notificações ao vivo (Godot: `Notifications/`, `NotificationClient`)
Esta é a falta mais funda: o Godot mantém um **websocket** (`/api/v1/notifications/ws`) que traz eventos na hora. Sem ele, o Unity não recebe sozinho:
- Pedidos de amizade, convites de grupo e de teleporte.
- Mensagens do chat de grupo ao vivo.
- Mudança de saldo (`wallet_changed`).
- **Sino com contador e central de notificações** (Todas, Grupos, Convites, Sistema), com "marcar como lido" (`notifications/center`, `center/read`).
- Preferências de quais avisos aparecem.

### 5. Busca global (Godot: `SearchWindow.*`, 9 arquivos)
O Godot usa o serviço de busca próprio (`/api/v1/search`, `search/suggest`, `popular`, `recent`, `recommended`, `interests`, `favorites`, `report`, `search/events`). O Unity busca pela descoberta, de forma simples. Faltam:
- As 7 abas (Tudo, Pessoas, Lugares, Eventos, Grupos, Itens, Lojas) com a coluna de detalhes de cada resultado.
- Sugestões enquanto digita, buscas recentes, populares e recomendadas.
- Favoritos da busca e denúncia de resultado.
- Filtros de itens por preço, criador e direitos.
- Links do jogo (`lifeverse://evento/…`, pessoa, grupo, item, loja, lugar).

### 6. Inventário (Godot: `InventoryPanel.cs`, 1.122 linhas)
- **Pastas:** criar, renomear, mover, esvaziar, restaurar (`inventory/folders`).
- **Lixeira:** mandar, restaurar, apagar de vez, esvaziar (`inventory/trash`, `trash/empty`, `items/{id}/purge`).
- **Mover e copiar** itens (`inventory/move`, `items/{id}/copy`).
- **Abrir pacotes** (`packs/unpack`), **espaço usado** (`inventory/storage`), **verificar e consertar** (`integrity`, `integrity/repair`).
- **Vestir pelo inventário** (`items/{id}/equip`), marcar como usado, aceitar atualização de item, visibilidade do histórico.
- Ordenação e "incluir subpastas" pelas configurações.

### 7. Grupos (Godot: `Groups/`, `GroupsWindow.*`)
O Unity cria, convida, conversa e lista membros. Faltam:
- **Perfil de grupo com 10 abas:** geral, membros, cargos, permissões, avisos, chat, terrenos, economia, propostas, configurações.
- **Criar grupo em 4 passos** com validação.
- **Descobrir grupos** com filtros (categoria, idioma, forma de entrada, só gratuitos).
- **Grupo ativo** (`groups/active`), **avisos do grupo** (`groups/{id}/announcements`), **notificações do grupo**.
- **Baú do grupo:** dar, retirar, congelar, extrato; produtos e lojas do grupo.
- **Terrenos do grupo:** pôr um terreno no grupo, devolver a um membro.
- **Perfil do membro no grupo** (`users/me/groups/{id}/profile`).

### 8. Carteira (Godot: `WalletWindow.cs`)
- **Comprar VerseCoin:** pacotes e pedidos (`economy/packages`, `coin-orders`).
- **Resumo da carteira** (`wallet/summary`) e **detalhe de uma transação** (`wallet/transactions/{id}`).
- **Taxas da plataforma** mostradas antes de comprar (`EconomyRates`).
- Saldo ao vivo na barra superior.

### 9. Perfil (Godot: `ProfileWindow.*`, 6 arquivos)
- Abas **Feed** (publicações com imagem, para todos ou só amigos), **Conquistas**, **Estatísticas**, **Favoritos**, **Grupos e Terrenos em destaque**.
- **Foto do perfil** e **imagem das publicações** (`profile/photo`, `posts/{id}/image`), **ordenar galeria**.
- Barra de ações no perfil de outra pessoa: mensagem, teletransportar, amizade, **enviar presente**, bloquear.
- **Perfil público de terrenos** (`users/me/parcels/{id}/profile`).

### 10. Pessoas e social (Godot: `PeoplePanel`, `PersonActions`)
- **Convites de teleporte entre amigos** (`social/teleports`, aceitar e recusar).
- **Relações** (`social/relations`).
- Lista de **pessoas perto** com distância e **recentes** guardadas por conta.
- Amigos no mapa (`map/people`) e chegada registrada (`map/arrival`).

### 11. Editor de avatar (Godot: `CharacterCreator.*`, `AppearanceWindow`, `AvatarRig.*`)
- **Corpo próprio:** enviar GLB, ver triângulos e ossos, escolher o ativo (aba "Modelos").
- **Roupas próprias:** enviar, ver camada e encaixe, vestir (aba "Itens").
- **Animações próprias:** enviar arquivos e trocar a animação de cada estado (`Animation Override`).
- **Visuais salvos** com código de compartilhamento ("Looks").
- **Sliders do corpo e do rosto** completos (Avatar 2.0) e os mesmos sliders num corpo próprio.
- **Editor dentro do mundo**, com a câmera de frente para o avatar como num espelho.
- Guarda-roupa que vem com o jogo para o corpo do homem (os arquivos não estão no repositório).

### 12. Anexos (Godot: `Attachments/`)
- **Editor com gizmo 3D** (setas, anéis, caixas) e câmera perto do ponto do corpo.
- **Imagens e construções vestidas** no corpo (placa com imagem, conjunto de formas).
- Janela de pontos agrupada por região e ações pelo menu do avatar.

### 13. Construção (Godot: `Build/`, 37 arquivos)
O Unity tem o editor com 11 abas e as operações principais. Faltam ou estão mais simples:
- **Luzes e reflexos dos objetos:** a aba Recursos do Godot dá luz (cor, intensidade, alcance, sombra) e reflexo; no Unity os objetos **não emitem luz**.
- **Texto flutuante** sobre objetos, posto por scripts (operação `text`): o Unity ignora.
- **Seletor de textura** com as texturas do inventário e os padrões do sistema.
- **Árvore de conteúdo** do objeto em pastas (Scripts, Objetos, Sons, Animações, Imagens, Notecards…).
- **Modelos de script** (`scripting/templates`).
- **Diretório de nomes** (criador, dono, dono anterior) e permissões finas por item do conteúdo.

### 14. Editor de scripts (Godot: `ScriptEditorWindow.*`, `ScriptApi.*`)
- **Completar código** a partir da documentação.
- **Depurar:** pontos de parada na margem, pausar e ver o estado.
- **Console e estado** do script em execução, parar e pausar.

### 15. Câmera, jogador e interface
- **Câmera livre** estilo Second Life: ALT, CTRL+ALT e CTRL+ALT+SHIFT com o botão esquerdo, foco no que foi clicado.
- **Roda do mouse** entra e sai da primeira pessoa; balanço e inclinação da cabeça; olhar para cima e para baixo enviado aos outros.
- **Lembrar para onde o avatar olhava** ao voltar.
- **Modos de interface** compacto e imersivo, barra inferior configurável (ordem, visibilidade, só ícones), **minimizar o chat**.
- **Voltar e avançar** entre ilhas visitadas; "Conectado · 47 avatares" na barra.
- **Menu de clique direito** em avatares e objetos (`ContextInteraction`, 497 linhas).
- **Nomes com indicador de quem está falando.**
- **Foto do mundo** pelo atalho, com ou sem interface, salva na pasta de fotos.

### 16. Entrada e carregamento
- **Pré-carregar os arquivos da ilha** antes de entrar, com cache em disco conferido por hash (`assets/manifest`, `AssetLoadingService`).
- **Lugar inicial** dado pelo servidor (última ilha ou a ilha de boas-vindas).
- Tela de entrada completa: fundo panorâmico, luzes, música, notícias, **estado dos servidores** (`/health`), "esqueci a senha".

### 17. Descoberta e eventos
- **Calendário de evento** (`discovery/events/{id}/calendar`), **política de eventos da ilha** (`islands/{id}/event-policy`), **informações da ilha** (`islands/{id}/info`), **detalhe de item** (`discovery/items/{id}`).

## Ordem sugerida

1. **Notificações ao vivo** (websocket, sino, central): várias outras faltas dependem disso (convites, chat de grupo, saldo).
2. **Gestão da ilha** completa e **gestão de terrenos**: o dono de ilha não consegue administrar pelo Unity hoje.
3. **Inventário** com pastas, lixeira, mover e copiar.
4. **Busca global** com o serviço próprio.
5. **Configurações** (câmera, gráficos, tela, privacidade, notificações).
6. **Construção:** luzes dos objetos, texto flutuante, seletor de textura.
7. **Grupos** completos, **carteira** (comprar moedas), **perfil** completo.
8. **Editor de avatar** (corpo, roupas e animações próprias, visuais salvos).
9. **Câmera livre**, menu de clique direito, modos de interface.
10. Tela de entrada, pré-carregamento, editor de scripts com depuração.

## Apêndice: as 95 rotas que o Godot usa e o Unity não

- `/api/v1/discovery/events/{}/calendar`
- `/api/v1/discovery/islands/{}/event-policy`
- `/api/v1/discovery/islands/{}/info`
- `/api/v1/discovery/items/{}`
- `/api/v1/economy/coin-orders`
- `/api/v1/economy/coin-orders/{}`
- `/api/v1/economy/packages`
- `/api/v1/economy/wallet/summary`
- `/api/v1/economy/wallet/transactions/{}`
- `/api/v1/groups/active`
- `/api/v1/groups/notifications`
- `/api/v1/groups/{}/announcements`
- `/api/v1/inventory/folders`
- `/api/v1/inventory/folders/{}`
- `/api/v1/inventory/folders/{}/purge`
- `/api/v1/inventory/folders/{}/restore`
- `/api/v1/inventory/integrity`
- `/api/v1/inventory/integrity/repair`
- `/api/v1/inventory/items/{}/ack-update`
- `/api/v1/inventory/items/{}/copy`
- `/api/v1/inventory/items/{}/equip`
- `/api/v1/inventory/items/{}/history-visibility`
- `/api/v1/inventory/items/{}/purge`
- `/api/v1/inventory/items/{}/used`
- `/api/v1/inventory/move`
- `/api/v1/inventory/packs/unpack`
- `/api/v1/inventory/storage`
- `/api/v1/inventory/trash`
- `/api/v1/inventory/trash/empty`
- `/api/v1/map/arrival`
- `/api/v1/map/people`
- `/api/v1/notifications/center`
- `/api/v1/notifications/center/read`
- `/api/v1/notifications/ws`
- `/api/v1/properties/notices`
- `/api/v1/properties/notices/read`
- `/api/v1/scripting/templates`
- `/api/v1/search`
- `/api/v1/search/events`
- `/api/v1/search/favorites`
- `/api/v1/search/favorites/{}/{}`
- `/api/v1/search/interests`
- `/api/v1/search/popular`
- `/api/v1/search/recent`
- `/api/v1/search/recommended`
- `/api/v1/search/report/{}/{}`
- `/api/v1/search/suggest`
- `/api/v1/social/groups/invites/{}/{(accept`
- `/api/v1/social/relations`
- `/api/v1/social/teleports`
- `/api/v1/social/teleports/{}/{(accept`
- `/api/v1/users/me/groups/{}/profile`
- `/api/v1/users/me/parcels/{}/profile`
- `/api/v1/users/me/profile/gallery/order`
- `/api/v1/users/me/profile/posts/{}`
- `/api/v1/users/profile`
- `/api/v1/users/{}/profile/photo`
- `/api/v1/users/{}/profile/posts/{}/image`
- `/api/v1/worlds/islands/{}/assets/manifest{}`
- `/api/v1/worlds/islands/{}/land/parcels/{}/group-access`
- `/api/v1/worlds/islands/{}/land/parcels/{}/group-access/{}`
- `/api/v1/worlds/islands/{}/management`
- `/api/v1/worlds/islands/{}/management/access`
- `/api/v1/worlds/islands/{}/management/access/allow`
- `/api/v1/worlds/islands/{}/management/access/allow/{}`
- `/api/v1/worlds/islands/{}/management/access/mode`
- `/api/v1/worlds/islands/{}/management/audit`
- `/api/v1/worlds/islands/{}/management/audit/{}`
- `/api/v1/worlds/islands/{}/management/bans`
- `/api/v1/worlds/islands/{}/management/bans/{}`
- `/api/v1/worlds/islands/{}/management/environment`
- `/api/v1/worlds/islands/{}/management/environment/presets`
- `/api/v1/worlds/islands/{}/management/environment/presets/{}`
- `/api/v1/worlds/islands/{}/management/general`
- `/api/v1/worlds/islands/{}/management/media`
- `/api/v1/worlds/islands/{}/management/objects`
- `/api/v1/worlds/islands/{}/management/objects/owners`
- `/api/v1/worlds/islands/{}/management/objects/return`
- `/api/v1/worlds/islands/{}/management/objects/return-owner`
- `/api/v1/worlds/islands/{}/management/objects/{}`
- `/api/v1/worlds/islands/{}/management/performance`
- `/api/v1/worlds/islands/{}/management/permissions`
- `/api/v1/worlds/islands/{}/management/permissions/{}`
- `/api/v1/worlds/islands/{}/management/physics`
- `/api/v1/worlds/islands/{}/management/physics/freeze`
- `/api/v1/worlds/islands/{}/management/physics/release`
- `/api/v1/worlds/islands/{}/management/restart`
- `/api/v1/worlds/islands/{}/management/restart/{}`
- `/api/v1/worlds/islands/{}/management/server`
- `/api/v1/worlds/islands/{}/management/server/kick`
- `/api/v1/worlds/islands/{}/management/server/notice`
- `/api/v1/worlds/islands/{}/management/terrain`
- `/api/v1/worlds/islands/{}/management/terrain/backups`
- `/api/v1/worlds/islands/{}/management/terrain/backups/{}`
- `/api/v1/worlds/islands/{}/management/terrain/backups/{}/restore`

## Feito depois do levantamento

### Notificações ao vivo (25/11/2026)
- `UI/LiveChannel.cs`: o websocket `/api/v1/notifications/ws`, aberto ao entrar com o token do momento, reaberto sozinho com pausa crescente (2 s até 1 min) e fechado ao sair.
- `UI/LiveEvents.cs`: cada evento vira aviso na tela pelo mesmo `NotificationFormatter` do Godot (respeitando as escolhas Grupos, Convites e Sistema, em Configurações › Interface), recontagem do sino, convite de teleporte com janela de aceitar ou recusar, resposta a um convite nosso, e saldo novo (`wallet_changed`).
- `UI/NotificationCenterWindow.cs`: botão "Avisos (n)" na barra e a central com as abas Todas, Grupos, Convites e Sistema (contador em cada uma), busca, "só não lidas", páginas de 20, marcar uma ou todas como lidas e aceitar ou recusar convite de grupo.
- Teleporte entre amigos: "Trazer para cá" e "Pedir para ir até" na janela de uma pessoa (`/api/v1/social/teleports`); aceitar leva a pessoa até o outro, dentro da ilha pela mensagem `RequestTeleport` do protocolo (já existia no servidor e agora é usada pelo cliente) e para outra ilha pelo teleporte.
- Janelas que se atualizam sozinhas: o chat do grupo (`group_message`) e a conversa privada aberta (`message`), além dos pedidos e das amizades na janela Social.
- Verificado: EditMode 125/125; auto-teste 214 checks (com um servidor websocket de teste próprio, porque o `HttpListener` da Unity não aceita websocket: abrir, avisos, sino, convite de teleporte e resposta da ilha, saldo, queda e volta, central, chat de grupo); API real 17/17 (mensagem empurrada pelo websocket de verdade, token falso recusado). Foto com GPU: `img/ui-notifications.png`.
- Observado: numa rodada com GPU o check antigo de "sentar no objeto" (linha 234 do auto-teste, parte do editor) falhou uma vez e passou na seguinte; é anterior a esta mudança.

### Gestão da ilha completa (25/11/2026)
- `UI/IslandManagementWindow.cs`: a janela "Gerenciamento da ilha" (botão Mais › Gestão da ilha) com menu lateral e o resumo no topo (estado, pessoas, dono, objetos e prims usados, "só leitura" para quem não pode mudar). Seções: Geral (nome, descrição, capa, categoria, classificação, idioma, buscador, mapa, lotação, ponto de chegada), Acesso (pública, só convidados ou fechada; liberar pessoa pelo nome com prazo em dias; tirar), Banimentos (contadores, filtros ativos, vencidos, retirados; banir com motivo e prazo; retirar), Permissões (as definições da API, dar pelo nome, tirar tudo), Administração (quem está na ilha, aviso para todos ou para uma pessoa, expulsar), Objetos e prims (uso, ordem, por dono, devolver marcados ou tudo de um dono), Física (congelar e soltar um ou todos), Desempenho (agora, mín, média e máx de 1, 6 ou 24 horas, scripts que mais pesam), Ambiente (hora, clima, neblina, luz, cor do céu, predefinições prontas e próprias), Mídia (música da ilha), Terreno (versão, alturas, backups com restaurar agora ou em 5 min, apagar), Reinicialização (agora, em 1, 5 ou 10 min, num horário, cancelar, histórico), Histórico e auditoria (categorias, períodos, detalhes com antes e depois), Regras dos terrenos, Terrenos (o painel que já existia) e Propriedade (anunciar ou tirar a venda e o aluguel da ilha inteira).
- Toda mudança leva `expectedUpdatedAt`; se outra pessoa salvou antes (409 `stale`) a seção é lida de novo e o aviso fica na tela. Respostas atrasadas de uma seção que já foi fechada são descartadas.
- A janela antiga (`IslandManageWindow`) continua no código porque os testes antigos a usam; o botão do jogo abre a nova.
- Verificado: EditMode 132/132 (7 testes novos); auto-teste 231 checks (17 novos, uma ou mais por seção, com recusas de dados errados); API real 18/18 (passo novo: dados gerais salvos e relidos, banir B pelo nome e retirar, dar permissão a B, hora do ambiente, música, histórico com as mudanças, e as outras sete seções abrem sem erro). Foto com GPU: `img/ui-island-management.png`.
- Ainda não feito em relação ao Godot: gráficos de desempenho (hoje só números), mudar o prazo de uma liberação ou banimento já feito (`PUT access/allow/{id}`, `PUT bans/{id}`), mudar permissões de quem já tem (`PUT permissions/{userId}`), detalhe de um objeto (`objects/{id}`) e prévia do terreno. A parte 2 deste item (terrenos) foi feita logo depois, abaixo.

### Gestão de terrenos e "Sobre este Terreno" (25/11/2026)
- `UI/LandManagementWindow.cs`: "Gerenciamento de Terrenos" (Mais › Gestão de terrenos, ou o botão na seção Terrenos da gestão da ilha), para o dono e a equipe da ilha, lendo `/land/overview`. Seções: Visão Geral (área, contagem por situação, aluguel por mês, contratos vencendo, objetos), Mapa de Parcelas (cada terreno pintado pela situação, com legenda), Demarcação (marcar os quadrados no mapa e criar o terreno, `/land/claim`), Lista de Terrenos (filtros por situação e busca), Vendas e Aluguéis (os anunciados, tirar do anúncio, anunciar um terreno, encerrar um aluguel), Contratos, Prims e Recursos (uso por terreno), Permissões (acesso de grupo, `/group-access`) e Histórico (mudanças e dinheiro).
- `UI/ParcelInfoWindow.cs`: "Sobre este Terreno" (Mais › Sobre este terreno abre o terreno onde a pessoa está; também pelo botão Sobre nas listas). Abas: Geral (nome e descrição com a revisão, finalidade, imagem, ponto de chegada com "usar onde estou" e modo de teleporte), Propriedade (comprar, alugar, renovar, encerrar, anunciar ou tirar venda e aluguel, transferir para alguém pelo nome, aceitar, recusar ou cancelar a transferência), Acesso (quem entra, a lista de pessoas por nome com tipo e prazo, os grupos), Opções (quem constrói, scripts, voz, mídia, devolver objetos soltos), Objetos (devolver os marcados ou os de quem não tem permissão), Mídia, Histórico (donos, negócios e dinheiro) e Auditoria. Quem não pode mudar vê tudo só para leitura.
- `UI/SectionWindow.cs`: a moldura comum (menu lateral, resumo, páginas, recusas, respostas atrasadas descartadas, aviso de conflito) agora é usada pelas três janelas de gestão; as recusas dos terrenos viram frases (`LandManagementWindow.Words`, os mesmos códigos do Godot).
- Verificado: EditMode 137/137 (5 testes novos); auto-teste 248 checks (17 novos: todas as seções e abas, com recusas de dados errados); API real 19/19 (passo novo: visão geral, demarcar um terreno pela janela, as outras seções, renomear, quem constrói, pôr B na lista pelo nome, as outras abas sem erro e B vendo o terreno só para leitura). Fotos com GPU: `img/ui-land-management.png`, `img/ui-parcel-info.png`. Numa rodada o check antigo de "sentar no objeto" falhou de novo e passou na seguinte (anterior a esta mudança).
- Ainda não feito em relação ao Godot: escolher o terreno clicando no mapa de parcelas (hoje a lista abaixo do mapa), abrir "Sobre este Terreno" com o clique direito no chão, escolher a imagem do terreno no inventário (hoje pelo identificador) e o perfil público de terreno (`/users/me/parcels/{}/profile`).

### Inventário completo (25/11/2026)
- `UI/InventoryManagerWindow.cs`: o botão Inventário da barra abre a janela nova. Vistas: Tudo, Pastas, Sem pasta, Favoritos, Recentes, Atualizados, Devolvidos, Lixeira, Espaço e verificação, Abrir um pacote. O resumo no topo conta itens, favoritos, sem pasta, recentes e o que está na lixeira.
- Lista: busca (nome, descrição, tipo), ordem (padrão, nome, mais novos, usados por último, tamanho, tipo), páginas de 30; marcar itens (e pastas) e movê-los juntos para uma pasta ou mandá-los para a lixeira.
- Detalhes de um item: tipo, versão, tamanho, datas, direitos em palavras (os seus e os de quem receber), renomear, mudar de pasta, favorito, fazer uma cópia, vestir num encaixe (e marcar como usado), "entendi a versão nova", direitos e histórico (a janela que já existia) e mandar para a lixeira.
- Pastas: navegação com o caminho no topo, criar, renomear, levar para o início, apagar (vai para a lixeira) e os itens de cada pasta.
- Lixeira: itens e pastas com restaurar e apagar de vez, esvaziar tudo e o prazo até sumirem.
- Espaço e verificação: uso e cota com barra, lixeira, versões antigas e uso por tipo; verificação de integridade com os problemas encontrados e "Consertar".
- Pacote: escolher um .zip no seletor de arquivos e transformá-lo numa pasta de itens (ou ver quais arquivos foram recusados).
- A janela antiga (`InventoryWindow`) continua no código porque os testes antigos a usam.
- Verificado: EditMode 140/140 (3 testes novos); auto-teste 255 checks (7 novos); API real 20/20 (passo novo: criar pasta, mover um item para ela, renomear, lixeira e restaurar, espaço e verificação, e um .zip de verdade com duas imagens virando uma pasta com 2 itens). Foto com GPU: `img/ui-inventory.png`.
- Ainda não feito em relação ao Godot: arrastar e soltar itens entre pastas, vestir direto pelo menu de clique direito e visibilidade do histórico de um item (`history-visibility`).

### Busca global (25/11/2026)
- `UI/GlobalSearchWindow.cs`: o botão Buscar da barra abre a busca nova, no serviço de busca da plataforma (`/api/v1/search`). Abas: Para você, Tudo, Pessoas, Lugares, Eventos, Grupos, Itens, Lojas e Favoritos (as mesmas sete do Godot mais Para você e Favoritos).
- Busca: sugestões enquanto digita (`search/suggest`), ordem (relevância, popularidade, recentes, nome; eventos por início; itens por preço), filtros de itens (preço mínimo e máximo, criador, direitos) e de eventos (só gratuitos), páginas de 20, contagem por tipo no topo.
- Detalhes de um resultado: ir até lá (ou visitar a loja), ver perfil e ações com a pessoa, ver o grupo, participar do evento, abrir o mercado, favoritar, copiar o link do jogo (`lifeverse://…`, o mesmo formato do Godot) e denunciar com motivo; abrir e viajar avisam o serviço (`search/events`) para os lugares populares.
- Para você: abrir um link do jogo colado (só o formato exato, então um link nunca abre nada de fora), as últimas buscas (guardadas só neste computador, com a opção de não guardar e de apagar), os interesses (`search/interests`), recomendados, lugares populares e lugares onde a pessoa esteve.
- As recusas viram frases (denunciar o que é seu, favoritos cheios, denúncias demais).
- A janela antiga (`SearchWindow`) continua no código porque os testes antigos a usam.
- Verificado: EditMode 145/145 (5 testes novos); auto-teste 261 checks (6 novos); API real 21/21 (passo novo: interesses salvos, a ilha de Alice achada no índice de busca de verdade, favoritar, a API recusando A denunciar a própria ilha e aceitando a denúncia de B, ir até lá e as outras abas sem erro). Foto com GPU: `img/ui-search.png`.
- Ainda não feito em relação ao Godot: miniaturas dos resultados, navegar pelos resultados com as setas do teclado e as ações de compra de produto e de loja dentro dos detalhes (hoje abre o mercado).

### Configurações com o catálogo do Godot (25/11/2026)
- O catálogo do Godot (`client/Scripts/Settings/SettingsCatalog*.cs`) foi trazido inteiro para o Unity por um gerador (`unity/tools/settings-catalog/`): os mesmos 196 ids, rótulos, textos de ajuda, padrões, limites, passos e escolhas, nas mesmas 18 categorias. O levantamento falava em 220; contando no código, são 196 (mais as teclas, que ficam na janela clássica).
- `UI/SettingsCatalogWindow.cs`: o botão Configurações abre a janela nova: as 18 categorias no menu, busca em todas (sem diferença de acento ou maiúscula), cada opção com a sua ajuda, "Restaurar o padrão desta categoria", e Perfis e restaurar (salvar todas as opções com um nome, até 10 perfis; usar, apagar; restaurar tudo). Mudar a tela (modo, resolução, monitor) pede para manter e volta sozinha em 15 segundos. Teclas, teste do microfone e sincronização com a conta continuam na janela clássica, aberta por Perfis.
- `UI/SettingsStore.cs`: as opções que o Unity já tinha continuam nas mesmas propriedades (a janela clássica, a sincronização e o mundo veem o mesmo valor); as outras ficam em `UserSettings.Values` por id. Mudar à mão uma opção que a predefinição controla a deixa "Personalizada", como no Godot.
- `UI/SettingsEffects.cs` e `FollowCamera`: o que as opções fazem no Unity: câmera (campo de visão em terceira e primeira pessoa, passo do zoom, roda entra e sai da primeira pessoa, altura, ombro, suavização, colisão, balanço e inclinação com a força, estabilização, distância inicial, modo inicial); gráficos além da predefinição (sombras e distância, filtragem anisotrópica, texturas, partículas, distância de renderização); tela (modo, resolução, monitor); avisos (quantos, por quanto tempo, não perturbe); grade da construção; passos; ordem do inventário e subpastas; números de desempenho; som e quadros com o jogo em segundo plano; informações de Geral, Rede e Avançado; e as ações (sair da ilha, fechar o jogo, abrir as pastas, exportar diagnóstico, restaurar janelas, abrir o editor de personagem e as pessoas).
- Números: das 196 opções do Godot, 82 agem no Unity, 33 também não funcionam no Godot (aparecem desligadas com o motivo dele) e 81 ainda não têm efeito no Unity (aparecem desligadas com "Ainda não tem efeito no cliente Unity."). Mais 8 opções que só o Unity tinha (volumes de música, efeitos, ambiente e mídia, música dos terrenos, sons da interface, texto maior, guardar na conta).
- Mudança de comportamento: a construção agora segue o padrão do Godot (sem encaixe na grade, passo de 0,5 m) até a pessoa ligar "Encaixar na grade"; o campo de visão padrão passou de 62° para 75°, o do Godot.
- Verificado: EditMode 152/152 (7 testes novos, incluindo que todo id tratado pelo Unity existe no catálogo e que todo padrão está entre as escolhas); auto-teste 270 checks (9 novos: abrir, opção desligada, predefinição Personalizada, distância de renderização, câmera, restaurar categoria, não perturbe, busca, tela com voltar, perfis, informações e diagnóstico). Não há passo novo na API real porque as configurações são locais (a sincronização com a conta não mudou). Foto com GPU: `img/ui-settings.png`.
- Ainda não feito: as 81 opções sem efeito, sobretudo os efeitos do HDRP (antialiasing, oclusão ambiente, reflexos, volumétricos, brilho, neblina), a escala de renderização, os detalhes de avatares, o minimapa e o mapa, o chat (hora, tempo visível), privacidade e scripts.

### Construção: luzes, reflexos, texto flutuante e texturas do sistema (25/11/2026)
- `World/WorldObjects.Resources.cs`: os objetos agora emitem luz como no Godot, lendo o JSON de material que o servidor guarda: "light" (cor, força, alcance, queda, sombra; e "face" com "cone" para a luz sair de uma face só), projetor ("projector" com abertura, foco e luz fora da imagem, e "projectorBuiltin" ou "projectorTexture" para a imagem projetada) e "reflection" (intensidade e alcance, um reflexo do que está em volta). Com o orçamento do Godot: só as primeiras luzes do mundo acendem e só as primeiras delas fazem sombra (Configurações › Gráficos: "Limite de luzes" e "Luzes com sombra", que agora valem no Unity), e no máximo 4 reflexos.
- `World/BuiltInTextures.cs`: as 7 texturas do sistema do Godot (compensado, xadrez, tijolos, tábuas, listras, ruído, ladrilhos), feitas em código com as mesmas fórmulas. Os objetos com "builtin" agora mostram o padrão (antes ficavam lisos), com a repetição e o deslocamento da imagem.
- `World/ObjectTexts.cs`: o texto flutuante que os scripts põem sobre objetos (operação "text" da ilha), virado para a câmera, na cor pedida, até 120 letras, até 64 de uma vez, visto até 40 m; texto vazio tira.
- `UI/BuildResourcesTab.cs`: a aba nova Recursos no editor (luz, face da luz, cone, sombra, projetor com imagem, reflexo), o seletor de textura com as texturas do sistema (com prévia) e as do inventário (na Aparência e no projetor), o Conteúdo em pastas como no Godot (Scripts, Objetos, Sons, Animações, Imagens, Texturas, Materiais, Notecards, Outros), e na aba Geral o criador e o dono pelo nome ("Você" para a própria pessoa, os outros pelo perfil público).
- `UI/LuaEditorWindow.cs`: botão Modelos com os modelos de script da plataforma (`/api/v1/scripting/templates`) ou o modelo de porta embutido.
- Verificado: EditMode 157/157 (5 testes novos, incluindo os pixels das texturas iguais aos do Godot); auto-teste 278 checks (8 novos, pelo editor com o servidor da ilha de verdade: luz, alcance, força, projetor, reflexo, limite de luzes, textura do sistema, seletor, texto flutuante, pastas, nomes e modelos); API real 22/22 (passo novo: modelos de script e o nome pelo perfil público). Foto com GPU: `img/ui-object-resources.png` (o texto aparece pequeno ao longe, porque a câmera do teste segue o personagem).
- Ainda não feito em relação ao Godot: aplicar a textura só na face escolhida pelo seletor (hoje no objeto inteiro), permissões finas por item do conteúdo e o "dono anterior" (o servidor não manda esse campo para o mundo).

### Grupos, carteira e perfil (25/11/2026)
- `UI/GroupsHubWindow.cs` (botão Mais › Grupos): Meus grupos (com o grupo ativo, cuja etiqueta e título aparecem no topo; usar ou deixar de usar), Descobrir (busca com categoria, idioma, forma de entrada e só sem taxa; entrar, pedir para entrar, cancelar o pedido; taxa de entrada confirmada pelo valor que a API pede), Convites, Avisos dos grupos (marcar tudo como lido) e Criar grupo em 4 passos (Informações, Entrada, Configurações, Confirmação), com as regras do Godot (`LifeVerse.Shared.Groups.GroupRules`).
- `UI/GroupProfileWindow.cs`: o perfil do grupo com as 10 abas do Godot: Geral, Membros (convidar pelo nome, aprovar ou recusar pedidos, silenciar, expulsar, banir, mudar o cargo), Cargos (criar, apagar, título), Permissões (as 19 permissões por cargo, como no Godot), Avisos (com prazo e fixar), Chat, Terrenos (do grupo, dar um terreno seu, devolver), Economia (baú: dar, retirar com motivo, congelar, extrato, quem mais deu), Propostas (abrir votação com opções, prazo e critério; votar; encerrar; cancelar) e Configurações (silenciar os avisos, perfil do grupo, banidos, histórico, passar a liderança e desfazer, com o nome do grupo para confirmar). As recusas viram frases.
- `UI/WalletHubWindow.cs` (botão Carteira): Resumo (disponível, reservado, total, recebido e gasto em 7, 30 ou 90 dias), Extrato (filtro por tipo e sentido; recibo de cada movimento com código e taxa), Comprar VerseCoin (pacotes com bônus e preço em reais, formas de pagar, o pedido e como ele está; o jogo nunca pede dados de cartão ou banco), Taxas da plataforma e Enviar (o formulário que já existia). O saldo aparece na barra de cima e muda sozinho pelo canal ao vivo (`wallet_changed`).
- `UI/ProfileHubWindow.cs` (Mais › Perfil, e o perfil de outra pessoa pela busca e pelas ações): Sobre (foto, capa, bio, fora do jogo, interesses, e editar com a privacidade), Feed (publicar texto com imagem da galeria, para todos ou só amigos; apagar), Galeria (pôr imagem do inventário, usar como foto, subir e descer, capa), Conquistas, Estatísticas, Favoritos, Grupos e terrenos (mostrar ou esconder no perfil) e Ações no perfil de outra pessoa (mensagem, amizade, teleporte, bloquear e enviar um presente do inventário).
- As janelas antigas (`GroupsWindow`, `GroupDetailWindow`, `WalletWindow`, `ProfileWindow`) continuam no código porque os testes antigos as usam (o envio de moedas ainda usa a `WalletWindow`).
- Verificado: EditMode 162/162 (5 testes novos); auto-teste 283 checks (5 novos); API real 24/24 (2 passos novos: A cria o grupo nos 4 passos, convida B pelo nome, B aceita, cargo novo com a permissão de convidar, aviso que B vê, chat, o baú recusando sem saldo em palavras, votação aberta e o voto de B, grupo ativo; carteira com as abas e o pedido de moedas respondendo em palavras; bio salva, publicação no feed que B vê, e B sem poder editar o perfil de A). Fotos com GPU: `img/ui-group.png`, `img/ui-wallet.png`, `img/ui-profile.png`.
- Ainda não feito em relação ao Godot: produtos e lojas do grupo, eventos do grupo (a descoberta de eventos já existe), o perfil do membro no grupo e as imagens no extrato e nas conquistas (ícones).

### Estúdio do avatar: visuais salvos, corpo, roupas e animações próprias (26/11/2026)
- `UI/AvatarStudioWindow.cs` (Mais › Avatar: visuais, corpo, roupas e animações): Visuais salvos (salvar o avatar de agora, vestir, copiar, compartilhar com um código de 8 letras copiado para a área de transferência, parar de compartilhar, trazer pelo código, apagar), Corpo próprio (enviar GLB, triângulos, ossos, altura, usar, voltar ao corpo do jogo, relatório das checagens), Roupas próprias (enviar, encaixe, tipo, camada, o que cobre, vestir e tirar sem mexer no resto do avatar), Animações próprias (enviar, os clipes de cada arquivo, escolher uma animação do jogo ou própria para cada um dos 18 estados) e Editor e espelho (o editor do personagem que já existia e a câmera de frente para o avatar). Os envios mostram o motivo das checagens do servidor quando ele recusa.
- `Avatar/AvatarWardrobe.cs`: no mundo, num corpo com esqueleto (o homem do jogo ou um corpo próprio), as roupas próprias aparecem para todos: uma peça rígida (chapéu, óculos, mochila) presa ao osso com o deslocamento, o giro e o tamanho dela; uma roupa com esqueleto presa aos ossos do corpo pelos nomes padrão, então dobra com o corpo. As animações próprias tomam o lugar das do jogo no estado escolhido (`SkeletalBody.Overrides`). Os arquivos vêm de `/api/v1/avatars/wearables|animations/{id}/file`, um download por arquivo.
- `FollowCamera.Mirror`: a câmera de terceira pessoa fica na frente do avatar enquanto o espelho está ligado; fechar o estúdio desliga.
- Layout: as linhas das janelas de gestão agora quebram e os botões não se espremem (vale para todas as janelas feitas sobre `SectionWindow`).
- Verificado: EditMode 165/165 (3 testes novos); auto-teste 287 checks (4 novos: chapéu preso ao osso da cabeça, camiseta usando os ossos do corpo, o clipe próprio "Aceno" tocando no estado Parado, os visuais e o espelho); API real 25/25 (passo novo com arquivos de verdade: corpo próprio aceito e em uso, camiseta e chapéu aceitos, chapéu vestido, animação própria aceita e escolhida para Parado, visual salvo, compartilhado e trazido por B pelo código, e a volta ao corpo do jogo). Durante o teste a API recusou com razão uma camiseta que era um corpo inteiro, uma animação que mexia só 1 osso e trazer um visual com o chapéu próprio de outra pessoa; as três recusas agora aparecem em palavras. Foto com GPU: `img/ui-avatar-studio.png`.
- Ainda não feito em relação ao Godot: as variações de animação do jogo ("builtin:…", o Unity só tem a animação principal de cada estado), refazer o encaixe de uma roupa com esqueleto na pose de descanso do corpo (hoje usa a pose em que a roupa foi feita), sliders do corpo e do rosto num corpo próprio e o guarda-roupa do homem que vem com o jogo (os arquivos não estão no repositório).

### Câmera livre, menu do botão direito e modos da interface (26/11/2026)
- Câmera livre como no Godot (`Avatar/FollowCamera.cs`, regras de `shared/Camera/CameraControl.cs`): ALT + botão esquerdo põe o foco no que foi clicado (pessoa, objeto ou chão) e gira em volta e aproxima; CTRL+ALT gira nos dois sentidos; CTRL+ALT+SHIFT desliza o foco. A roda aproxima do foco (CTRL para ir devagar). Andar ou Esc devolve a câmera ao avatar. Enquanto o gesto da câmera acontece, o clique não abre o menu do objeto.
- Menu do botão direito (`GameApp.ContextItems`, no mesmo anel do menu radial): um clique rápido do botão direito (segurar e arrastar continua girando a câmera) abre ações para o que está embaixo do cursor. No próprio avatar: meu perfil, avatar, gestos, sentar no chão, primeira pessoa e inventário. Em outra pessoa: perfil e ações (mensagem, trazer, ir até, amizade, bloquear) e dar zoom. Num objeto: tocar, sentar, dar zoom, scripts, editar e mais. No chão: teleportar para cá, sobre este terreno, olhar para cá e construir. A pessoa é achada pelo raio da tela passando pelo corpo (`AvatarPick`), e a mais perto entre pessoa e objeto ganha.
- Modos da interface (`interface.mode`): completo, compacto (só as barras, botões menores, sem mapa e sem chat) e imersivo (só o mundo; F10 mostra e esconde a interface). `interface.compact_toolbar` deixa a barra pequena. A barra inferior tem ordem e botões escondidos configuráveis em Mais › Barra de ferramentas (`ToolbarLayout`; pelo menos um botão fica), e "Restaurar a barra" volta ao padrão (também em Configurações › Interface).
- O chat minimiza (botão no canto; as mensagens ficam guardadas). A barra de cima ganhou voltar e avançar entre as ilhas visitadas na sessão (`IslandHistory`, até 20; o histórico só anda quando a viagem chega). A câmera lembra para onde olhava em cada ilha.
- F12 tira uma foto do mundo em Imagens/LifeVerse ("LifeVerse 2026-11-26 07-30-12.png"), sem a interface a não ser que `camera.photo_include_ui` esteja ligado, em PNG ou JPG (`camera.photo_format`).
- Corrigido de passagem: com a câmera presa num ponto, a colisão com o chão usava a distância normal em vez da distância do zoom.
- Verificado: EditMode 170/170 (5 testes novos: ordem da barra, histórico de ilhas, nome e formato da foto, qual avatar o raio acerta, a câmera livre focando, girando e deslizando); auto-teste 298 checks (11 novos: o botão direito no próprio avatar e no chão ou num objeto, o menu abrindo no anel, a câmera livre girando em volta do foco, os modos compacto, imersivo com F10 e completo, a ordem e os botões escondidos da barra e restaurar, minimizar o chat, o histórico com a ilha entrada e a foto F12 na pasta). Nada novo na API, então o teste com a API real não muda. Fotos com GPU: `img/ui-context-menu.png`, `img/ui-compact.png`, `img/ui-toolbar.png`.
- Ainda não feito em relação ao Godot: a câmera solta do avatar (`AllowDetached`) e o foco por clique duplo, ícones de verdade na barra compacta (hoje só diminui o texto), arrastar os botões para reordenar (hoje são setas) e pagar alguém pelo menu do botão direito (está na carteira).

### Tela de entrada, pré-carregamento da ilha e depuração de scripts (26/11/2026)
- Tela de entrada (`UI/LoginView.cs`): o fundo panorâmico do Godot (cobrindo a janela sem esticar, com um véu escuro), a música da entrada (entra suave, some ao entrar no mundo, botão "Música: ligada/desligada" lembrado), o estado dos servidores lido de `/health` a cada 20 segundos ("Servidores Online / Indisponíveis", nunca inventado), a faixa de notícias (o aviso de versão nova; sem nada, "Sem novidades no momento.") e "Esqueceu sua senha?", que diz com clareza que a recuperação por e-mail ainda não existe no servidor (como no Godot).
- Lugar inicial dado pelo servidor: a primeira opção da lista de ilhas é "Onde parei (último lugar ou a ilha de boas-vindas)"; com ela (ou com o código em branco) o jogo pergunta `/api/v1/worlds/me/start` depois de entrar e vai para a ilha que o servidor escolheu. Essa escolha fica lembrada para a próxima vez.
- Pré-carregar os arquivos da ilha (`World/AssetPreloader.cs`, como o `AssetLoadingService` do Godot): na entrada o jogo lê `assets/manifest`, baixa antes de entrar os modelos e imagens perto do ponto de chegada (o `AssetPlanner` compartilhado), mostra "Baixando os recursos da ilha… 3,2 de 10 MB" na tela de carregamento e baixa o resto depois de entrar. Tudo fica em disco pelo hash (pasta `assets` dos dados do jogo, até 512 MB, os mais antigos saem primeiro): um arquivo que já está lá e confere não é baixado de novo, um estragado é trocado e um download com o hash errado é recusado e tentado mais uma vez. Sem a lista (servidor antigo) a entrada segue como antes.
- Editor de scripts (`UI/LuaEditorWindow.cs`, regras em `UI/LuaEditorRules.cs`):
  - completar código com Ctrl+Espaço ou o botão Completar, a partir da referência da linguagem da plataforma (funções depois de `objeto.`, eventos, palavras do Lua e as variáveis `local` do script);
  - a verificação completa do servidor (`/validate`: erros e avisos por linha na aba Problemas, com o cursor indo para o primeiro erro);
  - Reiniciar, Parar e Pausar/Continuar;
  - as abas Console (o que o script escreveu e os erros, ou as execuções antes de a ilha informar), Desempenho (eventos, CPU, memória, último erro) e Permissões (as do dono, que podem ser ligadas e desligadas, e as dadas por avatares, com Revogar), atualizadas a cada 5 segundos;
  - depuração: as linhas de parada (digitadas ou "Marcar a linha do cursor"), Depurar, e quando a ilha para o script na linha o editor mostra a linha, as variáveis locais e o que o script guarda, com Continuar, Passo, Por cima e Sair. Os comandos vão pela conexão com a ilha (`script_debug`) e só o dono pode depurar. O editor pede para salvar antes, porque a ilha depura o código salvo.
- Verificado: EditMode 178/178 (8 testes novos: lista de recursos, cache por hash com arquivo estragado e hash errado, limpeza do cache, ilha escolhida pelo servidor, completar código, linhas e pontos de parada, mensagens da depuração, estado e verificação do script); auto-teste 309 checks (11 novos: fundo e escolha do servidor na entrada, estado dos servidores, esqueci a senha, música, ilha onde a pessoa parou, recurso baixado antes da entrada e guardado pelo hash, a música sumindo ao entrar, console e estado, permissões, completar, aba Problemas, pausar, dar permissão, parar na linha 3 com as variáveis, fim da depuração); API real 26/26 (passo novo: `/health`, `/me/start`, `assets/manifest`, verificação do servidor, desempenho e console, pausar e continuar, dar uma permissão e vê-la no script, completar com a referência real). No host de teste `/health` responde "Unhealthy", porque a única checagem dele é o Postgres, que o host não usa; a tela mostra "Servidores Indisponíveis" nesse caso. Fotos com GPU: `img/ui-login.png`, `img/ui-script-debug.png`.
- Os cinco que tinham ficado faltando foram feitos depois, no mesmo dia (ver abaixo).

### O que faltava da entrada e do editor de scripts (26/11/2026)
- Show de luzes na entrada (`UI/LoginLightShow.cs`): seis fachos que varrem devagar (ciano, violeta, rosa e dourado) e 46 pontos de luz subindo, desenhados por cima do fundo e por baixo do formulário. Não respondem ao mouse e ficam parados com "Reduzir movimento".
- Lembrar a senha (`UI/SavedLogin.cs`): "Lembrar senha neste computador" é opcional. Usuário e senha ficam selados pela proteção de dados do Windows (DPAPI, só a mesma conta do Windows no mesmo computador consegue abrir), num arquivo próprio (`login.sealed`), nunca no arquivo de configurações nem em texto. Na próxima abertura o formulário vem preenchido. Desmarcar e entrar apaga o arquivo; um arquivo que não abre (outra conta, estragado) é apagado. Em outros sistemas a opção não aparece.
- Editor de scripts:
  - a margem com os números das linhas: clicar num número põe ou tira a linha de parada (bolinha vermelha), e a linha onde a depuração parou fica marcada em amarelo;
  - o código em cores (`UI/LuaHighlight.cs`): palavras do Lua, textos, comentários (também `--[[ ]]`), números, os nomes da plataforma (`object.`, `timer.`…) e as funções chamadas, desenhados por baixo do texto do campo com a mesma fonte, mantendo os espaços do começo das linhas. O código, os números e as cores rolam juntos.
- Pré-carregar a partir do ponto de chegada: numa viagem para um lugar da ilha (busca, mapa, marco, convite), a lista de recursos é pedida com `?x=…&z=…`, então os arquivos perto de onde a pessoa vai chegar vêm primeiro.
- Verificado: EditMode 181/181 (3 testes novos: as cores do Lua sem perder nem acrescentar texto, a senha selada pelo Windows e recusada quando o arquivo está estragado, as luzes que se movem e param com "Reduzir movimento"); auto-teste 314 checks (5 novos: as luzes se movem, a senha lembrada fica selada e nunca em texto, ela volta na próxima abertura, a margem com os números e as cores, clicar no número marca e desmarca, os recursos da ilha de destino pedidos a partir do ponto de chegada); API real 26/26 (a lista de recursos a partir de um ponto). Fotos com GPU: `img/ui-login.png`, `img/ui-script-debug.png`.

### Pessoas, descoberta, anexos e mais configurações (26/11/2026)
- Pessoas (`UI/PeopleHubWindow.cs`, no botão Pessoas): "Perto de você" (por distância, atualiza a cada 1,5 s, com Perfil e ações e Ir até, que leva para perto da pessoa na mesma ilha), "Vistos recentemente" (guardados por conta, os 20 mais novos, quem está aqui agora marcado) e "Amigos agora" (`/api/v1/map/people`: em que ilha e ponto cada amigo está, com Perfil e Ir até).
- Antes de viajar para um ponto (amigos e mapa-múndi) o servidor confere a chegada (`/api/v1/map/arrival`): terreno restrito, banido, sem teletransporte, ilha cheia ou fechada viram uma frase, e um terreno que tem ponto de chegada próprio leva a pessoa para lá.
- Busca: o detalhe de uma pessoa diz a relação (`/social/relations`: amigos, pedido enviado ou recebido, bloqueada); o de um lugar traz as informações da ilha (`/discovery/islands/{id}/info`: dono, categoria, classificação, idioma, visitantes agora, o acesso, com o botão de ir trocado por "Ilha cheia", "Ilha privada"…) e, para o dono, "Eventos de convidados: permitidos/bloqueados" (`event-policy`); o de um evento tem "Adicionar ao calendário" (`/calendar`); o de um item traz o detalhe do mercado (`/discovery/items/{id}`: preço, criador, vendedor, o que se poderá fazer, presente, tipo e tamanho, se já é seu) e compra ali mesmo.
- Anexos:
  - "Editar no corpo" (`UI/AttachmentEditor.cs`): a câmera chega perto do objeto, que ganha setas (mover), anéis (girar) e caixas (escalar) no espaço do ponto do corpo, com passos de posição e de rotação, manter proporções, desfazer e refazer, os números para digitar, Salvar (pede à plataforma) e Cancelar (tudo volta);
  - imagens e construções vestidas: uma imagem vira uma placa com a imagem e uma construção do inventário aparece com as formas dela, desenhadas com o mesmo construtor do mundo;
  - a lista de pontos do corpo agora é escolhida por região (cabeça, tronco, braços, pernas, especiais, tela).
- Configurações que passaram a agir (31):
  - imagem: neblina, luz volumétrica, brilho, tratamento de cor, oclusão ambiente e a qualidade dela, reflexos, faixas e qualidade das sombras, suavização de bordas (no HDRP: FXAA, SMAA e TAA no lugar do MSAA, dito na própria opção), escala de renderização e resolução dinâmica. O suporte a SSAO, SSR, volumétricos e resolução dinâmica foi ligado no pacote do HDRP;
  - chat: a hora nas mensagens e o tempo visível;
  - nomes sobre os outros e sobre o próprio avatar;
  - mini-mapa: coordenadas, quantos metros mostra (também pela roda do mouse sobre ele) e os avatares;
  - opacidade das janelas e do mini-mapa;
  - menu radial: tamanho, opacidade e brilho;
  - editor de scripts: tamanho da letra, espaços do Tab e sugestões ao digitar;
  - passos de giro e de escala da construção, limpar o cache e reconectar sozinho (até três vezes) quando a conexão com a ilha cai.
- Verificado: EditMode 188/188 (7 testes novos: recentes, chegada, palavras da busca, edição de posição por passos, tipos de arquivo vestido, pontos por região, efeitos e escala de renderização); auto-teste 323 checks (9 novos: pessoas perto, recentes e amigos, o servidor recusando a chegada, editor no corpo com cancelar e salvar, imagem vestida, efeitos de imagem, nomes, mini-mapa, chat sumindo e voltando, reconexão sozinha); API real 27/27 (passo novo: relação, amigos no mapa, chegada, informações da ilha, eventos de convidados, calendário de B, detalhe de item, aba Amigos agora). Foto com GPU: `img/ui-people.png`.
- Ainda faltam 43 opções sem efeito no Unity, por exemplo:
  - detalhes e distâncias dos avatares;
  - as camadas do mapa-múndi e o mini-mapa girando;
  - voz espacial e distância de audição;
  - correr com toque duplo e segurar para voar;
  - ondas da água e qualidade automática.

  Outras 33 trazem o mesmo motivo do Godot para não agir (por exemplo, privacidade que depende de uma preferência de conta que o servidor ainda não guarda). Também faltam os títulos de grupo sobre o nome (o mundo não recebe esse dado).
