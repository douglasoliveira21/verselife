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
| Configurações | 18 categorias, 196 opções no catálogo, 272 KB | feito em 25 e 26/11: o mesmo catálogo (mais 4 da câmera livre do Godot); 173 opções agem no Unity, 33 têm o mesmo motivo do Godot para não agir e 2 explicam por que o Unity não consegue (saída de áudio e barra lateral); nenhuma fica sem resposta | Equivalente | Baixa |
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
| Câmera e jogador | câmera livre (ALT, CTRL), roda para primeira pessoa, balanço da cabeça | feitos em 26/11: câmera livre, foco e roda (ver abaixo); câmera desacoplada e foco por clique duplo feitos depois | Equivalente | Baixa |
| Interface (casca) | modos compacto e imersivo, barra configurável, histórico de ilhas, menu de clique direito | feitos em 26/11: modos, barra configurável, chat minimizado, histórico de ilhas, menu do botão direito e foto F12 (ver abaixo) | Equivalente | — |
| Mapa-múndi | 10 arquivos | mapa, marcos, pastas, imagens, terrenos | Quase equivalente | Baixa |
| Mercado imobiliário e propriedades | 6 arquivos | 2 janelas | Quase equivalente | Baixa |
| Mundo, avatar, rede, áudio, voz | | | Equivalente (fases 7 a 11) | — |

## O que falta, área por área

> **Levantamento original**, de 25/11/2026. O que foi feito depois está em "Feito depois do levantamento", no fim do arquivo; o que ainda está aberto está em [FALTA_FAZER.md](FALTA_FAZER.md).

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
- Feito depois (26/11, rodada dos detalhes): gráficos de desempenho, mudar o prazo de uma liberação ou banimento, trocar as permissões de quem já tem, detalhe de um objeto e prévia do terreno. A parte 2 deste item (terrenos) foi feita logo depois, abaixo.

### Gestão de terrenos e "Sobre este Terreno" (25/11/2026)
- `UI/LandManagementWindow.cs`: "Gerenciamento de Terrenos" (Mais › Gestão de terrenos, ou o botão na seção Terrenos da gestão da ilha), para o dono e a equipe da ilha, lendo `/land/overview`. Seções: Visão Geral (área, contagem por situação, aluguel por mês, contratos vencendo, objetos), Mapa de Parcelas (cada terreno pintado pela situação, com legenda), Demarcação (marcar os quadrados no mapa e criar o terreno, `/land/claim`), Lista de Terrenos (filtros por situação e busca), Vendas e Aluguéis (os anunciados, tirar do anúncio, anunciar um terreno, encerrar um aluguel), Contratos, Prims e Recursos (uso por terreno), Permissões (acesso de grupo, `/group-access`) e Histórico (mudanças e dinheiro).
- `UI/ParcelInfoWindow.cs`: "Sobre este Terreno" (Mais › Sobre este terreno abre o terreno onde a pessoa está; também pelo botão Sobre nas listas). Abas: Geral (nome e descrição com a revisão, finalidade, imagem, ponto de chegada com "usar onde estou" e modo de teleporte), Propriedade (comprar, alugar, renovar, encerrar, anunciar ou tirar venda e aluguel, transferir para alguém pelo nome, aceitar, recusar ou cancelar a transferência), Acesso (quem entra, a lista de pessoas por nome com tipo e prazo, os grupos), Opções (quem constrói, scripts, voz, mídia, devolver objetos soltos), Objetos (devolver os marcados ou os de quem não tem permissão), Mídia, Histórico (donos, negócios e dinheiro) e Auditoria. Quem não pode mudar vê tudo só para leitura.
- `UI/SectionWindow.cs`: a moldura comum (menu lateral, resumo, páginas, recusas, respostas atrasadas descartadas, aviso de conflito) agora é usada pelas três janelas de gestão; as recusas dos terrenos viram frases (`LandManagementWindow.Words`, os mesmos códigos do Godot).
- Verificado: EditMode 137/137 (5 testes novos); auto-teste 248 checks (17 novos: todas as seções e abas, com recusas de dados errados); API real 19/19 (passo novo: visão geral, demarcar um terreno pela janela, as outras seções, renomear, quem constrói, pôr B na lista pelo nome, as outras abas sem erro e B vendo o terreno só para leitura). Fotos com GPU: `img/ui-land-management.png`, `img/ui-parcel-info.png`. Numa rodada o check antigo de "sentar no objeto" falhou de novo e passou na seguinte (anterior a esta mudança).
- Feito depois (26/11, rodada dos detalhes): clicar num terreno do mapa de parcelas abre o "Sobre" e a imagem do terreno é escolhida no inventário. O "Sobre este terreno" pelo clique direito no chão e mostrar o terreno no perfil já existiam.

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
- Feito depois (26/11, rodada dos detalhes): arrastar um item até uma pasta (ou até Início), o clique direito na linha com Vestir, Detalhes e Lixeira, e a opção de mostrar o histórico a quem tiver o item depois.

### Busca global (25/11/2026)
- `UI/GlobalSearchWindow.cs`: o botão Buscar da barra abre a busca nova, no serviço de busca da plataforma (`/api/v1/search`). Abas: Para você, Tudo, Pessoas, Lugares, Eventos, Grupos, Itens, Lojas e Favoritos (as mesmas sete do Godot mais Para você e Favoritos).
- Busca: sugestões enquanto digita (`search/suggest`), ordem (relevância, popularidade, recentes, nome; eventos por início; itens por preço), filtros de itens (preço mínimo e máximo, criador, direitos) e de eventos (só gratuitos), páginas de 20, contagem por tipo no topo.
- Detalhes de um resultado: ir até lá (ou visitar a loja), ver perfil e ações com a pessoa, ver o grupo, participar do evento, abrir o mercado, favoritar, copiar o link do jogo (`lifeverse://…`, o mesmo formato do Godot) e denunciar com motivo; abrir e viajar avisam o serviço (`search/events`) para os lugares populares.
- Para você: abrir um link do jogo colado (só o formato exato, então um link nunca abre nada de fora), as últimas buscas (guardadas só neste computador, com a opção de não guardar e de apagar), os interesses (`search/interests`), recomendados, lugares populares e lugares onde a pessoa esteve.
- As recusas viram frases (denunciar o que é seu, favoritos cheios, denúncias demais).
- A janela antiga (`SearchWindow`) continua no código porque os testes antigos a usam.
- Verificado: EditMode 145/145 (5 testes novos); auto-teste 261 checks (6 novos); API real 21/21 (passo novo: interesses salvos, a ilha de Alice achada no índice de busca de verdade, favoritar, a API recusando A denunciar a própria ilha e aceitando a denúncia de B, ir até lá e as outras abas sem erro). Foto com GPU: `img/ui-search.png`.
- Feito depois (26/11, rodada dos detalhes): miniaturas dos resultados (imagens desta API e dos itens; nunca de outro site; sem imagem, o ícone do tipo) e as setas com Enter. Ainda falta: comprar produto e loja dentro dos detalhes (hoje abre o mercado).

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
- Feito depois (26/11, rodada dos detalhes): textura só na face escolhida (do sistema ou do inventário, ou a face sem textura) e o botão Permissões do conteúdo, que explica o que o objeto e o item permitem. O "dono anterior" aparece no detalhe do objeto na gestão da ilha.

