# Fase 17 — Construção avançada

Implementado sobre o editor autoritativo existente:

- `geometryJson` versionado por objeto para hollow, taper, twist e limites de cut/slice.
- Validação no backend e no island-server com limites de tamanho, números finitos e complexidade controlada.
- Campos numéricos de rotação e snap no editor.
- Copiar/colar propriedades de material, geometria, escala e rotação.
- Materiais com cor, rugosidade, metallic, transparência, emissão e overrides por face.
- Revisões continuam obrigatórias em cada edição; conflitos são rejeitados pelo servidor.
- Migration `Phase17AdvancedBuildGeometry` adiciona `world.objects.geometry_json`.

O mesh procedural atual usa `CylinderMesh`, `TorusMesh`, `BoxMesh` e `PlaneMesh`. Parâmetros que exigem topologia dedicada são preservados no JSON e limitados pelo servidor; a implementação de extrusão e hollow real por face depende de um gerador `ArrayMesh` dedicado.

Ainda pendente para equivalência completa com ferramentas de mundos virtuais: seleção múltipla com pivô de grupo, distribuição/alinhamento visual, gizmos X/Y/Z e conjuntos reutilizáveis no inventário.
