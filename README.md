# Snake VS

Snake em fases contínuas (Jornada), clássico sem fim, níveis e duelo contra a CPU, feito em C# com MonoGame/KNI. Alvo: web (navegador, site estático). O projeto Android continua no repositório, mas o desenvolvimento novo é só para a web.

![Gráfico de destaque](store/graphics/feature-graphic-1024x500.png)

## O jogo

- **Jornada**: fases empilhadas, sem parada entre elas. Coma as maçãs da fase, o portal abre na parede de cima,
  a cobra atravessa e o mapa rola para a fase seguinte com ela em movimento; o número e o nome da fase aparecem
  por um instante e o jogo segue. Cada fase traz um elemento novo e depois elas se misturam (ver abaixo).
- **Clássico**: o snake de verdade. Uma tela aberta, sem fim: a cobra cresce sem parar e o jogo acelera a cada 5 maçãs. Maçã dourada e poderes valem aqui também.
- **Níveis**: 8 maçãs por nível. Cada nível novo traz obstáculos e mais velocidade (níveis 1-5 desenhados à mão, depois gerados por semente).
- **VS CPU**: duelo contra uma cobra da CPU (busca em largura + flood fill para não se encurralar). Ganha quem chegar a 15 maçãs ou sobreviver.
- **Poderes**: Lento, Fantasma (atravessa tudo), Pontos x2, Tesoura (corta a cauda), Escudo (salva de uma batida) e Maçã dourada (+50).
- **Controles**: deslizar (padrão) ou botões na tela; teclado/gamepad no desktop e no navegador.
- **Opções**: dificuldade (Fácil/Normal/Difícil), som, vibração, tipo de controle. Recordes salvos no aparelho.
- **Idiomas**: Português (Brasil) e Inglês, escolhidos pelo idioma do sistema.
- **Ranking online (só web)**: ao fim de cada partida da Jornada, do Clássico ou de Níveis, salvar a pontuação com nome/apelido;
  top 10 por modo e dificuldade, guardado em SQLite por um servidor Rust de poucos MB.

## Jornada

Regra única para os perigos: **na cabeça mata, na cauda corta** (a cobra perde o pedaço atingido e tudo atrás dele).
Tudo anda no compasso dos passos da cobra, então o poder Lento desacelera os perigos junto.

| Fase | Elemento | O que faz |
|---|---|---|
| 1 | Largada | Uma maçã só, embaixo: descer, comer, subir pelo portal. |
| 2 | Blocos | Paredes fixas (barras, pilares, zigue-zague, cantos). |
| 3 | Serras | Lâminas que correm em trilhos, indo e voltando ou em circuito. |
| 4 | Lasers | Raios que atravessam a fase: apagado, aviso piscando, ligado. |
| 5 | Besouros | Andam pela fase, mordem a cabeça e desviam do corpo. |
| 6 | Pisca-pisca | Blocos (ou barreiras inteiras) que somem e voltam em turnos. |
| 7 | Lava | Sobe do fundo, linha por linha, até um limite marcado. |
| 8 | Teleportes | Uma parede corta a fase ao meio: só os anéis ligam os dois lados. |
| 9 | Espectro | Persegue a cabeça atravessando paredes; comer uma maçã o atordoa. |
| 10 | Apagão | Só se vê em volta da cabeça, das maçãs e do portal aberto. |
| 11 | Gelo | No gelo não dá para virar: a cobra desliza reto. |
| 12 | Meteoros | Miras avisam onde vão cair (a casa e as quatro vizinhas). |
| 13 | Cobra rival | Rouba as maçãs. Se bater em você: bônus e uma maçã dourada. |
| 14 | Torre laser | Bloco central com quatro raios que disparam em rodízio. |
| 15+ | Misturas | Dois elementos por fase (três a cada cinco fases, "CAOS"), cada vez mais intensos. |

- Ao abrir o portal, dois postes nascem ao lado dele (nunca em cima nem bem na frente da cobra).
- Não há volta: o portal atravessado fecha e os perigos da fase que ficou para trás se desligam.
- Pontos: maçã `10 + 2 x (fase - 1)`, bônus de `50 x fase` ao atravessar o portal, `50` pela rival derrubada.
- Velocidade: 2 ms mais rápido por fase, até o piso de 80 ms por passo.
- As fases saem de uma semente (`StageGenerator`): entrada e saída sempre livres, todo espaço alcançável e nenhuma
  maçã em beco sem saída (os testes conferem isso em milhares de fases).

## Estrutura

```
src/
  SnakeVs.Core/      regras do jogo puras em C# (sem MonoGame): sessão, cobra, IA, níveis, save; Journey/ tem a Jornada
  SnakeVs.Game/      código MonoGame compartilhado (render, fonte pixel, input, áudio, telas)
  SnakeVs.Android/   app Android (Activity, manifest, ícones, assinatura, AAB)
  SnakeVs.Web/       versão web (Blazor WebAssembly + KNI/WebGL): só o host do navegador, o jogo vem de Core/Game
  SnakeVs.Api/       servidor do ranking online (Rust + SQLite, binário estático de ~2 MB) e arquivos de deploy
  SnakeVs.Desktop/   build desktop para desenvolvimento, capturas automáticas e arte da loja
tests/
  SnakeVs.Core.Tests/  testes xUnit (regras, colisões, poderes, níveis, Jornada, IA, save, simulação em massa, ranking)
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
                                                               # ?stage=7 abre a Jornada na fase 7; &auto=1 põe a CPU para jogar (só em localhost)
./scripts/build-web.ps1                                        # testes + site estático em artifacts/web/wwwroot
cd src/SnakeVs.Api; cargo test; cargo run                      # servidor do ranking (binários Linux: workflow ranking-api)
```

Publicação passo a passo: [docs/PUBLISHING.md](docs/PUBLISHING.md) (Android) e [docs/WEB.md](docs/WEB.md) (web). Padrão visual: [docs/STYLE.md](docs/STYLE.md).

## Requisitos

.NET SDK 10 com workload `android`, Android SDK (plataforma 36) e JDK 17+. A versão web precisa só do .NET SDK 10; o servidor do ranking, de Rust (ou só do GitHub Actions).
