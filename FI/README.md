# FI — AI Integration Failure Intelligence

Bir SaaS entegrasyonu bozulduğunda neyin bozulduğunu, neden bozulmuş olabileceğini ve hangi
kullanıcıların/işlemlerin etkilendiğini evidence-backed AI analiziyle gösteren sistem.

Mimari kaynak: [`docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md`](../docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md)
Karar dokümanı: [`docs/ARCHITECTURE_REVIEW.md`](../docs/ARCHITECTURE_REVIEW.md)

## Durum

**M1 — Solution Skeleton tamamlandı.** Integration/ApiKey CRUD ve altyapı iskeleti (bkz. mimari
doküman Bölüm 50).

**M2 — Ingestion tamamlandı.** Eklenenler:
- `CorrelationIdMiddleware` — `X-Correlation-Id` üretir/yayar/echo eder.
- `ApiKeyAuthMiddleware` — yalnızca `/api/v1/events` ve `/api/v1/deployments` için zorunlu,
  HMAC-SHA256+pepper ile doğrulama.
- `POST /api/v1/events` — statusCode (100-599) ve occurredAt (gelecekte olamaz) validasyonu,
  413/422 hataları, iki katmanlı idempotency (`Idempotency-Key` header + content-hash fallback),
  raw event + outbox kaydı tek transaction'da.
- `POST /api/v1/deployments` — `changedConfig` sözleşme gereği yalnızca `{key, changed}` taşır,
  değer asla kabul edilmez.

**M3 — Classification + Fingerprinting + Incident tamamlandı.** Eklenenler:
- `EventClassifier` — Bölüm 21'deki 11 kategorilik deterministik rule engine (saf, framework'ten
  bağımsız fonksiyon).
- `FingerprintCalculator` — `SHA256(integrationId|category|errorSignature)`, kategoriye özgü
  errorSignature türetimi.
- `SeverityCalculator` — pencere bazlı (10/15/30dk) deterministik severity hesaplama.
- `Incident` entity — Open/RecordNewEvent/Reopen/ResetAsNewOccurrence durum geçişleri (bkz.
  ADR-014: `uq_incidents_open_fingerprint` kısıtı nedeniyle "cooldown sonrası yeni incident"
  senaryosu, aynı satırı sıfırlayarak pratik olarak çözülür).
- Hangfire + PostgreSQL storage (Redis yok, ADR-004 ile tutarlı) — `OutboxDispatcher` (5sn'de
  bir recurring job) bekleyen outbox kayıtlarını `ClassifyJobHandler`'a enqueue eder.
