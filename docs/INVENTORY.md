# Inventário 2.0

O inventário global guarda, para cada pessoa, **itens** que apontam para um **asset** (e uma versão dele). O que está colocado no mundo é outra coisa: uma **instância** (objeto de construção ou instância de asset). Essas quatro camadas continuam separadas: organizar, mover, apagar ou restaurar mexe só no *item*.

## Fase 1 — organização, busca e lixeira

### Pastas e subpastas
- Árvore de pastas por pessoa: até **8 níveis** e **500 pastas**; nome de 1 a 120 caracteres, sem caracteres de controle, único entre irmãs (sem diferenciar maiúsculas de minúsculas).
- Criar, renomear, mover e excluir passam por `InventoryOrganizer` (`backend/Modules/Inventory`). Uma pasta nunca vai para dentro dela mesma nem de uma pasta que está dentro dela, e a profundidade conta o que vai junto com ela (`FolderRules`, aritmética pura e testada).
- Pasta de outra pessoa, pasta que não existe ou que está na lixeira nunca serve de destino (404).
- **Alterações na forma da árvore são serializadas por pessoa** no PostgreSQL (trava consultiva até salvar): duas mudanças ao mesmo tempo poderiam, cada uma sozinha, parecer válidas e juntas formar um laço. O teste com PostgreSQL real faz oito conexões mexerem nas mesmas pastas e confere que não sobra laço, pasta funda demais nem item numa pasta alheia ou inexistente.

### Mover (atômico)
`POST /api/v1/inventory/move` `{ itemIds, folderIds, targetFolderId }` move itens e pastas juntos, de uma vez só: se qualquer um não puder ir (não é seu, está na lixeira, o destino não existe, formaria laço, passaria da profundidade, repetiria um nome), **nada** é movido. A interface usa esta rota ao arrastar um item ou uma pasta para outra pasta na árvore.
Antes `PATCH /items/{id}` trocava a pasta do item para vazio sempre que o campo faltava (renomear tirava o item da pasta) e aceitava pasta alheia; agora a pasta só muda se for enviada (`folderId`, ou `clearFolder: true` para tirar) e tem de ser sua.

### Lista, busca, filtros, ordem, páginas
`GET /api/v1/inventory/items` agora faz tudo no servidor:
- `view`: `all`, `favorites`, `recent`, `trash`; `folderId` (+ `includeSubfolders`) ou `unfiled`;
- `search`: até 6 palavras, todas precisam aparecer no nome do item, no nome ou descrição do asset, ou no tipo; sem diferenciar maiúsculas; `%` e `_` valem como texto;
- filtros: `types` (lista), `rights` (todos os direitos pedidos: copy, modify, transfer, resell), `minSize`, `maxSize`, `from`, `to`;
- `sort`: `name`, `date`, `updated`, `size`, `type`, `recent` com `dir` asc/desc; sem `sort` vale a ordem de sempre (favoritos primeiro, depois os mais novos). O desempate é sempre o identificador, então as páginas não repetem nem perdem itens.
- Páginas de até 100 (a interface usa 50, com "Carregar mais"); a resposta traz `total` e `hasMore`.
`GET /inventory/summary` devolve os números das seções; `GET /inventory/folders` traz a quantidade de itens de cada pasta.

### Recentes e favoritos
Um item é *recente* se foi usado (colocado, equipado, colocado por construção do inventário, ou marcado em `POST /items/{id}/used`) ou criado nos últimos 14 dias; a coluna nova `LastUsedAt` guarda o uso. Favoritos já existiam; a seção ganhou o mesmo caminho de lista.

### Lixeira
- Excluir um item o manda para a lixeira; excluir uma **pasta** manda a pasta, as subpastas e tudo que está dentro, num passo só. Itens que não podem ir (em oferta) saem da pasta em vez de se perderem com ela.
- **Restaurar um item** o devolve; se a pasta dele não existe mais, ele volta para fora das pastas. **Restaurar uma pasta** traz de volta o que foi apagado junto com ela (e só isso): se a pasta-mãe continua na lixeira ela volta para o topo, e se o nome foi tomado nesse meio-tempo vira "Nome (2)".
- **Excluir definitivamente** (item, pasta ou "Esvaziar a lixeira"): o item vira `purged`; a linha fica no banco (a história e a auditoria continuam apontando para ele), mas some de todas as listas e não volta. Fica registrado na auditoria de itens.
- Expiração: o que está na lixeira há mais de **30 dias** é eliminado por um serviço de fundo (`InventoryTrashWorker`, de hora em hora).
- Itens `consumed` (usados ao serem colocados no mundo) e `purged` nunca aparecem na lixeira.

