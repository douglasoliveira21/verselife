# Câmera livre

Uma só câmera (a do `PlayerController`); a do editor de construção (`BuildCamera`) é a que se vê enquanto se constrói e recebe os mesmos gestos. Nada da câmera vai para a rede.

## Controles
| Gesto | O que faz |
|---|---|
| ALT + botão esquerdo + arrastar | gira em volta do foco; o arrasto vertical aproxima/afasta |
| ALT + clique (sem arrastar) | olha para o que está sob o mouse (pessoa, objeto, anexo, terreno) |
| CTRL + ALT + arrastar | órbita completa |
| CTRL + ALT + SHIFT + arrastar | desliza a câmera (pan) |
| Roda | zoom (proporcional na câmera livre); CTRL + roda: zoom de precisão |
| ESC | volta para trás do avatar |
| ALT + V | câmera desacoplada (se permitida) |
| CTRL + arrastar | órbita (só com o modo CTRL simplificado ligado) |
| Duplo clique | foca em pessoa/objeto (só com a opção ligada); no chão continua andando |

Andar, sentar, entrar num veículo ou teleportar-se devolve a câmera ao avatar. Gestos da câmera nunca são também um clique no editor de construção nem uma pegada dos gizmos do editor de anexos.

## Código
`shared/Camera/CameraControl.cs` (opções, classificação dos gestos, órbita, zoom, pan, foco, colisão, regras da câmera desacoplada) · `client/Scripts/Camera/` (`CameraController`, `CameraFocus`, `CameraSettings`, colisão) · `PlayerController` (modos: livre, edição, desacoplada) · Configurações → Câmera e visualização → **Câmera livre** (`SettingsCatalog.CameraFree.cs`).

## Testes
`CameraControlTests` (xUnit) · `CameraControlsSmoke` · `CameraSettingsSmoke` · `CameraIntegrationSmoke` (headless).
