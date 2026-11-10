# Tela de loading e entrada no mundo

## O que existe (fases 1 e 2)

- `shared/Loading/LoadingManager.cs` e `LoadingState.cs`: núcleo sem Godot, testável. Uma carga é uma lista de etapas (`StepDefinition`) com peso, dependências e estado (`Pending`, `Running`, `Completed`, `Failed`, `Cancelled`). Cada etapa guarda início, fim, erro e, quando dá para medir, `Done`/`Total`. Eventos: `LoadingStarted`, `LoadingStepChanged`, `LoadingProgressChanged`, `LoadingStepCompleted`, `LoadingFailed`, `LoadingCompleted`, `LoadingCancelled`.
- **Progresso real**: a barra é a soma ponderada do que foi concluído e do que foi medido (objetos carregados, partes do ambiente). Uma etapa sem tamanho conhecido não soma nada e a barra mostra um trecho em movimento (indeterminada). O progresso nunca chega a 100% antes de todas as etapas concluírem; a barra suaviza por interpolação e nunca passa do valor real. Não há temporizador aumentando a porcentagem. Os pesos são uma estimativa do trabalho relativo (servidor e conexão pesam mais que a verificação de sessão).
- `client/Scripts/LoadingScreen.cs`: a tela cheia (CanvasLayer 100): logo, título, painel `#1B262E` com barra de título `#1E2A37`, borda `#6D818D`, barra ciano `#12F2E0`, prévia e nome da ilha, uma linha por etapa (ícone de espera, rodando, ok ou erro), status, dicas rotativas, mensagem de boas-vindas e fade. Mostra erro com apenas as ações possíveis. O painel acompanha o tamanho da janela.
- `client/Scripts/WorldEntryCoordinator.cs`: liga as etapas a fatos reais (ver abaixo). Controla o jogador (`PlayerController.ControlsLocked`) enquanto a tela cobre o mundo.
- `NetworkManager`: sinal novo `WorldProgress(key, done, total)` (`snapshot`, `terrain`, `spawn`, `objects`), e `TargetIslandId`/`PreviousIslandId`/`RestorePreviousIsland()`.
- `BuildEditor` informa quantos objetos da ilha já foram criados (contagem real).

## Fluxo da entrada inicial

1. `ViewerUI.EnterFirstIsland` abre o loading (`BeginLocating`) e pergunta ao servidor onde a pessoa começa (`/worlds/me/start`).
2. **Verificando sua sessão**: concluída se há sessão; sem sessão, falha sem “tentar novamente”.
3. **Localizando sua ilha**: concluída quando a ilha foi decidida (início, última ilha ou Boas-vindas). Se o servidor não achar nenhuma, falha com “Tentar novamente”.
4. **Preparando servidor**: enquanto a API pede o token e a ilha sobe (`requesting_ticket`, `waiting_island`); indeterminada.
5. **Conectando à ilha**: ENet conectando e autenticando; conclui em `online`.
6. **Carregando seu avatar**: concluída na primeira posição que o servidor confirma para o avatar.
7. **Carregando ambiente**: 2 partes medidas (snapshot da ilha pela API e terreno enviado pelo servidor).
8. **Sincronizando objetos**: objetos criados / objetos da ilha.
9. **Preparando sua entrada**: um quadro depois de 6 a 8 concluírem. Então “Bem-vindo ao LifeVerse!”, fade de 0,5 s e os controles voltam.

Qualquer entrada em ilha (mapa, busca, lista, ID) passa pelo mesmo `NetworkManager.JoinIsland`, então abre a mesma tela sem mudar cada tela de origem. Quando já há conexão com outra ilha, o título é “Viajando para outra ilha...” e as etapas são as do teletransporte (a transferência segura entre servidores é a fase 3).

## Falhas e recuperação

- Erro de token/ilha, servidor que não sobe, ENet que não conecta, autenticação recusada, protocolo incompatível, conexão perdida e mundo que não termina de chegar em 45 s (`WorldReadyTimeoutSeconds`) falham a etapa correspondente com a mensagem do sistema.
- **Tentar novamente** reabre a mesma carga: o que já concluiu continua, o resto recomeça.
- **Voltar à ilha anterior** só aparece em viagem quando a conexão com a ilha de origem ainda existe.
- **Voltar ao menu principal** fecha a tela e mostra a lista de ilhas.

## Testes

- `LoadingManagerTests` (7): progresso, dependências, falha e retry, cancelamento, definições inválidas, vários threads.
- `LoadingSmoke` (Godot headless): entrada passo a passo, fato que chega cedo espera a vez, barra indeterminada, 100% só no fim, fade, erro com ações possíveis, sessão expirada sem retry, viagem com outro título, timeout do mundo, retry, barra que nunca passa do real.

## Limites conhecidos