### Grupos, carteira e perfil (25/11/2026)
- `UI/GroupsHubWindow.cs` (botão Mais › Grupos): Meus grupos (com o grupo ativo, cuja etiqueta e título aparecem no topo; usar ou deixar de usar), Descobrir (busca com categoria, idioma, forma de entrada e só sem taxa; entrar, pedir para entrar, cancelar o pedido; taxa de entrada confirmada pelo valor que a API pede), Convites, Avisos dos grupos (marcar tudo como lido) e Criar grupo em 4 passos (Informações, Entrada, Configurações, Confirmação), com as regras do Godot (`LifeVerse.Shared.Groups.GroupRules`).
- `UI/GroupProfileWindow.cs`: o perfil do grupo com as 10 abas do Godot: Geral, Membros (convidar pelo nome, aprovar ou recusar pedidos, silenciar, expulsar, banir, mudar o cargo), Cargos (criar, apagar, título), Permissões (as 19 permissões por cargo, como no Godot), Avisos (com prazo e fixar), Chat, Terrenos (do grupo, dar um terreno seu, devolver), Economia (baú: dar, retirar com motivo, congelar, extrato, quem mais deu), Propostas (abrir votação com opções, prazo e critério; votar; encerrar; cancelar) e Configurações (silenciar os avisos, perfil do grupo, banidos, histórico, passar a liderança e desfazer, com o nome do grupo para confirmar). As recusas viram frases.
- `UI/WalletHubWindow.cs` (botão Carteira): Resumo (disponível, reservado, total, recebido e gasto em 7, 30 ou 90 dias), Extrato (filtro por tipo e sentido; recibo de cada movimento com código e taxa), Comprar VerseCoin (pacotes com bônus e preço em reais, formas de pagar, o pedido e como ele está; o jogo nunca pede dados de cartão ou banco), Taxas da plataforma e Enviar (o formulário que já existia). O saldo aparece na barra de cima e muda sozinho pelo canal ao vivo (`wallet_changed`).
- `UI/ProfileHubWindow.cs` (Mais › Perfil, e o perfil de outra pessoa pela busca e pelas ações): Sobre (foto, capa, bio, fora do jogo, interesses, e editar com a privacidade), Feed (publicar texto com imagem da galeria, para todos ou só amigos; apagar), Galeria (pôr imagem do inventário, usar como foto, subir e descer, capa), Conquistas, Estatísticas, Favoritos, Grupos e terrenos (mostrar ou esconder no perfil) e Ações no perfil de outra pessoa (mensagem, amizade, teleporte, bloquear e enviar um presente do inventário).
- As janelas antigas (`GroupsWindow`, `GroupDetailWindow`, `WalletWindow`, `ProfileWindow`) continuam no código porque os testes antigos as usam (o envio de moedas ainda usa a `WalletWindow`).
- Verificado: EditMode 162/162 (5 testes novos); auto-teste 283 checks (5 novos); API real 24/24 (2 passos novos: A cria o grupo nos 4 passos, convida B pelo nome, B aceita, cargo novo com a permissão de convidar, aviso que B vê, chat, o baú recusando sem saldo em palavras, votação aberta e o voto de B, grupo ativo; carteira com as abas e o pedido de moedas respondendo em palavras; bio salva, publicação no feed que B vê, e B sem poder editar o perfil de A). Fotos com GPU: `img/ui-group.png`, `img/ui-wallet.png`, `img/ui-profile.png`.
- Feito depois (26/11, rodada dos detalhes): abas Eventos (marcar, ir, cancelar) e Loja (produtos à venda, lojas no mundo, pôr à venda escolhendo no inventário, abrir loja), o botão Perfil de cada membro e ícones no extrato. Ainda falta: ícones nas conquistas.

### Estúdio do avatar: visuais salvos, corpo, roupas e animações próprias (26/11/2026)
- `UI/AvatarStudioWindow.cs` (Mais › Avatar: visuais, corpo, roupas e animações): Visuais salvos (salvar o avatar de agora, vestir, copiar, compartilhar com um código de 8 letras copiado para a área de transferência, parar de compartilhar, trazer pelo código, apagar), Corpo próprio (enviar GLB, triângulos, ossos, altura, usar, voltar ao corpo do jogo, relatório das checagens), Roupas próprias (enviar, encaixe, tipo, camada, o que cobre, vestir e tirar sem mexer no resto do avatar), Animações próprias (enviar, os clipes de cada arquivo, escolher uma animação do jogo ou própria para cada um dos 18 estados) e Editor e espelho (o editor do personagem que já existia e a câmera de frente para o avatar). Os envios mostram o motivo das checagens do servidor quando ele recusa.
- `Avatar/AvatarWardrobe.cs`: no mundo, num corpo com esqueleto (o homem do jogo ou um corpo próprio), as roupas próprias aparecem para todos: uma peça rígida (chapéu, óculos, mochila) presa ao osso com o deslocamento, o giro e o tamanho dela; uma roupa com esqueleto presa aos ossos do corpo pelos nomes padrão, então dobra com o corpo. As animações próprias tomam o lugar das do jogo no estado escolhido (`SkeletalBody.Overrides`). Os arquivos vêm de `/api/v1/avatars/wearables|animations/{id}/file`, um download por arquivo.
- `FollowCamera.Mirror`: a câmera de terceira pessoa fica na frente do avatar enquanto o espelho está ligado; fechar o estúdio desliga.
- Layout: as linhas das janelas de gestão agora quebram e os botões não se espremem (vale para todas as janelas feitas sobre `SectionWindow`).
- Verificado: EditMode 165/165 (3 testes novos); auto-teste 287 checks (4 novos: chapéu preso ao osso da cabeça, camiseta usando os ossos do corpo, o clipe próprio "Aceno" tocando no estado Parado, os visuais e o espelho); API real 25/25 (passo novo com arquivos de verdade: corpo próprio aceito e em uso, camiseta e chapéu aceitos, chapéu vestido, animação própria aceita e escolhida para Parado, visual salvo, compartilhado e trazido por B pelo código, e a volta ao corpo do jogo). Durante o teste a API recusou com razão uma camiseta que era um corpo inteiro, uma animação que mexia só 1 osso e trazer um visual com o chapéu próprio de outra pessoa; as três recusas agora aparecem em palavras. Foto com GPU: `img/ui-avatar-studio.png`.
- Feito depois (26/11, rodada dos detalhes): as variações da biblioteca do jogo (relaxado, atento, alegre, cansado, disparada, balançado, giro, aceno grande, reverência, riso dobrado) e os sliders do corpo (altura, ombros, tronco, braços, pernas, cabeça) num corpo próprio. Ainda falta: refazer o encaixe de uma roupa com esqueleto, os sliders do rosto num corpo próprio e o guarda-roupa do homem do jogo (os arquivos não estão no repositório).