### Interface
Árvore com subpastas, seções *Meu Inventário*, *Recentes*, *Favoritos* e *Lixeira* (pastas apagadas aparecem sob a lixeira com a quantidade de itens), filtro por tipo, ordem, "Com subpastas", "Carregar mais", busca com pausa de 0,3 s antes de perguntar ao servidor, arrastar pastas e itens na árvore e mensagens que explicam por que o servidor recusou uma mudança.

### Limites desta fase
- O indicador de armazenamento ainda soma só o que está carregado (a fase 6 traz o total e a cota de verdade).
- Arrastar vários itens de uma vez ainda não existe na tela (a rota de mover já aceita vários).
- A paginação é por página numerada com ordem estável (não por cursor): em listas enormes mudando durante a leitura, um item pode aparecer numa página vizinha.

### Testes
`FolderRulesTests` e `InventoryOrganizerTests` (regras da árvore, mover, lixeira, restaurar, expirar, recentes, busca, filtros, ordem, páginas estáveis, nunca ver o inventário de outra pessoa), `InventoryPostgresTests` (todas as combinações de filtro e ordem em PostgreSQL real; oito conexões alterando a mesma árvore ao mesmo tempo) e blocos novos em `InventorySmoke`.

## Fase 2 — pré-visualização

"Pré-visualizar…" (menu do item e menu "+") abre uma janela que mostra o que o item realmente é, sem colocar nada no mundo e sem mudar nada. Duplo clique em textura, som ou animação abre a prévia (em modelo e em construção continua a colocar no mundo). Abrir uma prévia conta como uso: o item entra em *Recentes*.

- **Modelo 3D:** gira sozinho num palco próprio (arrastar gira à mão, a roda aproxima e afasta, "Reenquadrar" volta ao começo) e mostra o tamanho real em metros, triângulos, materiais, texturas, peças e animações embutidas, lidos do arquivo pelo mesmo inspetor da importação.
- **Textura:** a imagem sobre um fundo xadrez (a transparência aparece como transparência), com tamanho em pixels, "Ajustar à janela", 100%, zoom pela roda ou pelos botões (de 2% a 3200%) e arrastar para mover.
- **Animação:** o arquivo é lido como o jogo o lê (`SkeletonClip`); o esqueleto humanoide padrão é desenhado e posto pelo quadro da animação (`StickFigure`, em `shared/Avatar`, cujos membros nunca mudam de comprimento). Lista todas as animações do arquivo, toca, pausa, volta ao início, arrasta pela barra de tempo, repete ou não, velocidade de 0,25× a 2×, e diz duração, ossos, chaves, se move o corpo todo e se emenda no início. Um arquivo sem animação reconhecida diz isso em vez de mostrar um boneco parado.
- **Áudio:** toca, pausa, para, barra de posição (arrastável), volume e repetir. Para WAV (PCM de 8, 16, 24 e 32 bits e ponto flutuante, decodificado por `WavReader` sem confiar em nenhum número do cabeçalho) mostra a forma de onda com a posição; OGG toca sem forma de onda. Áudio ilegível é explicado, nunca tocado.

**De onde vêm os bytes.** `AssetBytes` pega o arquivo da memória (6 últimos), do disco (`user://asset_cache`, por hash) ou baixa do servidor. Todo arquivo com hash é conferido contra o hash do inventário: se for diferente, é recusado e não é guardado. Acima de 40 MB não se baixa para pré-visualizar. Abrir outro item enquanto um carrega descarta o resultado antigo.

**Limites.** A janela foi testada por comportamento (tamanhos, tempos, reprodução, mensagens), mas a aparência não foi conferida numa janela de verdade neste ambiente sem tela. A animação aparece como esqueleto simples, não no avatar (os arquivos de animação do jogo só tocam em corpos personalizados). O preview de modelo mostra o arquivo, não o modelo já ajustado ao tamanho de colocação.

**Testes:** `StickFigureTests`, `WavReaderTests` (incluindo todos os truncamentos e bytes corrompidos de um arquivo) e um bloco novo em `InventorySmoke` (um modelo, uma textura, um arquivo com duas animações, um WAV tocando, arquivos inválidos, hash errado, arquivo enorme, tipo sem prévia, falha de download).

