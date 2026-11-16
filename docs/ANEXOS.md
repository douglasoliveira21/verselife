# Anexos corporais (Attachments)

Objetos do inventário presos ao corpo do avatar, com posição, rotação e escala relativas ao ponto. O servidor decide tudo; o cliente só pede.

## Pontos
`shared/Avatar/AttachmentPoints.cs`: 52 pontos (cabeça e rosto, tronco, braços e mãos, pernas e pés, especiais, 8 de HUD) + 10 dedos. Cada ponto tem **id numérico e chave estáveis** (é o que fica no banco), nome em português, osso padrão, deslocamento padrão e nome no Second Life (`FromSecondLife`). Cauda, asas e dedos só existem quando o corpo personalizado tem esses ossos (perfis `tail`, `wings`, `fingers`, detectados pelo nome dos ossos em `AttachmentProfiles.Detect` e guardados no relatório do modelo). Num corpo sem o ponto, o objeto vai para o ponto mais próximo (`Fallback`) sem mexer no ajuste, e volta quando o corpo o tem de novo.

## Banco
`identity.avatar_attachments` (migração `Phase99AvatarAttachments`): id, avatar_id, inventory_item_id, asset_id, attachment_point, position/rotation(graus)/scale x-y-z (double), equipped, sort_order, settings_json (`originalPoint`, `sway`), skeleton_profile_version, revision, created_at, updated_at. Único por (avatar, item). Tirar do corpo mantém a linha (`equipped=false`).

## API (`/api/v1/users/me/avatar/attachments`)
`GET /points` · `GET` (lista, `profiles`, limites) · `POST` (anexar: `inventoryItemId`, `point`, `mode` add/replace, transformação opcional) · `PATCH /{id}` (position, rotation, scale, sway) · `POST /{id}/reset` · `POST /{id}/move` · `DELETE /{id}` · `DELETE ?point=`.
Regras: item é seu, ativo e com asset pronto; ponto existe no corpo; limites (38 no total, 8 por ponto, 8 de HUD, deslocamento ≤ 5 m, escala 0,01–10); mudar a transformação exige o direito de **modificar** (trocar de ponto e remover não).
Outros: `/platform/avatars/{id}/appearance` e a admissão na ilha trazem `bodyAttachmentsJson`; o arquivo do modelo de um objeto vestido é servido a quem está na ilha (`MeshAssets`). Outfits (looks) guardam e restauram os objetos (`LookSnapshot.BodyAttachments`).

## Rede (ENet, ilha)
`AttachmentLive(string id, float[9])` cliente→servidor e `AttachmentLiveChanged(int peer, string id, float[9])` servidor→outros. O servidor confere que o anexo está no look do jogador, limita a 1 a cada 60 ms e recusa números fora dos limites (`shared/Avatar/AttachmentLive.cs`). Nada é guardado; o ajuste salvo chega a todos pelo `AppearanceChanged` normal (o pedido de atualização agora é adiado, não descartado, dentro dos 3 s de espera).

## Cliente
`AvatarRig.Attachments.cs` (segue o osso, pré-visualização, limites de desempenho, animações do modelo, balanço), `Scripts/Attachments/*` (store, editor, janela de pontos, lista “Anexos”, camada de HUD), `PlayerController` (câmera de edição), menu radial e menus do inventário.

## Desempenho
200 mil triângulos no seu avatar, 40 mil nos dos outros; 2 luzes e 2 sistemas de partículas por avatar; objetos de outros somem a 45 m; carregamento assíncrono e em cache (`ModelCache`); tudo é liberado ao remover.

## Limitações conhecidas
- Scripts Lua em objetos anexados **não** foram implementados (hoje só rodam em objetos de construção na ilha).
- HUD: o objeto é desenhado na tela, mas ainda não há editor de posição para ele (só ponto, escala e rotação vindos do ajuste salvo).
- O balanço é um movimento leve (inclinação), não física de corpo rígido.
- Perfis de cauda/asas/dedos dependem do nome dos ossos do modelo.
- A janela de escolha e o editor só foram testados em modo headless.