### Câmera livre, menu do botão direito e modos da interface (26/11/2026)
- Câmera livre como no Godot (`Avatar/FollowCamera.cs`, regras de `shared/Camera/CameraControl.cs`): ALT + botão esquerdo põe o foco no que foi clicado (pessoa, objeto ou chão) e gira em volta e aproxima; CTRL+ALT gira nos dois sentidos; CTRL+ALT+SHIFT desliza o foco. A roda aproxima do foco (CTRL para ir devagar). Andar ou Esc devolve a câmera ao avatar. Enquanto o gesto da câmera acontece, o clique não abre o menu do objeto.
- Menu do botão direito (`GameApp.ContextItems`, no mesmo anel do menu radial): um clique rápido do botão direito (segurar e arrastar continua girando a câmera) abre ações para o que está embaixo do cursor. No próprio avatar: meu perfil, avatar, gestos, sentar no chão, primeira pessoa e inventário. Em outra pessoa: perfil e ações (mensagem, trazer, ir até, amizade, bloquear) e dar zoom. Num objeto: tocar, sentar, dar zoom, scripts, editar e mais. No chão: teleportar para cá, sobre este terreno, olhar para cá e construir. A pessoa é achada pelo raio da tela passando pelo corpo (`AvatarPick`), e a mais perto entre pessoa e objeto ganha.
- Modos da interface (`interface.mode`): completo, compacto (só as barras, botões menores, sem mapa e sem chat) e imersivo (só o mundo; F10 mostra e esconde a interface). `interface.compact_toolbar` deixa a barra pequena. A barra inferior tem ordem e botões escondidos configuráveis em Mais › Barra de ferramentas (`ToolbarLayout`; pelo menos um botão fica), e "Restaurar a barra" volta ao padrão (também em Configurações › Interface).
- O chat minimiza (botão no canto; as mensagens ficam guardadas). A barra de cima ganhou voltar e avançar entre as ilhas visitadas na sessão (`IslandHistory`, até 20; o histórico só anda quando a viagem chega). A câmera lembra para onde olhava em cada ilha.
- F12 tira uma foto do mundo em Imagens/LifeVerse ("LifeVerse 2026-11-26 07-30-12.png"), sem a interface a não ser que `camera.photo_include_ui` esteja ligado, em PNG ou JPG (`camera.photo_format`).
- Corrigido de passagem: com a câmera presa num ponto, a colisão com o chão usava a distância normal em vez da distância do zoom.
- Verificado: EditMode 170/170 (5 testes novos: ordem da barra, histórico de ilhas, nome e formato da foto, qual avatar o raio acerta, a câmera livre focando, girando e deslizando); auto-teste 298 checks (11 novos: o botão direito no próprio avatar e no chão ou num objeto, o menu abrindo no anel, a câmera livre girando em volta do foco, os modos compacto, imersivo com F10 e completo, a ordem e os botões escondidos da barra e restaurar, minimizar o chat, o histórico com a ilha entrada e a foto F12 na pasta). Nada novo na API, então o teste com a API real não muda. Fotos com GPU: `img/ui-context-menu.png`, `img/ui-compact.png`, `img/ui-toolbar.png`.
- Feito depois (26/11): ícones de verdade na barra compacta (rodada visual), e na rodada dos detalhes a câmera desacoplada (ALT + V, com alcance e velocidade), o foco por clique duplo, arrastar os botões da barra para reordenar e "Pagar…" no clique direito de uma pessoa.

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
- Faltavam 43 opções sem efeito no Unity (feitas na rodada seguinte, abaixo), por exemplo:
  - detalhes e distâncias dos avatares;
  - as camadas do mapa-múndi e o mini-mapa girando;
  - voz espacial e distância de audição;
  - correr com toque duplo e segurar para voar;
  - ondas da água e qualidade automática.

  Outras 33 trazem o mesmo motivo do Godot para não agir (por exemplo, privacidade que depende de uma preferência de conta que o servidor ainda não guarda). Os títulos de grupo sobre o nome também vieram na rodada seguinte (a aparência do avatar já traz o `groupLabel`).

### Visual do Godot: ícones, barras, menus, placas de nome e o mundo lado a lado (26/11/2026)
- **Ícones** (`UI/UiIconData.cs`, `UI/Icon.cs`): os 111 ícones do Godot (Tabler, MIT), com os mesmos dados, desenhados pelo próprio Unity a partir do SVG (linhas, arcos, círculos e retângulos viram curvas do pincel vetorial). Ficam nítidos em qualquer tamanho e cor. Aparecem:
  - nos botões da barra, com ícone e nome; a barra compacta mostra só o ícone, com o nome na dica;
  - no título das janelas e no botão de fechar;
  - nos avisos (info, ok, alerta, erro);
  - nos menus do alto.
- **Barra de cima, como `01_barra_superior.png`:**
  - o logo em cubo;
  - os menus Eu, Comunicar, Mundo, Conteúdo, Ajuda e Desenvolver, com seta, que abrem listas com ícones (as mesmas janelas do "Mais", agrupadas como no Godot);
  - o pino antes da ilha;
  - a moeda com o saldo e o "+" que abre a carteira.
- **Barra de baixo, como `02_barra_inferior.png`:**
  - a ordem do Godot (Voz, Gestos, Foto, Construir, Pessoas, Mapa, Inventário, Mini-mapa, Buscar, Carteira, Avisos), com Gestos, Foto e Mini-mapa novos;
  - o botão de uma janela aberta fica verde-água, e o do inventário fica laranja;
  - Chat, Social, Mais, Configurações e Ajuda começam escondidos porque estão nos menus; quem arrumar a barra mantém a própria escolha.
- **Paleta:** conferida com o `UiStyle` do Godot, que é o código que vai para o jogo. As cores do tema do Unity já eram as mesmas. A imagem `13_paleta_tipografia.png` é um rascunho anterior com tons um pouco diferentes.
- **Placas de nome, como `09_placas_nome.png`** (`UI/NamePlates.cs`): uma pílula de vidro escuro com contorno claro, o ponto de status (verde online, cinza ausente, vermelho ocupado, para o próprio avatar), o nome e as ondas da voz enquanto a pessoa fala. Quando e onde a placa aparece continua sendo a regra do avatar (distância, opção de mostrar nomes).
- **O mundo lado a lado** (`client/Tests/WorldCompareCapture.tscn` no Godot e `-lifeverse-world-preview` no Unity): a mesma ilha esculpida, as mesmas 9 câmeras, horas e climas, fotografadas nos dois motores. As fotos estão em `img/compare/` (`godot-*.png` e `unity-*.png`; `unity-island-antes.png` é o Unity antes dos ajustes). A geometria bate exatamente. Ajustes feitos a partir da comparação:
  - **céu:** as nuvens do céu do Godot (o mesmo ruído e a mesma projeção na cúpula, pela camada de nuvens do HDRP; havia uma camada de nuvens no Unity que nunca aparecia por faltar ligar o tipo de nuvem), e o azul do alto mais forte;
  - **chão:** sem a granulação que se repetia a cada metro (o Godot não tem), cozinhado de uma versão filtrada de cada camada (antes saía chiado) e no mesmo tom de verde;
  - **mar:** azul fundo e mais opaco, para a areia do fundo não aparecer.
- **Ainda diferente do Godot:** a água não muda de cor com a profundidade (turquesa no raso, escura no fundo), os morros saem um pouco mais escuros, a repetição da textura do chão aparece um pouco mais e a espuma da beira é mais irregular.
- Verificado: EditMode 191/191 (2 testes novos: os 111 ícones viram desenho, caminhos e arcos chegam ao ponto certo); auto-teste com os menus, os ícones da barra, o estado aberto do inventário e as placas de nome; fotos das interfaces com GPU: `img/ui-icons.png`, `img/ui-menu.png`, `img/ui-plates.png`.

