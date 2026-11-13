# API Lua do LifeVerse — referência

> Gerado automaticamente a partir do catálogo do código (`scripting/ScriptCatalog.cs`); não edite à mão. Cada função listada aqui é verificada por teste: ela existe no ambiente dos scripts.

Os eventos chamam funções globais com o nome do evento e recebem **uma tabela `event`** com os campos de cada evento (não parâmetros soltos). O que não aparece aqui ainda não existe: veja *Pendências* em `docs/LUA_SCRIPTING.md`.

## Eventos

| Evento | Quando | Campos de `event` |
|---|---|---|
| `on_start` | O script foi carregado, editado ou reiniciado. | — |
| `on_reset` | O script foi reiniciado (vem antes de on_start). | — |
| `on_destroy` | O script vai ser removido (ou o objeto foi apagado). | — |
| `on_enable` | O script foi ligado de novo. | — |
| `on_disable` | O script vai ser desligado. | — |
| `on_owner_changed` | O objeto mudou de dono (as permissões do dono antigo acabam). | owner_id |
| `on_region_start` | A ilha começou (ou recomeçou). | — |
| `on_region_shutdown` | A ilha vai ser desligada (guarde o que precisa com storage). | — |
| `on_touch` | Um avatar clica no objeto (a até 12 m). | avatar_id, avatar_name, avatar.id, avatar.name, user_id, distance, position |
| `on_touch_start` | O mesmo que on_touch. | os mesmos de on_touch |
| `on_click` | O mesmo que on_touch. | os mesmos de on_touch |
| `on_touch_end` | O avatar solta o botão depois de tocar no objeto (só chega para quem tocou, até 60 s depois). | os mesmos de on_touch, mais duration (segundos com o botão apertado) |
| `on_double_click` | O mesmo avatar toca duas vezes seguidas no objeto (até 0,5 s entre os toques); on_touch também dispara em cada toque. | os mesmos de on_touch |
| `on_enter` | Um avatar entra na área do objeto. | os mesmos de on_touch |
| `on_avatar_enter` | O mesmo que on_enter. | os mesmos de on_touch |
| `on_leave` | Um avatar sai da área do objeto. | os mesmos de on_touch |
| `on_avatar_leave` | O mesmo que on_leave. | os mesmos de on_touch |
| `on_chat` | Alguém fala a até 20 m (canal 0) ou algo é enviado a um canal que o script escuta. | channel, sender, avatar_name, avatar.id, avatar.name, message, is_object |
| `on_message` | Outro objeto do mesmo dono enviou uma mensagem. | message, from |
| `on_object_message` | O mesmo que on_message. | message, from |
| `on_region_restart` | O mesmo que on_region_start. | — |
| `on_link_message` | Outra peça do conjunto enviou uma mensagem. | message, from, from_script |
| `on_timer` | Um temporizador de object.create_timer venceu. | timer_id, count |
| `on_interval` | Um timer.every com nome disparou. | name, timer_id, count |
| `on_delay_complete` | Um timer.after com nome terminou. | name, timer_id, count |
| `on_http_response` | A resposta de uma http.request chegou (ou ela falhou). | request_id, status, body, error, ok |
| `on_payment` | Um pagamento pedido com economy.requestPayment foi concluído: o dinheiro já está na carteira do dono. | request_id, avatar_id, avatar.id, avatar.name, amount, transaction_id |
| `on_payment_failed` | O pagamento foi recusado pelo avatar ou não pôde ser feito. | request_id, avatar_id, amount, reason (declined, insufficient_balance, wallet_blocked, own_object, unavailable) |
| `on_inventory_changed` | O Conteúdo do objeto mudou (item posto ou tirado). | added, removed (quantos) |
| `on_item_added` | Um item foi posto no Conteúdo do objeto. | item.name, item.type, item.id |
| `on_item_removed` | Um item saiu do Conteúdo do objeto. | item.name, item.type, item.id |
| `on_animation_finished` | Uma animação de avatar começada por avatar.playAnimation terminou. | avatar_id, avatar.id, avatar.name, name, reason (finished, interrupted, stopped, replaced) |
| `on_hud_click` | O jogador clicou num botão do HUD do script. | avatar_id, avatar.id, avatar.name, element |
| `on_hud_input` | O jogador confirmou um campo de texto do HUD. | avatar_id, avatar.id, avatar.name, element, text |
| `on_hud_closed` | O jogador fechou o HUD do script no X. | avatar_id, avatar.id, avatar.name |
| `on_collision_enter` | O corpo físico do objeto bateu em algo (outro objeto ou o terreno). Só objetos dinâmicos (physics.setDynamic) e só batidas com velocidade acima de 0,3 m/s; no máximo 10 por segundo. | entity.type (object ou ground), entity.id, entity.name, speed |
| `on_collision` | O mesmo que on_collision_enter. | os mesmos de on_collision_enter |
| `on_collision_exit` | O corpo físico do objeto deixou de tocar em algo. | entity.type, entity.id, entity.name, speed |
| `on_land_collision` | O corpo físico do objeto bateu no terreno. | entity (type ground), speed |
| `on_parcel_enter` | Um avatar entrou no terreno onde o objeto está. | avatar_id, avatar.id, avatar.name, parcel.id, parcel.name, parcel.owner, parcel.access, parcel.build, parcel.forSale |
| `on_parcel_leave` | Um avatar saiu do terreno onde o objeto está. | os mesmos de on_parcel_enter (do terreno que ele deixou) |
| `on_dialog_response` | Um avatar respondeu a um diálogo. | avatar_id, response, input, request_id |
| `on_permission_granted` | Um avatar concedeu (ou já tinha concedido) uma permissão. | avatar_id, permission |
| `on_permission_denied` | Um avatar negou uma permissão. | avatar_id, permission |
| `on_script_error` | Um evento do próprio script falhou (o script continua rodando). | event, message, line, status |

