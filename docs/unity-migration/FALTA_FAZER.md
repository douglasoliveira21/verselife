# O que falta na migração para o Unity (passagem de serviço)

Estado em 27/11/2026. A última rodada (defeitos que só aparecem no PostgreSQL, privacidade e sessões, teste longo, avisos de licença, prévia dos anexos e encaixe de janelas) e a troca para o servidor novo vêm nos commits depois do `8516450`. Este é o documento para quem continuar o trabalho, seja pessoa ou IA. Ele junta tudo o que está aberto. Cada item diz onde fica no Godot, onde entraria no Unity, qual API usa e como saber que acabou.

Os outros documentos desta pasta contam a história do trabalho. Alguns estão **desatualizados** (ver a seção 10); quando um deles disser "falta" e este não disser, vale este.

---

## 0. Antes de começar

### Onde está cada coisa
- **O Godot saiu do repositório em 27/11/2026** (`client/`, `island-server/` e as ferramentas que dependiam dele). O comportamento antigo se consulta no histórico: `git show 177c0c1:client/...`. Onde este documento ou os outros citam um desses caminhos, é no histórico que ele está.
- Arquivos que vão no jogo: `assets/` (corpo, sons, ícone, licenças); os desenhos de referência da interface estão em `docs/design/`.
- Cliente Unity: `unity/UnityClient/Assets/LifeVerse/`, com as pastas:
  - `UI`: janelas e HUD em UI Toolkit, construídas por código. O visual é o **Varanda** e fica em `Resources/LifeVerse/UI/Theme.uss` (as cores são variáveis no começo do arquivo, uma lista para o dia e outra para a noite); os ícones da barra são `VarandaIcons.cs`; só cinco texturas pequenas saem de `python unity/tools/ui-skin/generate.py`;
  - `World`: terreno, mar, objetos, ambiente;
  - `Avatar`: corpo, câmera, jogador;
  - `Audio`;
  - `Server`: o servidor de ilha Unity, os autotestes e a verificação ao vivo;
  - `Tests/EditMode`.
- Código compartilhado pelo cliente, pelo servidor de ilha e pela API: `shared/` (compilado como `LifeVerse.Shared.dll`) e `scripting/` (Lua, MoonSharp).
- Servidor de ilha Unity, sem a Unity: `unity/UnityIslandServer` (`Core`, com os scripts Lua em `Core/Scripting`; `Host`; `LoadTest`). O protocolo é o LifeVerse Wire 12, em `unity/UnityWire`, descrito em `WIRE_PROTOCOL.md`.
- API: `backend/` (ASP.NET, Postgres). Testes: `tests/`.
- Motor da ilha: `world.islands.engine`. Só as `unity` têm servidor; quem não manda `X-LifeVerse-Engine: unity` recebe 409 `engine_mismatch`. O guia de implantação é `DEPLOY_UNITY_ISLANDS.md`.

### Comandos (Windows, PowerShell ou Git Bash)
O executável da Unity é `C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Unity.exe`. O modo batch só funciona com a licença válida; se der o código 198, reative a licença no Unity Hub.

- **Testes EditMode:**
  ```
  Unity.exe -batchmode -nographics -projectPath unity/UnityClient -runTests -testPlatform EditMode -testResults <arquivo.xml>
  ```
- **Gerar o cliente:**
  ```
  Unity.exe -batchmode -nographics -quit -projectPath unity/UnityClient -executeMethod LifeVerse.Unity.EditorTools.BuildClient.BuildWindows
  ```
  O resultado sai em `unity/UnityClient/Builds/Windows/LifeVerse.exe`.
- **Autoteste do mundo** (servidor de verdade e 3 jogadores num processo):
  ```
  LifeVerse.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -lifeverse-world-selfcheck
  ```
  Espera-se `event=world_sync_check result=passed checks=370`.
  - A cena inicial (`Bootstrap.unity`) **não tem luz própria**: o sol e a lua são do `IslandEnvironment`. A luz padrão da Unity que estava lá iluminava todas as ilhas, inclusive de noite. O teste de sentar ou o de pulo podem falhar por tempo: rode de novo.
- **Verificação contra a API real, local:**
  1. Suba a API: `dotnet run --project tools/ApiLiveHost -c Release --no-build -- 5099`.
  2. Rode `LifeVerse.exe -lifeverse-live-api http://127.0.0.1:5099`. Espera-se `steps=32 failed=0`.
  3. Mate só o PID que escuta a porta 5099. O banco é em memória e dura enquanto o processo viver: reinicie-o antes de repetir. Com `LIFEVERSE_LIVE_PG` (a conexão de administrador de um PostgreSQL **desta máquina**) a mesma API sobe sobre um banco de verdade, criado e migrado na hora; apague depois os bancos `lifeverse_live_*`. **Prefira assim:** só no PostgreSQL aparecem os erros de tipo, e a renovação da sessão só é testada nele.
