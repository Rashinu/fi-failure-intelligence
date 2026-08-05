# M20.2 — Demo Readiness & Security Hardening

> Kapsam: `LIVE_THREE_PERSPECTIVE_TEST.md`'de tespit edilen yalnızca 2 bulguyu kapatmak. Yeni
> feature, mimari değişiklik, refactor yok.

## P0-1 — Webhook Admission Policy

**Bulgu (canlıda doğrulanmıştı):** `WebhooksController.IngestEvent` imzasız/geçersiz imzalı
istekleri kabul edip (HTTP 201) `SIGNATURE_ERROR` kategorisiyle gerçek bir incident'a
dönüştürüyordu — bu, dashboard-spam riski taşıyordu. Bu davranış aslında **bilinçli, dokümante
edilmiş bir M3-dönemi tasarım kararıydı** (`IIntegrationConnector.VerifySignature` XML doc'u:
"Doğrulama başarısızsa event reddedilmez") — kod bir hata değil, artık geçersiz olan bir karardı.

**Karar: 401.** Üç seçenek arasında (401/403/400) 401 seçildi çünkü bu, codebase'in KENDİ İÇİNDE
zaten kurulu olan tutarlı bir desen: `ApiKeyAuthMiddleware` (X-Api-Key eksik/geçersiz → 401),
`AdminBasicAuthMiddleware` (→ 401), ve aynı controller'daki **kardeş action**
`IngestDeployment`'ın **zaten** kullandığı davranış (→ 401, imza doğrulanamazsa). Yani bu bir yeni
konvansiyon icat etmek değil, `IngestEvent`'i zaten var olan `IngestDeployment`'ın desenine
getirmekti — en dar, en tutarlı düzeltme.

**Uygulama:** `WebhooksController.IngestEvent`'te imza kontrolü artık `Normalize()`/event
oluşturmadan ÖNCE yapılıyor; başarısızsa `Unauthorized(...)` dönülüyor, **hiçbir
IntegrationEvent/Incident hiç oluşturulmuyor**. Idempotency/dedup kontrolü yalnızca doğrulanmış
istekler için, değişmeden çalışmaya devam ediyor — meşru sağlayıcı retry'ları etkilenmedi.
`IIntegrationConnector.VerifySignature`'ın artık-yanlış olan XML doc'u güncellendi.

**Requirements karşılandı:**
- Unauthenticated request → asla incident olmuyor ✅ (event hiç DB'ye yazılmıyor).
- Dashboard pollution imkansız ✅ (yukarıdakiyle aynı nedenle).
- Replay davranışı korundu ✅ (geçerli+tekrarlanan event hâlâ dedupe ediliyor, 200 dönüyor).
- Meşru retry zayıflatılmadı ✅ (yalnızca DOĞRULANAMAYAN istekler reddediliyor).

## P0-2 — Demo Experience

**Bulgu (canlıda doğrulanmıştı):** Landing page'in her iki (üç, gerçekte) "Canlı demoyu incele"
linki de doğrudan authenticated dashboard'a gidiyordu — kimliksiz bir ziyaretçi yalnızca 401
görüyordu, Golden Incident'ı hiç göremiyordu.

**Karar: STATIC INCIDENT** (verilen 5 seçenekten). Gerekçe:
- **Demo tenant**: çoklu-kiracılık gerektirir — bu görevin ve önceki M19/M20 kararlarının açıkça
  "asla yapılmayacaklar" listesinde.
- **Read-only demo / Demo snapshot / Anonymous incident**: hepsi bir şekilde canlı DB'ye
  dokunmayı gerektirir (gerçek bir incident'ı "public" işaretlemek, veya auth middleware'inde bir
  istisna açmak) — bu, gelecekte yanlışlıkla gerçek/farklı veri sızdırma riski taşıyan bir yüzey
  ekler (bir bayrak/config yanlış ayarlanırsa canlı veri açığa çıkabilir).
- **Static Incident** (seçilen): yeni bir Razor Page (`/Demo/GoldenIncident`), **hiçbir
  `DbContext` enjekte etmiyor, hiçbir sorgu çalıştırmıyor, hiçbir `<form>`/POST handler'ı yok**.
  `AdminBasicAuthMiddleware`'in korumalı prefix listesinde (`/Incidents`, `/hangfire`,
  `/api/v1/...`) olmadığı için kimlik doğrulaması hiç devreye girmiyor — bu bir "izin verilen
  istisna" değil, yapısal olarak farklı bir route. Veri sızıntısı/yıkıcı eylem riski **yapısal
  olarak sıfır** çünkü sızdırılacak/değiştirilecek hiçbir canlı state yok.

