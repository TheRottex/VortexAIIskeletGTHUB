# Vortex AI Assistant

Vortex AI Assistant; Avalonia tabanlı Desktop Client, güvenli Local Agent, merkezi Backend/Control Server ve ayrı Admin yüzeyinden oluşan dağıtık bir yapay zekâ asistanı iskeletidir.

## Projeler

- `Vortex.Desktop`: Avalonia UI masaüstü istemcisi.
- `Vortex.LocalAgent`: Yalnızca `127.0.0.1` üzerinde çalışan yerel ajan.
- `Vortex.Server`: ASP.NET Core API, JWT oturum, SQLite veri tabanı, model router ve OpenAI uyumlu streaming proxy.
- `Vortex.Admin`: Ayrı admin paneli başlangıç yüzeyi.
- `Vortex.Web`: Kullanıcı kayıt/giriş ve Desktop authorization web sitesi.
- `Vortex.Shared`: DTO, sözleşme, rol, özellik ve güvenlik yardımcıları.
- `Vortex.Tests`: Temel sözleşme testleri.

## Güvenlik kararları

- Sağlayıcı API anahtarları Desktop veya LocalAgent içinde yoktur.
- İlk Owner hesabı ilk kayıt akışında oluşur; sabit admin parolası yoktur.
- LocalAgent dış ağa açılmaz; varsayılan URL `http://127.0.0.1:47891`.
- Riskli yerel işlemler ilk sürümde otomatik çalıştırılmaz; açık kullanıcı onayı ve imzalı istek altyapısı için sözleşmeler hazırdır.
- API anahtarları loglanmamalıdır; görüntüleme için `SecretMasker` kullanılır.

## Geliştirme çalıştırma

```bash
dotnet restore VortexAI.sln
dotnet build VortexAI.sln

dotnet run --project Vortex.Server --urls http://127.0.0.1:5000
dotnet run --project Vortex.Web --urls http://127.0.0.1:5080
dotnet run --project Vortex.LocalAgent
dotnet run --project Vortex.Admin
dotnet run --project Vortex.Desktop
```

Windows geliştirme ortamında tüm servisleri health check ile sırayla başlatmak için:

```powershell
./scripts/start-dev.ps1
```

Yalnızca bu scriptin başlattığı süreçleri kapatmak için:

```powershell
./scripts/stop-dev.ps1
```

Desktop artık sabit geliştirme kullanıcısı/parolası oluşturmaz. Giriş ve kayıt işlemleri `Vortex.Web` üzerinden yapılır; Desktop yalnızca Authorization Code + PKCE benzeri kısa ömürlü akışla token alır.

## Test

```bash
dotnet test VortexAI.sln
```

Hermes izolasyon ve Free plan limitleri için çalışan integration test:

```bash
dotnet test VortexAI.sln -c Release --filter HermesIsolationIntegrationTests
```

Desktop web login + PKCE authorization code akışı için çalışan integration test:

```bash
dotnet test VortexAI.sln -c Release --filter DesktopAuthIntegrationTests
```

Tam doğrulama komutu:

```bash
dotnet build VortexAI.sln -c Release
dotnet test VortexAI.sln -c Release --no-build
```

## Desktop web giriş akışı

Desktop giriş yapmamışsa karşılama ekranı gösterir. `Giriş Yap` ve `Kayıt Ol` düğmeleri varsayılan tarayıcıyı açar.

Akış:

1. Desktop rastgele `state` ve `code_verifier` üretir.
2. Server'da kısa ömürlü `DesktopAuthSessions` kaydı oluşturulur.
3. Tarayıcı `Vortex.Web /desktop/authorize` sayfasına gider.
4. Kullanıcı `/register` veya `/login` üzerinden web sitesinde kimlik doğrular.
5. Web sitesi authorization code üretimini Server'a onaylatır.
6. Başarı sayfası “Giriş başarılı. Artık işleminize Vortex uygulamasından devam edebilirsiniz.” mesajını gösterir.
7. Tarayıcı loopback callback adresine döner.
8. Desktop `state` doğrular, code'u PKCE verifier ile Server'da token'a çevirir.
9. URL içinde JWT taşınmaz; authorization code tek kullanımlık ve kısa ömürlüdür.

## Hermes Agent izolasyonu

Kullanıcı kayıt olduğunda Vortex Server otomatik olarak kullanıcıya özel Hermes profilini hazırlar:

```text
App_Data/hermes-profiles/{userId}/
├── config
├── memory
├── sessions
├── cron
├── skills
├── workspace
└── logs
```

E-posta klasör adı olarak kullanılmaz. Server, profile erişimini token içindeki `UserId` üzerinden yapar; istemciden gelen farklı profil ID değerleri dikkate alınmaz.

Free plan agent policy seed değerleri:

- Günlük Hermes agent çalıştırma limiti: 5
- Aktif zamanlanmış görev limiti: 3
- Kalıcı hafıza limiti: 25
- Alt ajan: kapalı
- Terminal/sistem komutu: kapalı
- Dosya erişimi: yalnızca workspace
- Maksimum görev süresi: 60 saniye
- Eşzamanlı agent görevi: 1

Hermes gateway varsayılan olarak test/geliştirme için fake çalışır:

```json
{
  "Hermes": {
    "UseFakeGateway": true,
    "ProfilesRoot": "App_Data/hermes-profiles",
    "ExecutablePath": ""
  }
}
```

Gerçek Hermes executable kullanılacaksa `Hermes:UseFakeGateway=false` ve `Hermes:ExecutablePath` yapılandırılmalıdır.

## Pardus/Linux build

```bash
bash scripts/build-linux.sh
bash scripts/package-deb.sh
sudo apt install ./artifacts/deb/vortex-ai-assistant_0.1.0_amd64.deb
```

## Bir sonraki aşama

1. Admin paneline sağlayıcı anahtarı ekleme/değiştirme ekranı.
2. API anahtarlarını secret manager veya ayrı şifreleme anahtarıyla korunan veri alanına taşıma.
3. PlanModelPolicy CRUD ekranları.
4. LocalAgent istek imzası, replay koruması ve kalıcı izin kasası.
5. Gerçek STT/TTS sağlayıcı implementasyonları.
6. Dosya depolama kota ve kullanıcı izolasyon servisleri.