- **As mesmas verificações dentro do editor** (quando o `.exe` não pode ser executado): o resultado sai no arquivo dado em `-lifeverse-editor-run`, e o editor se fecha com o código da verificação (0 passou, 2 o autoteste quebrou, 3 passou de 20 minutos).
  ```
  Unity.exe -batchmode -projectPath unity/UnityClient -executeMethod LifeVerse.Unity.EditorTools.EditorChecks.Play -logFile editor.log -lifeverse-editor-run <saida.log> -lifeverse-live-api http://127.0.0.1:5099
  Unity.exe -projectPath unity/UnityClient -executeMethod LifeVerse.Unity.EditorTools.EditorChecks.Play -logFile editor.log -lifeverse-editor-run <saida.log> -lifeverse-world-selfcheck
  ```
- **Fotos do mundo e física, no editor** (`-lifeverse-world-preview <prefixo>` com `-batchmode`; `-lifeverse-physics-report` com `-batchmode -nographics`), pelo mesmo `EditorChecks.Play`.

  O autoteste precisa da **janela** do editor (sem `-batchmode`): sem ela o fim do quadro nunca chega e ele trava. O erro `ArgumentOutOfRangeException` em `UnityEditor.Search`, logo no começo, é do editor.
- **Outros modos de foto e teste:**
  - `-lifeverse-ui-shots <prefixo>`: fotos das telas (no editor, sem `-batchmode`, pelo `EditorChecks.Play`); com `-lifeverse-editor-size 1920x1080` o editor desenha nesse tamanho e a escala automática da interface é a que se vê;
  - `-lifeverse-doc-shots <pasta>`, junto do autoteste: as fotos `ui-*.png` das janelas com dados;
  - `-lifeverse-world-preview <prefixo>`: as 9 vistas do mundo;
  - `-lifeverse-e2e <api> <usuário> <senha>`: ponta a ponta, contra uma API e um supervisor de verdade.
- **API:** `dotnet test tests/LifeVerse.Api.Tests`. A última rodada, com `LIFEVERSE_TEST_PG`, deu 2.614 aprovados, 0 falhas e nenhum pulado. Os testes de carga e do depurador Lua já falharam uma vez cada quando rodaram junto com os outros, e passaram sozinhos.
- **Protocolo e servidor de ilha:** `dotnet test tests/LifeVerse.Wire.Tests` (33) e `dotnet test tests/LifeVerse.IslandServer.Tests` (64, com os 64 jogadores em rede ruim, as entradas hostis e a volta longa). Para sessões longas: `LIFEVERSE_SOAK_SECONDS` (gente entrando e saindo; memória e passos), `LIFEVERSE_FUZZ_SECONDS` (fuzz do leitor), `LIFEVERSE_FUZZ_SEED` e `LIFEVERSE_FUZZ_MESSAGES` (mensagens absurdas ao servidor) e `LIFEVERSE_LAG_SECONDS`.
- **Imagens WebP de referência:** em `tests/fixtures/webp`, geradas com o Godot antes de ele sair; não há mais gerador no repositório.
- **Pilha completa em WSL** (Postgres, API, supervisor Unity e servidor Linux): foi montada com scripts `e2e/up.sh` e `e2e/down.sh` na pasta temporária da sessão. Esses scripts não estão no repositório; o roteiro está no fim de `DEPLOY_UNITY_ISLANDS.md`.

### Duas travas desta máquina (descobertas em 26/11/2026)
- **O Controle Inteligente de Aplicativos do Windows bloqueia o `LifeVerse.exe` gerado** ("uma política de Controle de Aplicativo bloqueou este arquivo"; eventos 3077 e 3118 em `Microsoft-Windows-CodeIntegrity/Operational`). Enquanto isso, nada que dependa do `.exe` roda nesta máquina. Os testes EditMode e o build funcionam, e o autoteste e a verificação ao vivo rodam **dentro do editor** (ver os comandos acima). É uma proteção do Windows: só o dono decide o que fazer com ela. Vale também para os jogadores: um `.exe` sem assinatura pode ser bloqueado no computador deles (ver a seção 6).
- **O `build-plugins.ps1` não roda sem liberar a política de execução do PowerShell.** Para um só projeto, dá para compilar com `dotnet build` (os alvos estão no próprio script) e copiar a DLL.

- **O mesmo controle já bloqueou DLLs recém-compiladas** dos testes e da API de teste (26/11/2026). Não há o que contornar: avise o dono.

