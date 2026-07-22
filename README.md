# FI — AI Integration Failure Intelligence

Bir SaaS entegrasyonu (Stripe, GitHub, SES/SendGrid vb.) bozulduğunda **neyin bozulduğunu, neden
bozulmuş olabileceğini ve hangi kullanıcıların/işlemlerin etkilendiğini** — evidence-backed AI
analiziyle, uydurma yapmadan — gösteren bir sistem.

> Uygulama kodu `FI/` klasöründedir. Bu dosya reponun geneline bakan bir giriş sayfasıdır;
> uygulamaya özgü ayrıntılar için [`FI/README.md`](FI/README.md)'ye, tam mimari karara
> [`docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md`](docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md)'ye bakın.

## Temel fikir

Deterministik kod her zaman **kesin olan** şeyi yapar: hata sınıflandırma, fingerprint hesaplama,
severity, incident açma/kapama. AI yalnızca **yorumlayıcı** bir rol oynar: yalnızca toplanan
evidence'a dayanarak kök neden anlatır, önerilen aksiyonları listeler, kendi belirsizliğini
raporlar. AI evidence dışında bir iddiada bulunursa (uydurursa) bu sistematik olarak yakalanır ve
incident insana devredilir (`NEEDS_HUMAN_REVIEW`) — hiçbir zaman sessizce yanlış bir hikaye
üretip geçmez.

## Mimari (özet)

```
Webhook/Event → [Deterministik Sınıflandırma] → [Fingerprint] → [Incident Aç/Güncelle]
                                                                        │
                                                          ┌─────────────┴─────────────┐
                                                          ▼                           ▼
                                                 [Evidence Toplama]          [Audit Log (config
                                                  (deploy, önceki                değişiklikleri)]
                                                  event, geçmiş incident)
                                                          │
                                                          ▼
                                          [AI Analiz — yalnızca evidence'a dayalı,
                                           parse/echo/confidence/grounding doğrulaması]
                                                          │
                                                          ▼
                                        AI_ANALYZED  veya  NEEDS_HUMAN_REVIEW
```

**Katmanlar** (`FI/src/`): `FI.Domain` (saf iş kuralları, framework bağımlılığı yok) →
`FI.Application` (DTO/interface) → `FI.Infrastructure` (EF Core/PostgreSQL, Hangfire, connector'lar,
Anthropic client) → `FI.Api` (composition root, controller'lar, middleware).

**Teknoloji:** .NET 8/10, PostgreSQL, Hangfire (arka plan job'ları), Anthropic Claude
(Haiku/Sonnet), Docker, GitHub Actions.

## Proje durumu

Çekirdek zincir (event → classify → fingerprint → incident → evidence → AI analiz →
observability) **uçtan uca çalışıyor ve gerçek bir Docker Compose ortamında (yalnızca izole
testlerle değil) doğrulandı.** 15 milestone'un tamamı tamamlandı; ayrıntılı changelog için
[`FI/README.md`](FI/README.md#durum)'deki "Durum" bölümüne bakın. Öne çıkanlar:

- Mock Stripe/GitHub/SES/SendGrid connector'ları, webhook imza doğrulama
- Golden dataset (20 senaryo) + eval harness — **gerçek Claude Haiku'ya karşı çalıştırıldı**
- İki aşamalı PII/secret redaction pipeline'ı
- 4 kaynaklı evidence toplama (deployment, önceki event, geçmiş incident, config değişikliği)
- Prompt version A/B + regresyon gate'i
- GitHub Actions CI/CD (build → test → migration-check → Docker build/push → GHCR)
- API key rotasyonunda grace period

**Bilinen, kasıtlı olarak açık bırakılmış eksikler** (bkz. `FI/README.md` "Sonraki Adımlar"):
- `fi-root-cause-v1` prompt'u kendi golden dataset kalite eşiğini (0.85) henüz geçmiyor
  (gerçek ölçüm: 0.726 — bkz. M14). Bu, prompt/grounding kontrolü üzerinde daha fazla çalışma
  gerektiren, **bilinen ve ölçülmüş** bir kalite açığı; sessizce göz ardı edilmedi.
  `PromptPromotionGate` bu prompt'u gerçek bir promote denemesinde haklı olarak reddederdi.
  M15'te gerçek Docker Compose ortamında yapılan yük testinde bu prompt/kalite konusundan
  BAĞIMSIZ, ayrı bir gerçek concurrency bug'ı da bulunup düzeltildi (bkz. aşağıda).
- Branch protection kuruldu ama repo'nun kendi CI/CD akışı henüz bir üretim ortamına deploy
  etmiyor (yalnızca GHCR'a image push ediyor).
- Seq/OTLP collector entegrasyonu yok (yalnızca konsol exporter).

## Hızlı başlangıç

```bash
cd FI
docker compose -f docker/docker-compose.yml up -d --build
```

`fi-postgres` sağlıklı olduğunda `fi-app` otomatik başlar, migration'lar container başlangıcında
otomatik uygulanır.

```bash
curl http://localhost:8080/health/ready
# Swagger UI: http://localhost:8080/swagger
```

Ayrıntılı kurulum, entegrasyon oluşturma, webhook gönderme örnekleri için
[`FI/README.md`](FI/README.md#quick-start)'ye bakın.

## Test etme

```bash
cd FI
dotnet tool restore
dotnet test tests/FI.Domain.Tests/FI.Domain.Tests.csproj        # saf birim testler, Docker gerekmez
dotnet test tests/FI.Integration.Tests/FI.Integration.Tests.csproj  # Testcontainers, gerçek PostgreSQL
```

Bu proje test edilirken yalnızca izole birim/entegrasyon testleriyle yetinilmedi — **gerçek bir
Docker Compose ortamında, gerçek HTTP istekleriyle, gerçek eşzamanlı yük altında** de uçtan uca
doğrulandı (M15). Bu süreçte izole testlerin yakalayamayacağı gerçek bir concurrency bug'ı
(Hangfire'ın paralel worker'ları aynı incident satırını eşzamanlı güncelleyince oluşan "lost
update") bulunup düzeltildi — ayrıntı için `FI/README.md`'deki M15 notuna bakın.

## CI/CD

`.github/workflows/fi-ci.yml` — her push/PR'da: **Build → Test (unit + Testcontainers
entegrasyon) → Migration Check (sıfır bir veritabanında tüm migration'lar) → Docker Build/Push
(GHCR, yalnızca `master`)**. `master` dalı branch protection ile korunuyor (Build/Test/Migration
Check zorunlu status check).

## Klasör yapısı

```
.
├── FI/                                 Uygulama kodu (bkz. FI/README.md)
│   ├── src/                            FI.Domain / FI.Application / FI.Infrastructure / FI.Api
│   ├── tests/                          FI.Domain.Tests / FI.Integration.Tests
│   └── docker/                         Dockerfile + docker-compose.yml
├── docs/
│   └── FAILURE_INTELLIGENCE_ARCHITECTURE.md   Tam mimari doküman (50+ bölüm)
└── .github/workflows/fi-ci.yml         CI/CD pipeline
```