- `GET /api/v1/incidents`, `GET /api/v1/incidents/{id}` — temel liste/detay (timeline/evidence/
  latestAnalysis M4-M5'te eklenecek).
- Hangfire dashboard `/hangfire`'da ama varsayılan olarak yalnızca local istekleri kabul ediyor
  (Docker port-forward üzerinden dıştan erişim 401 döner — kasıtlı, henüz dashboard auth'u yok).

**M4 — Evidence Collection tamamlandı.** Eklenenler:
- `IncidentEvidence` entity + `EvidenceCollectorJobHandler` — Bölüm 23'teki 4 kaynaktan
  **3'ünü** dolduruyor: `DEPLOYMENT` (-2sa/+0 pencere), `PREVIOUS_EVENT` (son 24sa, max 5),
  `HISTORICAL_INCIDENT` (son 90 gün, aynı kategori, max 5). `CONFIG_CHANGE` kasıtlı olarak
  atlanıyor — config-değişiklik audit günlüğü (API key rotasyonu, webhook URL/secret geçmişi)
  henüz sistemde yok; hiçbir evidence uydurulmuyor, gerçek veriye sahip olmadığımız kaynak
  boş bırakılıyor (Bölüm 23'ün "kaynak boşsa listede yer almaz" kuralıyla tutarlı).
  `summary` alanı deterministik template ile üretiliyor (AI değil).
- Önceliklendirme: `HISTORICAL_INCIDENT` > `DEPLOYMENT` > `PREVIOUS_EVENT`, toplam
  `maxEvidenceItems=10`.
- `Incident.StartInvestigating()` — evidence toplama tamamlanınca incident `Open`/`Reopened`'dan
  `Investigating`'e geçer. Zaten aktif bir incident'a bağlanan tekrar event'ler için evidence
  yeniden toplanmaz (yalnızca yeni/reopen/reset-as-new-occurrence durumlarında tetiklenir).
- `ClassifyJobHandler` artık uygun durumlarda `EvidenceCollectorJob` outbox mesajı yazıyor;
  `OutboxDispatcher` bu mesaj tipini de tüketiyor (iki aşamalı zincir: Classify → Evidence,
  her aşama 5sn'lik dispatcher döngüsünden geçtiği için toplam gecikme ~10-15sn olabilir).
- `GET /api/v1/incidents/{id}` yanıtına `evidence` listesi eklendi.

**M5 — AI Analysis Pipeline tamamlandı.** Eklenenler:
- `IAiAnalysisClient` + `AnthropicMessagesClient` — Semantic Kernel'in stabil bir Anthropic
  konnektörü olmadığı için doğrudan Anthropic Messages API'sine HTTP ile bağlanan, tek adaptör
  arkasında provider-agnostic bir istemci (bkz. ADR-013, kod içi not).
- `AiAnalysisValidator` — Bölüm 26.2'deki zincir: parse → şema/echo → confidence eşiği →
  grounding (evidence-dışı iddia) kontrolü. Yalnızca **parse hatası** ve **şema/echo
  uyumsuzluğu** analiz kaydını tamamen reddeder; düşük confidence ve grounding sorunları kaydı
  oluşturur ama `needsHumanReview`'ı zorlar (dokümanın "zorla true" ifadesiyle birebir).
  Modellerin (özellikle Claude) "yalnızca JSON döndür" talimatına rağmen çoğunlukla yanıtı
  ` ```json ` code fence'ine sarması, parse öncesi otomatik temizlenerek ele alınıyor.
- `AiAnalysisJobHandler` — evidence boşsa AI çağrısı hiç yapılmaz; severity=Critical'da
  Sonnet'e, aksi halde Haiku'ya yönlendirir; her çağrı (başarısız dahil) `AiAnalysisLog`'a,
  yalnızca geçerli çıktı `AiIncidentAnalysis`'e (business-facing, versiyonlu) yazılır.
- `PromptVersion` — startup'ta tek bir ACTIVE prompt (`fi-root-cause-v1`) seed edilir.
- Üçüncü outbox aşaması: Evidence → `AiAnalysisJob`.
- `GET /api/v1/incidents/{id}` yanıtına `latestAnalysis` eklendi.
- Testlerde gerçek API çağrısı yerine `FakeAiAnalysisClient` (test double) kullanılıyor.

**M6 — Observability (Serilog + OpenTelemetry) tamamlandı.** Eklenenler:
- Serilog JSON structured logging (`CompactJsonFormatter`, konsola), `Enrich.FromLogContext()`.
- `CorrelationIdMiddleware` artık `Serilog.Context.LogContext.PushProperty("CorrelationId", ...)`
  ile alt loglara correlation id'yi yayıyor, ayrıca aktif OpenTelemetry `Activity`'ye
  `fi.correlation_id` tag'i ekliyor (Bölüm 30'daki span-attribute kuralına uygun).
- OpenTelemetry tracing: ASP.NET Core + HttpClient instrumentation (Anthropic çağrıları dahil),
  konsol exporter. `Npgsql` instrumentation, EF Core'un kullandığı Npgsql sürümüyle (8.0.x)
  `Npgsql.OpenTelemetry` paketinin çektiği sürüm (10.0.x) arasındaki potansiyel çakışma riski
  nedeniyle bilinçli olarak **dışarıda bırakıldı** — ASP.NET Core+HttpClient span'ları zaten
  ingestion→AI-çağrısı zincirinin kritik kısmını kapsıyor.
- `app.UseSerilogRequestLogging()` — her HTTP isteği için yapılandırılmış özet log satırı.

**M7 — Mock Connector'lar (Stripe/GitHub/SES/SendGrid) tamamlandı.** Eklenenler:
- `IIntegrationConnector`/`IDeploymentConnector` (`FI.Domain.Connectors`) — Bölüm 34'teki arayüz,
  **kasıtlı sadeleştirme:** dokümandaki `Classify` metodu eklenmedi; sınıflandırma zaten
  `EventClassifier`'da tek gerçek kaynak (Bölüm 21). Connector'lar bunun yerine ham webhook
  gövdesini `EventClassifier`'ın zaten anladığı request/response JSON şekline (`headers.
  X-Signature-Valid`, `error.code`) normalize eder — iki ayrı kural motorunun birbirinden
  sapması riski böylece ortadan kalkıyor.
- `StripeConnector` — `Stripe-Signature: t=...,v1=...` (HMAC-SHA256, 5dk replay toleransı,
  `CryptographicOperations.FixedTimeEquals`), `client_secret`/`api_key` redaction.
- `GitHubDeploymentConnector` — `X-Hub-Signature-256: sha256=...`, `deployment_status`
  webhook'undan `commit`/`environment`/`changedConfig` çıkarımı.
- `SesConnector`/`SendGridConnector` (ortak `EmailDeliveryConnectorBase`) — e-posta teslim
  olayları (bounce/dropped/complaint/delivered) gerçek HTTP çağrısı olmadığından, Bölüm 21'in
  statusCode-tabanlı kurallarıyla uyumlu çalışması için **sentetik ama tutarlı** bir statusCode
  eşlemesi kullanılır (bounce→502, dropped→503, complaint→400, delivered→200); `DELIVERY_FAILURE`
  alt-kategorisi Bölüm 37'nin notuyla tutarlı olarak `error.code`'da taşınır, core taksonomiye
  eklenmez.
- `ConnectorRegistry` — ProviderKey'e göre basit dictionary lookup (Bölüm 34, "generic repository
  soyutlaması eklenmez" kararına uygun).
- `Integration.WebhookSecret` — API key'den ayrı saklanır ama **hash değil düz metin** (kasıtlı
  sapma: HMAC doğrulaması sırrın kendisiyle hesaplama gerektirir, tek yönlü hash'ten
  doğrulanamaz; prod'da KMS/Data Protection ile şifreleme takip konusu). Entegrasyon
  oluşturulurken API key ile birlikte otomatik üretilip `CreateIntegrationResponse.
  WebhookSecret` içinde bir kez döndürülür.
- `POST /api/v1/webhooks/{provider}/{integrationId}/events` ve
  `POST /api/v1/webhooks/{provider}/{integrationId}/deployments` — `X-Api-Key` middleware'inin
  kapsamı dışında (webhook kimlik doğrulaması imza tabanlı); imza doğrulaması başarısız olsa
  bile event **reddedilmez**, `isSignatureVerified=false` ile kaydedilir (bu bilginin kendisi
  bir incident sinyali, Bölüm 34 madde 6).
- Demo senaryosu doğrulandı (Bölüm 35, "Stripe Webhook Auth Patlaması"): imzalı 6 adet
  `charge.failed`/401 webhook'u → tek `AuthenticationError` incident'ına toplanıyor
  (`StripeWebhookIngestionTests`).

**M8 — Golden Dataset + Eval Harness tamamlandı.** Eklenenler:
- `RubricScorer` (`FI.Domain.AiAnalysis.Eval`) — Bölüm 26.4'teki 7 boyutta (category echo,
  root cause doğruluğu, grounding, actionability, confidence kalibrasyonu, needsHumanReview
  doğruluğu, format uyumu) 0-1 arası saf, framework'ten bağımsız puanlama. `AiAnalysisValidator`
  ile karıştırılmaz: validator sistemin **güvenli davranışını** garanti eder (parse/echo/
  confidence/grounding), rubric ise modelin/promptun **kalitesini** ölçer.
- `GoldenDataset` (`FI.Infrastructure.Eval`) — 11 kanonik kategorinin (Bölüm 21) her biri en az
  bir kez, artı yetersiz/çelişkili/gürültülü evidence, reopen, stale historical evidence ve
  **prompt injection adversarial testi** dahil 20 sabit senaryo.
- `EvalHarness` — `AiAnalysisJobHandler` ile birebir aynı evidence-only input contract'ını
  (Bölüm 25.1) üretir ama DB'ye dokunmaz; herhangi bir `IAiAnalysisClient` (gerçek
  `AnthropicMessagesClient` veya test double) ile çalışır.
- `EvalReport.Passed` — Bölüm 26.4 eşiği (toplam ortalama ≥ 0.85 VE hiçbir category-echo/format
  uyumu FAIL yok) tek bir yerde uygulanır. Regresyon karşılaştırması (önceki `ACTIVE`'e göre
  boyut bazlı >%10 düşüş, Bölüm 26.3) bu MVP'de kapsam dışı — `prompt_versions` A/B akışı henüz
  otomatikleştirilmedi.
- Testler `ScriptedAiAnalysisClient` (gerçek model DEĞİL, "ideal davranış" scripted double)
  kullanır — amaç Claude'u değerlendirmek değil, harness'in puanlama/eşik mantığının doğru
  çalıştığını CI'da ağdan/API maliyetinden bağımsız kanıtlamaktır. Ayrıca harness'in gerçekten
  ayırt edici olduğu kanıtlandı: enjeksiyona boyun eğen (yanlış kategori üreten) bir davranış
  eşiği düşürüyor ve `Passed=false` üretiyor.
- **Gerçek model kalitesi değerlendirmesi (Open Decision #1, Bölüm 49) henüz otomatik değil:**
  aynı `EvalHarness`, gerçek `AnthropicMessagesClient` ile manuel çalıştırılabilir (bir sonraki
  adım — bkz. Sonraki Adımlar).

**M9 — PII/Secret Redaction Pipeline tamamlandı.** Eklenenler:
- `PayloadRedactor` (`FI.Domain.Redaction`) — Bölüm 33.3'teki "EvidenceMaskingPolicy domain
  katmanında merkezi uygulanır" kararının karşılığı: saf, framework'ten bağımsız, idempotent,
  tek gerçek redaction motoru. Field-based masking (öncelikli — `authorization`, `x-api-key`,
  `x-auth-token`, `apiKey`, `secret`, `client_secret`, `password`, `token`) + pattern-based
  masking (yedek — Bearer/JWT token, e-posta, Luhn-doğrulamalı kredi kartı, telefon).
- **Aşama A (ingestion sırasında):** `EventsController.Ingest`, `request`/`response` JSON'unu
  DB'ye yazmadan önce `PayloadRedactor.RedactJson` ile geçiriyor — `RequestRedacted`/
  `ResponseRedacted` kolonları artık isimleriyle tutarlı şekilde gerçekten redakte edilmiş veri
  taşıyor. `ClassifyJobHandler`'ın okuduğu yapısal alanlar (`headers.X-Signature-Valid`,
  `error.code`, `path`) hassas alan adı listesinde olmadığı için etkilenmiyor — regresyon yok
  (tüm classification/ingestion testleri yeşil).
- **Aşama B (AI'a gönderilmeden hemen önce):** `AiAnalysisJobHandler` (ve tutarlılık için
  `EvalHarness`), evidence özetlerini `PayloadRedactor.RedactText` ile ikinci, daha katı bir
  redaction pass'inden geçiriyor — evidence zaten deterministik template'lerden türediği için
  (Bölüm 23) pratikte no-op ama savunma-derinliği olarak zorunlu tutuldu.
- Connector'ların kendi `Redact` implementasyonları (`StripeConnector`, `SesConnector`,
  `SendGridConnector`) artık bu tek motora delege ediyor — iki ayrı maskeleme mantığının
  birbirinden sapması riski ortadan kalktı.
- Yeni testler: 19 `PayloadRedactorTests` (field/pattern/Luhn/idempotency) + bir uçtan uca
  entegrasyon testi (`Ingest_WithSensitiveFieldsInPayload_PersistsRedactedNotRaw`) — Authorization
  header, API key, e-posta ve kredi kartı içeren bir payload'ın veritabanında **asla ham**
  saklanmadığını doğruluyor.

**M10 — CONFIG_CHANGE Evidence Kaynağı tamamlandı.** Eklenenler:
- `AuditLog` (`FI.Domain.Audit`) — Bölüm 16.11/33.6'daki append-only audit kaydı
  (`actor_type/actor_id/action/entity_type/entity_id/correlation_id/changes/created_at`).
  Serilog'dan (yüksek hacim, teknik) bilinçli olarak ayrı; iş/uyumluluk amaçlı ve şimdi ayrıca
  CONFIG_CHANGE evidence kaynağının **tek veri kaynağı**.
- `POST /api/v1/integrations/{id}/api-key/rotate` ve `.../webhook-secret/rotate` — Bölüm 35'in
  flagship demo senaryosunun ("API key rotasyonu sonrası 401 patlaması") daha önce eksik olan
  parçası: artık gerçekten bir rotasyon eylemi var ve `AuditLog`'a yazıyor.
  `IntegrationsController.Update` de `endpointUrl` gerçekten değiştiğinde audit log yazıyor
  (no-op update'ler sinyal üretmiyor).
- `Integration.RotateApiKey` — eski aktif key'leri revoke edip yenisini issue eder. Bölüm 33.4'ün
  24 saatlik grace period'u kasıtlı olarak sadeleştirildi (anında rotasyon) — grace period,
  zamanlanmış bir revoke job'u gerektirir, post-MVP takip konusu.
- `EvidenceCollectorJobHandler.CollectConfigChangeEvidenceAsync` — `AuditLog` kayıtlarını
  `incident.FirstSeen` referanslı **-6 saat/+0** penceresinde sorgular, deterministik template
  ile özet üretir. Önceliklendirme Bölüm 23'e göre güncellendi:
  **CONFIG_CHANGE > HISTORICAL_INCIDENT > DEPLOYMENT > PREVIOUS_EVENT**.
- **Mühendislik notu (EF Core gotcha):** `RotateApiKey` ilk implementasyonu
  `DbUpdateConcurrencyException` fırlatıyordu — EF Core, önceden atanmış bir Guid PK'sı olan yeni
  bir çocuğu, ZATEN TAKİP EDİLEN (Unchanged) bir ebeveynin koleksiyonuna eklerken bunu otomatik
  `Added` işaretlemiyor (yalnızca ebeveyn de `Added` ise cascade eder) — yeni `ApiKey`'in
  `_db.ApiKeys.Add(...)` ile açıkça işaretlenmesi gerekti.
- 8 yeni domain unit testi (`AuditLogTests`, `Integration.RotateApiKey`/`IssueWebhookSecret`) +
  entegrasyon testleri (rotasyon endpoint'leri, audit log yazımı, CONFIG_CHANGE evidence üretimi).

**M11 — Prompt Version A/B ve Regresyon Otomasyonu tamamlandı.** Eklenenler:
- `PromptVersion.CreateDraft`/`RecordEvalResult`/`Activate`/`Deprecate` — DRAFT → ACTIVE →
  DEPRECATED yaşam döngüsü artık gerçek durum geçişlerine sahip (önceden yalnızca M5'te seed
  edilen tek bir ACTIVE versiyon vardı, A/B akışı yoktu).
- `EvalReport.PerDimensionAverages` — Bölüm 26.4'ün 7 rubric boyutunun her biri için ayrı ortalama
  (regresyon karşılaştırmasının girdisi).
- `PromptPromotionGate` (`FI.Domain.AiAnalysis.Eval`) — Bölüm 26.3/26.4'teki iki kuralı tek yerde
  uygular: (1) aday, golden dataset eşiğini (≥0.85 ortalama, kritik FAIL yok) geçmeli, (2) mevcut
  ACTIVE'e göre **hiçbir boyutta >%10 düşüş olmamalı**. Saf, framework'ten bağımsız.
- `PromptVersionPromotionService` (Infrastructure) — bir DRAFT'ı golden dataset'e karşı çalıştırır
  (`EvalHarness` + `GoldenDataset`, M8'de kurulan altyapı yeniden kullanıldı); mevcut ACTIVE hiç
  değerlendirilmediyse (ör. seed edilen ilk versiyon) onu da değerlendirip sonucu cache'ler —
  sonraki promote çağrıları bu baseline'ı yeniden hesaplamadan kullanır.
- `POST /api/v1/prompt-versions` (DRAFT oluştur), `GET /api/v1/prompt-versions[/{id}]`,
  `POST /api/v1/prompt-versions/{id}/promote` — onaylanırsa yeni versiyon ACTIVE, eskisi
  DEPRECATED olur; onaylanmazsa hiçbir durum değişmez ama değerlendirme sonucu (skor + red
  gerekçeleri) döner ve DRAFT'a cache'lenir.
- **Kasıtlı sadeleştirme:** dokümandaki "son N=200 canlı analizde parse-fail/evidence-dışı iddia
  oranı kötüleşmeden" ek koşulu uygulanmadı — bu, aday prompt'tan bağımsız genel sistem sağlığı
  sinyali ve ayrı bir takip konusu.
- 11 yeni domain testi (`PromptPromotionGateTests`, `PromptVersionTests`) + 5 entegrasyon testi
  (`PromptVersionPromotionTests`) — CRUD, 404/409 durumları, gerçek golden dataset koşusu +
  sonucun kalıcı hale gelmesi, ve ikinci bir promote çağrısının cache'lenen baseline'ı kullanıp
  sahte bir regresyon üretmediği doğrulandı.

**M12 — CI/CD (GitHub Actions) tamamlandı.** Eklenenler:
- `.github/workflows/fi-ci.yml` — Bölüm 39'daki sıralamayı (en ucuz→en pahalı) uygular:
  **Build → Test (unit önce, sonra Testcontainers-ağırlıklı integration/e2e) → Migration Check →
  Docker Build**. Tetikleyiciler: `push`/`pull_request` (master/main, yalnızca `FI/**` veya
  workflow dosyası değiştiğinde) ve `workflow_dispatch`.
- **Migration Check** — geçici bir `postgres:16-alpine` service container'ına karşı
  `dotnet ef database update` çalıştırır; `InitialCreate`'ten en son migration'a kadar sıfır bir
  veritabanında manuel müdahalesiz uygulanabildiğini doğrular (bu oturumda yerel olarak da
  doğrulandı — tüm 8 migration sıfırdan başarıyla uygulandı).
- **Docker Build** — multi-stage `docker/Dockerfile`'ı build eder, imaj boyutunu/süresini loglar
  (yerel doğrulama: ~91MB, ~54sn); gerçek bir container registry'ye push henüz yapılandırılmadı
  (kapsam dışı — bkz. Sonraki Adımlar).
- `.config/dotnet-tools.json` — `dotnet-ef` artık global kurulum varsayımı yerine repo'ya
  sabitlenmiş bir sürümle (`dotnet tool restore`) geliyor; hem yerel geliştirme hem CI aynı
  sürümü kullanır.
- Coverage raporları (`coverlet`, cobertura formatı) her PR/push'ta artifact olarak yükleniyor.

**M13 — API Key Rotasyonunda Grace Period tamamlandı.** Eklenenler:
- `ApiKey.MarkRotated(rotatedAt)` — rotasyon anında eski key artık anında revoke edilmiyor,
  yalnızca `LastRotatedAt` işaretleniyor (alan M7'den beri vardı ama hiç kullanılmıyordu).
- `ApiKeyGracePeriodRevocationJobHandler` — saatte bir çalışan Hangfire recurring job,
  `LastRotatedAt`'i 24 saati geçmiş ama henüz revoke edilmemiş key'leri revoke eder (Bölüm 33.4).
- Davranış değişikliği: rotasyondan hemen sonra **hem eski hem yeni key çalışır** (grace period
  boyunca) — henüz güncellenmemiş istemcilerin kesintiye uğramaması için. Eski key yalnızca
  grace period job'u çalıştıktan sonra 401 döner.
- Testler güncellendi: `RotateApiKey_OldKeyStopsWorking_NewKeyWorks` →
  `RotateApiKey_OldKeyStillWorksDuringGracePeriod_NewKeyAlsoWorks` (artık eski key'in hâlâ
  çalıştığını doğruluyor) + yeni `ApiKeyGracePeriodRevocationJob_RevokesKeysPastGracePeriod...`
  testi (grace period'u geçmiş bir key'i simüle edip job'ın onu revoke ettiğini kanıtlıyor).

**Henüz YOK:** gerçek şema validasyonu/timeout/network hatası tespiti, parse-fail durumunda 1 kez
retry (Bölüm 26.2 — şu an doğrudan NEEDS_HUMAN_REVIEW), golden dataset'in gerçek Claude ile
çalıştırılması (şu an yalnızca scripted/fake double ile doğrulandı), promote akışının CI'da bir
zorunlu status check'e bağlanması (workflow var ama branch protection henüz yapılandırılmadı),
canlı analiz sağlık metriklerine dayalı ek promotion koşulu (Bölüm 26.3'ün N=200 kuralı), Docker
image'ının gerçek bir container registry'ye push edilmesi, Npgsql-özel trace span'ları, Seq/OTLP
collector entegrasyonu (şu an yalnızca konsol exporter).

**Doğrulama durumu:** Build 0 hata/0 uyarı. 79/79 domain unit testi (AI validator'ın parse/
echo/confidence/grounding senaryoları dahil) geçti. Entegrasyon testleri her sınıf **izole**
çalıştırıldığında güvenilir şekilde geçiyor (M1 3/3, M2 13/13, M3 18/18, M4 22/22, M5 5/5, M6
app-boot doğrulaması 3/3) — bu oturumda altı Testcontainers-ağırlıklı sınıfın *aynı process'te
art arda* çalıştırılması zaman zaman yerel Docker Desktop'ta bağlantı kararsızlığına yol açtı
(kod hatası değil, ortam sınırlaması). **Canlı doğrulama (gerçek Anthropic API key ile, M5'te):**
tam pipeline (ingest → classify → fingerprint → incident → evidence → gerçek Claude Haiku
analizi) uçtan uca çalıştı — model, tek kaynaklı evidence'a dayanarak grounded bir kök neden
üretti ve kendi belirsizliğini fark edip `needsHumanReview=true` işaretledi.

## Quick Start

Önkoşul: Docker Desktop çalışıyor olmalı.

```bash
cd FI
docker compose -f docker/docker-compose.yml up -d --build
```

`fi-postgres` sağlıklı olduğunda `fi-app` otomatik başlar ve container başlangıcında
EF Core migration'ları (`InitialCreate`) otomatik uygulanır.

Doğrulama:

```bash
# Liveness/readiness
curl http://localhost:8080/health/live
curl http://localhost:8080/health/ready

# Swagger UI (Development ortamında)
# http://localhost:8080/swagger

# Bir entegrasyon oluştur
curl -X POST http://localhost:8080/api/v1/integrations \
  -H "Content-Type: application/json" \
  -d '{"name":"Stripe Payments","provider":"stripe","environment":"production","owner":"backend-team","endpointUrl":"https://api.stripe.com","businessCriticality":"High"}'

# Dönen integrationId ile detay getir
curl http://localhost:8080/api/v1/integrations/{integrationId}
```

Durdurmak için:

```bash
docker compose -f docker/docker-compose.yml down
```

## Yerel Geliştirme (Docker olmadan)

```bash
dotnet tool restore   # dotnet-ef'i .config/dotnet-tools.json'daki sabitlenmiş sürümle kurar (bir kez)
dotnet build
dotnet test tests/FI.Domain.Tests/FI.Domain.Tests.csproj
dotnet test tests/FI.Integration.Tests/FI.Integration.Tests.csproj   # Docker gerektirir (Testcontainers)
```

## Proje Yapısı

```
FI/
├── src/
│   ├── FI.Domain/           Entity, value object, deterministik kurallar. Framework bağımlılığı yok.
│   ├── FI.Application/      Use case DTO'ları, interface'ler.
│   ├── FI.Infrastructure/   EF Core, PostgreSQL, migration'lar.
│   └── FI.Api/               Controller, middleware, composition root (Hangfire worker'ı da burada barındıracak).
├── tests/
│   ├── FI.Domain.Tests/      Unit testler (classifier/fingerprint/entity kuralları).
│   └── FI.Integration.Tests/ Testcontainers ile gerçek PostgreSQL'e karşı API testleri.
└── docker/                   Dockerfile + docker-compose.yml
```

Bağımlılık kuralı: `Domain` hiçbir şeye bağımlı değildir; `Application` yalnızca kendi
arayüzlerine bağımlıdır; `Infrastructure` bu arayüzleri implemente eder; `Api` composition root'tur.

## Sonraki Adımlar (Post-M13)

14 günlük planın çekirdek zinciri (event → classify → fingerprint → incident → evidence →
AI analiz → observability) artık uçtan uca çalışıyor; mock connector'lar (Bölüm 34-37), golden
dataset/eval harness (Bölüm 26.4), PII/secret redaction pipeline'ı (Bölüm 33.3), evidence
collector'ın 4 kaynağının tamamı (Bölüm 23), prompt version A/B/regresyon gate'i (Bölüm 26.3),
CI/CD pipeline'ı (Bölüm 39) ve API key rotasyon grace period'u (Bölüm 33.4) buna bağlandı. Kalan,
kasıtlı olarak ertelenmiş işler:
- Golden dataset'in gerçek Anthropic API'ye karşı manuel çalıştırılması (maliyet/latency ölçümü
  + prompt kalitesi hakkında gerçek sinyal, Bölüm 49 Open Decision #1) — `PromptVersionPromotionService`
  bunun için hazır, yalnızca `IAiAnalysisClient` gerçek `AnthropicMessagesClient`'a bağlanmalı
- GitHub'da branch protection kuralının Build/Test/Migration Check'i zorunlu status check yapması
  (workflow dosyası hazır, repo ayarı henüz yapılmadı)
- Docker image'ının gerçek bir container registry'ye (ör. GHCR) push edilmesi
- Canlı analiz sağlık metriklerine (son N=200) dayalı ek promotion koşulu (Bölüm 26.3)
- Seq/OTLP collector entegrasyonu (şu an yalnızca konsol exporter)

Bkz. `docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md` Bölüm 43 (Post-MVP Roadmap) ve Bölüm 49
(Open Decisions).

## AI Provider Yapılandırması (Anthropic)

Gerçek Claude çağrısı için API key **asla appsettings.json'a veya git'e yazılmamalı**.
Yerel geliştirmede .NET user-secrets kullanılır:

```bash
cd FI/src/FI.Api
dotnet user-secrets init   # zaten yapıldıysa atlanır
dotnet user-secrets set "Ai:AnthropicApiKey" "sk-ant-..."
```

Üretimde ortam değişkeni: `Ai__AnthropicApiKey`. Key ayarlı değilse `AiAnalysisJobHandler`
çağrıyı hiç yapmaz, incident'ı `NEEDS_HUMAN_REVIEW` yapar ve `AiAnalysisLog`'a nedeni yazar —
sistem key'siz ortamda da (ör. CI, Testcontainers) çökmeden çalışır.
