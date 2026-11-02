# Fase 21 — Scripting avançado

## Entregue

- Eventos versionados adicionais: toque, colisão, avatar, inventário, permissões, reset e reinício de região, mantendo aliases legados.
- Mensageria entre objetos autorizados com canais privados, payload JSON limitado, fila máxima de 100 mensagens e expiração de 2 minutos.
- Persistência de estado com validação de tipos simples, profundidade máxima, quantidade limitada de campos e quota de 8 KiB por valor.
- Sandbox Lua continua executando em processo separado, com ambiente limpo, timeout, orçamento de instruções, limite de memória, rate limit e quarentena após timeout.
- Editor Godot atualizado para selecionar os novos eventos.

## Limitações conhecidas

- A fila de mensagens é em memória por instância; mensagens não entregues durante uma reinicialização não são recuperadas.
- Histórico de versões do código-fonte ainda é representado pelo campo `Version` do script, sem endpoint de restauração de versões antigas.
- Autocomplete semântico, retargeting de API e integração completa com eventos do servidor headless ainda exigem o dispatcher de eventos da ilha.

## Verificação

Backend e cliente foram compilados sem erros ou avisos. O acesso do script ao sistema operacional, rede, arquivos e APIs perigosas continua bloqueado pelo validador e pelo processo worker isolado.
