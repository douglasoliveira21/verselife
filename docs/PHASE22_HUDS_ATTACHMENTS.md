# Fase 22 — HUDs e attachments

## Entregue

- Editor visual de HUD com painel, texto, botão, imagem/ícone, barra, lista, entrada e container.
- Layout serializado com versão, âncoras, margens, visibilidade, camadas e marcação de edição.
- Salvamento da HUD como item `huds` no inventário lógico.
- Endpoints autenticados para equipar/desequipar HUDs, com prioridade de 0 a 15 e até uma HUD por camada.
- Persistência usando `inventory.equipped_items`, associada ao avatar do usuário.
- Validação de layout JSON e limites de tamanho existentes; scripts continuam sujeitos ao sandbox Lua.

## Segurança

HUDs não recebem APIs de carteira, mensagens privadas, arquivos locais ou rede externa. Toda associação de script passa pelo identificador autorizado e pela API autenticada.

## Limitações conhecidas

- Arrastar/redimensionar diretamente com gizmos ainda não está implementado; o editor gera componentes e camadas de forma determinística.
- O botão Equipar do editor orienta salvar primeiro; o equipamento programático usa `POST /api/v1/huds/{hudId}/equip` com prioridade.
- Preview de imagens reais e composição de múltiplas HUDs no cliente final exigem o renderer de HUD runtime.