## Fase 3 — entregas, histórico, direitos e detecção de duplicações

As entregas entre jogadores (oferecer, aceitar, recusar, cancelar, expirar em 7 dias, ou presente com cópia) e o histórico de donos de cada item já existiam desde os direitos de itens; a camada de itens garante que um item nunca está em dois inventários. Esta fase completa o que faltava em volta disso.

### Quem recebe fica sabendo
Ao oferecer, o destinatário recebe na hora um aviso ("ana quer te dar “Sofá”. Veja em Ofertas de itens."); quando ele responde, quem deu é avisado ("bia recusou “Sofá”."); quando quem deu cancela, o destinatário é avisado. Os avisos usam o mesmo canal ao vivo dos pedidos de amizade (`item_offer`, `item_offer_answer`), não derrubam a entrega se falharem e não se repetem se o mesmo pedido for reenviado. Com a janela de ofertas ou o inventário abertos, a lista se atualiza sozinha.

### Ver os direitos antes de aceitar
Cada oferta traz `arrives`: os direitos com que o item chega a quem recebe (os "direitos do próximo dono"). A janela mostra "Você receberá com estes direitos: Copiar · Modificar…" antes do botão Aceitar, e quem oferece vê a mesma promessa. Os cartões do inventário em lista mostram as letras dos direitos de cada item (`C M T R`, com `-` onde falta).

### Histórico de transferências
`GET /api/v1/inventory/transfers` lista **todas** as entregas da pessoa, em qualquer situação (esperando, aceita, recusada, cancelada, expirada), da mais nova para a mais antiga, com filtros de direção (recebidas/enviadas), resultado, palavra (item ou a outra pessoa; o próprio nome não conta), período e páginas de até 100 (a janela usa 20, com "Carregar mais"). Só as duas pessoas da entrega a veem, cada uma na sua lista. Item apagado e conta removida aparecem como "item removido" e "conta removida": o registro continua inteiro. Na janela *Ofertas de itens* é a terceira aba, **Histórico**. O histórico de donos de **um** item continua em *Direitos e propriedades*.

### Detecção de duplicações e itens presos
`GET /api/v1/inventory/integrity` confere o inventário da pessoa (e `InventoryIntegrityWorker` confere o de todos a cada 6 horas e avisa no log). Regras:
- `stuck_offered`: item reservado por uma oferta que não existe mais (não dá para usar nem oferecer);
- `orphan_offer`: oferta esperando resposta cujo item não está mais reservado para ela;
- `double_offer`: o mesmo item em duas ofertas ao mesmo tempo (qualquer uma poderia ser aceita: seria entregue duas vezes);
- `owner_history_mismatch`: o dono de um item não é o último do histórico de donos;
- `missing_copy`: oferta aceita que prometia uma cópia que não existe;
- `uncopyable_in_many_objects`: algo que não pode ser copiado está em mais de um objeto da mesma pessoa.
`POST /inventory/integrity/repair` corrige só o que tem uma resposta segura: devolve o item preso, fecha a oferta sem item reservado e, entre duas ofertas do mesmo item, mantém a mais antiga. O resto é só informado: decidir quem deve ficar com o quê é de uma pessoa, não de um programa. No inventário: menu "+" → "Verificar duplicações e itens presos" e "Corrigir itens presos por ofertas".
O teste de carga com 16 conexões negociando ao acaso (presentes, vendas, cópias, devoluções) agora termina conferindo que a verificação não acha nada.

### Limites
- Compras e vendas no mercado e cópias não entram no histórico de *entregas* (têm o histórico de donos do item e os pedidos do mercado).
- A detecção olha o estado do banco; ela não prova a ausência de duplicação, só procura os sinais que as regras conhecem.
- Quem pode receber é escolhido pelo nome completo na janela de direitos, como antes.

### Testes
`ItemDeliveryTests` (histórico, filtros, páginas, registro inteiro, direitos que chegam, avisos ao vivo, cada regra da verificação e cada correção), `ItemDeliveryPostgresTests` (consultas do histórico e da verificação em PostgreSQL real), a verificação incluída no teste de carga `ItemRightsChaosTests`, e blocos novos em `ItemRightsSmoke`.

