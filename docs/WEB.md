# Snake VS na web

A versão web é o mesmo jogo do app, abrindo direto no menu do jogo (sem landing page). Roda no navegador com
WebAssembly + WebGL. Tem um **ranking online** (só na web): ao fim de cada partida do Clássico ou de Níveis o jogador
pode salvar a pontuação com nome/apelido, guardada em SQLite no servidor.

## Como funciona

| Projeto | Papel na web |
|---|---|
| `SnakeVs.Core` | Regras, IA, níveis e save (os mesmos do Android) + contrato do ranking (`Leaderboard.cs`: DTOs, regra de apelido, checagem de pontuação, JSON). |
| `SnakeVs.Game` | Telas, render, fonte, input e áudio: os mesmos arquivos do app. Ranking: `Online/ILeaderboard.cs`, `Scenes/RankingScene.cs` e o botão no fim da partida (`PlayScene`). Só aparece onde a plataforma oferece ranking. |
| `SnakeVs.Web` | Host do navegador: `WebPlatform.cs`, `HttpLeaderboard.cs`, `wwwroot/index.html` e `wwwroot/js/snakevs.js`. |
| `SnakeVs.Server` | ASP.NET Core: serve o jogo e a API `/api/scores` com SQLite (`ScoreStore.cs`). |

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
  (o que a fonte pixel desenha). A mesma regra roda no navegador, no jogo e no servidor (`Leaderboard.NormalizeName`).
  Empates ficam com quem chegou primeiro.

### API

| Método | Rota | |
|---|---|---|
| `GET` | `/api/scores/{endless\|levels}/{easy\|normal\|hard}?take=10` | Top N (máx. 50). |
| `POST` | `/api/scores` | `{ mode, difficulty, name, score, apples, level, steps }` → `201 { id, rank, total }`. |
| `DELETE` | `/api/scores/{id}` | Moderação, com header `X-Admin-Key`. Desligado enquanto `Leaderboard:AdminKey` estiver vazio. |

Proteções:

- O servidor normaliza o apelido e confere se a pontuação é possível pelas regras do jogo (`Leaderboard.IsPlausible`:
  pontos por maçã, bônus, níveis, passos). Um teste joga dezenas de partidas reais com a IA e garante que nenhuma
  pontuação legítima é recusada.
- Limite por IP: 6 envios e 120 leituras por minuto (`Leaderboard:SubmitsPerMinute` / `ReadsPerMinute`).
- **Limite honesto**: o jogo roda no navegador, então quem souber chamar a API consegue enviar uma pontuação
  inventada dentro do plausível. A checagem barra o absurdo; o resto é moderação (`DELETE`).
- Não há filtro de palavrões. Para remover um apelido ofensivo:
  `curl -X DELETE https://seu-site/api/scores/123 -H "X-Admin-Key: <chave>"` (o `id` aparece no `GET`).

### Banco

SQLite em `App_Data/scores.db` (relativo à pasta do servidor; mude em `Leaderboard:DatabasePath`). O arquivo e a
tabela são criados na primeira execução. Modo WAL: leituras não esperam escritas.

- A pasta precisa de permissão de escrita para o usuário do serviço (IIS: `IIS AppPool\<pool>`; Linux: o usuário do systemd).
- Backup a quente: `sqlite3 App_Data/scores.db ".backup scores-backup.db"` (copiar só o `.db` com o serviço rodando
  pode perder o que ainda está no `-wal`).
- Um deploy novo **não** pode sobrescrever `App_Data/` (o publish não inclui essa pasta).

## Rodar localmente

```powershell
dotnet run --project src/SnakeVs.Server     # jogo + ranking em http://localhost:5090
dotnet run --project src/SnakeVs.Web        # só o jogo (sem ranking) em http://localhost:5080
```

## Gerar o site

```powershell
./scripts/build-web.ps1                                   # testes + jogo com ranking (servidor) em artifacts/web
./scripts/build-web.ps1 -StaticOnly                       # só o jogo, site estático, ranking desligado
./scripts/build-web.ps1 -StaticOnly -ApiUrl https://api.exemplo.com/   # estático usando um servidor de ranking em outro lugar
```

