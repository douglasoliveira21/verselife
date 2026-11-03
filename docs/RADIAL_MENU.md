`# Menu radial (botão direito)
`
`Clique com o botão direito (sem arrastar, o arrasto continua girando a câmera) em uma pessoa, um objeto, um assento ou no terreno: abre um círculo de vidro escuro com fatias, no estilo do desenho em `client/Assets/ui`. O círculo do meio diz o que foi clicado e, dentro de um submenu, vira **Voltar**. Fatia sob o cursor acende em ciano; clique esquerdo executa ou abre um círculo menor; `Esc`, clique fora ou botão direito fecham. Também funcionam as teclas `1`–`8`, as setas, `Enter` e `Backspace` (volta). Listas com mais de 8 entradas viram uma cadeia de círculos ligada pela fatia **Mais**. O círculo é empurrado para dentro da tela quando abre perto da borda. Fica desativado enquanto a câmera de edição ou o editor de terreno usam o botão direito.
`
`## O que cada alvo oferece (só o que funciona hoje)
`
`- **Você:** Inventário, Animações (Acenar, Dançar, Mais gestos), Pessoas e grupos, Construir, Configurações, Fechar.
`- **Outra pessoa:** Adicionar amigo (abre Pessoas já buscando o nome), Interagir (Acenar, Dançar), Ir até lá, Fechar. Como os avatares não têm colisão, o clique é associado à pessoa pelo raio do cursor contra o corpo (pés à cabeça).
`- **Objeto:** Interagir (toque, ativa o script Lua), Inspecionar, e só se for seu: Editar (Mover, Girar, Esticar, Textura, Duplicar, Excluir), Copiar. O servidor confere a permissão de qualquer jeito.
`- **Assento:** Sentar.
`- **Terreno:** Ir até aqui, Construir, Criar objeto (escolhe onde colocar), Copiar local (coordenadas na área de transferência).
`
`## Código
`
`\`shared/Gameplay/RadialMath.cs\` (qual fatia está sob o cursor, posição dos rótulos, manter na tela, dividir listas) e \`RayMath.cs\` (raio contra corpo) têm testes; \`client/Scripts/RadialMenu.cs\` desenha e trata a entrada; \`ContextInteraction.cs\` descobre o alvo (\`ContextInteractionManager\`) e decide as fatias (\`InteractionRegistry\`); \`ViewerUI.Bars.cs\` liga cada ação à janela dona dela. O projeto é em C#, não GDScript.
`
`## Ainda não existe (do pedido original)
`
`Ver perfil, mensagem direta a partir do menu, teleporte, enviar item, transferir dinheiro, abraçar e animações conjuntas com consentimento, seguir, silenciar, bloquear, denunciar, convidar para grupo; comprar, vender, permissões, devolver e abrir conteúdo pelo menu; portas, veículos, televisão e ações registradas por scripts de criadores; terrenos (propriedade, aluguel, regras); sons do menu. Cada uma depende de um sistema que ainda não existe ou de protocolo de rede novo, e o menu só mostra o que funciona.