## Objetos

### `object.getId()`

O identificador do objeto.

- **Retorna:** texto (UUID)
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
chat.say(object.getId())
```

### `object.getName()`

O nome do objeto.

- **Retorna:** texto
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
chat.say(object.getName())
```

### `object.setName(nome)`

Muda o nome do objeto.

- **Retorna:** nada
- **Permissão:** Só o próprio objeto (as regras de construção continuam valendo)
- **Limites:** até 63 caracteres; vale ao fim do evento
- **Erros possíveis:** name longo demais

```lua
object.setName('Porta aberta')
```

### `object.getOwner()`

O identificador do dono do objeto.

- **Retorna:** texto (UUID)
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local dono = object.getOwner()
```

### `object.getRegion()`

O identificador da ilha onde o objeto está.

- **Retorna:** texto (UUID)
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local ilha = object.getRegion()
```

## Transformações

### `object.getPosition()`

A posição do objeto, em metros.

- **Retorna:** tabela {x, y, z}
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local p = object.getPosition()
```

### `object.setPosition(x, y, z)`

Põe o objeto numa posição (também aceita uma tabela {x=, y=, z=}).

- **Retorna:** nada
- **Permissão:** Só o próprio objeto (as regras de construção continuam valendo)
- **Limites:** ±5000 m; no máximo 5 mudanças por segundo
- **Erros possíveis:** valor que não é número finito

```lua
object.setPosition(10, 1, 20)
```

### `object.getRotation()`

A rotação do objeto, em graus.

- **Retorna:** tabela {x, y, z}
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local r = object.getRotation()
```

### `object.setRotation(x, y, z)`

Gira o objeto para uma rotação, em graus.

- **Retorna:** nada
- **Permissão:** Só o próprio objeto (as regras de construção continuam valendo)
- **Limites:** no máximo 5 mudanças por segundo
- **Erros possíveis:** valor que não é número

```lua
object.setRotation(0, 90, 0)
```

### `object.getScale()`

O tamanho do objeto, em metros.

- **Retorna:** tabela {x, y, z}
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local s = object.getScale()
```

### `object.setScale(x, y, z)`

Muda o tamanho do objeto.

- **Retorna:** nada
- **Permissão:** Só o próprio objeto (as regras de construção continuam valendo)
- **Limites:** de 0,01 a 64 m por eixo
- **Erros possíveis:** valor que não é número

```lua
object.setScale(2, 2, 2)
```

### `object.moveTo(x, y, z, velocidade)`

Leva o objeto até uma posição, aos poucos (4 passos por segundo).

- **Retorna:** nada
- **Permissão:** Só o próprio objeto (as regras de construção continuam valendo)
- **Limites:** velocidade de 0,1 a 50 m/s; uma viagem por vez
- **Erros possíveis:** valor que não é número

```lua
object.moveTo(10, 3, 20, 2)
```

### `object.rotateTo(x, y, z, velocidade)`

Gira o objeto até uma rotação, pelo caminho mais curto.

- **Retorna:** nada
- **Permissão:** Só o próprio objeto (as regras de construção continuam valendo)
- **Limites:** velocidade de 1 a 720 graus/s
- **Erros possíveis:** valor que não é número

```lua
object.rotateTo(0, 180, 0, 90)
```

### `object.move({x=, y=, z=})`

Move o objeto em relação à posição atual.

- **Retorna:** nada
- **Permissão:** Só o próprio objeto (as regras de construção continuam valendo)
- **Limites:** ±5000 m
- **Erros possíveis:** —

```lua
object.move({x=1, y=0, z=0})
```

### `object.rotate({x=, y=, z=})`

Gira o objeto em relação à rotação atual.

- **Retorna:** nada
- **Permissão:** Só o próprio objeto (as regras de construção continuam valendo)
- **Limites:** —
- **Erros possíveis:** —

```lua
object.rotate({x=0, y=15, z=0})
```

## Visibilidade

### `object.setVisible(verdadeiro)`

Mostra ou esconde o objeto (transparência total).

- **Retorna:** nada
- **Permissão:** Só o próprio objeto (as regras de construção continuam valendo)
- **Limites:** —
- **Erros possíveis:** —

```lua
object.setVisible(false)
```

### `object.setAlpha(0 a 1)`

Opacidade do objeto (1 = opaco).

- **Retorna:** nada
- **Permissão:** Só o próprio objeto (as regras de construção continuam valendo)
- **Limites:** —
- **Erros possíveis:** —

```lua
object.setAlpha(0.5)
```

### `object.setColor(r, g, b)  ou  object.setColor('#RRGGBB')`

Cor do objeto (r, g e b vão de 0 a 1).

- **Retorna:** nada
- **Permissão:** Só o próprio objeto (as regras de construção continuam valendo)
- **Limites:** —
- **Erros possíveis:** cor inválida

```lua
object.setColor(1, 0, 0)
```

### `object.setGlow(0 a 1)`

Brilho do objeto.

- **Retorna:** nada
- **Permissão:** Só o próprio objeto (as regras de construção continuam valendo)
- **Limites:** —
- **Erros possíveis:** —

```lua
object.setGlow(0.5)
```

### `object.setFullbright(verdadeiro)`

Objeto que ignora as sombras (brilho total).

- **Retorna:** nada
- **Permissão:** Só o próprio objeto (as regras de construção continuam valendo)
- **Limites:** —
- **Erros possíveis:** —

```lua
object.setFullbright(true)
```

### `object.set_material{color=, emission=, transparency=, roughness=, metallic=}`

Muda o material (aceita só essas chaves).

- **Retorna:** nada
- **Permissão:** Só o próprio objeto (as regras de construção continuam valendo)
- **Limites:** —
- **Erros possíveis:** chave desconhecida

```lua
object.set_material{roughness=0.2}
```

### `object.setText(texto, '#RRGGBB')`

Texto flutuante sobre o objeto (texto vazio apaga).

- **Retorna:** nada
- **Permissão:** —
- **Limites:** 120 caracteres; 5 chamadas a cada 10 s
- **Erros possíveis:** cor inválida

```lua
object.setText('Olá', '#12F2E0')
```

## Linksets

### `object.getRoot()`

O objeto raiz do conjunto de peças ligadas.

- **Retorna:** texto (UUID)
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local raiz = object.getRoot()
```

