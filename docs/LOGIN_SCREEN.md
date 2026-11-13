# Tela de login

Três camadas independentes, todas em `client/Scripts/Login/` (namespace `LoginUi`) e montadas por `client/Scripts/LoginScreen.cs`:

1. **Fundo** — `LoginBackground`: a imagem `res://Assets/ui/Login/login_background.png` cobre a janela sem distorcer (`KeepAspectCovered`) e um véu escuro suave garante a leitura. Para trocar a imagem basta substituir o arquivo.
2. **Interface** — `LoginLogo` (o mesmo logotipo do jogo (`Assets/brand/logo.png`) e o subtítulo) e `LoginPanel` (700 px, abas **Entrar** / **Criar Conta**) com `LoginForm`, `RegisterPanel` e `PasswordRecoveryPanel`. Campos: `IconField`; botão: `PrimaryButton` (hover, clique, spinner, bloqueia envios repetidos).
3. **Sobreposição** — `WindowControls` (configurações, minimizar, fechar), `ServerStatus` (consulta `/health` a cada 20 s), `NewsTicker` (só mostra o que o cliente sabe: nova versão ou atualização obrigatória) e a versão/build.

## Integração
O login e o cadastro usam o `AuthManager` existente (`/api/v1/auth/login` e `/register`): a sessão vai para o `SessionManager` e o jogo segue para o mundo (ou para o criador de personagem, numa conta nova). "Lembrar de mim" usa o `SavedLogin` existente (arquivo criptografado, chave presa ao computador). Atualização obrigatória continua bloqueando a entrada.

## O que o servidor ainda não tem
- **Recuperação de senha**: não há endpoint (precisa de provedor de e-mail). O painel diz isso com clareza; ligue-o quando a API existir (`PasswordRecoveryPanel.Available`).
- **Autenticação em dois fatores**: não existe na API; nada foi simulado.
- **Novidades da comunidade**: não há feed; a faixa mostra "Sem novidades no momento." até `NewsTicker.SetItems` receber dados reais.
- O cadastro exige e-mail (regra da API), mas o e-mail não serve para entrar.

## Música e luzes
`LoginMusic` toca `Assets/audio/login_theme.mp3` em loop (fade de entrada e de saída; botão de alto-falante silencia e lembra a escolha; respeita o volume geral das configurações). `LoginLightShow` desenha, por cima do papel de parede e sob a interface, feixes de luz que varrem a tela como canhões (ciano, violeta, rosa e dourado) e pontos de luz subindo, em mistura aditiva.

## Testes
`LoginSmoke` (sobe uma API de mentira que o `AuthManager` real consulta por HTTP: validações, olho, Enter, um pedido por vez, credenciais erradas, sucesso com lembrar, cadastro, recuperação, status e layout em 1366×768, 1920×1080, 2560×1440, 3840×2160, 1280×1024 e 1000×1000) e `UpdateSmoke`. Captura: `LIFEVERSE_CAPTURE_VIEW=login|login-register|login-recovery` com `UiCapture`.
