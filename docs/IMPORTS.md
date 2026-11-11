# Importação de arquivos

O que o jogo aceita no inventário (botão *Importar* ou um `.zip` com vários arquivos), o que o servidor confere e como isso foi testado.

| Tipo | Formatos aceitos | O que o servidor confere |
|---|---|---|
| Imagens / texturas | PNG, JPG/JPEG, WebP (com perda, sem perda e estendido) | cabeçalho de verdade (não só a extensão), tamanho real, o arquivo não está cortado (PNG sem fim, JPEG sem fim, WebP menor que o declarado), até 8192×8192 e 20 MB |
| Sons | WAV (PCM 8/16/24/32 bits e ponto flutuante de 32 bits, mono ou estéreo, 4000–192000 Hz, inclusive o formato "extensível"), OGG **Vorbis**, MP3 | WAV com formato e dados; OGG precisa ser Vorbis (Opus é recusado com a instrução de exportar de novo); MP3 com quadro Layer III; até 25 MB (o que o cliente baixa) |
| Modelos 3D | GLB e glTF (o glTF com arquivos externos é reunido num GLB pelo cliente) | contagens (triângulos, materiais, texturas, partes), e agora a **estrutura**: parte binária presente, nenhum trecho fora dos dados, esqueleto com no máximo 512 ossos e que aponta para partes que existem, animações que apontam para dados e partes que existem |
| Animações | GLB só com movimento (ou com o personagem junto) | arquivo sem malha mas com animação vira **Animação** automaticamente, qualquer que seja a aba usada para enviar |

Não são aceitos: GIF, BMP, TGA, SVG, DDS, EXR, FBX, OBJ, DAE, BLEND, FLAC, M4A, AAC, WMA, AIFF, Opus, WAV compactado (ADPCM) e WAV com mais de 2 canais. A mensagem de recusa diz o que exportar.

## Metadados de modelo
Cada modelo guarda `rigged` (tem esqueleto ligado à malha), `joints`, `animations` e `animationOnly`, para o inventário e as abas mostrarem "com rig" e "com animação".

## No cliente
- Imagens: PNG, JPG e WebP carregam pelo conteúdo, não pelo nome; acima de 2048 px a imagem é reduzida; arquivo cortado ou lixo é recusado.
- Sons de objetos: OGG Vorbis, WAV de 8/16/24/32 bits e float (convertidos para 16 bits na hora de tocar) e MP3.
- Modelos: o esqueleto, a malha ligada a ele (skin) e as animações atravessam a importação; a aba Recursos oferece os clipes e o objeto toca o clipe com o relógio do servidor.

## Testes
- API: `AssetImportMatrixTests` (cada tipo, cada recusa, esqueleto, animação, arquivo cortado, tamanhos), `ExportedModelTests` (modelos exportados pelo próprio Godot em `tests/fixtures/` e o humanoide do jogo) e `AssetSizeLimitTests`.
- Cliente: `ImportFormatsSmoke` (imagens em vários tamanhos e formatos, WAV de todos os tipos com taxa, canais e volume conferidos, e modelos estático / com rig / com rig e animação / só animação exportados e relidos como o mundo lê, com os ossos virando de verdade quando a animação toca). Para arquivos reais da sua máquina: `LIFEVERSE_IMPORT_SAMPLES="caminho1|caminho2"` (a API e o smoke leem a mesma lista).
- Os arquivos de `tests/fixtures/` são regerados com `LIFEVERSE_WRITE_FIXTURES=1` ao rodar o `ImportFormatsSmoke`.

## Conferido com arquivos reais
WAV do Windows (5,6 s e 3,8 s), OGG Vorbis (1,6 s), MP3 (0,6 s e 0,3 s), WebP de 1536×1024, um GLB com esqueleto (`pinguim_rigado.glb`) e dois glTF com arquivos externos e animação (reunidos em GLB de 4 e 7 MB, com 1 animação e 1 esqueleto cada): todos passam no servidor e carregam no cliente.

## Limites conhecidos
- O servidor confere a estrutura mas não decodifica: um PNG/JPEG com o miolo corrompido, mas início e fim certos, passa e falha só no cliente.
- OGG e MP3 não foram testados com arquivos gerados nos testes (não há codificador): os testes automáticos usam o formato do cabeçalho; os arquivos reais foram conferidos à mão.
- Animação de avatar usa nomes de ossos padrão (importador próprio, com suas próprias mensagens); animação em objetos usa os clipes do próprio modelo.
