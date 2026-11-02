# Fase 26 — Áudio e mídia avançados

## Entregue

- Preferências persistentes de voz para push-to-talk, ativação por voz, sensibilidade, volume e dispositivos de entrada/saída.
- Tokens de voz com nomes de sala validados; salas `island:{uuid}` verificam acesso à ilha antes da emissão.
- Sinalização de áudio espacial habilitada no contrato do token, deixando atenuação 3D para o cliente/LiveKit.
- Rádios continuam com HTTPS obrigatório, faixa/volume limitados e alteração permitida ao proprietário ou administrador/editor da ilha.
- Validação SSRF ampliada para bloquear localhost, serviços internos, domínios `.local`/`.internal` e redes privadas.
- Migration `Phase26AudioMedia` para preferências de voz e privacidade.

## Vídeo

Reprodução de vídeo não foi simulada nesta fase. Não existe player ou pipeline de transcodificação no cliente; a funcionalidade permanece indisponível até haver um serviço de mídia autorizado, limites de banda e validação de codecs.

## Limitações conhecidas

- Seleção real de dispositivos, teste de microfone, VAD e reconexão precisam ser conectados ao SDK LiveKit no cliente.
- Atenuação espacial e indicador de fala dependem do mixer/renderer do cliente.
