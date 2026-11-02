# Publicação e distribuição — Fase 12

## Build

`scripts/build-release.ps1` compila a solução em Release e gera artefatos versionados em `artifacts/<versão>`. `scripts/generate-manifest.ps1` produz manifesto SHA-256 para arquivos distribuídos. O launcher PowerShell em `launcher/LifeVerse.Launcher.ps1` baixa somente por HTTPS, retoma por arquivo parcial, verifica hash e troca arquivos de forma atômica.

O instalador Windows está preparado em `installer/LifeVerse.iss` para Inno Setup, com diretório configurável, atalhos, desinstalação e preservação de configurações em `%LOCALAPPDATA%`.

## Configurações e medições

O cliente persiste resolução, janela/tela cheia, V-Sync, limite de FPS, escala de UI, sensibilidade e volume em `user://settings.cfg`. `LIFEVERSE_SHOW_METRICS=1` habilita overlay de FPS, frame time, RAM e contagem de nós. GPU/VRAM/draw calls devem ser capturados com o profiler Godot/Windows GPUView durante o beta; não são inferidos.

## Critério de lançamento

Esta fase não declara prontidão pública sem export template Windows, servidor oficial, testes de instalação/atualização e carga executados. O ambiente atual não possui Docker/servidores oficiais, portanto esses itens permanecem bloqueados e devem ser executados no pipeline de release autorizado.