### As 43 configurações que faltavam (26/11/2026)
Cada uma segue a regra do Godot (`client/Scripts/Settings/*`), em `UI/SettingsEffects.cs` e onde a coisa acontece:
- **Avatares** (`SettingsEffects.BudgetFor`, a mesma conta do Godot): limite de completos e de reduzidos, distância de visibilidade, orçamento de triângulos, LOD (perto 0,6×, médio 1×, longe 1,5× nas distâncias) e "simplificar distantes" viram o `DetailBudget` do planejador compartilhado. "Qualidade das animações" faz o avatar reduzido mexer a cada 1, 2 ou 3 quadros (`AvatarView.AnimationDivisor`).
- **Mundo:** distância dos objetos (nenhum objeto é desenhado além dela; 1000 m não esconde nada além do alcance próprio), ondas da água (desligadas, o mar fica plano) e qualidade automática (média dos quadros em janelas de 5 s; depois de 3 janelas abaixo de 70% da meta, a predefinição desce um nível, no máximo uma vez por minuto, nunca a Personalizada; o jogo avisa).
- **Voz:** ligar e desligar (nada sai, nada entra, e o botão Voz avisa), microfone aberto, distância de audição (até os 25 m que a ilha manda; mais longe fica mudo), voz espacial (desligada, todo mundo soa igual nos dois ouvidos) e indicadores de fala sobre a cabeça.
- **Controles** (`Avatar/MoveGestures.cs`): dois toques rápidos para a frente (até 0,32 s) correm até soltar; segurar pular por 0,45 s começa a voar, uma vez por aperto.
- **Câmera em primeira pessoa:** suavização do olhar (a mesma taxa compartilhada `LookRate`) e colisão (algo sólido entre o pescoço e os olhos puxa os olhos de volta, com o `EyeCollision` compartilhado).
- **Mini-mapa:** aberto (o jogo começa com ele fechado, como no Godot, e o botão da barra e a opção andam juntos), girar com a câmera (vira um disco e gira a cada quadro), nomes das pessoas e contorno das construções (as caixas dos objetos maiores que 1,5 m).
- **Mapa-múndi** (`MapLayers`): terrenos, nomes, e selos sobre as ilhas para eventos (de `/discovery/events`), destaques (a de boas-vindas e as mais visitadas e favoritadas, como no Godot), favoritas e ilhas com amigos.
- **Interface:** velocidade de abertura do menu radial (com "Reduzir movimento" abre sem animar) e dicas do menu radial (inclusive por que uma ação está indisponível); janela sempre no topo (Windows).
- **Avatar:** títulos de grupo embaixo do nome na placa (`groupLabel` da aparência), animações padrão nos corpos personalizados (desligadas, só o movimento básico) e o botão Espelho (abre o estúdio do avatar).
- **Outras:** mídia externa (desligada, nenhuma imagem ou vídeo de objeto é baixado e a superfície volta ao material próprio), eixos da construção (Mundo ou Objeto: as setas seguem os lados do objeto), colisão padrão de modelos importados (a janela de importar e a opção andam juntas), "Baixar atualização" (abre o endereço da versão nova, com as mesmas regras de segurança do instalador) e navegação por teclado (Esc fecha, Ctrl+F vai para a busca, Tab e setas movem o foco).
- **Sem efeito, com o motivo na tela:** saída de áudio (o motor do Unity sempre usa o dispositivo padrão do Windows) e barra lateral (este cliente não tem a barra; os atalhos estão na barra de ferramentas e nos menus).
- Verificado: EditMode 197/197 (6 testes novos: toda opção age ou diz por que não, orçamento dos avatares, qualidade automática, toque duplo e segurar para voar, selos do mapa, dicas do radial e distância dos objetos); auto-teste 331 checks (novos: mini-mapa começa fechado e abre pela opção, nomes no mini-mapa, mini-mapa girando com a câmera, voz desligada e ligada, orçamento dos avatares, mídia externa).

### Detalhes menores por área (26/11/2026)
- **Gestão da ilha:** gráficos de linha do desempenho (quadros, milissegundos, memória e pessoas, com mínimo e máximo), "Mudar prazo" em liberações e banimentos (vazio = para sempre), "Trocar pelas marcadas" nas permissões de quem já tem, "Detalhe" de cada objeto (dono, criador, dono anterior, custo, scripts, física, posição, giro, tamanho, trava, validade e permissões, com "Devolver ao dono") e a prévia do terreno salvo (a grade de alturas que a API manda, com as cores do mini-mapa).
- **Terrenos:** clicar num terreno do mapa de parcelas abre o "Sobre"; a imagem do terreno é escolhida no inventário (só imagens suas, como a API exige).
- **Inventário:** arrastar um item até uma pasta (ou até Início) o muda de lugar; o botão direito na linha abre Vestir, Detalhes e Lixeira; nos direitos do item, o dono escolhe se quem tiver o item depois vê o histórico.
- **Busca:** miniaturas nos resultados (imagens e itens desta API, nunca de outro site; sem imagem, o ícone do tipo) e as setas para cima e para baixo com Enter para abrir.
- **Construção:** "Textura nesta face…" e "Face sem textura" na seção de uma face só; "Permissões" no conteúdo explica se o conteúdo pode mudar e quais direitos o item mantém dentro do objeto.
- **Grupos:** aba Eventos (marcar com data, ilha e vagas; "Vou" e "Não vou mais"; cancelar), aba Loja (produtos com preço e vendas, lojas no mundo com "Visitar", pôr um item à venda escolhendo no inventário, abrir uma loja), botão Perfil em cada membro e ícones no extrato.
- **Avatar:** as variações da biblioteca do jogo ("builtin:…") no corpo simples, como poses do Godot; os sliders do corpo num corpo próprio escalam os ossos (pernas e braços ao longo, tronco na largura, cabeça por inteiro; altura e ombros no corpo todo), com os pés no chão.
- **Câmera e interface:** câmera desacoplada (ALT + V solta a câmera, as teclas de andar a movem e o avatar fica parado; Esc devolve; não passa do alcance) e foco por clique duplo em pessoas e objetos, as duas com as opções do Godot (4 opções novas em Câmera › Câmera livre, desligadas por padrão como lá); arrastar um botão da barra sobre outro troca o lugar; "Pagar…" no botão direito de uma pessoa abre a carteira com ela como quem recebe (o valor é confirmado na carteira).
- **Ainda falta:** comprar produto e loja dentro dos detalhes da busca, ícones nas conquistas do grupo, refazer o encaixe de uma roupa com esqueleto, os sliders do rosto num corpo próprio e o guarda-roupa do homem do jogo (sem os arquivos).
- Verificado: EditMode 204/204 (7 testes novos: gráficos, prévia do terreno, de onde vêm as miniaturas, arrastar, direitos do conteúdo e datas, variações de animação, câmera desacoplada e clique duplo); auto-teste 331 checks; API real 28/28 (passo novo: prazo do banimento e da liberação, troca de permissões, gráficos de desempenho, evento e loja do grupo, histórico do item).

