# O que falta na migração para o Unity (passagem de serviço)

Estado em 27/11/2026, depois do commit `37b6fd1` e de uma rodada **ainda sem commit** (limite de envio e testes de rede no servidor, WebP, rosto e primeira pessoa, céu e neblina próprios, o que faltava da interface, quatro opções). Este é o documento para quem continuar o trabalho, seja pessoa ou IA. Ele junta tudo o que está aberto. Cada item diz onde fica no Godot, onde entraria no Unity, qual API usa e como saber que acabou.

Os outros documentos desta pasta contam a história do trabalho. Alguns estão **desatualizados** (ver a seção 10); quando um deles disser "falta" e este não disser, vale este.

---

## 0. Antes de começar

### Onde está cada coisa
- Cliente Godot (a referência do comportamento): `client/` (`client/Scripts`, `client/Autoloads`). O Godot portátil fica em `.tools/godot-4.7.2/...console.exe`.
- Cliente Unity: `unity/UnityClient/Assets/LifeVerse/`, com as pastas:
  - `UI`: janelas e HUD em UI Toolkit, construídas por código. O visual fica em `Resources/LifeVerse/UI/Theme.uss` e nas texturas de `Resources/LifeVerse/UI/Skin`, desenhadas por `python unity/tools/ui-skin/generate.py`;
  - `World`: terreno, mar, objetos, ambiente;
  - `Avatar`: corpo, câmera, jogador;
  - `Audio`;
  - `Server`: o servidor de ilha Unity, os autotestes e a verificação ao vivo;
  - `Tests/EditMode`.
- Código compartilhado pelos dois motores e pela API: `shared/` (compilado como `LifeVerse.Shared.dll`) e `scripting/` (Lua, MoonSharp).
- Servidor de ilha Unity, sem a Unity: `unity/UnityIslandServer` (`Core`, `Host`, `LoadTest`). O protocolo é o LifeVerse Wire 12, em `unity/UnityWire`, descrito em `WIRE_PROTOCOL.md`.
- API: `backend/` (ASP.NET, Postgres). Testes: `tests/`.
- Convivência dos dois motores: cada ilha tem um motor (`world.islands.engine`).
  - O cliente diz o seu no cabeçalho `X-LifeVerse-Engine`.
  - Um motor diferente recebe 409 `engine_mismatch`.
  - O guia de implantação é `DEPLOY_UNITY_ISLANDS.md`.

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
  Espera-se `event=world_sync_check result=passed checks=339`.
  - A cena inicial (`Bootstrap.unity`) **não tem luz própria**: o sol e a lua são do `IslandEnvironment`. A luz padrão da Unity que estava lá iluminava todas as ilhas, inclusive de noite. O teste de sentar ou o de pulo podem falhar por tempo: rode de novo.
- **Verificação contra a API real, local:**
  1. Suba a API: `dotnet run --project tools/ApiLiveHost -c Release --no-build -- 5099`.
  2. Rode `LifeVerse.exe -lifeverse-live-api http://127.0.0.1:5099`. Espera-se `steps=31 failed=0`.
  3. Mate só o PID que escuta a porta 5099. O banco é em memória e dura enquanto o processo viver: reinicie-o antes de repetir.
- **As mesmas verificações dentro do editor** (quando o `.exe` não pode ser executado): o resultado sai no arquivo dado em `-lifeverse-editor-run`, e o editor se fecha com o código da verificação (0 passou, 2 o autoteste quebrou, 3 passou de 20 minutos).
  ```
  Unity.exe -batchmode -projectPath unity/UnityClient -executeMethod LifeVerse.Unity.EditorTools.EditorChecks.Play -logFile editor.log -lifeverse-editor-run <saida.log> -lifeverse-live-api http://127.0.0.1:5099
  Unity.exe -projectPath unity/UnityClient -executeMethod LifeVerse.Unity.EditorTools.EditorChecks.Play -logFile editor.log -lifeverse-editor-run <saida.log> -lifeverse-world-selfcheck
  ```
- **Fotos do mundo e física, no editor** (`-lifeverse-world-preview <prefixo>` com `-batchmode`; `-lifeverse-physics-report` com `-batchmode -nographics`), pelo mesmo `EditorChecks.Play`. As fotos do Godot saem de `client/Tests/WorldCompareCapture.tscn` (variável `LIFEVERSE_CAPTURE_PATH`), e a física dele de `Godot_console.exe --headless --path unity/PhysicsComparison`.

  O autoteste precisa da **janela** do editor (sem `-batchmode`): sem ela o fim do quadro nunca chega e ele trava. O erro `ArgumentOutOfRangeException` em `UnityEditor.Search`, logo no começo, é do editor.
