# CTO Review & Roadmap — Değerlendirme ve Güncellenmiş Aksiyon Planı

> Bu doküman, ekten gelen `FI_Failure_Intelligence_CTO_Review_Roadmap.md` incelemesini
> (`Executive Summary`, `Faz 1-3`, `SaaS Roadmap`, `M14`/`M15`) gerçek kod tabanının **şu anki**
> durumuyla (M1-M15 tamamlanmış, bkz. `FI/README.md`) karşılaştırır. Amaç: incelemenin doğru
> tespitlerini onaylamak, artık geçerli olmayan/eksik bilgiyle yapılmış varsayımları düzeltmek,
> ve gerçekçi, önceliklendirilmiş bir sonraki-adım planı çıkarmak.

## 1. Genel Değerlendirme: İnceleme Doğru mu?

**Kısa cevap: Teşhis %90 doğru.** İnceleme, projenin en gerçek ve en acil sorununu isabetle
tespit etmiş: **güçlü bir motor var, ama sıfırdan bakan biri için "ürün" yok.** Bu repo şu anda
yalnızca bir Swagger sayfası ve JSON API'den ibaret — hiçbir dashboard, incident detay ekranı,
timeline, "AI ne dedi" görünümü yok. Bir işe alım görevlisi, potansiyel müşteri veya yatırımcı
repoyu açtığında gördüğü şey "çok sayıda C# dosyası ve etkileyici bir mimari doküman" — ürünün
gerçekte ne yaptığını **görerek** anlayamıyor. Bu, incelemenin "Faz 1 — Product Proof" önceliği
için güçlü bir gerekçe.

Ancak inceleme birkaç noktada ya eksik bilgiyle ya da modası geçmiş bir kod tabanı görüntüsüyle
yazılmış — aşağıda madde madde düzeltiyorum, çünkü bunlar planlamayı doğrudan etkiliyor.

## 2. Düzeltmeler: İncelemenin Yanıldığı/Eksik Bıraktığı Noktalar

### 2.1 "Config Change Correlation" — zaten var, backend'de tamamlanmış

İnceleme Faz 3'te "API Key/Secret/Endpoint değişikliklerini otomatik ilişkilendir" diye bir
özellik öneriyor. **Bu zaten yapıldı** (M10, `docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md` Bölüm
23 — `CONFIG_CHANGE` evidence kaynağı): `AuditLog` her API key/webhook secret rotasyonunu ve
`endpointUrl` değişikliğini kaydediyor; `EvidenceCollectorJobHandler`, incident'ın `firstSeen`
zamanına göre -6 saat penceresinde bu kayıtları otomatik evidence'a ekliyor. Bu, **gerçek bir
Docker Compose ortamında canlı olarak da doğrulandı** (M15): bir API key rotasyonundan hemen
sonra oluşan bir incident, evidence listesinde "API key rotated for integration 'X' 0 minute(s)
before first failure" satırını doğru şekilde gösterdi.

**Sonuç:** Bu madde Faz 3'ten çıkarılmalı — eksik olan şey backend mantığı değil, bunu
**görselleştirecek bir UI**. Bu, tam olarak Faz 1'in kapsamına giriyor.

### 2.2 "Similar Incidents" — zaten var (HISTORICAL_INCIDENT evidence kaynağı)

Aynı şekilde, "geçmiş benzer olayları göster" önerisi zaten backend'de karşılanıyor:
`HISTORICAL_INCIDENT` evidence kaynağı, son 90 gün içinde aynı kategoriye sahip çözülmüş
incident'ları (max 5) otomatik topluyor. Eksik olan yine UI, backend değil.

### 2.3 "Business Impact" metrikleri — kısmen var, "kaç müşteri etkilendi" gerçekten eksik

