# Perfil do personagem

Janela **Perfil** (menu *Conteúdo → Meu perfil…*, ou no clique direito: **Meu perfil** em você e **Perfil** em outro avatar). O servidor decide o que cada pessoa vê; o cliente só desenha o que recebe.

## O que tem
- **Foto de perfil**: uma imagem PNG, JPG ou WebP que a pessoa possui (escolhe das suas texturas ou importa uma). Outros veem a foto por `GET /users/{id}/profile/photo` (o arquivo em si continua privado ao dono); perfil privado esconde a foto.
- **Sobre mim**, **Vida real** (até 2000 letras cada) e **Interesses** (até 500).
- **Aniversário**: é o dia em que o personagem foi criado (dia/mês, com a idade em anos de LifeVerse). Ninguém digita nem edita, então não pode ser falso; perfil privado esconde de outras pessoas. A data completa aparece como "Personagem criado em".
- **Grupos**: lista os grupos de que a pessoa participa e escolheu mostrar (por grupo e um interruptor geral). Grupos privados só aparecem para quem também é membro.
- **Tipo de conta**: *Conta padrão* ou *Doador* (com "desde mm/aaaa"). Só a administração muda (`POST /api/v1/admin/users/{id}/account-type`, exige administrador e MFA, e fica no registro de auditoria). Editar o perfil nunca altera isso.
- **Casamento dentro do jogo**: pedir (com mensagem de até 300 letras; vale 14 dias; até 5 pedidos abertos), aceitar, recusar, cancelar e divorciar-se (qualquer um dos dois). Só um casamento por vez; ao casar, os outros pedidos das duas pessoas são cancelados. O perfil mostra "Casado(a) com … desde …" ou "Solteiro(a)", e a pessoa pode esconder isso.
- **Perfil privado**: outros veem só o nome, o @usuário e o tipo de conta.
- Data de entrada no jogo ("Membro desde") e presença (online/offline, respeitando a privacidade já existente).

## Servidor
- Campos novos em `identity.users` (foto, vida real, interesses, aniversário, tipo de conta, doador desde), `social.group_members.show_in_profile` e a tabela `social.marriages` (migração `Phase64Profile`).
- Endpoints: `GET /api/v1/users/{id}/profile/full`, `GET /api/v1/users/profile?name=`, `PUT /api/v1/users/me/profile/details`, `PUT /api/v1/users/me/groups/{id}/profile`, `GET/POST /api/v1/marriage…`. Os endpoints antigos `PUT /users/me/profile` e `GET /users/{id}/profile` continuam.
- A privacidade fica em `PrivacyJson` (`profile`, `birthday`, `groups`, `marriage`).

## Testes
`ProfileTests` (API: textos, limites, aniversário, foto só minha e só imagem, perfil privado, grupos, busca por nome, doador só pela administração, todo o ciclo do casamento) e `ProfileSmoke` (janela: editar e salvar, recusas do servidor, foto, perfil de outra pessoa somente leitura, doador, casamento, pedido, perfil privado, aceitar e divorciar).

## Falta
- Quem paga/ativa o tipo *Doador* depende de um fluxo de doação que ainda não existe; por enquanto é a administração que marca.

## Notificações e atalhos
- Pedido de casamento, aceite, recusa e cancelamento chegam como aviso em tempo real (`marriage_proposal`, `marriage_accepted`, `marriage_declined`, `marriage_cancelled`).
- O perfil abre também por **Ver perfil** na lista de amigos (janela Pessoas) e no resultado de uma pessoa na busca.