- **Outros modos de foto e teste:**
  - `-lifeverse-ui-shots <prefixo>`: fotos das telas (no editor, sem `-batchmode`, pelo `EditorChecks.Play`);
  - `-lifeverse-doc-shots <pasta>`, junto do autoteste: as fotos `ui-*.png` das janelas com dados;
  - `-lifeverse-world-preview <prefixo>`: as 9 vistas do mundo;
  - `-lifeverse-e2e <api> <usuário> <senha>`: ponta a ponta, contra uma API e um supervisor de verdade.
- **API:** `dotnet test tests/LifeVerse.Api.Tests`. A última rodada deu 2.566 aprovados e 0 falhas; 39 testes de Postgres pedem `LIFEVERSE_TEST_PG`.
- **Protocolo e servidor de ilha:** `dotnet test tests/LifeVerse.Wire.Tests` (33) e `dotnet test tests/LifeVerse.IslandServer.Tests` (63, com os 64 jogadores em rede ruim e as entradas hostis). Para sessões longas: `LIFEVERSE_FUZZ_SECONDS` (fuzz do leitor), `LIFEVERSE_FUZZ_SEED` e `LIFEVERSE_FUZZ_MESSAGES` (mensagens absurdas ao servidor) e `LIFEVERSE_LAG_SECONDS`.
- **Imagens WebP de referência:** `Godot_console.exe --headless --script unity/tools/webp-fixtures/make_fixtures.gd -- <repo>\tests\fixtures\webp`.
- **Pilha completa em WSL** (Postgres, API, supervisor Unity e servidor Linux): foi montada com scripts `e2e/up.sh` e `e2e/down.sh` na pasta temporária da sessão. Esses scripts não estão no repositório; o roteiro está no fim de `DEPLOY_UNITY_ISLANDS.md`.

### Duas travas desta máquina (descobertas em 26/11/2026)
- **O Controle Inteligente de Aplicativos do Windows bloqueia o `LifeVerse.exe` gerado** ("uma política de Controle de Aplicativo bloqueou este arquivo"; eventos 3077 e 3118 em `Microsoft-Windows-CodeIntegrity/Operational`). Enquanto isso, nada que dependa do `.exe` roda nesta máquina. Os testes EditMode e o build funcionam, e o autoteste e a verificação ao vivo rodam **dentro do editor** (ver os comandos acima). É uma proteção do Windows: só o dono decide o que fazer com ela. Vale também para os jogadores: um `.exe` sem assinatura pode ser bloqueado no computador deles (ver a seção 6).
- **O `build-plugins.ps1` não roda sem liberar a política de execução do PowerShell.** Para um só projeto, dá para compilar com `dotnet build` (os alvos estão no próprio script) e copiar a DLL.

### Regras combinadas com o dono do projeto
- **Fale em português** com ele.
- **Commit e push só quando ele pedir.** O normal é commitar no `main`.
- **Nunca use credenciais reais contra a produção** (`api.nerdplus.com.br`).
- **Peça autorização antes** de qualquer mudança de produção, de banco ou irreversível: migração aplicada em produção, troca do motor de uma ilha, deploy.
- **Nunca mate processos pelo nome;** só o PID que você mesmo iniciou.
- **Faça cada item inteiro** sem perguntar se deve continuar.
- **Atualize sempre a documentação:** depois de cada rodada, `GODOT_PARITY_AUDIT.md` (uma seção "Feito…" com o que foi verificado) e este arquivo.
- **Verifique cada rodada** com os testes EditMode, o autoteste, a verificação ao vivo e, se for visual, fotos com GPU.

### Armadilhas conhecidas
- **Botões:** um `Button` com elementos filhos para de medir o próprio texto. Ícones são desenhados pelo `UiKit.WithIcon`, com `generateVisualContent`.
- **Desenho sem GPU:** o `generateVisualContent` não roda com `-nographics`. Contagens de desenho só podem ser conferidas com GPU.
- **Autoteste:**
  - no `WorldSyncSelfCheck`, `_shots` é nulo dentro de `Entering`; o `Snap` já se protege;
  - toggles de abas escondidas não disparam eventos de mudança;
  - para dar exemplo de opção indisponível, use uma que o Godot também não tenha (`graphics.vegetation`), nunca uma que o Unity possa ganhar depois;
  - os testes de sentar antigo e de pulo às vezes falham: rode de novo.
