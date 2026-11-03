# Scripts Lua nos objetos

Cada objeto pode ter scripts Lua. Eles rodam **no servidor da ilha** (nunca na API nem no cliente), dentro de uma máquina virtual Lua isolada por script ([MoonSharp](https://www.moonsharp.org/), interpretador em C# com licença BSD-3). O editor Lua (menu Conteúdo) salva o script pela API; o servidor da ilha lê as alterações em até 15 s.

## Eventos

O script define funções globais com estes nomes; cada uma recebe uma tabela `event`:

| Função | Quando | Campos de `event` |
|---|---|---|
| `on_start` | o script foi carregado ou editado | — |
| `on_touch` (ou `on_touch_start`) | um avatar clica no objeto (até 12 m) | `avatar_id`, `avatar_name`, `user_id`, `distance`, `position` |
| `on_enter` / `on_leave` (`on_avatar_enter` / `on_avatar_leave`) | um avatar entra na área do objeto ou sai dela | os mesmos de toque |
| `on_timer` | um temporizador do script venceu | `timer_id`, `count` |
| `on_chat` | alguém conversa a até 20 m do objeto | `avatar_id`, `avatar_name`, `message` |
| `on_message` (ou `on_object_message`) | outro objeto do mesmo dono enviou uma mensagem | `message`, `from` |

A área de entrada é um retângulo em volta do objeto (metade da maior largura mais 1,5 m).

## API (`object` ou `lv.object`)

Um script só enxerga e muda **o próprio objeto**. Mudanças valem durante o tratamento de um evento e são aplicadas juntas, no fim dele, pelo mesmo caminho dos jogadores (permissões do dono do script, revisão, replicação).

| Chamada | O que faz |
|---|---|
| `get_id()`, `get_name()` | identificação |
| `get_position()`, `get_rotation()`, `get_scale()` | tabelas `{x,y,z}` (rotação em graus) |
| `set_position(v)`, `move(v)` | posição absoluta ou relativa (limitada a ±5000 m) |
| `set_rotation(v)`, `rotate(v)` | rotação absoluta ou relativa, em graus |
| `set_scale(v)` | escala (0,01 a 64) |
| `set_color('#RRGGBB')`, `set_material{color=, emission=, transparency=, roughness=, metallic=}` | aparência |
| `say(texto)` | fala na conversa local (5 mensagens a cada 10 s) |
| `send_message(texto[, id_do_objeto])` | sem destino, igual a `say`; com destino, dispara `on_message` em outro objeto **do mesmo dono** |
| `create_timer(segundos[, repetir])` | temporizador (0,25 s a 24 h; até 4 por script); devolve o id |
| `cancel_timers()` | cancela os temporizadores |
| `state_get(chave)`, `state_set(chave, valor)` | estado persistente (número, texto até 1024 caracteres ou booleano; até 32 chaves); `nil` apaga |
| `lv.time()` | relógio da ilha em segundos |
| `log(texto)` | registro do servidor |
| `play_sound`, `teleport_authorized` | reservadas: ainda devolvem `false` |

Exemplo:

```lua
function on_touch(event)
  local aberto = object.state_get('aberto') or false
  object.state_set('aberto', not aberto)
  object.move({x = aberto and -2 or 2, y = 0, z = 0})
  object.say(event.avatar_name .. (aberto and ' fechou' or ' abriu') .. ' a porta')
end
```

## Limites e segurança

- **Biblioteca mínima:** só `string`, `table`, `math`, `bit32` e funções básicas. Não existem `os`, `io`, `debug`, `load`, `dofile`, `require`, `package`, `pcall`, `setmetatable`, corrotinas nem `collectgarbage`; não há como alcançar arquivos, processos, rede, banco, outros scripts ou o servidor.
- **Orçamento por evento:** 10 000 instruções por padrão (até 100 000 por script), 12 ms e 24 MB alocados. O código roda em fatias de 16 instruções e é interrompido entre elas; nem `pcall` nem comparadores de ordenação escapam disso (`table.sort` é reescrita em Lua).
- **Funções que fariam trabalho ilimitado numa só chamada** são limitadas: `string.rep` (64 KB), padrões (`find`, `match`, `gmatch`, `gsub`: texto até 256 caracteres e no máximo 3 repetições; `gsub` não aceita função), `unpack` (256 valores), `table.sort` (2000 elementos).
- **Quarentena:** 3 estouros de orçamento em 60 s, código que não carrega ou guardar dados demais entre eventos desativa o script até ele ser editado; o dono recebe um aviso na conversa local.
- **Taxas:** 20 eventos por segundo por script, 5 alterações do objeto por segundo, 5 falas a cada 10 s, 4 toques por segundo por avatar, fila de 400 eventos e no máximo 6 ms de scripts por ciclo do servidor.
- **Estado:** o servidor grava o estado dos scripts a cada 30 s e ao desligar (`world.script_states`).

## Componentes

- `scripting/` (`LifeVerse.Scripting`): `LuaSandbox` (a máquina virtual e seus limites) e `ScriptHost` (eventos, temporizadores, estado, quarentena, API `object`).
- `island-server/Scripts/IslandScripting.cs`: liga o host à ilha (objetos e scripts da API, toque, proximidade, conversa, alterações aplicadas como operações de construção).
- API: `GET .../servers/{id}/island-content` e `PUT .../servers/{id}/script-state` (chave de serviço). Os endpoints `.../scripts/run` foram desativados (410): a API não executa mais código de usuário.
- Cliente: clicar num objeto envia o toque (`TouchObject`); passar o mouse mostra o nome. O botão "Executar" do editor Lua envia um toque ao servidor.

## Limitações

- MoonSharp é lento (cerca de 2 milhões de instruções por segundo); por isso o orçamento padrão é pequeno e os scripts devem ser curtos.
- Não há som nem teleporte por script ainda; objetos não ganham animações próprias nem física dinâmica.
- A área de entrada é uma aproximação por distância, não uma colisão física.
- Verificado por testes unitários (ataques de laço infinito, bombas de memória e de texto, tentativas de fuga, limites do host) e por um teste do servidor com uma API de mentira; não testado com jogadores reais nem contra o banco real.
