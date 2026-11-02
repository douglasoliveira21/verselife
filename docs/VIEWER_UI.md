# Interface do jogo (estilo viewer)

A interface segue o layout de um viewer de mundo virtual desktop: barra de menus com status no topo, barra de ferramentas com a conversa local embaixo, janelas flutuantes arrastáveis, mini-mapa e tema cinza-escuro. Layout e comportamento são reproduzidos; ícones, logotipos e arte de terceiros não são usados.

## Estrutura

- **Barra de menus** (`ViewerUI`): Eu, Comunicar, Mundo, Conteúdo, Ajuda, Desenvolver. À direita: saldo de LV Coins, coordenadas (leste, norte, altura), hora do mundo, FPS e estado da conexão.
- **Barra inferior**: campo de conversa local (Enter foca, Esc sai) e botões Conversar, Falar (desativado: sem voz), Gestos, Mover, Ver (modos de câmera), Foto, Construir, Pessoas, Mapa, Inventário e Mini-mapa.
- **Janelas** (`FloatingWindow`): conversa local, mini-mapa, Mover (Andar/Correr/Voar), entrar em ilha, ajuda. Os painéis existentes (inventário, social, mapa, marketplace, configurações, construção, terreno, Lua, HUD) receberam o tema e podem ser arrastados pelo fundo.
- **Mini-mapa** (`MiniMap`): norte para cima, centrado no avatar, terreno por altura e textura, água por profundidade, avatares próximos e cone da câmera; roda do mouse altera o zoom.
- **Foto**: captura a tela sem a interface em `user://snapshots`.
- **Ambiente**: Mundo > Ambiente muda a hora do dia só no seu computador.
- **Tema** (`ViewerTheme`): botões, campos, menus, árvores e abas no mesmo estilo.

## Controles novos

- A/D giram, W/S andam, Q/Z passo lateral; o botão Correr do painel Mover mantém "sempre correr" e Shift inverte.
- O botão Voar usa a mesma lógica da tecla F e informa o servidor.

## Limitações

- Sem ícones nos botões, sem menu circular de objeto, sem perfil de avatar, sem arrastar e soltar no inventário, sem voz.
- A escala da interface segue a janela (base 1920×1080); não há ajuste de escala próprio ainda.
- Verificado por capturas de tela no Godot; não testado com dois clientes reais.
