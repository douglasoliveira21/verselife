# Veículos (protocolo v5)

Os veículos agora existem de verdade no servidor da ilha: ele simula o carro a cada tick, o motorista fica preso ao banco e todos os jogadores veem o mesmo carro. O cliente só mostra e envia teclas.

## Como jogar

- Chegue a até 4 m de um carro e aperte **E** (a mesma tecla de sentar). Se houver uma cadeira na mira, ela tem prioridade.
- **W/S** acelera e dá ré, **A/D** esterça, **Espaço** freia, **E** sai do carro (você desce ao lado dele).
- Um carro vazio freia sozinho. Se o motorista desconectar, o banco é liberado.
- Mensagens: "Chegue mais perto do veículo", "Alguém já está dirigindo este veículo", "Você já está em um veículo".

## Onde os veículos vêm

Os carros são os da tabela `world.vehicles` (criados por `POST /api/v1/worlds/islands/{id}/vehicles`, só dono ou quem pode construir). A ilha consulta `GET /api/v1/platform/servers/{instância}/vehicles` (chave de serviço) ao iniciar e a cada 15 s, e avisa os jogadores sobre os novos. No máximo 64 por ilha.

## Física (`shared/Vehicles/VehiclePhysics.cs`)

Carro de arcade: aceleração limitada pela velocidade máxima (3 a 60 m/s, padrão 20), ré a 35 % da máxima, freio, resistência de rolamento e arrasto, esterço mais suave em velocidade (modelo de bicicleta), subidas freiam e descidas aceleram, o carro acompanha o terreno e inclina com ele. Água funda (mais de 0,9 m) e a borda do mundo param o carro. Entradas inválidas (NaN, valores enormes) são limitadas pelo servidor.

## Rede (v5)

| RPC | Direção | Canal |
|---|---|---|
| `EnterVehicle(id)`, `ExitVehicle()` | cliente → servidor | confiável |
| `SubmitVehicleInput(throttle, steer, brake)` | cliente → servidor | não confiável, ordenado |
| `VehicleSpawned(id, nome, tipo, x, y, z, yaw)`, `VehicleRejected(código)` | servidor → cliente | confiável |
| `VehicleSnapshots(bytes)` | servidor → cliente | não confiável, ordenado |

O snapshot (`VehicleCodec`) leva posição em float, ângulos em 16 bits, velocidade e o motorista de cada carro. O motorista aparece para os outros jogadores sentado e virado para onde o carro aponta. `IslandEntry:ProtocolVersion` do backend passou para **5**: publique API e servidor de ilhas juntos.

## Testes

- `VehicleTests` (31 casos): física, regras da frota (alcance, um motorista por carro, um carro por jogador, saída, desconexão) e codec.
- `client/Tests/VehicleSmoke` (em `scripts/godot-smoke.sh`): o cliente cria, move e remove os carros.
- `client/Tests/VehicleE2E`: roda contra uma pilha real (API + Postgres + supervisor + ilha). Entra na ilha pelo fluxo normal, entra no carro, dirige, desce, volta a entrar e desce de novo.
  Variáveis: `LIFEVERSE_API_URL`, `LIFEVERSE_E2E_TOKEN`, `LIFEVERSE_E2E_ISLAND`.

Esse teste achou um bug no fluxo de ilha sob demanda: logo depois que a ilha registra e antes do primeiro "saudável", a API responde 503 com `state: "running"`, e o cliente só tentava de novo para `"starting"`. Corrigido.

## Limites conhecidos

- Os carros não têm colisão com objetos, outros carros ou jogadores (atravessam), e só há um assento (motorista).
- A posição do carro não é salva: ao reiniciar a ilha os carros voltam para onde foram criados.
- Sem previsão no cliente: o carro do motorista segue o servidor, então com latência alta há um pequeno atraso na direção.
- Os endpoints REST antigos de veículos (`/enter`, `/exit`, `/input`) continuam existindo, mas não controlam o carro da ilha.
