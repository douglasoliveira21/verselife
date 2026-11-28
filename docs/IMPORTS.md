# Importação de arquivos

> **Conferido no cliente Unity em 28/11/2026.** Modelos entram por **Construir (B) → Criar → Importar um modelo 3D**; a animação de um modelo colocado é controlada em **Construir → Recursos → Animação do modelo** (ou por script, `animation.play`).

O que o jogo aceita no inventário (botão *Importar* ou um `.zip` com vários arquivos), o que o servidor confere e como isso foi testado.

| Tipo | Formatos aceitos | O que o servidor confere |
|---|---|---|
| Imagens / texturas | PNG, JPG/JPEG, WebP (com perda, sem perda e estendido) | cabeçalho de verdade (não só a extensão), tamanho real, o arquivo não está cortado (PNG sem fim, JPEG sem fim, WebP menor que o declarado), até 8192×8192 e 20 MB |
| Sons | WAV (PCM 8/16/24/32 bits e ponto flutuante de 32 bits, mono ou estéreo, 4000–192000 Hz, inclusive o formato "extensível"), OGG **Vorbis**, MP3 | WAV com formato e dados; OGG precisa ser Vorbis (Opus é recusado com a instrução de exportar de novo); MP3 com quadro Layer III; até 25 MB (o que o cliente baixa) |
| Modelos 3D | GLB e glTF (o glTF com arquivos externos é reunido num GLB pelo cliente) | contagens (triângulos, materiais, texturas, partes), e agora a **estrutura**: parte binária presente, nenhum trecho fora dos dados, esqueleto com no máximo 512 ossos e que aponta para partes que existem, animações que apontam para dados e partes que existem |
| Animações | GLB só com movimento (ou com o personagem junto) | arquivo sem malha mas com animação vira **Animação** automaticamente, qualquer que seja a aba usada para enviar |
| Modelos COLLADA | DAE (`.dae`) — convertido para GLB **no cliente** antes do envio | o servidor continua recebendo só GLB e confere o resultado como qualquer outro modelo |

Não são aceitos: GIF, BMP, TGA, SVG, DDS, EXR, FBX, OBJ, BLEND, FLAC, M4A, AAC, WMA, AIFF, Opus, WAV compactado (ADPCM) e WAV com mais de 2 canais (BMP e TGA só são aceitos como textura de um `.dae`, que o cliente converte para PNG). A mensagem de recusa diz o que exportar.

## COLLADA (.dae)
O jogo guarda e confere só GLB, e o servidor nunca lê caminhos de arquivo. Por isso o `.dae` é convertido no cliente (`ColladaConverter`, chamado por `DaeUploadPackager`) para um GLB único, da mesma forma que o glTF com arquivos externos é reunido (`GltfUploadPackager`). Entra no mesmo fluxo de *Importar* do inventário e da janela de modelos 3D.

O que atravessa a conversão:
- **Malhas**: `triangles`, `polylist` e `polygons` (polígonos viram triângulos), normais (calculadas pelas faces quando faltam) e coordenadas de textura (o V é invertido, porque o COLLADA conta de baixo e o glTF de cima).
- **Materiais e texturas**: cor difusa, textura difusa, emissão, brilho (vira rugosidade) e transparência. As texturas são procuradas **só na pasta do `.dae`**: primeiro no caminho que o arquivo diz e, não estando lá, pelo nome na pasta e nas subpastas até 3 níveis (a mais próxima vence), mesmo que o caminho gravado seja de outro computador. PNG, JPG e WebP seguem como estão; TGA e BMP viram PNG. Textura que não for achada deixa o material só com a cor e aparece como aviso.
- **Cena**: hierarquia de nós com `matrix`, `translate`, `rotate` e `scale`, `instance_node`, eixo vertical do arquivo (`Y_UP`, `Z_UP`, `X_UP`) e a unidade (`<unit meter>`): um modelo em centímetros chega em metros.
- **Esqueleto**: pele (`skin`) com até 4 ossos por vértice, matrizes de bind e a matriz de forma (`bind_shape_matrix`) incorporada às matrizes inversas.
- **Animações**: de matriz inteira ou de componentes (`translate.X`, `rotateZ.ANGLE`…), amostradas em todas as chaves e gravadas como translação, rotação e escala; a interpolação é linear.

Fora do que é importado (a malha entra mesmo assim, com um aviso): morph targets, `trifans`/`tristrips`/linhas, curvas, câmeras e luzes, e texturas em formatos que o cliente não decodifica (DDS, por exemplo). O arquivo é lido sem DTD nem entidades externas.

Testes do cliente: `ModelImportTests` (onde as texturas são procuradas e o que nunca é lido; TGA e BMP virando PNG; um `.dae` com eixo Z, centímetros, textura, esqueleto e animação) e o autoteste do mundo (importar pela janela, recusas, colocar na ilha e animar pela aba Recursos).

## Metadados de modelo
Cada modelo guarda `rigged` (tem esqueleto ligado à malha), `joints`, `animations` e `animationOnly`, para o inventário e as abas mostrarem "com rig" e "com animação".

## No cliente
- Imagens: PNG, JPG e WebP carregam pelo conteúdo, não pelo nome; acima de 2048 px a imagem é reduzida; arquivo cortado ou lixo é recusado.
- Sons de objetos: OGG Vorbis, WAV de 8/16/24/32 bits e float (convertidos para 16 bits na hora de tocar) e MP3.
- Modelos (GLB, glTF e COLLADA): o esqueleto, a malha ligada a ele (skin) e as animações atravessam a importação; a aba Recursos oferece os clipes (tocar, pausar, repetir e velocidade) e o objeto toca o clipe com o relógio do servidor.

## Testes
- API: `AssetImportMatrixTests` (cada tipo, cada recusa, esqueleto, animação, arquivo cortado, tamanhos), `ExportedModelTests` (modelos exportados pelo próprio Godot em `tests/fixtures/` e o humanoide do jogo) e `AssetSizeLimitTests`.
- Cliente: `ModelImportTests` e o autoteste do mundo (ver acima). Para arquivos reais da sua máquina, a API lê `LIFEVERSE_IMPORT_SAMPLES="caminho1|caminho2"`.
- Os arquivos de `tests/fixtures/` foram gerados pelo cliente Godot, que saiu do repositório; não há mais gerador.

## Conferido com arquivos reais
WAV do Windows (5,6 s e 3,8 s), OGG Vorbis (1,6 s), MP3 (0,6 s e 0,3 s), WebP de 1536×1024, um GLB com esqueleto (`pinguim_rigado.glb`) e dois glTF com arquivos externos e animação (reunidos em GLB de 4 e 7 MB, com 1 animação e 1 esqueleto cada): todos passam no servidor e carregam no cliente.

## Limites conhecidos
- O servidor confere a estrutura mas não decodifica: um PNG/JPEG com o miolo corrompido, mas início e fim certos, passa e falha só no cliente.
- OGG e MP3 não foram testados com arquivos gerados nos testes (não há codificador): os testes automáticos usam o formato do cabeçalho; os arquivos reais foram conferidos à mão.
- Animação de avatar usa nomes de ossos padrão (importador próprio, com suas próprias mensagens); animação em objetos usa os clipes do próprio modelo.