- **Heredoc:** um heredoc no Bash estraga barras invertidas. Use `chr(92)` em Python ou a ferramenta de edição.
- **WSL:** servidores esquecidos no WSL prendem as portas 17777 e 17070 do Windows.
- **IL2CPP:** não há como gerar com IL2CPP nesta máquina (falta o Visual Studio C++). Os builds são Mono.

---

## 1. Produção: pôr as ilhas Unity no ar (precisa de autorização)

Nada disto foi feito na produção. O passo a passo está em `DEPLOY_UNITY_ISLANDS.md`.

1. **Push da API com a migração `Phase104IslandEngine`.** O push já foi feito. Confirmar que a API subiu e aplicou a migração: ela cria a coluna `engine` com `godot` em todas as ilhas.
2. **Pacote do servidor Linux:**
   - rode `powershell -File scripts/package-unity-server.ps1` (gera `artifacts/LifeVerseServer-linux.tar.gz` e mostra o SHA-256);
   - suba o arquivo num endereço público, sem login.
3. **Serviço novo no Easypanel** (`lifeverse-islands-unity`):
   - Dockerfile `island-supervisor/Dockerfile.unity`;
   - argumentos de build `SERVER_URL` e `SERVER_SHA256`;
   - variáveis `LIFEVERSE_API_URL`, `LIFEVERSE_SERVICE_KEY` e `LIFEVERSE_HOSTNAME`;
   - portas UDP 7800 a 7807.
4. **Passar uma ilha de teste para `unity`** com `PUT /api/v1/admin/islands/{id}/engine`. Exige estar em `LIFEVERSE_ADMIN_USER_IDS` e mandar `X-Admin-MFA`.
5. **Conferir** com `-lifeverse-e2e https://api.nerdplus.com.br <conta de teste> <senha>`. A conta de teste é do dono do projeto; peça a ele.

**Nunca provado:**
- a imagem Docker em si (não havia Docker aqui);
- o Easypanel;
- UDP pela internet;
- HTTPS contra a API de produção;
- horas seguidas de uso (memória, vazamento);
- a renovação do token de 15 minutos durante uma sessão longa contra a API real.

## 2. Quando o Godot for aposentado (não fazer antes)

Fazer isto hoje quebraria a imagem Godot em produção ou deixaria os clientes Godot de fora.

1. **Scripts Lua do servidor:**
   - o `Core` Unity compila `island-server/Scripts/IslandScripting*.cs` direto da pasta do Godot (`Compile Include`);
   - mover essas fontes para `unity/UnityIslandServer/Core` e tirar o `Compile Include`;
   - ver `MIGRATION_ISLAND_SERVER.md`, seção "Scripts".
2. **Versão mínima do cliente:**
   - subir a versão mínima (`ClientRelease`, lida em `/client/release`) para a do cliente Unity, que hoje é 0.7.0;
   - publicar o instalador Unity como o oficial.
3. **Ilhas e serviços:**
   - trocar o motor de todas as ilhas para `unity`;
   - desligar o serviço de ilhas Godot.
4. **Limpeza:** tirar `client/`, `island-server/` e o supervisor em modo Godot do repositório e do CI.

## 3. Funcionalidades do Godot que ainda faltam no cliente Unity

Das 95 rotas da API que o Godot usava e o Unity não usava, **todas** têm uso agora. Os itens 3.1 (avisos imobiliários), 3.2 (comprar dentro da busca), 3.3 (ícones das conquistas), 3.4 (reencaixe de roupa com esqueleto), 3.8 (voz desligada pelo terreno) e 3.9 (sincronizar as configurações) foram feitos; o detalhe está em `GODOT_PARITY_AUDIT.md`. A numeração dos que ficaram foi mantida.

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

## 4. As 25 opções do catálogo que o cliente Unity ainda não cumpre

Elas aparecem desligadas, com o motivo do Godot. A lista sai de `UI/SettingsCatalog.Generated.cs` (os que têm `Unavailable`). Quase todas dependem de **servidor** ou de **decisão de produto**, não só do cliente:

- **Precisam de preferência de conta no servidor** (campo novo e rota em `backend/Modules/Users`, depois o cliente):
  - `privacy.online_visibility`;
  - `privacy.private_messages`;
  - `privacy.group_invites`;
  - `privacy.friend_requests`.
