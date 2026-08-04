# FI — Engineering Due Diligence Review

> Rol: Principal Software Architect / Technical Due Diligence Reviewer. Bu doküman yalnızca
> inceleme — hiçbir production kodu değiştirilmedi, refactor edilmedi. Bulgular gerçek dosyalar
> okunarak, gerçek migration/test/CI geçmişi incelenerek toplandı; varsayımla doldurulmuş hiçbir
> madde yok — emin olunamayan yerler açıkça "doğrulanmadı" olarak işaretlendi.

---

## 1. Architecture — 7/10

**Katmanlar:** `FI.Domain` (saf) → `FI.Application` (DTO'lar) → `FI.Infrastructure` (EF Core,
connector'lar, AI client) → `FI.Api` (controller'lar, Razor Pages). Bağımlılık yönü genel olarak
doğru: `FI.Domain`'in hiçbir dış paket referansı yok, `FI.Infrastructure` `FI.Domain`'e bağımlı,
`FI.Api` her ikisine de bağımlı.

**Sorunlar:**
- **`FI.Application` neredeyse boş bir katman.** İçinde yalnızca DTO record'ları var
  (`IntegrationDtos.cs`, `IncidentDtos.cs`, `PromptVersionDtos.cs`, `IngestionDtos.cs`) — gerçek
  bir "use case" / application service / command handler yok. Tüm orkestrasyon (DB sorgusu +
  domain metodu çağrısı + audit log + response mapping) doğrudan `FI.Api` controller'larında
  yaşıyor. Bu, Clean Architecture'ın "Application katmanı use case'leri temsil eder" ilkesini
  zayıflatıyor — Controller'lar hem API hem application-orchestration görevini üstleniyor.
- **Controller'lar `FiDbContext`'i doğrudan enjekte ediyor** (repository/interface soyutlaması
  yok). `FI.Api`, `FI.Infrastructure`'ın somut bir tipine (EF Core `DbContext`) doğrudan bağımlı.
  Modern .NET'te yaygın kabul gören pragmatik bir kısayol, ama katı Clean Architecture'da bu bir
  ihlal — `FI.Api`'nin yalnızca `FI.Application`'daki soyutlamalara bağımlı olması beklenir.
- **Domain saflığı iyi korunmuş**: `FI.Domain`'de EF Core, HTTP, JSON gibi hiçbir altyapı
  referansı yok (`EventClassifier`, `Incident`, `IntegrationEvent`, `AiAnalysisValidator` saf
  C#/iş kuralı). Bu, projenin en güçlü mimari yanı.
- **`DomainEvent.cs`** (`FI.Domain/Shared/DomainEvent.cs`) repoda **tek başına var, hiçbir yerde
  raise edilmiyor veya dispatch edilmiyor** (grep ile doğrulandı — bu dosya dışında hiçbir
  referans yok). Kullanılmayan, yarım bırakılmış bir soyutlama — ölü kod.

---

## 2. Domain Model — 7/10

**Aggregate'ler:** `Incident` (en zengin aggregate — `Resolve`, `Reopen`, `ResetAsNewOccurrence`,
`IsWithinReopenCooldown`, concurrency token ile optimistic locking), `Integration` (API key/webhook
secret issuance + rotation lifecycle), `IntegrationEvent`, `PromptVersion` (bootstrap-vs-promotion
ayrımı `CreateActive`/`CreateDraft` ile açıkça modellenmiş).

**İyi yanlar:**
- `Incident.Resolve()` gibi metodlar gerçek invariant koruyor (`IsActive` değilse
  `InvalidOperationException` — anemic model değil, davranış entity içinde).
- `EventClassifier`, `FingerprintCalculator`, `SeverityCalculator` tamamen deterministik, saf
  static fonksiyonlar — yan etkisiz, test edilmesi kolay, mimarinin "deterministik kod karar
  verir, AI yalnızca yorumlar" ilkesiyle tutarlı.
- `AiAnalysisValidator.CheckGrounding` domain katmanında yaşıyor (AI çıktısını doğrulama mantığı
  altyapıya sızmamış) — iyi bir sınır kararı.