### `object.getChildren()`

As peças filhas do conjunto.

- **Retorna:** lista de textos (UUID)
- **Permissão:** —
- **Limites:** até 200
- **Erros possíveis:** —

```lua
for i, id in ipairs(object.getChildren()) do end
```

### `object.getLinkCount()`

Quantas peças tem o conjunto (a raiz é a 1).

- **Retorna:** número
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local n = object.getLinkCount()
```

### `object.getLink(indice)`

Os dados de uma peça do conjunto (1 = raiz).

- **Retorna:** tabela {id, name, position, rotation, scale} ou nada
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local p = object.getLink(2)
```

### `object.setLinkPosition(indice, x, y, z)`

Move outra peça do conjunto.

- **Retorna:** nada
- **Permissão:** Só peças do mesmo dono
- **Limites:** ±5000 m; 5 mudanças por segundo
- **Erros possíveis:** peça de outra pessoa

```lua
object.setLinkPosition(2, 1, 0, 0)
```

### `object.setLinkRotation(indice, x, y, z)`

Gira outra peça do conjunto.

- **Retorna:** nada
- **Permissão:** Só peças do mesmo dono
- **Limites:** 5 mudanças por segundo
- **Erros possíveis:** peça de outra pessoa

```lua
object.setLinkRotation(2, 0, 90, 0)
```

### `object.sendLinkMessage(mensagem)`

Manda uma mensagem aos scripts das outras peças do conjunto (evento on_link_message).

- **Retorna:** verdadeiro se alguém recebeu
- **Permissão:** Só peças do mesmo dono
- **Limites:** 256 caracteres; 20 a cada 10 s
- **Erros possíveis:** —

```lua
object.sendLinkMessage('abrir')
```

### `script.sendLinkMessage(mensagem)`

O mesmo que object.sendLinkMessage.

- **Retorna:** verdadeiro se alguém recebeu
- **Permissão:** Só peças do mesmo dono
- **Limites:** 20 a cada 10 s
- **Erros possíveis:** —

```lua
script.sendLinkMessage('abrir')
```

## Scripts

### `script.getId()`

O identificador deste script.

- **Retorna:** texto (UUID)
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local id = script.getId()
```

### `script.getName()`

O nome deste script.

- **Retorna:** texto
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local nome = script.getName()
```

## Chat

### `chat.say(mensagem)`

Fala no chat local (a até 30 m).

- **Retorna:** nada
- **Permissão:** —
- **Limites:** 256 caracteres; 5 mensagens a cada 10 s
- **Erros possíveis:** mensagem vazia; limite de taxa

```lua
chat.say('Olá!')
```

### `chat.whisper(mensagem)`

Sussurra: só quem está a até 10 m ouve.

- **Retorna:** nada
- **Permissão:** —
- **Limites:** 256 caracteres; mesmo limite do say
- **Erros possíveis:** mensagem vazia

```lua
chat.whisper('psiu')
```

### `chat.shout(mensagem)`

Grita: quem está a até 100 m ouve.

- **Retorna:** nada
- **Permissão:** —
- **Limites:** 256 caracteres; mesmo limite do say
- **Erros possíveis:** mensagem vazia

```lua
chat.shout('Alarme!')
```

### `chat.sendChannel(canal, mensagem)`

Manda uma mensagem a scripts que escutam o canal. Canais positivos são públicos (até 100 m); negativos só chegam a scripts do mesmo dono.

- **Retorna:** quantos scripts receberam
- **Permissão:** —
- **Limites:** 10 a cada 10 s; o canal 0 é o chat aberto e não pode ser usado
- **Erros possíveis:** canal 0

```lua
chat.sendChannel(100, 'abrir')
```

### `chat.listen(canal)`