Download inicial do jogo: ~2,6 MB com Brotli (o .NET + o jogo). Depois disso o navegador usa o cache.

O endereço da API fica no `index.html`: `<meta name="snakevs-api" content="" />`. Vazio = mesmo site (padrão,
`SnakeVs.Server`); uma URL = servidor em outro domínio; `off` = sem ranking (o botão some do menu e do fim da partida).

## Hospedagem com ranking (recomendado)

`artifacts/web` é um app ASP.NET Core 10 completo: serve o jogo (já comprimido em Brotli/gzip, com cache longo para
os arquivos com hash) e a API. Precisa de um host que rode .NET 10.

### Linux (VPS) com systemd + nginx

```ini
# /etc/systemd/system/snakevs.service
[Unit]
Description=Snake VS
After=network.target

[Service]
WorkingDirectory=/var/www/snakevs
ExecStart=/usr/bin/dotnet /var/www/snakevs/SnakeVs.Server.dll --urls http://127.0.0.1:5090
User=www-data
Restart=always
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=Leaderboard__AdminKey=troque-por-uma-chave-longa

[Install]
WantedBy=multi-user.target
```

```nginx
server {
    listen 443 ssl;
    server_name jogo.exemplo.com;

    location / {
        proxy_pass http://127.0.0.1:5090;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

O servidor confia no `X-Forwarded-For` só quando vem do próprio servidor (127.0.0.1), então o limite por IP funciona
atrás do nginx sem deixar o cliente forjar o IP.

### IIS / Azure App Service

Instale o *ASP.NET Core Hosting Bundle* (.NET 10) e envie `artifacts/web` inteira (o publish gera o `web.config`).
Dê permissão de escrita em `App_Data` ao pool. A chave de moderação vai em variável de ambiente
(`Leaderboard__AdminKey`) ou em `appsettings.Production.json` (não versionar).

### Jogo e API em lugares diferentes

Jogo estático (Apache, Cloudflare Pages...) + `SnakeVs.Server` em outro domínio: gere o jogo com
`-StaticOnly -ApiUrl https://api.exemplo.com/` e no servidor libere a origem do jogo:
`Leaderboard__AllowedOrigins__0=https://jogo.exemplo.com`.

## Hospedagem só do jogo (sem ranking)

`./scripts/build-web.ps1 -StaticOnly` e envie **o conteúdo** de `artifacts/web/wwwroot`.

- Apache / LiteSpeed (cPanel, Hostinger etc.): o `.htaccess` incluído cuida de tipos, `.br`/`.gz` e cache. Envie
  também o `.htaccess` (arquivo oculto). Precisa de `mod_rewrite`, `mod_headers` e `AllowOverride All`.
- Cloudflare Pages, Netlify, GitHub Pages, Azure Static Web Apps: já servem `.wasm` certo e comprimem sozinhos.
- nginx: sirva a pasta com `gzip_static on;` (e `brotli_static on;` se tiver o ngx_brotli), `application/wasm` para
  `.wasm`, `Cache-Control: no-cache` no geral e `max-age=31536000, immutable` para `_framework/*.<hash>.*`.

O `index.html` usa `<base href="./" />`: funciona na raiz ou numa subpasta (acesse com a barra final).

## Checklist de deploy

- [ ] `./scripts/build-web.ps1` sem erros (roda os testes do Core e do servidor).
- [ ] `Leaderboard:AdminKey` definido no servidor (fora do repositório) e `App_Data` com permissão de escrita.
- [ ] Abrir o site no celular e no PC: menu, toque/clique, teclado e som após o primeiro toque.
- [ ] Jogar uma partida, salvar no ranking com um apelido e ver a posição no Ranking.
- [ ] DevTools > Network: `dotnet.native.*.wasm` com `content-encoding: br` e `content-type: application/wasm`.
- [ ] Backup periódico do `scores.db`.
- [ ] **Política de privacidade**: com o ranking, o site passa a guardar no servidor o apelido digitado, a pontuação e
      a data (o IP só é usado em memória para o limite de envios, não é gravado). O texto atual diz que nada é
      armazenado em servidores; precisa ser atualizado antes de publicar a versão web com ranking.
