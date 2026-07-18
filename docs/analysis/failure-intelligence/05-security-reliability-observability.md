# 05 — Security, Reliability & Observability Architecture

**Ürün:** AI Integration Failure Intelligence (FI)
**Kapsam:** Threat model, trust boundaries, secrets, PII redaction, logging/tracing/metrics, resilience politikaları, rate limiting, audit, backup, kendi incident response planı.
**Seviye:** MVP — "doğru ve yeterli", enterprise-grade değil. Post-MVP'ye ertelenebilecek maddeler her bölümde açıkça işaretlendi.

---

## 0. Neden bu doküman kritik?

FI, müşterilerin entegrasyon hatalarını (webhook payload'ları, API response'ları, log satırları) toplayıp analiz eden bir üründür. Bu veri kümesi doğası gereği **ikincil bir hassas veri kaynağıdır**: müşterinin kendi müşterisine ait e-posta, token, kart numarası, iç sistem adresleri sızabilir. Ayrıca ürün "biz incident analiz ederiz" dediği için kendi güvenilirliği ve gözlemlenebilirliği kalitesizse konumlandırma çöker. Bu doküman hem dış yüzeyin (ingestion API, AI provider entegrasyonu) hem iç yüzeyin (kendi log/trace/metrik altyapısı) tasarımını kapsar.

---

## 1. Threat Model (Hafifletilmiş STRIDE)

### 1.1 Saldırgan / kötüye kullanım profilleri