### Pendências curtas e opções do cliente (26/11/2026)
- **Avisos imobiliários:** aba "Avisos" em Minhas propriedades (com a contagem na aba e uma faixa em cima enquanto há o que ler), marcar um ou todos como lidos; o jogo pergunta pelos avisos 8 s depois de entrar e a cada 5 minutos e anuncia cada um uma vez.
- **Busca:** a compra pelo detalhe pede confirmação, confere o saldo, manda uma só chave enquanto não houver resposta e relê o item; o detalhe de uma loja mostra dono, lugar, categoria e destaques, com "Ver produtos" (aba Itens só daquele vendedor) e "Ver o proprietário"; "Ver a loja" num item abre a loja. O botão "Abrir o mercado" saiu.
- **Perfil:** conquistas com o ícone que o servidor indica (estrela quando o cliente não o tem), na aba Conquistas e no Feed. Conquistas de grupo não existem no servidor.
- **Voz por terreno:** num terreno com a voz desligada o microfone fica fechado, o botão vira "Sem voz" e aparece "A voz está desligada em <terreno>". O servidor de ilha já cortava a voz. A versão do terreno é perguntada a cada 10 s, e uma mudança de regra ou de música chega sozinha.
- **Sincronia com a conta:** vão todas as configurações que agem no Unity e estão na lista da plataforma (`AccountPreferenceRules`), mais as teclas; o que o Unity só mostra desligado fica como a conta tem.
- **Roupa com esqueleto:** é reencaixada na pose de descanso do corpo de quem veste, como no Godot (aparece onde foi feita, no espaço do modelo, e dobra com os ossos).
- **Opções que passaram a valer:** volume dos sons da interface, "LOD de objetos", "Tamanho do cache de recursos" (o padrão passou de 512 MB para os 2048 MB do catálogo) e "Ping, jitter e perda de pacotes" (no painel de desempenho). O `LifeVerse.Wire` ganhou a perda de pacotes (o protocolo não mudou).
- **Verificado:** EditMode 214/214 (10 testes novos), testes do protocolo 25/25, o `tools/ApiLiveHost` compila e o cliente Windows compila.
- **Verificado dentro do editor** (o Controle Inteligente de Aplicativos bloqueia o `LifeVerse.exe` nesta máquina; entraram `Server/CheckRun.cs` e `Editor/EditorChecks.cs` para rodar as mesmas verificações em modo Play): API real local 31/31 e autoteste do mundo com 331 verificações. O player gerado em si não foi executado, e não houve fotos.
- **Defeitos achados por essas execuções e corrigidos:** o `GameApp` lia a pasta de dados num inicializador de campo, o que quebrava o jogo ao apertar Play no editor; a sessão expirada era avisada fora da thread principal, e a tela de entrada não voltava (a parada dos avisos imobiliários mexe na interface nesse caminho); quando o pedido de sentar num objeto se perdia e era reenviado, a pessoa acabava sentada fora do objeto (a causa do teste de sentar "que às vezes falha"); e o passo novo de compra da verificação ao vivo pedia um pacote de moedas que não existe.

### Aparência e física (26/11/2026)
- **Imagem:** o último passo passou a ser o do Godot (tonemapper filmic com branco 6, contraste e saturação do preset em "Ajuste de cor"), por uma tabela externa do HDRP; sol, céu e luz ambiente nas mesmas unidades do Godot, e o sol do mesmo lado (estava espelhado).
- **Mar:** shader próprio portado do Godot (cor pela profundidade, três ondulações, espuma, reflexo do céu, brilho do sol, horizonte e o teto visto de baixo), mais barato nos presets Baixo e Médio. A folha de espuma separada saiu.
- **Chão:** cozido com a conta do shader do Godot, granulação calibrada e quase sem brilho. **Debaixo d'água:** o azul que o Godot põe sobre a tela.
- **Defeito antigo corrigido:** a luz direcional padrão na cena inicial iluminava todas as ilhas, inclusive de noite.
- **Física:** 32 cenários nos dois motores; atrito como no Godot (tabela em `MIGRATION_ISLAND_SERVER.md`).
- **Verificado no editor:** EditMode, o build do cliente, o autoteste do mundo e as fotos das nove vistas contra as do Godot. **Não verificado:** o player gerado e o desempenho.

### Interface no formato do Second Life (26/11/2026)
A pedido do dono, a interface do cliente Unity **deixou de imitar a do Godot** e passou a seguir o visualizador do Second Life.
- **Visual:** texturas próprias (barras com relevo, janelas com sombra, botões com volume), geradas por `unity/tools/ui-skin/generate.py`, e um `Theme.uss` novo.
- **Corrigido:** a seta das listas de escolha ficava embaixo do texto; a lista aberta não tinha fundo; as caixas de marcar ficavam fora de linha; o mini-mapa cobria a barra de cima; os títulos não saíam em negrito; as dicas dos botões não apareciam.
- **Novo:** barra de menus com saldo, pessoas, hora e conexão; barra de endereço com voltar/avançar, ilha, posição e busca; barra de ferramentas com ícone e nome; janelas que minimizam e mudam de tamanho; janelas **Mover** e **Câmera**; mini-mapa com zoom, fechar e o norte; avisos embaixo à direita; entrada e carregamento refeitos; ícones nos menus laterais e no inventário.
- **Verificado no editor:** EditMode 225/225, autoteste do mundo com 337 verificações e fotos das telas (`img/ui-*.png`). **Não verificado:** o player gerado.

### O que faltava, parte cliente e rede (27/11/2026)
- **Servidor de ilha:** limite de envio por jogador; uma mensagem que quebre o próprio tratamento não leva o passo da simulação junto; **corrigido** (Godot e Unity): uma resposta de script com formato errado derrubava o passo; **corrigido:** com atraso variável, os jogadores de uma ilha cheia recebiam metade dos snapshots.
- **Imagens:** WebP lido no cliente. **Avatar:** rosto do corpo simples e o corpo visível em primeira pessoa.
- **Céu:** o shader de céu do Godot como céu próprio do HDRP e a neblina por distância do Godot; chuva, tempestade, pôr do sol e noite agora batem nas fotos lado a lado (o `WorldCompareCapture` passou a esperar a neblina chegar).
- **Interface:** abas de conversa, inventário em árvore, tamanho das janelas guardado, barra de ferramentas numa borda.
- **Opções que passaram a valer:** limite de download, uso de CPU e GPU, verificação dos arquivos (o build grava `integrity.json`) e legendas.
- **Verificado:** EditMode 233/233, autoteste do mundo 339, API local 31/31, protocolo 33/33, servidor de ilha 63/63, API 2.566/0, build do Windows. **Não verificado:** o player gerado, desempenho, internet de verdade.

### PostgreSQL, privacidade e o que faltava (26/11/2026)
- **API sobre PostgreSQL:** `ApiLiveHost` aceita `LIFEVERSE_LIVE_PG`. **Corrigidos** defeitos que o banco em memória não mostrava: privacidade (perfil, presença, mensagens e busca) ignorada porque o `jsonb` volta com outro espaçamento; consultas de texto em `jsonb` (busca com pessoas, abas Física e Desempenho, pagamento por script, conferência do inventário) dando erro 500; datas com fuso recusadas; JSON inválido dando 500. A renovação de sessão da verificação ao vivo passou a ser feita pelo cliente (antes nunca tinha rodado).
- **Privacidade e sessões:** quatro escolhas aplicadas no servidor, lista de bloqueados com desbloqueio e sessões abertas com encerramento; no Unity, a janela "Privacidade e sessões".
- **Anexos:** prévia no corpo enquanto se digita. **Janelas:** encaixe nas bordas e entre janelas.
- **Distribuição:** avisos de licença de terceiros gerados e copiados pelo build.
- **Servidor de ilha:** teste de volta longa (`SoakTests`). O teste de 20 minutos achou um defeito no servidor Unity: a releitura dos objetos pelos scripts mudava, em outra thread, a lista de paredes que a simulação percorria (66 passos com erro em 20 minutos). **Corrigido:** o que o servidor espera volta dentro de um passo (`IslandServer.Thread.cs`), como no Godot. Depois: 1.200 s, 2.394 entradas e saídas, 759.585 snapshots, nenhum passo com erro, p99 do passo 1,13 ms; a memória medida foi de 3,2 para 4,8 MB, dentro do limite do teste (a origem dessa diferença não foi separada).
- **Verificado:** API 2.614/0 com os testes de PostgreSQL; protocolo 33/33; servidor de ilha 64/64; EditMode 236/236; autoteste do mundo 342 (no editor, com fotos); verificação ao vivo 32/32 em PostgreSQL e em memória; build do Windows com os avisos de licença. **Não verificado:** a produção, o player gerado, desempenho, mouse e teclado de verdade.