Passa a escutar um canal (evento on_chat com o campo channel).

- **Retorna:** nada
- **Permissão:** —
- **Limites:** até 8 canais por script
- **Erros possíveis:** canal 0; canais demais

```lua
chat.listen(100)
```

### `chat.stopListen(canal)`

Para de escutar um canal.

- **Retorna:** nada
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
chat.stopListen(100)
```

### `chat.sendPrivateMessage(avatarId, mensagem)`

Mensagem privada a um avatar que está na ilha.

- **Retorna:** verdadeiro se foi entregue
- **Permissão:** SEND_MESSAGES do avatar, ou ele ter tocado no objeto nos últimos 5 minutos
- **Limites:** 256 caracteres; 5 a cada 10 s
- **Erros possíveis:** —

```lua
chat.sendPrivateMessage(event.avatar_id, 'Obrigado!')
```

### `object.sendMessage(mensagem, idDoObjeto)`

Manda uma mensagem a outro objeto do mesmo dono (evento on_message).

- **Retorna:** verdadeiro se entregue
- **Permissão:** Só objetos do mesmo dono
- **Limites:** 256 caracteres
- **Erros possíveis:** id inválido

```lua
object.sendMessage('abrir', outroId)
```

### `object.broadcastMessage(mensagem)`

Manda uma mensagem a todos os outros objetos com script do mesmo dono.

- **Retorna:** quantos objetos receberam
- **Permissão:** Só objetos do mesmo dono
- **Limites:** até 50 objetos; 3 vezes a cada 10 s
- **Erros possíveis:** limite de taxa

```lua
object.broadcastMessage('alarme')
```

## Temporizadores

### `timer.after(segundos, funcao)  ou  timer.after(segundos, 'nome')`

Chama a função uma vez depois do tempo (com um nome, dispara on_delay_complete).

- **Retorna:** número (id do temporizador)
- **Permissão:** —
- **Limites:** 0,25 s a 24 h; até 16 por script
- **Erros possíveis:** tempo inválido; temporizadores demais

```lua
timer.after(5, function() chat.say('pronto') end)
```

### `timer.every(segundos, funcao)  ou  timer.every(segundos, 'nome')`

Chama a função a cada intervalo (com um nome, dispara on_interval).

- **Retorna:** número (id do temporizador)
- **Permissão:** —
- **Limites:** 0,25 s a 24 h; até 16 por script
- **Erros possíveis:** tempo inválido; temporizadores demais

```lua
timer.every(10, function() object.rotate({x=0, y=15, z=0}) end)
```

### `timer.cancel(id)`

Cancela um temporizador.

- **Retorna:** verdadeiro se existia
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
timer.cancel(meu)
```

### `timer.pause(id)`

Pausa um temporizador (o tempo que falta fica guardado).

- **Retorna:** verdadeiro se pausou
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
timer.pause(meu)
```

### `timer.resume(id)`

Continua um temporizador pausado.

- **Retorna:** verdadeiro se continuou
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
timer.resume(meu)
```

### `timer.getRemaining(id)`

Quanto falta para o temporizador disparar, em segundos.

- **Retorna:** número ou nada
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local falta = timer.getRemaining(meu)
```

### `object.create_timer(segundos, repetir)`

Temporizador que dispara o evento on_timer.

- **Retorna:** número (id)
- **Permissão:** —
- **Limites:** 0,25 s a 24 h; até 16 por script
- **Erros possíveis:** tempo inválido

```lua
object.create_timer(5, true)
```

### `object.cancel_timers()`

Cancela todos os temporizadores.

- **Retorna:** nada
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
object.cancel_timers()
```

## Persistência

### `storage.set(chave, valor)`

Guarda um valor que sobrevive a reinícios (número, texto, booleano ou tabela pequena). Valor nil apaga.

- **Retorna:** nada
- **Permissão:** —
- **Limites:** chave de 1 a 64 caracteres; 32 chaves; texto até 1024; tabela até 64 itens, 3 níveis e 2048 caracteres
- **Erros possíveis:** valor não guardável

```lua
storage.set('aberta', true)
```

### `storage.get(chave)`

Lê um valor guardado.

- **Retorna:** o valor ou nada
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local aberta = storage.get('aberta')
```

### `storage.delete(chave)`

Apaga um valor guardado.

- **Retorna:** nada
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
storage.delete('aberta')
```

### `storage.keys()`

As chaves guardadas, em ordem.

- **Retorna:** lista de textos
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
for _, k in ipairs(storage.keys()) do end
```

## Depuração

### `debug.log(...)`

Escreve no console do script.

- **Retorna:** nada
- **Permissão:** —
- **Limites:** 50 linhas por segundo
- **Erros possíveis:** —

```lua
debug.log('porta aberta')
```

### `debug.warn(...)`

Escreve um aviso no console.

- **Retorna:** nada
- **Permissão:** —
- **Limites:** 50 linhas por segundo
- **Erros possíveis:** —

```lua
debug.warn('velocidade alta')
```

### `debug.error(...)`

Escreve um erro no console (não para o script).

- **Retorna:** nada
- **Permissão:** —
- **Limites:** 50 linhas por segundo
- **Erros possíveis:** —

```lua
debug.error('falha de configuração')
```

### `print(...)`

O mesmo que debug.log.

- **Retorna:** nada
- **Permissão:** —
- **Limites:** 50 linhas por segundo
- **Erros possíveis:** —

```lua
print('olá', 3)
```

### `log(texto)`

Registro do servidor.

- **Retorna:** nada
- **Permissão:** —
- **Limites:** 200 caracteres
- **Erros possíveis:** —

```lua
log('iniciado')
```

## Regiões

### `region.getName()`

O nome da ilha.

- **Retorna:** texto
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
chat.say(region.getName())
```