**Veri dürüstlüğü:** Sayfadaki tüm sayılar (43 event / 12 operasyon / 7 müşteri, evidence,
timeline, fingerprint) bu projenin M19/M20.1 sırasında **gerçekten çalıştırılmış** Golden
Incident senaryosundan birebir alındı — hiçbir sayı uydurulmadı. AI analiz bölümü kasıtlı olarak
"NeedsHumanReview" gösteriyor çünkü gerçek çalıştırmada da (API key yapılandırılmadan) tam olarak
bu gerçekleşti — ürünün "kanıt yetersizse AI hiç çağrılmaz" iddiasını kanıtlayan gerçek bir örnek,
pazarlama amaçlı uydurma bir AI çıktısı değil.

**Landing page:** `landing/index.html`'deki 3 CTA linki de (nav + hero + alt) artık
`/Demo/GoldenIncident`'a gidiyor (Vercel'e yeniden deploy edilecek).

**Requirements karşılandı:**
- Admin secret gerekmiyor ✅ (route middleware kapsamı dışında, canlı doğrulanacak).
- Production data açığa çıkmıyor ✅ (sıfır DB erişimi).
- Yıkıcı eylem yok ✅ (form/POST handler yok).
- Golden Incident tam görünür ✅ (gerçek sayılar, gerçek evidence, gerçek timeline).

## Test Sonuçları

**P0-1 (`StripeWebhookIngestionTests`, 7 test):** valid signature (mevcut, değişmedi) + invalid
signature → 401/0 event + missing signature → 401/0 event + expired timestamp → 401/0 event +
geçerli+tekrarlanan event → hâlâ dedupe/200 + malformed JSON+geçersiz imza → 401 (ham 500 değil) +
büyük payload+geçersiz imza → hâlâ 401 (boyut bir bypass yaratmıyor). **7/7 yeşil.**

**P0-2 (`GoldenIncidentDemoPageTests`, 4 test):** kimliksiz erişim → 200 + doğru sayılar sayfada
var + `<form` hiç yok + POST denemesi başarısız oluyor. **4/4 yeşil.**

**Tam regresyon:** Domain 164/164 değişmedi. Integration: 23 sınıf (22 mevcut + 1 yeni:
`Demo.GoldenIncidentDemoPageTests`), tamamı yeşil — sonuçlar aşağıda ayrıca raporlanıyor.

## Sonuç

**Webhook Decision:** 401, imza kontrolü event oluşturulmadan önce, mevcut `IngestDeployment`
deseniyle tutarlı.

**Demo Decision:** Static Incident — `/Demo/GoldenIncident`, sıfır DB erişimi, sıfır form,
kimlik doğrulama kapsamı dışında.

**Security Result:** Dashboard-pollution vektörü kapatıldı; canlı üç-perspektif testte bulunan
diğer tüm savunmalar (auth gate'ler, Cloudflare WAF, secret guard, ProblemDetails) dokunulmadan
korundu.

**Demo Result:** Golden Incident artık kimliksiz herhangi bir ziyaretçiye tam olarak görünür;
landing page CTA'ları gerçek hedefe işaret ediyor.

**Engineering Decision:** A

**Ready For Design Partner:** YES
