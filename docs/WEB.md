# Snake VS na web

A versão web é o mesmo jogo do app, abrindo direto no menu do jogo (sem landing page). Roda no navegador com
WebAssembly + WebGL. Tem um **ranking online** (só na web): ao fim de cada partida do Clássico ou de Níveis o jogador
pode salvar a pontuação com nome/apelido.

Na VM ficam só duas coisas:

| Peça | O que é | Custo na VM |
|---|---|---|
| Jogo | Arquivos estáticos (`artifacts/web/wwwroot`) servidos pelo nginx | Nenhum processo próprio (o nginx já existe) |
| Ranking | `snakevs-api`: binário Rust estático de ~2,3 MB + um arquivo SQLite | ~2–5 MB de RAM, CPU só quando alguém salva/abre o ranking |

Medido aqui com 16 mil requisições (8 clientes simultâneos): `snakevs-api` foi de 4,7 para 5,3 MB de RAM e gastou 2 s de
CPU no total. O servidor ASP.NET que existia antes, com a mesma carga, foi de 65 para 122 MB e gastou 9 s de CPU.

## Como funciona

| Projeto | Papel na web |
|---|---|
| `SnakeVs.Core` | Regras, IA, níveis e save (os mesmos do Android) + contrato do ranking (`Leaderboard.cs`: DTOs, regra de apelido, checagem de pontuação, JSON). |
| `SnakeVs.Game` | Telas, render, fonte, input e áudio: os mesmos arquivos do app. Ranking: `Online/ILeaderboard.cs`, `Scenes/RankingScene.cs` e o botão no fim da partida (`PlayScene`). Só aparece onde a plataforma oferece ranking. |
| `SnakeVs.Web` | Host do navegador: `WebPlatform.cs`, `HttpLeaderboard.cs`, `wwwroot/index.html` e `wwwroot/js/snakevs.js`. |
| `SnakeVs.Api` | Servidor do ranking em Rust: `src/rules.rs` (regras), `src/lib.rs` (rotas + SQLite), `src/main.rs` (HTTP mínimo), `deploy/` (nginx + systemd). |

- Motor: [KNI](https://github.com/kniEngine/kni), compatível com a API XNA/MonoGame usada pelo jogo e com suporte a
  Blazor WebAssembly/WebGL. Android e desktop continuam no MonoGame. A única diferença de API entre os dois está em
  `SnakeGame.cs` (`#if KNI`, assinatura de `OnDeactivated`/`OnExiting`).
- Save local: `localStorage` do navegador (chave `snakevs.save`, mesmo formato do arquivo do app), incluindo o último
  apelido usado, que vem preenchido na próxima vez.
- Tela: canvas em tela cheia na resolução real do aparelho (até 3x), respeitando notch/cantos (`safe-area-inset`).
- Input: toque, mouse e caneta via Pointer Events, enfileirados entre frames (um toque rápido nunca se perde);
  teclado (setas/WASD, Enter/Espaço, Esc, P) e gamepad.
- Botão/gesto "voltar" do navegador: funciona como no Android (pausa, volta de tela; no menu sai do site).
  Com o diálogo de apelido aberto, "voltar" só fecha o diálogo.
- Áudio: sintetizado em código como no app; o navegador só libera o som após o primeiro toque ou tecla.
- Vibração: só onde o navegador suporta (`navigator.vibrate`, ex.: Chrome no Android). No iPhone não vibra.
- Idioma: português para navegadores em `pt`, inglês nos demais (mesma regra do app).

## Ranking online

- Fim de partida (Clássico e Níveis, pontuação > 0): botão **Salvar no ranking** abre um diálogo HTML (teclado nativo
  do celular) para o apelido. Depois de salvo, abre a tela de Ranking com a posição do jogador destacada (se não
  entrou no top 10, aparece embaixo com a posição real). Se falhar (sem internet), o botão vira "tentar de novo"
  e reenvia sem pedir o nome outra vez.
- Menu: botão **Ranking** com abas Clássico/Níveis e Fácil/Normal/Difícil (top 10 de cada).
- VS CPU não entra: o resultado é vitória/derrota, não pontuação.
- Apelido: até 12 caracteres, maiúsculas, letras (com acentos do português, Ñ e Ü), números, espaço, `.`, `-`, `_`
  (o que a fonte pixel desenha). Empates ficam com quem chegou primeiro.

### API (`snakevs-api`)

| Método | Rota | |
|---|---|---|
| `GET` | `/api/scores/{endless\|levels}/{easy\|normal\|hard}?take=10` | Top N (máx. 50). |
| `POST` | `/api/scores` | `{ mode, difficulty, name, score, apples, level, steps }` → `201 { id, rank, total }`. |
| `DELETE` | `/api/scores/{id}` | Moderação, header `X-Admin-Key`. Desligado sem `SNAKEVS_ADMIN_KEY`. |
| `GET` | `/api/health` | `ok` (para monitoramento). |

Configuração por variáveis de ambiente (todas opcionais):

| Variável | Padrão | |
|---|---|---|
| `SNAKEVS_LISTEN` | `127.0.0.1:5090` | Endereço. Deixe em 127.0.0.1: só o nginx fala com ele. |
| `SNAKEVS_DB` | `./scores.db` | Arquivo SQLite (criado na primeira execução). |
| `SNAKEVS_ADMIN_KEY` | (vazio) | Liga o `DELETE`. |
| `SNAKEVS_ALLOWED_ORIGINS` | (vazio) | Sites (separados por vírgula) que podem chamar a API de outro domínio. |
| `SNAKEVS_SUBMITS_PER_MINUTE` / `SNAKEVS_READS_PER_MINUTE` | `6` / `120` | Limite por IP. |
| `SNAKEVS_THREADS` | `4` | Conexões atendidas ao mesmo tempo. |

### Regras iguais no jogo e no servidor

A checagem de apelido e de pontuação existe em C# (`Leaderboard.cs`, usada pelo jogo) e em Rust (`rules.rs`, no
servidor). O arquivo `tests/fixtures/leaderboard.json` guarda os casos e as respostas certas, **gerados pelo C#**:

- `LeaderboardFixtureTests` (C#) falha se as regras do jogo mudarem e o fixture não for regenerado;
- `cargo test` (Rust) falha se o servidor não bater com o fixture;
- o fixture inclui 36 partidas reais jogadas pela IA: o servidor tem que aceitar todas.

Mudou `Rules.cs` ou `Leaderboard.cs`? Rode
`SNAKEVS_UPDATE_FIXTURES=1 dotnet test tests/SnakeVs.Core.Tests --filter LeaderboardFixture`, depois
`cd src/SnakeVs.Api && cargo test` e ajuste `rules.rs` até passar.

### Proteções e limites

- Apelido normalizado e pontuação conferida contra as regras do jogo (pontos por maçã, bônus, níveis, passos).
- Limite por IP (atrás do nginx, o IP real vem no `X-Forwarded-For`, aceito só de conexões locais).
- Corpo máximo de 4 KB, timeouts de 5 s, número fixo de threads.
- **Limite honesto**: o jogo roda no navegador, então quem souber chamar a API consegue enviar uma pontuação
  inventada dentro do plausível. A checagem barra o absurdo; o resto é moderação.
- Não há filtro de palavrões. Para remover um apelido:
  `curl -X DELETE https://seu-site/api/scores/123 -H "X-Admin-Key: <chave>"` (o `id` aparece no `GET`).

## Rodar localmente

```powershell
cd src/SnakeVs.Api; $env:SNAKEVS_ALLOWED_ORIGINS="http://localhost:5080"; cargo run    # API em :5090
dotnet run --project src/SnakeVs.Web                                                    # jogo em :5080
# abrir http://localhost:5080/?api=http://localhost:5090/
```

O `?api=` só funciona em `localhost` (um link não consegue apontar o jogo publicado para outra API). Sem ele, o jogo
local abre com o ranking indisponível.

## Gerar

**Jogo** (site estático):

```powershell
./scripts/build-web.ps1                                     # testes + publish em artifacts/web/wwwroot (ranking em /api/ do mesmo site)
./scripts/build-web.ps1 -NoRanking                          # sem ranking
./scripts/build-web.ps1 -ApiUrl https://api.exemplo.com/    # ranking em outro domínio
```

**Ranking**: o workflow **ranking-api** do GitHub Actions compila a cada push em `src/SnakeVs.Api` (ou manualmente em
Actions > ranking-api > Run workflow), roda os testes e publica os artefatos `snakevs-api-linux-x64` e
`snakevs-api-linux-arm64` (binário estático + arquivos de `deploy/`). Use o da arquitetura da VM (`uname -m`:
`x86_64` ou `aarch64`). Não é preciso instalar Rust na VM.

Para compilar na sua máquina: `rustup target add x86_64-unknown-linux-musl`, `pip install ziglang`,
`cargo install cargo-zigbuild` e `cargo zigbuild --release --target x86_64-unknown-linux-musl` (funciona no Windows).

## Instalar na VM (nginx + systemd)

1. **Jogo**: copie o conteúdo de `artifacts/web/wwwroot` para `/var/www/snakevs`.
2. **Ranking**:
   ```sh
   sudo install -m 755 snakevs-api /usr/local/bin/
   sudo cp snakevs-api.service /etc/systemd/system/
   echo "SNAKEVS_ADMIN_KEY=$(openssl rand -hex 24)" | sudo tee /etc/snakevs-api.env && sudo chmod 600 /etc/snakevs-api.env
   sudo systemctl daemon-reload && sudo systemctl enable --now snakevs-api
   curl http://127.0.0.1:5090/api/health      # ok
   ```
   O serviço roda com usuário próprio e descartável, prioridade de CPU baixa (`Nice=10`), memória limitada a 32 MB e
   disco somente leitura, exceto `/var/lib/snakevs` (onde fica o `scores.db`).
3. **nginx**: use `deploy/nginx-snakevs.conf` (ajuste `server_name`), habilite e `sudo nginx -t && sudo systemctl reload nginx`.
   HTTPS: `sudo certbot --nginx -d jogo.exemplo.com`. O nginx entrega os arquivos `.gz` já comprimidos pelo publish
   (sem gastar CPU) e manda `/api/` para o `snakevs-api`.

Atualizar:

- **Jogo**: substitua os arquivos de `/var/www/snakevs`.
- **Ranking**: `sudo install -m 755 snakevs-api /usr/local/bin/ && sudo systemctl restart snakevs-api`.
- O banco não é tocado em nenhum dos dois casos.

Logs: `journalctl -u snakevs-api`. Backup a quente (`sudo apt install sqlite3`): `sudo sqlite3 /var/lib/snakevs/scores.db ".backup /root/scores-backup.db"`.

Se a VM usa **Apache** em vez de nginx: sirva `/var/www/snakevs` (o `.htaccess` incluído cuida de tipos, `.gz`/`.br` e
cache) e adicione `ProxyPass "/api/" "http://127.0.0.1:5090/api/"` (`a2enmod proxy proxy_http`); o Apache já acrescenta
o `X-Forwarded-For` que o limite por IP usa.

### Sem ranking ou em host estático

`./scripts/build-web.ps1 -NoRanking` e envie o conteúdo de `artifacts/web/wwwroot` para qualquer host estático
(Apache/LiteSpeed com o `.htaccess` incluído, Cloudflare Pages, Netlify, GitHub Pages). O `index.html` usa
`<base href="./" />`: funciona na raiz ou numa subpasta (acesse com a barra final).

## Checklist de deploy

- [ ] `./scripts/build-web.ps1` sem erros e workflow **ranking-api** verde.
- [ ] `curl http://127.0.0.1:5090/api/health` na VM responde `ok`; `/etc/snakevs-api.env` com a chave de moderação.
- [ ] Abrir o site no celular e no PC: menu, toque/clique, teclado e som após o primeiro toque.
- [ ] Jogar uma partida, salvar no ranking com um apelido e ver a posição no Ranking.
- [ ] DevTools > Network: `dotnet.native.*.wasm` com `content-encoding: gzip` (ou `br`) e `content-type: application/wasm`.
- [ ] Backup periódico do `scores.db`.
- [ ] **Política de privacidade**: com o ranking, o site guarda no servidor o apelido digitado, a pontuação e a data
      (o IP só é usado em memória para o limite de envios, não é gravado). O texto atual diz que nada é armazenado em
      servidores; precisa ser atualizado antes de publicar a versão web com ranking.
