# Fase 30 — Ambientação e mundo

## Entregue

- O cliente já possui `IslandEnvironmentController` com ciclo dia/noite, rotação solar e interpolação de iluminação ambiente.
- Clima configurável por ambiente (`clear`, `cloudy`, `rain`, `storm`, `fog`) com densidade de neblina interpolada para evitar mudanças bruscas.
- Hora inicial pode ser configurada por `LIFEVERSE_ISLAND_HOUR`; clima por `LIFEVERSE_WEATHER`.
- Água e transição de natação já estão presentes na cena `World.tscn` por `WaterVolume`.
- Configurações de ilha continuam persistidas em `EnvironmentJson` e passam pela API autenticada de mundos.

## Limitações conhecidas

- Vegetação procedural, LOD avançado, reflexos de água e áudio ambiente por superfície ainda exigem assets/streams licenciados e profiling de GPU.
- Chuva, vento e tempestade têm nesta etapa representação ambiental (fog/lighting); partículas e sons dedicados ainda não foram conectados.
- Medição de FPS/CPU/GPU em uma ilha tropical completa requer execução manual com assets reais.