## Fase 4 — pacotes, conteúdo interno e recuperação de objetos

### Dois tipos de pacote
- **Construção salva** (já existia): um item da categoria "pacote" com os objetos de uma construção, com árvore e pivôs. Agora dá para **olhar dentro** sem colocá-la (veja abaixo).
- **Pacote de arquivos (.zip)**: novo. Menu "+" → "Importar pacote (.zip)…" descompacta vários arquivos de uma vez para o inventário.
**Não existe** (ainda) uma "caixa" que junte itens já existentes do inventário para entregar de uma vez; isso mexeria em oferta, venda e devolução de cada item e fica para depois, se for desejado.

### Descompactar um .zip (`POST /api/v1/inventory/packs/unpack`)
Todos os arquivos são conferidos **antes** de qualquer coisa ser criada; se um só falhar, **nada** é criado e a resposta lista cada arquivo com problema e o motivo.
- Regras do envio comum, para cada arquivo: tipo (modelos `.glb/.gltf`, imagens `.png/.jpg/.webp`, sons `.ogg/.wav`), tamanho, cabeçalho, sem scripts embutidos, sem referências a fora.
- Regras próprias de zip: no máximo 100 arquivos, 50 MB por arquivo e 300 MB no total (contados pelo que o arquivo **realmente** tem, não pelo que o cabeçalho diz); caminhos que sobem (`..`), absolutos ou com letra de unidade são recusados; zip dentro de zip é recusado; arquivos que se expandem mais de 200 vezes (bomba de compressão) são recusados; pastas, `__MACOSX` e arquivos ocultos são ignorados; zip danificado é uma recusa, nunca um erro.
- Se está tudo certo, vira uma pasta nova com o nome do zip (com "(2)" se já existir) e um item por arquivo, marcado como *descompactado*, num único passo. Arquivos que já estão guardados (mesmos bytes) são reaproveitados, como num envio comum. Fica registrado na auditoria.

### Dentro de uma construção (`GET /api/v1/inventory/items/{id}/contents`)
"Pré-visualizar…" numa construção mostra as peças em árvore (com scripts, animações, itens dentro e pivôs de cada uma), quantos conjuntos há e **de que ela é feita** (modelos, texturas, conteúdos) com ✓ para o que você tem e ✗ para o que falta, antes de colocá-la. Só o dono vê, e ninguém recebe o documento da construção.

### Devolvidos e recuperados
- Objetos **devolvidos** ao inventário (por você, pelo dono do terreno ou por um administrador) chegam como uma construção marcada *devolvido*; a seção **Devolvidos e recuperados** os junta. Para colocá-los de novo é só usar o item.
- Objetos **apagados** do mundo ficam recuperáveis por **30 dias**: "Objetos apagados…" (seção do inventário e menu "+") lista os que você apagou (inclusive os que um administrador apagou) e "Recuperar" os põe no inventário como **uma** construção marcada *recuperado*. Nunca voltam direto ao mundo: as regras de onde e o que pode ser colocado valem quando você os coloca.
- Atômico: se um dos escolhidos não pode ser recuperado (não é seu, já voltou, já foi devolvido, venceu o prazo), nenhum é. Cada objeto só pode ser recuperado **uma vez**, mesmo com várias requisições ao mesmo tempo (testado em PostgreSQL com 8 simultâneas: uma vence).
- Conjuntos apagados peça por peça perdem o grupo conforme as outras saem; a recuperação **reconstrói** os conjuntos pelos ponteiros de pai que cada peça guarda (e pelo grupo que ainda dividam), garantindo uma única raiz.

### Objetos com conteúdo interno
O conteúdo de um objeto (scripts, animações, sons…) já existia na aba *Conteúdo* do editor e viaja dentro das construções salvas e recuperadas; agora também aparece na lista de peças de uma construção, no inventário.

### Limites
- Não há limpeza automática de objetos apagados do banco: o prazo de 30 dias só decide o que dá para recuperar.
- Objetos perdidos por exclusão de uma ilha inteira não entram na lista (só os apagados com a ilha de pé).
- Os arquivos de um .zip chegam sem direitos especiais (todos os direitos, como num envio comum), já que quem os envia é quem os tem.

### Testes
`InventoryPacksTests` (devolvidos x apagados, recuperação com árvore e pivôs, colocar de novo, tudo-ou-nada, conteúdo de construção, zips bons e ruins), `RecoveryRelinkTests`, `InventoryPacksPostgresTests` (recuperação simultânea) e blocos novos em `InventorySmoke`.

