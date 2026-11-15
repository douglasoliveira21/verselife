# Qualidade gráfica

O cliente usa o renderizador **Forward+** do Godot. Antes só havia céu, névoa e uma luz com sombras padrão; agora há **predefinições de qualidade** em Configurações → "Gráficos, controles e som…" → *Qualidade gráfica*.

| Nível | Antialiasing | Oclusão ambiente | Luz indireta | Brilho | Cor | Sombras do sol |
|---|---|---|---|---|---|---|
| **Baixo** | nenhum | — | — | — | — | 2 cascatas, 55 m, duras |
| **Médio** | MSAA 2x | baixa | — | leve | contraste +4%, saturação +6% | 2 cascatas, 90 m, suaves |
| **Alto** | MSAA 4x | média | — | sim | +6% / +10% | 4 cascatas, 140 m, suaves |
| **Ultra** | MSAA 4x | alta | sim | sim | +8% / +12% | 4 cascatas, 240 m, muito suaves |
| **Automático** (padrão) | placa dedicada → Alto, integrada → Baixo, outra → Médio | | | | | |

- Código: `client/Scripts/GraphicsQuality.cs` (predefinições e regras), aplicado pelo `IslandEnvironmentController` ao `WorldEnvironment`, ao `Sun` e à viewport; a escolha é salva em `user://settings.cfg` (`[graphics] quality`) e vale na hora, sem reiniciar.
- Já existiam e não mudaram: o céu com shader (sol, nuvens), a névoa por clima e a água do oceano (cor por profundidade, espuma na costa, brilho do sol).
- **Não feito:** névoa volumétrica (raios de sol), reflexos de tela na água, iluminação global (SDFGI), vento na vegetação, FSR/escala de resolução 3D, melhorias de material do terreno e do avatar.

## Medição

`Tests/GraphicsBenchmark.tscn` roda cada nível por 3 s e salva uma captura (`LIFEVERSE_CAPTURE_PATH`). Em 1920×1080, RTX 2060 Super, mundo de teste vazio (V-Sync desligado):

| Nível | FPS |
|---|---|
| Baixo | ~640 |
| Médio | ~490 |
| Alto | ~310 |
| Ultra | ~205 |

É um mundo quase vazio: serve para comparar o custo relativo dos níveis, não para prever uma ilha cheia (onde objetos, luzes e scripts pesam mais que os efeitos de tela).

Teste automático: `GraphicsQualitySmoke`.

**Por que não há TAA nem FXAA:** os dois borram o que se move como um sprite plano, como o nome sobre o avatar (relato: o nome ficava borrado ao andar nos níveis Alto e Ultra). Os níveis usam só MSAA.