**Sorunlar:**
- **Value Object neredeyse yok.** `EventCategory`, `IncidentSeverity`, `BusinessCriticality` gibi
  kavramlar zengin VO'lar yerine enum olarak modellenmiş. Kabul edilebilir bir basitlik ama
  örneğin `Fingerprint` (şu an düz `string`) bir VO olsaydı (eşitlik, format invariant'ı ile)
  daha güçlü olurdu.
- **`ResolutionSource` (kategorik: `HUMAN_MANUAL|AUTO_SILENCE|AI_APPROVED`) düz `string`, enum
  değil** — tip güvenliği yok, yanlış bir string sessizce kabul edilebilir (invariant kod
  incelemesiyle korunuyor, derleyiciyle değil).
- **Ölü domain-event soyutlaması** (yukarıda, Architecture bölümünde not edildi).
- Duplicated logic riski: `SeverityBadgeClass`/`StatusBadgeClass` gibi UI-tarafı switch
  mantıkları hem `Index.cshtml` hem `Detail.cshtml`'de ayrı ayrı tekrarlanmış (domain sorunu
  değil ama sunum katmanında bir DRY ihlali).

---

## 3. Application Layer — 5/10

- CQRS yok, MediatR yok, pipeline/behavior yok — her şey controller action'ı içinde imperatif
  olarak yazılmış. Küçük/orta ölçekli bir sistem için fazla mühendislik olmazdı ama şu an
  "Application katmanı" adı verilen şey fiilen yok.
- Validation: `[ApiController]`'ın otomatik model-state doğrulaması dışında (DTO'larda
  `[Required]` gibi attribute'lar sınırlı görünüyor — FluentValidation veya benzeri yok),
  iş kuralı doğrulamaları (örn. `ParseCriticality`'nin geçersiz string'de fırlattığı
  `ArgumentException`) controller içinde try/catch olmadan direkt fırlıyor — 400 yerine 500
  dönme riski (bkz. API bölümü).
- Transaction sınırları: `SaveChangesAsync` her yerde tek çağrı olarak kullanılıyor (EF Core'un
  kendi implicit transaction'ı) — birden fazla aggregate'i aynı anda değiştiren akışlarda (örn.
  `IncidentsController.Resolve` → incident + audit log tek `SaveChangesAsync`) bu doğru ve
  yeterli; ayrı bir explicit transaction yönetimi gerekmiyor, bu açıdan sorun yok.
- Background job'lar (`ClassifyJobHandler`, `EvidenceCollectorJobHandler`, `AiAnalysisJobHandler`,
  `OutboxDispatcher`) net sorumluluk ayrımıyla yazılmış, iyi bir zincir oluşturuyor.

---

## 4. Infrastructure — 8/10

- **EF Core + Migrations**: ~20 migration, tamamı incremental ve isimlendirmesi anlamlı
  (`AddIncidentEvidence`, `AddAiAnalysisPipeline`, `AddM19OperationIdentityAndResolution` vb.).
  CI'da ayrı bir `migration-check` job'u sıfır bir Postgres'e tüm migration'ları uyguluyor —
  gerçek bir production-readiness sinyali.
