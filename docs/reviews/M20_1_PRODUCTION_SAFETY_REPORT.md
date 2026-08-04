# M20.1 — Production Safety and Pre-Live Verification

> Kapsam: M20 Engineering Review'ün 3 Major bulgusunu (zayıf secret fallback'leri, global
> exception handling eksikliği, Render migration güvenliği) dar ve odaklı şekilde kapatmak.
> Yeni feature yok, mimari yeniden tasarım yok.

## P0-A — Production Secret Guard

**Bulgu (doğrulandı):** `Admin:SharedSecret` ve `ApiKeys:Pepper` için hardcoded placeholder
fallback'ler vardı, Production'da bunların kullanılmasını engelleyen hiçbir kontrol yoktu.
`ApiKeys` config bölümü `appsettings.json`'da hiç yoktu; pepper fallback'i 3 ayrı yerde
(`ApiKeyAuthMiddleware`, `IntegrationsController` x2) bağımsız olarak tekrarlanmıştı.

**Live tespit:** Render production deploy'unda `ApiKeys__Pepper` hiç set edilmemişti — gerçek
production, placeholder pepper ile çalışıyordu.

**Fix:**
- `ApiKeyAuthMiddleware.LocalDevPepperDefault` (public const) — pepper fallback'i tek bir yere
  merkezi hale getirildi, `IntegrationsController`'ın iki call site'ı buna referans veriyor.
- `appsettings.json`'a açık bir boş `ApiKeys:Pepper` eklendi (Ai:AnthropicApiKey ile aynı desen).
- `ProductionSecretValidator` (saf, host'suz, `FI.Api/Security/`) — Production'da her iki
  secret'ı da (boş/whitespace/placeholder) kontrol eder, yalnızca sorunlu config KEY'lerini
  döner, hiçbir zaman değer loglamaz.
- `Program.cs`'te `--migrate` bloğundan SONRA (migration'ı hiç etkilemez), `app.Environment.IsProduction()`
  true ise ve sorun varsa `InvalidOperationException` fırlatılır — host `app.Run()`'a hiç ulaşmaz.

