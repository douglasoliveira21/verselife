# Fase 14 — Avatar avançado

Implementado sobre o rig humanoide placeholder existente.

## Suporte atual

- Cor de olhos, cabelo e pele.
- Estilos de cabelo `short`, `bob`, `mohawk`, `long`, `ponytail` e `bald`.
- Barba `none`, `stubble`, `short` e `full`.
- Proporções limitadas por servidor (`height` 0,85–1,15 e `shoulder` 0,90–1,10).
- Pré-visualização 3D com rotação e zoom.
- Desfazer a edição corrente e restaurar o último estado salvo.
- Slots de roupa existentes e dados versionáveis para outfits e attachments.
- Attachments replicados como identificadores e offsets lógicos; o cliente limita a oito itens ativos.
- Aparência enviada aos servidores de ilha por `AppearanceJson`, sem transmissão de meshes.

## Persistência

A migration `20261102204215_Phase14AdvancedAvatar` adiciona campos JSON para rosto, outfits e attachments em `identity.avatar_appearances`. A API valida tamanho e JSON antes de salvar. O startup da API aplica migrations pendentes.

## Limitações conhecidas

Os modelos atuais são placeholders originais, sem morph targets de rosto ou roupas skinned de produção. Os controles de rosto avançado só devem ser expandidos quando um asset com morph targets e rig compatível for importado.
