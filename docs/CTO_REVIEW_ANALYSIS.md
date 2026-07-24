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

### M19 — Customer Validation

İncelemenin önerdiği doğrudan uygulanabilir: en az 3 entegrasyon geliştiricisi, 2 support
mühendisi, 1 otomasyon danışmanıyla M16 demo'sunu göster, tek soru sor: **"Bugün aynı problemi
çözmek için hangi ekranlara bakıyorsunuz?"** Bu adım kod gerektirmez, planlama gerektirir.

### Sonrasında — SaaS Roadmap

İncelemenin "şimdilik yapma" listesi (multi-tenant, billing, teams, RBAC, marketplace,
analytics) aynen korunmalı — ürün M19'da doğrulanmadan bunlara yatırım yapmak riskli.

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