### Regras combinadas com o dono do projeto
- **Fale em português** com ele.
- **Commit e push só quando ele pedir.** O normal é commitar no `main`.
- **Nunca use credenciais reais contra a produção** (`app.lifeverse.euporiagames.com`; antes, `api.nerdplus.com.br`).
- **Peça autorização antes** de qualquer mudança de produção, de banco ou irreversível: migração aplicada em produção, troca do motor de uma ilha, deploy.
- **Nunca mate processos pelo nome;** só o PID que você mesmo iniciou.
- **Faça cada item inteiro** sem perguntar se deve continuar.
- **Atualize sempre a documentação:** depois de cada rodada, `GODOT_PARITY_AUDIT.md` (uma seção "Feito…" com o que foi verificado) e este arquivo.
- **Verifique cada rodada** com os testes EditMode, o autoteste, a verificação ao vivo e, se for visual, fotos com GPU.

### Armadilhas conhecidas
- **Botões:** um `Button` com elementos filhos para de medir o próprio texto. Ícones são desenhados pelo `UiKit.WithIcon`, com `generateVisualContent`.
- **Desenho sem GPU:** o `generateVisualContent` não roda com `-nographics`. Contagens de desenho só podem ser conferidas com GPU.
- **Nome cortado apaga a dica:** quando um `Button` com `text-overflow: ellipsis` volta a caber, a Unity zera o `tooltip` dele. Use `displayTooltipWhenElided = false` em quem tem dica própria.
- **Altura em listas roláveis:** o tema padrão está vazio; sem `overflow: hidden` e `min-height: 0` o conteúdo passa por cima do que está em volta.
- **Autoteste:**
  - no `WorldSyncSelfCheck`, `_shots` é nulo dentro de `Entering`; o `Snap` já se protege;
  - toggles de abas escondidas não disparam eventos de mudança;
  - para dar exemplo de opção indisponível, use uma que o Godot também não tenha (`graphics.vegetation`), nunca uma que o Unity possa ganhar depois;
  - os testes de sentar antigo e de pulo às vezes falham: rode de novo;
  - o clique no menu do chão usa um mouse simulado e falha de vez em quando ("«Sobre este terreno» abrir uma janela"), ao que parece quando a janela do editor perde o foco ou o mouse de verdade se mexe (a causa não foi confirmada): rode de novo sem mexer no computador. A abertura sozinha do criador de personagem falhou uma vez e não se repetiu.
- **Banco:** uma coluna `jsonb` volta reescrita pelo PostgreSQL (`{"a": 1}`, com espaço) e não aceita operações de texto. Para ler um valor, interprete o JSON; para filtrar numa consulta, use `JsonColumn.Text(coluna)`. Os testes em memória não pegam nenhum dos dois erros.
- **Heredoc:** um heredoc no Bash estraga barras invertidas. Use `chr(92)` em Python ou a ferramenta de edição.
- **WSL:** servidores esquecidos no WSL prendem as portas 17777 e 17070 do Windows.
- **IL2CPP:** não há como gerar com IL2CPP nesta máquina (falta o Visual Studio C++). Os builds são Mono.

---

## 1. Produção (precisa de autorização)

### Servidor novo (27/11/2026)

O jogo passou para outro servidor, com banco de dados novo e vazio.

- **API:** `https://app.lifeverse.euporiagames.com`. **Host:** `38.4.141.223` (é o `LIFEVERSE_HOSTNAME` das ilhas).
- **Clientes:** o endereço padrão do jogo foi trocado no código. Um jogo gerado antes ainda procura o endereço antigo; `LIFEVERSE_API_URL` troca sem gerar de novo.
- **Banco vazio:** as três ilhas oficiais nascem no primeiro cadastro. Os IDs de ilha antigos não valem mais.
- **Conferido de fora às 12:44 UTC:** `/health` responde `Healthy` (versão 0.6.0); as três ilhas oficiais foram criadas pelo primeiro cadastro; a "LifeVerse Plaza" está `running`, com sinal de vida recente, e a porta **UDP 7800** responde ao início da conexão. Nesse momento as ilhas ainda eram Godot (ver 1.1).
- **Supervisor conferido (13:10 UTC):** a "Sandbox Island", desligada, ligou sozinha depois de um pedido de entrada feito com a conta do dono (`starting` → `running`), a porta 7800 respondeu e a API entregou o host `38.4.141.223`. A "Plaza" tinha desligado às 12:53:49, cerca de 10 minutos depois de ligar, como no desligamento por inatividade.
- **Não conferido:** o log do serviço de ilhas, andar e conversar dentro da ilha, a "Tropical Island" e duas ilhas ao mesmo tempo (portas 7801 e 7802). O log de um contêiner com a imagem antiga (uma ilha só) repetia 404 às 12:49: falta pará-lo no Easypanel.

### 1.0 Correções da API (implantadas em 27/11/2026)