`Incident.EventCount`, `FirstSeen`, `LastSeen` zaten model üzerinde var ve API'de dönüyor —
"kaç event başarısız oldu", "ilk/son hata zamanı" sorularının cevabı zaten mevcut, sadece
dashboard'da gösterilmiyor. **Ama "kaç müşteri/kullanıcı etkilendi" gerçekten eksik** — sistemde
şu an bir `IntegrationEvent`'in hangi son-kullanıcıya/müşteriye ait olduğunu tutan bir alan yok
(bu, mimari dokümanın kendi kapsamı dışında bıraktığı bir konu, bkz. Bölüm 8). Bu, incelemenin
doğru tespit ettiği gerçek bir backend eksiği — ama Faz 1'in değil, Faz 3'ün kapsamında kalmalı,
çünkü connector'ların her provider'a özgü "affected customer" alanını normalize etmesini
gerektirir (şu anki `NormalizedEvent` bunu taşımıyor).

### 2.4 "Suggested Action" — kısmen var ama deterministik değil

Şu an `recommendedActions` alanı **AI tarafından serbest metin olarak** üretiliyor (evidence'a
dayalı, ama deterministik bir kural motorundan gelmiyor). İncelemenin önerdiği "401→API key
kontrol et, 429→Retry-After" gibi **deterministik, kategoriye bağlı sabit öneriler** şu an yok.
Bu gerçek ve ucuz bir kazanç: `EventCategory` zaten 11 kategoriyi kapsıyor
(`FI.Domain.Classification.EventCategory`), her biri için 1-2 satırlık sabit bir öneri metni
eklemek yarım günlük bir iş ve AI çağrısı beklemeden anında görünür bir "action" verir.

### 2.5 Program.cs, Polly, migration stratejisi, OTel exporter — hepsi doğru tespit

Bunları doğruladım, hepsi doğru:
- `Program.cs` 146 satır, servis kayıtları extension method'lara bölünmemiş.
- Projede hiçbir yerde Polly (retry/circuit breaker/timeout) kullanılmıyor —
  `AnthropicMessagesClient` çıplak bir `HttpClient.SendAsync` çağrısı yapıyor.
- Migration'lar **startup'ta** (`db.Database.Migrate()`, `Program.cs:92`) uygulanıyor — birden
  fazla replica ile prod'da race condition riski var (deployment-time migration'a taşınmalı).
- OpenTelemetry yalnızca konsol exporter kullanıyor, OTLP/Jaeger/Tempo yok.
- Webhook secret **düz metin** saklanıyor (`Integration.WebhookSecret`, KMS/Data Protection'a
  taşınması zaten kod içinde XML doc yorumuyla not edilmiş bir "sonraki adım").

Bunların hepsi Faz 2 kapsamında doğru sıralanmış; ekleyecek bir şeyim yok.

### 2.6 M14/M15 numaralandırma çakışması

İncelemenin önerdiği "M14 — Product Proof" ve "M15 — Customer Validation" isimleri, kod
tabanındaki **gerçek** M14 (golden dataset'in gerçek Claude Haiku'ya karşı çalıştırılması) ve M15
(gerçek Docker Compose E2E testi + kritik concurrency bug düzeltmesi) ile çakışıyor — bu doküman
muhtemelen bu iki milestone tamamlanmadan önce yazılmış. **Aşağıdaki plan M16'dan başlıyor.**

## 3. Güncellenmiş Öncelik Sırası

İncelemenin "Engine → Product → Customer Validation → SaaS" sıralaması doğru ve korunmalı.
Aşağıda bunu, düzeltmelerle birlikte somut bir milestone planına çeviriyorum.

### M16 — Production Readiness (Faz 2) — ✅ TAMAMLANDI

Bu bölümde önerilen 5 madde de tamamlandı ve gerçek bir Docker Compose ortamında doğrulandı —
ayrıntı için `FI/README.md`'deki M16 notuna bakın:
1. ~~`Program.cs`'i servis kayıtları için extension method'lara böl~~ — `FI.Api/Extensions/`
   altında 5 extension method.
2. ~~`AnthropicMessagesClient`'a Polly retry + circuit breaker + timeout ekle~~ —
   `Microsoft.Extensions.Http.Resilience` ile standart resilience handler.
