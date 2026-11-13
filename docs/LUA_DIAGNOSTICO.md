# Scripting Lua — diagnóstico técnico (antes da implementação)

## O que já existia e funciona (reaproveitado, não recriado)
| Peça | Onde | Estado |
|---|---|---|
| Runtime Lua | `scripting/LuaSandbox.cs` (MoonSharp 2.0, .NET 8, 100 % C#) | Sandbox forte: biblioteca mínima, coroutine com `AutoYieldCounter` (orçamento de instruções, tempo e alocação por evento), `string.rep`/padrões/`unpack`/`sort` limitados |
| Host de scripts | `scripting/ScriptHost.cs` | Eventos start/touch/enter/leave/timer/chat/message, timers (4), estado persistente (32 chaves), quarentena, taxas, API `object` (própria) |
| Ligação com a ilha | `island-server/Scripts/IslandScripting.cs` | Carrega objetos/scripts da API, proximidade, toque, chat, aplica mudanças pela rota de build autoritativa (permissões do dono, revisão, replicação), som, texto flutuante |
| Persistência | `world.object_scripts`, `world.script_states`, `PUT .../script-state` | Scripts e estado sobrevivem a reinício; rodam sem o dono online |
| API/CRUD/validação | `backend/Modules/Scripting/*` | CRUD, templates, validação de sintaxe, execuções; o antigo worker externo está desativado (410) |
| Editor | `client/Scripts/ScriptEditorWindow.cs` | CodeEdit com números de linha e cores Lua, salvar, validar, console |
| HUD (definições) | `inventory.hud_definitions` | Só o layout JSON; não há runtime de HUD |
| Testes | `LuaSandboxTests`, `ScriptHostTests`, `ScriptEditor/ContentApiTests` | Cobrem sandbox, host e API |

**Escolha do Lua:** MoonSharp. É 100 % C# (sem biblioteca nativa, roda no servidor Linux headless e no Godot Mono), permite preempção por contagem de instruções e dá controle total da ponte C#/Lua (nada de reflexão .NET exposta). Custo: ~2 M instruções/s; por isso o orçamento por evento é pequeno e há limite de CPU por ciclo.

## O que faltava em relação ao pedido (e o que esta entrega cobre)
- API só tinha `object.*` com `snake_case`; faltavam `chat`, `timer` (com *callbacks*), `storage`, `debug`, `region`, `script`, `permissions`, nomes `camelCase`, linkset e eventos de ciclo de vida/erro.
- Ciclo de vida: não havia parar/pausar/reiniciar por script nem `on_reset/on_destroy/on_enable/on_disable/on_script_error`.
- Observabilidade: sem métricas por script (CPU, instruções, memória, eventos descartados, erros) nem registro de logs/stack por script.
- Permissões granulares e diálogos/consentimento: inexistentes.
- Faltam, e dependem de sistemas novos (ver `docs/LUA_SCRIPTING.md`, seção "Pendências"): runtime de HUD no cliente, física dinâmica por script, partículas, animações por script, mídia, NPCs, veículos programáveis, HTTP intermediado, pagamentos por script, inventário por script.
