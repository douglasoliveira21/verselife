# Inventário e importação de assets — Fase 06

O inventário separa quatro conceitos: `inventory.assets` (identidade/licença), `inventory.asset_versions` (hash e conteúdo), `inventory.items` (referências do usuário) e `world.placed_assets` (instâncias colocadas). Uma cópia autorizada cria apenas uma nova referência ao mesmo asset/version; não duplica bytes.

Uploads autenticados aceitam GLB/glTF, PNG/JPEG/WebP e OGG/WAV. O pipeline valida extensão, assinatura, tamanho, resolução, headers, complexidade textual de glTF e rejeita scripts/Javascript embutidos. O hash SHA-256 deduplica conteúdo. PostgreSQL guarda metadados, permissões e licenciamento; `AssetStorageService` grava no volume de desenvolvimento configurado e é a fronteira para o bucket MinIO em produção.

O inventário oferece busca, categoria, pastas, renomear/mover, lixeira/restauração, cópia com flag `copy`, download autorizado, equipamento em slots e colocação em ilha com permissão de construção. O cliente abre o painel dentro da cena World, seleciona arquivos com FileDialog e envia multipart autenticado.

As flags de asset (`copy`, `modify`, `transfer`, `use`, `distribute`) são persistidas no servidor; o cliente não consegue elevar permissões. Equipamentos armazenam uma referência `asset:{uuid}` na tabela existente de equipamentos do avatar.

Limitações conhecidas: a conversão/mipmap/thumbnail e processamento isolado completo de mídia são pontos de extensão do adaptador de storage; o fallback local mantém o pipeline executável no ambiente atual sem um SDK S3 adicional. Docker/MinIO real e downloads interrompidos ainda requerem execução de integração.
