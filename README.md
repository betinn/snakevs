# Snake VS

Snake clássico (sem fim), modo por níveis com obstáculos e poderes, feito em C# com MonoGame. Alvos: Android (Google Play) e web (navegador, site estático).

![Gráfico de destaque](store/graphics/feature-graphic-1024x500.png)

## O jogo

- **Clássico**: o snake de verdade. Uma tela aberta, sem fim: a cobra cresce sem parar e o jogo acelera a cada 5 maçãs. Maçã dourada e poderes valem aqui também.
- **Níveis**: 8 maçãs por nível. Cada nível novo traz obstáculos e mais velocidade (níveis 1-5 desenhados à mão, depois gerados por semente).
- **VS CPU**: duelo contra uma cobra da CPU (busca em largura + flood fill para não se encurralar). Ganha quem chegar a 15 maçãs ou sobreviver.
- **Poderes**: Lento, Fantasma (atravessa tudo), Pontos x2, Tesoura (corta a cauda), Escudo (salva de uma batida) e Maçã dourada (+50).
- **Controles**: deslizar (padrão) ou botões na tela; teclado/gamepad no desktop e no navegador.
- **Opções**: dificuldade (Fácil/Normal/Difícil), som, vibração, tipo de controle. Recordes salvos no aparelho.
- **Idiomas**: Português (Brasil) e Inglês, escolhidos pelo idioma do sistema.
- **Ranking online (só web)**: ao fim de cada partida do Clássico ou de Níveis, salvar a pontuação com nome/apelido;
  top 10 por modo e dificuldade, guardado em SQLite por um servidor Rust de poucos MB.

## Estrutura

```
src/
  SnakeVs.Core/      regras do jogo puras em C# (sem MonoGame): sessão, cobra, IA, níveis, save
  SnakeVs.Game/      código MonoGame compartilhado (render, fonte pixel, input, áudio, telas)
  SnakeVs.Android/   app Android (Activity, manifest, ícones, assinatura, AAB)
  SnakeVs.Web/       versão web (Blazor WebAssembly + KNI/WebGL): só o host do navegador, o jogo vem de Core/Game
  SnakeVs.Api/       servidor do ranking online (Rust + SQLite, binário estático de ~2 MB) e arquivos de deploy
  SnakeVs.Desktop/   build desktop para desenvolvimento, capturas automáticas e arte da loja
tests/
  SnakeVs.Core.Tests/  testes xUnit (regras, colisões, poderes, níveis, IA, save, simulação em massa, ranking)
  fixtures/            casos do ranking que o C# gera e o servidor Rust precisa cumprir
store/               textos da loja, política de privacidade, ícone, destaque e capturas de tela
docs/                padrão visual (STYLE.md), publicação Android (PUBLISHING.md) e web (WEB.md)
scripts/             chave de upload, build do AAB assinado e build do site
```

O jogo não usa content pipeline: texturas, fonte, ícones e sons são gerados em código. Isso deixa o build simples
e o APK leve.

## Comandos

```powershell
dotnet test                                                    # testes
dotnet run --project src/SnakeVs.Desktop                       # jogar no PC
dotnet run --project src/SnakeVs.Desktop -- --tour shots       # captura todas as telas em 4 resoluções
dotnet run --project src/SnakeVs.Desktop -- --store-art art    # gera ícones Android e gráficos da loja
./scripts/create-keystore.ps1                                  # chave de upload (uma vez)
./scripts/build-android.ps1                                    # testes + AAB assinado em artifacts/android
dotnet run --project src/SnakeVs.Web                           # jogar no navegador (http://localhost:5080)
./scripts/build-web.ps1                                        # testes + site estático em artifacts/web/wwwroot
cd src/SnakeVs.Api; cargo test; cargo run                      # servidor do ranking (binários Linux: workflow ranking-api)
```

Publicação passo a passo: [docs/PUBLISHING.md](docs/PUBLISHING.md) (Android) e [docs/WEB.md](docs/WEB.md) (web). Padrão visual: [docs/STYLE.md](docs/STYLE.md).

## Requisitos

.NET SDK 10 com workload `android`, Android SDK (plataforma 36) e JDK 17+. A versão web precisa só do .NET SDK 10; o servidor do ranking, de Rust (ou só do GitHub Actions).
