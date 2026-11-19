# Comparação de física: Godot x Unity (PhysX)

Mede os mesmos cenários nos dois motores, com os parâmetros de `island-server/Scripts/ServerPhysics.cs` (corpo rígido, amortecimento linear .05 e angular .6, material), chão plano em y = 0, 60 Hz, bola de raio 0,5 ou caixa de 1 m.

| Cenário | O que mede |
|---|---|
| `drop` | bola solta de 4,5 m: instante e velocidade do impacto, onde repousa, quando dorme |
| `bounce` | bola com quique 1,0 solta de 5,5 m: altura do pico depois do primeiro impacto |
| `on_box` | bola cai sobre uma caixa fixa de 1 m |
| `box_drop` | caixa física solta de 3,5 m |
| `kick` | bola parada recebe um impulso de 3,6 N·s (o de quem anda a 4 m/s): distância rolada |

```powershell
# Godot (projeto separado, sem tocar no island-server)
Godot_v4.7.2-stable_mono_win64_console.exe --headless --path unity\PhysicsComparison

# Unity (player Windows construído com BuildClient.BuildWindows)
unity\UnityClient\Builds\Windows\LifeVerse.exe -batchmode -nographics -lifeverse-physics-report -logFile -
```

Resultados e leitura: `docs/unity-migration/MIGRATION_ISLAND_SERVER.md`, seção "Física de objetos".