**API Key Pepper Migration (M20.1 onay kısıtı #1):**
- Gerçek production etkisi belirlendi: Render'da **tam olarak 1 entegrasyon** vardı
  ("Render Deploy Verification", `owner: deploy-verify`) — bu, Render deploy görevi sırasında
  benim oluşturduğum bir smoke-test artifact'ıydı, gerçek bir design-partner/müşteri kullanımı
  YOKTU (Render API'den doğrudan sorgulanarak doğrulandı).
- **Seçilen strateji: A (rotate)** — B (dual-pepper grace period) gerçek bir müşteri etkisi
  olmadığı için gereksiz karmaşıklık olurdu.
- Yeni, güçlü bir pepper üretildi ve Render'a set edildi (`ApiKeys__Pepper`).
- Placeholder pepper'ın kabul edilme noktası: **bu deploy'un canlıya alındığı an** — P0-A'nın
  guard'ı sayesinde bundan sonra Production'ın placeholder pepper'la HİÇ başlayamaması yapısal
  olarak garanti; ayrıca bir "grace period"a gerek yok çünkü etkilenen tek key zaten gerçek bir
  bağımlı olmayan bir test artifact'ıydı.
- Doğrulama: pepper değişikliğinden sonra eski ham API key otomatik olarak geçersiz hale gelir
  (hash artık yeni pepper'la eşleşmiyor) — bu davranış Render'da canlı gözlemlendi/dokümante
  edildi (bkz. aşağıdaki "Live Deploy Verification").

**Tests:** `ProductionSecretValidatorTests` (8 saf unit test, tam matris) + `ProductionStartupTests`
(2 minimal host-seviyeli test: placeholder → fail-fast, geçerli değer → başarılı boot).

## P0-B — Global Exception Handling / ProblemDetails

**Bulgu (doğrulandı):** `FI.Api`'de `UseExceptionHandler`/`IExceptionHandler`/`AddProblemDetails`
hiç yoktu. `IntegrationsController.ParseCriticality`'nin `Enum.Parse` hatası (`ArgumentException`)
yakalanmıyordu.

**Fix (M20.1 onay kısıtı #2'ye tam uyumlu — DAR eşleme):**
- `GlobalExceptionHandler` (`IExceptionHandler`) — yalnızca:
  - `ArgumentException` (ama `ArgumentNullException` HARİÇ) → 400 (gerçek client girdi hatası).
  - `ArgumentNullException` → 500 (programlama hatası olarak kabul edilir, 400 arkasına
    gizlenmez).
  - Her şey diğer → 500, sanitize edilmiş genel mesaj.
- **`InvalidOperationException` GLOBAL olarak 409'a ÇEVRİLMEDİ** — `IncidentsController.Resolve`'daki
  mevcut, dokunulmamış yerel `catch (InvalidOperationException) → Conflict` aynen korundu; buraya
  kadar ulaşan (yakalanmamış) herhangi bir `InvalidOperationException` artık sanitize 500 döner,
  yanlışlıkla client-conflict gibi etiketlenmez.
- Middleware sırası: `CorrelationIdMiddleware` → `UseExceptionHandler` → `UseHttpsRedirection` →
  ... → rate limiting/auth/routing/endpoint (hepsini sarar).
- Development davranışı bilinçli olarak Production'la AYNI (sanitize edilmiş genel mesaj) —
  karmaşıklık eklememek için tek bir tutarlı gövde şekli tercih edildi, bu M20.1 kapsamında
  belgelenmiş bir tasarım kararıdır.

**Tests:** `GlobalExceptionHandlingTests` (gerçek HTTP: geçersiz `businessCriticality` → 400
ProblemDetails + traceId, bilinmeyen route → düz 404 etkilenmez) + `GlobalExceptionHandlerUnitTests`
(3 doğrudan unit test: bilinmeyen exception → sanitize 500 + hassas mesaj sızmıyor,
`ArgumentNullException` → 500, düz `ArgumentException` → 400).

## P0-C — Migration / Deployment Safety

**Karar: Kategori C** (dokümante edilmiş manuel süreç, minimize edilmiş maruziyet) — Free tier'da
gerçekçi olarak mevcut TEK güvenli seçenek. Kategori A (`preDeployCommand`) yalnızca paid tier'da
var (Render dokümantasyonundan doğrulandı, uydurulmadı). `docs/DEPLOYMENT_RENDER.md` tam bir
runbook'a (ön-doğrulama → erişim aç → migrate → doğrulama → erişim kapat → rollback) dönüştürüldü,
ve **M20.1 onay kısıtı #4'e göre** üç aşama açıkça ayrıştırıldı:
- Design-Partner Demo (sentetik veri): bugünkü süreç geçici olarak kabul edilebilir.
- Private Pilot (gerçek müşteri verisi): paid tier veya başka güvenli mekanizma ZORUNLU.
- First Paying Customer: mevcut süreç KABUL EDİLEMEZ.

Süreç genel olarak "pilot-ready" ilan EDİLMEDİ.

## Regression & Verification Sonuçları

- Domain: **164/164** ✅ (değişmedi, dokunulmadı).
- Integration: **22 sınıf, tamamı yeşil** (18 mevcut + 4 yeni: `ProductionSecretValidatorTests`,
  `ProductionStartupTests`, `GlobalExceptionHandlingTests`, `GlobalExceptionHandlerUnitTests`).
  Çalışma sırasında Docker Desktop bir kez çöktü/durdu (ortamsal, kodla ilgisiz) — etkilenen 7
  sınıf Docker yeniden başlatıldıktan sonra tekrar çalıştırıldı, hepsi geçti.
- **Migration-check eşdeğeri**: sıfır bir Postgres'e (`postgres:16-alpine`, Testcontainers dışı,
  doğrudan Docker) `dotnet ef database update` ile tüm migration seti başarıyla uygulandı.
- **Docker build + Compose fresh startup**: `docker compose down -v && up --build` temiz —
  `fi-migrate` başarıyla exit oldu, `fi-app` ve `fi-postgres` healthy.
- **Golden Incident (canlı, lokal Docker Compose'da uçtan uca)**:
  - 43 event / 12 operasyon / 7 müşteri, `OperationCoverage=Complete`, `CustomerCoverage=Complete` ✅
  - ConfigChange + PreviousEvent evidence toplandı ✅
  - AI key yokken `NeedsHumanReview`'a düştü (tasarlandığı gibi, çökmedi) ✅
  - Resolve: 200, `ResolvedAt`/`ResolvedBy`/`ResolutionNote` doğru ✅
  - Eşleşen yeni bir hata → otomatik Reopen: `Status=Reopened`, `ReopenCount=1`, `resolution=null` ✅
- **GitHub Actions CI**: aşağıda ayrı raporlanıyor (push sonrası).

## Bilinen Sınırlamalar (M20.1 sonrası hâlâ geçerli)

- Development ve Production, exception handler'da AYNI (sanitize) gövdeyi döner — daha ayrıntılı
  bir Development-only hata görünümü bilinçli olarak eklenmedi (kapsam dışı, gerekirse ayrı bir
  küçük iş).
- Render Free tier'ın migration süreci hâlâ manuel (yukarıda belgelendi) — kalıcı çözüm Starter
  plan yükseltmesi.
- Diğer tüm M20 bulguları `docs/reviews/TECHNICAL_DEBT_REGISTER.md`'ye zamanlamalı olarak
  aktarıldı, bu görevde ELE ALINMADI.
