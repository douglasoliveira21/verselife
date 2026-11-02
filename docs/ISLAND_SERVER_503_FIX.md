# Correção crítica do Island Server / HTTP 503

## Causa raiz identificada

1. O Dockerfile executava apenas a importação do projeto Godot; não fazia `dotnet restore/build` nem verificava que o assembly C# existia na imagem final.
2. A imagem não instalava `libfontconfig1`, dependência nativa reportada pelo runtime.
3. O backend marcava a ilha como `running` durante o registro, antes de snapshot e ENet estarem prontos.
4. O health listener do servidor estava preso a `127.0.0.1`, inadequado para healthcheck externo do container.

## Correções aplicadas

- Dockerfile agora instala `libfontconfig1`, restaura e compila `LifeVerse.IslandServer.csproj` em Release e executa `godot --headless --editor --build-solutions` verificando o DLL gerado.
- O health listener escuta `0.0.0.0` e retorna `503` até registro, snapshot e ENet estarem prontos; retorna `200` somente depois disso.
- Registro de instância passa a estado `starting`; somente heartbeat `healthy` promove a ilha para `running`.
- `NetworkBridge` e `ServerMain` já herdam de `Godot.Node`, e `Server.tscn` referencia `res://Scripts/ServerMain.cs` com a capitalização correta.

## Verificação local

- Backend compilado: aprovado.
- Island-server compilado: aprovado.
- Build Docker: NÃO VERIFICADO, pois Docker CLI não está instalado neste ambiente.
- Execução Godot headless Linux: NÃO VERIFICADO localmente; requer a imagem Godot Mono no host/CI.

## Procedimento EasyPanel

Após publicar o commit, force rebuild sem cache do serviço island. Confira nos logs, nesta ordem: `server_registered`, `world_snapshot_loaded`, `server_started`. O endpoint TCP/HTTP da porta de health deve responder `200` com `ready:true`; antes disso, `503` é esperado. Só então o backend deverá mostrar `runtime_state=running` e o cliente poderá receber o token de entrada.
