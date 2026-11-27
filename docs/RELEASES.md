# Lançamentos, instalador e atualização

## Como publicar uma versão do cliente

1. Suba a versão no projeto Unity (Player settings, `bundleVersion`) e, em `backend/appsettings.json`, `ClientRelease:Latest` (um teste confere que `Latest` e `Minimum` batem com a do projeto).
2. Gere o pacote: `powershell -ExecutionPolicy Bypass -File scripts\build-unity-client.ps1 -Installer`. Em `artifacts\unity-<versão>\` ficam o zip, o instalador (Inno Setup, `installer\LifeVerse.iss`), `SHA256SUMS.txt` e `release.json`.
3. Publique os arquivos (por exemplo, numa release do GitHub).
4. Avise os jogadores configurando a API (variáveis de ambiente do serviço da API):

```
ClientRelease__Latest=0.7.0
ClientRelease__Minimum=0.7.0
ClientRelease__DownloadUrlWindows=https://github.com/<dono>/<repo>/releases/download/v0.7.0/LifeVerse-Setup-0.7.0-unity.exe
ClientRelease__DownloadUrl=https://github.com/<dono>/<repo>/releases/latest
ClientRelease__Notes=O que mudou, em uma frase
```

## O que o jogador vê

Ao abrir, o cliente pergunta à API (`GET /api/v1/client/release?platform=windows`, sem login):

- **Versão igual ou mais nova que `Latest`:** nada.
- **Mais velha que `Latest`:** aviso de versão nova, com o link de download (só links http/https são abertos).
- **Mais velha que `Minimum`:** atualização obrigatória; entrar e se cadastrar ficam bloqueados.
- API fora do ar, resposta quebrada ou versão do cliente que não é número (build de desenvolvimento): nada acontece e nunca bloqueia.

Use `Minimum` quando mudar o protocolo da rede (cliente, servidor de ilha e API precisam combinar; um cliente antigo seria recusado de qualquer forma).

A atualização é manual (o jogador baixa e instala). Não existe atualização silenciosa dentro do jogo.

## Estado da verificação

O que falta provar (instalar de fato, assinatura do executável, CI) está em `docs/unity-migration/FALTA_FAZER.md`, seção 6.