Achadas ao rodar a API inteira sobre um PostgreSQL local. Foram enviadas (`4a5259f`) e implantadas no servidor novo. Conferido de fora às 12:48 UTC: `/health` `Healthy` e as três rotas novas respondem 401 sem sessão (antes, 404); a "LifeVerse Plaza" seguiu `running` e a porta UDP 7800 respondendo. **Falta conferir lá, com uma conta**, que cada defeito sumiu:

- **Privacidade ignorada:** perfil marcado como privado mostrava biografia e foto e entrava na busca; presença privada aparecia; "mensagens só de amigos" e "de ninguém" não barravam ninguém.
- **Erro 500:** busca e sugestões quando o resultado tinha pessoas; abas Física e Desempenho da ilha; pagamento feito por script; conferência do inventário.
- **Datas com fuso:** um horário enviado com `-03:00` (por exemplo, ao criar um evento) era recusado.
- **JSON inválido** na privacidade do perfil ou nas provas de uma denúncia dava erro 500 em vez de 400.

As rotas novas de privacidade, bloqueados e sessões foram no mesmo deploy.

**Envio de arquivos (resolvido em 27/11/2026):** a API apontava para nomes que não existem (`Name does not resolve`). Funcionou com `ObjectStorage__Endpoint=http://verselife_minio_minio:9000` (o nome interno do serviço; a 9001 é a do painel web) e a correção de `S3ObjectStore.cs` do commit `3f13a5e` (o MinIO recusa `_` no `Host`, segundo relatos); o dono confirmou depois de ajustar. Se voltar a falhar, `getent hosts <nome>` no console do app da API diz se o nome existe.

### 1.1 Ilhas Unity (no ar desde 27/11/2026)

- Pacote do servidor Linux publicado como pré-lançamento na tag `unity-server-20261127` (SHA-256 `9a08579be897fbaef5740d065b8fca044156e40620ea57200022c92ffd74a72b`). Um pacote novo só é preciso quando o código do servidor de ilha mudar (`powershell -File scripts/package-unity-server.ps1`).
- O dono trocou o serviço no Easypanel e passou as ilhas para `unity` no banco. O serviço antigo precisou ser apagado e recriado (o contêiner Godot nunca era substituído). A "LifeVerse Plaza" ficou `running`, a porta UDP 7800 aceitou o protocolo Unity e **o dono entrou pelo jogo (no editor da Unity) e confirmou que funcionou**, e depois jogou nela.
- Passo a passo: `DEPLOY_UNITY_ISLANDS.md`. Segredos colados no chat durante a implantação ainda precisam ser trocados.

**Nunca provado:**
- conversa, voz, outra ilha ou duas ilhas ao mesmo tempo (portas 7801 e 7802) na produção;
- horas seguidas de uso (memória, vazamento);
- a renovação do token de 15 minutos durante uma sessão longa contra a API real.

## 2. Aposentar o Godot (feito no repositório em 27/11/2026)

Feito: os scripts Lua foram para `unity/UnityIslandServer/Core/Scripting`; os arquivos que o jogo usa foram para `assets/`; `client/`, `island-server/`, o modo Godot do supervisor, o CI e as ferramentas do Godot saíram; a versão mínima do cliente passou a `0.7.0` em `backend/appsettings.json`.

**API reimplantada (27/11/2026, 17:23 UTC, conferido de fora):** `/health` `Healthy`, `/client/release` pede `0.7.0`, as três ilhas oficiais são `unity` e nenhuma `godot`. Estavam `suspended`; às 18:01 UTC, durante o teste do dono, a "LifeVerse Plaza" estava `running` com sinal de vida recente, então **o supervisor segue ligando ilhas depois do commit `dc35fe5`** (de fora não se sabe se o serviço dele foi reconstruído).

O que falta:
- **Apagar o app antigo `island`** no Easypanel, se ainda existir: o Dockerfile dele (`island-server/Dockerfile`) não existe mais, e um novo deploy falharia.
- **Publicar o instalador Unity** como o oficial (seção 6).
- As sobras locais das pastas `client/` e `island-server/` (caches do Godot, `export_presets.cfg`, `LifeVerse.Client.sln`, nunca estiveram no git) foram apagadas nesta máquina a pedido do dono. Em outra máquina elas podem continuar lá; o `.gitignore` as esconde.
- A API ainda aceita o motor `godot` e ele é o padrão da coluna no banco. Tirar isso pede uma migração em produção; não há ganho agora.

## 3. Funcionalidades do Godot que ainda faltam no cliente Unity

Os itens 3.1 (avisos imobiliários), 3.2 (comprar dentro da busca), 3.3 (ícones das conquistas), 3.4 (reencaixe de roupa com esqueleto), 3.8 (voz desligada pelo terreno) e 3.9 (sincronizar as configurações) foram feitos; o detalhe está em `GODOT_PARITY_AUDIT.md`. A numeração dos que ficaram foi mantida.