### `region.getId()`

O identificador da ilha.

- **Retorna:** texto (UUID)
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local id = region.getId()
```

### `region.getAvatarCount()`

Quantas pessoas estão na ilha.

- **Retorna:** número
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
chat.say(region.getAvatarCount())
```

## Permissões

### `permissions.request(avatarId, 'PERMISSAO')`

Pede uma permissão a um avatar (aparece um diálogo para ele). A resposta chega em on_permission_granted ou on_permission_denied.

- **Retorna:** verdadeiro se o pedido foi mostrado
- **Permissão:** —
- **Limites:** o mesmo pedido só se repete depois de 30 s; permissões válidas: CONTROL_ANIMATION, CONTROL_AVATAR, REQUEST_PAYMENT, SHOW_HUD, GIVE_ITEMS, SEND_MESSAGES
- **Erros possíveis:** permissão desconhecida

```lua
permissions.request(event.avatar_id, 'CONTROL_ANIMATION')
```

### `permissions.has(avatarId, 'PERMISSAO')`

Se o avatar já deu a permissão.

- **Retorna:** verdadeiro ou falso
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
if permissions.has(id, 'SEND_MESSAGES') then end
```

### `permissions.revoke(avatarId, 'PERMISSAO')`

O script devolve uma permissão que tinha.

- **Retorna:** verdadeiro se tinha
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
permissions.revoke(id, 'SEND_MESSAGES')
```

## Diálogos

### `dialog.show(avatarId, {title=, message=, buttons={...}, input=, timeout=})`

Mostra uma janela com botões (e uma caixa de texto, se pedir) a um avatar próximo. A resposta chega em on_dialog_response.

- **Retorna:** texto (id do diálogo)
- **Permissão:** O avatar precisa estar a até 40 m
- **Limites:** até 12 botões; 3 diálogos abertos por script; 10 a cada 10 s; tempo de 5 a 300 s
- **Erros possíveis:** limite de diálogos

```lua
dialog.show(event.avatar_id, {title='Porta', message='Abrir?', buttons={'Sim', 'Não'}})
```

### `dialog.cancel(idDoDialogo)`

Cancela um diálogo que ainda não foi respondido.

- **Retorna:** verdadeiro se existia
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
dialog.cancel(id)
```

## Áudio

### `audio.play(nome, volume, repetir)`

Toca um som que está no Conteúdo do objeto, para quem está perto.

- **Retorna:** verdadeiro se o objeto tem esse som
- **Permissão:** —
- **Limites:** 5 chamadas a cada 10 s
- **Erros possíveis:** —

```lua
audio.play('Campainha', 0.8)
```

### `audio.stop()`

Para o som do objeto.

- **Retorna:** nada
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
audio.stop()
```

### `object.play_sound(nome, volume, repetir)`

O mesmo que audio.play.

- **Retorna:** verdadeiro se tem o som
- **Permissão:** —
- **Limites:** 5 a cada 10 s
- **Erros possíveis:** —

```lua
object.play_sound('Campainha')
```

### `object.stop_sound()`

O mesmo que audio.stop.

