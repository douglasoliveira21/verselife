# Fase 18 — Sistema avançado de objetos

Implementado:

- Identidade persistente com UUID, criador original, proprietário atual, nome, descrição, ilha, revisão e timestamps.
- Hierarquia por `ParentObjectId` e `GroupId` para linksets, com FK protegida contra ciclos destrutivos no banco.
- `permissionsJson` separado do RBAC da ilha; edição e exclusão do objeto exigem o proprietário atual.
- `contentJson` para referências a scripts, sons, animações e itens internos sem duplicar assets.
- `ownerHistoryJson` preparado para histórico de transferência.
- Objetos continuam vinculados a `ObjectScript` por FK e podem expor pontos de interação/assento via conteúdo.
- Nome e metadados chegam ao cliente junto com material, geometria, física e revisão.
- Migration `Phase18AdvancedObjects` adiciona os campos e preserva objetos antigos usando o proprietário existente como criador inicial.

Limitações conhecidas:

- Transferência de propriedade e retorno ao inventário ainda precisam de endpoints de workflow e auditoria própria.
- A seleção “objeto atrás de outro”, edição de partes de linkset e inventário interno visual ainda são próximos incrementos do editor.
