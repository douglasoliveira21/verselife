# Ilhas e terrenos — Fase 04

As ilhas persistem em `world.islands`, com permissões em `world.permissions`, snapshots versionados em `world.snapshots` e trilha de alterações em `world.changes`. A migração `Phase04PersistentIslands` não usa `EnsureCreated` e mantém UUID, timestamps UTC, índices e exclusão lógica.

Os três templates oficiais são criados no primeiro cadastro, caso ainda não existam:

- `lifeverse-plaza`: praça social pública;
- `tropical-island`: praia, água e relevo;
- `sandbox-island`: terreno de construção livre.

Na ausência de um pacote de assets licenciado, o cliente usa geometria procedural original identificada por `IslandTerrain`. Ela cria relevo determinístico, cores de camada, colisão e quatro limites geográficos. `TerrainEditBuffer` fornece operações de elevar, abaixar, nivelar e suavizar; alterações persistidas exigem a permissão `edit_terrain`, versão esperada e hash SHA-256.

O World Manager serializa a inicialização por UUID, encaminha para o heartbeat saudável mais recente e marca ilhas sem instância como suspensas. O island-server carrega o snapshot ao iniciar e salva periodicamente, além de tentar salvar no encerramento controlado. O salvamento periódico é a proteção principal contra perda em encerramento inesperado.

Estados de execução: `suspended`, `starting`, `running` e `deleted`. Instâncias concorrentes não são iniciadas para a mesma ilha enquanto o lock do World Manager estiver ativo.

## Ilha de boas-vindas e volta ao mesmo lugar

**Para onde se vai ao entrar.** Logo depois do login, o mundo pergunta ao servidor (`GET /api/v1/worlds/me/start`) e leva a pessoa sozinha para uma ilha, uma vez por visita ao mundo (e só se ela ainda não estiver numa ilha):
- a **última ilha** em que ela esteve, se ainda existe e ela pode entrar (pública, dela, ou com permissão `enter`) → "Voltando para X, onde você estava.";
- senão a **ilha de boas-vindas** → "Bem-vindo! Vamos para X." É assim que **todo avatar novo** começa.
O menu **Mundo → "Ir para a Ilha de Boas-vindas"** leva até ela quando quiser (`GET /api/v1/worlds/welcome`).

**A ilha de boas-vindas** (`Island.IsWelcome`, no máximo uma, índice único; migração `Phase48WelcomeAndLocations`). Quem decide:
1. a que o operador marcou: `PUT /api/v1/platform/welcome-island/{islandId}` com a chave de serviço (só ilha pública e existente; a anterior deixa de ser);
2. se ninguém marcou (ou a marcada foi apagada/fechada): a **ilha pública mais antiga que já existe** é usada e marcada, sem criar nada;
3. se não existe nenhuma: é criada a **"Ilha de Boas-vindas"** (pública, 256 m, 100 pessoas), de uma conta de sistema `lifeverse` que **não entra** (hash de senha "!"; se já houver uma pessoa chamada lifeverse, a de sistema é `lifeverse-sistema`).
Ninguém escolhe a ilha de boas-vindas pelos outros: não há rota para usuários.

**Voltar ao mesmo lugar.** O servidor da ilha conta à API onde cada jogador está (`PUT /api/v1/platform/servers/{instanceId}/player-locations`, chave de serviço, a cada 20 s, **quando alguém sai** e ao desligar). A API guarda só o **último lugar** de cada pessoa (`world.player_locations`: ilha e X, Y, Z), ignorando posição fora da ilha, que não é número, altura absurda e gente que não existe. Ao entrar, a validação do ingresso (`/platform/entry-tokens/validate`) devolve ao servidor da ilha o ponto de retomada **se o último lugar era essa ilha**, o ponto de nascer da ilha e o tamanho, e o servidor coloca a pessoa ali (`SpawnPlanner`): no ponto salvo se ainda está dentro da ilha (com 1 m de folga), na altura salva sem ficar sob o chão nem mais de 3 m no ar (um voo não é retomado no ar), senão em volta do ponto de nascer da ilha (oito por fileira, a 2 m). Quem estava num terreno fechado ou banido é movido pela regra de sempre.
**Para onde olhava:** o servidor não guarda a direção, então o cliente guarda neste computador (`user://last_place.json`, a cada 5 s) e vira o avatar para ela ao conectar, se for a mesma pessoa e a mesma ilha.

**Limitações:** o ponto de nascer de cada ilha agora vale (antes todos nasciam em 0, 0) — ilhas com ponto configurado passam a receber ali; a direção do olhar só volta no mesmo computador; se o servidor da ilha cair sem avisar, volta-se ao último ponto contado (até 20 s antes); quem ficou sentado em veículo volta ao ponto do veículo, a pé; o ponto de retomada e a gravação foram verificados pelos testes da API e do `SpawnPlanner`, e o servidor da ilha compila e foi ligado a eles, mas **não foi rodado de ponta a ponta com um banco e jogadores reais**.
