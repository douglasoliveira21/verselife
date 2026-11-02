# Comunicação e sistema social — Fase 08

`/api/v1/social/messages` suporta canais `local`, `global`, `private` e `group`, com rate limit, limpeza de caracteres de controle, limite de 2.000 caracteres e bloqueios persistentes. Amizades usam estados `pending`/`accepted`; grupos possuem proprietário, administradores, moderadores, membros e convites.

`POST /api/v1/voice/rooms/{roomName}/token` emite JWT temporário de cinco minutos, limitado ao usuário autenticado e à sala. A integração LiveKit usa esse token para publicar/assinar; push-to-talk e atenuação espacial ficam no cliente.

Rádios são vinculados a ilhas e proprietários. Somente HTTPS público é aceito; localhost, loopback e faixas privadas são rejeitados. O servidor não baixa nem retransmite conteúdo protegido.

Denúncias são persistidas com status `open`; bloqueios e rate limiting ficam no servidor. Retenção automatizada e ações administrativas globais devem ser ligadas ao painel de moderação da implantação.