- **Retorna:** nada
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
object.stop_sound()
```

### `audio.playAtPosition(nome, x, y, z, volume, repetir)`

Toca um som que está no Conteúdo do objeto num ponto da ilha (som 3D: some com a distância).

- **Retorna:** verdadeiro se o objeto tem esse som
- **Permissão:** —
- **Limites:** 5 chamadas a cada 10 s; ±5000 m
- **Erros possíveis:** —

```lua
audio.playAtPosition('Alarme', 10, 1, 20, 0.8)
```

## Física

### `physics.setEnabled(verdadeiro)`

Liga ou desliga a colisão sólida do objeto.

- **Retorna:** nada
- **Permissão:** CONTROL_PHYSICS, dada pelo dono no editor
- **Limites:** vale ao fim do evento; as regras de física das construções continuam valendo
- **Erros possíveis:** falta de permissão

```lua
physics.setEnabled(false)
```

### `physics.setCollisionEnabled(verdadeiro)`

O mesmo que physics.setEnabled.

- **Retorna:** nada
- **Permissão:** CONTROL_PHYSICS, dada pelo dono no editor
- **Limites:** —
- **Erros possíveis:** falta de permissão

```lua
physics.setCollisionEnabled(true)
```

### `physics.setPhantom(verdadeiro)`

Torna o objeto um fantasma (sem colisão) ou sólido de novo.

- **Retorna:** nada
- **Permissão:** CONTROL_PHYSICS, dada pelo dono no editor
- **Limites:** —
- **Erros possíveis:** falta de permissão

```lua
physics.setPhantom(true)
```

### `physics.setDynamic(verdadeiro)`

Faz o objeto obedecer à física (cair, rolar, ser empurrado) ou o deixa parado.

- **Retorna:** nada
- **Permissão:** CONTROL_PHYSICS, dada pelo dono no editor
- **Limites:** precisa de colisão sólida e não pode ser gatilho nem malha triangular
- **Erros possíveis:** falta de permissão

```lua
physics.setDynamic(true)
```

### `physics.setMass(massa)`

Massa do objeto, em kg.

- **Retorna:** nada
- **Permissão:** CONTROL_PHYSICS, dada pelo dono no editor
- **Limites:** de 0,01 a 10000
- **Erros possíveis:** massa fora dos limites

```lua
physics.setMass(10)
```

### `physics.setGravityScale(fator)`

Multiplica a gravidade do objeto (0 = flutua).

- **Retorna:** verdadeiro se o objeto é simulado
- **Permissão:** CONTROL_PHYSICS, dada pelo dono no editor
- **Limites:** de -5 a 5; vale enquanto a ilha roda
- **Erros possíveis:** falta de permissão

```lua
physics.setGravityScale(0.2)
```

### `physics.applyImpulse(x, y, z)`

Dá um empurrão instantâneo ao objeto.

- **Retorna:** verdadeiro se o objeto é simulado
- **Permissão:** CONTROL_PHYSICS, dada pelo dono no editor
- **Limites:** até 10 chamadas por segundo; o objeto precisa ser dinâmico
- **Erros possíveis:** falta de permissão; limite de taxa

```lua
physics.applyImpulse(0, 5, 0)
```

### `physics.applyForce(x, y, z)`

Aplica uma força por 0,1 segundo.

- **Retorna:** verdadeiro se o objeto é simulado
- **Permissão:** CONTROL_PHYSICS, dada pelo dono no editor
- **Limites:** até 10 chamadas por segundo
- **Erros possíveis:** falta de permissão; limite de taxa

```lua
physics.applyForce(0, 100, 0)
```

### `physics.setVelocity(x, y, z)`

Define a velocidade do objeto, em m/s.

- **Retorna:** verdadeiro se o objeto é simulado
- **Permissão:** CONTROL_PHYSICS, dada pelo dono no editor
- **Limites:** até 50 m/s
- **Erros possíveis:** falta de permissão; limite de taxa

```lua
physics.setVelocity(0, 10, 0)
```

### `physics.setAngularVelocity(x, y, z)`

Define a rotação do objeto, em radianos por segundo.

- **Retorna:** verdadeiro se o objeto é simulado
- **Permissão:** CONTROL_PHYSICS, dada pelo dono no editor
- **Limites:** até 20 rad/s
- **Erros possíveis:** falta de permissão; limite de taxa

```lua
physics.setAngularVelocity(0, 3, 0)
```

### `physics.getVelocity()`

A velocidade atual do objeto.

- **Retorna:** tabela {x, y, z} ou nada se o objeto não é simulado
- **Permissão:** CONTROL_PHYSICS, dada pelo dono no editor
- **Limites:** —
- **Erros possíveis:** falta de permissão

```lua
local v = physics.getVelocity()
```

## HTTP

### `http.request({url=, method=, headers=, body=})`

Faz uma requisição a um endereço externo, em nome do script (quem faz é o servidor da API, não o script). A resposta chega em on_http_response.

- **Retorna:** texto (id da requisição)
- **Permissão:** HTTP_REQUESTS, dada pelo dono do objeto no editor (aba Permissões)
- **Limites:** só https, portas 443 e 8443, endereços públicos; GET ou POST; corpo até 8 KB; resposta até 32 KB (json ou texto); 8 s; no máximo 3 ao mesmo tempo e 10 por minuto por script; até 8 cabeçalhos (Accept, Content-Type, Authorization, User-Agent e X-...)
- **Erros possíveis:** falta de permissão; endereço privado ou não permitido; limite de taxa

```lua
http.request({url='https://api.exemplo.com/status', method='GET'})
```

## Economia

### `economy.requestPayment(avatarId, valor, motivo)`

Pede um pagamento a um avatar. Aparece uma janela dizendo quem pede e quanto, e o avatar tem de apertar Pagar. O dinheiro vai para o DONO do objeto (o script não escolhe o destino e nunca mexe em saldo). O resultado chega em on_payment (dinheiro que de fato mudou de mãos) ou on_payment_failed.

- **Retorna:** texto (id do pedido)
- **Permissão:** —: cada pagamento exige a confirmação do avatar
- **Limites:** L$ 1 a L$ 100.000; até 5 pedidos esperando por script e 10 por minuto; a mesma pessoa só pode ser cobrada de novo depois de 10 s; o avatar tem 60 s para responder
- **Erros possíveis:** valor fora dos limites; limite de taxa

```lua
economy.requestPayment(event.avatar.id, 100, 'Entrada')
```

## Inventário

### `inventory.list()`

Os itens que o objeto carrega no Conteúdo (sons, texturas, notas...), só leitura.

- **Retorna:** lista de tabelas {name, type, id}
- **Permissão:** —
- **Limites:** até 200 itens
- **Erros possíveis:** —

```lua
for _, item in ipairs(inventory.list()) do end
```

### `inventory.getItem('Nome')`

Um item do objeto pelo nome (sem diferenciar maiúsculas).

- **Retorna:** tabela {name, type, id} ou nada
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local som = inventory.getItem('Campainha')
```

### `inventory.hasItem('Nome')`

