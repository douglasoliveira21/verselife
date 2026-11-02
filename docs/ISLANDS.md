# Ilhas e terrenos — Fase 04

As ilhas persistem em `world.islands`, com permissões em `world.permissions`, snapshots versionados em `world.snapshots` e trilha de alterações em `world.changes`. A migração `Phase04PersistentIslands` não usa `EnsureCreated` e mantém UUID, timestamps UTC, índices e exclusão lógica.

Os três templates oficiais são criados no primeiro cadastro, caso ainda não existam:

- `lifeverse-plaza`: praça social pública;
- `tropical-island`: praia, água e relevo;
- `sandbox-island`: terreno de construção livre.

Na ausência de um pacote de assets licenciado, o cliente usa geometria procedural original identificada por `IslandTerrain`. Ela cria relevo determinístico, cores de camada, colisão e quatro limites geográficos. `TerrainEditBuffer` fornece operações de elevar, abaixar, nivelar e suavizar; alterações persistidas exigem a permissão `edit_terrain`, versão esperada e hash SHA-256.

O World Manager serializa a inicialização por UUID, encaminha para o heartbeat saudável mais recente e marca ilhas sem instância como suspensas. O island-server carrega o snapshot ao iniciar e salva periodicamente, além de tentar salvar no encerramento controlado. O salvamento periódico é a proteção principal contra perda em encerramento inesperado.

Estados de execução: `suspended`, `starting`, `running` e `deleted`. Instâncias concorrentes não são iniciadas para a mesma ilha enquanto o lock do World Manager estiver ativo.