## Fase 5 — versionamento e atualizações de produtos

Antes desta fase todo asset tinha uma só versão (a tabela de versões existia, mas nada criava a segunda). Agora o criador publica novas versões, volta a uma anterior se a nova der problema, e quem tem o item é avisado.

### Como funciona
- **Versão em uso.** O asset tem uma versão "em uso" e é essa que **todos** recebem: objetos já colocados no mundo, pré-visualização, miniaturas, lista do inventário. Publicar uma versão nova, portanto, **muda o que está colocado no mundo**; por isso o criador é convidado a escrever o que mudou. (Quem já está na ilha vê a versão nova ao entrar de novo: o cache de modelos da sessão não é invalidado ao vivo.)
- **Publicar** (`POST /api/v1/assets/{id}/versions`, arquivo + nota de até 400 caracteres): só o criador. O arquivo passa pelas mesmas conferências de um envio comum e tem de ser do mesmo tipo do asset (modelo com modelo, imagem com imagem, som com som); o mesmo conteúdo da versão em uso é recusado ("igual"); no máximo 50 versões. A numeração só sobe. Publicações e trocas do mesmo asset fazem fila (no PostgreSQL, trava consultiva), então duas ao mesmo tempo nunca pegam o mesmo número nem deixam a versão em uso apontando para a mais velha das duas.
- **Voltar** (`POST /api/v1/assets/{id}/versions/{n}/activate`): torna uma versão existente a versão em uso, sem reescrever nada, e dá para avançar de novo. O criador pode baixar qualquer versão (`GET /assets/{id}/download?version=n`); os outros recebem sempre a versão em uso.
- **Histórico** (`GET /api/v1/assets/{id}/versions`): versões, datas, tamanhos e notas, da mais nova para a mais velha. Vê o criador e quem tem um item vivo do asset (item na lixeira não vê).
- **Aviso e marca de atualização.** Quem tem o item (e não é o criador) recebe ao vivo "“Estátua” ganhou a versão 3: corrige a porta" (até 500 pessoas por publicação; as que estão offline veem a marca). No inventário o item ganha "●" no nome e a seção **Com atualização** os junta. A marca aparece quando saiu uma versão depois que a pessoa recebeu o item ou depois da última vez que ela olhou; abrir **Versões…** conta como olhar. O criador nunca vê marca nos próprios itens.
- **Produtos.** A lista do mercado traz `assetVersion` e `assetUpdatedAt` de cada produto: quem compra vê qual versão está à venda e quando foi atualizada. Quem já comprou recebe a atualização de graça, pelo aviso acima.

### Janela "Versões…"
Menu do item (e do "+"): lista as versões com as notas. O criador tem o campo "O que mudou…", "Publicar nova versão…" e "Usar a versão escolhida"; quem só tem o item lê e pronto.

### Limites
- Não existe "fixar" uma versão para um comprador: todos usam a versão em uso. Se isso for necessário (por exemplo, para não mudar um objeto já montado), é uma fase à parte, porque os objetos do mundo guardam só o asset.
- Só os assets com arquivo (modelos, texturas, sons, animações, roupas, acessórios, objetos) têm versões; construções salvas e roteiros não.
- Os 12 testes da fase usam arquivos mínimos; o aviso ao vivo depende de a pessoa estar com a janela do jogo aberta.

### Testes
`AssetVersioningTests` (publicar, tipo errado, arquivo inválido, igual, limite, voltar e avançar, histórico e quem o vê, marca de atualização, avisos, lista de produtos), `AssetVersionsPostgresTests` (10 publicações e 6 trocas ao mesmo tempo) e um bloco novo em `InventorySmoke`.

## Fase 6 — armazenamento, sincronização entre sessões e fechamento

### Indicador de armazenamento (`GET /api/v1/inventory/storage`)
A barra do inventário agora vem do servidor. Conta cada asset **distinto** de que a pessoa tem um item (vivo ou ainda na lixeira; o que se tem duas vezes conta uma) no tamanho da versão em uso, mais as versões antigas do que ela mesma criou. Item entregue, eliminado ou transformado em objeto deixa de contar. A dica da barra mostra o total por tipo (modelos, texturas, sons…), quanto está na lixeira (que só deixa de contar ao esvaziar) e as versões antigas; acima de 90% o texto fica laranja e avisa que envios novos podem ser recusados.
O limite padrão é de 100 GB e pode ser trocado na configuração (`Inventory:QuotaBytes`). Ele vale **na hora de guardar algo novo**: envio de arquivo, descompactar um .zip (checado antes de guardar qualquer arquivo) e publicar uma versão nova (`storage_full`, 409). Nunca tira nada do que já está lá, então ninguém fica trancado fora das próprias coisas.

