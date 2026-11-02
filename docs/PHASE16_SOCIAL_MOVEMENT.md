# Fase 16 — Movimento e animações sociais

Implementado:

- Aceleração e parada gradual em caminhada, corrida, voo e natação.
- Ocupação exclusiva de assentos no cliente, alinhamento ao `SeatAnchor`, saída pelo `ExitAnchor` e liberação no encerramento da cena.
- Estado sentado replicado pelo protocolo autoritativo com alternância segura e velocidade zerada no servidor.
- Gestos locais: acenar, dançar, aplaudir, apontar, cumprimentar, rir e sentar no chão.
- Painel de gestos criado no HUD; acenar e dançar continuam usando os flags de rede existentes.
- Sistema de `AnimationOverride` com duração e prioridade para interações interromperem movimento visualmente.
- Proteção contra assento removido durante a animação.

Limitações conhecidas:

- O protocolo atual só possui estados de rede para `Wave`, `Dance` e `Sit`; os demais gestos são locais até a próxima versão de protocolo.
- Pontos de assento programáveis por Lua ainda precisam ser expostos pela API de objetos.
- Retargeting de animações importadas depende de um rig compatível e ainda não é automático.
