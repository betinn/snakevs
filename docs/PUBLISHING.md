# Publicação na Google Play

Guia passo a passo para levar o Snake VS do código até a Play Store.

## 1. Pré-requisitos

- .NET SDK 10 com o workload Android (`dotnet workload install android`).
- Android SDK com a plataforma 36 (o Visual Studio já instala em `C:\Program Files (x86)\Android\android-sdk`).
- JDK 17+ (o Visual Studio instala em `C:\Program Files\Android\openjdk`).
- Conta de desenvolvedor Google Play (taxa única).

## 2. Chave de upload (uma vez só)

```powershell
./scripts/create-keystore.ps1
```

Cria `%USERPROFILE%\.snakevs-keys\snakevs-upload.jks` e `snakevs-upload.properties` (com a senha), **fora do repositório**.
Faça backup dessa pasta. O projeto usa *Play App Signing*: o Google guarda a chave final do app; esta é só a chave de
upload. Se ela for perdida, dá para pedir a troca no Play Console.

Em CI, use variáveis de ambiente em vez do arquivo: `SNAKEVS_KEYSTORE`, `SNAKEVS_KEY_ALIAS`,
`SNAKEVS_KEYSTORE_PASS`, `SNAKEVS_KEY_PASS`.

## 3. Gerar o bundle (.aab)

```powershell
./scripts/build-android.ps1                                   # roda os testes e gera o AAB
./scripts/build-android.ps1 -VersionCode 2 -VersionName 1.0.1  # próximas versões
```

Saída: `artifacts/android/com.betinn.snakevs-Signed.aab`. O `versionCode` precisa aumentar a cada envio.
O padrão fica em `src/SnakeVs.Android/SnakeVs.Android.csproj` (`ApplicationVersion` / `ApplicationDisplayVersion`).

APK assinado para instalar direto num aparelho:

```powershell
dotnet publish src/SnakeVs.Android -c Release -p:AndroidPackageFormat=apk -o artifacts/apk
adb install -r artifacts/apk/com.betinn.snakevs-Signed.apk
```

## 4. Requisitos da Play já atendidos

| Requisito | Situação |
|---|---|
| Formato Android App Bundle | `AndroidPackageFormat=aab` no Release |
| API alvo recente | `targetSdkVersion 36` (Android 16) |
| 64 bits | `arm64-v8a` e `x86_64` (mais `armeabi-v7a` para aparelhos antigos) |
| Páginas de memória de 16 KB | Todas as libs nativas 64 bits com segmentos alinhados a 16 KB (verificado) |
| Gesto voltar preditivo (Android 13+) | `OnBackInvokedCallback` registrado; o jogo nunca fecha sem querer |
| Edge-to-edge / recortes de tela | Tela cheia imersiva e área segura respeitada no HUD |
| Telas grandes / orientação | Layout responsivo funciona em retrato, paisagem e tablets |
| Ícone adaptativo + ícone temático | `mipmap-anydpi-v26` com camada monocromática |
| Permissões | Só `VIBRATE` (normal, sem diálogo) |
| Coleta de dados | Nenhuma. Sem internet, sem anúncios, sem analytics |

## 5. Play Console

1. **Criar app**: nome "Snake VS", idioma padrão Português (Brasil), tipo Jogo, gratuito.
2. **Ficha da loja**: textos em `store/listing-pt-BR.md` e `store/listing-en-US.md`.
   - Ícone 512x512: `store/graphics/icon-512.png`
   - Gráfico de destaque 1024x500: `store/graphics/feature-graphic-1024x500.png`
   - Capturas de tela (celular e tablet): `store/screenshots/` (geradas por `SnakeVs --tour <pasta>`)
3. **Política de privacidade**: publique `store/privacy-policy.md` (GitHub Pages, Google Sites, etc.) e cole a URL.
4. **Segurança dos dados**: responda "O app não coleta nem compartilha dados do usuário".
5. **Classificação de conteúdo (IARC)**: jogo casual, sem violência realista, sem interação entre usuários,
   sem compras, sem anúncios. Resultado esperado: Livre / PEGI 3 / Everyone.
6. **Público-alvo**: 13+ é o caminho mais simples. Se incluir menores de 13, o app já cumpre a política
   Famílias (sem anúncios, sem coleta de dados), mas o formulário é mais longo.
7. **Anúncios**: "Não, o app não contém anúncios".
8. **Teste**: contas pessoais novas precisam de *teste fechado* com pelo menos 12 testadores por 14 dias
   antes de liberar a produção. Envie o AAB primeiro na faixa de teste fechado.
9. **Produção**: crie a versão, envie `com.betinn.snakevs-Signed.aab`, preencha as notas da versão e envie para revisão.

## 6. Checklist antes de cada envio

- [ ] `dotnet test` verde
- [ ] `versionCode` aumentado
- [ ] Testado num aparelho real (deslizar, botões, pausa ao sair do app, gesto voltar, som e vibração)
- [ ] Capturas de tela atualizadas se o visual mudou
