# Implantar as ilhas (servidor Unity)

As ilhas rodam no servidor dedicado da Unity, ligadas sob demanda por um serviço só (`island-supervisor`). Desde 27/11/2026 não há mais servidor nem cliente Godot (a troca em produção foi feita nesse dia).

## Como funciona

- Cada ilha tem um motor na coluna `world.islands.engine`. O serviço de ilhas só atende as `unity`.
- O cliente diz o motor no cabeçalho `X-LifeVerse-Engine`; o cliente Unity manda `unity`. Quem não manda (um jogo Godot antigo) recebe 409 `engine_mismatch`.
- A API ainda aceita o valor `godot` (é o padrão da coluna no banco), mas as ilhas que ela cria já nascem `unity`: as oficiais e a de boas-vindas (`IslandEngines.ForNewIslands`) e as criadas pelo jogo. Uma ilha antiga que ficou `godot` não tem servidor: passe-a para `unity` (seção 4).

## 1. A API

A API aplica as migrações sozinha ao subir.

## 2. O pacote do servidor Unity

O servidor Linux não fica no git (60 MB compactado; o pacote leva só o que o servidor usa, sem `StreamingAssets`, onde estão o corpo e os sons do jogo). Num computador com a Unity 6000.6.9f1 e o módulo "Linux Dedicated Server Build Support":

```powershell
powershell -File scripts/package-unity-server.ps1
```

O script gera `artifacts/LifeVerseServer-linux.tar.gz` e mostra o SHA-256. Suba esse arquivo para um endereço que o Easypanel consiga baixar sem login, por exemplo um anexo de release do GitHub (num repositório privado o anexo pede login, então use outro lugar, como o armazenamento de objetos que a API já usa, com um link público). O repositório respondia como público em 27/11/2026. Marque a release do servidor como **pré-lançamento**, para ela não virar a "latest", que a página de download do jogo pode usar (`docs/RELEASES.md`).

## 3. O serviço no Easypanel

Um serviço (App) para as ilhas:

- **Fonte:** este repositório, Dockerfile `island-supervisor/Dockerfile.unity`. **Antes de criar ou atualizar, publique o pacote** (seção 2): sem ele a construção falha.
- **Argumentos de build:** `SERVER_URL` (o endereço do `.tar.gz`) e `SERVER_SHA256` (o hash que o script mostrou). Se o hash não bater, o build para.
- **Variáveis de ambiente:**
  - `LIFEVERSE_API_URL=https://app.lifeverse.euporiagames.com`;
  - `LIFEVERSE_SERVICE_KEY`: a chave de serviço da API (`IslandServer:SharedKey`);
  - `LIFEVERSE_HOSTNAME`: o nome ou IP público que os jogadores alcançam;
  - no servidor atual, `SUPERVISOR_PORT_START=7800` e `SUPERVISOR_MAX_ISLANDS=3`.
- **Portas:** publique em modo host uma porta UDP por ilha aberta ao mesmo tempo (no servidor atual, `7800-7802`) e libere-as no firewall. As portas de saúde ficam dentro do contêiner. O padrão do contêiner é `7800-7807`.

Deixe a ordem de atualização do serviço em `stop-first`. Se um contêiner antigo nunca for substituído (aconteceu na troca do Godot), apague o serviço e crie outro.

## 4. Passar uma ilha antiga para o Unity

Com a ilha parada, um administrador chama:

```powershell
$h = @{ Authorization = "Bearer <token de um administrador>"; "X-Admin-MFA" = "<LIFEVERSE_ADMIN_MFA_TOKEN>" }
Invoke-RestMethod -Method Put -Uri "https://app.lifeverse.euporiagames.com/api/v1/admin/islands/<id da ilha>/engine" -Headers $h -ContentType "application/json" -Body '{"engine":"unity"}'
```

Administrador é quem está em `LIFEVERSE_ADMIN_USER_IDS` e manda o `X-Admin-MFA` certo. Com a ilha rodando, a resposta é 409 `island_running`. Fica registrado na auditoria (`island.engine.unity`). Para várias de uma vez, no banco: `update world.islands set engine = 'unity' where deleted_at is null;`

## 5. Conferir

- No cliente Unity, entre com "Onde parei"; o avatar deve nascer, andar e falar no chat.
- Também dá para conferir sozinho, com uma conta de teste:
  ```powershell
  LifeVerse.exe -screen-fullscreen 0 -lifeverse-e2e https://app.lifeverse.euporiagames.com <usuario> <senha>
  ```
  Esse teste escreve uma mensagem no chat da ilha.
- O log do serviço deve mostrar `supervisor started (unity)`, `started island ...`, `server_started` e `peer_authenticated`.

## O que foi provado e o que não foi

Registro de 26/11/2026, de quando os dois motores ainda conviviam.

**Provado numa cópia local (Ubuntu no WSL 2), 26/11/2026:**
- Postgres de verdade, com a migração aplicada pela API ao subir;
- a API publicada para Linux, o supervisor com `SUPERVISOR_ENGINE=unity` e o servidor Unity para Linux;
- o cliente Unity no Windows (`-lifeverse-e2e`) entrou pela tela de entrada com "Onde parei", o servidor da ilha foi ligado pelo supervisor, o avatar nasceu, andou 6,2 m e a mensagem do chat foi e voltou;
- um cliente sem o cabeçalho (como o Godot) recebeu 409 `engine_mismatch` na entrada e na viagem;
- a API passou nos testes: 2.566 aprovados, 0 falhas.

**Não provado:** a imagem Docker em si (não há Docker aqui), o Easypanel, UDP pela internet, HTTPS contra a API de produção e uso longo (memória, horas seguidas).

**Achado no caminho:** o cliente Unity reconhecia o próprio avatar comparando o nome com o bilhete. Nos testes locais isso batia, mas na API real nunca bateria, e o jogo ficaria preso em "Entrando…". Agora ele lê o `avatar_id` de dentro do bilhete.
