# Permissões de itens (Copiar, Modificar, Transferir, Revender)

Quem decide é sempre o servidor (`ItemRightsService`). O jogo só mostra o que o servidor responde; nenhuma verificação vive só na interface.

## Fase 1 — o núcleo

**Os direitos** (`shared/Items/ItemRights.cs`) são quatro bits: Copiar 1, Modificar 2, Transferir 4, Revender 8. Revender sem Transferir não existe (some ao normalizar); bits desconhecidos são descartados. Um conjunto não pode dar mais do que tem (`Clamp`, `IsWithin`).

**Três conjuntos por item** (`ItemPermissions`): o do dono atual, o do grupo com quem o item é compartilhado, e o do **próximo dono** (quem compra ou recebe). O do grupo e o do próximo dono têm de caber dentro do conjunto do dono. Ao chegar ao próximo dono, ele passa a ter o conjunto "próximo dono", o compartilhamento com grupo some e o que ele repassa não passa disso (`ForNextOwner`).

**Herança** (`Intersect`, `ItemPermissions.Compose`, `ItemRightsService.ComposeAsync`): algo feito de partes (objeto com filhos, objetos agrupados, pacote) só tem o que **todas** as partes permitem. Uma parte não copiável torna o conjunto não copiável.

**Item não copiável:** só se move (de um dono para outro), nunca se duplica. A regra de mover sem duplicar é da fase 2; a fase 1 já guarda e decide `IsCopyable`.

**Regras por tipo de conteúdo** (`ContentRules`): o código-fonte de um **script** só pode ser lido/editado por quem tem Modificar; **modelos, texturas, animações, materiais e sons** só podem ser baixados como arquivo por quem tem Copiar; pôr qualquer um deles dentro de um objeto que pode mudar de mãos exige Transferir (o conteúdo vai junto). A categoria do ativo define o tipo (`FromCategory`). Essas regras são aplicadas nos endpoints de download, de scripts e de construção nas fases seguintes.

**No banco** (migração `Phase52ItemRights`): `inventory.items` ganhou `owner_rights`, `group_rights`, `next_owner_rights`, `group_id`, `copied_from_item_id`, `show_owner_history`, `status` (`active` ou `offered`) e `revision` (token de concorrência). Itens que já existiam ficam com **todos os direitos** (nada era restrito antes) e cada um ganha a primeira linha do histórico (`legacy`). Novas tabelas: `inventory.item_owners` (histórico de donos) e `inventory.item_audit` (trilha de auditoria). Nenhuma das duas tem chave estrangeira para o item, para sobreviver à remoção dele ou da conta.

**Serviço** (`backend/Modules/Inventory/ItemRightsService.cs`):
- `HeldAsync` / `CanAsync`: o dono tem o conjunto do dono; membro do grupo do item tem o do grupo (limitado ao do dono); qualquer outra pessoa nada. Item removido ou "oferecido" (esperando resposta) não pode ser usado por ninguém.
- `RequireAsync`: carrega o item e confere o direito. O que não é da pessoa **não existe** para ela (`not_found`); o que é dela mas sem o direito dá `not_allowed` e **fica na auditoria** como `denied`.
- `SetPermissionsAsync`: só o dono altera. Pode abrir mão de direitos (e o que repassa acompanha), mas só recupera direitos que não tem se também for quem **criou** o ativo e ainda o tiver. Grupo e próximo dono acima do dono são recusados. Uma mudança feita sobre uma versão velha (`expectedRevision`) é recusada; duas mudanças simultâneas: só uma vale (testado com 20 pedidos ao mesmo tempo num PostgreSQL real).
- `ShareWithGroupAsync`: só com grupo do qual o dono é membro; parar de compartilhar zera o conjunto do grupo.
- Histórico (`HistoryAsync`, `SetHistoryVisibilityAsync`): sempre gravado; o dono vê tudo; membro do grupo só vê se o dono permitiu, e então os nomes **intermediários** ficam ocultos ("proprietário anterior"): aparecem o criador, o dono atual e as linhas da própria pessoa.
- Auditoria (`Audit`): grava no mesmo salvamento da mudança que descreve (ou as duas coisas acontecem, ou nenhuma). Também registra o que foi recusado.

**API** (`/api/v1/inventory/items/{id}`): `GET rights`, `PUT rights` (`owner`, `group`, `nextOwner`, `expectedRevision`), `PUT group`, `PUT history-visibility`, `GET history`. Erros: 404 (não é seu), 409 (`stale`, `item_busy`), 400 (`cannot_raise_rights`, `exceeds_owner`, `not_in_group`).

**Testes:** `ItemRightsModelTests` e `ItemRightsServiceTests` (modelo, herança, quem tem o quê, recusas com marca na auditoria, mudança desatualizada, histórico) e `ItemRightsPostgresTests` (padrões do banco para itens "antigos"; 20 mudanças simultâneas, uma só vence).

## Ainda não (próximas fases)
2: inventário, cópia e transferência (a cópia atual ainda ignora estes direitos); 3: marketplace; 4: construção e comandos multiplayer; 5: cliente; 6: testes finais de exploração.
