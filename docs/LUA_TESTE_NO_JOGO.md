# Roteiro para testar o Lua dentro do jogo

Tudo o que está aqui foi testado por testes automáticos (sem janela e sem jogador real). Este roteiro é para **você conferir no jogo aberto**, numa ilha sua, o que os testes não enxergam: como as coisas aparecem e soam. Marque cada item e anote o que fugiu do esperado.

**Antes de começar**
- Aplique as migrações do banco (`Phase88` e `Phase89`) na API que o jogo usa. Sem elas, o Histórico e a revogação de permissões falham.
- Entre numa ilha sua, abra **Construir**, crie um cubo e, na aba **Conteúdo**, use **Novo script**. Cole cada exemplo abaixo, clique **Validar**, **Salvar** e espere até 15 s (a ilha lê os scripts a cada 15 s).
- Permissões do dono (`CONTROL_PHYSICS`, `HTTP_REQUESTS`, `CONTROL_MEDIA`, `CREATE_NPCS`, `CONTROL_VEHICLES`) ficam na aba **Permissões** do editor.

## 1. Editor

| # | O que fazer | O que esperar |
|---|---|---|
| 1.1 | Salvar duas vezes com códigos diferentes e clicar **Histórico** | A lista mostra a versão anterior; escolher uma mostra o código; **Restaurar** volta a ela e o código de antes continua no histórico |
| 1.2 | Selecionar um script e clicar **Duplicar** | Nasce "nome (cópia)" no mesmo objeto, com o mesmo código |
| 1.3 | Num script que recebeu uma permissão de avatar (ver 4.1), abrir Permissões e clicar **Revogar** | A permissão some; o script precisa pedir de novo (até 15 s) |
| 1.4 | **Depurar** (ver seção 9) | O script para na linha marcada e mostra as variáveis |

## 2. Eventos de toque, terreno, região

```lua
function on_touch(e)      chat.say('toque de ' .. e.avatar.name) end
function on_touch_end(e)  chat.say('soltou após ' .. e.duration .. ' s') end
function on_double_click(e) chat.say('duplo clique!') end
function on_parcel_enter(e) chat.say(e.avatar.name .. ' entrou em ' .. e.parcel.name) end
function on_parcel_leave(e) chat.say(e.avatar.name .. ' saiu de ' .. e.parcel.name) end
function on_region_changed(e) chat.say('região mudou: ' .. e.kinds .. ', mar em ' .. e.sea_level) end
```
- [ ] Clicar rápido duas vezes dispara `on_double_click`; segurar e soltar dispara `on_touch_end` com o tempo.
- [ ] Andar de um terreno para outro avisa entrada e saída (o objeto precisa estar num dos terrenos).
- [ ] Editar o terreno ou o nível do mar avisa **uma vez** a cada 2 s, não a cada pincelada.

## 3. Física, gatilho, loop de movimento

```lua
function on_start() physics.setDynamic(true) end            -- precisa de CONTROL_PHYSICS
function on_touch(e) physics.applyImpulse(0, 8, 0) end
function on_velocity_changed(e) chat.say('velocidade ' .. math.floor(e.speed)) end
function on_trigger_enter(e) chat.say(e.avatar.name .. ' pisou na zona') end
function on_trigger_exit(e)  chat.say(e.avatar.name .. ' saiu da zona') end
```
- [ ] O cubo pula ao toque e avisa a velocidade ao subir e ao parar.
- [ ] Em outro cubo achatado (tapete) com o script de gatilho, pisar nele avisa; sair avisa.

## 4. Permissões de avatar, itens, pagamentos, HUD

```lua
function on_touch(e) permissions.request(e.avatar_id, 'GIVE_ITEMS') end
function on_permission_granted(e) inventory.giveItem(e.avatar_id, 'Brinde') end
function on_item_given(e) chat.say(e.avatar.name .. ' recebeu ' .. e.item) end
function on_item_give_failed(e) chat.say('não deu: ' .. e.reason) end
```
- [ ] 4.1 Ponha um item **copiável e transferível** seu, chamado "Brinde", no Conteúdo do cubo. Tocar abre o pedido de permissão; ao permitir, uma cópia aparece no seu inventário (conta de teste) e o chat avisa.
- [ ] 4.2 Com um item **sem direito de copiar**, o aviso é `not_copyable`.
- [ ] 4.3 `economy.requestPayment(e.avatar_id, 5, 'Café')` abre o diálogo de pagamento; pagar dispara `on_payment`.
- [ ] 4.4 Ponha o objeto à venda (Comprar) e compre com outra conta: o script com `function on_purchase(e) chat.say(e.avatar_name .. ' pagou ' .. e.price) end` avisa. `economy.setObjectPrice(150)` muda o preço de quem comprar depois.

