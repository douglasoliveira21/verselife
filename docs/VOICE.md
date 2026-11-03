# Voz de proximidade (protocolo v6)

Voz de perto, como numa conversa: quem está a até 25 m ouve você, mais baixo quanto mais longe e vindo da direção do seu avatar. Não existe servidor de voz separado: o áudio passa pelo servidor da ilha que você já tem.

## Como usar

- **Botão Falar** (canto inferior, ao lado do chat): clique e **segure** para falar; ao soltar, o microfone fecha.
- **Cadeado** ao lado do botão: clique para deixar o microfone **sempre aberto** sem precisar segurar (só envia quando você fala); clique de novo para abrir o cadeado e voltar ao modo de segurar. O item *Voz: microfone aberto (cadeado)* do menu Comunicar faz o mesmo.
- Menu **Comunicar**, *Voz: silenciar tudo*: você não fala nem ouve (o botão e o cadeado ficam apagados).
- **Bolinha sobre o nome:** aparece em você quando a voz está ligada e em cada jogador depois que você ouve a voz dele. Quando alguém fala, ondas se espalham em volta da bolinha: **verdes** quando o som está limpo e **vermelhas e maiores** quando o áudio estoura (volume no máximo). O mesmo vale para a sua própria bolinha, e uma barra fina embaixo do botão Falar mostra o seu volume, também ficando vermelha quando estoura.
- **Use fones de ouvido.** O Godot não tem cancelamento de eco; com caixa de som e microfone abertos você vai ouvir eco.
- O microfone só é aberto na primeira vez que você fala. O sistema operacional pode pedir permissão nessa hora.

## Como funciona

1. O Godot captura o microfone (`AudioEffectCapture`) e o cliente reduz para 16 kHz mono (`VoiceFramer`).
2. Cada 20 ms vira um pacote Opus de 20 kbps (`VoiceEncoder`, pacote NuGet `Concentus`, C# puro, sem código nativo). Isso dá cerca de 50 pacotes e uns 3 KB por segundo por pessoa falando.
3. O pacote vai ao servidor da ilha por `SubmitVoice` (não confiável, canal 1). O servidor confere o tamanho (até 400 bytes) e a taxa (até 60 por segundo por pessoa), descobre quem está dentro de 25 m e repassa por `ReceiveVoice` (não confiável, canal 2). O servidor **não decodifica** o áudio.
4. Cada ouvinte tem um decodificador por falante, descarta pacotes atrasados ou repetidos, esconde pequenas perdas (até 3 pacotes) e toca por um `AudioStreamPlayer3D` preso ao avatar, com um buffer curto de 60 ms.

Com o microfone aberto, silêncio não é enviado (corte por volume com 200 ms de sobra no fim de cada fala).

## Verificado e não verificado

- **Verificado:** a barra (segurar, soltar, cadeado, voz desligada), o medidor e a bolinha com ondas verdes e vermelhas (`VoiceUiSmoke`, com uma foto dos estados numa janela real), codec ida e volta (o tom volta com a mesma altura, pacotes de poucas dezenas de bytes), conversão de taxa, corte de silêncio, regras de alcance e limite de taxa (31 testes), comportamento do cliente com áudio sintético (`VoiceSmoke`) e **dois clientes reais** em uma ilha real: um envia 3 s de tom e o outro recebe e decodifica (`VoiceE2E`).
- **Não verificado:** microfone de verdade, alto-falante, qualidade e latência percebidas, e o comportamento em Windows/Linux de verdade fora desta máquina. O teste automático usa o driver de áudio "mudo" do Godot. Teste com dois jogadores reais antes de divulgar.

## Limites conhecidos

- Só voz de proximidade na ilha. Não há sala de voz privada, chamada de grupo ou voz entre ilhas.
- Bloqueio de jogadores ainda não silencia a voz (a lista de bloqueios está na API, não na ilha). O botão de silenciar tudo existe.
- O servidor repassa o áudio: para dezenas de pessoas por ilha é tranquilo; centenas falando ao mesmo tempo aumentariam a banda e aí vale considerar um serviço como o LiveKit.
- Sem cancelamento de eco nem supressão de ruído além do corte por volume.
- `IslandEntry:ProtocolVersion` passou para **6**: publique API e servidor de ilhas juntos.