3. ~~Webhook secret'ı KMS/Data Protection ile şifreleyerek sakla~~ — ASP.NET Core Data
   Protection, anahtarlar `FiDbContext` üzerinden kalıcı.
4. ~~Migration'ı startup'tan ayrı bir deployment adımına taşı~~ — `--migrate` modu +
   `docker-compose.yml`'de ayrı `fi-migrate` servisi.
5. ~~OTLP exporter ekle~~ — `Otel:OtlpEndpoint` yapılandırılırsa devreye giriyor.

### M17 — Product Proof (Faz 1) — ✅ TAMAMLANDI

Razor Pages (FI.Api'ye gömülü) ile Incident Dashboard + Detail (timeline, evidence kartları,
AI summary, deterministik suggested action) eklendi; gerçek Docker Compose ortamında
`scripts/seed-demo-data.sh` ile üretilen gerçekçi senaryolarla doğrulandı. Ayrıntı için
`FI/README.md`'deki M17 notuna bakın. Yalnızca demo video/GIF kaydı (kod dışı bir iş) kaldı.

Teknoloji seçimi: ayrı bir frontend projesi (React/Next.js) mi, yoksa Razor Pages/Blazor ile
`FI.Api`'ye gömülü basit bir sunum mu — bu bir **ürün kararı**, koda başlamadan önce netleşmeli.

### M18 — Incident Intelligence (Faz 3) — ✅ TAMAMLANDI

1. ~~Deterministik "Suggested Action" kural motoru~~ — M17'de tamamlandı (`SuggestedActionCatalog`).
2. ~~"Kaç müşteri etkilendi"~~ — `NormalizedEvent`/`IntegrationEvent`'e `AffectedCustomerRef`
   eklendi (şema değişikliği + migration), `StripeConnector` mock payload'dan çıkarıyor.
3. ~~Business Impact özet~~ — Incident Detail sayfasında ve API'de yeni "İş Etkisi" bölümü
   (`EventCount` + tekil müşteri sayısı + süre). Gerçek Docker Compose ortamında (fresh DB,
   migration'dan itibaren) doğrulandı; ayrıntı ve bu süreçte bulunup düzeltilen bir eşzamanlılık
   bug'ı için `FI/README.md`'deki M18 notuna bakın.

### Due Diligence Düzeltmeleri (D1, D7/D8) — ✅ TAMAMLANDI

Harici bir due-diligence raporu (statik kod okumasıyla, canlı çalıştırma yapılamadan), bu ortamda
birebir doğrulanıp düzeltilen iki gerçek bulgu tespit etti:

- **D1** — `ClassifyJobHandler`'daki severity/iş-etkisi pencere sayımları, henüz DB'ye
  yazılmamış (SaveChangesAsync öncesi) güncel event'i kendi penceresinden dışlıyordu
  (off-by-one). Bir entegrasyon testiyle canlı doğrulandı ve düzeltildi.
- **D7/D8** — Control plane'de (`IntegrationsController`, `PromptVersionsController`,
  `IncidentsController` JSON API, `/Incidents` dashboard, `/hangfire`) hiç authentication yoktu;
  ayrıca Hangfire'ın kendi varsayılan "yalnızca localhost" filtresi, admin kimlik bilgisiyle bile
  Docker port-forwarding arkasında güvenilmez şekilde reddediyordu. Minimal bir paylaşılan-sır
  HTTP Basic Auth kapısı eklendi. Ayrıntı için `FI/README.md`'deki ilgili nota bakın.
- **D4** — Outbox `Failed` durumu bir çıkmaz sokaktı (ne zaman/kaç kez/neden başarısız olduğuna
  dair iz yok, dispatcher bir daha bakmıyordu). `FailureCount`/`LastFailedAt`/`LastError` eklendi,
  admin-görünür `GET /api/v1/admin/outbox?status=Failed` uç noktasıyla gözlemlenebilir hale
  getirildi.
