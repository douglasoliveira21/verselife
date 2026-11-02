# Lua e HUDs — Fase 07

## Segurança e runtime

O contrato usa Lua 5.4 (intérprete oficial, licença MIT) como worker externo configurado por `LuaRuntime:WorkerPath`/`LIFEVERSE_LUA_WORKER`. O processo principal nunca avalia código do usuário. O worker recebe um envelope JSON por stdin, não recebe `LUA_PATH`/`LUA_CPATH`, não tem acesso a SQL, arquivos, rede, `os`, `io`, `debug`, `require`, `package`, FFI ou sockets, e é encerrado em 250 ms por padrão. O container de produção deve aplicar limite de memória de 256 KiB/worker e CPU através do sandbox do sistema operacional.

O serviço falha fechado quando o worker não existe. Antes de iniciar, o validador limita fonte a 64 KiB, estima instruções, rejeita loops infinitos óbvios e APIs perigosas, limita eventos a 20/s e quarentena scripts que excedem timeout. Execuções, erros e quarentenas são auditados em `world.script_executions`.

Eventos permitidos: `on_start`, `on_touch`, `on_collision`, `on_enter`, `on_leave`, `on_timer`, `on_chat`, `on_message`, `on_owner_changed`, `on_object_rezzed` e `on_object_removed`. O estado persistente é somente chave/valor JSON limitado por script; não há acesso SQL.

## Editor e HUD

O cliente possui janela Lua com editor, evento, salvar/executar/parar e console. O editor de HUD permite preview de texto, botão, imagem, barra, lista e entrada; layouts são JSON versionados em `inventory.hud_definitions` e podem referenciar um script autorizado. O painel abre pelos botões Lua/HUD na cena World.

Templates de porta, elevador, lâmpada, plataforma, botão, som, teleporte autorizado, placar, temporizador e painel informativo ficam disponíveis em `/api/v1/scripting/templates`.

Operações de inventário, dinheiro e dados privados não são expostas na API de objetos do worker. A integração real depende de um executável Lua 5.4 ou worker containerizado fornecido pela implantação; neste ambiente o binário Lua não está instalado, portanto a execução retorna `worker_unavailable` sem executar código.