### Sincronização entre sessões
Depois de qualquer mudança bem-sucedida no inventário (mover, apagar, restaurar, pasta, envio, descompactar, versão, compra), o servidor avisa as **outras** sessões abertas da mesma pessoa com um evento ao vivo (`inventory_changed`). Cada jogo aberto tem um nome (`X-Client-Session`) que vai em cada pedido e volta no evento, então a sessão que fez a mudança a ignora e as outras leem o inventário de novo, **uma vez**, meio segundo depois da última mudança de uma sequência. O aviso não gera mensagem na tela. Ofertas, respostas e versões novas já tinham os seus avisos.
Limite: as mudanças feitas dentro de uma ilha (colocar ou devolver objetos) chegam ao inventário pelos avisos que já existiam, não por este evento; e uma sessão sem conexão ao vivo só vê o que mudou ao abrir o inventário.

### Mudanças que se cruzam
O teste final em PostgreSQL com 8 conexões e 4 pessoas (mover itens e pastas, apagar e restaurar pastas, lixeira, apagar definitivamente, favoritar, oferecer, aceitar, recusar e cancelar entregas, ler o armazenamento, tudo ao mesmo tempo) achou uma falha: apagar uma pasta ao mesmo tempo em que um dos itens dela mudava de dono (uma entrega aceita) dava um erro interno. Agora a operação de pastas devolve "tente de novo" (`stale`, 409) sem aplicar nada, e todos os pedidos do inventário e das versões transformam uma corrida desse tipo em 409 em vez de erro do servidor.

### Conferência final dos 30 itens do pedido
| # | Item | Onde |
|---|------|------|
| 1–2 | pastas e subpastas | fase 1 |
| 3–5 | busca avançada, filtros por tipo, ordenação | fase 1 |
| 6–7 | favoritos, recentes | fase 1 |
| 8–9 | lixeira, recuperação | fase 1 |
| 10–13 | prévia de modelo, textura, animação, áudio | fase 2 |
| 14–16 | entrega, aceitar ou recusar, histórico | itens e ofertas já existiam; fase 3 trouxe avisos ao vivo, histórico completo e direitos visíveis |
| 17 | permissões de itens | direitos de itens; letras nos cartões e direitos que chegam (fase 3) |
| 18–19 | pacotes e descompactação | construções salvas (já existiam) e .zip (fase 4) |
| 20 | objetos com conteúdo interno | lista das peças e do que levam (fase 4) |
| 21 | salvamento de construções | já existia; árvore e pivôs (editor, fase 4) |
| 22–23 | objetos devolvidos e perdidos | fase 4 |
| 24 | duplicações inválidas | fase 3 (verificação e correção segura) e testes de carga |
| 25–26 | versionamento, atualizações de produtos | fase 5 |
| 27 | arrastar e soltar | fase 1 (itens e pastas na árvore) |
| 28 | uso de armazenamento | esta fase |
| 29 | carregamento paginado | fase 1 (páginas numeradas de ordem estável) |
| 30 | sincronização entre sessões | esta fase |

A separação entre asset, versão, item de inventário e instância colocada foi mantida: organizar, mover, apagar e restaurar mexem só no item; versões são do asset; o objeto no mundo guarda só o asset.

### O que não foi feito
- Uma "caixa" de itens do inventário para entregar de uma vez (ver a fase 4).
- Fixar uma versão para quem comprou (ver a fase 5).
- Paginação por cursor (é por página numerada).
- Arrastar vários itens ao mesmo tempo na tela (a rota de mover já aceita vários).
- Conferir a aparência das janelas novas numa tela de verdade: foram testadas por comportamento.

### Testes
`InventoryStorageTests` (uso, quota, descompactar e publicar acima do limite, eventos de sincronização), `InventoryFinalPostgresTests` (caos misto de pastas, lixeira, entregas e favoritos; reinício) e blocos novos em `InventorySmoke`.
