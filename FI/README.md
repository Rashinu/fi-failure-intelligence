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

**Henüz YOK (M5+):** AI analiz pipeline'ı (evidence-only prompt, structured output, confidence,
grounding validasyonu), `CONFIG_CHANGE` evidence kaynağı (audit log altyapısı gerektiriyor),
PII/secret redaction pipeline'ı (event/response şu an ham JSON olarak saklanıyor — Bölüm 33.3),
gerçek şema validasyonu/timeout/network hatası tespiti (connector'larla gelecek),
Serilog/OpenTelemetry (Bölüm 50 M6).

**Doğrulama durumu:** Build 0 hata/0 uyarı. 69/69 domain unit testi ve 22/22 Testcontainers
entegrasyon testi geçti. Canlı `docker compose` duman testi tamamlandı: gerçek Hangfire
recurring job (iki aşamalı outbox zinciri) bir deployment'ı doğru şekilde evidence olarak
topladı ve incident'ı `Investigating` durumuna geçirdi (uçtan uca, test içinde handler'ı
doğrudan çağırmadan).

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

## Sonraki Milestone

M5 — AI analiz pipeline'ı: `IAiAnalysisClient` interface (Semantic Kernel üzerinden Claude
Haiku/Sonnet), evidence-only structured output şeması, prompt versioning, parse/şema-echo/
confidence/grounding validasyon zinciri, golden dataset. Bkz. mimari doküman Bölüm 24
(AI Analysis Pipeline), Bölüm 25 (Structured Output Schema), Bölüm 26 (Prompt and Evaluation
Strategy), Bölüm 42 (14 Günlük Plan).
