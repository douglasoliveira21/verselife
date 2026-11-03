# Inventário e importação de assets — Fase 06

O inventário separa quatro conceitos: `inventory.assets` (identidade/licença), `inventory.asset_versions` (hash e conteúdo), `inventory.items` (referências do usuário) e `world.placed_assets` (instâncias colocadas). Uma cópia autorizada cria apenas uma nova referência ao mesmo asset/version; não duplica bytes.

Uploads autenticados aceitam GLB/glTF, PNG/JPEG/WebP e OGG/WAV. O pipeline valida extensão, assinatura, tamanho, resolução, headers, complexidade textual de glTF e rejeita scripts/Javascript embutidos. O hash SHA-256 deduplica conteúdo. PostgreSQL guarda metadados, permissões e licenciamento; `AssetStorageService` grava no volume de desenvolvimento configurado e é a fronteira para o bucket MinIO em produção.

O inventário oferece busca, categoria, pastas, renomear/mover, lixeira/restauração, cópia com flag `copy`, download autorizado, equipamento em slots e colocação em ilha com permissão de construção. O cliente abre o painel dentro da cena World, seleciona arquivos com FileDialog e envia multipart autenticado.

As flags de asset (`copy`, `modify`, `transfer`, `use`, `distribute`) são persistidas no servidor; o cliente não consegue elevar permissões. Equipamentos armazenam uma referência `asset:{uuid}` na tabela existente de equipamentos do avatar.

Limitações conhecidas: a conversão/mipmap/thumbnail e processamento isolado completo de mídia são pontos de extensão do adaptador de storage; o fallback local mantém o pipeline executável no ambiente atual sem um SDK S3 adicional. Docker/MinIO real e downloads interrompidos ainda requerem execução de integração.

## Modelos 3D no mundo, miniaturas e arrastar e soltar

- **Modelos 3D no mundo:** um modelo importado (GLB/glTF) vira um objeto do tipo `mesh`, cuja geometria guarda o `assetId` (`{"assetId":"..."}`). Por ser um objeto comum, herda tudo do editor de construção: alças de mover/girar/escalar, grupos, copiar e colar, desfazer, scripts Lua, permissões, revisões e replicação. A API só aceita um `mesh` cujo modelo exista, seja `model/*` e pertença ao autor ou esteja no inventário dele (editar mantendo o mesmo modelo é sempre permitido).
- **Como aparece:** quem pode entrar na ilha baixa o modelo em `GET /api/v1/worlds/islands/{id}/assets/{assetId}/file` (só serve modelos usados por um `mesh` daquela ilha). O cliente (`ModelCache`) baixa cada modelo uma vez, ajusta para que o lado maior tenha 1 m e o centro fique na origem (a escala do objeto define o tamanho real; ao colocar começa com 2 m), cria uma caixa de colisão do tamanho do modelo e mostra uma caixa translúcida enquanto baixa (vermelha se o modelo não estiver disponível). Arquivos glTF que apontam para arquivos externos não carregam.
- **Colocar:** botão **Colocar** (na frente da câmera) ou arrastar o item do inventário para o mundo. Só modelos 3D podem ser colocados; o objeto fica selecionado com as alças de mover ligadas.
- **Miniaturas reais** (`ThumbnailLoader`): imagens (até 4 MB) são baixadas e reduzidas, modelos (até 12 MB) são renderizados uma vez num pequeno viewport, e tudo fica em cache em disco por hash de conteúdo (`user://thumbs`). Sons e outros tipos recebem um ícone gerado.
- **Pastas e arrastar e soltar:** o inventário mostra pastas com contagem; arraste um item para uma pasta (ou para fora das pastas) para movê-lo; há botões para criar e excluir pastas (excluir devolve os itens para fora das pastas). A lista de itens agora traz `folderId`, `contentHash` e `mimeType`.
- **Importar:** a categoria é escolhida pela extensão quando o filtro está em "Todos" (glb/gltf → modelos, imagens → texturas, ogg/wav → sons).

### Limitações

- Modelos não têm animação nem esqueleto no mundo; a colisão é uma caixa, não a malha.
- O inventário lista até 100 itens por vez e as pastas não se aninham na interface.
- A conversão de mídia e o armazenamento continuam no disco local da API (sem MinIO).
- Verificado por testes unitários da validação e por um teste no Godot com um GLB gerado em memória; não testado com a API real nem com vários jogadores.

## Construções salvas e limites de modelos (Etapa 4 do editor)

- Uma construção salva é um item de inventário da categoria `packages` (tipo `application/vnd.lifeverse.build+json`), guardado pelo mesmo armazenamento dos outros itens. Veja `docs/BUILD_EDITOR.md`.
- O upload de modelos 3D agora lê a parte JSON do GLB/glTF (`GltfLimits`) e recusa arquivos danificados, que não sejam glTF 2.0, sem triângulos ou acima dos limites: 300 mil triângulos, 64 materiais, 32 texturas, 5 mil partes. Um GLB que é só o cabeçalho deixou de ser aceito.
