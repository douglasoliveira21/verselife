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

## Protocolo (v3)

Cliente → servidor: `SubmitTerrainEdit(tool, x, z, radius, strength, layer)`, `RequestTerrain()`.
Servidor → cliente: `TerrainFull(data, canEdit)` após autenticar, `TerrainEdited(..., revision)` para cada edição aceita, `TerrainEditRejected(code)`.
Se a revisão local divergir da recebida, o cliente pede o mapa completo.

## Limitações

- Sem rios nem ondas quebrando na praia; a ilha fica limitada a 256×256 m e o formato de importação é só RAW de 16 bits (sem PNG).
- A textura é procedural (sem arte licenciada); não há texturas customizadas por usuário.
- Terreno não é editável fora de uma ilha conectada (nada seria persistido).
- Não foi testado com dois clientes reais nem contra o servidor em produção.

## Oceano

- A borda do mapa vira praia (faixa de 24 m com areia que desce até o fundo do mar a -8 m) e, além dela, há oceano aberto por mais 192 m até um limite invisível. O nível do mar é -0,8 m; o terreno plano fica em 0 m. Rebaixar o terreno abaixo do nível do mar cria lagos.
- A fileira de vértices da borda é o fundo do mar e não pode ser esculpida. Mapas salvos antes do oceano são normalizados ao carregar.
- Visual (`OceanSurface`): plano animado que acompanha a câmera até o horizonte, com ondas, normais animadas, cor por profundidade, espuma na costa, reflexo do céu e filtro azul quando a câmera fica debaixo d'água. A câmera sobe um pouco ao nadar para não cortar a superfície.
- Física (`AuthoritativeMovement`): o avatar nada quando a água sobre o chão tem pelo menos 1,1 m; flutua com a cabeça fora d'água, pula/espaço sobe, Ctrl desce até o fundo, Shift nada mais rápido (3,5 → 5 m/s). Voar sobre o mar não é nadar. O servidor também limita a posição ao limite do mundo.
- Rede: novo estado de movimento `Swim` para os outros jogadores.

Verificado: testes unitários (`OceanTests`) e `TerrainSmoke` no Godot (flutuar, mergulhar e voltar à superfície). Não verificado: dois clientes reais e o servidor em produção.

## Desfazer, nível do mar e mapas de altura (protocolo v3)

- **Desfazer/refazer** (Ctrl+Z, Ctrl+Y ou Ctrl+Shift+Z, ou os botões do painel): cada jogador tem sua própria pilha, mantida pelo servidor (até 50 pinceladas). Edições seguidas em menos de 0,7 s formam uma pincelada só, então um arrasto inteiro é desfeito de uma vez. O servidor devolve só os vértices alterados (`TerrainPatched`); remendos muito grandes viram o mapa completo. Desfazer restaura o estado anterior, inclusive sobre edições feitas por outra pessoa depois.
- **Nível do mar por ilha:** o painel tem um controle (de -7 a 20 m) e o botão "Aplicar nível do mar". O valor é salvo com o terreno, move o oceano, a natação, o mini-mapa e o som. Mapas salvos antes disso carregam com o nível padrão (-0,8 m).
- **Exportar mapa:** grava as alturas em `user://exports/heightmap-129x129-<data>.r16` (RAW de 16 bits, little-endian, linha a linha, de -8 m a 48 m). **Importar mapa:** lê um arquivo desse formato com o mesmo tamanho e o envia ao servidor, que valida o tamanho (máximo de 300 KB), a permissão e o limite de uma importação a cada 5 s. A importação também pode ser desfeita; as bordas (fundo do mar) não mudam.
- Novas mensagens: `UndoTerrain`, `RedoTerrain`, `SubmitSeaLevel`, `SubmitTerrainImport` (cliente → servidor) e `TerrainPatched`, `SeaLevelChanged` (servidor → cliente). A versão do protocolo é a **3**; um teste confere que `IslandEntry:ProtocolVersion` do `appsettings.json` da API acompanha a versão do protocolo e que as pontes de rede do cliente e do servidor têm os mesmos métodos.

## Ferramentas novas, pincel e terrenos (Fase 5 dos terrenos)

- **Ferramentas:** além de elevar, abaixar, nivelar-ao-ponto (Achatar), suavizar, rugosidade e pintar, existem **Nivelar** (a uma altura escolhida), **Restaurar** (volta ao relevo e às texturas da ilha original) e **Erosão** (deslizamento térmico: onde um vértice fica mais íngreme que 0,55 de inclinação sobre o vizinho mais baixo, metade do excesso desliza; material só muda de lugar).
- **Pincel:** redondo ou quadrado, borda de macia a dura (`BrushOptions`). Quando não há opções, o resultado é idêntico ao de antes (teste).
- **8 camadas de textura** (`TerrainMap.LayerCount`). Formato novo `LVT3`; mapas `LVT1` e `LVT2` carregam com as camadas novas zeradas. As camadas de 4 a 7 vão para o shader nas coordenadas UV e UV2 da malha.
- **Mensagem de edição:** `SubmitTerrainEdit(tool, x, z, radius, strength, layer)` — o inteiro `layer` leva também as opções do pincel (`BrushCodec`: bits 0-3 camada, 4 quadrado, 5-8 dureza, 9-25 altura do nível). Um número de 0 a 15 é só a camada, como antes.
- **Direitos por terreno:** a API envia ao servidor da ilha, junto com os acessos (`land-sync`), quem pode esculpir cada terreno (`LandTerrainRights`); o servidor aplica a pincelada só nos vértices permitidos e repassa o resultado como `TerrainPatched` quando a pincelada não é a comum. Mudar o nível do mar e importar mapas exige `edit_terrain` na ilha.
- Veja os detalhes e as limitações em [LAND.md](LAND.md).
