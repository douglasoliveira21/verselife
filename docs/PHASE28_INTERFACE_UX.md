# Fase 28 — Interface e UX

## Entregue

- HUD principal reorganizado em uma barra de acesso com ações reais para construção, inventário, comunicação, mapa/busca, marketplace e configurações.
- Botões passaram a ter tooltips, foco de teclado e descrições claras de ação.
- Entrar/Sair exibem finalidade explícita e o estado de conexão permanece visível.
- Layout continua baseado em controles responsivos da Godot, com configurações persistentes de resolução, janela, FPS, V-Sync e escala da interface.
- Nenhum atalho novo foi criado para ações inexistentes; módulos ainda não disponíveis devem permanecer indicados pela própria janela.

## Limitações conhecidas

- Um gerenciador universal de janelas com arrastar/redimensionar/minimizar ainda não foi introduzido; os painéis atuais têm posições fixas por módulo.
- Menus contextuais completos para terreno, parcela, objeto e avatar ainda dependem dos editores correspondentes.
- Teste visual automatizado em 1366×768 e 1920×1080 requer execução manual do cliente Godot.