| Aktör | Motivasyon | Erişim seviyesi |
|---|---|---|
| **Sahte event gönderen (unauthenticated/leaked key ile)** | Sistemi spam ile doldurmak, rakip tenant'ın quota/maliyetini sömürmek, DoS | Sadece public ingestion endpoint |
| **API key'i çalan kişi** (log sızıntısı, müşteri tarafında yanlış saklama, .env commit) | Tenant adına sahte event basmak, mevcut incident verisini okumak (eğer key read scope'u da varsa) | Geçerli tenant kimliği ile |
| **Kötü niyetli/dikkatsiz iç kullanıcı (müşteri tarafı developer)** | Yanlışlıkla prod secret'ları event payload'ına gömüp göndermek | Meşru key sahibi |
| **Rakip / meraklı üçüncü taraf** | Başka tenant'ın incident verisine, AI analiz sonuçlarına erişmek (tenant isolation açığı arayarak) | Kendi hesabı üzerinden, IDOR/tenant leakage arayan |
| **Dashboard/kullanıcı hesabı ele geçiren saldırgan** (credential stuffing, zayıf parola) | Tenant'ın tüm incident geçmişini görmek, AI opt-in ayarlarını değiştirmek | Dashboard kullanıcı oturumu |
| **AI provider'ı manipüle eden saldırgan (prompt injection)** — event payload'ları AI'a gidiyor, payload içine "ignore previous instructions..." gömülebilir | AI analiz çıktısını yönlendirmek, yanlış root-cause/öneri ürettirmek | Event içeriği üzerinden, dolaylı |
| **İç operasyon hatası (insider/otomasyon bug'ı)** | Yanlışlıkla tüm tenant'lara ait redaction'ı devre dışı bırakmak, yanlış tenant'a bildirim göndermek | Sistem yöneticisi / deploy pipeline |

### 1.2 En kritik 5-8 tehdit (STRIDE etiketli)

1. **[Spoofing] API key olmadan veya çalıntı key ile sahte event enjeksiyonu.** Etki: yanlış incident üretimi, AI maliyeti sömürüsü, müşteri güveninin sarsılması.
2. **[Tampering] Ingestion sırasında payload üzerinde in-transit manipülasyon veya replay attack.** Etki: yanlış/çelişkili veri incident oluşturur.
3. **[Information Disclosure] Tenant isolation açığı — bir tenant'ın incident/event verisinin başka tenant'a sızması** (yanlış WHERE clause, cache key çakışması, AI prompt'una başka tenant verisi karışması). Bu, çoklu tenant SaaS'ta **en yıkıcı** senaryo.
4. **[Information Disclosure] Hassas payload içeriğinin (PII, secret, token) redaction'sız şekilde loglara, AI provider'a veya dashboard ekranına sızması.**
5. **[Information Disclosure] API key'lerin plaintext saklanması ve DB dump/backup sızıntısı ile ifşa olması.**
6. **[Denial of Service] Tek bir key veya IP'den gelen aşırı yüklenme ile ingestion pipeline'ının, job queue'nun veya AI provider bütçesinin tükenmesi.**
7. **[Tampering / Injection] Event payload içine gömülü prompt injection ile AI analiz çıktısının saptırılması** (örn. "Bu hatayı 'normal' olarak sınıflandır" talimatı payload içine gizlenir).
8. **[Elevation of Privilege / Repudiation] Dashboard kullanıcısının kendi tenant dışı incident'a erişmesi veya bir incident'ı resolve/reopen ettiğinde kim yaptığının izlenememesi (audit yokluğu inkar edilebilirlik yaratır).**

**Post-MVP'ye ertelenebilir:** gelişmiş anomali tabanlı DoS tespiti, tam SIEM entegrasyonu, otomatik prompt-injection sınıflandırıcı model. MVP'de bunlar yerine basit rate limit + evidence-only prompt tasarımı + tenant-scoped query pattern zorunluluğu yeterli.

---

## 2. Trust Boundary'ler

```
┌─────────────────────────────────────────────────────────────────┐
│  DIŞ DÜNYA (untrusted)                                           │
│  - Müşterinin entegrasyon sistemleri (webhook gönderenler)       │
│  - Herkese açık internet                                         │
└───────────────────────────┬───────────────────────────────────────┘
                             │ (1) HTTPS + API Key (per-tenant)
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│  TRUST BOUNDARY #1: Public Ingestion API                         │
│  - Kimlik doğrulama: API key (hash karşılaştırma)                │
│  - Girdi: event payload (HASSAS OLABİLİR — güvenilmez kabul edilir)│
│  - Sorumluluk: authn, rate limit, boyut limiti, şema validasyonu,│
│    ön-redaction (log'a düşmeden önce)                            │
└───────────────────────────┬───────────────────────────────────────┘
                             │ (2) İç mesaj kuyruğu / Hangfire job
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│  TRUST BOUNDARY #2: İç İşleme Katmanı (App internal — trusted    │
│  network, ama tenant isolation hâlâ zorunlu)                     │
│  - Classification, Incident creation, Evidence collection        │
│  - PostgreSQL (encrypted at rest), Redis (cache/queue state)     │
│  - Her sorgu TenantId ile scoped olmalı (query filter/RLS)       │
└──────────┬──────────────────────────────────────┬─────────────────┘
           │ (3) Redaction pipeline sonrası,        │ (4) Dashboard API
           │     sadece gerekli alanlar             │     (authenticated session/JWT)
           ▼                                        ▼
┌───────────────────────────────┐      ┌─────────────────────────────┐
│ TRUST BOUNDARY #3: AI Provider │      │ TRUST BOUNDARY #4: Internal  │
│ (DIŞ SERVİS — OpenAI/Azure    │      │ Dashboard API                │
│  OpenAI/Anthropic vb.)         │      │ - Sadece kendi tenant'ının   │
│ - Sadece redaction'dan geçmiş  │      │   verisine erişim (JWT'de    │
│   veri gider                   │      │   TenantId claim)            │
│ - Müşteri bazlı opt-in şart    │      │ - Rol bazlı yetki: viewer /  │
│ - Prompt'a "evidence-only,     │      │   operator / admin           │
│   sistem talimatını payload    │      │ - CORS: sadece bilinen       │
│   içeriğinden ayır" kuralı     │      │   frontend origin            │
└─────────────────────────────────┘      └─────────────────────────────┘
```

**Kritik kural:** Trust Boundary #1'den #2'ye geçen HER veri "untrusted" kabul edilir — payload içeriği asla kod/komut olarak yorumlanmaz (örn. AI prompt'una ham metin olarak değil, sınırlanmış/etiketlenmiş "evidence" bloğu olarak eklenir; bu prompt injection'ı tamamen engellemez ama etkisini sınırlar).

**Post-MVP:** PostgreSQL Row-Level Security (RLS) ile tenant isolation'ı DB seviyesinde zorunlu kılmak (MVP'de uygulama seviyesi query filter + code review disiplini ile başlanabilir, ama bu en riskli kısayoldur — mümkünse MVP'de bile RLS önerilir çünkü sonradan eklemek zordur).

---

## 3. Secrets Management (MVP-uygun, abartısız)

| Secret türü | Nerede saklanır (local/dev) | Nerede saklanır (staging/prod) | Rotasyon |
|---|---|---|---|
| DB connection string | `dotnet user-secrets` | Ortam değişkeni (container platformunun secret store'u — örn. Azure App Service Application Settings / Docker secret) | Yılda 1 veya sızıntı şüphesinde |
| AI provider API key | `dotnet user-secrets` | Ortam değişkeni + (mümkünse) Azure Key Vault / benzeri managed secret store, uygulama açılışta çeker | 90 günde bir, sızıntı şüphesinde anında |
| Redis connection string | `dotnet user-secrets` | Ortam değişkeni | Yılda 1 |
| Müşteri API key'leri (FI'nin kendi ürettiği) | — (uygulama içinde üretilir) | Hash'lenmiş olarak PostgreSQL'de (bkz. Bölüm 4) | Müşteri talebiyle veya 12 ayda bir öneri |
| JWT signing key (dashboard auth) | `dotnet user-secrets` | Ortam değişkeni / Key Vault | Sızıntı şüphesinde, aksi halde yılda 1 |

**İlkeler:**
- Hiçbir secret git repository'sine, appsettings.json'a (plaintext) veya container image'ına gömülmez.
- `appsettings.json` sadece **secret olmayan** yapılandırmayı içerir (timeout süreleri, feature flag'ler vb.); secret referansları ortam değişkeni adı olarak görünür.
- MVP'de tam Key Vault entegrasyonu (dinamik secret rotation, managed identity) **opsiyonel** — ortam değişkeni + platformun native secret injection'ı (örn. Docker Compose `secrets:`, Azure App Settings) yeterlidir. Key Vault, ikinci müşteri segmentine (enterprise/compliance talep eden) geçildiğinde eklenir.
- CI/CD pipeline secret'ları (deploy key'leri) GitHub Actions Secrets / benzeri native mekanizmada tutulur, koda asla yazılmaz.

**Post-MVP:** Azure Key Vault / HashiCorp Vault ile otomatik rotation, managed identity ile secret-less auth, envelope encryption.

---

## 4. API Key Security

### 4.1 Format ve saklama
- Key formatı: `fi_live_<32-byte-random-base62>` (prefix ile ortam ayrımı: `fi_live_`, `fi_test_`). Prefix, log'larda ve UI'da key'in **son 4 karakteri + prefix** gösterilir (`fi_live_...a91f`), tam key asla ikinci kez gösterilmez.
- Sunucu tarafında saklanan: **SHA-256 hash** (key + sabit bir pepper/uygulama sırrı ile HMAC-SHA256 önerilir, salt gerekmez çünkü key zaten yüksek entropili rastgele değerdir). Bcrypt/Argon2 gibi yavaş hash'ler burada gereksizdir — brute force zaten pratik değildir (yüksek entropi), asıl amaç DB sızıntısında ham key'in geri elde edilememesi. HMAC-SHA256 + gizli pepper, hız açısından yüksek hacimli ingestion doğrulaması için de uygundur.
- İlk üretimde key kullanıcıya **bir kez** gösterilir, sonra sadece hash + metadata (oluşturulma tarihi, son kullanım tarihi, oluşturan kullanıcı) saklanır.

### 4.2 Rotation / Revoke akışı
- Her tenant birden fazla aktif key'e sahip olabilir (`status: active | revoked`), böylece rotation sırasında kesinti olmaz: yeni key oluştur → müşteri geçiş yapar → eski key `revoked` işaretlenir (fiziksel silme değil, audit için saklanır).
- Revoke anlık etkilidir — sonraki istekte hash eşleşmesi `status=active` filtresiyle yapılır, revoked key derhal 401 döner.
- Sızıntı şüphesi bildirimi (müşteri veya otomatik tarama — örn. GitHub secret scanning entegrasyonu post-MVP) → key otomatik/manuel revoke + müşteriye bildirim.
- `LastUsedAt` her istekte (batch/async güncellenebilir, her istekte senkron DB yazımı gereksiz) güncellenir; 90+ gün kullanılmamış key'ler için dashboard'da uyarı gösterilir (post-MVP: otomatik expire).

### 4.3 Rate limit per key
Bkz. Bölüm 12 — key bazlı limit, plan tipine göre farklılaşabilir (örn. Free: 60 req/dk, Pro: 600 req/dk).

**Post-MVP:** Key scope'ları (yalnızca ingest / yalnızca read), IP allowlist per key, otomatik leaked-key taraması (GitHub'da public repo tarama entegrasyonu).

---

## 5. PII Redaction

### 5.1 İki aşamalı redaction stratejisi

**Aşama A — Ingestion sırasında (log'a yazılmadan önce):** Ham payload log'lara veya herhangi bir observability sistemine (Serilog, trace attribute) **asla redaction'sız** yazılmaz. Bu aşamada amaç: kendi altyapımızın (log dosyaları, APM, trace backend) hassas veri deposu haline gelmesini önlemek.

**Aşama B — AI'a gönderilmeden hemen önce:** Ham payload veritabanında (encrypted at rest) saklanabilir (incident evidence için gerekli), ama AI provider'a giden prompt'a eklenmeden önce **ikinci bir redaction pass**'i zorunludur — çünkü veri üçüncü taraf bir servise (AI provider) gidiyor ve bu, veri işleme sözleşmesi/compliance açısından farklı bir risk sınıfıdır. Ayrıca müşteri bazlı AI opt-in kontrolü de bu noktada uygulanır (opt-out ise AI çağrısı hiç yapılmaz).

Bu iki aşama **aynı redaction motorunu** kullanır ama farklı konfigürasyon profilleriyle çalışabilir: log profili biraz daha gevşek olabilir (debug için bazı alanlar hash'lenmiş halde tutulabilir), AI profili en katıdır (tam maskeleme).

### 5.2 Redaction edilecek alan kategorileri ve somut pattern'ler

| Kategori | Nerede aranır | Pattern / yöntem | Maskeleme |
|---|---|---|---|
| **Authorization header** | HTTP header alanları | Header adı case-insensitive `authorization`, `x-api-key`, `x-auth-token` eşleşmesi | Değer tamamen `[REDACTED]` ile değiştirilir |
| **Cookie** | HTTP header | Header adı `cookie`, `set-cookie` | Tamamen `[REDACTED]` |
| **Bearer/JWT token** | Body veya header içi serbest metin | Regex: `Bearer\s+[A-Za-z0-9\-_\.]+` ve JWT şekli `eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+` | `Bearer [REDACTED]` |
| **E-posta** | Body, log mesajı, herhangi bir string alan | Regex: `[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}` | Domain korunabilir, local-part maskelenir: `j***@example.com` (analiz için domain bazen faydalı, konfigüre edilebilir) |
| **Kredi kartı numarası** | Body, serbest metin | Regex (Luhn ön-kontrolü ile birlikte): `\b(?:\d[ -]*?){13,19}\b` + Luhn doğrulaması ile false-positive azaltma | Son 4 hane hariç maskele: `**** **** **** 1234` veya tam `[REDACTED-CC]` |
| **API key / secret benzeri değerler** | Body içinde key=value veya JSON alan adı `apiKey`, `secret`, `password`, `token`, `client_secret` içeren alanlar | Alan adı bazlı (allow/deny list) + yüksek entropili string heuristiği (>20 karakter, karışık alfanümerik) | `[REDACTED]` |
| **IP adresi (opsiyonel, tenant konfigüre edilebilir)** | Body, log context | Regex: standart IPv4/IPv6 pattern | Son oktet maskelenir: `192.168.1.***` (varsayılan kapalı, çoğu entegrasyon debug'ı için IP gerekli olabilir) |
| **TC Kimlik No / SSN benzeri ulusal kimlik no (bölgeye göre)** | Body | Bölgeye özel regex (örn. TR TC No: 11 haneli, ilk hane 0 olamaz + checksum) | Tamamen `[REDACTED]` |
| **Telefon numarası** | Body | Regex: `\+?\d[\d\s\-\(\)]{7,}\d` | Son 4 hane hariç maskele |

### 5.3 Uygulama yöntemi
- **Field-based masking (öncelikli, yüksek güvenilirlik):** Bilinen JSON path'ler (`$.headers.authorization`, `$.body.user.email`) için tenant bazlı konfigüre edilebilir kural listesi. Müşteri kendi entegrasyonuna özel alanları (`$.body.customer.ssn` gibi) dashboard üzerinden ekleyebilir.
- **Pattern-based masking (yedek/genel amaçlı):** Yukarıdaki regex'ler, bilinmeyen/serbest metin alanlarında son çare olarak çalışır. Field-based'e göre daha fazla false-positive/false-negative riski taşır, bu yüzden birincil değil tamamlayıcı katmandır.
- Redaction motoru **idempotent ve deterministik** olmalı — aynı payload iki kez redaction'dan geçse aynı sonucu üretmeli (log korelasyonu bozulmasın).
- Redaction sonrası orijinal-uzunluk bilgisi (kaç karakter maskelendi) opsiyonel olarak korunabilir, debugging için faydalı olabilir ama gizli bilgi sızdırmaz.

**Post-MVP:** ML tabanlı PII tespiti (regex'in kaçırdığı serbest metin PII'leri yakalamak için, örn. isim tespiti), format-preserving encryption (maskelemek yerine geri döndürülebilir şifreleme, sadece yetkili incident review'da açılabilir).

---

## 6. Log Schema (Serilog JSON Structured Logging)

Her log satırı aşağıdaki alanları (mümkün olanları) içerir; Serilog `LogContext`/enricher'lar ile otomatik eklenir:

```json
{
  "Timestamp": "2026-07-12T14:32:07.123Z",
  "Level": "Information",
  "Message": "Event classified as WebhookTimeout",
  "MessageTemplate": "Event classified as {Classification}",
  "SourceContext": "FI.Classification.EventClassifier",
  "CorrelationId": "b3f1c9e2-4a7d-4e0a-9c1a-8f2d3e5a6b7c",
  "TenantId": "tn_01HXYZ...",
  "IntegrationId": "int_01HABC...",
  "IncidentId": "inc_01HDEF... (varsa)",
  "JobId": "job_01HGHI... (Hangfire job'undan tetiklenmişse)",
  "SpanId": "a1b2c3d4e5f6",
  "TraceId": "1234567890abcdef1234567890abcdef",
  "Environment": "production",
  "ServiceName": "fi-api",
  "ServiceVersion": "1.4.2",
  "Exception": "null (varsa .NET exception ToString() + StackTrace)",
  "Properties": {
    "Classification": "WebhookTimeout",
    "ConfidenceScore": 0.87
  }
}
```

**Zorunlu alanlar (her log satırında bulunmalı):** `Timestamp`, `Level`, `Message`, `CorrelationId`, `ServiceName`, `Environment`.
**Mevcut olduğunda zorunlu (context varsa eklenmeli, yoksa `null` bırakılabilir):** `TenantId`, `IntegrationId`, `IncidentId`, `JobId`, `TraceId`/`SpanId`.
**Hata durumunda zorunlu:** `Exception` (tam stack trace, ama mesaj içeriğinde PII varsa Bölüm 5'teki redaction aynı şekilde exception mesajlarına da uygulanır — özellikle 3. parti kütüphane exception'ları ham request body'yi içerebilir).

**Kurallar:**
- Serilog `Enrich.FromLogContext()` + custom enricher (`TenantIdEnricher`, `CorrelationIdEnricher`) middleware'de scope açılırken set edilir; alt katmanlar (job, servis) tekrar tekrar parametre geçirmek zorunda kalmaz.
- Log seviyesi disiplini: `Debug` (yerel geliştirme, prod'da kapalı), `Information` (normal iş akışı — event alındı, incident oluşturuldu), `Warning` (retry tetiklendi, redaction eksik alan tespit edildi), `Error` (işlem başarısız ama sistem ayakta), `Fatal` (servis başlatılamıyor).
- **Asla loglanmayacaklar:** ham (redaction'sız) request/response body, tam API key, şifreler, session token'ları.

**Post-MVP:** Merkezi log aggregation (Loki/Elastic/Seq) ile sorgulanabilir dashboard, log retention policy'nin otomasyonu, log-based alerting kuralları (örn. "aynı TenantId için 5 dakikada 50+ Error").

---

## 7. Correlation Stratejisi

**Amaç:** Bir HTTP isteğinin, ondan doğan arka plan job'ın, ürettiği event/incident/AI çağrısının uçtan uca tek bir kimlikle izlenebilmesi.

1. **Girdi (public ingestion API):** İstemci `X-Correlation-Id` header'ı gönderebilir (opsiyonel). Göndermezse middleware yeni bir `Guid.NewGuid()` üretir.
2. **Middleware (ASP.NET Core, pipeline'ın en başında):** `CorrelationIdMiddleware` bu değeri okur/üretir, `HttpContext.Items["CorrelationId"]` içine koyar, response header'ına da (`X-Correlation-Id`) geri yazar (müşteri kendi loglarıyla eşleştirebilsin diye), ve Serilog `LogContext.PushProperty("CorrelationId", id)` ile tüm alt loglara otomatik yayılır.
3. **OpenTelemetry entegrasyonu:** Aynı middleware, mevcut `Activity` (root span) üzerine `CorrelationId`'yi bir tag/attribute olarak ekler (`activity.SetTag("fi.correlation_id", id)`), böylece hem log hem trace aynı kimlikle sorgulanabilir. `TraceId` zaten OpenTelemetry tarafından otomatik yönetilir; `CorrelationId` iş-anlamlı ek bir kimlik olarak (müşteri-görünür, insan-okunur senaryo takibi için) ayrıca taşınır.
4. **Job'a taşınma (Hangfire):** İşlem senkron kısımda tamamlanmıyorsa (örn. AI analysis job'a devrediliyorsa), job'ı enqueue eden kod `CorrelationId`'yi job parametresi olarak (job argümanı, DB'ye yazılan Event/Incident kaydının bir alanı) taşır — Hangfire'ın kendi execution context'i thread'ler arası otomatik taşımadığı için **açıkça parametre olarak geçirilmesi zorunludur**.
5. **Job içinde:** Job başlarken `LogContext.PushProperty("CorrelationId", ...)` tekrar açılır (yeni thread/worker olduğu için) ve aynı zamanda yeni bir OpenTelemetry `Activity` başlatılırken parent olarak orijinal `TraceId` (eğer taşınabiliyorsa W3C Trace Context formatında saklanmışsa) veya en azından `CorrelationId` tag'i eklenir.
6. **AI analysis çağrısına kadar:** Aynı `CorrelationId`, `IncidentId` ile birlikte AI provider isteğine (HTTP header olarak değil, sadece kendi telemetrimizde) taşınır — dış AI provider'a bizim iç correlation id'mizi header olarak göndermek gerekmez, sadece kendi trace/log kayıtlarımızda tutarlı olması yeterli.

**Sonuç:** `CorrelationId` ile log sorgusu yapıldığında: ingestion isteği → classification job'ı → incident oluşturma → evidence toplama → AI analysis çağrısı, tek sorguda kronolojik olarak görülebilir.

---

## 8. OpenTelemetry Trace Yapısı

### 8.1 Span hiyerarşisi (tipik bir olay akışı için)

```
IngestEvent (root span, HTTP request)
 └─ ValidateAndRedact
 └─ PersistRawEvent
 └─ ClassifyEvent (job veya inline)
     └─ (opsiyonel) MatchRuleEngine
 └─ CreateIncident  (eğer yeni/eşleşen incident yoksa/varsa güncelleme)
     └─ CollectEvidence
         ├─ FetchRelatedLogs
         ├─ FetchRelatedEvents
         └─ FetchIntegrationMetadata
     └─ AIAnalysis
         ├─ BuildPrompt (redaction pass burada da doğrulanır)
         ├─ CallAIProvider   (dış çağrı, Polly retry/timeout span'ları içerir)
         └─ ParseAndValidateResponse
 └─ NotifyCustomer (webhook/email — opsiyonel, dış çağrı)
```

### 8.2 Her span için önerilen attribute'lar

| Span | Attribute'lar |
|---|---|
| `IngestEvent` | `fi.tenant_id`, `fi.integration_id`, `fi.correlation_id`, `http.method`, `http.route`, `http.status_code`, `fi.payload_size_bytes` |
| `ClassifyEvent` | `fi.tenant_id`, `fi.event_id`, `fi.classification_result`, `fi.classification_confidence`, `fi.classification_method` (rule-based/AI-assisted) |
| `CreateIncident` | `fi.tenant_id`, `fi.incident_id`, `fi.incident_status` (new/updated/duplicate), `fi.severity` |
| `CollectEvidence` | `fi.incident_id`, `fi.evidence_count`, `fi.evidence_sources` |
| `AIAnalysis` | `fi.incident_id`, `fi.ai_provider`, `fi.ai_model`, `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`, `fi.ai_latency_ms`, `fi.ai_confidence_score`, `fi.ai_parse_success` (bool), `fi.ai_estimated_cost_usd` — (OpenTelemetry semantic conventions'daki `gen_ai.*` isimlendirmesi tercih edilir, uyumluluk için) |
| `CallAIProvider` (Polly ile sarmalanmış dış çağrı) | `fi.retry_attempt`, `fi.circuit_breaker_state`, `http.status_code`, `error.type` (timeout/5xx/parse-error) |
| `NotifyCustomer` | `fi.notification_channel` (webhook/email), `http.status_code`, `fi.retry_attempt` |

**Hata durumunda:** Her span, başarısız olduğunda OpenTelemetry standardına uygun şekilde `Status = Error` işaretlenir ve `exception` event'i (mesaj + stack trace, redaction uygulanmış) eklenir.

**Post-MVP:** Sampling stratejisinin olgunlaştırılması (MVP'de %100 sampling düşük hacimde sorun değil; hacim arttıkça tail-based sampling — özellikle hata içeren trace'leri her zaman tut, başarılıları örnekle), distributed trace context'in AI provider'a W3C traceparent header'ı olarak iletilmesi (provider destekliyorsa).

---

## 9. Metrics Listesi

| Metrik | Tip | Amaç |
|---|---|---|
| `fi.events.ingested.count` (tag: `tenant_id`, `integration_type`) | Counter | Ingestion hacmi, tenant bazlı kullanım/faturalama sinyali |
| `fi.events.ingestion.duration` | Histogram | Ingestion endpoint latency |
| `fi.events.classification.duration` | Histogram | Sınıflandırma latency |
| `fi.events.classification.confidence` | Histogram | Model/kural güven dağılımı (düşükse insan review tetikler) |
| `fi.incidents.created.count` (tag: `tenant_id`, `severity`) | Counter | Incident üretim hızı |
| `fi.incidents.deduplicated.count` | Counter | Aynı köke bağlanan tekrar event sayısı (gürültü ölçütü) |
| `gen_ai.request.duration` (`fi.ai_analysis.latency_ms`) | Histogram | AI çağrı gecikmesi |
| `gen_ai.usage.input_tokens` / `gen_ai.usage.output_tokens` | Counter/Histogram | Token tüketimi (maliyet hesaplamasının temeli) |
| `fi.ai_analysis.cost_usd` (tag: `tenant_id`) | Counter | Tahmini AI maliyeti, tenant bazlı — plan/quota kararları için |
| `fi.ai_analysis.parse_failure.count` | Counter | AI'ın structured output beklentisini karşılamadığı durumlar — hallucination/format riskinin erken sinyali |
| `fi.ai_analysis.human_review_required.count` | Counter | Düşük confidence nedeniyle insana devredilen analiz sayısı |
| `hangfire.jobs.queue_depth` (tag: `queue_name`) | Gauge | Job kuyruk derinliği — backlog sinyali |
| `hangfire.jobs.failed.count` (tag: `job_type`) | Counter | Job başarısızlık oranı |
| `hangfire.jobs.duration` | Histogram | Job işleme süresi |
| `fi.api_key.rate_limited.count` (tag: `tenant_id`) | Counter | Rate limit'e takılan istek sayısı — abuse veya plan yükseltme sinyali |
| `fi.redaction.fields_masked.count` | Counter | Redaction pipeline'ının aktif çalıştığının kanıtı (sıfırsa bir şey bozuktur) |
| `fi.circuit_breaker.state_changed.count` (tag: `dependency`) | Counter | AI provider veya webhook hedefi circuit breaker açılma sıklığı |
| `db.postgresql.connection_pool.active` | Gauge | DB bağlantı havuzu doygunluğu |

**Post-MVP:** Business-level dashboard (tenant başına aylık maliyet/kar), SLO tabanlı error budget metrikleri, anomaly detection üzerine kurulu otomatik alerting (MVP'de basit eşik tabanlı alert yeterli).

---

## 10. Health Checks

ASP.NET Core `Microsoft.Extensions.Diagnostics.HealthChecks` ile iki ayrı endpoint:

- **`/health/live` (liveness):** Sadece process'in ayakta olduğunu doğrular (dış bağımlılık kontrolü YOK — Kubernetes/orchestrator'ın gereksiz yere pod'u restart etmesini önlemek için). Her zaman hızlı döner.
- **`/health/ready` (readiness):** Trafiği kabul etmeye hazır olup olmadığını kontrol eder, aşağıdaki bağımlılıkları içerir:

| Bağımlılık | Kontrol yöntemi | Kritiklik |
|---|---|---|
| PostgreSQL | Basit `SELECT 1` sorgusu, kısa timeout (2sn) | **Kritik** — başarısızsa `ready` false döner, trafik alınmaz |
| Redis | `PING` komutu | **Kritik** (cache/queue state için kullanılıyorsa) — ama tasarıma göre "degraded ama çalışır" da olabilir (bkz. Bölüm 15) |
| AI Provider | Hafif bir erişilebilirlik kontrolü (örn. provider'ın kendi status endpoint'i veya son N dakikadaki başarı oranı üzerinden içsel hesaplama — her health check'te gerçek bir AI çağrısı yapmak maliyetli ve gereksizdir) | **Kritik değil** — AI provider down olsa bile sistem event ingestion'ı kabul etmeye devam etmeli (bkz. Bölüm 15, degrade mode). Readiness'i bloke etmemeli, ayrı bir "AI subsystem health" göstergesi olarak dashboard'da gösterilir. |
| Hangfire / job processing | Hangfire'ın kendi dashboard'u (`/hangfire`) zaten temel bir görünürlük sağlar; ek olarak "son N dakikada en az 1 job işlendi mi" gibi basit bir canlılık sinyali readiness'e eklenebilir ama zorunlu değildir | Düşük — kritiklik seviyesi |

**Kurallar:**
- `/health/ready` sadece PostgreSQL'i **zorunlu** bağımlılık olarak işaretler (veri yazılamıyorsa gerçekten hizmet veremeyiz). Redis ve AI provider için "kısmi hizmet verebilirlik" felsefesi benimsenir (bkz. Bölüm 15).
- Health check endpoint'leri **authentication gerektirmez** ama sadece internal network/load balancer'dan erişilebilir olmalı (public'e açılmamalı — dış dünyaya iç mimari bilgisi sızdırmamak için).
- Hangfire dashboard (`/hangfire`) mutlaka authentication arkasına alınır (varsayılan olarak herkese açıktır, bu yaygın bir yapılandırma hatasıdır).

**Post-MVP:** Her bağımlılık için ayrı granüler health check response'u (detaylı JSON: hangi bağımlılık ne kadar sürede yanıt verdi), dependency health history/trend.

---

## 11. Retry / Timeout / Circuit Breaker (Polly)

Dış çağrılar iki kategoridedir: **AI provider çağrısı** (kritik iş akışı ama yavaş/pahalı) ve **webhook/bildirim gönderimi** (best-effort, müşteriye giden çıkış trafiği).

### 11.1 AI Provider çağrısı

| Politika | Değer | Gerekçe |
|---|---|---|
| Timeout (per attempt) | 30 saniye | LLM çağrıları uzun sürebilir ama sınırsız beklenemez; incident analizi kullanıcıyı bekletmemeli |
| Retry sayısı | 2 (toplam 3 deneme) | LLM çağrıları pahalı — agresif retry maliyeti şişirir |
| Backoff stratejisi | Exponential backoff + jitter: 2sn, 5sn (± rastgele %20 jitter) | Thundering herd'i önler, provider rate limit'ine nazik davranır |
| Retry edilecek durumlar | HTTP 429, 500, 502, 503, 504, timeout | Retry edilmeyecek: 400 (bozuk istek — retry sonucu değiştirmez), 401/403 (auth sorunu, retry çözmez) |
| Circuit breaker eşiği | Son 10 çağrıda %50+ başarısızlık → circuit **Open**, 30 saniye boyunca çağrı denenmez (fail-fast), sonra **Half-Open** ile 1 deneme | Provider tamamen down olduğunda gereksiz bekleme/maliyetten kaçınır, degrade mode'a (Bölüm 15) hızlı geçişi tetikler |
| Toplam bütçe (timeout+retry) | ~1 dakikayı geçmemeli | Job worker'ın çok uzun süre bir işi bloke etmesini önler |

### 11.2 Webhook / bildirim gönderimi (müşteriye çıkış)

| Politika | Değer | Gerekçe |
|---|---|---|
| Timeout (per attempt) | 10 saniye | Müşteri endpoint'i bizim kontrolümüzde değil, kısa tutulur |
| Retry sayısı | 4 (toplam 5 deneme), Hangfire'ın kendi retry mekanizmasıyla dakikalar/saatler arası genişleyen aralıklarla (örn. 1dk, 5dk, 30dk, 2sa) | Webhook alıcı taraf geçici olarak down olabilir, uzun vadeli retry mantıklı (AI çağrısının aksine ucuz) |
| Circuit breaker | Aynı hedef URL için son 5 denemede tamamı başarısızsa 15 dakika boyunca o hedefe çağrı durdurulur | Sürekli down bir müşteri endpoint'ine gereksiz kaynak harcamayı önler |
| Retry edilmeyecek | 4xx (401 hariç tutulabilir çünkü müşteri secret'ı yanlış yapılandırmış olabilir — yine de retry mantıklı değil, bildirim gerekir) | Müşteri tarafı yapılandırma hatası retry ile çözülmez |

**Genel ilke:** Polly `AddResilienceHandler` (yeni .NET 8+ resilience pipeline API'si) ile her dış bağımlılık için ayrı, isimlendirilmiş pipeline tanımlanır (`"ai-provider-pipeline"`, `"webhook-pipeline"`), böylece her birinin metrikleri (Bölüm 9'daki `fi.circuit_breaker.state_changed.count`) ayrı ayrı izlenebilir.

**Post-MVP:** Bulkhead isolation (AI çağrıları için ayrı thread pool/concurrency limiti), adaptive timeout (geçmiş latency dağılımına göre dinamik ayarlama), provider fallback (birincil AI provider down olursa ikincil provider'a otomatik geçiş — bu MVP için aşırı mühendislik, tek provider + degrade mode yeterli).

---

## 12. Rate Limiting Planı

ASP.NET Core `Microsoft.AspNetCore.RateLimiting` (built-in, ek bağımlılık gerektirmez) ile:

### 12.1 Per API key (tenant bazlı, plan'a göre farklılaşan)

| Plan | Limit | Pencere | Algoritma |
|---|---|---|---|
| Free/Trial | 60 istek/dakika | Sliding window | Ingestion endpoint'i için |
| Pro | 600 istek/dakika | Sliding window | |
| Enterprise (post-MVP, custom) | Anlaşmaya göre | | |

- Limit aşıldığında: HTTP 429 + `Retry-After` header'ı.
- Redis, dağıtık rate limit sayacı için kullanılır (birden fazla instance arasında tutarlılık için — tek instance'da in-memory yeterli olurdu ama yatay ölçeklenme MVP'de bile muhtemel).

### 12.2 Per IP (kimliksiz/anonim koruma katmanı)

- Authentication öncesi (henüz key doğrulanmadan) IP bazlı genel bir koruma: örn. aynı IP'den dakikada 1000 istek üstü otomatik 429 — bu, key doğrulama maliyetini bile tüketen kaba DoS denemelerine karşı ilk savunma hattıdır.
- Dashboard login endpoint'i ayrıca IP bazlı sıkı limitlenir (örn. 10 başarısız login/15dk) — credential stuffing koruması.

### 12.3 Genel kurallar
- Rate limit response'u içinde `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset` header'ları döner (istemci deneyimi için standart pratik).
- Rate limit ihlalleri metrik olarak izlenir (`fi.api_key.rate_limited.count`) — bu hem abuse tespiti hem de "müşteri plan yükseltmeli mi" sinyali olarak iş tarafına faydalıdır.

**Post-MVP:** Endpoint bazlı granüler limitler (ingestion vs dashboard read farklı limit), burst allowance (token bucket ile kısa süreli patlamalara izin), otomatik anomali tabanlı geçici bloklama.

---

## 13. Audit Log Sistemi

**Amaç:** "Kim, ne zaman, hangi incident'a, ne yaptı" sorusuna kesin yanıt — hem güvenlik hem operasyonel hesap verebilirlik için.

### 13.1 Kapsam (MVP)
Aşağıdaki **kullanıcı tetikli** aksiyonlar audit edilir (sistem/AI tetikli otomatik aksiyonlar zaten normal loglamada var, ama audit ayrı ve değiştirilemez bir kayıt gerektirir):

- Incident resolve / reopen / severity değiştirme / manuel assignment
- AI analiz sonucunun insan tarafından onaylanması/reddedilmesi (human review kararı)
- API key oluşturma / revoke etme
- Tenant ayarlarının değiştirilmesi (AI opt-in/opt-out, redaction kuralı ekleme/kaldırma)
- Dashboard kullanıcı davet/rol değişikliği

### 13.2 Şema
Ayrı bir `AuditLogs` tablosu (append-only, update/delete yok — uygulama seviyesinde ve mümkünse DB seviyesinde bir trigger/permission ile bu garanti altına alınır):

```
AuditLogId, TenantId, ActorUserId, ActorEmail (snapshot, kullanıcı silinse bile iz kalsın),
Action (enum: IncidentResolved, IncidentReopened, ApiKeyRevoked, ...),
TargetEntityType, TargetEntityId,
Timestamp, CorrelationId,
Metadata (JSONB — örn. eski/yeni değer: {"from": "open", "to": "resolved"})
```

- Genel loglama (Serilog) ile audit log **ayrı tutulur**: Serilog operasyonel/teknik izlenebilirlik içindir (yüksek hacim, kısa retention olabilir), audit log iş/uyumluluk izlenebilirliği içindir (düşük hacim, uzun retention, değiştirilemez).
- Dashboard'da her incident detay sayfasında "History" sekmesi bu tablodan beslenir — kullanıcıya doğrudan görünür.

**Post-MVP:** Audit log'un ayrı, write-once bir depoya (örn. append-only object storage) periyodik export'u; tamper-evident hash zinciri (her kayıt bir önceki kaydın hash'ini içerir).

---

## 14. Backup / Recovery (PostgreSQL)

**MVP seviyesinde yeterli yaklaşım (abartısız):**

- **Otomatik günlük tam backup** (managed PostgreSQL servisi kullanılıyorsa — örn. Azure Database for PostgreSQL, AWS RDS — bu genelde yerleşik bir özellik; kendi barındırılan Docker PostgreSQL ise `pg_dump` cron job ile).
- **Point-in-time recovery (PITR) için WAL archiving** — managed servislerde genelde varsayılan olarak gelir (örn. Azure/AWS 7-35 gün arası PITR penceresi sunar). Kendi barındırılan senaryoda `wal-g` veya benzeri bir araçla WAL arşivleme kurulabilir; MVP'de bu opsiyonel olabilir, günlük full backup + 24 saatlik veri kaybı toleransı (RPO=24s) kabul edilebilir bir başlangıç noktasıdır.
- **Backup retention:** 7 günlük günlük backup + haftalık backup'ın 4 hafta saklanması yeterli (MVP). Uzun vadeli arşivleme (compliance gerektirmedikçe) gereksiz.
- **Restore testi:** Ayda bir, backup'tan bir staging ortamına restore edilip doğrulanmalı — "backup alınıyor ama hiç test edilmemiş" en yaygın ve tehlikeli hatadır.
- **RTO/RPO hedefleri (MVP için makul):** RPO ≤ 24 saat (managed servis PITR varsa saatler mertebesine iner), RTO ≤ birkaç saat (otomatik değil, manuel müdahale ile restore).

**Redis için:** Redis burada cache/queue-state amaçlı kullanılıyorsa (kalıcı sistem-of-record değilse) backup gerekmez — veri kaybında yeniden inşa edilebilir olmalı (bu tasarım kısıtı önemlidir: Redis asla tek kaynak olmamalı, PostgreSQL her zaman gerçek kaynak).

**Post-MVP:** Cross-region backup replikasyonu, otomatik disaster recovery failover, gerçek zamanlı PITR ile RPO'yu dakikalar seviyesine indirme, backup şifreleme anahtarlarının ayrı yönetimi.

---

## 15. Kendi Incident Response Planı (FI, kendi kendini nasıl idare eder)

FI bir "hata analiz" ürünü olduğu için kendi hatalarını nasıl yönettiği ürünün güvenilirliğinin en somut kanıtıdır.

### 15.1 Senaryo: AI provider tamamen down

**Degrade mode (graceful degradation) — sistem AI olmadan da değer üretmeye devam eder:**

1. Circuit breaker (Bölüm 11) 30 saniye içinde AI provider'ın down olduğunu tespit eder ve açılır.
2. Ingestion, classification (kural bazlı kısım), incident creation **normal şekilde çalışmaya devam eder** — bunlar AI'a bağımlı değildir. Event kaybı olmaz.
3. `AIAnalysis` adımı, circuit açıkken denenmez; incident kaydı `AiAnalysisStatus: Pending` olarak işaretlenir (kuyruğa alınır, silinmez).
4. Dashboard'da ilgili incident'larda "AI analizi şu anda kullanılamıyor, otomatik olarak yeniden denenecek" bildirimi gösterilir — kullanıcı boşlukta bırakılmaz.
5. Circuit half-open'a geçip provider'ın geri geldiğini doğruladığında, bekleyen (`Pending`) incident'lar bir backlog job'ı ile kuyruktan işlenir (öncelik: en yeni önce veya FIFO — ürün kararı, MVP'de FIFO yeterli).
6. Sistem içi bir health/status göstergesi (dashboard üstünde banner: "AI Analysis: Degraded") kullanıcıya şeffaf şekilde durumu bildirir — sessizce başarısız olmak yerine.
7. Belirli bir süre (örn. 15 dakika) sonra hâlâ down ise, operasyon ekibine (kendi iç Slack/e-posta alert'i, bkz. 15.3) otomatik bildirim gider.

**Bu, ürünün kendi felsefesini kanıtlar:** "Evidence-only, insan review'a düşür, asla sessizce yanlış/eksik bilgi üretme" prensibi AI provider kesintisinde de korunur — sistem AI'sız da temel işlevini (event/incident yönetimi) sürdürür, sadece AI-destekli zenginleştirmeyi ertelemiş olur.

### 15.2 Diğer kritik senaryolar (kısa)

- **PostgreSQL erişilemez:** Readiness false döner, ingestion endpoint 503 döner (Hangfire de yeni job almaz). Bu tam bir kesintidir — RPO/RTO planı (Bölüm 14) devreye girer. Müşteriye giden webhook/API üzerinden 503 + `Retry-After` döndürülür (istemcinin retry etmesi beklenir, çoğu webhook gönderen zaten retry mantığına sahiptir).
- **Redis erişilemez:** Rate limiting ve cache in-memory fallback'e düşer (tek instance'da sorun değil, çoklu instance'da rate limit tutarlılığı geçici olarak zayıflar ama sistem çalışmaya devam eder — "fail open" tercih edilir, "fail closed" ingestion'ı tamamen durdurmaktan daha az zararlıdır).
- **Job queue backlog'u büyüyor (Hangfire):** `hangfire.jobs.queue_depth` metriği eşik aşınca (örn. >1000) alert tetiklenir; worker sayısı yatay ölçeklenebilir (MVP'de manuel müdahale, post-MVP'de otomatik scaling).

### 15.3 Operasyonel alerting (MVP seviyesinde basit)

- Kritik eşikler (circuit breaker açıldı, readiness sürekli false, job backlog eşik üstü, error rate ani artış) için basit eşik tabanlı alert — e-posta veya Slack webhook'u yeterli (MVP'de PagerDuty gibi tam bir on-call aracı aşırı mühendisliktir, tek kişilik/küçük ekipte e-posta+Slack yeterli başlangıçtır).
- Her alert, ilgili `CorrelationId`/`TraceId` ve dashboard'daki ilgili görünümün linkini içerir — kendi observability altyapımızı kendi incident response'umuzda da kullanırız (dogfooding).

**Post-MVP:** Tam bir on-call rotasyonu ve PagerDuty/Opsgenie entegrasyonu, otomatik runbook tetikleme, çoklu AI provider fallback (birincil down olunca otomatik ikincil provider'a geçiş), status page (public, müşteriye açık uptime/incident sayfası).

---

## 16. Özet — MVP'de Yapılacaklar vs Post-MVP Ertelenecekler

**MVP'de mutlaka olmalı:**
- API key hash'leme (HMAC-SHA256 + pepper), revoke akışı, per-key rate limit
- İki aşamalı PII redaction (log öncesi + AI öncesi), field-based + regex tamamlayıcı
- Tenant-scoped query disiplini (mümkünse PostgreSQL RLS)
- Serilog JSON structured logging + zorunlu alan seti (CorrelationId, TenantId vb.)
- CorrelationId middleware → job taşıma zinciri
- OpenTelemetry temel span hiyerarşisi + AI/job attribute'ları
- Temel metrik seti (ingestion, classification, AI latency/token/cost/parse-failure, job queue)
- Liveness/readiness health check (PostgreSQL zorunlu, AI provider zorunlu değil)
- Polly ile AI provider ve webhook için timeout/retry/circuit breaker
- Audit log (incident resolve/reopen, key revoke, ayar değişiklikleri)
- Günlük PostgreSQL backup + aylık restore testi
- AI provider down senaryosu için degrade mode (sessizce başarısız olmama)

**Post-MVP'ye ertelenebilir:**
- Key scope'ları, otomatik leaked-key taraması
- ML tabanlı PII tespiti, format-preserving encryption
- Tail-based trace sampling, W3C trace context'in AI provider'a iletilmesi
- Anomaly-based alerting, tam SIEM entegrasyonu
- Azure Key Vault / managed identity ile secret-less auth
- Çoklu AI provider fallback, bulkhead isolation
- Cross-region backup, otomatik disaster recovery
- Tam on-call rotasyonu (PagerDuty vb.), public status page
- Audit log'un tamper-evident hash zinciri ile ayrı depoya export'u