- Não há imagem panorâmica de fundo nem imagem de capa da ilha ainda (a prévia usa o logotipo; a capa e o cache de imagens vêm na fase de assets). Também não foi possível conferir a tela visualmente neste ambiente sem janela.
- O teletransporte seguro entre servidores (reserva, token de transferência, confirmação e liberação da origem) é a fase 3; hoje trocar de ilha reutiliza o fluxo de entrada.
- Etapas de download de assets com bytes e cache ficam para a fase de assets.

## Teletransporte entre ilhas: a transferência segura (fase 3, servidor)

Fluxo: `prepare → connect → (ilha de destino confirma a chegada) → release da origem`. Código: `backend/Modules/Worlds/TeleportService.cs` e `TeleportEndpoints.cs`; tabela `world.teleport_transfers` (migração `Phase61TeleportTransfers`); no servidor da ilha, `ServerLocations.cs` e `ServerMain.cs`.

| Passo | Quem | O que acontece |
|---|---|---|
| `POST /api/v1/worlds/teleports/prepare` | cliente | Valida chave de idempotência, ilha existente, acesso (pública, dona, ou permissão `enter`/`build`/`admin`; ilha privada sem acesso → `island_private`), destino diferente da origem, coordenadas dentro da ilha (`invalid_destination`) e capacidade (`island_full`: jogadores que o servidor da ilha informa no heartbeat + reservas de outras viagens). Cria a reserva (90 s). |
| `POST .../{id}/connect` | cliente | Garante o servidor (inicia pelo `WorldManager` se preciso; 503 `server_starting` enquanto sobe) e emite o **ticket**: JWT de 45 s com `transfer_id`, ligado ao usuário, à sessão e à ilha de destino. Um ticket novo substitui um não usado. |
| validação do ticket | servidor da ilha → API | A API aceita o ticket de viagem **uma vez**, só para aquela viagem, usuário, ilha e dentro da validade: atualização atômica no banco (`ConsumeTokenAsync`). Reuso, outro usuário, outra ilha ou outro ticket → 401. A posição de destino pedida vira o ponto de chegada. |
| `POST /platform/servers/{id}/transfers/{id}/arrived` | servidor de destino | Depois de admitir, posicionar e enviar o mundo, confirma a chegada. Só vale com ticket já usado e para a ilha certa; repetir é inofensivo. Se a viagem foi cancelada nesse meio-tempo, recusa e o servidor derruba o jogador (`traveller_arrival_refused`). |
| release | API → servidor de origem | Quando a origem envia as posições (a cada 20 s ou ao alguém sair), a resposta lista quem chegou em outro lugar; o servidor derruba esses jogadores, **exceto** quem voltou depois da chegada, e não os mantém suspensos. Cada liberação é entregue uma vez. |
| `POST .../{id}/cancel` | cliente | Cancela e libera a reserva; repetir é inofensivo; depois da chegada → 409 `already_arrived`. |

Garantias:
- **Presença única / sem duplicar**: o banco só aceita uma viagem ativa por pessoa (índice único parcial `ux_teleport_transfers_one_active`); pedidos concorrentes com chaves diferentes deixam exatamente uma (a outra recebe 409 `transfer_in_progress` com a viagem existente). A mesma chave devolve a mesma viagem.
- **A origem só é solta depois da confirmação do destino.** Se o destino falha, expira (`expired`) ou é cancelado, a pessoa continua onde estava.
- **Reservas expiram** e deixam de contar para a capacidade. Uma chave expirada nunca revive.
- Testes: `TeleportTests` (9 em memória) e `TeleportPostgresTests` (PostgreSQL real: 12 pedidos simultâneos → 1 viagem; 10 usos simultâneos do mesmo ticket → 1 aceito).

Pendências desta fase: restrições de região por parcela (banimento de terreno) ainda não são consultadas no `prepare`; o cliente (fase 4) ainda não usa estes endpoints, então trocar de ilha continua pelo fluxo de entrada comum.

## Teletransporte entre ilhas: o cliente (fase 4)

`client/Scripts/TeleportCoordinator.cs` usa os endpoints da fase 3. Toda troca de ilha com o jogador já numa ilha (mapa, busca, lista, favoritos, convites, histórico e portais chamam `ViewerUI.Join`) vira uma viagem: com a pessoa fora de qualquer ilha continua a entrada comum.