Se o objeto tem um item com esse nome.

- **Retorna:** verdadeiro ou falso
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
if inventory.hasItem('Chave') then end
```

### `inventory.getItemType('Nome')`

O tipo (categoria) de um item do objeto.

- **Retorna:** texto ou nada
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local tipo = inventory.getItemType('Campainha')
```

### `inventory.count()`

Quantos itens o objeto carrega.

- **Retorna:** número
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
chat.say(inventory.count())
```

## Animações

### `avatar.playAnimation(avatarId, 'dance', segundos)`

Faz o avatar dançar ou acenar por alguns segundos, para todos que estão perto. Só acontece se o avatar estiver parado: se ele andar, voar ou sentar, a animação termina. Quando acaba, o script ouve on_animation_finished.

- **Retorna:** verdadeiro se começou
- **Permissão:** CONTROL_ANIMATION, dada pelo avatar (permissions.request)
- **Limites:** animações: wave e dance; de 1 a 15 s; 10 a cada 10 s
- **Erros possíveis:** sem permissão do avatar; animação desconhecida; limite de taxa

```lua
avatar.playAnimation(event.avatar.id, 'dance', 5)
```

### `avatar.stopAnimation(avatarId)`

Interrompe a animação que o script começou nesse avatar.

- **Retorna:** nada
- **Permissão:** CONTROL_ANIMATION, dada pelo avatar
- **Limites:** —
- **Erros possíveis:** sem permissão do avatar

```lua
avatar.stopAnimation(event.avatar.id)
```

## HUD

### `hud.show(avatarId)`

Mostra o painel (HUD) do script na tela de um avatar. O jogador pode fechá-lo no X (o script ouve on_hud_closed). Os elementos que o script definiu com hud.set... aparecem nele.

- **Retorna:** nada
- **Permissão:** SHOW_HUD, dada pelo avatar (permissions.request)
- **Limites:** até 12 elementos por HUD; até 20 HUDs por script; atualizações enviadas no máximo 4 vezes por segundo
- **Erros possíveis:** sem permissão do avatar

```lua
hud.show(event.avatar.id)
```

### `hud.hide(avatarId)`

Esconde o HUD do avatar.

- **Retorna:** nada
- **Permissão:** SHOW_HUD, dada pelo avatar
- **Limites:** —
- **Erros possíveis:** sem permissão do avatar

```lua
hud.hide(event.avatar.id)
```

### `hud.setTitle(avatarId, texto)`

O título do painel.

- **Retorna:** nada
- **Permissão:** SHOW_HUD, dada pelo avatar
- **Limites:** até 120 caracteres
- **Erros possíveis:** sem permissão do avatar

```lua
hud.setTitle(event.avatar.id, 'Painel da loja')
```

### `hud.setPosition(avatarId, x, y)`

Onde o painel fica na tela (0 a 1: 0,8 e 0,25 é à direita, no alto).

- **Retorna:** nada
- **Permissão:** SHOW_HUD, dada pelo avatar
- **Limites:** de 0,05 a 0,95
- **Erros possíveis:** sem permissão do avatar

```lua
hud.setPosition(event.avatar.id, 0.8, 0.2)
```

### `hud.setSize(avatarId, largura)`

A largura do painel, em pixels.

- **Retorna:** nada
- **Permissão:** SHOW_HUD, dada pelo avatar
- **Limites:** de 160 a 420
- **Erros possíveis:** sem permissão do avatar

```lua
hud.setSize(event.avatar.id, 280)
```

### `hud.setLabel(avatarId, id, texto, '#RRGGBB')`

Cria ou muda uma linha de texto do painel. O id tem 1 a 32 letras, números, _ ou -.

- **Retorna:** nada
- **Permissão:** SHOW_HUD, dada pelo avatar
- **Limites:** até 120 caracteres
- **Erros possíveis:** id inválido; elementos demais

```lua
hud.setLabel(event.avatar.id, 'status', 'Vida: 100', '#3BF45F')
```

### `hud.setButton(avatarId, id, texto)`

Cria ou muda um botão. Quando o jogador clica, o script ouve on_hud_click com o campo element.

- **Retorna:** nada
- **Permissão:** SHOW_HUD, dada pelo avatar
- **Limites:** até 120 caracteres
- **Erros possíveis:** id inválido; elementos demais

```lua
hud.setButton(event.avatar.id, 'ativar', 'Ativar')
```

### `hud.setProgress(avatarId, id, valor, maximo, texto)`

Cria ou muda uma barra de progresso.

- **Retorna:** nada
- **Permissão:** SHOW_HUD, dada pelo avatar
- **Limites:** máximo a partir de 1
- **Erros possíveis:** id inválido; elementos demais

```lua
hud.setProgress(event.avatar.id, 'energia', 70, 100, 'Energia')
```

### `hud.setInput(avatarId, id, dica)`

Cria ou muda um campo de texto. Ao confirmar com Enter, o script ouve on_hud_input com element e text.

- **Retorna:** nada
- **Permissão:** SHOW_HUD, dada pelo avatar
- **Limites:** texto de até 200 caracteres
- **Erros possíveis:** id inválido; elementos demais

```lua
hud.setInput(event.avatar.id, 'nome', 'Seu nome')
```

### `hud.remove(avatarId, id)`

Tira um elemento do painel.

- **Retorna:** nada
- **Permissão:** SHOW_HUD, dada pelo avatar
- **Limites:** —
- **Erros possíveis:** id inválido

```lua
hud.remove(event.avatar.id, 'status')
```

### `hud.clear(avatarId)`

Tira todos os elementos do painel.

- **Retorna:** nada
- **Permissão:** SHOW_HUD, dada pelo avatar
- **Limites:** —
- **Erros possíveis:** sem permissão do avatar

```lua
hud.clear(event.avatar.id)
```

## Efeitos

### `particles.create({preset=, rate=, lifetime=, size=, color=, speed=})`

Cria um emissor de partículas no objeto, visível para todos na ilha. Presets: smoke, fire, sparks, rain, snow, explosion, trail, teleport, dust (também aceita texture='smoke' como no exemplo do manual). color é '#RRGGBB' ou {r, g, b} de 0 a 1.

- **Retorna:** número (id do emissor)
- **Permissão:** —
- **Limites:** até 4 emissores por script e 12 por pessoa; 10 criações a cada 10 s; rate 1 a 60 por segundo, lifetime 0,2 a 6 s, size 0,05 a 2 m, speed 0 a 10
- **Erros possíveis:** preset desconhecido; emissores demais; número inválido

```lua
particles.create({preset='fire', rate=30, lifetime=1.5, size=0.5, color={1, 0.5, 0}, speed=2})
```

### `particles.stop(id)`

Para um emissor.

- **Retorna:** verdadeiro se existia
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
particles.stop(fumaca)
```

