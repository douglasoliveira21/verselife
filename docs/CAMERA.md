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

## Primeira pessoa (corpo inteiro)

A primeira pessoa é "true first person": a câmera fica nos olhos da própria cabeça do avatar e o corpo inteiro é desenhado. Ao olhar para baixo aparecem peito, barriga, braços e mãos, pernas, joelhos e pés, com as roupas e os objetos anexados (e os efeitos deles). Só a cabeça, o pescoço e o que está preso a eles ficam de fora do que o dono vê; os outros jogadores continuam vendo tudo.

- **Posição:** a câmera segue o osso da cabeça (corpo personalizado) ou a cabeça do corpo padrão, um pouco acima e à frente da base da cabeça. Agachar, sentar, inclinar e o balanço dos passos levam a câmera junto, e corpos mais altos ou mais baixos põem os olhos na altura certa (`AvatarRig.EyeWorldPosition`).
- **Cabeça escondida:** no corpo padrão o nó do pescoço some; num corpo personalizado o pescoço e tudo abaixo dele na hierarquia (cabeça, olhos, maxilar, cabelo) encolhem a um ponto onde estão, sem mexer no resto.
- **Olhar:** 360° na horizontal; na vertical de −86° (os pés) a +77°. O plano próximo da câmera é de 6 cm.
- **Campo de visão:** 75° por padrão, de 60° a 100° em Configurações › Câmera › "Campo de visão da primeira pessoa". A terceira pessoa mantém o campo de visão dela.
