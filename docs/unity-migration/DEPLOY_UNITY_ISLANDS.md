# Pôr as ilhas Unity no ar, ao lado das Godot

Este é o passo a passo para o cliente Unity jogar na produção sem tirar o Godot do ar. Cada ilha roda em um dos dois servidores (o "motor" dela); o cliente de cada motor só entra nas ilhas do seu. Nada aqui foi feito na produção: tudo foi provado de ponta a ponta numa cópia local (WSL), descrita no fim.

## Como funciona

- Cada ilha tem um motor: `godot` (o de hoje, protocolo 11) ou `unity` (LifeVerse Wire 12). Coluna `world.islands.engine`, padrão `godot`.
- O cliente diz o motor no cabeçalho `X-LifeVerse-Engine`. O cliente Unity manda `unity`. O cliente Godot não manda nada, e isso conta como `godot`: os clientes já instalados continuam como estão.
- A API só deixa entrar e viajar para uma ilha do mesmo motor. Se o motor for outro, responde 409 com `error: engine_mismatch` e uma frase que o jogo mostra.
- A lista de ilhas públicas mostra só as do motor do cliente. O lugar inicial ("Onde parei") é a última ilha do mesmo motor, depois a de boas-vindas se for do mesmo motor, depois a pública mais antiga do motor.
- Uma ilha criada pelo cliente Unity já nasce `unity`.
- Cada supervisor pede à API só as ilhas do seu motor (`/api/v1/platform/supervisor/pending?engine=...`). O supervisor Godot de hoje, que não manda `engine`, continua recebendo só as Godot.

## 1. O push (API)

O push leva a migração `Phase104IslandEngine`, que a API aplica sozinha ao subir: uma coluna nova com o valor `godot` em todas as ilhas. Nenhuma ilha muda de motor e nada muda para quem joga no Godot.

## 2. O pacote do servidor Unity

O servidor Linux não fica no git (115 MB compactado). Num computador com a Unity 6000.6.9f1 e o módulo "Linux Dedicated Server Build Support":

```powershell
powershell -File scripts/package-unity-server.ps1
```

O script gera `artifacts/LifeVerseServer-linux.tar.gz` e mostra o SHA-256. Suba esse arquivo para um endereço que o Easypanel consiga baixar sem login, por exemplo um anexo de release do GitHub (num repositório privado o anexo pede login, então use outro lugar, como o armazenamento de objetos que a API já usa, com um link público).

## 3. O serviço no Easypanel

Crie um serviço novo (App), por exemplo `lifeverse-islands-unity`, ao lado do serviço de ilhas Godot:

- **Fonte:** este repositório, Dockerfile `island-supervisor/Dockerfile.unity`.
- **Argumentos de build:** `SERVER_URL` (o endereço do `.tar.gz`) e `SERVER_SHA256` (o hash que o script mostrou). Se o hash não bater, o build para.
- **Variáveis de ambiente:**
  - `LIFEVERSE_API_URL=https://api.nerdplus.com.br`;
  - `LIFEVERSE_SERVICE_KEY`: a mesma chave de serviço que o serviço de ilhas Godot usa (`IslandServer:SharedKey` da API);
  - `LIFEVERSE_HOSTNAME`: o nome ou IP público que os jogadores alcançam (o mesmo tipo de valor do serviço Godot).
- **Portas:** publique as portas UDP `7800-7807` (uma por ilha aberta ao mesmo tempo). O Godot usa `7777...`, então os dois cabem na mesma máquina. As portas de saúde ficam dentro do contêiner.

O padrão do contêiner é `SUPERVISOR_ENGINE=unity`, até 8 ilhas e 10 minutos de inatividade até desligar.

## 4. Passar uma ilha para o Unity

Com a ilha parada (sem servidor rodando), um administrador chama:

```powershell
$h = @{ Authorization = "Bearer <token de um administrador>"; "X-Admin-MFA" = "<LIFEVERSE_ADMIN_MFA_TOKEN>" }
Invoke-RestMethod -Method Put -Uri "https://api.nerdplus.com.br/api/v1/admin/islands/<id da ilha>/engine" -Headers $h -ContentType "application/json" -Body '{"engine":"unity"}'
```

Administrador é quem está em `LIFEVERSE_ADMIN_USER_IDS` e manda o `X-Admin-MFA` certo. Com a ilha rodando, a resposta é 409 `island_running`; espere ela desligar por inatividade. Fica registrado na auditoria (`island.engine.unity`).

Para voltar atrás, chame o mesmo com `{"engine":"godot"}`. Os dados da ilha (objetos, terreno, terrenos) são os mesmos nos dois servidores.

Sugestão: comece por uma ilha de teste nova (criada pelo cliente Unity, que já nasce `unity`), não pela de boas-vindas.

## 5. Conferir

- No cliente Unity, entre com "Onde parei". Ele deve ir para a ilha Unity, e o avatar deve nascer, andar e falar no chat.
- Também dá para conferir sozinho, com uma conta de teste:
  ```powershell
  LifeVerse.exe -screen-fullscreen 0 -lifeverse-e2e https://api.nerdplus.com.br <usuario> <senha>
  ```
  Esse teste escreve uma mensagem no chat da ilha.
- No cliente Godot, a ilha Unity não aparece na lista, e entrar por código mostra a frase de que ela roda no novo LifeVerse.
- O log do serviço deve mostrar `supervisor started (unity)`, `started island ...`, `server_started` e `peer_authenticated`.

## O que foi provado e o que não foi

**Provado numa cópia local (Ubuntu no WSL 2), 26/11/2026:**
- Postgres de verdade, com a migração aplicada pela API ao subir;
- a API publicada para Linux, o supervisor com `SUPERVISOR_ENGINE=unity` e o servidor Unity para Linux;
- o cliente Unity no Windows (`-lifeverse-e2e`) entrou pela tela de entrada com "Onde parei", o servidor da ilha foi ligado pelo supervisor, o avatar nasceu, andou 6,2 m e a mensagem do chat foi e voltou;
- um cliente sem o cabeçalho (como o Godot) recebeu 409 `engine_mismatch` na entrada e na viagem;
- a API passou nos testes: 2.566 aprovados, 0 falhas.

**Não provado:** a imagem Docker em si (não há Docker aqui), o Easypanel, UDP pela internet, HTTPS contra a API de produção e uso longo (memória, horas seguidas).

**Achado no caminho:** o cliente Unity reconhecia o próprio avatar comparando o nome com o bilhete. Nos testes locais isso batia, mas na API real nunca bateria, e o jogo ficaria preso em "Entrando…". Agora ele lê o `avatar_id` de dentro do bilhete.
