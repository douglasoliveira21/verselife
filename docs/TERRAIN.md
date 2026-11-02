# Terreno editável

Cada ilha começa como um terreno plano de 256×256 m (128×128 células de 2 m) coberto pela textura de grama, sob um céu com nuvens e ciclo dia/noite. Quem tem permissão pode reformar e repintar o terreno em tempo real.

## Controles (tecla T)

- Elevar, Rebaixar, Nivelar, Suavizar, Rugosidade: botão esquerdo sobre o terreno aplica o pincel.
- Pintar textura: escolhe entre Grama, Terra, Areia e Rocha (quatro camadas misturadas por vértice).
- Raio do pincel (1–24 m) e intensidade (1–100%) nos controles do painel.

## Arquitetura

- `shared/Terrain/TerrainMap.cs` — mapa de alturas (precisão de 1 mm) e pesos das camadas, pincéis determinísticos, amostragem bilinear, serialização binária comprimida e JSON de snapshot. Ilhas com snapshot vazio ou antigo carregam como terreno plano.
- Servidor da ilha: mantém o `TerrainMap` autoritativo, valida cada edição (permissão, limite de 20 edições/s, valores finitos, raio e intensidade limitados), replica a edição aceita a todos e persiste o mapa no snapshot da ilha a cada 60 s e ao desligar.
- Movimento autoritativo: `AuthoritativeMovement.Step` recebe a altura do terreno; o avatar sobe e desce o relevo e é levantado se o chão sobe sob ele. A origem do avatar fica nos pés.
- Cliente (`IslandTerrain`): malha com normais, shader de mistura de texturas procedurais, `HeightMapShape3D` para colisão, brilho do pincel em `TerrainEditor`.
- Permissão: a API informa `canEditTerrain` na validação do ticket (dono da ilha, ou permissão `edit_terrain`/`admin`).

## Protocolo (v2)

Cliente → servidor: `SubmitTerrainEdit(tool, x, z, radius, strength, layer)`, `RequestTerrain()`.
Servidor → cliente: `TerrainFull(data, canEdit)` após autenticar, `TerrainEdited(..., revision)` para cada edição aceita, `TerrainEditRejected(code)`.
Se a revisão local divergir da recebida, o cliente pede o mapa completo.

## Limitações

- Sem undo/redo de terreno, sem importação/exportação de heightmap e sem água; a ilha fica limitada a 256×256 m.
- A textura é procedural (sem arte licenciada); não há texturas customizadas por usuário.
- Terreno não é editável fora de uma ilha conectada (nada seria persistido).
- Não foi testado com dois clientes reais nem contra o servidor em produção.
