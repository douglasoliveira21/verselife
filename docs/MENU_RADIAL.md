# Menu radial contextual (botão direito)

Plano em etapas para o menu radial do VerseLife. O menu **já existia** (`RadialMenu`, `ContextInteractionManager`, `InteractionRegistry`, `shared/Gameplay/RadialMath`); ele é evoluído, não substituído.

## Etapa 1 — visual do mockup (feita)
Arquivos: `client/Scripts/RadialMenu.cs` (reescrito), `shared/Gameplay/RadialMath.cs`, `client/Scripts/Settings/SettingsCatalog.Stage4.cs` e `SettingsAppliers.Stage4.cs` (opções), testes `client/Tests/RadialSmoke.cs`, `RadialCapture`, `tests/.../RadialMathTests.cs`.

- **Geometria:** anel com 300 px de diâmetro externo e 110 px no disco central (a 100% em 1920×1080), setores anulares reais (o hover e o clique usam o ângulo e os dois raios, não retângulos). Encolhe sozinho para caber em telas baixas (`RadialStyle.Fit`); o tamanho nunca passa de 80% da altura da tela.
- **Setores:** até 8 normais; 9–12 com texto compacto; mais de 12 viram um círculo com «Mais» (nada se perde). Teclas 1–9, setas, Enter e Backspace.
- **Paleta:** a do mockup (`RadialStyle`): fundo `#1B262E`, secundário `#202D36`, borda `#6D818D`, destaque `#12F2E0`, laranja `#F4A340`, perigo `#EF5350`, sucesso `#34C759`.
- **Estados:** habilitado, destacado (preenchimento + brilho ciano que acende em ~100 ms), desabilitado (cinza, dica explicando), perigoso (ícone vermelho; com `Confirm` abre um círculo «Confirmar / Cancelar»), seta laranja nos setores que abrem algo.
- **Centro:** ícone e nome do alvo, ou uma imagem circular quando se passa `image` a `Open` (miniaturas de avatar e objeto dependem de a Etapa 4/3 fornecê-las; hoje usa o ícone). Dentro de um submenu vira «Voltar».
- **Painel lateral:** um item com `SidePanel = true` mostra seus filhos como lista ao lado do setor (abre do lado com espaço, fica na tela, não fecha ao mover o cursor; Esc fecha o painel primeiro, depois o menu). Os demais submenus continuam substituindo o círculo.
- **Animações:** abre com fade e leve expansão em ~150 ms; respeita «Reduzir movimento» (Acessibilidade).
- **Configurações › Interface e HUD › Menu radial:** tamanho (60–150%), transparência, brilho, velocidade das animações (0,5–2×) e dicas. São do aparelho (não sincronizam com a conta).

### Verificado
`RadialSmoke` (geometria, 12 setores, painel lateral, borda da tela, confirmação, tamanho), `RadialMathTests`, `SettingsMoreSmoke`, `SettingsWindowSmoke` e capturas em 1920×1080 e 1280×720 (`RadialCapture`).

### Ainda não verificado
3840×2160 e 1366×768 em captura; uso durante o editor `Build`; teclado dentro do painel lateral com o jogo real.

## Etapas seguintes (plano)
2. ~~Resolvedor de permissões e novos alvos~~ (feito, veja abaixo).
3. ~~Objetos e terrenos~~ (feito, veja abaixo).
4. ~~Avatares~~ (feito, veja abaixo).
5. ~~Moderação e teleporte~~ (feito em parte, veja abaixo); consentimento entre avatares, congelar e Lua continuam pendentes.

## Etapa 2 — permissões e novos alvos (feita)
Arquivos novos: `shared/Interaction/ContextPermissions.cs`, `tests/.../ContextPermissionsTests.cs` (15 casos), `client/Tests/RadialWiringSmoke.cs`. Alterados: `ContextInteraction.cs` (alvos, serviços e registro), `InventoryPanel.cs`, `ScriptHudLayer.cs`, `ViewerUI.Bars.cs`, `ViewerUI.cs`, `Build/BuildEditorController.cs`, `Build/BuildHost.cs`.

