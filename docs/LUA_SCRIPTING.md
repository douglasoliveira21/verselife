# Scripting Lua — arquitetura, integrações, testes e pendências

Complementa `docs/SCRIPTING.md` (primeira camada da API) e `docs/LUA_DIAGNOSTICO.md` (o que já existia). A **referência das funções e eventos** é `docs/LUA_API.md`, gerada do código e conferida por teste.

## Arquitetura final do runtime

| Componente pedido | Onde está | Observação |
|---|---|---|
| LuaRuntimeManager / LuaScriptHost | `scripting/ScriptHost*.cs` (`ScriptHost`) | Um host por ilha; instâncias isoladas; várias por objeto (até 8) |
| LuaScriptInstance | `ScriptHost.Instance` | Estado, timers, logs, métricas, ciclo de vida |
| LuaSandbox | `scripting/LuaSandbox.cs` | MoonSharp; biblioteca mínima; orçamento de instruções/tempo/alocação por evento; funções ilimitadas limitadas |
| LuaEventDispatcher / LuaEventQueue | `ScriptHost.Runtime.cs` (`Enqueue`, `Process`) | Fila única (400), ordem de chegada, limite por script (20/s) e por dono (CPU por janela de 10 s) |
| LuaScheduler | `ScriptHost.Tick` | Os timers rodam no tick do servidor; nenhuma thread por timer; `timer.after/every` com função, pausa e retomada |
| LuaPermissionsManager | `ScriptPermissions` | Concessões por script × avatar × permissão; revogação vale na próxima chamada; troca de dono revoga as do dono |
| LuaStateManager / LuaScriptStorage | `storage.*` + `world.script_state` | Números, textos, booleanos e tabelas pequenas; sobrevive a reinício |
| LuaApiRegistry | `ScriptCatalog` | Fonte única: manual, autocompletar do editor, avisos do validador |
| LuaExecutionMonitor | `ScriptMetrics` + `world.script_runtime` | CPU, instruções, memória, eventos, descartes, erros, suspensões |
| LuaDebugger | console por script (`debug.*`, `print`, erros com linha e pilha) | Breakpoints/passo a passo: **não implementados** (ver pendências) |
| LuaScriptCompiler / Diagnostics | `ScriptCompiler` | Sintaxe com linha, chamadas que o sandbox não tem (erro), eventos e funções com erro de digitação (aviso, com sugestão) |
| LuaScriptNetworkBridge | `island-server/Scripts/IslandScripting*.cs` + RPC `ScriptDialogResponse` | Liga o host à ilha: objetos, toque, proximidade, chat, diálogos, relatório à API |

**Por que MoonSharp:** 100 % C# (sem biblioteca nativa: roda no servidor Linux headless e no Godot Mono), preempção por contagem de instruções (coroutine com `AutoYieldCounter`) e controle total da ponte C#/Lua (nada de reflexão .NET exposta aos scripts). Custo: é interpretado (cerca de 0,1 ms por evento simples neste teste).

**Duas otimizações de memória (medidas):** o MoonSharp reserva ~2 MB por estado Lua e por coroutine. Cada script agora usa **uma** coroutine permanente (um despachante interno atende todos os eventos) e as pilhas foram reduzidas por reflexão (`MoonSharpStacks`): de **2,3 MB para 0,4 MB por script** e de 2 MB para 0,9 KB de lixo por evento. Recursão passa a falhar com erro limpo ("too many nested calls") a cerca de 2 mil chamadas. Um teste falha se o MoonSharp for trocado por uma versão em que isso deixe de valer.

### Ciclo de vida
`Created → Loaded → Validated → Waiting ⇄ Running`, `Paused`, `Suspended` (limite estourado 3× em 60 s), `Faulted` (não carrega), `Stopped`, `Destroyed`. Só as transições de `ScriptLifecycleRules` são permitidas. Ao reiniciar, `on_reset` e `on_start` rodam e o que o script guardou com `storage` permanece. Remover o script (ou o objeto) dispara `on_destroy` e encerra tudo; a ilha desligando dispara `on_region_shutdown`.