### `particles.stopAll()`

Para todos os emissores do script.

- **Retorna:** quantos pararam
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
particles.stopAll()
```

### `light.set({color=, radius=, energy=, flicker=})`

Põe uma luz no objeto (uma por script); chamar de novo a muda.

- **Retorna:** nada
- **Permissão:** —
- **Limites:** radius 0,5 a 30 m; energy 0 a 8
- **Erros possíveis:** número inválido

```lua
light.set({color='#FFC46B', radius=10, energy=2, flicker=true})
```

### `light.off()`

Apaga a luz do script.

- **Retorna:** nada
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
light.off()
```

## Terrenos

### `parcel.getAt(x, z)`

Os dados públicos do terreno que cobre um ponto da ilha.

- **Retorna:** tabela {id, name, owner, access, build, forSale} ou nada onde não há terreno
- **Permissão:** —
- **Limites:** só o que qualquer visitante vê no mapa de terrenos
- **Erros possíveis:** —

```lua
local p = parcel.getAt(10, 20)
```

### `parcel.getId()`

O identificador do terreno onde o objeto está.

- **Retorna:** texto (UUID) ou nada
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local id = parcel.getId()
```

### `parcel.getName()`

O nome do terreno onde o objeto está.

- **Retorna:** texto ou nada
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
chat.say(parcel.getName())
```

### `parcel.getOwner()`

O dono do terreno onde o objeto está.

- **Retorna:** texto (UUID) ou nada
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local dono = parcel.getOwner()
```

### `parcel.getPermissions()`

Como o terreno é aberto: entrada, construção e se está à venda.

- **Retorna:** tabela {access, build, forSale} ou nada
- **Permissão:** —
- **Limites:** access: public, list ou private; build: owner, list ou everyone
- **Erros possíveis:** —

```lua
local p = parcel.getPermissions()
```

### `object.getParcel()`

O identificador do terreno onde o objeto está.

- **Retorna:** texto (UUID) ou nada
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local id = object.getParcel()
```

## Tempo

### `lv.time()`

O relógio da ilha, em segundos.

- **Retorna:** número
- **Permissão:** —
- **Limites:** —
- **Erros possíveis:** —

```lua
local agora = lv.time()
```

## Permissões

| Permissão | O que permite | Pedida ao avatar |
|---|---|---|
| `MODIFY_OBJECT` | O script pode modificar o objeto | não |
| `CONTROL_PHYSICS` | O script pode controlar a física do objeto | não |
| `CONTROL_ANIMATION` | O script pode controlar as animações do seu avatar | sim (`permissions.request`) |
| `CONTROL_AVATAR` | O script pode controlar o seu avatar | sim (`permissions.request`) |
| `SHOW_HUD` | O script pode mostrar um HUD na sua tela | sim (`permissions.request`) |
| `REQUEST_PAYMENT` | O script pode pedir pagamentos em seu nome | sim (`permissions.request`) |
| `GIVE_ITEMS` | O script pode entregar itens a você | sim (`permissions.request`) |
| `SEND_MESSAGES` | O script pode enviar mensagens privadas a você | sim (`permissions.request`) |
| `HTTP_REQUESTS` | O script pode acessar endereços externos | não |
| `CREATE_NPCS` | O script pode criar NPCs | não |
| `CONTROL_MEDIA` | O script pode controlar a mídia do objeto | não |
| `REGION_INFO` | O script pode ler informações da região | não |

## Estados de um script

| Estado | Significa |
|---|---|
| `Created` | criado |
| `Loaded` | carregado |
| `Validated` | validado |
| `Running` | executando |
| `Waiting` | aguardando evento |
| `Paused` | pausado |
| `Suspended` | suspenso por limite |
| `Stopped` | parado |
| `Faulted` | com erro |
| `Destroyed` | destruído |
