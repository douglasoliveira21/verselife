# Editor de construção — protótipos da Fase 2 (para aprovação)

Feitos em UI Toolkit, dentro da Unity, em 28/11/2026. **A interface do jogo não mudou** e o `Theme.uss` não foi alterado: o protótipo fica em `unity/UnityClient/Assets/LifeVerse/Prototypes/BuildEditor/` e só existe no editor (não entra no jogo gerado).

**Nada aqui funciona ainda:** os controles não respondem, os valores são de exemplo e os manipuladores são desenhos sobre a cena. A cena do fundo é desenhada pelo próprio mundo do jogo (terreno, mar, céu e formas), montada para o protótipo; não é uma ilha da plataforma.

As posições vêm direto do [mapa de coordenadas](../build-editor-coordinate-map.json): a janela e o documento não têm como divergir.

## Capturas (1920 × 1080)

| Nº | Estado | Captura | Comparativo |
|--:|---|---|---|
| 1 | Foco | [captura](01-foco.jpg) | [lado a lado](comparativos/01-foco.png) |
| 2 | Mover | [captura](02-mover.jpg) | [lado a lado](comparativos/02-mover.png) |
| 3 | Editar: posição | [captura](03-editar-posicao.jpg) | [lado a lado](comparativos/03-editar-posicao.png) |
| 4 | Editar: rotação | [captura](04-editar-rotacao.jpg) | [lado a lado](comparativos/04-editar-rotacao.png) |
| 5 | Editar: escala | [captura](05-editar-escala.jpg) | [lado a lado](comparativos/05-editar-escala.png) |
| 6 | Editar: face | [captura](06-editar-face.jpg) | [lado a lado](comparativos/06-editar-face.png) |
| 7 | Criar | [captura](07-criar.jpg) | [lado a lado](comparativos/07-criar.png) |
| 8 | Terreno | [captura](08-terreno.jpg) | [lado a lado](comparativos/08-terreno.png) |
| 9 | Aba Geral | [captura](09-aba-geral.jpg) | [lado a lado](comparativos/09-aba-geral.png) |
| 10 | Aba Objeto (toro) | [captura](10-aba-objeto.jpg) | [lado a lado](comparativos/10-aba-objeto.png) |
| 11 | Aba Recursos | [captura](11-aba-recursos.jpg) | [lado a lado](comparativos/11-aba-recursos.png) |
| 12 | Textura clássica | [captura](12-textura-classica.jpg) | [lado a lado](comparativos/12-textura-classica.png) |
| 13 | Textura PBR | [captura](13-textura-pbr.jpg) | [lado a lado](comparativos/13-textura-pbr.png) |
| 14 | Aba Conteúdo | [captura](14-conteudo.jpg) | [lado a lado](comparativos/14-conteudo.png) |
| 15 | Opções da grade | [captura](15-grade.jpg) | [lado a lado](comparativos/15-grade.png) |
| 16 | Mídia | [captura](16-midia.jpg) | [lado a lado](comparativos/16-midia.png) |
| 17 | Seletor de textura | [captura](17-seletor-textura.jpg) | [lado a lado](comparativos/17-seletor-textura.png) |
| 18 | Seletor de cor | [captura](18-seletor-cor.jpg) | [lado a lado](comparativos/18-seletor-cor.png) |
| 19 | Seleção múltipla | [captura](19-selecao-multipla.jpg) | [lado a lado](comparativos/19-selecao-multipla.png) |
| 20 | Partes vinculadas | [captura](20-vinculadas.jpg) | [lado a lado](comparativos/20-vinculadas.png) |

Os estados 3, 9 e 12 também em 1280 × 720, 1366 × 768, 2560 × 1440, 3840 × 2160, 2560 × 1080 e 5120 × 1440, na pasta [resolucoes](resolucoes/).

## Sobre os comparativos

À esquerda **não há uma captura do Second Life**: é a janela original **reconstruída pelas coordenadas dos XML**, em cinza e com os textos em inglês. O que o visualizador desenha por código (o quadrado de cores, as prévias, os ícones dos botões) não aparece ali. Para comparar com o programa de verdade faltam capturas do visualizador atual em tamanho real.

## O que muda em relação ao original

| Adaptação | Motivo |
|---|---|
| Janela de 428 × 954 (escala 1,45), em (50, 70) | proporção original (proposta 1) |
| Sem faixa de título na janela de construção; minimizar e fechar ao lado dos cinco modos | como no visualizador, onde os modos ficam no topo |
| Caixas de marcar com 22 px, opções com 21, campos numéricos de 23 a 28, listas de 25 a 30, textos com 19 ou mais | leitura; cresce em torno do centro visual do original |
| Amostras de cor e miniaturas quadradas | no original a parte de baixo do retângulo é da legenda, onde cai o componente seguinte |
| Campo do preço mais largo | "VC$" é maior que "L$" |
| Textos curtos em português: "Grade", "Editar partes", "Dos dois lados", I/F no lugar de B/E | as linhas originais são estreitas |
| Letras X, Y e Z coloridas; Z é a altura | proposta 2 |
| Ícones do LifeVerse nos modos; 15 ícones de forma desenhados | identidade |
| Itens que pedem mudança de banco, servidor ou protocolo aparecem apagados, com um ponto cor de barro | proposta 5 (lista abaixo) |
| Quadrado de cores, cores guardadas e prévia de textura | o original desenha por código: posição aproximada |

**Indisponíveis:** "Exibir na busca"; "Aplicar" no terreno; tetraedro, semicilindro e semicone; formato do oco; gravidade e densidade físicas; "Nova nota"; material PBR como item (escolher do inventário continua); mapa especular; editor externo.

## Telas estreitas

- Até 1920 × 1080 a janela inteira cabe quando há altura; com pouca altura, o que está sob as abas rola e os modos, as opções e as abas ficam parados.
- Em 1280 × 720 e 1366 × 768 a barra de baixo do jogo chegaria sob a janela: **a janela termina acima dela** (731 e 788 de 954).

## Para decidir antes da Fase 3

1. **Chat e cartão da ilha:** a janela passa por cima dos dois (o chat fica embaixo, à esquerda). Proposta: ao abrir a construção, o chat se minimiza e volta ao fechar. As capturas não mostram a interface do jogo; essa medida saiu de uma foto real dela em 1920 × 1080.
2. Se os rótulos curtos em português estão bons.
3. Se os itens indisponíveis devem aparecer (como aqui) ou ser escondidos até existirem.

## O que não está nos 20 estados

45 componentes condicionais não aparecem em nenhum deles: canais de relevo e especular, campos de forma esculpida, "côncavo" da esfera, avisos de carregamento, máscaras de depuração e atributos de navegação (lista em [relatorio.txt](relatorio.txt)). As janelas "mais informações" (pesos) e "permissões em lote" estão no mapa e não foram prototipadas: não estavam entre os 20.

## Como refazer

```text
Unity.exe -projectPath unity/UnityClient -executeMethod LifeVerse.Unity.EditorTools.EditorChecks.Play -lifeverse-editor-run saida.log -lifeverse-build-prototypes <pasta>
```

Com a janela do editor aberta (sem `-batchmode`). O relatório aponta componentes fora da janela, sobrepostos, cortados pela rolagem e textos cortados.