- **`ContextPermissions` (compartilhado):** uma só função diz o que cada pessoa pode fazer num objeto (`ForObject`) ou item (`ForItem`), com o motivo escrito quando não pode. Espelha o que o servidor já impõe (dono, direitos Copiar/Modificar/Transferir, proteção contra edição); o servidor continua decidindo de novo ao executar.
- **Fatias cinzas:** o que não é permitido **fica no lugar, cinza, com o motivo como dica** (não some por surpresa). Não existe mais o setor «Fechar» (Esc, clique fora ou o centro fecham).
- **Objeto:** o setor principal segue a ação de clique (Tocar, Comprar, Pagar ou Abrir), mais Inspecionar, Editar (Mover, Girar, Esticar, Textura, Duplicar, Excluir), **Pegar** e **Pegar cópia** (guardam no inventário pelo mesmo caminho do editor, com `Retirar` ou `Guardar`) e **Mais opções** em painel lateral (copiar identificador, posição e tamanho; selecionar; excluir com pergunta).
- **Item do inventário** (botão direito no cartão, no lugar do menu em lista): Pré-visualizar, Colocar no mundo, Vestir, Copiar, Enviar, Favoritar e Mais opções (direitos, versões, lixeira). Na lixeira: Restaurar, Excluir de vez (com pergunta), Direitos, Pré-visualizar. Cada setor roda a ação que a janela já tinha.
- **Pasta do inventário** (botão direito na árvore): Nova subpasta, Renomear, Excluir (com pergunta).
- **Painel de script (HUD):** botão direito abre Fechar painel (avisa a ilha, como o X) e Sobre o painel.
- **Avatar e você:** «Interagir», «Animações» e «Mais opções» passam a usar o painel lateral; bloquear pede confirmação.
- O menu em lista do inventário continua como reserva quando ninguém ouve o pedido (paridade preservada).

### Verificado
`ContextPermissionsTests`, `RadialSmoke` (permissões, cinzas, confirmação, itens, lixeira, pasta, HUD), `RadialWiringSmoke` (no jogo de verdade: cartão, pasta e painel de script abrem o menu e as ações rodam), `ScriptHudSmoke`, `InterfaceResolutionSmoke`, `SettingsMoreSmoke`, captura do menu sobre o inventário.

### Não feito (e por quê)
- **Alvos Anexo e Administração:** o jogo não permite apontar para um anexo (só para o avatar inteiro) e não há perfil de administrador no cliente nem endpoint de "devolver objeto"; o resolvedor já tem `Return` e `IsAdmin`/`IsParcelOwner`, mas o menu só os mostrará quando esses dados e endpoints existirem. `IsParcelOwner` hoje é sempre falso.
- **Miniatura no centro:** continua o ícone; falta fornecer a imagem do avatar/objeto.
- **Recortar, Colar, Mover e Organizar pasta:** não existem no inventário hoje, então não aparecem no menu.

## Etapa 3 — objetos e terrenos (feita)
Alterados: `shared/Interaction/ContextPermissions.cs` (`ParcelFacts`, `ForParcel`), `client/Scripts/ContextInteraction.cs`, `ViewerUI.Bars.cs`, `RadialMenu.cs`; testes `ContextParcelPermissionsTests`, `RadialSmoke`, `RadialWiringSmoke`.

- **Terreno e parcela:** o menu do chão olha a parcela sob o cursor (dados do `LandManager`: dono, estado, modo de construção, permissão de terreno vinda do servidor) e monta oito setores: **Ir até aqui**, **Editar terreno** (abre as ferramentas de terreno), **Comprar** (ou **Alugar** quando está para alugar; abre a janela «Sobre o terreno», onde a compra acontece), **Gerenciar** (janela de gerenciamento de terrenos), **Sobre a parcela**, **Construir**, **Criar objeto** e **Mais opções** em painel lateral (selecionar terreno, ver proprietário, copiar local, alugar terreno).
- **Regras (`ForParcel`):** comprar só se está à venda e não é seu; alugar só se está para alugar; gerenciar só para dono ou inquilino; editar terreno segue a permissão que o servidor manda (`canEditTerrain`); construir segue o modo da parcela (dono, lista, todos, ninguém; em «lista» o servidor confere a lista). Antes de os dados chegarem, só «Sobre a parcela» funciona e os outros dizem por quê. Terreno sem dono (`free`) só oferece «Sobre a parcela», «Ir até aqui» e «Selecionar terreno».
- **Objeto:** o setor principal já segue a ação de clique (inclui **Sentar** para objetos com assento). **Inspecionar** agora mostra tipo, tamanho, se é seu, direitos (C·M·T·R), ação do clique, proteção e se é temporário. «Editar» abre o editor `Build` com o objeto selecionado, que é a janela de propriedades do jogo.
- O ícone do centro do terreno virou uma montanha.

### Verificado
`ContextParcelPermissionsTests`, `RadialSmoke` (todos os estados do chão), `RadialWiringSmoke` (com as parcelas de demonstração reais: a minha, a à venda e a sem dono), `LandSmoke`, `ParcelInfoSmoke`, `InterfaceResolutionSmoke` e captura do menu sobre a sobreposição de terrenos.