- **D5 — rapor bu noktada hatalı, düzeltme gerekmedi.** Rapor `ClassifyJobHandler`'ın retry
  tükenince sessizce (exception fırlatmadan) döndüğünü iddia ediyordu; kodun okunmasıyla bu doğru
  bulunmadı — son denemedeki bir concurrency conflict, exception filter koşulu (`attempt <
  MaxConcurrencyRetries`) false olduğundan zaten yakalanmadan yukarı fırlatılıyor. Ayrıntı için
  `FI/README.md`'deki ilgili nota bakın.
- **D9** — Hiçbir yerde rate limiting yoktu; D7 ile control plane authentication gerektirse bile,
  geçerli bir API key/admin kimlik bilgisiyle hacimli istek atan biri DB'yi veya faturalandırılan
  Anthropic API çağrılarını kontrolsüz tüketebilirdi. `/api/v1/*` için IP başına sabit-pencere
  rate limiti eklendi, gerçek Docker Compose'da canlı doğrulandı.
- **PB6 — UI'da tekrarlama (reopen) görünürlüğü.** `Incident.ReopenCount>0` daha önce yalnızca
  header'da ham bir sayaç olarak ("· N kez reopen") gösteriliyordu; `StatusBadgeClass`'ın hem
  Detail hem Index sayfasındaki switch'inde `"Reopened"` için ayrı bir case de yoktu (nötr gri
  rozet gösteriyordu, Open ile görsel olarak ayırt edilemiyordu). Artık ayrı, amber renkli bir
  "Reopened" rozeti ve Detail sayfasında "bu ilk kez görülen yeni bir olay değil" diyen açık bir
  banner var. Gerçek Docker Compose'da (DB'de manuel reopen_count/status güncellemesiyle) canlı
  doğrulandı.
- **TD1 — 3 ayrı severity-penceresi COUNT sorgusu tek sorguya toplandı.** `ClassifyJobHandler`
  artık `count10/15/30`'u üç ayrı round trip yerine tek bir sorguda (Npgsql bunu
  `COUNT(*) FILTER (WHERE ...)` olarak çeviriyor) hesaplıyor. Gerçek Postgres'e karşı üretilen
  SQL doğrudan incelenerek doğrulandı.