### Direção do corpo e teclado (27/11/2026, achados jogando no servidor novo)
- **Corpo de costas:** o corpo com esqueleto (o padrão do jogo e os personalizados) era girado meia volta e andava, corria e voava virado ao contrário. Um modelo glTF já olha para a frente no Unity; a meia volta saiu (`SkeletalBody`).
- **WASD escrevendo no chat:** a interface da Unity lê W, A, S, D e as setas como "mover o foco" e Enter como "apertar o que tem o foco". Depois de um clique num botão, o primeiro passo levava o foco ao campo do chat. Em jogo essas teclas agora são só do jogo (`GameKeyboard`; Tab continua percorrendo a interface). Além disso, **Enter no chat envia e devolve o teclado**, como no Godot, e um clique no mundo tira o foco de qualquer campo.
- **Verificado:** o autoteste reproduziu a troca de foco antes da correção; depois, autoteste do mundo 343 (no editor, com fotos, incluindo o corpo de frente) e EditMode 236/236. **Não verificado:** uma sessão com teclado e mouse de verdade.

### Corrida travada (27/11/2026, "corre travado na diagonal")
- **Solavancos na tela:** o movimento é previsto 60 vezes por segundo e o corpo (e a câmera) só mudavam nesses passos; numa tela com outro ritmo havia quadros parados. Agora o desenho fica entre os dois últimos passos (`LocalPlayer.DrawnPosition`).
- **Puxões da ilha:** a previsão era puxada 30% a cada resposta com mais de 8 cm de diferença (a 6,4 m/s, um passo já são 11 cm), e a mesma diferença era corrigida de novo pelas respostas seguintes. Agora, correndo, a margem é de 30 cm e a parte corrigida é 12% (como no Godot), a correção vale uma vez só e entra aos poucos na tela; parado, a margem continua 8 cm.
- **Verificado:** autoteste do mundo 344 (no editor, com um caso de corrida diagonal desenhada a 144 quadros por segundo) e EditMode 237/237. As duas causas foram achadas no código; a segunda **não foi medida pela internet**. O dono testou a correção e confirmou que a corrida ficou boa.

### Godot retirado do repositório (27/11/2026, "tirar o Godot")
- **Saíram:** `client/`, `island-server/`, o modo Godot do supervisor e a sua imagem, os testes de fumaça e a release do Godot no CI, `build-release.ps1`, `protocol-smoke.ps1` e as ferramentas que precisavam do Godot (`unity/PhysicsComparison`, `unity/tools/settings-catalog`, `unity/tools/webp-fixtures`). Tudo continua no histórico (último commit com eles: `177c0c1`).
- **Mudaram de lugar:** os scripts Lua (`unity/UnityIslandServer/Core/Scripting`), o corpo, os sons, o ícone e as licenças (`assets/`), os desenhos de referência (`docs/design/`). `SettingsCatalog.Generated.cs` passa a ser mantido à mão.
- **Testes que saíram:** os dois que comparavam as pontes de rede do cliente e do servidor Godot. Os que liam arquivos do cliente Godot leem agora os do projeto Unity.
- **Verificado:** numa cópia só com os arquivos do git, a solução compilou (a parte `netstandard2.1` com os fontes gerados que já havia aqui, porque a política de execução do PowerShell bloqueou o `build-plugins.ps1`), a API deu 2.569 aprovados e 44 de PostgreSQL pulados, o protocolo 33, o servidor de ilha 64, e o supervisor publicou só com os arquivos que a imagem copia. Aqui: build do jogo para Windows com o corpo e os sons vindos de `assets/` (idênticos byte a byte), instalador compilado pelo Inno Setup com o ícone novo, EditMode 237/237 e autoteste do mundo 345 (no editor). **Não verificado:** a imagem Docker, o CI, instalar o pacote e a produção.

### Instalador (27/11/2026, "gerar o instalador")
- **Defeito achado ao conferir o pacote:** a lista de arquivos do jogo (`integrity.json`) incluía a pasta de depuração que não vai no pacote, então um jogo instalado acusaria arquivo faltando em "Verificar arquivos do jogo" e mandaria instalar de novo. A lista agora só leva o que vai para o jogador (`GameFiles.Write`).
- **Verificado:** EditMode 237/237 (o teste da lista cobre as duas pastas de depuração); zip extraído e conferido contra a lista, 230 arquivos sem problema; o jogo do zip passou na autoverificação (`plugin_self_check`, 6). **Não verificado:** instalar, abrir com janela e entrar numa ilha pela versão instalada. Nem o jogo nem o instalador são assinados.

### Verificação de funcionamento (27/11/2026, commit `dc35fe5`)
- **Passou:** verificação ao vivo contra a API local sobre PostgreSQL, 32/32 (no editor); autoteste do mundo, 345 verificações (no editor). Do mesmo dia: EditMode 237/237 e os testes .NET do protocolo (33) e do servidor de ilha (64).
- **Comparação com o cliente Godot (por busca no código):** faltam no Unity a pré-visualização de itens, o conteúdo de construções, andar com duplo clique, ambiente e clima locais, "Primeiros passos", redefinir a câmera, o editor de HUD e o atalho para a Ilha de Boas-vindas (`FALTA_FAZER.md`, 3.12 a 3.19).
- **Não verificado:** jogar com teclado e mouse, voz com microfone, várias pessoas, a versão instalada e o supervisor em produção.

### Recursos do cliente antigo repostos (27/11/2026)
- **Andar com duplo clique no chão** (para ao chegar, ao encontrar um obstáculo ou quando a pessoa anda pelo teclado); não vale em primeira pessoa nem construindo. Com o foco por duplo clique ligado, pessoas e objetos continuam focando a câmera.
- **R** redefine a câmera (tecla trocável, sincronizada como `camera_reset`). **Mundo → Ir para a Ilha de Boas-vindas** e **Mundo → Hora e clima só para você** (vale até sair da ilha).
- **"Primeiros passos"**: abre na primeira entrada do jogo aberto pela pessoa (não nas verificações automáticas) e em Ajuda; mostra as teclas que a pessoa usa.
- **Inventário → Detalhes → Pré-visualizar** (modelo, textura, animação num esqueleto, som) e **Ver o conteúdo** (peças e o que falta de uma construção).
- **Verificado:** EditMode 240/240; autoteste do mundo 358 (no editor, com fotos das janelas novas), incluindo a caminhada até um ponto contra o servidor da ilha. As prévias e o conteúdo foram testados com arquivos e respostas de teste. **Não verificado:** mouse e teclado de verdade, arquivos reais de um inventário e o jogo gerado.
- **Fica de fora:** o editor visual de HUD (3.18).
- **Depois:** o dono testou no editor e confirmou que os recursos funcionaram.

### Terreno pelo clique no chão (27/11/2026, "não está funcional")
- **O que se achou:** com mouse simulado, abrir o menu do chão e editar segurando o botão esquerdo já funcionavam no servidor de teste. Faltava no menu o **Editar terreno** (o Godot tinha), "Sobre este terreno" num chão sem parcela só mostrava um aviso curto ("Aqui não há terreno"), e uma edição recusada por falta de permissão só aparecia numa linha pequena da janela.
- **Feito:** o menu do chão tem Ir até aqui, Teleportar, Editar terreno, Sobre este terreno, Gerenciar terrenos, Construir aqui, Olhar para cá e Copiar local; sem parcela, "Sobre este terreno" abre uma janela com o que dá para fazer ali; a recusa de uma edição vira um aviso na tela (uma vez, não a cada pincelada).
- **Verificado:** autoteste do mundo 363 (no editor), com cliques de mouse simulados no menu e na edição (o chão sobe), e EditMode 240/240. **Não verificado:** a conta do dono em produção; lá só edita o relevo quem é dono da ilha ou tem a permissão de terreno.