### Não feito (e por quê)
- **Sentar aqui no chão** e **Teleportar para cá / criar ponto de referência / ver limites:** não existem no jogo; «Selecionar terreno» já mostra os limites.
- **Janela de propriedades só de leitura** (nome do criador/proprietário, impacto na ilha): o estado do objeto só traz ids e o custo; falta o servidor devolver nomes. Por enquanto: Inspecionar (texto) e Editar (abre o editor).
- **Comprar/Pagar objeto como janela própria:** continua pelo clique do objeto (o script ou a ação de venda do objeto cuida do pagamento); a janela de pagamento entre pessoas é da Etapa 4.
- **Editar terreno** depende de `canEditTerrain` vir do servidor; nas parcelas de demonstração não vem, então aparece cinza.

## Etapa 4 — avatares (feita)
Alterados: `shared/Interaction/ContextPermissions.cs` (`PersonFacts`, `ForPerson`), `ContextInteraction.cs`, `PersonActions.cs`, `RadialMenu.cs`, `Autoloads/NetworkManager.cs`, `VoiceChat.cs`, `ViewerUI.Bars.cs`; testes `ContextPersonPermissionsTests`, `RadialSmoke`, `RadialWiringSmoke`, `VoiceSmoke`.

O menu de outro jogador tem os oito setores do desenho:
- **Perfil** (janela de perfil existente, que também mostra os grupos públicos) e **Mensagem** (conversa privada pela API social).
- **Amizade** (painel lateral), que **muda com a relação** que o servidor informa: Adicionar amigo (sem relação), Aceitar pedido (pedido recebido), Remover amizade (amigo; pergunta antes), ou cinza com o motivo se o pedido já foi enviado. Mais «Ver na lista» (janela Pessoas). A relação é buscada em segundo plano ao abrir o menu e o painel se atualiza sem fechar (`ReplaceRoot`).
- **Pagar:** janela nova com o saldo (L$), valor e confirmação; usa `POST /economy/transfer`. Cada janela tem **uma chave de idempotência**: apertar Pagar duas vezes envia um pagamento só; valor maior que o saldo é recusado na tela; o servidor recusa saldo insuficiente ou carteira bloqueada; o saldo da barra superior é pedido de novo após pagar.
- **Interagir** (gestos reais: acenar, dançar, mais gestos), **Grupos** (convidar para um grupo seu; ver grupos públicos = perfil) e **Moderação**: Bloquear (pergunta antes; vale na conta), **Silenciar voz** e **Ocultar avatar**, que são escolhas **locais**: valem só nesta tela até sair da ilha (a voz dessa pessoa é descartada ao chegar; o avatar fica invisível) e viram «Reativar voz» / «Mostrar avatar»; e Denunciar (moderação). **Mais opções:** ir até a pessoa, copiar nome, copiar identificador da conta, buscar na lista.
- Sem login: não dá para pagar, escrever, adicionar, bloquear ou denunciar (cinza, com o motivo), mas ocultar e silenciar continuam disponíveis.
- O painel lateral agora abre **ao lado do anel, do lado do setor** que o abriu (antes cobria setores).

### Verificado
`ContextPersonPermissionsTests`, `RadialSmoke` (relação, pagar, moderação, reposição do painel, API de mentira com remover/aceitar/pagar/copiar id), `RadialWiringSmoke` (a janela de pagamento no jogo: saldo, valor acima do saldo recusado, duplo clique = um pagamento), `VoiceSmoke` (voz silenciada é descartada), `InterfaceResolutionSmoke`, `SettingsMoreSmoke` e captura do menu com o painel de Moderação.

### Não feito (e por quê)
- **Teleporte (oferecer, pedir, ir até o amigo, localização compartilhada), Expulsar e Banir da parcela/região, Congelar avatar:** o servidor da ilha e a API não têm essas operações; precisam ser construídas antes (próxima etapa).
- **Abraçar, Apertar mão, Dançar junto, Sentar junto, Oferecer animação:** exigem consentimento do outro jogador e animações sincronizadas entre dois avatares; não existe protocolo para isso hoje.
- **Pedir entrada em grupo e ver o perfil de um grupo a partir do avatar:** a lista de grupos públicos da pessoa só aparece no perfil; falta um endpoint que devolva os grupos públicos dela com ids.
- **Seguir / Olhar para o avatar:** não há seguimento contínuo nem controle de câmera por alvo.
- **Permissões da amizade** (ver, editar, conceder): não existem no modelo de amizade.
- **Enviar item a partir do avatar:** o envio do inventário ainda pede o nome na janela de ofertas; ligar o avatar a essa janela fica para depois.
- **Silenciar/ocultar** não persistem entre sessões (o número do jogador muda a cada entrada).