### Fluxo autoritativo
Evento no servidor → o script roda → a API valida argumentos, taxas e permissões → a mudança do objeto entra na fila e é aplicada **pela mesma rota de construção dos jogadores** (permissões do dono, revisão, replicação) → o estado autoritativo é transmitido aos clientes. O cliente nunca decide o resultado de um script. Scripts rodam sem o dono online.

## Integrações realizadas
- **Objetos/linksets:** API camelCase, `moveTo`/`rotateTo` por passos, peças do conjunto (`getLink`, `setLinkPosition`, mensagens entre peças; só peças do mesmo dono).
- **Chat:** `say` (30 m), `whisper` (10 m), `shout` (100 m), canais públicos (até 100 m) e privados (negativos, só do mesmo dono), mensagem privada a avatar (permissão `SEND_MESSAGES` ou o avatar ter tocado no objeto há ≤ 5 min).
- **Diálogos e permissões:** `dialog.show` e `permissions.request` abrem uma janela nativa no cliente (`ScriptDialogWindow`); a resposta volta pelo RPC `ScriptDialogResponse` (protocolo **v10**) e a ilha só aceita de quem foi perguntado, uma vez, dentro do prazo.
- **Regiões:** `region.getName/getId/getAvatarCount`.
- **Persistência/observabilidade:** a ilha envia a cada 30 s estado, contadores, linhas do console e permissões para `PUT /api/v1/platform/servers/{id}/script-runtime`; o editor lê por `GET .../scripts/{id}/runtime`. Painel de consumo da ilha: `GET /api/v1/worlds/{islandId}/script-usage` (scripts por CPU e por dono).
- **Controle pelo dono:** pausar/continuar (`POST .../pause|resume`), parar, reiniciar; a ilha aplica em até 15 s.
- **Editor:** abas Console / Problemas / Desempenho / Permissões, estado em tempo real, autocompletar a partir da API real, Validar com linha clicável, janela de Documentação com busca e "inserir exemplo".
- **Validação ao salvar:** o mesmo `ScriptCompiler` (o filtro antigo recusava, por texto, palavras como `audio.` e `debug`).
- **Orçamentos:** 8 scripts por objeto, 200 por dono; CPU por dono (600 ms a cada 10 s); fila de eventos; taxas de chat, mensagens, diálogos e mudanças.
- **Permissões do dono:** na aba Permissões do editor, caixas para `CONTROL_PHYSICS`, `HTTP_REQUESTS`, `CONTROL_MEDIA` e `CREATE_NPCS` (`PUT .../scripts/{id}/permissions/{permissão}`); a ilha as lê a cada 15 s. Só o editor cria permissões do dono: a ilha não consegue conceder nem apagar uma por relatório.
- **HTTP (`http.request` / `on_http_response`):** quem faz a requisição é a API, não o script nem a ilha (`POST /api/v1/platform/servers/{id}/script-http`, chave de serviço). Só https nas portas 443 e 8443, sem usuário na URL, nunca para endereços privados: o IP realmente conectado é conferido na hora da conexão (cobre nome que aponta para dentro e DNS rebinding, inclusive resposta mista), redirecionamentos são seguidos à mão (até 3) e conferidos a cada passo, cabeçalhos de uma lista curta, corpo ≤ 8 KB, resposta ≤ 32 KB só json/texto, 8 s, 3 simultâneas e 10 por minuto por script (120 por ilha). Exige `HTTP_REQUESTS` do dono (conferido no banco). A linha do console traz o endereço sem a query: sem segredos.
- **Física (`physics.*`):** exige `CONTROL_PHYSICS` do dono. `setEnabled/setPhantom/setDynamic/setMass` mudam as configurações pela mesma rota de construção (e pelas mesmas regras de `PhysicsRules`: um fantasma não vira corpo físico); `applyImpulse/applyForce/setVelocity/setAngularVelocity/setGravityScale/getVelocity` agem sobre o corpo simulado pela ilha (Godot), com limites (50 m/s, 20 rad/s, 10 empurrões por segundo) e devolvem `false` se o objeto não é simulado.
- **Economia (`economy.requestPayment`, `on_payment`, `on_payment_failed`):** o script pede, o jogador vê uma janela (quem pede, quanto e que o valor vai para o dono do objeto) e precisa apertar **Pagar**. A ilha pede à API (`POST .../script-payment`, chave de serviço), que move o dinheiro numa transação de banco com chave de idempotência: do pagador para o **dono atual do objeto** (lido do banco; o script não escolhe o destino e nunca mexe em saldo), sem criar moeda, com saldo e carteira bloqueada checados, de L$ 1 a L$ 100.000, e uma linha no console do script. O script só ouve `on_payment` para dinheiro que realmente mudou de mãos; recusa, saldo insuficiente ou falha chegam em `on_payment_failed` com o motivo. Pedido repetido ou resposta de outro avatar não cobra nada.
- **Inventário do objeto (somente leitura):** `inventory.list/getItem/hasItem/getItemType/count` sobre o Conteúdo do objeto, e os eventos `on_inventory_changed`, `on_item_added` e `on_item_removed` quando o Conteúdo muda.
- **HUD por script (`hud.*`, `on_hud_click`, `on_hud_input`, `on_hud_closed`):** o script pede `SHOW_HUD` ao avatar (`permissions.request`) e então monta um painel na tela **desse avatar**: título, textos coloridos, botões, barras de progresso e campos de texto (até 12 elementos, textos de 120 caracteres, 20 painéis por script, envio no máximo 4 vezes por segundo, posição de 0 a 1 e largura de 160 a 420 px). O cliente desenha (`ScriptHudLayer`, até 8 painéis ao mesmo tempo) e o jogador sempre pode fechar no X. Cliques, textos e fechamento voltam pela mesma mensagem dos diálogos (sem novo RPC) e a ilha só aceita para um HUD que o script realmente mostra àquele avatar, para elemento do tipo certo, até 10 por segundo. O painel sai da tela se o avatar retira a permissão, se o script é parado, suspenso, pausado, removido ou editado, e volta a ser enviado quando o avatar reconecta. Um script não desenha fora do próprio painel nem lê o que o jogador digita em outro lugar.
- **Animações de avatar (`avatar.playAnimation/stopAnimation`, `on_animation_finished`):** exige `CONTROL_ANIMATION` **do próprio avatar**. Só as duas animações que o protocolo de rede carrega (`wave` e `dance`), de 1 a 15 s, e só enquanto o avatar está parado (andar, voar ou sentar interrompe); o servidor da ilha aplica e todos que estão perto veem. Ao terminar, o script ouve o motivo (`finished`, `interrupted`, `stopped`, `replaced`).
- **Partículas e luzes (`particles.*`, `light.*`):** emissores com presets (`smoke`, `fire`, `sparks`, `rain`, `snow`, `explosion`, `trail`, `teleport`, `dust`) e luz pontual (com cintilação). A ilha anuncia a todos, guarda para quem chega depois, faz o efeito acompanhar o objeto e o remove quando o script é parado, suspenso, editado ou removido. Limites: 4 emissores por script, 12 por pessoa, 10 criações a cada 10 s, taxa 1–60/s, vida 0,2–6 s; no cliente no máximo 48 emissores (240 partículas cada) e 16 luzes. Desenhados pelo cliente com `CpuParticles3D`/`OmniLight3D`.
- **Som posicional (`audio.playAtPosition`):** toca um som do Conteúdo do objeto num ponto da ilha (3D, some com a distância).
- **Eventos de colisão (`on_collision_enter`/`on_collision`, `on_collision_exit`, `on_land_collision`):** vêm da simulação física real da ilha (Godot) para objetos dinâmicos: batidas acima de 0,3 m/s (contato parado não conta), no máximo 10 por segundo por objeto, com `entity` (objeto ou terreno) e `speed`. Provado por um teste em que uma bola cai no terreno.

