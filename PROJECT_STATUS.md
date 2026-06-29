# PROJECT STATUS

Tarih: 2026-06-29

## Mevcut korunan durum

Hermes profil izolasyonu ve Free plan limit sistemi korunmuştur. Bu sistem yeniden yazılmadı.

## Bugünkü düzeltme hedefi

Browser callback başarılı çalışmasına rağmen Vortex Desktop'ın giriş yapmış duruma geçmemesi düzeltildi. Yalnızca callback sonrası Desktop oturum tamamlama akışına müdahale edildi.

## Yapılan düzeltmeler

- Desktop callback başarı yanıtı artık yalnızca şu adımlar tamamlandıktan sonra tarayıcıya gönderiliyor:
  1. Callback alındı.
  2. State doğrulandı.
  3. Authorization code alındı.
  4. Code-token exchange çağrısı yapıldı.
  5. Exchange HTTP durum kodu alındı.
  6. AuthResponse parse edildi.
  7. Access token kaydedildi.
  8. `/api/me` çağrısı başarılı oldu.
- Hassas `state`, `code`, `verifier` ve token değerleri loglanmıyor.
- Desktop log dosyası eklendi:
  - `%LOCALAPPDATA%/VortexAI/logs/desktop-yyyyMMdd.log`
- `ExchangeDesktopCodeAsync` yerine durum kodu ve güvenli hata gövdesi döndüren detaylı exchange metodu eklendi.
- Exchange başarısız olursa callback tarayıcı sayfasında HTTP durum kodu ve güvenli hata gövdesi gösteriliyor.
- `auth.User` nesnesine körü körüne güvenme kaldırıldı; token kaydedildikten sonra `/api/me` ile güncel kullanıcı alınıyor.
- ViewModel UI state güncellemeleri UI thread üzerinden yapılıyor.
- Test ortamında Avalonia dispatcher loop yoksa UI action doğrudan çalıştırılıyor; gerçek uygulamada Dispatcher kullanılıyor.
- Hermes durumu alınamazsa kullanıcı girişi geri alınmıyor; sadece şu metin gösteriliyor:
  - `Hermes durumu alınamadı`
- `AuthenticateAsync` catch bloğu exception ayrıntısını desktop log dosyasına yazıyor.
- `IsAuthenticated` değiştiğinde `IsWelcomeVisible` güncelleniyor.
- `MainWindow.axaml` görünürlük bindingleri kontrol edildi:
  - Welcome: `IsWelcomeVisible`
  - Ana ekran: `IsAuthenticated`
- Authorization state dönüşü düzeltildi:
  - `state` artık callback URI içine gömülmüyor.
  - Desktop authorization URL üzerinden web akışına taşınıyor.
  - Web `/desktop/authorize` state'i server complete çağrısına gönderiyor.
  - Server state hash doğrulayıp callback'e doğru state'i ekliyor.

## Eklenen / güncellenen dosyalar

```text
Vortex.Shared/Contracts.cs
Vortex.Server/Services/DesktopAuthService.cs
Vortex.Server/Program.cs
Vortex.Web/Pages/DesktopAuthorize.cshtml
Vortex.Web/Pages/DesktopAuthorize.cshtml.cs
Vortex.Desktop/Services/BackendClient.cs
Vortex.Desktop/Services/DesktopAuthenticationService.cs
Vortex.Desktop/Services/DesktopLogService.cs
Vortex.Desktop/ViewModels/MainWindowViewModel.cs
Vortex.Tests/DesktopAuthIntegrationTests.cs
Vortex.Tests/DesktopViewModelAuthTests.cs
Vortex.Tests/Vortex.Tests.csproj
```

## Testler

Eklenen doğrulama:

- Authorization code + PKCE integration testi güncellendi.
- Server complete endpoint'i artık state doğruluyor.
- ViewModel `LoginCommand` tamamlandığında:
  - `IsAuthenticated == true`
  - `IsWelcomeVisible == false`
  - kullanıcı adı/planı görünüyor
  - Hermes hatası kullanıcı girişini geri almıyor

## Çalıştırılan komutlar

```bash
dotnet build VortexAI.sln -c Release
dotnet test VortexAI.sln -c Release --no-build
```

Sonuç:

```text
Build: başarılı, 0 uyarı, 0 hata
Test: başarılı, 8/8 test geçti
```

## Gerçek servis durumu

`powershell.exe -File scripts/start-dev.ps1` denendi ancak Windows PowerShell execution policy script çalıştırmayı engelledi.

Script yerine bu oturumdan `dotnet run` ile servisleri başlatmayı denedim; sistemde zaten çalışan Debug süreçleri DLL dosyalarını kilitlediği için yeni süreçler derleme aşamasında durdu. Kullanıcıya ait çalışan süreçleri zorla kapatmadım.

Mevcut çalışan servislerin health endpointleri kontrol edildi:

```text
Vortex.Server: 200 /health
Vortex.Web: 200 /health
Vortex.LocalAgent: 200 /health
```

Yeni kodla gerçek Desktop akışını manuel doğrulamak için mevcut çalışan Debug servisleri kapatılıp yeniden başlatılmalıdır:

```powershell
./scripts/stop-dev.ps1
./scripts/start-dev.ps1
```

PowerShell script policy engeli devam ederse kullanıcı kendi oturumunda script çalıştırma politikasını izinli hale getirmelidir veya servisler elle başlatılmalıdır.