### Fotos no perfil, no grupo e na ilha (27/11/2026, "não está aparecendo")
- **O que se achou:** o perfil só aceitava uma imagem que já estivesse no inventário, escondida na aba Galeria; não havia como pôr imagem num grupo; a ilha só aceitava um endereço https, que a busca não carrega; e a busca não sabia pedir a imagem de grupo (`asset://`).
- **Feito:** **Perfil → Sobre → Foto do perfil**, **Grupo → Configurações → Imagem do grupo** e **Gerenciar esta ilha → Geral → Imagem da ilha**, com envio de arquivo (PNG, JPG ou WebP, até 8 MB) ou escolha do inventário, e a prévia; a busca pede a imagem do grupo pela rota do grupo. Sem mudança no servidor.
- **Verificado:** EditMode 240/240; autoteste do mundo 369 (no editor, com fotos das telas); contra a API real nesta máquina (banco em memória), com os mesmos pedidos que o jogo faz: as três imagens salvas, lidas por outra conta e devolvidas pela busca com um endereço que o jogo consegue buscar. **Não verificado:** a produção, o PostgreSQL e o seletor de arquivos usado com o mouse.

### Protótipos de direção de arte do HUD (27/11/2026, "três propostas realmente distintas")
- **Feito:** Horizonte, Meridiano e Varanda em montagem só de editor (`Assets/LifeVerse/Prototypes/HudDirections`), janela de comparação (**LifeVerse › Protótipos de HUD**) e fotos sobre um fundo desenhado pelo próprio jogo. Nada do jogo mudou. Detalhes: `docs/ui-directions/README.md`.
- **Verificado:** 27 fotos revistas; 94 funções do HUD com lugar em cada proposta (não ligadas); janela com 27 combinações; EditMode 240/240. **Não verificado:** uso com o mouse, animações em movimento, 1366×768, 1440p, 4K e 32:9. O Horizonte fica abaixo de 4,5:1 no alto da tela de dia.

### Envio de foto falhando em produção (27/11/2026, "problema na API")
- **O que o log mostrou:** `Name does not resolve (verselife_minio:9000)`: a API não encontra um servidor com esse nome (sugerido por mim sem ter conferido). O arquivo não chega ao MinIO. O endereço anterior era `lifeverse-minio:9000`.
- **Nome interno informado pelo dono:** `verselife_minio_minio` (a porta 9001 que veio junto é a do painel web do MinIO; a API usa a 9000).
- **Resolvido em produção:** com `ObjectStorage__Endpoint=http://verselife_minio_minio:9000` e a correção abaixo (commit `3f13a5e`), o dono confirmou que funcionou.
- **Feito no código:** se o nome tiver `_`, a API continua conectando por ele, mas envia e assina o `Host` com `-` (o MinIO recusa `_`, segundo relatos). **Verificado:** testes da API 2.570 aprovados, 44 de PostgreSQL ignorados; em produção, pelo relato do dono. **Não verificado:** se a busca mostra as três imagens em produção, e o caso sem a correção contra um MinIO real.

### Interface Varanda no jogo (27/11/2026, "fazer o varanda")
- **Feito:** o HUD da Varanda (placa do lugar com os rostos de quem está perto; busca, saldo, sino, relógio e conexão no alto; barra com o próprio rosto e a presença no meio; **Mais** abre "Tudo da ilha" com todos os menus e "Sua varanda": botões à vista, lado da barra e modo), conversa em balões que se recolhe quando ninguém fala, mini-mapa em cartão-postal, avisos em bilhetes no alto à direita, placas de nome com rosto, menu radial, dicas e **todas as janelas** nas cores e letras novas. Papel escuro à noite, seguindo o céu (ou fixo, em Configurações → Interface). Escala automática mínima de 0,75 para 0,85, e ela acompanha a janela que muda de tamanho. Contorno de foco nos botões e campos; contraste alto passou a trocar cores de verdade. Fontes Fraunces e Nunito Sans (Google Fonts, OFL, nos avisos de terceiros).
- **Mudou para quem joga:** a barra de cima com os menus saiu (estão em **Mais**); por padrão a barra mostra Chat, Voz, Gestos, Pessoas, Mapa, Construir, Inventário, Foto e Mais (Mover, Câmera, Mini-mapa, Buscar, Carteira e Avisos podem voltar por "Sua varanda"; busca, saldo e sino estão no alto; o Mini-mapa ganhou entrada no menu Mundo). Quem já tinha arrumado a barra continua com a sua. A antiga janela "Mais" saiu: tudo o que ela tinha está na folha.
- **Defeitos achados pelas fotos e corrigidos:** falas passando por cima das abas da conversa; posição colada no número de pessoas; mini-mapa sob a barra em tela estreita; folha de menus cortada em tela baixa; e um antigo, que a folha revelou: depois da barra de lado, quatro botões ficavam sem dica.
- **Verificado:** EditMode 246/246 (com contraste de pelo menos 4,5:1 medido para as cores de texto do dia e da noite); autoteste do mundo 370 no editor, com fotos; `-lifeverse-ui-shots` 14 verificações em 1280×720, 1366×768, 1920×1080 e 2560×1080. **O autoteste falhou algumas vezes** no clique simulado do menu do chão (passou nas repetições) e uma vez na abertura do criador de personagem.
- **Não verificado:** mouse e teclado de verdade, o player gerado, a API real e a produção, 1440p, 4K, 32:9, desempenho e animações em movimento.

### Importação de modelos, texturas e animações (28/11/2026, "está tudo funcional?")
- **Feito:** nenhuma mudança no jogo; conferência com arquivos criados para o teste, por uma sonda temporária no editor (removida) e pela API local em memória.
- **Passou:** DAE com eixo Z, centímetros, polylist, textura em pé, esqueleto de 2 ossos e animação (a malha dobra, vista em foto); glTF com `.bin` e imagem ao lado (nome com espaço) e animação; os GLB de `tests/fixtures`; a API local aceitou os modelos convertidos e devolveu os mesmos bytes.
- **Falhou:** DAE com a textura em caminho de outro computador, só pelo nome com o arquivo numa subpasta, ou em TGA (o modelo entra sem textura, com aviso).
- **Não verificado:** colocar um modelo com textura numa ilha de verdade, o jogo gerado, a produção, arquivos exportados por Blender ou SketchUp e a importação de corpo, roupa e animação de avatar.

### Correções da importação de modelos (28/11/2026, "sim, corrigir")
- **Feito:** as texturas de um `.dae` são procuradas pelo nome na pasta do modelo e até 3 níveis abaixo quando o caminho gravado não serve (nada fora da pasta nem da internet é lido), e TGA e BMP viram PNG (`DaeUploadPackager`, `PictureConverter`); **Construir → Recursos → Animação do modelo** liga a animação, escolhe o clipe, toca, pausa, repete e muda a velocidade; a janela de importar rola o conteúdo, cabe na tela e a prévia não mostra mais o céu da ilha atrás do modelo.
- **Defeito do teste achado e corrigido:** o autoteste falhava às vezes em "o criador de personagem abrir sozinho" porque o servidor de teste ainda anunciava a atualização obrigatória de um passo anterior e, conforme o tempo, o jogo novo recusava o login.
- **Verificado:** EditMode 251/251; autoteste do mundo 372 (no editor, com fotos), incluindo a animação de ponta a ponta pelo servidor da ilha de teste; em foto, os cinco casos de textura de DAE usados na conferência anterior. **O clique simulado do menu do chão falhou numa rodada** e passou na repetição.
- **Não verificado:** arquivos de Blender ou SketchUp, a API real e a produção (o carimbo de início da animação é do servidor real), o jogo gerado e mouse de verdade.