- **TD2 — Eşzamanlılık-retry'leri için bir metrik eklendi.** `FiJobMetrics.ClassifyJobConcurrencyRetries`
  (OTel `FI.Api` meter'ına bağlı) artık her concurrency-conflict retry'sinde artıyor — önceden
  yalnızca tek tek log satırlarında görünen bir sinyal, artık toplu bir sayaç/alarm yüzeyinde de
  var.
- **TD3 — Açık bir `IntegrationEvent`↔`Incident` ilişkisi eklendi.** Önceden "bu event'ler bu
  incident'a mı ait" sorusu üç ayrı çağrı noktasında (`IncidentsController`,
  `AiAnalysisJobHandler`, `Detail.cshtml.cs`) bağımsızca `IntegrationId+Category-string+zaman-
  penceresi` (+ D2'nin 15-dakikalık payı) ile yeniden türetiliyordu. `IntegrationEvent`'e artık
  `ClassifyJobHandler`'ın sınıflandırma anında set ettiği gerçek bir `IncidentId` FK'sı var; üç
  çağrı noktası da artık doğrudan bu FK'ya göre filtreleniyor — zaman-penceresi tahmini yok,
  D2'nin residual eksik-sayım riski de bu üç yerde ortadan kalktı. Fresh bir veritabanına karşı
  migration doğrulandı (`incident_id` her event için doğru dolduruğunu DB'den doğrudan kontrol
  ederek), 4 yeni test eklendi (2 domain + 2 integration, biri M18'den beri ilk kez affected-
  customer sayımını otomatik test ediyor).
- **TD8 — Hangfire job sınırları arasında gerçek W3C trace-context yayılımı eklendi.** Önceden
  yalnızca manuel bir correlation-id string'i job payload'ları ve metod imzaları üzerinden
  taşınıyordu; FI'nin kendi logları arasında ilişkilendirme sağlıyordu ama harici bir OTel
  backend'inde (Jaeger/Tempo) job'lar arası gerçek bir span hiyerarşisi kurmuyordu. `OutboxMessage`
  artık oluşturulduğu andaki `Activity.Current?.Id`'yi otomatik yakalıyor (`TraceParent`);
  `ClassifyJobHandler`/`EvidenceCollectorJobHandler`/`AiAnalysisJobHandler` bunu kendi
  Activity'lerinin parent'ı olarak kullanıyor (`FiTelemetry.StartLinkedActivity`, daha önce hiç
  Activity üretmeyen `AddSource("FI.Api")` kancasını ilk kez gerçek span'lerle dolduruyor). Gerçek
  Docker Compose'da canlı doğrulandı: konsol trace exporter'ında her `ClassifyJob` span'inin
  `ParentSpanId`/`TraceId`'sinin orijinal HTTP isteğinin trace'iyle doğru eşleştiği doğrudan
  gözlemlendi. 4 yeni test eklendi.
- **TD6 — çoklu-replica altında `OutboxDispatcher` güvenliği canlı doğrulandı (kendisi için kod
  değişikliği gerekmedi; testte bulunan ayrı bir Hangfire şema yarışı düzeltildi).** Gerçek 2
  `fi-app` replikası (izole, geçici bir Docker Compose stack'i, aynı
  Postgres/Hangfire storage'ı paylaşan) ile 10 event'lik gerçek bir eşzamanlı patlama gönderildi;
  hepsi tam olarak bir kez sınıflandırıldı, tek bir incident satırı `event_count=10` ile oluştu,
  hiçbir outbox mesajı `Failed`/tekrar-dispatch edilmedi. Hangfire'ın recurring-job distributed
  lock mekanizması beklendiği gibi çalışıyor.
  **Bu test sırasında ayrı, önceden bilinmeyen bir bulgu ortaya çıktı:** 2 replika mutlak
  eşzamanlı (soğuk/ilk kez) başladığında, Hangfire.PostgreSql kütüphanesinin kendi şema kurulumu
  (`CREATE SCHEMA "hangfire"`) iki replika arasında yarışa giriyor ve kaybeden replika
  `23505 duplicate key value violates unique constraint "pg_namespace_nspname_index"` ile
  **çöküyor** (bir kez yeniden başlatıldığında şema zaten var olduğu için sorunsuz açılıyor).
  Bu, `OutboxDispatcher`'ın kendisiyle ilgili değil - `fi-migrate` adımı yalnızca FI'nin kendi EF
  Core migration'larını uyguluyor, Hangfire'ın storage şemasını değil; o, ilk bağlanan `fi-app`
  instance'ı tarafından lazily oluşturuluyordu. **Sonradan düzeltildi:** `Program.cs`'in
  `--migrate` modu artık `IGlobalConfiguration`'ı da resolve ediyor - bu, Hangfire'ın
  `PostgreSqlStorage` kurucusunu (ve dolayısıyla şema kurulumunu) sunucuyu hiç başlatmadan,
  `fi-migrate`'in tek/garantili-seri instance'ında tetikliyor. Aynı 2-replika senaryosu (izole
  Docker Compose stack'i, sıfırdan/soğuk başlangıç) yeniden çalıştırılarak doğrulandı: her iki
  replika da artık hiç çökmeden ayağa kalkıyor, `fi-migrate` loglarında "Hangfire SQL objects
  installed." satırı görülüyor. Normal tek-instance geliştirme stack'i ve demo seed script'i de
  bu değişiklikten sonra yeniden doğrulandı.

### M19 — Close the Product Loop (Business Operation Identity, Incident Resolution, AI Trust Calibration) — ✅ TAMAMLANDI

Ayrı bir "Product Reality Audit"in bulduğu üç P0 ürün açığını kapatan milestone. Ayrıntı için
`docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md`. Kısaca: `IntegrationEvent`'e opsiyonel
`OperationRef`/`OperationType`/`BusinessRecordRef` eklendi (43 event ≠ 43 iş operasyonu ayrımı,
dürüst "None/Partial/Complete" coverage semantiği ile); `Incident.Resolve()` domain metodu ve
`POST /api/v1/incidents/{id}/resolve` eklendi (daha önce hiç ulaşılamayan `Resolved` durumu artık
gerçek, mevcut Reopen/cooldown mekanizmasıyla entegre); `AiAnalysisValidator.CheckGrounding`,
gerçek testlerle teşhis edilen iki false-positive'i (deterministik bağlamın echo'sunun
"desteklenmeyen" sayılması, entity adı yeniden biçimlendirmesi) katmanlı, sayısal kontrolü
gevşetmeyen bir düzeltmeyle kapattı. Golden bir PaymentSync senaryosu (43 event/12 operasyon/7
müşteri) gerçek Docker Compose'da uçtan uca (Resolve dahil) canlı doğrulandı.

**Not: bu milestone'un kendi kaynağı (harici bir "M19" promptu), bir sonraki adımı "M20" olarak
adlandırıyor — bu doküman ve `FI/README.md` bu numaralandırmayı benimsedi.** Önceki "M19 —
Customer Validation" başlığı, aşağıda **M20** olarak yeniden adlandırıldı; kapsamı değişmedi.

### M20 — Design Partner / Customer Validation

İncelemenin önerdiği doğrudan uygulanabilir: en az 3 entegrasyon geliştiricisi, 2 support
mühendisi, 1 otomasyon danışmanıyla M19'un golden incident demo'sunu göster, altı soru sor (bkz.
`docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md` sonundaki liste) — en önemlisi: **"Bugün aynı
problemi çözmek için hangi ekranlara bakıyorsunuz?"** Bu adım kod gerektirmez, planlama
gerektirir.

### Sonrasında — SaaS Roadmap

İncelemenin "şimdilik yapma" listesi (multi-tenant, billing, teams, RBAC, marketplace,
analytics) aynen korunmalı — ürün M20'de doğrulanmadan bunlara yatırım yapmak riskli.

## 4. GitHub Sunumu

**Şu anda yapılabilecek, sıfır maliyetli/riskli, bekleyen tek madde:** repo'nun GitHub "About"
açıklaması hâlâ boş (doğrulandı — `gh api repos/Rashinu/fi-failure-intelligence` boş
`description` dönüyor). İncelemenin önerdiği metin doğrudan kullanılabilir:

> Evidence-backed AI analysis for API and webhook failures.

Bunu (ve varsa birkaç GitHub "topics" etiketi — `dotnet`, `postgresql`, `hangfire`, `anthropic`
gibi) eklemek `gh repo edit --description "..." --add-topic ...` ile tek komutluk bir iş; M16
UI'ından bağımsız, hemen yapılabilir.

İncelemenin önerdiği README sırası (Problem → Demo GIF → Product → Architecture → Screenshots →
Quick Start → Roadmap → Technical Details) kök `README.md`'ye M16 tamamlandığında (demo
GIF/ekran görüntüsü mevcut olduğunda) uygulanmalı. Şu anki kök `README.md` bunun bir ön-sürümü —
Problem/Mimari/Quick Start/Durum bölümlerini içeriyor ama demo görseli yok (çünkü henüz bir UI
yok). Milestone günlükleri zaten `FI/README.md` içinde, ayrı bir `docs/` dosyasına taşınması
küçük bir düzenleme işi, önceliği düşük.

## 5. Sonuç

İncelemenin ana tavsiyesi — **yeni backend özelliği eklemeden önce Engine → Product → Customer
Validation → SaaS sırasını izle** — doğru ve bu dokümanın da vardığı sonuç. Tek fark: Faz 3'te
önerilen "Config Change Correlation" ve "Similar Incidents" zaten backend'de bitmiş durumda,
bu yüzden gerçek kalan iş listesi incelemenin öngördüğünden **daha kısa** — asıl büyük, hiç
başlanmamış iş kalemi bir **kullanıcı arayüzü inşa etmek** (M16).
