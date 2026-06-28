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
