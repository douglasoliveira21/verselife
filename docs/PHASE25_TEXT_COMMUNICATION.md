# Fase 25 — Comunicação textual avançada

## Entregue

- Mensagens paginadas para global, local, privado e grupo.
- Idempotência por `ClientMessageId`, evitando duplicatas em reconexões/retries.
- Marcação de mensagens privadas como lidas.
- Presença online, offline, ausente, ocupado e invisível, com preferência de privacidade.
- Rate limit existente mantido para proteção contra flood.
- Bloqueio de usuários aplicado ao envio privado.
- Migration `Phase25TextCommunication` para presença, privacidade, chave idempotente e leitura.

## Limitações conhecidas

- Indicador de digitação, notificações push, emojis renderizados e layout completo de conversas ainda dependem do cliente Godot.
- O status invisível é aplicado na consulta de presença; não há ainda um serviço de presença distribuído entre instâncias.