## Migrações
`Phase87LuaRuntime`: tabelas `world.script_runtime`, `world.script_logs` (só as últimas 200 linhas por script) e `world.script_grants`. O valor de estado de script passou a aceitar até 2100 caracteres (tabelas). Protocolo de rede 9 → 10.

## Eventos disponíveis
Todos os de `docs/LUA_API.md`, seção Eventos (inclui `on_http_response`): start, reset, destroy, enable, disable, owner_changed, region_start/restart, region_shutdown, touch/touch_start/click, enter/avatar_enter, leave/avatar_leave, chat (com canal), message/object_message, link_message, timer, interval, delay_complete, dialog_response, permission_granted/denied e script_error. Cada um tem gatilho real no servidor.

## Testes
- Unitários e de integração do host: `LuaApiTests`, `LuaCatalogTests`, `ScriptHostTests`, `LuaSandboxTests` (laço infinito, recursão, bombas de memória e de texto, fuga do sandbox, spam de eventos e de mensagens, funções perigosas inexistentes, ataques por tabelas), `ScriptRuntimeApiTests` (API/EF), `LuaDocsTests` (manual = código), `LuaLoadTests`.
- Ilha (Godot headless, contra uma API de mentira): `ScriptingSmoke`, `ScriptingApiSmoke` (alcances do chat, peça do conjunto alterada, diálogo e permissão só para o avatar certo, relatório à API, scripts pausados, desligamento).
- Cliente (Godot headless): `ScriptDialogSmoke`, `ScriptEditorSmoke`, `BuildContentSmoke`.
- Não há teste de ponta a ponta com **vários jogadores reais** nem contra PostgreSQL real nesta entrega.