## Etapa 5 — moderação e teleporte (feita em parte)
Esta etapa ligou o menu ao que o servidor já fazia (moderação de parcela e de ilha) e **construiu** o que faltava para o teleporte entre jogadores. O resto (consentimento entre dois avatares, congelar, ações de contexto em Lua) continua pendente, com o motivo abaixo.

### Moderação (usa as APIs que já existiam)
Alterados: `shared/Interaction/ContextPermissions.cs` (`PersonEjectParcel`, `PersonBanParcel`, `PersonKickRegion`), `ContextInteraction.cs`, `PersonActions.cs`, `ViewerUI.Bars.cs`.
- Dentro de **Moderação** aparecem, **só para quem pode usar pelo menos uma**: **Expulsar do terreno** (`POST …/land/parcels/{id}/eject`: banimento de 1 minuto; a ilha empurra a pessoa para fora), **Banir do terreno** (`POST …/land/parcels/{id}/access`, kind `ban`, por 1 hora, 1 dia, 7 dias ou para sempre; cada duração pede confirmação) e **Expulsar da ilha** (`POST …/management/server/kick`; a pessoa pode voltar).
- Quem pode: dono ou inquilino do terreno onde a pessoa está, ou administrador/proprietário da ilha; expulsar da ilha só administrador. O jogo descobre se você administra a ilha perguntando uma vez ao servidor (403 = não) e o menu aberto se atualiza; **o servidor decide de novo em cada chamada** e recusa o que não é permitido (dono e administradores não podem ser expulsos).
- Mensagens claras para os erros (sem permissão, pessoa já saiu, ilha desligada, etc.).

### Teleporte entre jogadores (novo)
Servidor: `backend/Modules/Social/TeleportInvites.cs`, entidade `TeleportInvite`, migração **`Phase91TeleportInvites` (não aplicada a nenhum banco)**, `tests/.../TeleportInviteTests.cs` (13 casos).
- `POST /api/v1/social/teleports` (convidar), `GET /pending`, `POST /{id}/accept`, `POST /{id}/decline`. **Oferecer** leva o convidado até onde você está; **Pedir** leva você até onde o outro está. Ninguém se move até a outra pessoa aceitar.
- O ponto de chegada **vem do relatório da própria ilha no momento do aceite** (nunca do jogo). Os dois precisam estar na ilha agora. Convites valem 2 minutos, só o destinatário responde (e uma vez), um pendente por par e tipo, no máximo 10 por minuto por pessoa, e quem bloqueou ou foi bloqueado não consegue convidar.
- Cliente: o aviso chega pelo WebSocket de notificações (`teleport_invite`), abre uma janela «Aceitar / Recusar», e a resposta (`teleport_answer`) move quem deve se mover pelo teleporte para um ponto que a ilha já tinha (`RequestTeleport`), 1,5 m ao lado da outra pessoa. A ilha ainda confere portas e banimentos de terreno na chegada.
- No menu: setor **Teleporte** (painel lateral com Oferecer e Pedir teleporte). Agora o menu de outro jogador tem nove setores, com texto compacto.

### Verificado
`TeleportInviteTests`, `ContextModerationPermissionsTests`, `RadialSmoke` (moderação só para quem pode, durações, API de mentira com sondagem de administrador, erros), `RadialWiringSmoke` (o convite chega, a janela abre uma vez, aceitar move quem deve, recusa só avisa), `InterfaceResolutionSmoke`, `SettingsMoreSmoke`, `VoiceSmoke`, `AccountSyncSmoke`, suíte do servidor (2165 passam; falha só `HighJumpTests … hemisphere`, que já falhava) e captura do menu de nove setores.

### Não feito (e por quê)
- **Teleporte para amigo em outra ilha e «ver localização compartilhada»:** não existe um índice de quem está em qual ilha (só cada ilha conhece seus jogadores); fica para um serviço de presença.
- **Banir da região:** a ilha só tem banimento por terreno; um banimento da ilha inteira exigiria novo modelo de dados e regra no servidor da ilha.
- **Congelar avatar:** o servidor da ilha não tem esse estado.
- **Abraçar, apertar mão, dançar junto, sentar junto, oferecer animação:** precisam de um protocolo de consentimento e animação sincronizada entre dois avatares na ilha (cliente, servidor da ilha e API); é uma etapa própria.
- **Ações de contexto registradas por scripts Lua (`RegisterContextAction`):** o menu não pergunta ao `ScriptHost`; falta uma API Lua nova, um comando no protocolo da ilha e o retorno de chamada autorizado; é uma etapa própria.
- **Convites perdidos:** quem estava desconectado quando o convite chegou não o vê; o endpoint `GET /pending` existe, mas o jogo ainda não o consulta ao entrar.
- **Migrações:** Phase88, Phase89, Phase90 e Phase91 continuam **sem ser aplicadas** a nenhum banco.
