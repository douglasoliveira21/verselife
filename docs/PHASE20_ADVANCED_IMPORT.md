# Fase 20 — Importação avançada

## Implementado

- Limites de upload e validação de assinatura para GLB/glTF, PNG/JPEG/WebP e OGG/WAV.
- Bloqueio de scripts embutidos, URLs externas, esquemas `file://` e caminhos relativos inseguros em modelos.
- Limite de complexidade aproximado para glTF e metadados de inspeção versionados em `asset_versions.validation_json`.
- Deduplicação por SHA-256, armazenamento pelo hash e reuso do asset no inventário.
- Sanitização do nome do arquivo e registro da origem/moderação pendente em `license_json`.
- Metadados de dimensão de imagem, formato, preview e flags de mipmaps/thumbnail; para áudio, flags de preview, loop e espacialidade.

## Limitações honestas

- A geração real de thumbnails, conversão/compressão, mipmaps, retargeting de animações e processamento em worker isolado ainda dependem de um processador de assets dedicado; nesta fase a API somente valida e armazena o conteúdo original.
- Upload multipart ainda é atômico (sem retomada por partes); cancelamento/retry devem ser tratados pelo cliente e pelo proxy.
- A inspeção de geometria é defensiva e baseada na estrutura glTF; não substitui uma ferramenta completa de análise de meshes.

## Verificação

O backend foi compilado após as alterações. Arquivos inválidos, excessivamente grandes, com referências externas ou scripts embutidos devem receber erro de validação antes do armazenamento.