1. **Abre o loading** em modo viagem (“Viajando para outra ilha...”, com **Cancelar**), trava o avatar e mantém a conexão com a ilha atual.
2. **Verificando destino e acesso** = `prepare` (ilha inexistente, privada, sem permissão, lotada e ponto inválido voltam com a mensagem certa; só “lotada” e erros de rede oferecem “Tentar novamente”). Uma viagem antiga esquecida (jogo fechado no meio) é cancelada e a nova segue com outra chave.
3. **Preparando ilha** = `connect`: enquanto o servidor de destino sobe (503) o cliente pergunta de novo a cada 4 s, até 15 vezes.
4. Com o ticket em mãos, **só então** a conexão é trocada (`NetworkManager.JoinWithTicket`): os avatares, veículos e objetos da ilha anterior saem na hora (estado `switching`), o que evita avatares duplicados. Daqui em diante Cancelar some.
5. **Estabelecendo conexão / Transferindo sessão / Carregando ambiente / Sincronizando objetos / Posicionando avatar** seguem os fatos reais, como na entrada.
6. **Finalizando teletransporte**: depois de tudo carregado o cliente consulta `GET /teleports/{id}` até o **servidor de destino** confirmar a chegada (`arrived`); sem confirmação em 12 s (ou se a viagem foi cancelada/expirou) a viagem falha. Só então vem “Você chegou à ilha X”, o fade e os controles.

Falhas e volta:
- Antes da troca: **Cancelar** libera a reserva e a pessoa continua na ilha em que estava, com tudo intacto.
- Depois da troca (conexão recusada, autenticação, perda de conexão, chegada não confirmada): a viagem é cancelada na API; **Tentar novamente** prepara uma viagem nova; **Voltar à ilha anterior** entra de novo na ilha de origem (ou apenas restaura se a conexão ainda existia); **Voltar ao menu principal** mostra a lista de ilhas.
- Um segundo pedido de viagem enquanto uma está em andamento é recusado (“Você já está viajando para outra ilha.”); a perda de conexão durante a viagem não dispara a reconexão automática à ilha de destino.

Testes: `TeleportSmoke` (Godot headless, com a API e a conexão simuladas): viagem completa com servidor subindo, chegada só depois da confirmação, cancelamento sem conectar, ilha privada e lotada, viagem esquecida, falha depois da troca com nova tentativa e destino que não confirma.

Limite: o Godot mantém uma conexão ENet por vez, então a ilha de origem só é mantida enquanto se prepara e se pede o ticket (as etapas que mais falham); no momento de conectar ao destino a conexão de origem é fechada. Se a conexão com o destino falhar, a volta para a origem é uma nova entrada, e o servidor de origem ainda guarda o avatar por cerca de 10 s.

## Recursos da ilha, cache e capa (fase 5)

- **Lista do que a ilha precisa** (`GET /api/v1/worlds/islands/{id}/assets/manifest?x=&z=`, `AssetManifest.cs`): cada modelo 3D dos objetos “mesh” e cada imagem de material (inclui projetor) com hash SHA-256, tamanho, tipo e a **distância** do uso mais próximo ao ponto de chegada (por padrão o spawn). Mesmas regras de acesso do arquivo (ilha privada nega a estranhos). O endpoint do arquivo agora devolve o hash como `ETag`.
- **Plano** (`shared/Loading/AssetPlan.cs`): os mais próximos, até 80 m e 24 MB, são **essenciais** (carregam antes de entrar); o resto é **adiado** e continua em segundo plano (2 downloads), mais perto primeiro. Um asset nunca aparece duas vezes.
- **`AssetLoadingService`** (cliente): começa quando a conexão fica `online`. Guarda tudo em `user://assets/{hash}.bin`, escrevendo em arquivo temporário e renomeando. Um arquivo já no disco só é reutilizado se o hash ainda confere; se estiver danificado é apagado e baixado de novo. Um download que chega com hash diferente é recusado e tentado mais uma vez; se falhar de novo o carregamento falha com a mensagem “Não foi possível baixar uma imagem/um modelo 3D da ilha”. Download interrompido é tentado de novo. Limite de 512 MB (os arquivos mais antigos saem primeiro).
- **Progresso real**: a etapa “Carregando recursos da ilha” mede **bytes** (arquivos do cache contam como feitos; downloads em andamento contam os bytes já recebidos), 3 downloads em paralelo, e entrega um arquivo por quadro aos caches de textura e modelo, sem travar. Sem nada essencial perto da chegada, a etapa termina na hora. Um servidor antigo sem lista (404) não bloqueia: os recursos carregam quando um objeto precisa, como antes.
- `TextureCache` e `ModelCache` agora consultam o disco antes de baixar e guardam o que baixam quando é um asset anunciado.
- **Capa da ilha**: o loading mostra a imagem de capa (`coverImageUrl`) na prévia e como fundo escurecido, com cache em disco por 7 dias; sem capa (ou se não carregar) fica o logotipo. A prévia também mostra o estado atual (“Estado: …”).
- Testes: `AssetPlanTests` e `AssetManifestTests` (backend) e `AssetSmoke` (Godot): fila por distância, bytes, cache de disco, arquivo danificado, download interrompido, hash errado, servidor sem lista e com lista quebrada.

