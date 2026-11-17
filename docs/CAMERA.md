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

### Movimento da câmera na primeira pessoa

Tudo em Configurações › Câmera › "Primeira pessoa" (classes puras em `shared/Camera/FirstPersonCamera.cs`, com testes em `FirstPersonCameraTests`):

- **Balanço da cabeça (head bob):** sobe e desce duas vezes e balança uma vez por ciclo de dois passos, uns 1,6 cm de altura a uma caminhada rápida, mais ao correr; some ao parar ou no ar. Liga/desliga e intensidade de 0,25× a 2×.
- **Inclinação (sway):** a visão inclina até uns 2° contra o giro e ao andar de lado, voltando sem oscilar (mola amortecida). Liga/desliga e intensidade.
- **Estabilização:** só a posição dos olhos é suavizada (0 segue o osso da cabeça exatamente, 1 é o mais firme); o giro da visão nunca é filtrado. Subir e descer (sentar, levantar) é mais lento que o resto, para deslizar. Um salto de mais de 1,5 m (teleporte) é seguido na hora.
- **Giro sem atraso:** na primeira pessoa a visão segue o mouse em poucos milissegundos por padrão; "Suavização do olhar" de 0 a 1 deixa o giro mais macio.
- **Colisão:** um raio da base da cabeça até um pouco além dos olhos impede a câmera de atravessar paredes e objetos (inclusive com o rosto encostado), e recua os olhos em direção à cabeça.

### Entrar e sair da primeira pessoa; mãos e braços

- **Roda do mouse:** em terceira pessoa, aproximar a câmera até a distância mínima (1,5 m) e rolar mais uma vez entra na primeira pessoa; rolar para trás de dentro dela volta à terceira, na distância mínima. Configurações › Câmera › "Roda do mouse entra na primeira pessoa" desliga isso (o atalho `C` continua).
- **Sem saltos:** a câmera desliza até os olhos (e de volta) e o campo de visão acompanha; a cabeça só some quando a câmera já chegou perto dela (e volta antes de a câmera se afastar). O avatar continua virado para onde estava: a visão é que gira até ele.
- **Braços e mãos:** nada é movido para a câmera. As mãos ficam onde a animação do estado as põe e só aparecem quando isso é natural: relaxadas ao lado do corpo parado e andando (aparecem ao olhar para baixo), à frente ao digitar (visíveis olhando um pouco para baixo), levantadas ao acenar. Objetos anexados às mãos acompanham. `FirstPersonHandsSmoke` confere isso nos 17 estados, no corpo padrão e no personalizado, e que nenhuma mão termina dentro da câmera.
- **Digitar no corpo padrão:** os braços agora vão para a frente (antes iam para trás), levemente para dentro, com o tronco inclinado e a cabeça olhando para baixo.

### Multiplayer: a cabeça de quem olha pelos olhos

- **O que vai pela rede:** só um ângulo (quanto a pessoa olha para cima ou para baixo, em radianos), nunca a câmera. O cliente o envia (`HeadLook`, canal não confiável) só na primeira pessoa, no máximo ~12 vezes por segundo e só quando ele muda mais que 0,03 rad; o mesmo ângulo é repetido uma vez por segundo para quem acabou de chegar, e ao sair da primeira pessoa o cliente manda 0 para a cabeça endireitar.
- **Servidor da ilha** (`ServerHeadLook.cs`, regras puras em `shared/Networking/HeadLook.cs`): só aceita jogador autenticado, recusa o que não é um ângulo (NaN, fora de ±1,6 rad), limita a frequência por jogador, limita o ângulo desenhado a ±0,9 rad (cerca de 52°; um pescoço não dobra mais) e repassa (`HeadLookChanged`) só aos jogadores dentro do raio de interesse. Nada fica guardado; o movimento continua autoritativo e o giro do corpo continua vindo dos snapshots.
- **Avatares dos outros** (`AvatarRig.Look.cs`): o corpo padrão inclina o pescoço; o personalizado vira os ossos do pescoço (40%) e da cabeça (60%) depois da animação, em torno do eixo da direita do avatar. A inclinação suaviza (sem teleportar) e vale em qualquer animação.
- **Publicação:** servidor da ilha e cliente juntos (duas chamadas remotas novas); sem mudança no backend nem no formato dos snapshots.
