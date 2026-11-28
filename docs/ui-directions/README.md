# Direções de arte do HUD (protótipos, 27/11/2026)

> **Decidido em 27/11/2026: a Varanda, que já está no jogo** (`UI/Hud.cs`, `UI/HudSheet.cs`, `Theme.uss`). Na implementação entraram a versão noturna, a conversa que se recolhe, horários mais legíveis, as fontes livres Fraunces e Nunito Sans (licença OFL) e os ícones sólidos na barra. Esta página e os protótipos ficam como registro da escolha: o texto abaixo descreve o momento da comparação, e a verificação automática deles compara com o jogo de hoje (pode apontar entradas de menu novas, como "Mini-mapa").

Três propostas para o HUD principal, para o dono escolher antes do novo design system. **Nada do jogo mudou:** os protótipos ficam em `unity/UnityClient/Assets/LifeVerse/Prototypes/HudDirections/`, numa montagem própria que só compila no editor (`defineConstraints: UNITY_EDITOR`), com folhas de estilo próprias. Não usam nem alteram `Theme.uss`.

## Como comparar

- **Ao vivo:** abra o projeto na Unity e use o menu **LifeVerse › Protótipos de HUD (comparar)**. Escolha a proposta (ou teclas 1, 2, 3), o momento (Explorando, Conversando, Menu) e o fundo (Dia, Entardecer, Noite). O mouse em cima e o foco funcionam como desenhados; os botões não fazem nada.
- **Em fotos:** `artifacts/hud-directions/` (fora do git). `comparativo-*.png` empilha Horizonte, Meridiano e Varanda, nesta ordem.
- **Gerar de novo** (editor fechado):
  ```
  Unity.exe -projectPath unity/UnityClient -executeMethod LifeVerse.Unity.EditorTools.EditorChecks.Play -lifeverse-editor-run <saida.log> -lifeverse-hud-directions <pasta completa>
  ```
  Com `-lifeverse-hud-skip-world` os fundos já existentes são reaproveitados. A pasta precisa ser um caminho completo (um relativo cai dentro do projeto Unity).

O fundo é uma cena montada para a comparação (praça, quiosque, farol, cinco avatares), desenhada pelas classes de mundo e avatar do próprio jogo. Não é uma ilha da plataforma.

## As propostas

| | 1 · Horizonte | 2 · Meridiano | 3 · Varanda |
|---|---|---|---|
| Ideia | Silêncio: a interface é tipografia sobre o mundo | Instrumento de navegação | A varanda onde as pessoas se encontram |
| Composição | Quatro cantos e uma linha ("horizonte") embaixo, sem caixas | Fita de rumo no topo, trilho de ferramentas à esquerda, leituras em colunas à direita, radar | Placa do lugar com quem está, barra central com o próprio rosto, mapa cartão-postal, balões |
| Ações à vista | 8 na linha; o resto no Índice | 14 no trilho | 9 na barra (companhia de um lado, mundo do outro) |
| Menu completo | "Índice": página tipográfica sobre o mundo | "Comando": painel numerado com atalhos | "Tudo da ilha": folha acima da barra, com "Sua varanda" |
| Letra (protótipo) | Century Gothic | Bahnschrift, maiúsculas condensadas | Sitka (lugares) e Corbel (fala) |
| Ícones | Traço fino, abertos | Cantos cortados, com faceta de destaque | Sólidos arredondados, dois tons |
| Material | Texto e sombra; sombra suave nos cantos | Vidro escuro com cantos chanfrados, gelo e âmbar | Papel claro, madeira, terracota e verde-mar |
| Movimento (USS) | Sobe 2 a 4 px e surge, 140 a 180 ms | Etiquetas deslizam, 120 a 160 ms | Salto leve (ease-out-back), 160 a 220 ms |

Cada pasta de ícones, estilo e composição é separada (`Hud<Nome>.cs`, `Styles/<Nome>.uss`, `Glyph.cs`).

## Verificado

- 27 fotos: Explorando, Conversando e Menu em 1920×1080 e 1280×720; Explorando em 2560×1080 e ao entardecer; Conversando à noite. Escala: `min(l/1920, a/1080)` entre 0,85 e 2.
- Verificação automática (`Checks.cs`): **94 funções com lugar** em cada proposta; os 18 botões da barra e as 41 entradas dos menus conferidos contra `Hud.cs` e `GameApp.cs`; nada fora da tela, nenhum bloco sobreposto, nenhum texto cortado ou sem tamanho nas fotos.
  - "Com lugar" quer dizer que existe um elemento para a função em algum dos três momentos. **Não** quer dizer que funciona: os protótipos não chamam o jogo.
  - A verificação não vê tudo: um cruzamento da linha do Horizonte com a conversa em 720p só apareceu na revisão das fotos (corrigido).
- Janela de comparação: as 27 combinações abrem sem erro (`CompareWindow.SelfTest`). Não foi usada com o mouse.
- EditMode 240/240.

## Contraste medido

- **Horizonte (ponto fraco):** texto branco direto sobre o mundo, de dia, contra o fundo mais claro em volta: nome da ilha 3,6:1; estado 3,7:1; aviso 2,6:1; nome sobre pessoa 2,5:1; conversa 4,7:1; linha de ações 6,4:1. A sombra do texto ajuda, mas o alto da tela fica abaixo de 4,5:1.
- **Meridiano:** no vidro, texto 9,5:1 ou mais e secundário 5,8:1 ou mais (três amostras).
- **Varanda:** texto no papel 13,5:1; secundário 8,2:1; botões 5,1:1 ou mais (cores sólidas).

## Limites

- **Fontes:** são as instaladas no Windows desta máquina, lidas na hora. A licença não permite distribuí-las com o jogo: a proposta escolhida precisa de equivalentes livres (por exemplo Jost, Barlow, Fraunces com Nunito Sans), que ainda não foram baixados nem testados.
- Não redesenhados: menu radial, painel de desempenho, legendas, janelas e dicas além das mostradas.
- Variações desenhadas só como opção no menu: barra à esquerda/à direita, compacta e imersiva.
- Não visto em 1366×768, 2560×1440, 3840×2160 nem 32:9.
- Um tema claro (Varanda) pede atenção: no projeto em espaço linear, texto fino escuro sobre claro fica mais pálido; o menu usa negrito por isso.
- Animações existem no USS, mas não foram avaliadas em movimento.
