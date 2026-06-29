# Vortex AI Assistant

Vortex AI Assistant; Avalonia tabanlı Desktop Client, güvenli Local Agent, merkezi Backend/Control Server ve ayrı Admin yüzeyinden oluşan dağıtık bir yapay zekâ asistanı iskeletidir.

## Projeler

- `Vortex.Desktop`: Avalonia UI masaüstü istemcisi.
- `Vortex.LocalAgent`: Yalnızca `127.0.0.1` üzerinde çalışan yerel ajan.
- `Vortex.Server`: ASP.NET Core API, JWT oturum, SQLite veri tabanı, model router ve OpenAI uyumlu streaming proxy.
- `Vortex.Admin`: Ayrı admin paneli başlangıç yüzeyi.
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
dotnet run --project Vortex.LocalAgent
dotnet run --project Vortex.Admin
dotnet run --project Vortex.Desktop
```

Desktop ilk bağlantıda geliştirme kullanıcısı olarak `owner@vortex.local` hesabını oluşturmayı dener. Üretimde bu akış ilk kurulum ekranıyla değiştirilmelidir.

## Test

```bash
dotnet test VortexAI.sln
```

Hermes izolasyon ve Free plan limitleri için çalışan integration test:

```bash
dotnet test VortexAI.sln -c Release --filter HermesIsolationIntegrationTests
```

Tam doğrulama komutu:

```bash
dotnet build VortexAI.sln -c Release
dotnet test VortexAI.sln -c Release --no-build
```

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