## Desempenho medido
Máquina: Intel Core i5-12400 (6 núcleos / 12 threads), 15,7 GB de RAM, .NET 10, Windows 11. Cada script: um objeto, um temporizador de 1 s, um estado e um tratador de toque; uma rajada de 2 toques por objeto processada em fatias de 6 ms (como a ilha faz) e 5 s de timers.

| Scripts | Carga | Memória | Rajada (eventos/s) | Tick (pior / médio) | Descartados | Suspensos |
|---:|---:|---:|---:|---:|---:|---:|
| 100 | 0,3 s | 41 MB | ≈ 184 mil | 0,8 / 0,0 ms | 0 | 0 |
| 500 | 0,6 s | 204 MB | ≈ 145 mil | 5,9 / 0,2 ms | 0 | 0 |
| 1 000 | 3,0 s | 414 MB | ≈ 7,2 mil | 13,9 / 4,7 ms | 0 | 0 |
| 5 000 | 12,6 s | 2,1 GB | ≈ 10 mil | 23,9 / 6,4 ms | 6 | 3 |

Leitura honesta: 100 a 1 000 scripts cabem folgadamente; **5 000 funcionam, mas custam 2 GB de RAM e o servidor gasta ~6 ms por tick só com scripts**; pausas de coleta de lixo chegaram a suspender 3 scripts (0,06 %) por estouro de tempo. O teste usa um dono por script (sem o limite de CPU por dono) e eventos curtos: scripts reais pesados reduzem esses números. Antes da otimização de memória, 5 000 scripts usavam 11 GB e o tick chegava a 1,3 s. Rodar `LIFEVERSE_LUA_LOAD=full` mede 1 000 e 5 000.

## Como testar dentro do LifeVerse
1. Entre numa ilha sua, abra **Construir**, crie um objeto e, na aba **Conteúdo**, use **Novo script** (ou abra um existente).
2. Cole o exemplo da porta de `docs/LUA_API.md` (`on_touch` com `object.setRotation` e `chat.say`), clique **Validar** (veja avisos/erros), **Salvar** e aguarde até 15 s.
3. Toque no objeto: a porta gira e fala. Veja as abas Console e Desempenho (atualizam a cada 5 s; a ilha informa a cada 30 s).
4. Teste um diálogo: `dialog.show(event.avatar.id, {title='Porta', message='Abrir?', buttons={'Sim','Não'}})` e trate `on_dialog_response`.
5. **Histórico:** salve duas vezes com códigos diferentes e clique em **Histórico**: cada salvamento guarda o código que ele substituiu (as últimas 20 versões; salvar o mesmo código não cria versão). Escolha uma versão para ver o código e **Restaurar**: ela vira a versão atual e o código de antes também fica no histórico, então dá para desfazer. O código antigo passa de novo pela validação (se as regras mudaram, a restauração é recusada). API: `GET .../scripts/{id}/versions`, `GET .../versions/{n}`, `POST .../versions/{n}/restore`.
6. Teste o limite: `function on_touch() while true do end end` — o script é suspenso e o motivo aparece em Desempenho; **Reiniciar** o religa.

