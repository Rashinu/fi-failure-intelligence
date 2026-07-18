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

**Henüz YOK:** `CONFIG_CHANGE` evidence kaynağı (audit log altyapısı gerektiriyor), PII/secret
redaction pipeline'ı (event/response şu an ham JSON olarak saklanıyor — Bölüm 33.3), gerçek
şema validasyonu/timeout/network hatası tespiti (connector'larla gelecek), parse-fail
durumunda 1 kez retry (Bölüm 26.2 — şu an doğrudan NEEDS_HUMAN_REVIEW), golden dataset/eval
harness (Bölüm 26.4), Npgsql-özel trace span'ları, gerçek Mock Stripe/GitHub/SES connector'ları
(Bölüm 34-37 — genel ingestion API zaten bunları kabul edebiliyor, özel connector kodu yok),
Seq/OTLP collector entegrasyonu (şu an yalnızca konsol exporter).

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

## Sonraki Adımlar (Post-M6)

14 günlük planın çekirdek zinciri (event → classify → fingerprint → incident → evidence →
AI analiz → observability) artık uçtan uca çalışıyor. Kalan, kasıtlı olarak ertelenmiş işler:
- Mock Stripe/GitHub/SES-SendGrid connector'ları + demo senaryosu (Bölüm 34-37, 42)
- Golden dataset (20 senaryo) + eval harness (Bölüm 26.4)
- PII/secret redaction pipeline'ı (Bölüm 33.3)
- `CONFIG_CHANGE` evidence kaynağı (audit log altyapısı)
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
