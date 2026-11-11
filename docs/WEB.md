# Snake VS na web

A versão web é o mesmo jogo do app, abrindo direto no menu do jogo (sem landing page). Roda no navegador com
WebAssembly + WebGL e é publicada como **site estático**: não precisa de servidor .NET, banco ou back-end.

## Como funciona

| Projeto | Papel na web |
|---|---|
| `SnakeVs.Core` | Regras, IA, níveis e save: os mesmos do Android, sem nenhuma mudança. |
| `SnakeVs.Game` | Telas, render, fonte, input e áudio: os mesmos arquivos, compilados também no projeto web. |
| `SnakeVs.Web` | Só o host do navegador: `WebPlatform.cs`, `wwwroot/index.html` e `wwwroot/js/snakevs.js`. |

- Motor: [KNI](https://github.com/kniEngine/kni), compatível com a API XNA/MonoGame usada pelo jogo e com suporte a
  Blazor WebAssembly/WebGL. Android e desktop continuam no MonoGame. A única diferença de API entre os dois está em
  `SnakeGame.cs` (`#if KNI`, assinatura de `OnDeactivated`/`OnExiting`).
- Save: `localStorage` do navegador (chave `snakevs.save`, mesmo formato do arquivo do app). Fica por domínio:
  trocar de domínio ou limpar os dados do site zera recordes e opções.
- Tela: canvas em tela cheia na resolução real do aparelho (até 3x), respeitando notch/cantos (`safe-area-inset`).
- Input: toque, mouse e caneta via Pointer Events, enfileirados entre frames (um toque rápido nunca se perde);
  teclado (setas/WASD, Enter/Espaço, Esc, P) e gamepad.
- Botão/gesto "voltar" do navegador: funciona como no Android (pausa, volta de tela; no menu sai do site).
  Esc no menu não sai da página.
- Áudio: os efeitos são sintetizados em código como no app; o navegador só libera o som após o primeiro toque ou tecla.
- Vibração: só onde o navegador suporta (`navigator.vibrate`, ex.: Chrome no Android). No iPhone não vibra.
- Idioma: português para navegadores em `pt`, inglês nos demais (mesma regra do app).

## Rodar localmente

```powershell
dotnet run --project src/SnakeVs.Web        # http://localhost:5080
```

## Gerar o site

```powershell
./scripts/build-web.ps1                     # testes + publish em artifacts/web
```

Ou direto: `dotnet publish src/SnakeVs.Web -c Release -o artifacts/web`.

Saída:

- `artifacts/web/wwwroot/` é o site. Para Apache/LiteSpeed, nginx e hosts estáticos, envie **o conteúdo** desta pasta.
- `artifacts/web/` inteira (com `web.config`) é para IIS / Azure App Service.

Download inicial: ~2,6 MB com Brotli (o .NET + o jogo). Depois disso o navegador usa o cache.

## Hospedagem

Requisitos de qualquer host:

1. Servir `.wasm` como `application/wasm` (e `.dat` como `application/octet-stream`).
2. Usar os arquivos pré-comprimidos `.br`/`.gz` gerados no publish (senão o download fica ~3x maior).
3. `index.html` sem cache longo; arquivos com hash no nome (`nome.abc123defg.js`, `.wasm`) com cache de 1 ano.
   Assim cada deploy novo aparece na hora e o resto continua em cache.
4. HTTPS (navegadores restringem vários recursos em HTTP).

### Apache / LiteSpeed (hospedagem compartilhada, cPanel, Hostinger etc.)

Já resolvido: o `.htaccess` em `wwwroot` vai junto no publish e cuida de tipos, `.br`/`.gz` e cache.
Basta enviar o conteúdo de `artifacts/web/wwwroot` (inclusive o `.htaccess`, que é um arquivo oculto) para a pasta
do site. Precisa de `mod_rewrite`, `mod_headers` e `AllowOverride All`, padrão nesses hosts.

### IIS / Azure App Service

Envie `artifacts/web` inteira. O `web.config` gerado pelo publish configura os tipos MIME e aponta para `wwwroot`.
Precisa do módulo URL Rewrite.

### nginx

```nginx
server {
    listen 443 ssl;
    server_name jogo.exemplo.com;
    root /var/www/snakevs;              # conteúdo de artifacts/web/wwwroot

    include /etc/nginx/mime.types;      # um bloco types sozinho substituiria a lista padrão
    types {
        application/wasm wasm;
        application/octet-stream dat blat;
    }

    brotli_static on;                   # requer ngx_brotli; sem ele, deixe só o gzip_static
    gzip_static on;

    location / {
        add_header Cache-Control "no-cache";
        try_files $uri $uri/ /index.html;
    }

    location ~* "\.[a-z0-9]{10}\.(js|wasm|dat)$" {
        add_header Cache-Control "public, max-age=31536000, immutable";
    }
}
```

### Hosts estáticos (Cloudflare Pages, Netlify, GitHub Pages, Azure Static Web Apps)

Envie o conteúdo de `artifacts/web/wwwroot`. Eles já servem `.wasm` com o tipo certo e comprimem sozinhos.

### Subpasta

O `index.html` usa `<base href="./" />`, então o jogo funciona tanto na raiz (`https://site.com/`) quanto numa
subpasta (`https://site.com/snake/`). Na subpasta, acesse com a barra final.

## Checklist de deploy

- [ ] `./scripts/build-web.ps1` sem erros.
- [ ] Abrir o site no celular e no PC: menu aparece, toque/clique e teclado funcionam, som após o primeiro toque.
- [ ] DevTools > Network: `dotnet.native.*.wasm` com `content-encoding: br` (ou `gzip`) e `content-type: application/wasm`.
- [ ] Recarregar a página mantém recordes e opções.
- [ ] Política de privacidade: a versão web também só grava recordes/opções no próprio navegador
      (`localStorage`), sem cookies nem analytics. O texto atual cita "app" e "desinstalar"; ajuste se for linkar
      a política no site.