**Comparação de 27/11/2026:** três rotas que o Godot usava não tinham uso no Unity (`/inventory/items/{id}/contents`, `/huds` e `/worlds/welcome`) e faltavam alguns recursos. No mesmo dia foram feitos o **3.12** (pré-visualizar item), o **3.13** (conteúdo de construção), o **3.14** (andar com duplo clique no chão), o **3.15** (hora e clima só neste computador), o **3.16** ("Primeiros passos"), o **3.17** (redefinir a câmera, R) e o **3.19** (Ilha de Boas-vindas pelo menu). A comparação foi por busca no código (`git show 177c0c1:client/Scripts/...`) e não cobriu cada tela em detalhe.

- **Menu do chão (botão direito):** refeito em 27/11/2026 (ver `GODOT_PARITY_AUDIT.md`). **Os menus do botão direito sobre pessoas, objetos, o próprio avatar e itens não foram comparados com os do Godot** (`ContextInteraction.cs`); ainda faltam no chão comprar ou alugar direto do menu, "selecionar terreno" arrastando e "criar objeto aqui".
- **Fotos (27/11/2026):** perfil, grupo e ilha têm botão para enviar uma imagem do computador ou escolher do inventário, e a busca mostra as três. Imagens que são endereços de outros sites continuam não aparecendo na busca, por escolha de privacidade. A capa de grupo (campo `coverUrl`, endereço https) segue só como endereço.
- **3.18 Editor visual de HUD** (montar, guardar e equipar um HUD; `HudEditor.cs` no Godot, `/api/v1/huds` na API): **não foi feito**; é pouco usado, combinar com o dono antes.
- **O dono testou os recursos novos no editor (27/11/2026) e confirmou que funcionaram.** Não foi detalhado quais prévias ele viu com arquivos do inventário (nos testes automáticos foram arquivos de teste). Falta ver no jogo gerado: o instalador de 27/11 é anterior a estes recursos.

- **Controles de andar (decidido em 27/11/2026: ficam como estão no Unity):** A/D andam de lado em relação à câmera e S vira o corpo e volta andando; Q abre o menu radial. No Godot, A/D giram, Q/Z dão o passo de lado e S anda de costas. A diferença é de propósito; não mudar sem o dono pedir.
- **Corrida na diagonal (27/11/2026):** corria travada; corrigido no cliente (desenho entre os passos de movimento e correções mais suaves da ilha) e o dono confirmou no editor que ficou boa. Não precisa de deploy do servidor.
- **3.3, uma ressalva:** o pedido citava também ícones nas conquistas do **grupo**, mas nem o servidor nem o Godot têm conquistas de grupo. Não há o que desenhar.

- **3.5 (rosto):** conferido no Godot: os sliders do rosto agem **só no corpo simples**, não num corpo GLB próprio (lá só há os do corpo). O que faltava no Unity era o rosto do corpo simples (nariz, boca, queixo, sobrancelhas, largura da cabeça), e foi feito.
- **3.7 (primeira pessoa):** feito como no Godot (o corpo inteiro é desenhado, menos a cabeça e o que está nela; o corpo vira para onde se olha). Os dedos só foram vistos num corpo sintético: falta um corpo de pessoa com ossos de dedo.
- **3.10 (WebP):** feito, em C# puro (`World/Webp`), com e sem perdas e com transparência, conferido pixel a pixel contra o Godot em 56 imagens. De uma animação só o primeiro quadro é lido.

### 3.6 Guarda-roupa do homem que vem com o jogo
- **O problema:** os arquivos das peças do corpo padrão do homem não estão no repositório. O Godot as lê de `BuiltInWearables` e de um catálogo.
- **O que fazer:** conseguir os arquivos com o dono do projeto e então ligá-los em `Avatar/BuiltInBodies.cs` e `AvatarWardrobe`.

### 3.11 Convites de teleporte como opção
- O Unity **tem** convites de teleporte (`UI/LiveEvents.cs`).
- A opção `world.teleport_invites` do catálogo veio do Godot marcada "o jogo ainda não tem convites".
- **O que fazer:** decidir com o dono o que a opção controla (por exemplo, aceitar convites só de amigos) e ligá-la.

## 4. As 19 opções do catálogo que o cliente Unity ainda não cumpre

Elas aparecem desligadas, com o motivo do Godot. A lista sai de `UI/SettingsCatalog.Generated.cs` (os que têm `Unavailable`). Quase todas dependem de **servidor** ou de **decisão de produto**, não só do cliente:

- **Precisam de serviço novo:**
  - `chat.translate`: tradução;
  - `advanced.error_report`: relatórios de erro;
  - `general.language` e `general.date_format`: internacionalização.
