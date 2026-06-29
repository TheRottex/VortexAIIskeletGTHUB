# PROJECT STATUS

Tarih: 2026-06-29

## Bugünkü hedef

Vortex'e kayıt olan test kullanıcıları için tamamen izole Hermes Agent profili oluşturmak ve Free plan limitlerini Vortex Server tarafında Hermes çalışmadan önce uygulatmak.

## Tamamlanan işler

- `UserAgentProfiles` tablosu eklendi.
- `AgentUsageCounters` tablosu eklendi.
- `AgentExecutionLogs` tablosu eklendi.
- `AgentScheduledTasks` tablosu eklendi.
- Merkezi `PlanAgentPolicies` tablosu eklendi.
- Free plan seed policy değerleri eklendi:
  - Günlük agent run limiti: 5
  - Aktif scheduled task limiti: 3
  - Kalıcı memory limiti: 25
  - Sub-agent kapalı
  - Terminal kapalı
  - Sistem komutu kapalı
  - Workspace dışı dosya erişimi kapalı
  - Maksimum çalışma süresi: 60 saniye
  - Eşzamanlı run limiti: 1
- Kayıt akışına Hermes profil provisioning eklendi.
- Her kullanıcı için ayrı profil/home dizini oluşturuluyor:
  - `config`
  - `memory`
  - `sessions`
  - `cron`
  - `skills`
  - `workspace`
  - `logs`
- E-posta klasör adı olarak kullanılmıyor; güvenli `UserId` kullanılıyor.
- `IAgentIsolationService` / `AgentIsolationService` eklendi.
- `IHermesProfileService` / `HermesProfileService` eklendi.
- `IHermesGatewayService` / `HermesGatewayService` eklendi.
- `FakeHermesGatewayService` eklendi.
- `IAgentPolicyService` / `AgentPolicyService` eklendi.
- `IAgentUsageService` / `AgentUsageService` eklendi.
- Endpointler eklendi:
  - `POST /api/agent/provision`
  - `GET /api/agent/status`
  - `POST /api/agent/chat`
  - `GET /api/agent/tasks`
  - `POST /api/agent/tasks`
  - `DELETE /api/agent/tasks/{id}`
- Agent chat endpoint'i istemciden gelen `RequestedProfileId` değerini güvenlik için dikkate almıyor; profil token içindeki `UserId` ile bulunuyor.
- Limit aşıldığında Hermes gateway çağrılmadan `429 Too Many Requests` dönüyor.
- Başarılı agent çağrısından sonra sayaç artırılıyor.
- Integration test eklendi:
  - User A ve User B kayıt olur.
  - Farklı Hermes profile/home değerleri doğrulanır.
  - User A memory verisi User B tarafından görülemez.
  - User A 5 başarılı agent çağrısı yapar.
  - 6. çağrı 429 döner.
  - User A 3 aktif görev oluşturur.
  - 4. görev 429 döner.
  - User B sayaçları User A limitlerinden etkilenmez.
  - Farklı profile ID gönderilse de server kendi kullanıcı profilini kullanır.

## Doğrulama

Çalıştırılan final komutlar:

```bash
dotnet build VortexAI.sln -c Release
dotnet test VortexAI.sln -c Release --no-build
```

Sonuç:

```text
Build: başarılı, 0 uyarı, 0 hata
Test: başarılı, 6/6 test geçti
```

Not: İlk `dotnet build VortexAI.sln` denemesi Debug çıktısında çalışan `Vortex.Desktop` süreçleri DLL kilitlediği için başarısız oldu. Kullanıcı süreçlerini zorla kapatmadım. Release build ve testler temiz şekilde geçti.

## Sonraki güvenli adım

Gerçek Hermes executable varsa `Hermes:UseFakeGateway=false` ve `Hermes:ExecutablePath` ayarlanarak yalnızca provisioning komutu güvenli argümanlarla test edilmeli. Komut çıktıları gizli veri sızdırmamak için loglanmamalı.
