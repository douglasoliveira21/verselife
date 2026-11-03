# Editor de construção 3D

O modo Construir abre pelo botão **Construir** da barra inferior, por Conteúdo > Construir ou pela tecla `B`. O editor suporta cubo, esfera, cilindro, cone, plano, prisma, tubo, anel e pirâmide.

## Como usar

- **Selecionar:** clique em um objeto; Shift soma ou tira da seleção. Objetos agrupados são selecionados juntos.
- **Alças 3D** (constantes na tela e desenhadas por cima da cena): `1` Mover (setas X vermelho, Y verde, Z azul), `2` Girar (anéis) e `3` Escalar (cubos nos eixos). Arraste uma alça para mudar a seleção. Mover usa o snap do painel (metros) e girar usa passos de 5°. Com um objeto só, as alças de escala seguem os eixos locais dele; com vários, a escala é uniforme em torno do centro. `Esc` cancela um arrasto.
- **Copiar / colar / duplicar** (`Ctrl+C`, `Ctrl+V`, `Ctrl+D`): colar põe a seleção copiada onde o cursor aponta, apoiada no chão; duplicar põe a cópia ao lado. Grupos copiados viram grupos novos e mantêm a disposição.
- **Agrupar / desagrupar** (`Ctrl+G`, `Ctrl+Shift+G`): o grupo guarda um `group_id` comum; mover, girar ou escalar o grupo não o desfaz.
- **Excluir** (`Delete`), **desfazer** (`Ctrl+Z`) e **refazer** (`Ctrl+Y`). O histórico (60 passos) cobre mover, girar, escalar, material, agrupar, criar, colar, duplicar e excluir.
- O painel também edita nome, rotação em graus, cor, material por face e geometria (oco, afunilamento, torção) da seleção.

## Garantias do servidor

Cada alteração é enviada ao servidor da ilha com a revisão do objeto (`expectedRevision`); o servidor valida permissão, limites (escala `0.01..64`, cota de objetos, 20 operações por segundo por conexão) e responde com o estado oficial, que é replicado a todos. Se outra pessoa alterou o objeto antes, a edição é recusada (conflito de revisão) e o objeto volta ao estado oficial. Desfazer envia a alteração inversa com a revisão atual, então também está sujeito à autorização e aos conflitos. As operações ficam em `world.build_operations`.

Materiais aceitam cor, transparência, emissão, rugosidade e metallic; a física aceita colisão, trigger e camadas.

## Arquitetura

- `BuildEditor` (cliente): seleção, histórico, copiar/colar, agrupamento e envio das operações.
- `BuildGizmo` (cliente): alças 3D com camada de física própria; arrastar usa `GizmoMath`.
- `shared/Building/GizmoMath.cs`: geometria dos arrastos (ponto mais próximo do eixo, plano de rotação, ângulo, snap), testada sem a engine.

## Limitações

- Não há seleção por retângulo, pivô personalizado nem alças em eixos locais para mover/girar (só para escalar um objeto).
- Desfazer pode ser recusado se outra pessoa mexeu no mesmo objeto depois.
- Texturas remotas, UV e simulação física dinâmica ainda não existem.
- Verificado por testes unitários da geometria e por um teste no Godot com um "servidor" em memória que impõe revisões; não testado com dois jogadores reais.