- **Precisa de decisão:** `notifications.sound`. O Unity já toca um som em todo aviso (quando "Sons da interface" está ligado), e a opção do catálogo começa **desligada**. Decidir se ela passa a mandar no som dos avisos (e os avisos ficam mudos por padrão) ou se soma ao que existe.
- **Precisam de trabalho no cliente ou de decisão** (as de limite de download, uso de CPU e GPU, verificação dos arquivos e legendas já funcionam; as legendas dizem quem fala e que sons tocam, sem transcrever a fala):
  - `accessibility.screen_reader` (o motor não fala com leitores de tela; precisaria da voz do Windows por um plugin nativo);
  - `perf.preload_assets`: a descrição fala em baixar antes os arquivos das ilhas **favoritas**. O Unity só pré-carrega a ilha de destino (`AssetPreloader`). Ligar a opção a isso mudaria o sentido dela e, como começa desligada, desligaria o pré-carregamento de hoje;
- **Sem sentido hoje** (ficam como estão, a menos que o produto mude):
  - `general.start_place`;
  - `privacy.script_permissions` e `scripts.revoke`;
  - `world.restricted_warnings`;
  - `scripts.theme`;
  - `accessibility.text_scale`;
  - `accessibility.flashes`;
  - `graphics.vegetation`;
  - `avatars.texture_quality`;
  - `perf.shader_cache`;
  - `voice.noise_suppression`: só se houver um filtro de ruído.

Mais duas que só o Unity não cumpre, com o motivo na tela: `audio.output_device` (o motor sai sempre pelo dispositivo padrão do Windows) e `interface.side_bar` (o cliente Unity não tem barra lateral).

**Privacidade e sessões (feitas):** quem vê a presença, quem manda mensagens, quem convida para grupos, quem pede amizade, a lista de bloqueados e as sessões abertas ficam em **Configurações → Privacidade e sessões** (`UI/AccountPrivacyWindow.cs`; no servidor, `Modules/Users/PrivacyChoices.cs` e `Modules/Identity/AccountSessions.cs`). Ficam de fora:
- uma sessão encerrada perde o acesso no pedido seguinte; não foi verificado se uma ilha ou o canal de avisos já abertos por ela são derrubados.

## 5. Aparência: diferenças que ainda se veem

Céu, nuvens, pôr do sol, chuva, tempestade, noite, mar, chão e a vista debaixo d'água seguem o Godot (céu próprio em `World/Resources/LifeVerse/IslandSky.shader`, neblina do tempo em `IslandFog.shader`). As fotos lado a lado, das nove vistas, estão em `img/compare/`. O que ainda difere ou não foi visto:

- **Nuvens:** o desenho do ruído é outro (o Unity não tem o mesmo gerador), então as manchas não coincidem uma a uma.
- **Chão visto rente:** na vista da praia saía um pouco mais claro que no Godot; não foi medido de novo.
- **Desempenho:** o mar, o chão, o céu e a neblina não foram medidos (o `-lifeverse-benchmark` precisa do player). Também não houve comparação lado a lado com o Godot, e só uma placa de vídeo foi usada (RTX 2060 SUPER).
- **No player gerado:** as fotos foram tiradas no editor.

## 5.1. Interface: o que ainda falta em relação ao Second Life

**Direção de arte (27/11/2026):** o dono escolheu a **Varanda** entre três protótipos (`docs/ui-directions/README.md`) e ela **está no jogo**: HUD, conversa, mini-mapa, avisos, nomes, menu radial, janelas, entrada e carregamento, com papel claro de dia e escuro à noite (**Configurações → Interface → Aparência da interface**). **Falta:**

- **o dono usar de verdade**, com mouse e teclado, e dizer o que ajustar;
- as janelas ganharam as cores e as letras, mas **a organização por dentro é a antiga** (formulários, o inventário em lista); redesenhá-las é a próxima etapa;
- os ícones de dentro das janelas continuam os de linha; só a barra, a placa, o alto e o cartão-postal têm os ícones da Varanda;
- o medalhão mostra a inicial, não a foto do perfil;
- com muitos botões na barra, os nomes longos são cortados (a dica diz o nome);
- ver em 1440p, 4K e 32:9, no player gerado, e as animações em movimento.

A organização continua a de um mundo como o Second Life, com tamanho das janelas guardado, barra de ferramentas embaixo ou numa borda, conversas particulares e de grupo em abas e o inventário também como árvore. Ainda faltaria:

- **Janelas:** não há "destacar" abas. O encaixe entre janelas e nas bordas existe (Shift arrasta sem encaixar), mas só foi testado pela regra, não com o mouse.
- **Barra de ferramentas:** é uma só (o Second Life permite várias, cada uma com os seus botões).
- **Conversas:** o histórico é o que a API guarda; o nome de quem escreve num grupo depende de a notificação ao vivo trazê-lo.
- **Verificado só no editor** (1280×720, 1366×768, 1920×1080 e 2560×1080).