Limites: o cache é por hash do conteúdo (versão nova de um asset = hash novo = arquivo novo); avatares, animações e roupas ainda carregam pelo caminho antigo, sem esta etapa; a imagem panorâmica de fundo é a capa da ilha (não há uma imagem padrão de ilha além do logotipo).

## Robustez e testes (fase 6)

- **Tempo limite de conexão**: se o servidor da ilha não responde em 20 s depois de “conectando”, a etapa falha com “O servidor da ilha não respondeu dentro do tempo esperado.” (ENet sozinho pode demorar bem mais). Servidor de destino que não sobe: 15 perguntas de 4 s; mundo que não termina de chegar: 45 s; chegada não confirmada: 12 s.
- A regra “quem a ilha de origem solta” está em `shared/Loading/TravellerRelease.cs` (testada): só quem entrou antes da chegada; quem voltou depois é uma nova visita.

### Os 20 cenários e como cada um foi verificado

Legenda: **A** = teste automático (xUnit / smoke Godot headless, com a rede simulada), **PG** = teste automático em PostgreSQL real, **—** = não verificável neste ambiente (precisa de servidores de ilha e uma janela de jogo reais).

| # | Cenário | Verificação |
|---|---|---|
| 1 | Login e entrada em ilha ativa | A `LoadingSmoke` (etapas, 100% só no fim, fade) — sem servidor real |
| 2 | Entrada em ilha desligada | A `LoadingSmoke` (servidor subindo = barra indeterminada); no backend o 503 `starting` já existia |
| 3 | Teleporte entre ilhas no mesmo servidor | A `TeleportTests` / `TeleportSmoke` (o caminho é o mesmo da transferência) |
| 4 | Teleporte entre servidores diferentes | A+PG `TeleportTests`, `TeleportPostgresTests` (reserva, ticket de uso único, chegada, liberação da origem) — dois servidores reais: **—** |
| 5 | Ilha privada | A `TeleportTests`, `TeleportSmoke` |
| 6 | Ilha lotada | A `TeleportTests` (contagem de jogadores + reservas), `TeleportSmoke` |
| 7 | Falha durante a inicialização | A `ScenarioSmoke` (servidor que não sobe falha depois das tentativas, com “Voltar”) |
| 8 | Falha de conexão ENet | A `LoadingSmoke`, `TeleportSmoke`, `ScenarioSmoke` (estado `failed`, tempo limite); ENet real: **—** |
| 9 | Download de assets interrompido | A `AssetSmoke` (tenta de novo) |
| 10 | Reutilização de cache | A `AssetSmoke` (segunda visita sem download) |
| 11 | Desconexão durante a transferência | A `ScenarioSmoke` (cancela a viagem, oferece voltar e tentar de novo) |
| 12 | Teleporte duplicado | A `TeleportSmoke`, `TeleportTests`; PG `TeleportPostgresTests` (12 pedidos simultâneos → 1 viagem) |
| 13 | Cancelamento antes da confirmação | A `TeleportSmoke`, `TeleportTests` |
| 14 | Recuperação da ilha anterior | A `ScenarioSmoke` (voltar antes da troca deixa tudo como estava); voltar depois da troca é uma nova entrada: **—** com servidor real |
| 15 | Entrada após reconexão | A `ScenarioSmoke` |
| 16 | Rede lenta | A `ScenarioSmoke` (servidor que demora 10 perguntas), `AssetSmoke` (progresso por bytes) |
| 17 | Mudança de resolução durante o loading | A `ScenarioSmoke` (largura do painel); janela real: **—** |
| 18 | Fechamento inesperado do cliente | A `TeleportTests` (reserva expira), `TeleportSmoke` (viagem esquecida é cancelada na próxima) |
| 19 | Reinício do servidor durante a transferência | PG `TeleportPostgresTests` (novo ticket substitui o antigo; cancelado/expirado não abre); reinício real: **—** |
| 20 | Avatar não duplicado | A `ScenarioSmoke` (avatares da ilha antiga somem na troca), `TeleportTests` (liberação da origem), PG (uma viagem ativa por pessoa) |

### Pendências reais

- **Build exportada para Windows**: o modelo de exportação do Godot Mono (`windows_release_x86_64.exe`) não está instalado nesta máquina, então a exportação não foi gerada nem testada. Os arquivos novos são C# e cenas de teste comuns, sem dependência de plataforma.
- **Ensaio com dois servidores de ilha reais e a janela do jogo** (aparência da tela, fade, ENet de verdade, perda de rede de verdade) não foi feito aqui.
- Banimento por terreno ainda não é consultado ao preparar a viagem.
- Avatares, animações e roupas ainda não entram na etapa de recursos.
- Imagem panorâmica padrão (sem capa) não existe; fica o logotipo.
