# Comparação de física: Godot x Unity (PhysX)

Mede os mesmos cenários nos dois motores, com os parâmetros de `island-server/Scripts/ServerPhysics.cs` (corpo rígido, amortecimento linear .05 e angular .6, material), chão plano em y = 0, 60 Hz, bola de raio 0,5 ou caixa de 1 m.

| Cenário | O que mede |
|---|---|
| `drop`, `box_drop`, `on_box` | queda de bola e de caixa, repouso e quando dormem |
| `bounce`, `material_bounce_*`, `ball_on_box_bounce` | altura do quique, por material e entre dois corpos |
| `kick` | bola parada recebe o impulso de quem anda: distância rolada |
| `stack`, `tilted`, `spin` | pilha de cinco caixas, caixa que cai torta, caixa girando |
| `tunnel_wall`, `tunnel_ground` | bola pequena a 80 m/s contra parede fina e contra o terreno |
| `collide`, `collide_heavy` | bola contra bola e contra caixa pesada |
| `slide_*` | caixa lançada a 6 m/s, com o atrito de cada material |
| `slope_box_*`, `slope_ball_*` | caixa grande e bola em terreno inclinado a 10°, 25° e 35° |
| `crowd` | 200 corpos: quantos dormem, se algum atravessa o chão e o custo por passo |

Os cenários e as medidas estão em `main.gd` (Godot) e em `UnityClient/Assets/LifeVerse/Server/PhysicsComparisonReport.cs`.

```powershell
# Godot (projeto separado, sem tocar no island-server)
Godot_v4.7.2-stable_mono_win64_console.exe --headless --path unity\PhysicsComparison

# Unity (no editor; no player gerado também serve: LifeVerse.exe -batchmode -nographics -lifeverse-physics-report)
Unity.exe -batchmode -nographics -projectPath unity\UnityClient -executeMethod LifeVerse.Unity.EditorTools.EditorChecks.Play -lifeverse-editor-run saida.txt -lifeverse-physics-report
```

Resultados e leitura: `docs/unity-migration/MIGRATION_ISLAND_SERVER.md`, seção "Física de objetos".