## 6. Build, distribuição e CI

- **IL2CPP nunca verificado:** falta o Visual Studio C++ nesta máquina.
  - `BuildClient.BuildWindowsIl2cpp` já confere os pré-requisitos, e há um `link.xml`;
  - riscos: MoonSharp (reflexão), Concentus e `System.Text.Json` sendo cortados no build.
- **Instalador:**
  - **27/11/2026:** pacote 0.7.0 gerado em `artifacts/unity-0.7.0-20261127/` (zip, instalador, `SHA256SUMS.txt`, `release.json`; a pasta `artifacts/unity-0.7.0` é de 20/11 e aponta para a API antiga). O zip extraído foi conferido contra a lista do jogo (230 arquivos) e o `LifeVerse.exe` dele passou na autoverificação sem janela nesta máquina;
  - `installer/LifeVerse.iss` com `/DEdition` gera o instalador Unity, que **nunca foi instalado** (pede administrador e regras de firewall);
  - falta testar instalar, jogar pela versão instalada e atualizar por cima;
  - `scripts/build-unity-client.ps1 -Installer`. Nesta máquina a política de execução do PowerShell bloqueia scripts: o pacote de 27/11 foi montado com os mesmos passos, um comando por vez.
- **Launcher:** adaptar o launcher (`launcher/`) e o manifesto ao formato do build Unity (risco R17).
- **CI** (visto no GitHub em 27/11/2026, commit `dc35fe5`):
  - passam: protocolo e servidor de ilha, a construção do estágio do supervisor da imagem e a compilação dos plugins no Linux;
  - falha o `ci.yml` em `dotnet build LifeVerse.slnx`: ele não roda antes o `build-plugins.ps1` (a parte `netstandard2.1` não acha `LifeVerse.Shared`), então os testes da API nem rodam. Já falhava antes de o Godot sair;
  - os testes e o build da Unity são pulados sem os segredos `UNITY_LICENSE` e afins (licença Unity em CI), e por isso o trabalho de pacote falha ao baixar um build que não existe;
  - os testes do servidor de ilha falharam no CI do commit anterior (`177c0c1`) e passaram neste: podem ser instáveis lá;
  - o build do servidor Linux não está no CI.
- **Avisos de licença:** o build copia `Assets/LifeVerse/Legal/THIRD-PARTY-NOTICES.txt` para junto do jogo, e um teste exige que toda DLL nova esteja nele. Falta conferir que o instalador o leva.
- **Assinatura do executável:** o `.exe` não é assinado, e o Controle Inteligente de Aplicativos do Windows 11 pode bloqueá-lo (aconteceu na máquina de desenvolvimento). Avaliar um certificado de assinatura de código antes de distribuir.
- **Versão:** o cliente Unity é a 0.7.0 (`bundleVersion`), e `release.json` e `SHA256SUMS` são gerados pelo script. Falta publicar uma versão de verdade no endereço que `UpdateCheck` lê.

## 7. Riscos e medições não fechados (`MIGRATION_RISKS.md`)

- **Rede (S2):** testados 64 jogadores atrás de uma internet ruim de mentira: com 60–90 ms de atraso por sentido e 5% de perda, o comando volta confirmado em 233 ms (mediana; 281 ms no 99º percentil) e cada um recebe 15 snapshots por segundo; com 120–180 ms e 10%, 385 ms (460 ms) e 14. O teste achou um defeito (os snapshots de uma ilha cheia chegavam pela metade), corrigido. **Falta a internet de verdade** e outra máquina.
- **Sessão longa do servidor Unity:** 20 minutos com gente entrando e saindo sem parar acharam um defeito de concorrência nos scripts, corrigido (o pacote do servidor Linux precisa ser gerado de um commit com essa correção). Falta medir **horas**, com física, veículos e voz, e a memória do processo inteiro: o teste mede só a parte gerenciada, que foi de 3,2 para 4,8 MB sem a origem separada.
- **Segurança (R12):** há limite de envio por jogador e uma sessão de fuzz de 15 minutos no leitor (1,4 bilhão de leituras, nenhuma exceção); mensagens válidas com valores absurdos não derrubam o servidor (um defeito achado e corrigido nas respostas de script, que vale também para o servidor Godot). Falta rever o limite com jogadores de verdade.
- **Física dos objetos (S4):** comparada em 32 cenários (`unity/PhysicsComparison` e `-lifeverse-physics-report`); a tabela está em `MIGRATION_ISLAND_SERVER.md`. Diferenças que ficaram:
  - bola solta numa rampa de 10°: no Godot ela fica parada, no Unity rola 4 m;
  - quique entre dois corpos elásticos: o Godot soma os dois, o Unity fica com o maior (1,95 contra 2,45 m no cenário medido);
  - 200 corpos: depois de 8 s dormem 100 no Unity e 186 no Godot (as bolas seguem rolando); o custo por passo é bem menor no Unity (0,2 ms contra 4 ms);
  - girar e tombar: o giro no chão e o rolar de uma caixa que cai torta diferem em até 2 vezes;
  - corpos de malha importada e juntas não existem nos dois servidores.