- **Precisam de rota nova:**
  - `privacy.blocked`: listar e desbloquear;
  - `account.sessions`: listar e encerrar as sessões de outros aparelhos.
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

## 5. Aparência: diferenças que ainda se veem

Céu, nuvens, pôr do sol, chuva, tempestade, noite, mar, chão e a vista debaixo d'água seguem o Godot (céu próprio em `World/Resources/LifeVerse/IslandSky.shader`, neblina do tempo em `IslandFog.shader`). As fotos lado a lado, das nove vistas, estão em `img/compare/`. O que ainda difere ou não foi visto:

- **Nuvens:** o desenho do ruído é outro (o Unity não tem o mesmo gerador), então as manchas não coincidem uma a uma.
- **Chão visto rente:** na vista da praia saía um pouco mais claro que no Godot; não foi medido de novo.
- **Desempenho:** o mar, o chão, o céu e a neblina não foram medidos (o `-lifeverse-benchmark` precisa do player). Também não houve comparação lado a lado com o Godot, e só uma placa de vídeo foi usada (RTX 2060 SUPER).
- **No player gerado:** as fotos foram tiradas no editor.

## 5.1. Interface: o que ainda falta em relação ao Second Life

A interface segue o visualizador do Second Life, com tamanho das janelas guardado, barra de ferramentas embaixo ou numa borda, conversas particulares e de grupo em abas e o inventário também como árvore. Ainda faltaria:

- **Janelas:** não há "destacar" abas nem encaixe entre janelas.
- **Barra de ferramentas:** é uma só (o Second Life permite várias, cada uma com os seus botões).
- **Conversas:** o histórico é o que a API guarda; o nome de quem escreve num grupo depende de a notificação ao vivo trazê-lo.
- **Texto:** só a fonte Inter; não há uma fonte de exibição própria para o jogo.
- **Verificado só no editor**, em 1280×720; falta ver no player gerado e em telas grandes.

## 6. Build, distribuição e CI

- **IL2CPP nunca verificado:** falta o Visual Studio C++ nesta máquina.
  - `BuildClient.BuildWindowsIl2cpp` já confere os pré-requisitos, e há um `link.xml`;
  - riscos: MoonSharp (reflexão), Concentus e `System.Text.Json` sendo cortados no build.
- **Instalador:**
  - `installer/LifeVerse.iss` com `/DEdition` gera o instalador Unity, que **nunca foi instalado** (pede administrador e regras de firewall);
  - falta testar instalar e atualizar por cima;
  - `scripts/build-unity-client.ps1 -Installer`.
- **Launcher:** adaptar o launcher (`launcher/`) e o manifesto ao formato do build Unity (risco R17).
- **CI:**
  - `.github/workflows/unity.yml` nunca rodou; precisa dos segredos `UNITY_LICENSE` e afins (licença Unity em CI);
  - o build do servidor Linux também não está no CI.
- **Assinatura do executável:** o `.exe` não é assinado, e o Controle Inteligente de Aplicativos do Windows 11 pode bloqueá-lo (aconteceu na máquina de desenvolvimento). Avaliar um certificado de assinatura de código antes de distribuir.
- **Versão:** o cliente Unity é a 0.7.0 (`bundleVersion`), e `release.json` e `SHA256SUMS` são gerados pelo script. Falta publicar uma versão de verdade no endereço que `UpdateCheck` lê.

## 7. Riscos e medições não fechados (`MIGRATION_RISKS.md`)

- **Rede (S2):** testados 64 jogadores atrás de uma internet ruim de mentira: com 60–90 ms de atraso por sentido e 5% de perda, o comando volta confirmado em 233 ms (mediana; 281 ms no 99º percentil) e cada um recebe 15 snapshots por segundo; com 120–180 ms e 10%, 385 ms (460 ms) e 14. O teste achou um defeito (os snapshots de uma ilha cheia chegavam pela metade), corrigido. **Falta a internet de verdade** e outra máquina.
- **Segurança (R12):** há limite de envio por jogador e uma sessão de fuzz de 15 minutos no leitor (1,4 bilhão de leituras, nenhuma exceção); mensagens válidas com valores absurdos não derrubam o servidor (um defeito achado e corrigido nas respostas de script, que vale também para o servidor Godot). Falta rever o limite com jogadores de verdade.
- **Física dos objetos (S4):** comparada em 32 cenários (`unity/PhysicsComparison` e `-lifeverse-physics-report`); a tabela está em `MIGRATION_ISLAND_SERVER.md`. Diferenças que ficaram:
  - bola solta numa rampa de 10°: no Godot ela fica parada, no Unity rola 4 m;
  - quique entre dois corpos elásticos: o Godot soma os dois, o Unity fica com o maior (1,95 contra 2,45 m no cenário medido);
  - 200 corpos: depois de 8 s dormem 100 no Unity e 186 no Godot (as bolas seguem rolando); o custo por passo é bem menor no Unity (0,2 ms contra 4 ms);
  - girar e tombar: o giro no chão e o rolar de uma caixa que cai torta diferem em até 2 vezes;
  - corpos de malha importada e juntas não existem nos dois servidores.