## Pendências (não implementadas — nenhuma delas tem botão ou função de enfeite)
- **Veículos, portas físicas e elevadores** prontos, e os volumes de gatilho com forma própria (hoje `on_trigger_enter/exit` usam a caixa do objeto; esferas e cilindros contam como a caixa em volta, e não há gatilhos sem objeto visível). `on_physics_update` e `on_velocity_changed` já existem (corpos simulados, 4 verificações por segundo, até 200 corpos observados).
- **Animações de objetos** (rig de malha, `animation.*`) e outras animações de avatar além de `wave`/`dance` (as demais são só locais e não viajam pela rede); para objetos use `object.moveTo/rotateTo`.
- **Mídia** (`media.*`, `on_media_finished`) e `on_sound_finished`: a mídia do objeto não é controlável por script. Partículas, luzes e som posicional já existem (ver acima), mas sem texturas próprias dos jogadores e sem trilhas que sigam avatares.
- **HUD:** sem imagens, menus paginados nem HUD anexado ao avatar persistido entre sessões (o painel volta quando o avatar reconecta enquanto o script roda, mas não sobrevive a um reinício da ilha); os layouts do editor visual de HUD continuam separados dos painéis por script.
- **Economia e inventário, o que falta:** `economy.getBalance` (o saldo é privado: não exposto), `on_transaction_completed`. **`economy.setObjectPrice(preco)` já existe**: muda o preço do próprio objeto, só se ele já está à venda e é do dono do script (se não estiver à venda, nada muda); L$ 0 a 10.000.000, 6 mudanças por minuto por script. **`on_purchase` já existe** para a compra do objeto pelo clique Comprar: chega aos scripts do objeto vendido com `price`, `sale_type` (original, copy ou contents) e o comprador, só depois de a API aprovar a compra (uma repetição da mesma compra não conta). **Entregar itens já existe:** `inventory.giveItem(avatarId, nome)` (permissão `GIVE_ITEMS` do avatar) entrega uma **cópia** de um item do Conteúdo do objeto, e só se o dono do objeto tem esse item com direito de copiar e de transferir; o avatar recebe com os direitos de próximo dono. Scripts e notecards não são entregues, no máximo 6 por minuto por script e 40 cópias por hora por item; o resultado chega em `on_item_given` / `on_item_give_failed` (`reason`: `not_copyable`, `item_unavailable`, `too_many`, `unavailable`). Itens que só podem ser movidos (sem copiar) não são entregues por script.
- **NPCs/veículos programáveis** e `on_region_changed`. Terrenos já existem: `parcel.getAt/getId/getName/getOwner/getPermissions`, `object.getParcel` e os eventos `on_parcel_enter/leave` (só dados públicos: nome, dono, acesso, construção, à venda; listas de permitidos/banidos nunca chegam ao script; a ilha confere a posição dos avatares a cada 0,25 s, com os dados do land-sync).
- **Depurador** com breakpoints, variáveis e passo a passo; copiar objeto com regras de execução de script; "adicionar script do inventário" na aba Conteúdo (o botão **Duplicar** já copia um script para o mesmo objeto, só o código); (revogar permissões de avatar já existe no editor, aba Permissões: **Revogar**; a ilha a aplica ao reler o conteúdo, em até 15 s, e o script pode pedir de novo); persistência de timers entre reinícios da ilha (um script os recria em `on_start`).
- Teste de carga com jogadores reais e contra PostgreSQL real; validação visual dentro do jogo aberto.
