namespace LifeVerse.Scripting;

/// <summary>One function of the Lua API as it is documented to players: what it is called, what it takes and returns, what it needs and where its limits are.</summary>
public sealed record ApiFunction(string Category, string Namespace, string Name, string Signature, string Description, string Returns, string Permission, string Limits, string Errors, string Example)
{
    public string FullName => $"{Namespace}.{Name}";
}

/// <summary>One event a script can handle: its function name, when it happens and the fields of the table it receives.</summary>
public sealed record ApiEvent(string Name, string When, string Fields, string Example);

/// <summary>
/// The list of what the Lua API really offers. The reference shown to players, the autocompletion of the editor and the warnings of the validator all come from here, and a test loads every entry into a real script
/// to make sure each one exists: the documentation cannot promise what the code does not do.
/// </summary>
public static class ScriptCatalog
{
    private static ApiFunction F(string category, string ns, string name, string signature, string description, string returns, string permission, string limits, string errors, string example) => new(category, ns, name, signature, description, returns, permission, limits, errors, example);
    private const string Own = "Só o próprio objeto (as regras de construção continuam valendo)";

    public static readonly IReadOnlyList<ApiFunction> Functions =
    [
        // ---- Objetos e transformações ----
        F("Objetos", "object", "getId", "object.getId()", "O identificador do objeto.", "texto (UUID)", "—", "—", "—", "chat.say(object.getId())"),
        F("Objetos", "object", "getName", "object.getName()", "O nome do objeto.", "texto", "—", "—", "—", "chat.say(object.getName())"),
        F("Objetos", "object", "setName", "object.setName(nome)", "Muda o nome do objeto.", "nada", Own, "até 63 caracteres; vale ao fim do evento", "name longo demais", "object.setName('Porta aberta')"),
        F("Objetos", "object", "getOwner", "object.getOwner()", "O identificador do dono do objeto.", "texto (UUID)", "—", "—", "—", "local dono = object.getOwner()"),
        F("Objetos", "object", "getRegion", "object.getRegion()", "O identificador da ilha onde o objeto está.", "texto (UUID)", "—", "—", "—", "local ilha = object.getRegion()"),
        F("Transformações", "object", "getPosition", "object.getPosition()", "A posição do objeto, em metros.", "tabela {x, y, z}", "—", "—", "—", "local p = object.getPosition()"),
        F("Transformações", "object", "setPosition", "object.setPosition(x, y, z)", "Põe o objeto numa posição (também aceita uma tabela {x=, y=, z=}).", "nada", Own, "±5000 m; no máximo 5 mudanças por segundo", "valor que não é número finito", "object.setPosition(10, 1, 20)"),
        F("Transformações", "object", "getRotation", "object.getRotation()", "A rotação do objeto, em graus.", "tabela {x, y, z}", "—", "—", "—", "local r = object.getRotation()"),
        F("Transformações", "object", "setRotation", "object.setRotation(x, y, z)", "Gira o objeto para uma rotação, em graus.", "nada", Own, "no máximo 5 mudanças por segundo", "valor que não é número", "object.setRotation(0, 90, 0)"),
        F("Transformações", "object", "getScale", "object.getScale()", "O tamanho do objeto, em metros.", "tabela {x, y, z}", "—", "—", "—", "local s = object.getScale()"),
        F("Transformações", "object", "setScale", "object.setScale(x, y, z)", "Muda o tamanho do objeto.", "nada", Own, "de 0,01 a 64 m por eixo", "valor que não é número", "object.setScale(2, 2, 2)"),
        F("Transformações", "object", "moveTo", "object.moveTo(x, y, z, velocidade)", "Leva o objeto até uma posição, aos poucos (4 passos por segundo).", "nada", Own, "velocidade de 0,1 a 50 m/s; uma viagem por vez", "valor que não é número", "object.moveTo(10, 3, 20, 2)"),
        F("Transformações", "object", "rotateTo", "object.rotateTo(x, y, z, velocidade)", "Gira o objeto até uma rotação, pelo caminho mais curto.", "nada", Own, "velocidade de 1 a 720 graus/s", "valor que não é número", "object.rotateTo(0, 180, 0, 90)"),
        F("Transformações", "object", "move", "object.move({x=, y=, z=})", "Move o objeto em relação à posição atual.", "nada", Own, "±5000 m", "—", "object.move({x=1, y=0, z=0})"),
        F("Transformações", "object", "rotate", "object.rotate({x=, y=, z=})", "Gira o objeto em relação à rotação atual.", "nada", Own, "—", "—", "object.rotate({x=0, y=15, z=0})"),
        // ---- Aparência ----
        F("Visibilidade", "object", "setVisible", "object.setVisible(verdadeiro)", "Mostra ou esconde o objeto (transparência total).", "nada", Own, "—", "—", "object.setVisible(false)"),
        F("Visibilidade", "object", "setAlpha", "object.setAlpha(0 a 1)", "Opacidade do objeto (1 = opaco).", "nada", Own, "—", "—", "object.setAlpha(0.5)"),
        F("Visibilidade", "object", "setColor", "object.setColor(r, g, b)  ou  object.setColor('#RRGGBB')", "Cor do objeto (r, g e b vão de 0 a 1).", "nada", Own, "—", "cor inválida", "object.setColor(1, 0, 0)"),
        F("Visibilidade", "object", "setGlow", "object.setGlow(0 a 1)", "Brilho do objeto.", "nada", Own, "—", "—", "object.setGlow(0.5)"),
        F("Visibilidade", "object", "setFullbright", "object.setFullbright(verdadeiro)", "Objeto que ignora as sombras (brilho total).", "nada", Own, "—", "—", "object.setFullbright(true)"),
        F("Visibilidade", "object", "set_material", "object.set_material{color=, emission=, transparency=, roughness=, metallic=}", "Muda o material (aceita só essas chaves).", "nada", Own, "—", "chave desconhecida", "object.set_material{roughness=0.2}"),
        F("Visibilidade", "object", "setText", "object.setText(texto, '#RRGGBB')", "Texto flutuante sobre o objeto (texto vazio apaga).", "nada", "—", "120 caracteres; 5 chamadas a cada 10 s", "cor inválida", "object.setText('Olá', '#12F2E0')"),
        // ---- Linksets ----
        F("Linksets", "object", "getRoot", "object.getRoot()", "O objeto raiz do conjunto de peças ligadas.", "texto (UUID)", "—", "—", "—", "local raiz = object.getRoot()"),
        F("Linksets", "object", "getChildren", "object.getChildren()", "As peças filhas do conjunto.", "lista de textos (UUID)", "—", "até 200", "—", "for i, id in ipairs(object.getChildren()) do end"),
        F("Linksets", "object", "getLinkCount", "object.getLinkCount()", "Quantas peças tem o conjunto (a raiz é a 1).", "número", "—", "—", "—", "local n = object.getLinkCount()"),
        F("Linksets", "object", "getLink", "object.getLink(indice)", "Os dados de uma peça do conjunto (1 = raiz).", "tabela {id, name, position, rotation, scale} ou nada", "—", "—", "—", "local p = object.getLink(2)"),
        F("Linksets", "object", "setLinkPosition", "object.setLinkPosition(indice, x, y, z)", "Move outra peça do conjunto.", "nada", "Só peças do mesmo dono", "±5000 m; 5 mudanças por segundo", "peça de outra pessoa", "object.setLinkPosition(2, 1, 0, 0)"),
        F("Linksets", "object", "setLinkRotation", "object.setLinkRotation(indice, x, y, z)", "Gira outra peça do conjunto.", "nada", "Só peças do mesmo dono", "5 mudanças por segundo", "peça de outra pessoa", "object.setLinkRotation(2, 0, 90, 0)"),
        F("Linksets", "object", "sendLinkMessage", "object.sendLinkMessage(mensagem)", "Manda uma mensagem aos scripts das outras peças do conjunto (evento on_link_message).", "verdadeiro se alguém recebeu", "Só peças do mesmo dono", "256 caracteres; 20 a cada 10 s", "—", "object.sendLinkMessage('abrir')"),
        F("Linksets", "script", "sendLinkMessage", "script.sendLinkMessage(mensagem)", "O mesmo que object.sendLinkMessage.", "verdadeiro se alguém recebeu", "Só peças do mesmo dono", "20 a cada 10 s", "—", "script.sendLinkMessage('abrir')"),
        F("Scripts", "script", "getId", "script.getId()", "O identificador deste script.", "texto (UUID)", "—", "—", "—", "local id = script.getId()"),
        F("Scripts", "script", "getName", "script.getName()", "O nome deste script.", "texto", "—", "—", "—", "local nome = script.getName()"),
        // ---- Chat e mensagens ----
        F("Chat", "chat", "say", "chat.say(mensagem)", "Fala no chat local (a até 30 m).", "nada", "—", "256 caracteres; 5 mensagens a cada 10 s", "mensagem vazia; limite de taxa", "chat.say('Olá!')"),
        F("Chat", "chat", "whisper", "chat.whisper(mensagem)", "Sussurra: só quem está a até 10 m ouve.", "nada", "—", "256 caracteres; mesmo limite do say", "mensagem vazia", "chat.whisper('psiu')"),
        F("Chat", "chat", "shout", "chat.shout(mensagem)", "Grita: quem está a até 100 m ouve.", "nada", "—", "256 caracteres; mesmo limite do say", "mensagem vazia", "chat.shout('Alarme!')"),
        F("Chat", "chat", "sendChannel", "chat.sendChannel(canal, mensagem)", "Manda uma mensagem a scripts que escutam o canal. Canais positivos são públicos (até 100 m); negativos só chegam a scripts do mesmo dono.", "quantos scripts receberam", "—", "10 a cada 10 s; o canal 0 é o chat aberto e não pode ser usado", "canal 0", "chat.sendChannel(100, 'abrir')"),
        F("Chat", "chat", "listen", "chat.listen(canal)", "Passa a escutar um canal (evento on_chat com o campo channel).", "nada", "—", "até 8 canais por script", "canal 0; canais demais", "chat.listen(100)"),
        F("Chat", "chat", "stopListen", "chat.stopListen(canal)", "Para de escutar um canal.", "nada", "—", "—", "—", "chat.stopListen(100)"),
        F("Chat", "chat", "sendPrivateMessage", "chat.sendPrivateMessage(avatarId, mensagem)", "Mensagem privada a um avatar que está na ilha.", "verdadeiro se foi entregue", "SEND_MESSAGES do avatar, ou ele ter tocado no objeto nos últimos 5 minutos", "256 caracteres; 5 a cada 10 s", "—", "chat.sendPrivateMessage(event.avatar_id, 'Obrigado!')"),
        F("Chat", "object", "sendMessage", "object.sendMessage(mensagem, idDoObjeto)", "Manda uma mensagem a outro objeto do mesmo dono (evento on_message).", "verdadeiro se entregue", "Só objetos do mesmo dono", "256 caracteres", "id inválido", "object.sendMessage('abrir', outroId)"),
        F("Chat", "object", "broadcastMessage", "object.broadcastMessage(mensagem)", "Manda uma mensagem a todos os outros objetos com script do mesmo dono.", "quantos objetos receberam", "Só objetos do mesmo dono", "até 50 objetos; 3 vezes a cada 10 s", "limite de taxa", "object.broadcastMessage('alarme')"),
        // ---- Temporizadores ----
        F("Temporizadores", "timer", "after", "timer.after(segundos, funcao)  ou  timer.after(segundos, 'nome')", "Chama a função uma vez depois do tempo (com um nome, dispara on_delay_complete).", "número (id do temporizador)", "—", "0,25 s a 24 h; até 16 por script", "tempo inválido; temporizadores demais", "timer.after(5, function() chat.say('pronto') end)"),
        F("Temporizadores", "timer", "every", "timer.every(segundos, funcao)  ou  timer.every(segundos, 'nome')", "Chama a função a cada intervalo (com um nome, dispara on_interval).", "número (id do temporizador)", "—", "0,25 s a 24 h; até 16 por script", "tempo inválido; temporizadores demais", "timer.every(10, function() object.rotate({x=0, y=15, z=0}) end)"),
        F("Temporizadores", "timer", "cancel", "timer.cancel(id)", "Cancela um temporizador.", "verdadeiro se existia", "—", "—", "—", "timer.cancel(meu)"),
        F("Temporizadores", "timer", "pause", "timer.pause(id)", "Pausa um temporizador (o tempo que falta fica guardado).", "verdadeiro se pausou", "—", "—", "—", "timer.pause(meu)"),
        F("Temporizadores", "timer", "resume", "timer.resume(id)", "Continua um temporizador pausado.", "verdadeiro se continuou", "—", "—", "—", "timer.resume(meu)"),
        F("Temporizadores", "timer", "getRemaining", "timer.getRemaining(id)", "Quanto falta para o temporizador disparar, em segundos.", "número ou nada", "—", "—", "—", "local falta = timer.getRemaining(meu)"),
        F("Temporizadores", "object", "create_timer", "object.create_timer(segundos, repetir)", "Temporizador que dispara o evento on_timer.", "número (id)", "—", "0,25 s a 24 h; até 16 por script", "tempo inválido", "object.create_timer(5, true)"),
        F("Temporizadores", "object", "cancel_timers", "object.cancel_timers()", "Cancela todos os temporizadores.", "nada", "—", "—", "—", "object.cancel_timers()"),
        // ---- Persistência ----
        F("Persistência", "storage", "set", "storage.set(chave, valor)", "Guarda um valor que sobrevive a reinícios (número, texto, booleano ou tabela pequena). Valor nil apaga.", "nada", "—", "chave de 1 a 64 caracteres; 32 chaves; texto até 1024; tabela até 64 itens, 3 níveis e 2048 caracteres", "valor não guardável", "storage.set('aberta', true)"),
        F("Persistência", "storage", "get", "storage.get(chave)", "Lê um valor guardado.", "o valor ou nada", "—", "—", "—", "local aberta = storage.get('aberta')"),
        F("Persistência", "storage", "delete", "storage.delete(chave)", "Apaga um valor guardado.", "nada", "—", "—", "—", "storage.delete('aberta')"),
        F("Persistência", "storage", "keys", "storage.keys()", "As chaves guardadas, em ordem.", "lista de textos", "—", "—", "—", "for _, k in ipairs(storage.keys()) do end"),
        // ---- Depuração ----
        F("Depuração", "debug", "log", "debug.log(...)", "Escreve no console do script.", "nada", "—", "50 linhas por segundo", "—", "debug.log('porta aberta')"),
        F("Depuração", "debug", "warn", "debug.warn(...)", "Escreve um aviso no console.", "nada", "—", "50 linhas por segundo", "—", "debug.warn('velocidade alta')"),
        F("Depuração", "debug", "error", "debug.error(...)", "Escreve um erro no console (não para o script).", "nada", "—", "50 linhas por segundo", "—", "debug.error('falha de configuração')"),
        F("Depuração", "global", "print", "print(...)", "O mesmo que debug.log.", "nada", "—", "50 linhas por segundo", "—", "print('olá', 3)"),
        F("Depuração", "global", "log", "log(texto)", "Registro do servidor.", "nada", "—", "200 caracteres", "—", "log('iniciado')"),
        // ---- Regiões ----
        F("Regiões", "region", "getName", "region.getName()", "O nome da ilha.", "texto", "—", "—", "—", "chat.say(region.getName())"),
        F("Regiões", "region", "getId", "region.getId()", "O identificador da ilha.", "texto (UUID)", "—", "—", "—", "local id = region.getId()"),
        F("Regiões", "region", "getAvatarCount", "region.getAvatarCount()", "Quantas pessoas estão na ilha.", "número", "—", "—", "—", "chat.say(region.getAvatarCount())"),
        // ---- Permissões e diálogos ----
        F("Permissões", "permissions", "request", "permissions.request(avatarId, 'PERMISSAO')", "Pede uma permissão a um avatar (aparece um diálogo para ele). A resposta chega em on_permission_granted ou on_permission_denied.", "verdadeiro se o pedido foi mostrado", "—", "o mesmo pedido só se repete depois de 30 s; permissões válidas: " + string.Join(", ", ScriptPermission.OfAvatars), "permissão desconhecida", "permissions.request(event.avatar_id, 'CONTROL_ANIMATION')"),
        F("Permissões", "permissions", "has", "permissions.has(avatarId, 'PERMISSAO')", "Se o avatar já deu a permissão.", "verdadeiro ou falso", "—", "—", "—", "if permissions.has(id, 'SEND_MESSAGES') then end"),
        F("Permissões", "permissions", "revoke", "permissions.revoke(avatarId, 'PERMISSAO')", "O script devolve uma permissão que tinha.", "verdadeiro se tinha", "—", "—", "—", "permissions.revoke(id, 'SEND_MESSAGES')"),
        F("Diálogos", "dialog", "show", "dialog.show(avatarId, {title=, message=, buttons={...}, input=, timeout=})", "Mostra uma janela com botões (e uma caixa de texto, se pedir) a um avatar próximo. A resposta chega em on_dialog_response.", "texto (id do diálogo)", "O avatar precisa estar a até 40 m", "até 12 botões; 3 diálogos abertos por script; 10 a cada 10 s; tempo de 5 a 300 s", "limite de diálogos", "dialog.show(event.avatar_id, {title='Porta', message='Abrir?', buttons={'Sim', 'Não'}})"),
        F("Diálogos", "dialog", "cancel", "dialog.cancel(idDoDialogo)", "Cancela um diálogo que ainda não foi respondido.", "verdadeiro se existia", "—", "—", "—", "dialog.cancel(id)"),
        // ---- Áudio ----
        F("Áudio", "audio", "play", "audio.play(nome, volume, repetir)", "Toca um som que está no Conteúdo do objeto, para quem está perto.", "verdadeiro se o objeto tem esse som", "—", "5 chamadas a cada 10 s", "—", "audio.play('Campainha', 0.8)"),
        F("Áudio", "audio", "stop", "audio.stop()", "Para o som do objeto.", "nada", "—", "—", "—", "audio.stop()"),
        F("Áudio", "object", "play_sound", "object.play_sound(nome, volume, repetir)", "O mesmo que audio.play.", "verdadeiro se tem o som", "—", "5 a cada 10 s", "—", "object.play_sound('Campainha')"),
        F("Áudio", "object", "stop_sound", "object.stop_sound()", "O mesmo que audio.stop.", "nada", "—", "—", "—", "object.stop_sound()"),
        // ---- Física ----
        F("Física", "physics", "setEnabled", "physics.setEnabled(verdadeiro)", "Liga ou desliga a colisão sólida do objeto.", "nada", "CONTROL_PHYSICS, dada pelo dono no editor", "vale ao fim do evento; as regras de física das construções continuam valendo", "falta de permissão", "physics.setEnabled(false)"),
        F("Física", "physics", "setCollisionEnabled", "physics.setCollisionEnabled(verdadeiro)", "O mesmo que physics.setEnabled.", "nada", "CONTROL_PHYSICS, dada pelo dono no editor", "—", "falta de permissão", "physics.setCollisionEnabled(true)"),
        F("Física", "physics", "setPhantom", "physics.setPhantom(verdadeiro)", "Torna o objeto um fantasma (sem colisão) ou sólido de novo.", "nada", "CONTROL_PHYSICS, dada pelo dono no editor", "—", "falta de permissão", "physics.setPhantom(true)"),
        F("Física", "physics", "setDynamic", "physics.setDynamic(verdadeiro)", "Faz o objeto obedecer à física (cair, rolar, ser empurrado) ou o deixa parado.", "nada", "CONTROL_PHYSICS, dada pelo dono no editor", "precisa de colisão sólida e não pode ser gatilho nem malha triangular", "falta de permissão", "physics.setDynamic(true)"),
        F("Física", "physics", "setMass", "physics.setMass(massa)", "Massa do objeto, em kg.", "nada", "CONTROL_PHYSICS, dada pelo dono no editor", "de 0,01 a 10000", "massa fora dos limites", "physics.setMass(10)"),
        F("Física", "physics", "setGravityScale", "physics.setGravityScale(fator)", "Multiplica a gravidade do objeto (0 = flutua).", "verdadeiro se o objeto é simulado", "CONTROL_PHYSICS, dada pelo dono no editor", "de -5 a 5; vale enquanto a ilha roda", "falta de permissão", "physics.setGravityScale(0.2)"),
        F("Física", "physics", "applyImpulse", "physics.applyImpulse(x, y, z)", "Dá um empurrão instantâneo ao objeto.", "verdadeiro se o objeto é simulado", "CONTROL_PHYSICS, dada pelo dono no editor", "até 10 chamadas por segundo; o objeto precisa ser dinâmico", "falta de permissão; limite de taxa", "physics.applyImpulse(0, 5, 0)"),
        F("Física", "physics", "applyForce", "physics.applyForce(x, y, z)", "Aplica uma força por 0,1 segundo.", "verdadeiro se o objeto é simulado", "CONTROL_PHYSICS, dada pelo dono no editor", "até 10 chamadas por segundo", "falta de permissão; limite de taxa", "physics.applyForce(0, 100, 0)"),
        F("Física", "physics", "setVelocity", "physics.setVelocity(x, y, z)", "Define a velocidade do objeto, em m/s.", "verdadeiro se o objeto é simulado", "CONTROL_PHYSICS, dada pelo dono no editor", "até 50 m/s", "falta de permissão; limite de taxa", "physics.setVelocity(0, 10, 0)"),
        F("Física", "physics", "setAngularVelocity", "physics.setAngularVelocity(x, y, z)", "Define a rotação do objeto, em radianos por segundo.", "verdadeiro se o objeto é simulado", "CONTROL_PHYSICS, dada pelo dono no editor", "até 20 rad/s", "falta de permissão; limite de taxa", "physics.setAngularVelocity(0, 3, 0)"),
        F("Física", "physics", "getVelocity", "physics.getVelocity()", "A velocidade atual do objeto.", "tabela {x, y, z} ou nada se o objeto não é simulado", "CONTROL_PHYSICS, dada pelo dono no editor", "—", "falta de permissão", "local v = physics.getVelocity()"),
        // ---- HTTP ----
        F("HTTP", "http", "request", "http.request({url=, method=, headers=, body=})", "Faz uma requisição a um endereço externo, em nome do script (quem faz é o servidor da API, não o script). A resposta chega em on_http_response.", "texto (id da requisição)", "HTTP_REQUESTS, dada pelo dono do objeto no editor (aba Permissões)", "só https, portas 443 e 8443, endereços públicos; GET ou POST; corpo até 8 KB; resposta até 32 KB (json ou texto); 8 s; no máximo 3 ao mesmo tempo e 10 por minuto por script; até 8 cabeçalhos (Accept, Content-Type, Authorization, User-Agent e X-...)", "falta de permissão; endereço privado ou não permitido; limite de taxa", "http.request({url='https://api.exemplo.com/status', method='GET'})"),
        // ---- Economia ----
        F("Economia", "economy", "requestPayment", "economy.requestPayment(avatarId, valor, motivo)", "Pede um pagamento a um avatar. Aparece uma janela dizendo quem pede e quanto, e o avatar tem de apertar Pagar. O dinheiro vai para o DONO do objeto (o script não escolhe o destino e nunca mexe em saldo). O resultado chega em on_payment (dinheiro que de fato mudou de mãos) ou on_payment_failed.", "texto (id do pedido)", "—: cada pagamento exige a confirmação do avatar", "L$ 1 a L$ 100.000; até 5 pedidos esperando por script e 10 por minuto; a mesma pessoa só pode ser cobrada de novo depois de 10 s; o avatar tem 60 s para responder", "valor fora dos limites; limite de taxa", "economy.requestPayment(event.avatar.id, 100, 'Entrada')"),
        // ---- Inventário do objeto ----
        F("Inventário", "inventory", "list", "inventory.list()", "Os itens que o objeto carrega no Conteúdo (sons, texturas, notas...), só leitura.", "lista de tabelas {name, type, id}", "—", "até 200 itens", "—", "for _, item in ipairs(inventory.list()) do end"),
        F("Inventário", "inventory", "getItem", "inventory.getItem('Nome')", "Um item do objeto pelo nome (sem diferenciar maiúsculas).", "tabela {name, type, id} ou nada", "—", "—", "—", "local som = inventory.getItem('Campainha')"),
        F("Inventário", "inventory", "hasItem", "inventory.hasItem('Nome')", "Se o objeto tem um item com esse nome.", "verdadeiro ou falso", "—", "—", "—", "if inventory.hasItem('Chave') then end"),
        F("Inventário", "inventory", "getItemType", "inventory.getItemType('Nome')", "O tipo (categoria) de um item do objeto.", "texto ou nada", "—", "—", "—", "local tipo = inventory.getItemType('Campainha')"),
        F("Inventário", "inventory", "count", "inventory.count()", "Quantos itens o objeto carrega.", "número", "—", "—", "—", "chat.say(inventory.count())"),
        // ---- Animações de avatar ----
        F("Animações", "avatar", "playAnimation", "avatar.playAnimation(avatarId, 'dance', segundos)", "Faz o avatar dançar ou acenar por alguns segundos, para todos que estão perto. Só acontece se o avatar estiver parado: se ele andar, voar ou sentar, a animação termina. Quando acaba, o script ouve on_animation_finished.", "verdadeiro se começou", "CONTROL_ANIMATION, dada pelo avatar (permissions.request)", "animações: wave e dance; de 1 a 15 s; 10 a cada 10 s", "sem permissão do avatar; animação desconhecida; limite de taxa", "avatar.playAnimation(event.avatar.id, 'dance', 5)"),
        F("Animações", "avatar", "stopAnimation", "avatar.stopAnimation(avatarId)", "Interrompe a animação que o script começou nesse avatar.", "nada", "CONTROL_ANIMATION, dada pelo avatar", "—", "sem permissão do avatar", "avatar.stopAnimation(event.avatar.id)"),
        // ---- HUD ----
        F("HUD", "hud", "show", "hud.show(avatarId)", "Mostra o painel (HUD) do script na tela de um avatar. O jogador pode fechá-lo no X (o script ouve on_hud_closed). Os elementos que o script definiu com hud.set... aparecem nele.", "nada", "SHOW_HUD, dada pelo avatar (permissions.request)", "até 12 elementos por HUD; até 20 HUDs por script; atualizações enviadas no máximo 4 vezes por segundo", "sem permissão do avatar", "hud.show(event.avatar.id)"),
        F("HUD", "hud", "hide", "hud.hide(avatarId)", "Esconde o HUD do avatar.", "nada", "SHOW_HUD, dada pelo avatar", "—", "sem permissão do avatar", "hud.hide(event.avatar.id)"),
        F("HUD", "hud", "setTitle", "hud.setTitle(avatarId, texto)", "O título do painel.", "nada", "SHOW_HUD, dada pelo avatar", "até 120 caracteres", "sem permissão do avatar", "hud.setTitle(event.avatar.id, 'Painel da loja')"),
        F("HUD", "hud", "setPosition", "hud.setPosition(avatarId, x, y)", "Onde o painel fica na tela (0 a 1: 0,8 e 0,25 é à direita, no alto).", "nada", "SHOW_HUD, dada pelo avatar", "de 0,05 a 0,95", "sem permissão do avatar", "hud.setPosition(event.avatar.id, 0.8, 0.2)"),
        F("HUD", "hud", "setSize", "hud.setSize(avatarId, largura)", "A largura do painel, em pixels.", "nada", "SHOW_HUD, dada pelo avatar", "de 160 a 420", "sem permissão do avatar", "hud.setSize(event.avatar.id, 280)"),
        F("HUD", "hud", "setLabel", "hud.setLabel(avatarId, id, texto, '#RRGGBB')", "Cria ou muda uma linha de texto do painel. O id tem 1 a 32 letras, números, _ ou -.", "nada", "SHOW_HUD, dada pelo avatar", "até 120 caracteres", "id inválido; elementos demais", "hud.setLabel(event.avatar.id, 'status', 'Vida: 100', '#3BF45F')"),
        F("HUD", "hud", "setButton", "hud.setButton(avatarId, id, texto)", "Cria ou muda um botão. Quando o jogador clica, o script ouve on_hud_click com o campo element.", "nada", "SHOW_HUD, dada pelo avatar", "até 120 caracteres", "id inválido; elementos demais", "hud.setButton(event.avatar.id, 'ativar', 'Ativar')"),
        F("HUD", "hud", "setProgress", "hud.setProgress(avatarId, id, valor, maximo, texto)", "Cria ou muda uma barra de progresso.", "nada", "SHOW_HUD, dada pelo avatar", "máximo a partir de 1", "id inválido; elementos demais", "hud.setProgress(event.avatar.id, 'energia', 70, 100, 'Energia')"),
        F("HUD", "hud", "setInput", "hud.setInput(avatarId, id, dica)", "Cria ou muda um campo de texto. Ao confirmar com Enter, o script ouve on_hud_input com element e text.", "nada", "SHOW_HUD, dada pelo avatar", "texto de até 200 caracteres", "id inválido; elementos demais", "hud.setInput(event.avatar.id, 'nome', 'Seu nome')"),
        F("HUD", "hud", "remove", "hud.remove(avatarId, id)", "Tira um elemento do painel.", "nada", "SHOW_HUD, dada pelo avatar", "—", "id inválido", "hud.remove(event.avatar.id, 'status')"),
        F("HUD", "hud", "clear", "hud.clear(avatarId)", "Tira todos os elementos do painel.", "nada", "SHOW_HUD, dada pelo avatar", "—", "sem permissão do avatar", "hud.clear(event.avatar.id)"),
        // ---- Partículas e luzes ----
        F("Efeitos", "particles", "create", "particles.create({preset=, rate=, lifetime=, size=, color=, speed=})", "Cria um emissor de partículas no objeto, visível para todos na ilha. Presets: smoke, fire, sparks, rain, snow, explosion, trail, teleport, dust (também aceita texture='smoke' como no exemplo do manual). color é '#RRGGBB' ou {r, g, b} de 0 a 1.", "número (id do emissor)", "—", "até 4 emissores por script e 12 por pessoa; 10 criações a cada 10 s; rate 1 a 60 por segundo, lifetime 0,2 a 6 s, size 0,05 a 2 m, speed 0 a 10", "preset desconhecido; emissores demais; número inválido", "particles.create({preset='fire', rate=30, lifetime=1.5, size=0.5, color={1, 0.5, 0}, speed=2})"),
        F("Efeitos", "particles", "stop", "particles.stop(id)", "Para um emissor.", "verdadeiro se existia", "—", "—", "—", "particles.stop(fumaca)"),
        F("Efeitos", "particles", "stopAll", "particles.stopAll()", "Para todos os emissores do script.", "quantos pararam", "—", "—", "—", "particles.stopAll()"),
        F("Efeitos", "light", "set", "light.set({color=, radius=, energy=, flicker=})", "Põe uma luz no objeto (uma por script); chamar de novo a muda.", "nada", "—", "radius 0,5 a 30 m; energy 0 a 8", "número inválido", "light.set({color='#FFC46B', radius=10, energy=2, flicker=true})"),
        F("Efeitos", "light", "off", "light.off()", "Apaga a luz do script.", "nada", "—", "—", "—", "light.off()"),
        F("Áudio", "audio", "playAtPosition", "audio.playAtPosition(nome, x, y, z, volume, repetir)", "Toca um som que está no Conteúdo do objeto num ponto da ilha (som 3D: some com a distância).", "verdadeiro se o objeto tem esse som", "—", "5 chamadas a cada 10 s; ±5000 m", "—", "audio.playAtPosition('Alarme', 10, 1, 20, 0.8)"),
        // ---- Terrenos ----
        F("Terrenos", "parcel", "getAt", "parcel.getAt(x, z)", "Os dados públicos do terreno que cobre um ponto da ilha.", "tabela {id, name, owner, access, build, forSale} ou nada onde não há terreno", "—", "só o que qualquer visitante vê no mapa de terrenos", "—", "local p = parcel.getAt(10, 20)"),
        F("Terrenos", "parcel", "getId", "parcel.getId()", "O identificador do terreno onde o objeto está.", "texto (UUID) ou nada", "—", "—", "—", "local id = parcel.getId()"),
        F("Terrenos", "parcel", "getName", "parcel.getName()", "O nome do terreno onde o objeto está.", "texto ou nada", "—", "—", "—", "chat.say(parcel.getName())"),
        F("Terrenos", "parcel", "getOwner", "parcel.getOwner()", "O dono do terreno onde o objeto está.", "texto (UUID) ou nada", "—", "—", "—", "local dono = parcel.getOwner()"),
        F("Terrenos", "parcel", "getPermissions", "parcel.getPermissions()", "Como o terreno é aberto: entrada, construção e se está à venda.", "tabela {access, build, forSale} ou nada", "—", "access: public, list ou private; build: owner, list ou everyone", "—", "local p = parcel.getPermissions()"),
        F("Terrenos", "object", "getParcel", "object.getParcel()", "O identificador do terreno onde o objeto está.", "texto (UUID) ou nada", "—", "—", "—", "local id = object.getParcel()"),
        // ---- Tempo ----
        F("Tempo", "lv", "time", "lv.time()", "O relógio da ilha, em segundos.", "número", "—", "—", "—", "local agora = lv.time()"),
    ];