## 5. Efeitos e áudio

```lua
function on_touch(e)
  particles.create({preset = 'fire', rate = 20})
  light.set({radius = 8, flicker = true})
end
```
- [ ] Fogo e luz tremulante aparecem e acompanham o objeto se ele se mover; sumem ao reiniciar o script.
- [ ] Som do Conteúdo: `audio.play('NomeDoSom')` toca em 3D; `function on_sound_finished(e) chat.say('acabou') end` avisa ao terminar.
- [ ] **Mídia da internet** (precisa de `CONTROL_MEDIA`): `media.play('https://SEU-SERVIDOR/musica.mp3', {volume = 0.6, loop = false, range = 25})`. O som sai do objeto e some com a distância; `function on_media_finished(e) chat.say('fim') end` avisa ao terminar (só se alguém estiver perto). Endereço `http://`, com login, `.wav` ou de rede interna é recusado com mensagem.

## 6. Animação do próprio objeto

Num objeto com modelo importado que tenha clipes (veja os nomes na aba Recursos, "Animação de Mesh"):
```lua
function on_touch(e) animation.play('NomeDoClipe', {loop = true, speed = 1.5}) end
```
- [ ] Todos veem a mesma pose; `animation.pause()`, `resume()`, `stop()` e `setSpeed(2)` funcionam. Um nome errado de clipe não faz nada.

## 7. NPCs (`CREATE_NPCS`)

```lua
local guarda
function on_start() guarda = npc.create('Guarda', 10, 10) end
function on_touch(e) npc.moveTo(guarda, 30, 10, 2) end
function on_npc_arrived(e) npc.say(e.npc, 'cheguei!') npc.playAnimation(e.npc, 'wave', 3) end
```
- [ ] O NPC aparece como um avatar para todos, anda em linha reta, fala com o nome dele, acena e some quando o script para ou reinicia. Não entra no mar.
- [ ] Anotar como o avatar sem aparência própria se parece (é o ponto que os testes não mostram).

## 8. Veículos (`CONTROL_VEHICLES`)

Com um veículo **seu** parado na ilha:
```lua
function on_touch(e) for _, v in ipairs(vehicle.list()) do chat.say(v.id .. ' ' .. v.name) end vehicle.driveTo(1, 60, 20, 8) end
function on_vehicle_arrived(e) chat.say('chegou: ' .. tostring(e.arrived)) end
```
- [ ] O carro vai sozinho em linha reta; ao chegar o script avisa. Sentar nele tira o controle do script.
- [ ] Veículo de outra pessoa: `vehicle.list()` não o mostra.

## 9. Depurador

```lua
function on_touch(e)
  local a = 5
  total = (total or 0) + a
  local b = a * 2
  chat.say('b = ' .. b)
end
```
- [ ] Salve, clique na margem esquerda na linha `local b = a * 2` (aparece o ponto), aperte **Depurar**, toque no objeto.
- [ ] O script para; a aba **Depuração** mostra a linha, `a = 5` e `total`. Enquanto parado, outros toques são descartados e o resto da ilha funciona normalmente.
- [ ] **Passo**, **Por cima**, **Sair** e **Continuar** avançam; a fala só sai quando o tratador termina.
- [ ] Deixar parado por 2 minutos: o script é solto e o editor avisa. **Parar depuração** recarrega o script normalmente.

## 10. Limites (devem recusar com mensagem clara)

- [ ] `while true do end` suspende o script e o motivo aparece em Desempenho; **Reiniciar** o religa.
- [ ] `npc.create` 6 vezes no mesmo script: o sexto devolve `nil`. `media.play` mais de 10 vezes por minuto dá erro.
- [ ] Sem a permissão do dono, `npc`, `vehicle`, `media` e `physics` dizem qual permissão falta.

> Anote tudo que parecer errado (texto, som, atraso, aparência) e me mande: os testes automáticos garantem a lógica, não a experiência.
