# M19 — Close the Product Loop

> Bu doküman, bir önceki bağımsız "Product Reality Audit"in bulduğu üç P0 ürün açığını
> (Business Operation Identity, Incident Resolution, AI Trust Calibration) kapatan M19
> çalışmasını belgeliyor. Ayrıntılı, satır satır bir implementasyon günlüğü değil —
> **neden**, **ne**, **ne yapılmadı** ve **nasıl doğrulandı** sorularına cevap veriyor.

## Neden M19 Var

Önceki audit'in tespiti: **"B — Doğru motor, eksik ürün."** Ürün Tezi Kapsamı tahmini: **%39**.

Motor (deterministik sınıflandırma, fingerprinting, severity, idempotency, transactional outbox,
concurrency, çok-kaynaklı evidence, redaction, AI pipeline, prompt eval) zaten güçlü ve
yeniden inşa edilmedi. Üç somut P0 açık vardı:

1. **Business Operation** — FI yalnızca `IntegrationEvent → Incident` modelliyordu; "43 event"
   ile "43 iş operasyonu"nu ayırt edemiyordu.
2. **Incident Resolution** — `IncidentStatus.Resolved` var ama hiçbir kod yolu bir incident'ı
   gerçekten bu duruma taşımıyordu.
3. **AI Trust Calibration** — Grounding kontrolü (`CheckGrounding`) gerçek Claude Haiku'ya karşı
   0.100 skorladı (hedef 0.85); basit substring eşleşmesinin, geçerli paraphrase'leri
   yanlışlıkla reddettiği hipotezi vardı.

## Neyin Uygulanmadığı (Bilinçli Olarak)

Prompt'un 16. bölümünde donmuş liste birebir uygulandı — dokunulmadı: yeni observability
altyapısı, ek OTLP işi, multi-tenancy, enterprise RBAC, organizasyon/workspace modeli, workflow
builder, otomatik remediation, otonom replay/retry/reconciliation motoru, silent-failure
detection, agent platformu, yeni genel analytics, çok sayıda yeni connector, prompt A/B
otomasyonu (doğruluk için gerekli olanın ötesinde).

Ayrıca:
- **Silent Failure** (Bölüm 17) — yalnızca bir hipotez dokümanı yazıldı (`SILENT_FAILURE_HYPOTHESIS.md`),
  hiçbir kod değişikliği yapılmadı.
- **Agent Tool Call Failures** (Bölüm 18) — yalnızca stratejik bir uzantı notu yazıldı
  (`AGENT_FAILURE_EXTENSION.md`), hiçbir kod değişikliği yapılmadı.
- **PB5 (gerçek connector)**, **PB7'nin ötesinde AI araştırması**, **PB8 (insan-skorlu eval)**,
  **PB10 (outbound alerting)** — hepsi M20 (Design Partner Validation) sinyaline veya gerçek
  API maliyetine bağlı olarak ertelendi.

---

## P0-A: Business Operation Identity

### Domain Değişikliği

`IntegrationEvent`'e üç yeni, nullable alan eklendi:

```csharp
public string? OperationRef { get; }        // "payment-sync-74921"
public string? OperationType { get; }        // "PaymentType" (görüntüleme amaçlı)
public string? BusinessRecordRef { get; }    // "subscription-18372"
```

`AffectedCustomerRef` (M18) ile kavramsal olarak AYRI tutuldu — bir müşteri birden fazla
operasyona sahip olabilir, bir operasyon birden fazla event üretebilir.

### Ingestion