    public static readonly IReadOnlyList<ApiEvent> Events =
    [
        new("on_start", "O script foi carregado, editado ou reiniciado.", "—", "function on_start() chat.say('ligado') end"),
        new("on_reset", "O script foi reiniciado (vem antes de on_start).", "—", "function on_reset() storage.delete('visitas') end"),
        new("on_destroy", "O script vai ser removido (ou o objeto foi apagado).", "—", "function on_destroy() chat.say('tchau') end"),
        new("on_enable", "O script foi ligado de novo.", "—", "function on_enable() end"),
        new("on_disable", "O script vai ser desligado.", "—", "function on_disable() end"),
        new("on_owner_changed", "O objeto mudou de dono (as permissões do dono antigo acabam).", "owner_id", "function on_owner_changed(event) end"),
        new("on_region_start", "A ilha começou (ou recomeçou).", "—", "function on_region_start() end"),
        new("on_region_shutdown", "A ilha vai ser desligada (guarde o que precisa com storage).", "—", "function on_region_shutdown() storage.set('t', lv.time()) end"),
        new("on_touch", "Um avatar clica no objeto (a até 12 m).", "avatar_id, avatar_name, avatar.id, avatar.name, user_id, distance, position", "function on_touch(event) chat.say('Oi, ' .. event.avatar.name) end"),
        new("on_touch_start", "O mesmo que on_touch.", "os mesmos de on_touch", "function on_touch_start(event) end"),
        new("on_click", "O mesmo que on_touch.", "os mesmos de on_touch", "function on_click(event) end"),
        new("on_touch_end", "O avatar solta o botão depois de tocar no objeto (só chega para quem tocou, até 60 s depois).", "os mesmos de on_touch, mais duration (segundos com o botão apertado)", "function on_touch_end(event) chat.say('Segurou por ' .. event.duration .. ' s') end"),
        new("on_double_click", "O mesmo avatar toca duas vezes seguidas no objeto (até 0,5 s entre os toques); on_touch também dispara em cada toque.", "os mesmos de on_touch", "function on_double_click(event) chat.say('Duplo clique de ' .. event.avatar.name) end"),
        new("on_enter", "Um avatar entra na área do objeto.", "os mesmos de on_touch", "function on_enter(event) chat.say('Bem-vindo, ' .. event.avatar.name) end"),
        new("on_avatar_enter", "O mesmo que on_enter.", "os mesmos de on_touch", "function on_avatar_enter(event) end"),
        new("on_leave", "Um avatar sai da área do objeto.", "os mesmos de on_touch", "function on_leave(event) end"),
        new("on_avatar_leave", "O mesmo que on_leave.", "os mesmos de on_touch", "function on_avatar_leave(event) end"),
        new("on_chat", "Alguém fala a até 20 m (canal 0) ou algo é enviado a um canal que o script escuta.", "channel, sender, avatar_name, avatar.id, avatar.name, message, is_object", "function on_chat(event) if event.message == 'abrir' then end end"),
        new("on_message", "Outro objeto do mesmo dono enviou uma mensagem.", "message, from", "function on_message(event) end"),
        new("on_object_message", "O mesmo que on_message.", "message, from", "function on_object_message(event) end"),
        new("on_region_restart", "O mesmo que on_region_start.", "—", "function on_region_restart() end"),
        new("on_link_message", "Outra peça do conjunto enviou uma mensagem.", "message, from, from_script", "function on_link_message(event) end"),
        new("on_timer", "Um temporizador de object.create_timer venceu.", "timer_id, count", "function on_timer(event) end"),
        new("on_interval", "Um timer.every com nome disparou.", "name, timer_id, count", "function on_interval(event) end"),
        new("on_delay_complete", "Um timer.after com nome terminou.", "name, timer_id, count", "function on_delay_complete(event) end"),
        new("on_http_response", "A resposta de uma http.request chegou (ou ela falhou).", "request_id, status, body, error, ok", "function on_http_response(event) if event.ok then chat.say(event.body) end end"),
        new("on_payment", "Um pagamento pedido com economy.requestPayment foi concluído: o dinheiro já está na carteira do dono.", "request_id, avatar_id, avatar.id, avatar.name, amount, transaction_id", "function on_payment(event) chat.say('Obrigado, ' .. event.avatar.name) end"),
        new("on_payment_failed", "O pagamento foi recusado pelo avatar ou não pôde ser feito.", "request_id, avatar_id, amount, reason (declined, insufficient_balance, wallet_blocked, own_object, unavailable)", "function on_payment_failed(event) chat.say(event.reason) end"),
        new("on_inventory_changed", "O Conteúdo do objeto mudou (item posto ou tirado).", "added, removed (quantos)", "function on_inventory_changed(event) end"),
        new("on_item_added", "Um item foi posto no Conteúdo do objeto.", "item.name, item.type, item.id", "function on_item_added(event) chat.say(event.item.name) end"),
        new("on_item_removed", "Um item saiu do Conteúdo do objeto.", "item.name, item.type, item.id", "function on_item_removed(event) end"),
        new("on_animation_finished", "Uma animação de avatar começada por avatar.playAnimation terminou.", "avatar_id, avatar.id, avatar.name, name, reason (finished, interrupted, stopped, replaced)", "function on_animation_finished(event) chat.say(event.reason) end"),
        new("on_hud_click", "O jogador clicou num botão do HUD do script.", "avatar_id, avatar.id, avatar.name, element", "function on_hud_click(event) if event.element == 'ativar' then chat.say('ligado') end end"),
        new("on_hud_input", "O jogador confirmou um campo de texto do HUD.", "avatar_id, avatar.id, avatar.name, element, text", "function on_hud_input(event) chat.say(event.text) end"),
        new("on_hud_closed", "O jogador fechou o HUD do script no X.", "avatar_id, avatar.id, avatar.name", "function on_hud_closed(event) end"),
        new("on_collision_enter", "O corpo físico do objeto bateu em algo (outro objeto ou o terreno). Só objetos dinâmicos (physics.setDynamic) e só batidas com velocidade acima de 0,3 m/s; no máximo 10 por segundo.", "entity.type (object ou ground), entity.id, entity.name, speed", "function on_collision_enter(event) chat.say(event.entity.name) end"),
        new("on_collision", "O mesmo que on_collision_enter.", "os mesmos de on_collision_enter", "function on_collision(event) end"),
        new("on_collision_exit", "O corpo físico do objeto deixou de tocar em algo.", "entity.type, entity.id, entity.name, speed", "function on_collision_exit(event) end"),
        new("on_land_collision", "O corpo físico do objeto bateu no terreno.", "entity (type ground), speed", "function on_land_collision(event) chat.say('pousou') end"),
        new("on_parcel_enter", "Um avatar entrou no terreno onde o objeto está.", "avatar_id, avatar.id, avatar.name, parcel.id, parcel.name, parcel.owner, parcel.access, parcel.build, parcel.forSale", "function on_parcel_enter(event) chat.say('Bem-vindo a ' .. event.parcel.name) end"),
        new("on_parcel_leave", "Um avatar saiu do terreno onde o objeto está.", "os mesmos de on_parcel_enter (do terreno que ele deixou)", "function on_parcel_leave(event) end"),
        new("on_dialog_response", "Um avatar respondeu a um diálogo.", "avatar_id, response, input, request_id", "function on_dialog_response(event) chat.say(event.response) end"),
        new("on_permission_granted", "Um avatar concedeu (ou já tinha concedido) uma permissão.", "avatar_id, permission", "function on_permission_granted(event) end"),
        new("on_permission_denied", "Um avatar negou uma permissão.", "avatar_id, permission", "function on_permission_denied(event) end"),
        new("on_script_error", "Um evento do próprio script falhou (o script continua rodando).", "event, message, line, status", "function on_script_error(event) debug.error(event.message) end"),
    ];

    /// <summary>The first version of the object API (snake_case); it keeps working, the documented names are the camelCase ones.</summary>
    public static readonly IReadOnlySet<string> LegacyObject = new HashSet<string>(StringComparer.Ordinal) { "get_id", "get_name", "get_position", "get_rotation", "get_scale", "set_position", "move", "set_rotation", "rotate", "set_scale", "set_color", "set_material", "say", "send_message", "create_timer", "cancel_timers", "state_get", "state_set", "play_sound", "stop_sound", "set_text", "teleport_authorized" };

    public static IReadOnlySet<string> EventNames { get; } = Events.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>The functions of each namespace, for telling a typo from a real call.</summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> ByNamespace { get; } = Functions.GroupBy(f => f.Namespace).ToDictionary(g => g.Key, g => (IReadOnlySet<string>)g.Select(f => f.Name).ToHashSet(StringComparer.Ordinal));
}
