# Equivalência dos testes de fumaça do Godot

> **Documento histórico** (marcado em 26/11/2026): vários itens contados como parciais foram fechados depois (editor de anexos, animações, modos da interface, central de avisos, informações do terreno, configurações, espelho). Para o que ainda está aberto, vale [FALTA_FAZER.md](FALTA_FAZER.md).

Gerado na Fase 14 (2026-11-20). Cada um dos 101 testes `client/Tests/<Nome>Smoke.cs` recebe um estado. A classificação foi feita pelo nome e pela descrição de cada teste e pelo que o cliente Unity tem hoje, sem rodar cada smoke. **Coberto**: há verificação equivalente no Unity (EditMode, auto-teste do mundo, API real ou xUnit). **Parcial**: parte do comportamento existe. **Não migrado**: a função não existe no cliente Unity. **Substituído**: outro meio cumpre o papel. **Não verificado**: não há evidência nos dois lados. Atualizado depois da Fase 15, quando teleporte, mapa, efeitos, mercado imobiliário, sincronia de configurações, dedos e atualização foram feitos.

Resumo: Coberto: 75; Parcial: 25; Substituído: 1.

| Smoke | Estado | Observação |
|---|---|---|
| AccountSync | Parcial | 5 preferências e a primeira tecla de 14 ações sincronizam com a conta (os nomes de tecla são convertidos); o resto da lista do Godot não tem par aqui |
| AnimationState | Coberto | auto-teste: andar, correr, parar, balanço dos braços |
| Asset | Coberto | API real: envio, inventário, versões |
| AttachmentEditor | Parcial | janela com campos e botões; sem o editor 3D com gizmo |
| AttachmentKinds | Parcial | modelos sim; imagens e construções salvas como anexo não são desenhadas |
| Attachments | Coberto | auto-teste e API real (vestir, ajustar, mover, tirar) |
| AttachmentsFinal | Coberto | ponto de tela, balanço e mover de ponto |
| Avatar2 | Coberto | aparência, editor de personagem, API real |
| AvatarAnimations | Parcial | clipes padrão sim; arquivos de animação próprios não |
| AvatarCustom | Parcial | corpo GLB próprio carrega; ajuste de ossos próprio não conferido |
| AvatarCustomShape | Parcial | sem teste com forma própria |
| AvatarDefaultAnimations | Coberto | clipes padrão comparados com o Godot (menos de 1 grau) |
| AvatarDetail | Coberto | planejador compartilhado |
| AvatarGrounding | Parcial | pés no chão sem teste dedicado |
| AvatarWearables | Parcial | peças do catálogo sim; peças personalizadas não |
| BuildClickAction | Coberto | lista de ações e clique no objeto |
| BuildCollab | Coberto | travas, arrasto ao vivo, revisões |
| BuildContent | Coberto | aba de conteúdo |
| BuildEditorV2 | Coberto | modo construção, undo/redo, ímã |
| BuildGeneral | Coberto | aba geral |
| BuildLinkFace | Coberto | ligar objetos, escolher face |
| BuildLoad | Coberto | objetos da ilha no mundo, limites (API real) |
| BuildObjectTab | Coberto | abas do objeto |
| BuildPackage | Coberto | construções salvas (API real) |
| BuildResources | Coberto | mapas, mídia, sons |
| BuildTexture | Coberto | texturas e mapas por face |
| BuildTransform | Coberto | mover, girar, escalar |
| BuiltInBody | Coberto | corpo lifeverse_man |
| CameraControls | Coberto | câmera de seguir, zoom, inverter Y |
| CameraIntegration | Coberto | câmera com o jogador e o terreno |
| CameraSettings | Coberto | sensibilidade e opções |
| ClientPersistence | Parcial | só as configurações e a direção de entrada |
| DaeImport | Coberto | conversor Collada e janela de importação |
| Fingers | Coberto | `HandFingers` curva os dedos conforme a mão; verificado em um corpo humanoide com 30 ossos de dedos lido do jeito real (glTF) e em esqueletos montados à mão. Falta ver num corpo de pessoa de verdade, que o repositório não tem |
| FirstPerson | Coberto | primeira pessoa (V) |
| FirstPersonCamera | Coberto | câmera em primeira pessoa |
| FirstPersonHands | Parcial | sem teste das mãos |
| GraphicsQuality | Coberto | presets medidos (Fase 13) |
| GroupCreate | Coberto | API real |
| GroupProfile | Coberto | API real |
| Groups | Coberto | API real |
| GroupsHub | Coberto | API real |
| HeadLook | Coberto | olhar da cabeça |
| HighJump | Parcial | a previsão do cliente agora usa as mesmas formas sólidas do servidor (`ObstacleField`): pular de cima de um objeto e esbarrar numa parede dão o mesmo resultado nos dois lados (EditMode). O teste do servidor com o hemisfério segue falhando, fora da migração |
| ImportFormats | Coberto | GLB, glTF, DAE |
| InterfaceBars | Coberto | barras superior e inferior |
| InterfaceModes | Parcial | sem os modos de interface do Godot |
| InterfaceResolution | Parcial | sem teste de resoluções |
| InterfaceSettings | Coberto | janela de configurações |
| InterfaceWindows | Coberto | janelas flutuantes |
| Inventory | Coberto | API real |
| IslandLandPages | Coberto | aba de terrenos (API real) |
| IslandManagement | Coberto | API real |
| IslandProperty | Parcial | sem teste dedicado |
| ItemRights | Coberto | API real |
| Land | Coberto | API real |
| LandManagement | Coberto | API real |
| LandPresence | Parcial | servidor cobre; falta o aviso no cliente |
| Loading | Coberto | tela de carregamento |
| Login | Coberto | login, cadastro, API real |
| MapBase | Coberto | canvas do atlas com arrastar, zoom e escolha de ponto, e as conversões de coordenadas (EditMode) |
| MeshCollisionDefault | Coberto | colisão exata por padrão na importação |
| MiniMap | Coberto | minimapa |
| Mirror | Parcial | editor de personagem em janela, não dentro do mundo |
| ModelLod | Coberto | simplificador de malha e LOD (Fase 12) |
| Movement | Coberto | previsão e reconciliação (auto-teste) |
| MyProperties | Coberto | resumo, ilhas, terrenos, aluguéis, anúncios, pagamentos, doações, e criar e retirar anúncios de venda e aluguel (auto-teste e EditMode) |
| NotificationCenter | Parcial | avisos e histórico; falta a central como janela |
| ObjectEffects | Coberto | partículas e luzes dos scripts, com limites e a qualidade do preset (auto-teste) |
| ParcelInfo | Parcial | dados no painel de terrenos |
| People | Coberto | janela de pessoas |
| PhysicsSync | Coberto | auto-teste de física |
| Profile | Coberto | API real |
| Radial | Coberto | menu radial |
| RadialWiring | Coberto | ligações do menu |
| RealEstateMarket | Coberto | janela do mercado imobiliário: abas, busca, páginas, comprar, alugar, visitar (auto-teste e EditMode) |
| Scenario | Parcial | cenas de referência de imagem só em parte |
| ScriptDialog | Coberto | diálogos de script |
| ScriptEditor | Coberto | editor Lua com verificação |
| ScriptHud | Coberto | painéis de script |
| Search | Coberto | API real |
| SessionRefresh | Parcial | renovação testada com a API de teste; com a real, inconclusivo (banco em memória) |
| SettingsControls | Coberto | teclas redefiníveis |
| SettingsEffects | Parcial | sem todos os efeitos do Godot |
| SettingsMore | Parcial | sem todas as opções |
| SettingsStore | Parcial | arquivo local |
| SettingsWindow | Coberto | janela de configurações |
| Shader | Substituído | shaders HLSL próprios, conferidos por fotos |
| StartPlace | Parcial | entrada na ilha; sem lembrar a direção |
| Teleport | Coberto | `TeleportTrip` e `GameApp.Teleport`: preparar, bilhete, trocar a conexão, confirmar a chegada e voltar à ilha anterior se falhar (auto-teste) |
| Terrain | Coberto | malha, camadas, edição |
| TerrainEditor | Coberto | editor de terreno |
| Typing | Coberto | digitando e som do teclado |
| Update | Coberto | aviso, bloqueio abaixo do mínimo e download do instalador com conferência de SHA-256 (a API agora publica `Sha256Windows`); abrir o instalador é um clique da pessoa |
| Vehicle | Coberto | veículos |
| Voice | Coberto | voz por proximidade (microfone real não verificável) |
| VoiceUi | Parcial | botão e teste de microfone; sem a janela completa |
| Wallet | Coberto | API real |
| WindowManager | Coberto | janelas |
| WorldMap | Coberto | janela do mapa-múndi: atlas, busca, cartão, viajar, favoritos (auto-teste e EditMode) |
| WorldMapPlaces | Coberto | marcos com pastas e compartilhamento, favoritos, recentes, ilhas de amigos, imagens aéreas e os terrenos desenhados sobre as ilhas, com o terreno sob o ponto escolhido no cartão (auto-teste com GPU e sem, e EditMode) |