- Generic ingestion (`POST /api/v1/events`): `IngestEventRequest`'e 3 opsiyonel trailing alan
  eklendi (M18'deki `CustomerRef` ile aynı additive-parameter deseni).
- `StripeConnector`: mock payload'ın `data.object.metadata.{operation_ref,operation_type,
  business_record_ref}` alanlarını çıkarır — gerçek Stripe'ın arbitrary key-value `metadata`
  konvansiyonunu taklit eder. Diğer connector'lara (GitHub Deployments, Email) DOKUNULMADI.

### Impact Semantics (Preflight Constraint 1)

`IncidentDetailResponse` artık üç ayrı, dürüst alan taşıyor:

| Alan | Anlamı |
|---|---|
| `EventCount` | Incident'a bağlı (IncidentId FK'sı ile) TÜM teknik event sayısı |
| `KnownOperationCount` | `COUNT(DISTINCT OperationRef)` — yalnızca OperationRef taşıyan event'lerden |
| `OperationCoverage` | `"None"` \| `"Partial"` \| `"Complete"` |

Kural: **null OperationRef = "bilinmiyor", "sıfır operasyon" değil.** Eğer hiçbir event
OperationRef taşımıyorsa `KnownOperationCount=null`, `OperationCoverage="None"`. Eğer bazıları
taşıyorsa (`0 < eventsWithField < totalEvents`) `OperationCoverage="Partial"` — API/UI bunu
**asla** "toplam etki" gibi sunmaz, yalnızca bilinen alt küme olarak gösterir. Aynı dürüstlük
ilkesi müşteri etkisine de uygulandı (`CustomerCoverage` eklendi, aynı üç değerli semantik).

Canlı doğrulandı (bkz. Test Sonuçları): 5 event'ten 3'ü OperationRef taşıdığında
`KnownOperationCount=2` (2 distinct operasyon), `OperationCoverage="Partial"` döndü — 3 değil,
çünkü 3 "kaç event operasyon taşıyor" sorusuna cevap, 2 ise "kaç DISTINCT operasyon" sorusuna
cevap.

### Index Kararı (Preflight Constraint 2)

**`OperationRef` için ayrı bir index EKLENMEDİ.** Ana projeksiyon sorgusu
`WHERE incident_id = ? GROUP BY ... COUNT(DISTINCT operation_ref)` şeklinde. `IncidentId` zaten
indeksli (TD3, önceki oturum) ve son derece seçici — bir incident'a ait event sayısı tipik
olarak birkaç düzine ile birkaç yüz arasında (golden incident'ta 43), sıralı taramayı
gerektirmeyecek kadar küçük bir satır kümesi. Bu ölçekte composite bir `(IncidentId,
OperationRef)` index'inin sorgu tarafında kazandıracağı şey marjinal — ama her
`ClassifyJobHandler` event-incident ataması (UPDATE) için ek yazma maliyeti kesin. Karar:
mevcut `IncidentId` index'i yeniden kullanıldı, yeni index eklenmedi (bkz.
`IntegrationEventConfiguration.cs` içindeki kod yorumu).

### Dashboard Listesi

Operasyon/müşteri sütunları **dashboard liste sayfasına eklenmedi** — 100 incident'a kadar
sayfalanan bir listede her satır için ek bir gruplu sorum gerektirirdi (N+1 riski). Prompt'un
kendi Bölüm 15 uyarısı ("expensive queries'e review olmadan bağımlı yapma") gereği bilinçli
olarak ertelendi; yalnızca Detail sayfasında gösteriliyor.

---

## P0-B: Incident Resolution Lifecycle

### Mevcut Semantiklerin İncelenmesi (Preflight Constraint 3)

Kodda inceleme: `ResolvedAt`/`ResolutionSource` zaten vardı ama hiç set edilmiyordu.
`docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md` satır 518'de `resolution_source` alanının orijinal
tasarım niyeti bulundu: `HUMAN_MANUAL | AUTO_SILENCE | AI_APPROVED` — yani **kategorik bir
"hangi mekanizma" alanı**, "kim" veya "neden" değil. Bu yüzden `ResolvedBy` (aktör etiketi) ve
`ResolutionNote` (serbest metin) **YENİ, AYRI** alanlar olarak eklendi — `ResolutionSource`
yanlış kullanılmadı.

### `Incident.Resolve(resolvedBy, resolutionNote)`

```csharp
public void Resolve(string? resolvedBy, string? resolutionNote)
{
    if (!IsActive)
        throw new InvalidOperationException($"'{Status}' durumundaki bir incident resolve edilemez.");
    Status = IncidentStatus.Resolved;
    ResolvedAt = DateTimeOffset.UtcNow;
    ResolutionSource = "HUMAN_MANUAL";
    ResolvedBy = resolvedBy;
    ResolutionNote = resolutionNote;
}
```

- Yalnızca aktif (`IsActive`) bir incident resolve edilebilir; zaten Resolved/Ignored bir
  incident'ı tekrar resolve etmek `InvalidOperationException` fırlatır (API bunu 409'a çevirir).
- `Reopen()` ve `ResetAsNewOccurrence()`, `ResolvedAt`'i temizlediği gibi artık `ResolvedBy`/
  `ResolutionNote`'u da temizliyor (bir önceki resolve'un "kim/not"u, reopen sonrası geçersiz).

### "14:20'de Resolve, 14:22'de Eşleşen Failure" Senaryosu

**Paralel bir recovery lifecycle İCAT EDİLMEDİ.** `ClassifyJobHandler`'ın MEVCUT
`existingIncident.IsWithinReopenCooldown(now)` dalı (zaten `ResolvedAt`'e bakıyor, 30 dakikalık
cooldown) otomatik olarak `Reopen()`'a yönlendiriyor — `Resolve()` yalnızca `ResolvedAt`'i doğru
set ederek bu MEVCUT mekanizmayı tetikliyor. Canlı ve testle doğrulandı (bkz. Test Sonuçları):
resolve edilen bir incident'a cooldown içinde yeni bir eşleşen event geldiğinde, `Status`
otomatik olarak `Reopened` oluyor, `ReopenCount` artıyor, resolution metadata'sı temizleniyor.

### API

`POST /api/v1/incidents/{id}/resolve` — mevcut `AdminBasicAuthMiddleware`'in zaten koruduğu
`/api/v1/incidents` önekinin altında (yeni middleware/auth kodu YAZILMADI). Body:
`{ "resolvedBy": "...", "note": "..." }` (ikisi de opsiyonel). Doğrulama: incident yoksa 404,
geçersiz geçiş (zaten resolved/ignored) ise 409, başarılıysa güncel `IncidentDetailResponse`.
Her resolve, `AuditLog`'a `INCIDENT_RESOLVED` action'ıyla yazılıyor (auditability).

### UI

Incident Detail sayfası, aktif bir incident için bir "Resolve Incident" formu (isim + not,
ikisi de opsiyonel), resolved bir incident için "Resolved / Resolved at / Resolved by /
Resolution note" kartını gösterir. Reopen zaten var olan `fi-reopen-banner` (önceki oturumdan,
PB6) ile birlikte çalışır — resolve edilip sonra reopen edilen bir incident hem "Resolved" kartını
KAYBEDER (artık aktif) hem de reopen banner'ını gösterir.

---

## P0-C: AI Trust Calibration (Grounding)

### Teşhis (Doğrulama Öncesi)

Gerçek testlerle (`AiAnalysisValidatorTests`), **düzeltmeden önce**, `CheckGrounding`'in mevcut
(substring-only) haline karşı çalıştırılan iki somut senaryo, İKİ gerçek false-positive ortaya
çıkardı:

1. **Model, kendisine zaten verilmiş deterministik bağlamı (kategori adı: "AuthenticationError")
   doğru şekilde tekrar ettiğinde bile "desteklenmeyen iddia" sayılıyordu** — çünkü bu alan
   evidence metninde değil, deterministik input'ta yaşıyordu ve kontrol yalnızca evidence'a
   bakıyordu.
2. **Entity adının noktalama/boşluk farkıyla yeniden biçimlendirilmesi**
   ("Stripe Payments (Prod)" evidence'da, model "StripePaymentsProd" diyor) birebir substring
   eşleşmesini bozuyordu.

Bu iki test, düzeltmeden ÖNCE gerçekten FAIL etti (canlı doğrulandı, varsayılmadı) —
`RootCauseWithDirectSupportedClaim_IsGrounded` ve
`RootCauseWithReformattedEntityName_IsNotFalselyFlaggedAsUnsupported`.

Sayısal iddialar (uydurulmuş rakamlar) **zaten doğru yakalanıyordu** — bu katmana dokunulmadı.

### Düzeltme (Katmanlı, Gevşetilmemiş)

`CheckGrounding` iki katman kazandı:

1. **Genişletilmiş corpus**: evidence özetlerinin yanına deterministik `Category`,
   `AffectedIntegration`, `Severity` de eklendi — model'in zaten bildiği bağlamı doğru tekrar
   etmesi artık "iddia" sayılmıyor.
2. **Normalize edilmiş karşılaştırma**: karşılaştırma öncesi hem candidate token hem corpus'tan
   noktalama/boşluk (`[^a-zA-Z0-9]`) temizleniyor — "Stripe Payments (Prod)" ve
   "StripePaymentsProd" artık aynı normalize forma iniyor.

**Sayısal kontrol KASITLI OLARAK gevşetilmedi** — evidence'da olmayan bir rakam hâlâ flag'leniyor
(bkz. `RootCauseWithUngroundedNumber_StillFlagged_NormalizationDoesNotLoosenNumericCheck` testi).
**Hiçbir skor uydurulmadı** — bu değişiklik gerçek bir Anthropic API çağrısı gerektirmedi,
tamamen deterministik test senaryolarıyla doğrulandı.

### Prompt Bootstrap (Bölüm 12)

Doğrulandı: `Program.cs`'in `--migrate` modu, ilk aktif prompt'u `PromptVersion.CreateActive(...)`
ile doğrudan ekliyor — `PromptPromotionGate`'i atlıyor. Bu **doğru ve kasıtlı** bir bootstrap
istisnası (sistem bir yerden başlamalı, henüz karşılaştırılacak bir baseline yok) — ama
belgelenmemişti. **Seçilen çözüm: Bölüm 12 Seçenek A.** `CreateActive`'e açık bir "YALNIZCA
bootstrap içindir" XML doc'u eklendi; `EvalOverallAverage`/`EvaluatedAt` bilerek null bırakılıyor
(sahte bir skor asla üretilmiyor — zaten önceden de üretilmiyordu, ama şimdi bu kasıtlı olarak
test'le de doğrulanıyor: `CreateActive_NeverFakesAnEvalScore`). Bootstrap sonrası TÜM versiyonlar
`CreateDraft` + gerçek `PromptVersionsController.Promote` (golden dataset gate) akışından geçmek
ZORUNDA — bu zaten böyleydi, yalnızca ilk bootstrap istisnasının kasıtlı olduğu netleştirildi.

---

## Golden Incident: PaymentSync Credential Failure

`scripts/seed-demo-data.sh`'a 4. senaryo olarak eklendi. Gerçek Docker Compose'da (fresh
migration'dan itibaren) uçtan uca canlı doğrulandı:

- **43 teknik event**, **12 PaymentSync operasyonu**, **7 müşteri** — API/UI'da birebir
  gösterildi (`eventCount=43`, `knownOperationCount=12`, `operationCoverage="Complete"`,
  `affectedCustomerCount=7`, `customerCoverage="Complete"`).
- ConfigChange evidence'ı (credential rotasyonu) doğru şekilde ilişkilendirildi.
- Deterministik suggested action ("API key'in geçerliliğini... kontrol edin") anında göründü.
- AI analizi dürüst bir "henüz yok" durumu gösterdi (bu ortamda gerçek bir Anthropic key
  yapılandırılmadığı için — uydurulmuş bir sonuç YOK).
- **Resolve Incident** formu dolduruldu ("murat" / "Credential rotated back, verified with
  provider.") ve gönderildi — sayfa `Resolved` durumuna, doğru `ResolvedAt`/`ResolvedBy`/
  `ResolutionNote` ile geçti, timeline'a "Incident resolve edildi" satırı eklendi.

---

## Test Sonuçları

| Katman | Önce | Sonra | Not |
|---|---|---|---|
| Domain (`FI.Domain.Tests`) | 150 | 164 | 14 yeni: operation fields (2), Resolve lifecycle (7), grounding diagnosis+fix (5) |
| Integration (`FI.Integration.Tests`, 18 sınıf) | 85 | 96 | 11 yeni: operation coverage (3), Stripe metadata (3), Resolve API + reopen-after-resolve (5, yeni `IncidentResolutionTests` sınıfı) |
| Migration | - | ✅ | Fresh DB'ye karşı (`docker compose down -v && up --build`) doğrulandı |
| Docker Compose (golden incident) | - | ✅ | Canlı, ekran görüntüleriyle doğrulandı (bkz. yukarı) |

Regresyon: mevcut hiçbir test zayıflatılmadı/silinmedi. Tüm domain + integration testleri
(sınıf-başına-ayrı-process, CI'nın kullandığı yöntem) geçti.

## Bilinen Sınırlamalar

- **Contradiction detection yok** — `CheckGrounding`, bir iddianın evidence'ı AÇIKÇA
  ÇELİŞTİRDİĞİNİ (ör. "hiçbir config değişmedi" derken evidence'da bir ConfigChange varsa)
  tespit etmiyor; yalnızca yeni/desteklenmeyen entity'leri yakalıyor. Bu, kapsamlı bir NLI
  (natural language inference) katmanı gerektirir — M19'un "AI'ı açık uçlu bir araştırma
  projesine dönüştürme" sınırının dışında bırakıldı.
- **Sözle ifade edilen sayılar** ("elli" yerine "50") hâlâ sayısal regex tarafından
  yakalanmıyor — bu bilinen bir kör nokta, kasıtlı olarak gevşetilmedi/genişletilmedi (yanlış
  bir düzeltmeyle sayısal kontrolü zayıflatma riski, doğru bir düzeltmeden daha kötü olurdu).
- **Gerçek Claude Haiku'ya karşı yeniden ölçüm yapılmadı** — bu düzeltme maliyetli bir gerçek
  API çağrısı gerektirmeden, deterministik test senaryolarıyla doğrulandı. 0.726→X gibi yeni bir
  skor **uydurulmadı**. Gerçek bir yeniden ölçüm, M20'nin (veya ayrı bir kararın) kapsamında.
- **Dashboard listesinde operasyon/müşteri sütunu yok** (yalnızca Detail'de) — kasıtlı, N+1
  riskinden kaçınmak için.

## Kalan Ürün Açıkları (M20 Öncesi)

- PB5 (gerçek connector), PB7'nin ötesinde AI kalitesi çalışması, PB8 (insan-skorlu eval),
  PB10 (outbound alerting) — hepsi gerçek kullanıcı sinyaline (M20) veya ek maliyete bağlı.
- Silent Failure ve Agent Tool Call Failure — yalnızca hipotez/strateji dokümanları var, kod yok.

## Sonraki Adım

**M20 — Design Partner Validation.** Kod tarafında yapılacak yeni bir şey yok; 5-10 gerçek
kullanıcıyla (entegrasyon geliştiricisi, support mühendisi, otomasyon danışmanı) görüşülüp
Bölüm 26'daki 6 soru sorulmalı. M21 yalnızca bu sonuçlara göre kararlaştırılmalı.
