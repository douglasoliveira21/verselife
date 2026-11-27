# Publicação e distribuição — Fase 12

## Build

O cliente é empacotado por `scripts/build-unity-client.ps1` (ver `RELEASES.md`). `scripts/generate-manifest.ps1` produz um manifesto SHA-256 para arquivos distribuídos. O launcher PowerShell em `launcher/LifeVerse.Launcher.ps1` baixa somente por HTTPS, retoma por arquivo parcial, verifica hash e troca arquivos de forma atômica; ele ainda não foi adaptado ao pacote Unity.

O instalador Windows está preparado em `installer/LifeVerse.iss` para Inno Setup, com diretório configurável, atalhos, desinstalação e preservação de configurações em `%LOCALAPPDATA%`.

## Critério de lançamento

Esta fase não declara prontidão pública sem servidor oficial, testes de instalação/atualização e carga executados. O ambiente atual não possui Docker/servidores oficiais, portanto esses itens permanecem bloqueados e devem ser executados no pipeline de release autorizado.
