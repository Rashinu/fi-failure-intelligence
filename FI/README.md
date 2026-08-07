# FI — AI Integration Failure Intelligence

Hookdeck/Svix size neyin patladığını gösterir, FI neden patladığını ve ne yapmanız gerektiğini
söyler — bir SaaS entegrasyonu (Stripe, GitHub, SES) bozulduğunda, hangi kullanıcıların/
işlemlerin etkilendiğini ve olası kök nedeni evidence-backed AI analiziyle ortaya çıkaran,
teslimat katmanının üstüne eklenen bir sistem (bkz. [`docs/positioning.md`](../docs/positioning.md)).

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

**M14 — Golden Dataset'in Gerçek Claude Haiku'ya Karşı Çalıştırılması (Bölüm 49 Open Decision #1)
tamamlandı.** `EvalHarness`, kullanıcının `dotnet user-secrets` ile ayarladığı gerçek
`Ai:AnthropicApiKey` kullanılarak `AnthropicMessagesClient` üzerinden 20 senaryonun tamamına
karşı çalıştırıldı (manuel, tek seferlik — CI'a otomatik bağlanmadı, çalıştırılan test dosyası
doğrulama sonrası kaldırıldı; bkz. M8'in "manuel/opt-in kalır" kararı).

**Gerçek sonuç (model: `claude-haiku-4-5`, mevcut `fi-root-cause-v1` prompt'u):**
- **Genel ortalama: 0.726 — eşiğin (0.85) altında, `Passed=false`.**
- Boyut bazlı: CategoryEcho 0.950, RootCauseAccuracy 0.950, Actionability 0.950,
  ConfidenceCalibration 0.933, FormatCompliance 0.950 — bunlar güçlü.
  **Grounding 0.100 ve NeedsHumanReviewAccuracy 0.250 — zayıf halkalar.**
- 1 kritik FAIL: `contradictory-evidence` senaryosu (çelişkili evidence karşısında model beklenen
  belirsizliği/needsHumanReview davranışını göstermedi).
- **Bu, sistemin kendi güvenlik mekanizmasının çalıştığının kanıtı:** `PromptPromotionGate`
  gerçek bir promote denemesinde bu prompt'u **haklı olarak reddederdi** — M11'de kurulan gate,
  tam da bunun için var.
- **Kök neden (yorum, kesin değil):** `Grounding` düşüklüğü muhtemelen `AiAnalysisValidator`'ın
  basit kelime-örtüşme temelli kontrolünün (Bölüm 26.2, kendi dokümantasyonunda "kesin değil"
  olarak işaretli) gerçek bir modelin doğal parafrazlamasını (evidence'taki sayıları/adları
  birebir tekrar etmeden yeniden ifade etmesi) yanlışlıkla "evidence-dışı iddia" olarak
  işaretlemesi. Bu, prompt'un "evidence'ı olabildiğince birebir tekrarla" talimatını
  güçlendirerek veya grounding kontrolünü gevşeterek iyileştirilebilir — ikisi de bu oturumun
  kapsamı dışında bırakıldı (bir sonraki adım).

**Henüz YOK:** gerçek şema validasyonu/timeout/network hatası tespiti, parse-fail durumunda 1 kez
retry (Bölüm 26.2 — şu an doğrudan NEEDS_HUMAN_REVIEW), `fi-root-cause-v1` prompt'unun (veya
grounding kontrolünün) golden dataset eşiğini geçecek şekilde iyileştirilmesi (M14'te ölçüldü,
şu an geçmiyor), promote akışının CI'da bir zorunlu status check'e bağlanması, canlı analiz
sağlık metriklerine dayalı ek promotion koşulu (Bölüm 26.3'ün N=200 kuralı), Docker image'ının
gerçek bir container registry'ye push edilmesi, Npgsql-özel trace span'ları, Seq/OTLP collector
entegrasyonu (şu an yalnızca konsol exporter).

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

**M15 — Gerçek Docker Compose Uçtan Uca Testi + Kritik Concurrency Düzeltmesi tamamlandı.**
Bu oturuma kadar tüm testler `WebApplicationFactory` (in-process test host) üzerinden
çalıştırılmıştı; job handler'lar da testlerde hep SIRAYLA, doğrudan çağrılıyordu. İlk kez
`docker compose up` ile gerçek container'lar halinde ayağa kaldırılıp gerçek HTTP istekleriyle
(curl, imzalı webhook, API key rotasyonu) uçtan uca test edildi.

**Bulunan ve düzeltilen kritik bug:** Hangfire varsayılan olarak 20 paralel worker ile
çalışıyor; aynı fingerprint'e ait event'ler gerçekten eşzamanlı geldiğinde (ör. bir entegrasyonun
attığı hata patlaması), birden fazla `ClassifyJob` aynı incident satırını aynı anda okuyup
güncelliyordu. Bu iki farklı hataya yol açtı:
- Birden fazla job aynı anda "incident yok" görüp aynı fingerprint için INSERT denemesi →
  Postgres UNIQUE ihlali (`23505`).
- Birden fazla job var olan incident'ı okuyup `EventCount++` yapması → son yazan kazanır, ara
  artışlar kaybolur ("lost update"). 8 event'lik gerçek bir paralel yük testinde `EventCount`
  8 yerine 4 çıktı.

**Düzeltme:** `Incident`, Postgres'in `xmin` sistem sütununu optimistic concurrency token
olarak kullanıyor (`IncidentConfiguration`); `ClassifyJobHandler` artık hem concurrency-token
çakışmasını (`DbUpdateConcurrencyException`) hem UNIQUE ihlalini (`23505`) yakalayıp tüm
sınıflandırma+upsert işlemini sıfırdan yeniden deniyor (`ChangeTracker.Clear()` + retry, en
fazla 5 deneme). Aynı 8 event'lik paralel yük testi tekrar çalıştırıldığında `EventCount=8`
doğru sonucunu verdi (20 retry olayı loglandı, hepsi başarıyla çözüldü).

**Ayrıca canlı olarak doğrulanan davranışlar:** webhook imza doğrulama, ingestion→classify→
incident zinciri, evidence yoksa "uydurma yok, direkt NEEDS_HUMAN_REVIEW" davranışı, API key
rotasyonu + audit log yazımı, ve **CONFIG_CHANGE evidence'ının gerçek bir rotasyon olayını
doğru zaman penceresinde yakalaması** ("API key rotated for integration '...' 0 minute(s)
before first failure").

**Önemli çıkarım:** Bu bug, `WebApplicationFactory`+sıralı-job-çağrısı temelli testlerin asla
yakalayamayacağı türden bir sınıf — gerçek eşzamanlı üretim trafiği simüle edilmeden ortaya
çıkmıyordu. Bu, "iyi test edilmiş" ile "üretim trafiğinde doğrulanmış" arasındaki farkı somut
şekilde gösteriyor.

**M16 — Production Readiness (Faz 2) tamamlandı.** CTO review'ün Faz 2 kalemlerinin tamamı,
gerçek bir Docker Compose ortamında (bir dış CI değil) uçtan uca doğrulanarak tamamlandı:

- **`Program.cs` bölündü** — servis kayıtları `FI.Api/Extensions/` altında 5 extension method'a
  ayrıldı (`AddFiPersistence`, `AddFiBackgroundJobs`, `AddFiConnectors`, `AddFiAiAnalysis`,
  `AddFiObservability`); `Program.cs` artık yalnızca bu method'ları çağıran bir composition root.
- **AI Resilience** — `AnthropicMessagesClient` artık .NET 8'in standart resilience handler'ı
  (Polly v8 tabanlı, `Microsoft.Extensions.Http.Resilience`) ile sarmalı: retry (üstel geri
  çekilme + jitter, `Retry-After` header'ını otomatik dikkate alır), circuit breaker, deneme
  başına + toplam timeout. Önceden yalnızca çıplak `HttpClient.Timeout` vardı.
- **Webhook secret artık düz metin değil** — `Integration.WebhookSecret`, ASP.NET Core Data
  Protection ile şifrelenmiş olarak saklanıyor (`WebhookSecretProtector`); anahtar halkası
  `FiDbContext` üzerinden kalıcı (container restart/çoklu replica arasında paylaşılır, bkz.
  `DataProtectionKeys` tablosu). Gerçek container'da doğrulandı: DB'deki değer artık
  `CfDJ8...` formatında (Data Protection ciphertext), API yanıtında hâlâ ham secret dönüyor
  (caller'ın webhook'unu imzalaması için gerekli) ve imza doğrulaması uçtan uca çalışıyor.
- **Migration artık startup'ta değil, deploy-time'da** — `Program.cs` artık başlangıçta otomatik
  migration uygulamıyor (çoklu replica'da race condition riski taşıdığı için). Bunun yerine aynı
  image, `--migrate` argümanıyla yalnızca migration+seed yapıp çıkan ayrı bir "migrator" modu
  kazandı; `docker-compose.yml`'e bu modu çalıştıran tek seferlik bir `fi-migrate` servisi
  eklendi, `fi-app` yalnızca bu başarıyla bittikten sonra başlıyor
  (`depends_on: condition: service_completed_successfully`) — gerçek Docker Compose'ta
  doğrulandı.
- **OTLP exporter eklendi** — `Otel:OtlpEndpoint` yapılandırılmışsa (Seq, Grafana Tempo, Jaeger)
  trace'ler oraya da gönderiliyor; yapılandırılmamışsa (varsayılan, yerel geliştirme) yalnızca
  konsol exporter kullanılıyor, ek altyapı gerekmiyor.
- GitHub repo About açıklaması ve topics eklendi (`dotnet`, `postgresql`, `hangfire`,
  `anthropic`, `incident-management`).
- Test fixture'ı (`FiApiFactory`) migration artık otomatik olmadığı için güncellendi — testler
  gerçek deploy pipeline'ındaki ayrı migration adımını taklit ederek migration'ı açıkça çağırıyor.

**M17 — Product Proof tamamlandı.** CTO review'ün Faz 1 kalemi: sistemin ürettiği hiçbir veri
daha önce görsel olarak gösterilmiyordu (yalnızca Swagger/JSON API vardı). Eklenenler:

- **Incident Dashboard** (`/Incidents`) — severity/status filtreli liste, `GET /api/v1/incidents`
  ile aynı veriyi okur (aynı process içinde `FiDbContext` üzerinden, ayrı bir HTTP çağrısı yok).
- **Incident Detail** (`/Incidents/Detail/{id}`) — timeline (ilk hata → evidence toplama →
  AI analizi, kronolojik), evidence kartları (kaynak tipine göre), AI özeti + confidence bar +
  "insan incelemesi gerekiyor mu" rozeti, AI analizi henüz yoksa dürüst bir boş durum mesajı.
- **Deterministik "Suggested Action"** (`SuggestedActionCatalog`, `FI.Domain.Classification`) —
  11 kategorinin her biri için sabit, anında görünen bir öneri ("API key'in geçerliliğini
  kontrol edin" vb.); AI'nin serbest metin önerilerinden bağımsız, evidence toplanmadan/AI
  çağrısı yapılmadan bile hemen görünür. `IncidentListItemResponse`/`IncidentDetailResponse`'a
  `suggestedAction` alanı olarak da eklendi (API tüketicileri için).
- **Teknoloji:** Razor Pages, `FI.Api` içine gömülü (ayrı bir frontend projesi/deploy yok) —
  proje ortamındaki karar gereği.
- **`scripts/seed-demo-data.sh`** — gerçek imzalı webhook'larla 3 gerçekçi senaryo üretir
  (API key rotasyonu sonrası auth patlaması, rate limit, provider outage). Gerçek Docker Compose
  ortamında çalıştırılıp dashboard ve detail sayfalarının doğru render ettiği doğrulandı —
  CONFIG_CHANGE evidence'ı (rotasyondan), PreviousEvent evidence'ı ve timeline'ı dahil.

**M18 — Incident Intelligence tamamlandı.** CTO review'ün Faz 3 kalemi: "kaç müşteri etkilendi"
sorusuna cevap veren business-impact özeti.

- **`AffectedCustomerRef`** — `IntegrationEvent` ve `NormalizedEvent`'e opsiyonel, PII olmayan
  (opak referans) yeni bir alan eklendi. `StripeConnector`, mock payload'ın `data.object.customer`
  alanını çıkarır; boş string de "yok" sayılır (provider'ın hiç göndermediği durumla aynı, tekil
  müşteri sayısını yanlışlıkla şişirmemesi için). Genel ingestion endpoint'i (`EventsController`)
  için de opsiyonel `customerRef` alanı eklendi.
- **Business-impact özeti** — Incident Detail sayfasında ve `GET /api/v1/incidents/{id}`
  yanıtında yeni bir "İş Etkisi" bölümü: etkilenen istek sayısı, tekil etkilenen müşteri sayısı
  (provider bu veriyi taşımıyorsa dürüst bir "bilinmiyor" mesajı — 0 ile "veri yok" birbirinden
  ayrılır) ve okunabilir süre.
- **Eşzamanlılık bulgusu (gerçek Docker Compose'da bulundu):** Müşteri sayısı ilk denemede yanlış
  çıktı (2 yerine 4) — sebebi, `Incident.FirstSeen`'in her zaman kronolojik olarak en erken event
  olmaması (paralel Hangfire worker'ları aynı fingerprint için yarışırken, incident'ı "açan" event
  yarışı kazanan event'tir, en erken gönderilen değil — bkz. M15'teki eşzamanlılık notu).
  Sorgunun alt zaman sınırına 15 dakikalık bir güvenlik payı eklenerek düzeltildi ve gerçek
  Docker Compose ortamında (6 event / 4 müşteri senaryosu ve rate-limit/outage senaryolarının
  "bilinmiyor" göstermesi) sıfırdan doğrulandı.
- Migration (`AddAffectedCustomerRef`) fresh bir veritabanına karşı (`docker compose down -v` +
  `up --build`) uçtan uca doğrulandı.

**Due Diligence düzeltmeleri (D1, D7/D8) tamamlandı.** Harici bir due-diligence raporu, statik
kod okumasıyla (canlı çalıştırma yapılamadan) iki gerçek bulgu tespit etti; ikisi de bu ortamda
doğrulanıp düzeltildi:

- **D1 — Severity/iş-etkisi pencereleri güncel event'i dışlıyordu:** `ClassifyJobHandler`,
  `count10/15/30` sorgularını `evt.SetCategory(...)` yalnızca bellekte set edildikten ve
  `SaveChangesAsync`'ten ÖNCE çalıştırıyordu — yani sınıflandırılan event, DB'de hâlâ eski
  kategoriyle durduğundan kendi penceresinden dışlanıyordu (off-by-one). Yazılan bir entegrasyon
  testiyle canlı doğrulandı (5 `RateLimitError` event'i sonrası severity yanlışlıkla Low kalıyordu,
  Medium eşiği ≥5 iken). Düzeltme: güncel event, düştüğü her pencereye elle +1 ekleniyor artık.
- **D7/D8 — Control plane'de hiç authentication yoktu:** `IntegrationsController`,
  `PromptVersionsController`, `IncidentsController` (JSON API), `/Incidents` (Razor dashboard) ve
  `/hangfire` — hiçbiri kimlik doğrulaması gerektirmiyordu; ağ erişimi olan HERKES ham bir webhook
  secret'ı kendine rotate edebilir, tüm incident verisini görebilirdi. Minimal bir paylaşılan-sır
  HTTP Basic Auth kapısı (`AdminBasicAuthMiddleware`) eklendi. Düzeltme sırasında ayrı bir bulgu
  daha çıktı: Hangfire'ın kendi varsayılan "yalnızca localhost" filtresi, admin kimlik bilgisiyle
  bile Docker Compose port-forwarding üzerinden 401 döndürüyordu (D8) — bu filtre kaldırılıp
  erişim tek başına admin kapısına bağlandı. Tüm değişiklikler gerçek Docker Compose'da (kimliksiz
  401, doğru/yanlış kimlikle 200/401) ve 8 yeni `ControlPlaneAuthTests` testiyle doğrulandı.
  Ingestion endpoint'leri (`/api/v1/events`, `/api/v1/webhooks`, `/api/v1/deployments`) bu kapsamın
  dışında — zaten ayrı, machine-to-machine `ApiKeyAuthMiddleware` ile korunuyor.

Yerel demo için varsayılan sır `local-dev-admin-secret-change-me` (`docker-compose.yml`,
`Admin__SharedSecret`) — **herhangi bir paylaşılan/production ortamında değiştirilmelidir.**

**D4 (Outbox dead-letter görünürlüğü) düzeltildi.** `OutboxMessage.MarkFailed()` daha önce yalnızca
`Status`'u `Failed` yapıyordu — ne zaman/kaç kez/neden başarısız olduğuna dair hiçbir iz
bırakmıyordu, ve dispatcher'ın sorgu filtresi (`Status==Pending`) bu satırı bir daha asla
görmüyordu. `FailureCount`/`LastFailedAt`/`LastError` alanları eklendi, `GET
/api/v1/admin/outbox?status=Failed` (admin auth arkasında) ile gözlemlenebilir hale getirildi.
Fresh bir veritabanına karşı migration doğrulandı, 6 yeni test (2 domain + 2 integration + 2
auth-gate) eklendi.

**D5 bulgusu — raporla çelişiyor, kod incelemesiyle düzeltme gerekmediği tespit edildi.**
Rapor, `ClassifyJobHandler.ExecuteAsync`'in concurrency retry'lerini tükettikten sonra hiç
exception fırlatmadan sessizce döndüğünü iddia ediyordu. Kodun birebir okunmasıyla bu **doğru
değil**: `catch (DbUpdateException ex) when (attempt < MaxConcurrencyRetries && ...)` koşulu, tam
olarak son denemede (`attempt == MaxConcurrencyRetries`) `false` olacak şekilde yazılmış — yani
son denemede bir concurrency conflict oluşursa, C#'ın exception filter semantiği gereği bu catch
bloğu hiç eşleşmez ve exception olduğu gibi yukarı (Hangfire'a) fırlatılır. `for` döngüsünün
"sessizce sonlanıp başarılıymış gibi dönmesi" mümkün değil — her döngü adımı ya `return` ile ya da
yakalanmamış bir `throw` ile sonlanıyor. Bu yüzden D5 için herhangi bir kod değişikliği
yapılmadı.

**D9 (rate limiting) düzeltildi.** Kod tabanının hiçbir yerinde rate limiting yoktu — D7 ile
control plane authentication gerektirse bile, geçerli bir API key veya admin kimlik bilgisiyle
hacimli istek atan biri DB'yi veya (evidence varsa) faturalandırılan Anthropic API çağrılarını
kontrolsüz tüketebilirdi. `Microsoft.AspNetCore.RateLimiting` ile yalnızca `/api/v1/*` altındaki
rotalar için IP başına sabit-pencere limiti eklendi (`RateLimitingExtensions.AddFiRateLimiting`,
100 istek / 10 saniye, aşımda 429). Razor dashboard, Hangfire, health check'ler kapsam dışı.
Gerçek Docker Compose'da canlı doğrulandı: 101. istek 429 döndü, pencere 10 saniye sonra
sıfırlandı, `/health/live` hiç etkilenmedi. 2 yeni test eklendi.

**PB6 (UI'da tekrarlama görünürlüğü) tamamlandı.** `ReopenCount>0` daha önce yalnızca header'da
ham bir sayaç olarak görünüyordu; ayrıca hem Detail hem Index sayfasındaki `StatusBadgeClass`
switch'inde `"Reopened"` için bir case yoktu (nötr gri rozet gösteriyordu). Artık ayrı, amber
`fi-badge-reopened` rozeti ve Detail'de "bu ilk kez görülen yeni bir olay değil" diyen açık bir
banner var. Gerçek Docker Compose'da (DB'de reopen_count/status manuel güncellemesiyle
simüle edilerek) canlı doğrulandı.

**TD1 (severity-pencere sorgu birleştirme) ve TD2 (concurrency-retry metriği) tamamlandı.**
`ClassifyJobHandler`'daki 3 ayrı `count10/15/30` sorgusu, tek bir sorguda koşullu aggregation'a
(`COUNT(*) FILTER (WHERE ...)`, gerçek Postgres'e karşı üretilen SQL doğrudan incelenerek
doğrulandı) dönüştürüldü. Ayrıca eşzamanlılık-conflict retry'leri artık yalnızca log satırlarında
değil, OTel `FI.Api` meter'ına bağlı bir sayaçta da (`FiJobMetrics.ClassifyJobConcurrencyRetries`)
görünür.

**TD3 (açık IntegrationEvent↔Incident ilişkisi) tamamlandı.** `IntegrationEvent`'e artık
`ClassifyJobHandler`'ın sınıflandırma anında set ettiği gerçek bir `IncidentId` FK'sı var.
`IncidentsController`, `AiAnalysisJobHandler` ve `Detail.cshtml.cs`'deki üç bağımsız
`IntegrationId+Category-string+zaman-penceresi` sorgusu (D2'nin 15-dakikalık payı dahil) bu FK'ya
göre doğrudan filtreleme ile değiştirildi — hem üç yerde ayrı ayrı bakım riski hem de D2'nin
residual eksik-sayım riski ortadan kalktı. Fresh bir veritabanına karşı migration doğrulandı
(`incident_id` her event için doğru dolduğu DB'den doğrudan kontrol edilerek), 4 yeni test
eklendi.

**TD8 (Hangfire job sınırları arasında W3C trace-context yayılımı) tamamlandı.** `OutboxMessage`
artık oluşturulduğu andaki `Activity.Current?.Id`'yi otomatik yakalıyor; `ClassifyJobHandler`/
`EvidenceCollectorJobHandler`/`AiAnalysisJobHandler` bunu (`FiTelemetry.StartLinkedActivity`
üzerinden) kendi Activity'lerinin parent'ı olarak kullanıyor — daha önce hiç Activity üretmeyen
`AddSource("FI.Api")` kancası ilk kez gerçek span'lerle doluyor. Gerçek Docker Compose'da canlı
doğrulandı: konsol trace exporter'ında her `ClassifyJob` span'inin `ParentSpanId`/`TraceId`'sinin
orijinal HTTP isteğinin trace'iyle doğru eşleştiği doğrudan gözlemlendi. 4 yeni test eklendi.

**TD6 (çoklu-replica altında OutboxDispatcher güvenliği) canlı doğrulandı; testte bulunan ayrı
bir Hangfire şema yarışı düzeltildi.** İzole, geçici bir Docker Compose stack'inde gerçek 2
`fi-app` replikası aynı Postgres/Hangfire storage'ı paylaştı; 10 event'lik gerçek bir eşzamanlı
patlama tam olarak bir kez sınıflandırıldı, tek bir incident `event_count=10` ile oluştu, hiçbir
outbox mesajı tekrar-dispatch edilmedi — Hangfire'ın recurring-job distributed lock'u beklendiği
gibi çalışıyor; `OutboxDispatcher`'ın kendisi için kod değişikliği gerekmedi.

Bu test sırasında ayrı bir bulgu ortaya çıktı: 2 replika mutlak eşzamanlı (soğuk/ilk kez)
başladığında, Hangfire.PostgreSql'in kendi şema kurulumu (`CREATE SCHEMA "hangfire"`) iki replika
arasında yarışıyor ve kaybeden `23505` ile çöküyordu. **Düzeltildi:** `Program.cs`'in `--migrate`
modu artık `IGlobalConfiguration`'ı da resolve ediyor — bu, Hangfire'ın şema kurulumunu sunucuyu
hiç başlatmadan, `fi-migrate`'in tek/garantili-seri instance'ında tetikliyor. Aynı 2-replika
senaryosu sıfırdan yeniden çalıştırılarak doğrulandı: her iki replika da artık hiç çökmeden ayağa
kalkıyor. Normal tek-instance geliştirme stack'i ve demo seed script'i de bu değişiklikten sonra
yeniden doğrulandı.

**UI/UX gözden geçirmesi tamamlandı.** Nav bar önceden `Incidents | API | Jobs`'u eşit ağırlıkta
yan yana gösteriyordu — Swagger ve Hangfire dashboard'u (iç geliştirici araçları) ürünün kendisiyle
aynı sırada, sanki birer ürün özelliğiymiş gibi sunuluyordu. Bu, projeyi ilk açan birine "planlı
bir ürün" değil "bir geliştiricinin iç aracı" izlenimi veriyordu. Düzeltildi:
- Ana nav artık yalnızca `Incidents`'ı (gerçek ürünü) içeriyor; Swagger/Hangfire linkleri sayfanın
  altına, açıkça "Geliştirici" etiketli, görsel olarak geri planda bir footer'a taşındı.
- Dashboard'a, zaten yüklenmiş incident listesinden türetilen (yeni bir API çağrısı gerektirmeyen)
  bir özet istatistik satırı eklendi (açık incident / critical / inceleme bekleyen / toplam event).
- Tipografi (Inter + JetBrains Mono), renk/yüzey katmanlaması, kart gölgeleri, badge kontrastı ve
  responsive davranış (760px altında stat grid 2 sütuna, detay header'ı dikey) yeniden tasarlandı.
- Gerçek Docker Compose'da canlı doğrulandı (ekran görüntüleriyle) — dashboard, detail sayfası ve
  Swagger/Hangfire linkleri sorunsuz çalışıyor.

## Sonraki Adımlar (Post-M18)

14 günlük planın çekirdek zinciri, CTO review'ün Faz 1 (Product Proof), Faz 2 (Production
Readiness) ve Faz 3'ün (Incident Intelligence) ilk kalemi tamamlandı (bkz.
`docs/CTO_REVIEW_ANALYSIS.md`). Kalan iş:

- **M19 — Close the Product Loop: tamamlandı ✅.** Business Operation Identity (`OperationRef`/
  `OperationType`/`BusinessRecordRef`), Incident Resolution lifecycle (`Incident.Resolve()`,
  reopen-within-cooldown korunarak) ve AI Grounding false-positive düzeltmesi kapatıldı. Detaylar:
  `docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md`.
- **M20 — Design Partner / Customer Validation:** Gerçek kullanıcılarla (entegrasyon geliştiricisi,
  support mühendisi, otomasyon danışmanı) M17 demo'sunu doğrulamak — "Bugün aynı problemi çözmek
  için hangi ekranlara bakıyorsunuz?" (Not: bu kalem daha önce "M19" olarak adlandırılmıştı; harici
  M19 mühendislik spesifikasyonuyla numaralandırma çakışmasını önlemek için M20 olarak yeniden
  adlandırıldı, bkz. `docs/CTO_REVIEW_ANALYSIS.md`.)
- Demo video/GIF kaydı (dashboard artık hazır).
- `fi-root-cause-v1` prompt'unun iyileştirilmesi — M14'te ölçülen skor (0.726) eşiği geçmiyor,
  en zayıf halkalar Grounding (0.100) ve NeedsHumanReviewAccuracy (0.250).
- Canlı analiz sağlık metriklerine (son N=200) dayalı ek promotion koşulu (Bölüm 26.3).

Bkz. `docs/CTO_REVIEW_ANALYSIS.md` (güncellenmiş, detaylı öncelik planı) ve
`docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md` Bölüm 43 (Post-MVP Roadmap) / Bölüm 49 (Open
Decisions).

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