- **Outbox pattern**: `OutboxDispatcher.DispatchPendingAsync` Hangfire recurring job olarak
  çalışıyor, pending mesajları okuyup ilgili job handler'a `Enqueue` ediyor.
  **Önemli tespit**: dispatcher kodunun kendisinde satır bazlı kilitleme (`FOR UPDATE SKIP LOCKED`
  vb.) YOK — güvenlik tamamen Hangfire'ın recurring job scheduler'ının kendi distributed lock'una
  dayanıyor (`RecurringJob.AddOrUpdate` ile kayıtlı, aynı recurring job ID'sini cluster genelinde
  aynı anda yalnızca bir node'un tetiklediği garanti ediliyor). Bu bugün doğru ve güvenli, ama
  **güvenlik invariant'ı kodun kendisinde değil, Hangfire kayıt/konfigürasyonunda yaşıyor** —
  biri ileride `DispatchPendingAsync`'i farklı bir tetikleyiciyle (örn. doğrudan bir
  `IHostedService` loop'u) çağırırsa bu güvenlik sessizce kaybolur. Kod seviyesinde
  defense-in-depth yok.
- **Connector'lar** (`StripeConnector`, `GitHubDeploymentConnector`, `EmailDeliveryConnectorBase`):
  imza doğrulama + normalize etme net ayrılmış, `IIntegrationConnector`/`IDeploymentConnector`
  arayüzleriyle genişletilebilir.
- **Webhook secret şifreleme**: ASP.NET Core Data Protection ile, key ring `FiDbContext` üzerinden
  Postgres'te kalıcı — restart/multi-replica'da kaybolmuyor, doğru tasarım.
- **Secrets**: API key'ler HMAC-SHA256 + pepper ile hash'leniyor, ham key hiç saklanmıyor —
  doğru pratik. Pepper değeri `appsettings`'te yoksa `"local-dev-pepper-change-me"` fallback'i var
  — Admin__SharedSecret ile aynı desen (prod'da override edilmesi *gerekiyor* ama kod bunu
  *zorunlu kılmıyor*; yanlışlıkla default ile prod'a çıkma riski var, sessiz bir başarısızlık
  modu).
- **Caching**: repo genelinde herhangi bir cache katmanı (in-memory, Redis) yok — şu an
  gerekmiyor (ölçek küçük), ama not edilmeli.

---

## 5. API — 6/10

- **Versioning**: gerçek bir API versioning stratejisi (content negotiation, `Asp.Versioning`
  paketi, deprecation header'ları) yok — `api/v1/...` yalnızca route'a gömülü bir string. Bugün
  bir sorun değil (tek istemci: kendi Razor UI'ı + entegrasyon webhook'ları), ama ikinci bir
  gerçek API versiyonu gerektiğinde acı verecek bir kısayol.
- **DTO tutarlılığı**: genel olarak iyi (`record` tipler, immutable, `sealed`). `ResolveIncidentRequest`
  gibi nullable-optional alanlar tutarlı bir "hepsi opsiyonel" deseni izliyor.
- **Error handling tutarsız**: `IntegrationsController.ParseCriticality`'nin attı
  `ArgumentException` (geçersiz `businessCriticality` string'i) hiçbir yerde yakalanmıyor —
  global bir exception-handling middleware/`ProblemDetails` filtresi YOK, bu da geçersiz bir
  enum string'i gönderen bir istemcinin 400 yerine ham bir 500 stack trace'i almasına yol açar.
  Aynı desen `IncidentsController.Resolve`'da yalnızca `InvalidOperationException`'ı `Conflict`
  (409) olarak özel olarak yakalıyor — bu iyi ama tek noktasal, sistematik değil.
- **Status code kullanımı** genel olarak doğru (`201 Created` + `Location`, `404`, `409`, `401`),
  ama bu tek-tek controller'larda elle yazılmış, ortak bir `ProblemDetails`/exception-filter
  standardı yok.
- **Idempotency**: `IngestionIdempotencyKey` entity'si var (event ingestion için) — iyi bir
  tasarım kararı. Webhook tarafında (`WebhooksController`) ayrı bir idempotency-key kontrolü
  doğrulanmadı (yalnızca imza + timestamp-tolerance kontrolü görüldü) — **Stripe'ın kendi
  `provider_event_id`'sinin ikinci kez gönderilmesi durumunda dedupe olup olmadığı bu incelemede
  doğrulanamadı**, ayrı bir kontrol gerektirir.
- **Idempotency / replay**: webhook imza doğrulaması 5 dakikalık bir zaman toleransı uyguluyor
  (Stripe'ın kendi önerdiği desenle tutarlı) — timestamp dışı kalan istekler reddediliyor. Ama
  tolerans penceresi İÇİNDE yakalanan geçerli bir isteğin ikinci kez "replay" edilmesini
  (aynı imzayla tekrar POST) engelleyen ayrı bir nonce/görülmüş-mesaj takibi yok — düşük şiddetli
  ama gerçek bir artık risk.

---

## 6. UI — 5/10

M20 UX incelemesinde (`docs/product/M20_DESIGN_PARTNER_READINESS.md`) zaten detaylıca ele alındı,
buraya mühendislik açısından özetleniyor:
- Razor Pages ile ince bir sunum katmanı, ayrı bir frontend build'i/deploy'u gerektirmiyor —
  operasyonel olarak basit, bu bir artı.
- Dil tutarlılığı (TR/EN karışık metin) yakın zamanda düzeltildi (bu oturumdaki önceki iş).
- Business Impact açıklama satırı, Fingerprint'in "developer detail" olarak katlanır hale
  getirilmesi de düzeltildi.
- **Kalan gerçek eksik**: Incident listesinde (Index.cshtml) operation/customer sütunu yok
  (kasıtlı, N+1 riskinden kaçınmak için) — dashboard'da liste seviyesinde iş etkisi görünmüyor,
  yalnızca detay sayfasında.
- Support-engineer deneyimi: etkilenen müşteri **sayısı** var ama **kimliği/listesi** yok — bir
  support mühendisi "kime ulaşmam lazım" sorusunu hâlâ FI dışında cevaplamak zorunda.

---

## 7. Testing — 7/10

- Domain: **164 test** (`FI.Domain.Tests`), Integration: **96 test / 18 sınıf**
  (`FI.Integration.Tests`, Testcontainers tabanlı gerçek Postgres). CI, her Testcontainers-ağırlıklı
  sınıfı ayrı bir process'te çalıştırıyor (bilinen bağlantı-kararsızlığı sınırlamasını
  belgelenmiş şekilde bypass ediyor).
- **Golden Dataset eval**: 20 senaryo, 7 boyutlu `RubricScorer` (CategoryEcho, RootCauseAccuracy,
  Grounding, Actionability, ConfidenceCalibration, NeedsHumanReviewAccuracy, FormatCompliance) —
  AI çıktı kalitesini gerçek bir rubric'e karşı ölçen, nadir görülen olgun bir pratik.
- **Migration regression**: CI'da ayrı bir job sıfırdan migration uyguluyor.
- **Golden Incident**: uçtan uca (seed script → API → classify → incident → resolve) gerçek
  Docker Compose'da ve gerçek Render deploy'unda canlı doğrulandı (bu konuşma geçmişinde).

**Eksikler:**
- **Kod coverage yüzdesi hiçbir yerde raporlanmıyor/izlenmiyor** — CI, `XPlat Code Coverage`
  toplıyor (`.xml` artifact) ama bir eşik (örn. %80) zorunlu kılınmıyor, coverage trend'i takip
  edilmiyor. Testlerin "ne kadarını" kapsadığı yalnızca dosya/sınıf sayımıyla tahmin edilebiliyor,
  gerçek satır/dal coverage'ı bilinmiyor.
- Contradiction detection, spelled-out sayıların grounding kontrolünü atlatması gibi bilinen
  sınırlamalar (M19 dokümantasyonunda dürüstçe belgelenmiş) için henüz test yok — ama bunlar
  bilinçli olarak ertelenmiş, gizlenmiş değil.
- Load/performance testi yok (bkz. Bölüm 9).

---

## 8. Security — 6/10

- **Authentication**: control-plane (`AdminBasicAuthMiddleware`) paylaşılan-sır HTTP Basic — tek
  kullanıcı kimliği yok, kim yaptı bilgisi audit log'da yalnızca elle girilen bir string
  (`ResolvedByInput`) olarak tutuluyor, gerçek bir kimlik doğrulaması değil. Ingestion
  (`ApiKeyAuthMiddleware`) daha güçlü: HMAC+pepper hash, per-integration key, kullanım takibi.
- **Authorization**: rol/izin ayrımı yok (RBAC yok) — ADR-009'un bilinçli MVP sınırı olarak
  belgelenmiş, sürpriz değil.
- **Secrets**: webhook secret'ları Data Protection ile şifreli saklanıyor (iyi); API key hash'i
  + pepper (iyi); ama hem `Admin__SharedSecret` hem `ApiKeys:Pepper` için **kod içinde sabit,
  zayıf local-dev fallback değerleri var** ve bu fallback'lerin prod'da kullanılmasını
  **engelleyen hiçbir runtime kontrolü yok** (örn. `ASPNETCORE_ENVIRONMENT=Production`'da bu
  değer hâlâ default ise başlatmayı reddetme) — sessiz bir yanlış yapılandırma riski.
- **PII**: `AffectedCustomerRef` düz string olarak saklanıyor (muhtemelen provider'ın kendi
  müşteri ID'si, ama email gibi PII de olabilir sağlayıcıya göre) — `PayloadRedactor` var (iyi
  sinyal) ama `AffectedCustomerRef`'in kendisinin redaksiyon kapsamında olup olmadığı bu
  incelemede doğrulanmadı.
- **Injection**: EF Core LINQ kullanımı yaygın, ham SQL neredeyse yok — SQL injection riski düşük.
- **Replay**: yukarıda (API bölümü) not edildi — timestamp tolerance var, nonce-tabanlı tam
  replay koruması yok.
- **Logging**: Serilog + structured JSON — API key/webhook secret'larının log'lara sızıp
  sızmadığı doğrulanmadı (muhtemelen sızmıyor, çünkü ham key hiç DB'ye yazılmıyor, ama
  request/response logging middleware'inin header'ları maskeleyip maskelemediği kontrol
  edilmedi).

---

## 9. Performance — 6/10

- **N+1 riski**: `IncidentsController.GetById` ve `Detail.cshtml.cs` impact hesaplaması tek bir
  gruplu sorguda yapılıyor (`GroupBy(e => 1)...Select(...)`) — iyi bir optimizasyon, N+1 değil.
- **Index kararı** (M19'da belgelenmiş): `OperationRef` için yeni bir composite index eklenmedi,
  mevcut `IncidentId` index'i yeniden kullanıldı — küçük/orta ölçek için makul, ama incident
  başına event sayısı büyürse (binler) bu karar yeniden değerlendirilmeli.
- **`AsNoTracking()`** read-only sorgularda tutarlı şekilde kullanılıyor — iyi pratik.
- **Concurrency**: `Incident` üzerinde Postgres `xmin` tabanlı optimistic concurrency +
  `ClassifyJobHandler`'da 5 deneme retry loop'u — gerçek çakışan-yazma senaryosunda doğru
  davranış.
- **Scalability**: OutboxDispatcher `BatchSize = 50` sabit — event hacmi arttığında bu sabit
  değerin bir darboğaz oluşturup oluşturmayacağı (Hangfire recurring job aralığına bağlı,
  saniyeler mertebesinde çalışıyor) yük testi olmadan bilinmiyor.
- **Yük/performans testi hiç yok** — hiçbir yerde bir benchmark, load test (k6, NBomber vb.)
  bulunamadı. Bugünkü ölçek (demo/design-partner) için sorun değil, ama gerçek bir müşteri
  hacmine geçmeden önce bilinmeyen bir alan.

---

## 10. Production Readiness — 7/10

- **Observability**: OpenTelemetry (tracing + metrics), Serilog structured logging, correlation
  ID middleware'i — sağlam bir temel. OTLP exporter opsiyonel/yapılandırılabilir.
- **Health checks**: `/health/live` (her zaman healthy) + `/health/ready` (Postgres bağlantısı
  dahil) ayrımı doğru yapılmış.
- **Deployment**: Render'a canlı deploy edildi ve bu konuşmada uçtan uca doğrulandı (health
  check, auth gate, webhook→incident pipeline). Migration ayrı bir "migrator modu" olarak
  tasarlanmış (`--migrate` argümanı) — çoklu-replika race condition'ını (Hangfire şema kurulumu
  23505 hatası) önlemek için bilinçli bir mimari karar, gerçek bir production-olgunluğu sinyali.
- **Recovery**: `Incident.Reopen` + cooldown mekanizması, optimistic concurrency retry — hataya
  dayanıklılık düşünülmüş.
- **Operasyonel risk (bu oturumda ortaya çıkan, gerçek)**: Render Free plan'da
  `preDeployCommand` desteklenmiyor — her yeni migration, Postgres'in IP allow-list'inin geçici
  olarak `0.0.0.0/0`'a açılıp elle migration çalıştırılıp tekrar kapatılmasını gerektiriyor. Bu,
  **kod/mimari değil ama gerçek, tekrarlayan bir operasyonel güvenlik penceresi** —
  `docs/DEPLOYMENT_RENDER.md`'de belgelendi.
- **Free-tier Postgres 30 gün sonra siliniyor** (2026-09-01) — gerçek bir veri kaybı riski,
  belgelendi ama henüz çözülmedi (kullanıcı bilinçli olarak erteledi).
- **Configuration**: ortam bazlı `appsettings.{Environment}.json` deseni var ama
  `appsettings.Production.json` YOK — production'a özgü hiçbir override dosyası yok, her şey
  env var'lara dayanıyor (bu bir sorun değil, ama "Production'da farklı davranması gereken"
  hiçbir ayar şu an yok, bu da henüz test edilmemiş bir varsayım).

---

## Technical Debt Sınıflandırması

**Critical**
- Yok. (Bu, ciddi bir bulgu yokluğu değil — repo bu ölçekte "her şeyin çöktüğü" bir critical
  bulguya sahip değil; en ciddi bulgular Major seviyesinde.)

**Major**
- `Admin__SharedSecret` / `ApiKeys:Pepper` zayıf local-dev fallback'lerinin prod'da kullanılmasını
  engelleyen bir runtime guard'ı yok (sessiz yanlış yapılandırma riski).
- Global bir exception-handling/`ProblemDetails` middleware'i yok — beklenmeyen
  `ArgumentException` gibi hatalar 500 + stack trace olarak dışarı sızabilir.
- `OutboxDispatcher`'ın concurrency güvenliği kodun kendisinde değil, Hangfire kayıt şeklinde
  yaşıyor — dokümante edilmemiş bir kırılganlık.
- Render free-tier migration süreci (tekrarlayan IP-allowlist aç/kapa) — operasyonel risk.

**Minor**
- `FI.Application` katmanının fiilen boş olması (yalnızca DTO'lar).
- API versioning gerçek bir strateji değil, yalnızca route string'i.
- Webhook replay koruması yalnızca timestamp-tolerance, nonce/dedupe yok.
- `ResolutionSource`'un enum değil düz string olması.
- Kod coverage hiçbir yerde ölçülmüyor/eşiklenmiyor.

**Nice to Have**
- Ölü `DomainEvent.cs` soyutlamasının kaldırılması ya da gerçekten kullanılması.
- Badge-class switch mantığının (`Index.cshtml`/`Detail.cshtml`) tek bir yere taşınması.
- Load/performance test paketi.

---

## Top 10 Engineering Risk

1. Prod'da zayıf default secret'ların (Admin/Pepper) sessizce kullanılabilir olması.
2. Global exception handling eksikliği — beklenmeyen 500'ler.
3. Render free-tier'da tekrarlayan, elle yapılan IP-allowlist açma penceresi.
4. Free-tier Postgres'in 2026-09-01'de silinme riski (henüz Starter'a geçilmedi).
5. `OutboxDispatcher`'ın concurrency güvenliğinin Hangfire konfigürasyonuna gizlice bağımlı olması.
6. Webhook replay koruması yalnızca zaman toleransına dayanıyor (nonce yok).
7. Yük/performans testi hiç yapılmamış olması — gerçek müşteri hacmi bilinmiyor.
8. `FI.Application` katmanının boşluğu — büyüdükçe controller'ların "god class"a dönüşme riski.
9. Kod coverage'ın ölçülmemesi — testlerin gerçek kapsamı bilinmiyor.
10. AdminBasicAuthMiddleware'in kimliksiz paylaşılan-sır modeli — birden fazla operatör olduğunda
    "kim ne yaptı" hesap verebilirliği zayıf.

---

## Technical Roadmap

**En yüksek öncelik**
- Global exception-handling middleware / `ProblemDetails` standardizasyonu.
- Prod'da zayıf default secret kullanımını engelleyen bir startup guard'ı (fail-fast).
- Render'ı Starter plana yükseltip `preDeployCommand`'ı devreye almak (operasyonel riski
  kalıcı olarak kapatır).

**Düşük öncelik**
- API versioning stratejisinin resmileştirilmesi (gerçek ikinci bir tüketici çıkana kadar
  ertelenebilir).
- Value Object'lerin zenginleştirilmesi (`Fingerprint` vb.) — bugünkü ölçekte davranışsal bir
  fark yaratmıyor.

**Asla yapılmamalı (şu an için)**
- CQRS/MediatR'ın bu ölçekte eklenmesi — mevcut karmaşıklık bunu haklı çıkarmıyor, gereksiz
  soyutlama maliyeti getirir.
- RBAC/çoklu-kullanıcı auth sistemi — gerçek bir müşteri sinyali olmadan (M20 görüşmeleri
  sonrası karar verilmeli).
- Kendi repository/unit-of-work soyutlama katmanının EF Core'un üzerine eklenmesi — `DbContext`
  zaten bu rolü oynuyor, ek bir katman yalnızca dolaylılık ekler.

---

## Final Scores

| Kategori | Skor |
|---|---|
| Architecture | 7/10 |
| Domain | 7/10 |
| Infrastructure | 8/10 |
| API | 6/10 |
| UI | 5/10 |
| Testing | 7/10 |
| Security | 6/10 |
| Performance | 6/10 |
| Production Readiness | 7/10 |
| **Overall Engineering Score** | **6.5/10** |

---

## Final Decision

### **B — Needs Minor Engineering Work**

**Gerekçe:** Repo'da hiçbir Critical bulgu yok; mimari temel (domain saflığı, deterministik
sınıflandırma, outbox pattern, migration disiplini, gözlemlenebilirlik) sağlam ve tutarlı bir
şekilde uygulanmış. Ancak birkaç gerçek Major bulgu (zayıf default secret'ların prod'da
engellenmemesi, global exception handling eksikliği, Render'daki tekrarlayan operasyonel
güvenlik penceresi) "Engineering Ready" (A) demeyi haklı çıkarmıyor — bunlar küçük, dar
kapsamlı, ve mimariye dokunmadan çözülebilir düzeltmeler, bu yüzden "Needs Significant
Engineering Work" (C) da değil. Yukarıdaki "en yüksek öncelik" listesindeki üç madde
kapatıldığında durum A'ya döner.