### Scripts, anexos, HUD e animações (28/11/2026, perguntas do dono)
- **Feito:** só leitura do código; nada mudou no jogo. O que se achou está em `FALTA_FAZER.md` §5.3.
- **Verificado:** pelo código. O autoteste do mundo do mesmo dia (372) passa pelo editor de scripts, pelos anexos no corpo e na tela e pelo painel de script (com mensagens simuladas). **Não verificado:** um script rodando num servidor de ilha nesta rodada, e os defeitos apontados em jogo.

### Avisar a ilha, animações próprias e inventário em Vestir (28/11/2026, "corrigir os defeitos")
- **Feito:** depois que a API aceita a troca (aparência, personagem, estúdio do avatar ou anexo), o cliente manda `RefreshAppearance` e a ilha mostra a todos (o servidor já espaçava pedidos seguidos sem perder o último); as animações próprias tocam também sentado, no chão, nos gestos, ao aterrissar e ao levantar, e as que não se repetem tocam uma vez (`SkeletalBody.ActivityOf`); **Anexos do corpo** lê o inventário de 100 em 100 até o fim, tem busca sem depender de acento e numera nomes repetidos.
- **Defeito achado na foto e corrigido:** em 720p os campos de **Vestir um objeto** apareciam uns sobre os outros.
- **Verificado:** EditMode 254/254; autoteste do mundo 374 (no editor, com fotos), com um segundo jogador recebendo a aparência nova e o inventário em duas páginas no servidor de teste; verificação ao vivo contra a API local, 32 passos, com a janela listando todos os itens. Numa rodada o clique simulado do botão direito no chão falhou e, em outra, o editor parou de avançar (causa não confirmada); as repetições passaram.
- **Não verificado:** duas pessoas numa ilha de produção, um arquivo de animação feito pela pessoa tocando em jogo (os testes usam os clipes do próprio jogo no lugar), o jogo gerado, mouse de verdade e inventário com mais de 100 itens na API real.

### Instalador (28/11/2026, "gerar o instalador")
- **Feito:** cliente compilado do commit `a2adc4e` e empacotado em `artifacts/unity-0.7.0-20261128/` (zip, instalador, `SHA256SUMS.txt`, `release.json`), com os passos do script um a um, por causa da política de execução do PowerShell. O pacote de 27/11 não foi alterado.
- **Verificado:** compilação sem erros; 231 arquivos no instalador e no zip; os 230 da lista do jogo conferem por SHA-256 no zip extraído; o jogo extraído passou na autoverificação (`plugin_self_check`, 6) e, com janela, no autoteste do mundo (374). É a primeira vez que o autoteste do mundo roda no jogo gerado desde que o Controle Inteligente de Aplicativos o bloqueou.
- **Não verificado:** instalar (pede administrador), atualizar por cima, entrar numa ilha de produção pela versão instalada e mouse de verdade. Nem o jogo nem o instalador são assinados. A versão segue 0.7.0.

### Construção como no Second Life, Fase 1 (28/11/2026)
- **Feito:** pesquisa do visualizador oficial (tag `Second_Life_Release#e960123-26.4`, 159 arquivos baixados com autorização para uma pasta temporária), auditoria do editor atual, mapa de coordenadas (489 componentes) e matriz de fidelidade (118 itens). Nenhum código do jogo mudou. O gerador do mapa ficou em `unity/tools/sl-build-map/`.
- **Verificado:** cada componente dos XML está no mapa (mesma contagem por janela), todos com descrição; a aba Objeto confere em colunas e ordem com a captura da wiki oficial. **Não verificado:** posições contra o visualizador atual em tela (a captura é da versão 1.23); os testes da API citados como evidência não foram rodados nesta fase; a escala proposta não foi vista em protótipo.

### Construção como no Second Life, Fase 2 (28/11/2026, "sim, começar Fase 2")
- **Feito:** protótipo em UI Toolkit, só no editor (`Assets/LifeVerse/Prototypes/BuildEditor/`), desenhado a partir do mapa de coordenadas; 20 estados em 1920 × 1080, três deles em mais seis resoluções e 20 comparativos, em `docs/design/build-editor/`. A interface do jogo e o `Theme.uss` não mudaram.
- **Achado:** a janela passa sobre o chat e o cartão da ilha; em 1280 × 720 e 1366 × 768 a barra de baixo chegaria sob ela, e a janela termina acima da barra.
- **Verificado:** o relatório do protótipo sem componentes fora da janela, sobrepostos, cortados pela rolagem ou com texto cortado; capturas conferidas por amostragem (não uma a uma na rodada final); EditMode 254/254. **Não verificado:** comparação com o visualizador real (o lado esquerdo dos comparativos é reconstruído pelos XML), interação de qualquer tipo, mouse e teclado, tema de noite e o jogo gerado.

### Construção como no Second Life, Fase 3 (28/11/2026, "aprovo, seguir")
- **Feito:** a janela de construção nova em UI Toolkit (`Assets/LifeVerse/UI/BuildTool/`) no lugar das duas antigas (construção com 12 abas e terreno): cinco modos, abas Geral, Objeto, Recursos, Textura e Conteúdo nas posições do mapa de coordenadas (`BuildLayout.json`, gerado por `unity/tools/sl-build-map/game_layout.py`), altura mostrada como Z (os dados não mudaram), campos numéricos que enviam ao confirmar e recusam o que não vale, valores diferentes entre objetos mostrados como mistos, formas do modo Criar, terreno com os extras do LifeVerse, janelas auxiliares e menu **⋯** com o que era das abas antigas. Abrir a janela recolhe o chat e fechá-la o restaura. `Theme.uss` não mudou. Banco, API e protocolo não mudaram.
- **Mudanças de comportamento:** o modo Foco passa o arrasto à câmera e não escolhe objetos; com "Selecionar face" um clique já escolhe o objeto e a face; `B` no terreno volta a construir e `T` na construção vai ao terreno, em vez de trocar de janela.
- **Defeitos achados e corrigidos antes de entregar:** a janela de mídia quebrava ao abrir (achado pelo autoteste); nas fotos, linhas separadoras das abas Recursos e Textura no lugar errado, preço cortado, "Atualização" sobre "Dinâmico" e o tutorial à vista no terreno e com a janela recolhida.
- **Verificado:** EditMode 262/262 (8 novos: eixos, formas, leiaute, campos, cores); autoteste do mundo 387 no editor, com fotos em 1280 × 720 e 1920 × 1080 (as de 1080p são de antes das últimas correções); fotos da interface sem ilha em 1366 × 768 (14); verificação ao vivo contra a API local, 32 passos. Os botões são acionados pelo código, não por mouse.
- **Não verificado:** mouse e teclado de verdade (arrasto da câmera no Foco, arrasto no seletor de cor, rolagem, duplo clique, dicas), o jogo gerado e o instalador, produção, duas pessoas editando, alto contraste, escolha de item no "Pôr item…" e colocar uma construção do inventário pela janela nova, e comparação com o visualizador real. **Não é equivalência com o Second Life:** o editor 3D (Fase 4) e os recursos avançados (Fase 5) não foram feitos; o que depende deles aparece apagado.