- **Licenças (R16):** verificar a licença comercial da Unity para o modelo de negócio e a licença da fonte (Inter) antes de distribuir. As tabelas do decodificador WebP vêm da libwebp (BSD de 3 cláusulas, Google): a distribuição precisa incluir o aviso.
- **Requisitos de hardware (R7):** definir e publicar a especificação mínima (HDRP pede GPU moderna).
- **Testes do código compartilhado:** a suíte inteira de `shared` ainda não roda no Test Runner da Unity (Fase 4); só partes rodam.

## 8. Testes que faltam ou que nunca rodaram de verdade

- **Microfone real** e como a voz soa: os testes usam amostras sintéticas.
- **No player gerado:** a última rodada foi verificada só dentro do editor (31/31 na API local e 339 verificações no autoteste). Falta repetir com o `.exe`, e as fotos com GPU, quando for possível executá-lo.
- **Teclado e mouse de verdade:** os testes usam entrada por script. Falta jogar uma sessão real, com estes pontos em especial:
  - ALT + V;
  - duplo clique;
  - arrastar botões da barra;
  - arrastar itens do inventário;
  - setas na busca.
- **Corpo próprio:** os sliders foram fotografados num corpo GLB de teste (o boneco do jogo); falta um corpo de pessoa de verdade, e as variações de animação.
- **Mapas:** há fotos do mini-mapa girando e dos terrenos e marcos no mapa-múndi; falta ver os contornos de construções numa ilha construída.
- **Testes de banco:** os 39 testes de Postgres da API pedem um banco de teste (`LIFEVERSE_TEST_PG`). Nunca use a produção.
- **Animações de modelos:** a troca de LOD de malha nunca foi vista num objeto pesado do mundo, e malhas com esqueleto não entram no LOD.
- **Anexos:**
  - pontos de tela (HUD) não são desenhados no corpo;
  - o balanço (sway) só aparece depois de "Aplicar", sem prévia ao vivo.

- **Reencaixe de roupa:** tem teste EditMode com dois esqueletos de proporções diferentes, mas não foi visto com uma roupa de verdade e GPU.

## 9. Pequenas pendências de comportamento

- **Esqueci a senha:** só diz que a recuperação por e-mail não existe. O servidor também não tem essa recuperação (vale para os dois clientes).
- **Pivô dos objetos:** não usado de propósito, porque o Godot também só guarda o dado.
- **Busca:** as miniaturas não carregam imagens de outros sites, por escolha de privacidade. Se o produto quiser, faça respeitando `privacy.external_media`.

## 10. Documentos antigos

`MIGRATION_CHECKLIST.md`, `MIGRATION_SMOKE_MAP.md`, `REMAINING_WORK_2026-11-20.md` e `MIGRATION_PROGRESS.md` foram marcados no topo como **históricos**; a primeira parte de `GODOT_PARITY_AUDIT.md` está marcada como o levantamento original. O conteúdo deles não foi reescrito.

---

## Ordem sugerida

1. **Repetir as verificações no player gerado** quando houver como executá-lo (seção 0).
2. **Seção 1** (produção), quando o dono autorizar: é o que põe o Unity na mão dos jogadores. O servidor de ilha Unity precisa ser do mesmo commit do cliente (a entrega dos snapshots mudou).
3. **Seção 8:** uma sessão real com teclado, mouse e microfone.
4. **Seção 6:** IL2CPP, instalador, assinatura do executável e CI, antes de distribuir.
5. **Seção 7:** internet de verdade e licenças.
6. **Item 3.6** (depende dos arquivos) e um corpo com dedos.
7. **Seções 5 e 5.1:** acabamento visual.
8. **Seção 4 e 3.11:** dependem de servidor ou de decisão de produto; combinar com o dono.
9. **Seção 2**, só no fim.