- **Licenças (R16):** as bibliotecas, a fonte, os ícones e as tabelas da libwebp estão nos avisos (seção 6). **Falta o dono confirmar** a licença comercial da Unity para o modelo de negócio e a origem e o direito de distribuição dos arquivos que vão no jogo e não têm licença registrada no repositório: o corpo `lifeverse_man.bin` (Character Creator), as animações (Mixamo e um pacote de captura de movimento), os três sons (`footsteps_concrete`, `keyboard_typing`, `login_theme`) e o fundo da tela de entrada. Roupas do MakeHuman com licença CC-BY, se forem distribuídas, pedem crédito.
- **Requisitos de hardware (R7):** definir e publicar a especificação mínima (HDRP pede GPU moderna).
- **Testes do código compartilhado:** a suíte inteira de `shared` ainda não roda no Test Runner da Unity (Fase 4); só partes rodam.

## 8. Testes que faltam ou que nunca rodaram de verdade

- **Microfone real** e como a voz soa: os testes usam amostras sintéticas.
- **No player gerado:** a última rodada foi verificada só dentro do editor (32/32 na API local, em memória e em PostgreSQL, e 342 verificações no autoteste). Falta repetir com o `.exe`, e as fotos com GPU, quando for possível executá-lo.
- **Teclado e mouse de verdade:** os testes usam entrada por script. Falta jogar uma sessão real, com estes pontos em especial:
  - ALT + V;
  - duplo clique;
  - arrastar botões da barra;
  - arrastar itens do inventário;
  - setas na busca.
- **Corpo próprio:** os sliders foram fotografados num corpo GLB de teste (o boneco do jogo); falta um corpo de pessoa de verdade, e as variações de animação.
- **Mapas:** há fotos do mini-mapa girando e dos terrenos e marcos no mapa-múndi; falta ver os contornos de construções numa ilha construída.
- **Testes de banco:** os testes de PostgreSQL só rodam com `LIFEVERSE_TEST_PG` (sem ela são pulados, e a maioria dos testes usa o banco em memória, que não pega erros de tipo). Rode-os sempre que mexer em consulta. Nunca use a produção.
- **Animações de modelos:** a troca de LOD de malha nunca foi vista num objeto pesado do mundo, e malhas com esqueleto não entram no LOD.
- **Anexos:**
  - pontos de tela (HUD) não são desenhados no corpo;
  - a prévia ao vivo (posição, giro, escala e balanço enquanto se digita) foi verificada no autoteste com um chapéu de teste, não com o mouse.

- **Reencaixe de roupa:** tem teste EditMode com dois esqueletos de proporções diferentes, mas não foi visto com uma roupa de verdade e GPU.

## 9. Pequenas pendências de comportamento

- **Esqueci a senha:** só diz que a recuperação por e-mail não existe. O servidor também não tem essa recuperação (vale para os dois clientes).
- **Pivô dos objetos:** não usado de propósito, porque o Godot também só guarda o dado.
- **Busca:** as miniaturas não carregam imagens de outros sites, por escolha de privacidade. Se o produto quiser, faça respeitando `privacy.external_media`.

## 10. Documentos antigos

`MIGRATION_CHECKLIST.md`, `MIGRATION_SMOKE_MAP.md`, `REMAINING_WORK_2026-11-20.md` e `MIGRATION_PROGRESS.md` foram marcados no topo como **históricos**; a primeira parte de `GODOT_PARITY_AUDIT.md` está marcada como o levantamento original. O conteúdo deles não foi reescrito.

---

## Ordem sugerida

1. **Seção 2:** apagar o app antigo no Easypanel (a API já foi reimplantada).
2. **Seção 6:** gerar e instalar o cliente de verdade, assinatura do executável, IL2CPP e CI. É o que falta para outras pessoas jogarem.
3. **Repetir as verificações no player gerado** quando houver como executá-lo (seção 0).
4. **Seção 8:** uma sessão real com teclado, mouse e microfone.
5. **Seção 7:** internet de verdade e licenças.
6. **Item 3.6** (depende dos arquivos) e um corpo com dedos.
7. **Seções 5 e 5.1:** acabamento visual.
8. **Seção 4 e 3.11:** dependem de servidor ou de decisão de produto; combinar com o dono.
