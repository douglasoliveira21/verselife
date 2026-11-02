# Fase 15 — Câmera e controles

## Controles atuais

- `W/A/S/D`: movimento relativo ao avatar.
- `Shift`: corrida.
- `Espaço`: pulo ou ascensão durante voo.
- `F`: alternar voo.
- `C`: alternar primeira/terceira pessoa.
- `V`: alternar modo de câmera (terceira pessoa, primeira pessoa e visão livre).
- `R`: retornar à terceira pessoa e recentrar a câmera.
- Botão direito pressionado + arraste: orbitar câmera.
- Clique do botão do meio: focar o ponto sob o cursor.
- Roda do mouse: zoom entre 1,5 m e 12 m.
- Clique direito sem arrastar: menu contextual.
- Duplo clique no terreno: caminhar até o ponto atingido pelo raycast.
- `Esc`: liberar cursor, fechar menu contextual e remover foco de campos de texto.

O cursor permanece visível por padrão. A câmera usa suavização de rotação e o `SpringArm3D` mantém a colisão contra paredes. Movimento e ações são bloqueados enquanto um `LineEdit`/`TextEdit` possui foco ou o menu contextual está aberto.

## Limitações

O duplo clique usa raycast direto e só define destino quando há uma superfície física válida; ainda não há navegação por `NavigationServer3D`. Os modos de construção e fotografia estão preparados no controlador, mas os editores específicos continuam responsáveis por suas próprias câmeras.
