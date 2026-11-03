# Lançamentos, instalador e atualização

## Como publicar uma versão do cliente

1. Suba o número em `client/Autoloads/AppManager.cs` (`Version`) e em `application/file_version` de `client/export_presets.cfg`.
2. Faça o commit e crie a tag com o mesmo número: `git tag v0.7.0 && git push origin v0.7.0`.
3. O workflow `.github/workflows/release.yml` confere que a tag bate com a versão do cliente, exporta Windows e Linux (`client/export_presets.cfg`), monta o instalador do Windows (Inno Setup, `installer/lifeverse.iss`) e cria a release no GitHub com: zip do Windows, tar.gz do Linux e `LifeVerse-Setup-<versão>.exe`.
4. Avise os jogadores configurando a API (variáveis de ambiente do serviço da API):

```
ClientRelease__Latest=0.7.0
ClientRelease__Minimum=0.6.0
ClientRelease__DownloadUrlWindows=https://github.com/<dono>/<repo>/releases/download/v0.7.0/LifeVerse-Setup-0.7.0.exe
ClientRelease__DownloadUrlLinux=https://github.com/<dono>/<repo>/releases/download/v0.7.0/LifeVerse-linux-0.7.0.tar.gz
ClientRelease__DownloadUrl=https://github.com/<dono>/<repo>/releases/latest
ClientRelease__Notes=O que mudou, em uma frase
```

## O que o jogador vê

Ao abrir, o cliente pergunta à API (`GET /api/v1/client/release?platform=windows`, sem login):

- **Versão igual ou mais nova que `Latest`:** nada.
- **Mais velha que `Latest`:** aviso na tela de login e no jogo ("Nova versão disponível") e o item **menu Ajuda, item Baixar atualização** abre o link (só links http/https são abertos).
- **Mais velha que `Minimum`:** na tela de login aparece "Atualização obrigatória", o login e o cadastro ficam bloqueados e há um botão **Baixar atualização**.
- API fora do ar, resposta quebrada ou versão do cliente que não é número (build de desenvolvimento): nada acontece e nunca bloqueia.

Use `Minimum` quando mudar o protocolo da rede (a versão em `NetworkProtocol.Version` precisa bater entre cliente, servidor de ilhas e API; um cliente antigo seria recusado de qualquer forma).

A atualização é manual (o jogador baixa e instala). Não existe atualização silenciosa dentro do jogo.

## Estado da verificação

- Verificado: comparação de versões, leitura da resposta da API, escolha do link por plataforma, bloqueio e aviso na tela de login (`UpdateSmoke`), 45 testes.
- **Não verificado:** a exportação para Windows e Linux e o instalador. Esta máquina só tem os templates de exportação de Android e Web, e o workflow depende de a imagem `barichello/godot-ci:mono-4.7.2` trazer os templates desktop e de o Inno Setup estar no runner do Windows. Rode o workflow uma vez (Actions → Release → Run workflow) e corrija o que aparecer antes de depender dele. Sem ícone próprio nem assinatura de código: o Windows pode mostrar o aviso "editor desconhecido".
