# AI Integration Operations Platform — Security, Reliability & Observability (V2-V4 Derinleşme)

**Doküman türü:** Platform mimarisi — güvenlik, güvenilirlik, gözlemlenebilirlik tasarımı
**Tarih:** 2026-07-12
**Kapsam:** V1 (FI) dışında kalan, V2-V4 fazlarında ortaya çıkan YENİ risk yüzeyleri ve bunlara karşı tasarım kararları. V1'in kendi security/reliability/observability tasarımı (PII redaction, API key hash+rotation, correlation id, Serilog+OpenTelemetry, health checks, Polly retry/circuit breaker) burada yeniden tasarlanmaz; bu doküman onun üzerine **nasıl derinleşeceğini** anlatır.
**Varsayım:** V2 = Multi-tenancy + Connector Framework + Support Correlation. V3 = Runbook Engine. V4 = Controlled Remediation.

---

## 0. Çerçeve: Neden Bu Doküman Ayrı

FI (V1), tek-tenant, tek yönlü (event içeri, rapor dışarı), aksiyon almayan bir sistemdir — en kötü senaryoda yanlış bir kök-neden özeti üretir, insan onu görmezden gelebilir. Platform (V2-V4) üç eksende niteliksel olarak farklı bir risk profiline geçer:

1. **Multi-tenancy** — artık "bizim verimiz" yok, "N tane müşterinin verisi aynı altyapıda" var. Tek bir izolasyon hatası = veri sızıntısı = güven kaybı + yasal risk.
2. **Connector ekosistemi** — platform artık üçüncü taraf servislere kendi credential'larıyla bağlanıyor (sadece webhook dinlemiyor, aktif olarak Stripe/HubSpot/Salesforce API'lerini çağırıyor). Her yeni connector = yeni credential türü = yeni saldırı yüzeyi.
3. **Remediation execution** — V4'te sistem artık sadece "ne oldu" demiyor, **gerçek dünyada bir aksiyon icra ediyor** (retry tetikleme, secret rotasyonu tetikleme, webhook endpoint'i devre dışı bırakma vb.). Yanlış bir remediation, yanlış bir rapordan çok daha pahalıya mal olur — geri alınamaz olabilir.

Bu üç eksen, her faz geçişinde güvenlik/güvenilirlik/gözlemlenebilirlik gereksinimlerini bir üst kademeye taşır. Aşağıdaki bölümler bu kademeleri tasarlar.

---

## 1. Multi-Tenancy Güvenlik Modeli (V2/V3)

### 1.1 İzolasyon Modeli Seçimi

Üç aday model var:

| Model | Açıklama | Trade-off |
|---|---|---|
| **A. Sadece query filter (application-level)** | Her sorguya `WHERE tenant_id = @current` eklenir; disiplin ORM/repository katmanında. | En düşük operasyonel maliyet, en yüksek insan-hatası riski. Bir geliştiricinin bir yerde `WHERE` eklemeyi unutması = tüm tenant'ların verisi sızar. |
| **B. Row-Level Security (RLS, veritabanı seviyesinde)** | PostgreSQL RLS policy'leri, session'a bağlı `app.current_tenant_id` üzerinden satır erişimini veritabanı motorunda zorlar. | Uygulama kodu hata yapsa bile veritabanı reddeder — savunma derinliği. Ek karmaşıklık: connection pooling ile session variable yönetimi, migration/seed script'lerinde RLS bypass ihtiyacı, ORM'in RLS'i "görmemesi" nedeniyle test disiplini gerekir. |
| **C. Fiziksel izolasyon (schema-per-tenant / database-per-tenant)** | Her tenant kendi şemasında veya kendi veritabanında. | En güçlü izolasyon, en yüksek operasyonel maliyet (migration N kat, connection pool patlaması, cross-tenant analytics zorlaşır). Küçük/orta ölçekli SaaS için genelde aşırı mühendislik. |

**Karar (V2):** **B + A birlikte — RLS zorunlu taban katman, query filter ek güvence (defense-in-depth), C değil.**

Gerekçe:
- Sadece A (query filter) tek başına kabul edilemez: bir tenant'ın verisi sızarsa bu platformun var oluş amacını (müşteri incident/support verisiyle çalışmak) doğrudan baltalar. Tek hata noktası riski, veri türünün hassasiyetiyle orantısız.
- C (fiziksel izolasyon), V2/V3 ölçeğinde (onlarca-yüzlerce tenant, henüz enterprise-tek-kiracı talebi yok) operasyonel yükü haklı çıkarmıyor. Bu, "büyük müşteri kendi izole ortamını istiyor" senaryosu netleşirse **V4+ sonrası opsiyonel bir "dedicated tenant" katmanı** olarak saklanmalı (roadmap notu, şimdi tasarlanmaz).
- B (RLS), PostgreSQL kullanıldığı varsayımıyla (V1'in muhtemel altyapısı), uygulama kod hatasına karşı veritabanı seviyesinde bir güvenlik ağı sağlar — "geliştirici WHERE'i unuttu" senaryosunu veritabanı reddeder.

**Uygulama notları (tasarım, kod değil):**
- Her tenant-scoped tabloda `tenant_id` kolonu zorunlu; RLS policy `USING (tenant_id = current_setting('app.tenant_id')::uuid)`.
- Uygulama, her request başında (middleware seviyesinde, auth'tan hemen sonra) DB session'a `app.tenant_id`'yi set eder; bu adım atlanırsa RLS varsayılan olarak **hiçbir satırı döndürmemeli** (fail-closed, fail-open değil).
- Background job / worker süreçleri (event ingestion, connector polling) için de aynı disiplin: her job tenant context'ini taşımalı, "sistem seviyesi" job'lar (örn. cross-tenant billing raporu) ayrı, açıkça işaretlenmiş bir "superuser" bağlam üzerinden RLS bypass eder — bu bypass yalnızca whitelisted, denetlenen job türlerinde kullanılır.
- Connection pooling dikkat noktası: pooled connection'lar arasında `app.tenant_id` sızıntısı olmaması için her connection checkout'ta reset zorunlu.

### 1.2 Tenant'lar Arası Veri Sızıntısı — Test Stratejisi

Bu, "bir kez yazılıp unutulan" değil, **her PR'da otomatik doğrulanan** bir kontrol olmalı:

1. **Zorunlu cross-tenant izolasyon test paketi** — her yeni tenant-scoped entity/endpoint için: Tenant A olarak login ol, Tenant B'nin kaynağının ID'sini tahmin/keşfet (IDOR tarzı), erişmeyi dene → beklenen sonuç 403/404, asla 200. Bu paket CI'da "yeni endpoint eklendi ama izolasyon testi yok" durumunu build'i kırarak yakalar (checklist/lint kuralı: yeni controller/repository eklenince eşlik eden izolasyon testi zorunlu).
2. **RLS policy regression testi** — migration pipeline'ına entegre: her migration sonrası, RLS policy'lerin hâlâ mevcut ve doğru tablo setine uygulandığını doğrulayan bir smoke test (yeni tablo eklenip RLS unutulmasını yakalamak için).
3. **Fuzzing / property-based test** — rastgele tenant_id kombinasyonlarıyla sorgu üretip, hiçbir yanıtın başka tenant'ın verisini içermediğini doğrulayan bir test katmanı (özellikle agregasyon/rapor endpoint'lerinde — "toplam sayı" gibi görünüşte zararsız endpoint'ler bile sızıntı kanalı olabilir, örn. count-based side-channel).
4. **Chaos/pentest ritüeli** — V2 GA öncesi ve her majör multi-tenant değişiklikte, harici veya iç "red team" bakış açısıyla manuel penetrasyon testi (özellikle JWT/claim manipülasyonu, tenant_id'nin request body/query'den güvenilmeden alınması gibi vektörler).
5. **Üretimde kanıt-tabanlı alarm** — sorgu seviyesinde "response tenant_id ≠ auth tenant_id" durumunu tespit eden bir application-level assertion/guard; bu tetiklenirse **request derhal reddedilir + P1 güvenlik alarmı** (bu, hem savunma hem de erken uyarı sistemi).

---

## 2. Connector Framework Credential Güvenliği (V2)

### 2.1 Saklama: Secret Manager Entegrasyonu

Üçüncü taraf credential'ları (Stripe secret key, HubSpot API key, Salesforce OAuth token vb.) **hiçbir zaman** uygulama veritabanında düz metin veya uygulama-seviyesi simetrik şifreleme ile saklanmaz. V1'in kendi API key hash+rotation modeli farklı bir problemi çözüyor (platforma gelen istekleri doğrulamak); burada çözülmesi gereken problem platformun **dışarıya** giderken kullanacağı credential'ları korumak.

**Tasarım:**
- Harici secret manager (AWS Secrets Manager / Azure Key Vault / HashiCorp Vault — platformun barındığı buluta göre) zorunlu bağımlılık; uygulama veritabanı yalnızca secret manager'daki kaydın **referansını** (opaque secret ID) tutar.
- Uygulama kodu credential'ın kendisini asla kendi belleğinde uzun süre tutmaz; ihtiyaç anında (connector çağrısı yapılırken) secret manager'dan çekilir, kullanılır, referans dışarı sızdırılmaz (log'lara, exception mesajlarına, trace attribute'larına yazılmaz — V1'in PII redaction pipeline'ının connector credential'ları için de zorunlu bir uzantısı olmalı: "secret redaction" ayrı bir sınıf olarak PII redaction'dan bağımsız ama aynı pipeline'da çalışır).
- Tenant başına ayrı secret path/namespace (`tenants/{tenant_id}/connectors/{connector_id}/credentials`) — bu hem izolasyonu hem de secret manager'ın kendi audit log'unun tenant bazlı okunabilmesini sağlar.
- Encryption at rest secret manager'ın sorumluluğunda; uygulama seviyesinde ayrıca "envelope encryption" eklenmesi (KMS ile ikinci katman) yüksek hassasiyetli connector türleri (finans/ödeme) için değerlendirilir, düşük hassasiyetli connector'lar için zorunlu tutulmaz (maliyet/karmaşıklık dengesi).

### 2.2 Connector-Özel Scope/Permission Modeli

Her connector türü, platform tarafında **minimum ayrıcalık ilkesiyle** tanımlanan bir "capability manifest" ile kayıtlıdır:

- Connector manifest'i, o connector'ın **hangi işlemleri yapabileceğini** deklare eder (örn. Stripe connector için: `read:webhooks`, `read:charges` — asla `write:charges` veya `refund:charges` V2'de yok, çünkü V2 salt-okunur gözlem yapıyor, aksiyon almıyor).
- Kullanıcıya (tenant admin'e) connector bağlarken **hangi scope'ların isteneceği** açıkça gösterilir; platform, üçüncü taraf servisin kendi OAuth scope sistemini varsa ona map eder ve **her zaman en dar scope'u** ister (örn. Stripe'ın "restricted key" mekanizması, tam secret key yerine tercih edilir — connector onboarding sırasında kullanıcıya restricted key oluşturma talimatı verilir).
- Platform içi yetkilendirme, connector'ın manifest'inde deklare ettiği scope dışında bir API çağrısı yapmasına **kod seviyesinde izin vermez** — yani connector implementasyonu bir "sandbox" arayüzü üzerinden dış servise erişir, bu arayüz manifest'te olmayan bir operasyon çağrılırsa runtime'da reddeder (defense-in-depth: hem üçüncü taraf servisin kendi scope kısıtlaması hem de platformun kendi iç kısıtlaması).

### 2.3 Credential Rotation Bildirimi

Credential rotation iki yönlü bir problem: (a) tenant kendi Stripe/HubSpot secret'ını rotate ettiğinde platform bunu nasıl öğrenir, (b) platform kendi tuttuğu secret manager referansını nasıl güvenle günceller.

**Tasarım:**
- Her connector bağlantısı için **health-check tabanlı erken tespit**: connector'ın periyodik "credential still valid" kontrolü (V1'in health check konseptinin connector'a özelleşmiş hali) — 401/403 dönerse bu "credential expired/rotated" olarak sınıflandırılır (genel bir bağlantı hatasından ayrı bir durum kodu ile).
- Bu durum tespit edildiğinde: connector **otomatik olarak "degraded" duruma** geçer (sessizce başarısız olmaz), tenant admin'e bildirim (email/in-app) gider, connector'ın ürettiği event'ler bu süre boyunca "credential-stale" etiketiyle işaretlenir ki downstream analiz (FI'nin kök-neden motoru) bunu "gerçek bir entegrasyon arızası" ile karıştırmasın.
- Kullanıcı yeni credential'ı girdiğinde: eski secret manager kaydı **hemen silinmez**, kısa bir "grace window" (örn. 24 saat) sonrası temizlenir — bu, yanlışlıkla yanlış credential girilip geri dönme ihtiyacına karşı bir güvenlik ağı, ama grace window sonunda zorunlu silme (secret manager'da sonsuza kadar eski secret biriktirmemek için).
- Platform tarafı rotation (V1'deki API key rotation modelinin platform-genelinde çalışan hali): secret manager referansı değiştiğinde connector'ı kullanan tüm background job'lar bir sonraki çalıştırmada yeni referansı otomatik çeker (cache TTL kısa tutulur, secret'lar uygulama içinde uzun süre cache'lenmez).

---

## 3. Support Correlation Veri Gizliliği (V2)

Destek ticket'ları, entegrasyon event'lerinden **niteliksel olarak daha riskli** bir veri kaynağıdır: müşteri isimleri, e-posta, bazen kredi kartı son 4 hanesi, bazen ekran görüntüsü içinde daha fazlası (API key'ler dahil — kullanıcılar destek ticket'ına yanlışlıkla secret yapıştırabilir).

**Risk modeli:**
1. **Yoğun PII girişi** — V1'in PII redaction pipeline'ı yapılandırılmış event verisi (JSON payload) için tasarlanmış; destek ticket'ları **serbest metin** (freeform text), bu redaction'ı zorlaştırır (regex/NER tabanlı tespit, yapılandırılmış alan tespitinden daha düşük doğrulukta).
2. **İkincil sızıntı ile secret ifşası** — ticket içine yapıştırılmış bir API key/secret, hem PII hem de credential sınıfında; bu iki ayrı redaction sınıfının (PII redaction + secret redaction) support correlation pipeline'ında **birlikte** çalışması gerekir.
3. **Cross-tenant support verisi karışması** — support correlation, ticket'ları entegrasyon event'leriyle eşleştirirken, yanlış eşleştirme (örn. yanlış tenant'ın ticket'ı başka tenant'ın incident'ına bağlanması) hem yanlış analiz hem de gizlilik ihlali.

**Tasarım kararları:**
- **Redaction-at-ingestion, iki geçişli:** Ticket platform'a girer girmez (1) yapılandırılmış PII sınıflandırıcı (e-posta, telefon, kredi kartı deseni) + (2) secret/credential deseni tarayıcı (API key formatları, JWT, bearer token desenleri) çalışır. Ham metin, redaction'dan geçmeden hiçbir downstream sisteme (AI özetleyici dahil) **iletilmez**.
- **Confidence-tabanlı insan gözden geçirme kuyruğu:** Redaction sınıflandırıcısının düşük güvenle işaretlediği (freeform text'te "bu PII olabilir mi emin değilim" durumları) segmentler, otomatik olarak maskelenir (fail-closed — şüpheli içerik varsayılan olarak gizlenir) VE ayrı bir insan gözden geçirme kuyruğuna düşer; bu, V1'in "confidence + human review flag" desenini support correlation'a taşımasıdır.
- **Erişim kontrolü — support verisi ayrı bir yetki sınıfı:** Ticket içeriğine erişim, genel entegrasyon event verisine erişimden **ayrı bir RBAC izni** olarak modellenir (örn. "incident-viewer" rolü ticket'ın var olduğunu ve özetini görebilir ama ham ticket metnini göremez; "support-data-viewer" ayrı, daha kısıtlı bir rol). Bu, "herkes her şeyi görsün" varsayımını kırar — support verisi varsayılan olarak en dar erişimle başlar.
- **Retention kısıtlaması:** Ham (redaksiyon öncesi orijinal) ticket metni, redaction işleminden sonra **saklanmaz** — yalnızca redakte edilmiş versiyon platform veritabanında kalır; orijinal, kaynağı (Zendesk/Intercom vb.) referans olarak tutulur ama platform kendi kopyasını uzun süre saklamaz (bu hem gizlilik hem de "bir gün redaction algoritması iyileşirse eski veriyi yeniden işleriz" beklentisini bilinçli olarak reddeder — trade-off açıkça not edilir).
- **Correlation eşleştirme doğrulaması:** Ticket-to-incident eşleştirme her zaman tenant_id eşleşmesi zorunlu ön koşuluyla yapılır (bölüm 1'deki RLS/query filter disiplininin support correlation'a uzantısı); eşleştirme confidence skoru düşükse otomatik bağlanmaz, öneri olarak sunulur.

---

## 4. Runbook Engine Güvenlik Riski (V3)

### 4.1 Risk

Runbook Engine, "bu tür bir incident'ta genelde şu adımlar izlenir" türünden **önerilen** aksiyon adımları üretir (örn. "webhook secret'ı rotate et", "rate limit'i geçici yükselt", "belirli bir endpoint'i devre dışı bırak"). V3'te bu henüz **execution yapmıyor** (V4'ün konusu), ama öneri metninin kendisi tehlikeli olabilir: yanlış/eksik bağlamla üretilmiş bir öneri, bir mühendisin **kör güvenerek** yanlış bir aksiyonu manuel olarak icra etmesine yol açabilir. Bu, "AI yanlış rapor verdi" (V1 riski) ile "AI yanlış aksiyon önerdi ve insan onu uyguladı" (V3 riski) arasındaki fark — ikincisi çok daha yüksek etkili.

### 4.2 "Önerilen Aksiyon" vs "İzin Verilen Aksiyon" Ayrımı

Bu ayrımın **politika/allowlist mekanizmasıyla** zorlanması gerekiyor, salt UI metniyle ("bu bir öneridir, dikkatli olun") değil:

- **Runbook Engine'in çıktı uzayı, sabit bir allowlist ile sınırlıdır.** Sistem serbest metinle "şunu yap" demez; önerilen her aksiyon, platformun önceden tanımladığı, kategorize edilmiş bir **aksiyon kataloğundan** (action catalog) bir öğeye referans verir (örn. `ROTATE_WEBHOOK_SECRET`, `INCREASE_RATE_LIMIT_TEMP`, `DISABLE_ENDPOINT`). Katalogda olmayan bir aksiyon **hiçbir zaman önerilemez** — bu, LLM'in serbest metin üretme özgürlüğünü kasıtlı olarak kısıtlayan bir tasarım kararı (guardrail, prompt seviyesinde değil, çıktı şeması seviyesinde zorlanır: yapılandırılmış çıktı + katalog ID doğrulaması, LLM katalog dışı bir ID üretirse çıktı reddedilir).
- **Her katalog aksiyonu, kendi risk seviyesiyle etiketlenir** (düşük/orta/yüksek — örn. "log seviyesini artır" düşük, "endpoint'i devre dışı bırak" yüksek). Risk seviyesi, önerinin UI'da nasıl sunulacağını belirler (yüksek riskli öneriler, ek bir onay adımı ve "bu aksiyonun olası yan etkileri" açıklamasıyla birlikte gösterilir — hiçbir zaman tek tıkla "uygula" değil, çünkü V3'te uygulama zaten yok, ama gösterim tasarımı V4'e hazırlık olarak şimdiden bu ayrımı taşımalı).
- **"İzin verilen aksiyon" tenant/rol bazında ayrı bir konfigürasyon.** Runbook Engine bir aksiyonu "önerebilir" ama o tenant'ta o rol için o aksiyon "izin verilenler" listesinde değilse, öneri UI'da salt-okunur/gri gösterilir ("bu aksiyon önerilir ama sizin yetki seviyenizde uygulanamaz — bir admin'e danışın"). Bu, V4'teki gerçek execution allowlist'inin (bölüm 5) erken bir versiyonu — V3'te politika altyapısı kurulur, V4'te bu altyapı gerçek icra kapısı olarak kullanılır.
- **Runbook önerisi her zaman kanıt zinciriyle gelir** (V1'in evidence-backed root cause deseninin runbook'a uzantısı): "bu aksiyon öneriliyor çünkü X, Y, Z kanıtı gözlemlendi" — kanıtsız, salt "genel best practice" önerisi düşük confidence olarak işaretlenir ve insan gözden geçirme zorunluluğu tetiklenir.
- **Yanlış öneri geri bildirim döngüsü:** kullanıcı bir öneriyi "yanlıştı/tehlikeliydi" olarak işaretleyebilir; bu sinyal hem o runbook şablonunun güven skorunu düşürür hem de tekrarlayan yanlış önerilerde şablonun otomatik olarak devre dışı bırakılmasını (insan reddi olmadan tekrar önerilmemesini) tetikler.

---

## 5. Controlled Remediation — Kritik Güvenlik Tasarımı (V4)

Bu, platformun en yüksek riskli yeteneği: sistem artık gerçek dünyada **aksiyon icra ediyor**. Tasarım, "varsayılan olarak güvenli, açıkça izin verilenler dışında hiçbir şey yapılamaz" ilkesine dayanmalı.

### 5.1 Approval Zorunluluğunun Teknik Garantisi (Sadece UI Kuralı Değil)

UI'da bir "onayla" butonu olması yeterli değildir — API'yi doğrudan çağıran biri (ister başka bir servis, ister kötü niyetli/hatalı bir istemci) onay adımını atlayabilmemeli. Tasarım:

- **Remediation execution API'si, iki ayrı adımdan oluşan zorunlu bir state machine üzerinde çalışır:** `PROPOSED → APPROVED → EXECUTING → COMPLETED/FAILED/ROLLED_BACK`. `EXECUTING` durumuna geçiş, **yalnızca** ayrı bir "approval" kaydının var olduğu, kriptografik olarak o remediation talebine bağlı (approval token, remediation request ID'sine imzalı referans verir) ve onaylayan kimliğin execution'ı tetikleyen kimlikten **farklı** olduğu (four-eyes / iki kişi kuralı — kendi kendini onaylayamama) durumlarda mümkündür. Bu kontrol API/servis katmanında, veritabanı transaction seviyesinde zorlanır — UI bu akışı atlayıp doğrudan `EXECUTING`'e geçiş isteyen bir çağrı yaparsa, backend bunu reddeder (state machine ihlali olarak 409/403).
- **Onay, remediation talebinin tam içeriğine bağlıdır (parametre bağlama).** Onaylanan şey "bir remediation" değil, "şu tam parametrelerle (hedef kaynak ID'leri, aksiyon tipi, blast radius tahmini) şu remediation"dır — onay sonrası parametreler değiştirilirse (örn. hedef kaynak listesi genişletilirse) onay geçersizleşir, yeniden onay gerekir (TOCTOU/parametre kaçırma saldırısına karşı).
- **Yüksek riskli aksiyon kategorileri için onay eşiği artar:** düşük riskli (`ROTATE_WEBHOOK_SECRET` gibi tek kaynaklı, geri alınabilir) aksiyonlar tek onayla geçebilir; yüksek riskli veya çok-kaynaklı aksiyonlar (bölüm 5.4) ikinci bir onay katmanı (örn. tenant admin + platform on-call) gerektirir — bu eşik, katalogdaki risk etiketine (bölüm 4.2) bağlı olarak konfigüre edilir, kod içine gömülü sabit değil.
- **Zaman aşımlı onay:** bir onay, belirli bir süre (örn. 15 dakika) içinde kullanılmazsa geçersizleşir — eski, unutulmuş bir onayın çok sonra "sürpriz" bir execution tetiklemesini engeller.

### 5.2 Execution Sandbox / Dry-Run

- **Her remediation aksiyonu, gerçek icra öncesi zorunlu bir "dry-run" fazından geçer.** Dry-run, aksiyonun gerçekte neyi değiştireceğini (etkilenecek kaynak listesi, mevcut durum → hedef durum diff'i) hesaplar ama üçüncü taraf servise/duruma **hiçbir yazma işlemi göndermez**. Onay ekranında kullanıcıya gösterilen "bu aksiyon şunları değiştirecek" bilgisi, dry-run çıktısının kendisidir — tahmini metin değil, gerçek hesaplanmış diff.
- **Connector implementasyonları remediation için ayrı bir "execute" arayüzü sunar** (bölüm 2.2'deki manifest modelinin uzantısı); bu arayüz, aksiyonun idempotent olup olmadığını deklare eder (aynı remediation talebi tekrar gönderilirse — retry senaryosu — yan etki tekrarlanır mı yoksa güvenli mi). Idempotent olmayan aksiyonlar için execution, tekilleştirme anahtarı (idempotency key) zorunlu taşır.
- **Prod-olmayan/sandbox connector modu:** özellikle yeni bağlanan veya düşük güven skorlu connector'larda (örn. onboarding sonrası ilk N gün), remediation execution varsayılan olarak "sandbox mode"da çalışır — gerçek API çağrısı yerine, connector'ın sağladığı bir test/staging endpoint'i (varsa) veya sadece "bu çağrı yapılacaktı" simülasyonu loglanır; gerçek moda geçiş açık bir konfigürasyon adımı gerektirir.

### 5.3 Rollback Garantisi

- **Katalogdaki her aksiyon, kayıt anında bir "rollback stratejisi" ile birlikte tanımlanır** — üç sınıf: (a) **otomatik geri alınabilir** (sistem, execution öncesi durumu kaydeder ve tersini otomatik uygulayabilir — örn. rate limit değerini eski haline döndürme), (b) **manuel geri alınabilir** (sistem geri alma adımlarını üretir ama insan onayı/icra gerekir — örn. bir secret rotate edildiyse eski secret'a dönmek genelde istenmez, bunun yerine "yeni durumu kabul et veya ileri git" seçenekleri sunulur), (c) **geri alınamaz** (örn. bir veri silme aksiyonu — bu sınıf V4'te **katalogda bulunmaz**, geri alınamaz aksiyonlar controlled remediation kapsamı dışında tutulur, kasıtlı bir kapsam sınırı).
- **Execution öncesi durum anlık görüntüsü (pre-state snapshot) zorunludur** — otomatik/manuel geri alınabilir sınıflar için, execution başlamadan hemen önce etkilenecek kaynağın mevcut durumu kayıt altına alınır (audit trail'in bir parçası, bölüm 5.5); rollback bu snapshot'a göre çalışır.
- **Rollback'in kendisi de aynı approval state machine'inden geçer** (bölüm 5.1) — rollback'i "acil, onaysız" bir kaçış yolu olarak tasarlamamak önemli, çünkü kötüye kullanım riski (yanlışlıkla veya kötü niyetle sürekli rollback tetikleme) aynı derecede gerçek. İstisna: **otomatik geri alınabilir** sınıf için, execution'ın kendisi "circuit breaker" tarzı bir post-execution health check ile izlenir — eğer aksiyon sonrası belirlenen bir hata eşiği aşılırsa, sistem **otomatik ve onaysız** rollback tetikleyebilir (bu, güvenlik onay modelinin istisnası değil, "kötüye giden bir durumu durdurma" refleksi — ayrı bir P1 alarmıyla birlikte, insan sonradan bilgilendirilir).

### 5.4 Blast Radius Sınırlama

- **Her remediation talebi, execution öncesi zorunlu bir "blast radius hesaplama" adımından geçer:** kaç kaynak (örn. kaç webhook endpoint, kaç API key), kaç tenant, ne kadar trafik hacmi etkilenecek.
- **Sabit üst sınırlar, katalog seviyesinde konfigüre edilir ve API/policy katmanında zorlanır** — örn. "bir remediation talebi tek seferde en fazla N kaynağı etkileyebilir" (N, aksiyon tipine göre değişir, düşük riskli aksiyonlarda daha yüksek, yüksek riskli aksiyonlarda düşük). Bu sınır aşılırsa, sistem talebi **otomatik olarak parçalara böler** (her biri ayrı onay gerektiren küçük batch'ler) veya tamamen reddeder — asla "büyük blast radius'u sessizce kabul edip tek seferde çalıştırma" yapmaz.
- **Tek tenant kuralı (varsayılan):** bir remediation talebi, varsayılan olarak **yalnızca tek bir tenant'ın kaynaklarını** etkileyebilir; cross-tenant remediation (örn. "tüm tenant'larda X connector'ı devre dışı bırak" gibi platform-genelinde bir aksiyon) ayrı, çok daha kısıtlı bir yetki sınıfı (yalnızca platform operasyon ekibi, ayrı onay zinciri, ayrı audit kategorisi) gerektirir — bu, bölüm 1'deki tenant izolasyon disiplininin remediation'a uzantısıdır.
- **Rate-limited execution:** blast radius sınırının yanı sıra, zaman bazlı bir sınır da vardır (örn. "bir tenant'ta saatte en fazla M remediation execution'ı") — bu hem bir hata döngüsünün (runbook önerisi → onay → execution → yeni hata → yeni öneri → ...) kontrolsüz şekilde tekrarlamasını önler hem de bir credential/hesap ele geçirme senaryosunda saldırganın seri şekilde remediation tetiklemesini yavaşlatır.

### 5.5 Tam Audit Trail

- **Her remediation talebinin yaşam döngüsündeki her state geçişi** (PROPOSED, APPROVED — kim/ne zaman/hangi parametrelerle, EXECUTING, COMPLETED/FAILED/ROLLED_BACK) **değişmez (immutable, append-only)** bir audit log'a yazılır — bu log'un kendisi güncellenemez/silinemez (V1'in correlation-id'li Serilog yapısının, remediation için ayrı, daha yüksek dayanıklılıklı bir "audit event store"a evrilmiş hali; genel uygulama logundan ayrı tutulur çünkü retention/erişim gereksinimleri farklıdır — audit trail çok daha uzun süre, değiştirilemez şekilde saklanır).
- **Audit kaydı minimum şunları içerir:** talebi tetikleyen kimlik, onaylayan kimlik (varsa), dry-run çıktısı, pre-state snapshot, gerçek execution sonucu, blast radius hesaplaması, hangi katalog aksiyonu ve hangi risk seviyesiyle.
- **Audit trail, kendisi de tenant-izole ama platform operasyon ekibi için cross-tenant sorgulanabilir** (güvenlik incelemesi/compliance için) — bu erişim de kendi başına audit edilir (kim, ne zaman, hangi tenant'ın audit log'unu görüntüledi — "audit'i denetleyen audit").
- **Değişmezlik teknik olarak zorlanır** — audit event store'a yazma sadece append, uygulama katmanında update/delete endpoint'i yoktur; ek güvence isteniyorsa hash-chain (her kayıt bir öncekinin hash'ini içerir) ile sonradan değiştirme tespiti eklenebilir (V4 sonrası sertleştirme adımı olarak not edilir, V4 GA'da zorunlu değil ama mimari buna izin verecek şekilde tasarlanır).

---

## 6. Platform Ölçeğinde Rate Limiting

V1'in muhtemelen tek boyutlu (API key başına) rate limiting'i, platformda çok boyutlu hale gelmeli:

- **Tenant bazlı:** her tenant'ın toplam event ingestion / API çağrı hacmi için üst sınır — bir tenant'ın anormal trafik patlaması (kötü niyetli veya hatalı bir entegrasyon konfigürasyonu) diğer tenant'ları etkilememeli (bkz. bölüm 7 noisy neighbor).
- **Connector bazlı:** her connector bağlantısının, hem platforma gelen (ingestion) hem platformdan çıkan (üçüncü taraf API çağrıları — remediation dahil) yönde ayrı limitleri olmalı; özellikle çıkış yönü, üçüncü taraf servisin **kendi** rate limit'ini aşıp o servis tarafında tenant'ın hesabının kısıtlanmasına (platform kaynaklı ama tenant'ın ödediği bir bedel) yol açmamalı — connector implementasyonu üçüncü taraf servisin dokümante edilmiş rate limit'ini bilmeli ve ona göre kendi çıkış hızını sınırlamalı (client-side throttling, sadece 429 alınca reaktif değil).
- **Plan bazlı:** ticari plan seviyesine göre (free/pro/enterprise) farklı limit kademeleri — bu hem kötüye kullanımı sınırlar hem de ticari modeli teknik olarak destekler; limit aşımında sert kesme yerine (özellikle incident anında, tam da en çok ihtiyaç duyulan anda kesmek kötü bir kullanıcı deneyimi) kademeli degradasyon (örn. AI özetleme gecikmeli kuyruğa alınır ama temel event ingestion durmaz) tercih edilir.
- **Remediation'a özel ayrı limit sınıfı (V4):** bölüm 5.4'teki blast radius/rate sınırlamasının bir üst düzey genel rate limiting politikasının parçası olarak da görünmesi — remediation, genel API rate limit havuzundan **ayrı** bir bütçeye sahip olmalı (yüksek event hacmi olan ama hiç remediation kullanmayan bir tenant'ın "bütçesi" başka bir tenant'ın remediation'ına aktarılmamalı — plan/kota izolasyonu burada da geçerli).

---

## 7. Platform Ölçeğinde Observability

### 7.1 Dashboard/Alerting Stratejisi

- **İki katmanlı dashboard modeli:** (1) **platform operasyon dashboard'u** — tüm tenant'lar genelinde toplu sağlık (ingestion gecikmesi, connector başarı oranı, remediation execution başarı/hata oranı, AI özetleme kuyruk derinliği); (2) **tenant-scoped dashboard** — her tenant kendi verisini görür, asla başkasının agregasyonuna dahi erişemez (agregasyon endpoint'lerinin de RLS/tenant filtresine tabi olması, bölüm 1.2'deki "count-based side-channel" riskine karşı).
- **Alerting, tenant-etki-ağırlıklı olmalı** — platform-genelinde "hata oranı %X arttı" alarmı yeterli değil; "hangi tenant'lar etkilendi, kaç tanesi enterprise/kritik plan" bilgisi alarma eşlik etmeli ki on-call doğru önceliklendirebilsin.
- **Connector sağlık matrisı:** connector türü × tenant kesişiminde bir sağlık görünümü (örn. "Stripe connector'ı 40 tenant'ta çalışıyor, bunlardan 3'ü credential-stale, 1'i rate-limited") — bu, tekil connector arızasının aslında geniş bir üçüncü taraf servis kesintisi olup olmadığını (V1'deki "bizim değişikliğimiz mi karşı taraf mı" sorusunun platform ölçeğindeki hali) hızlı ayırt etmeyi sağlar.
- **Remediation-özel gözlemlenebilirlik (V4):** her remediation execution'ının kendi trace'i (dry-run → approval → execution → sonuç) OpenTelemetry span zinciri olarak izlenmeli; bu hem debugging hem de audit trail'i (bölüm 5.5) tamamlayıcı, insan-okunur bir "neden bu kadar sürdü / nerede takıldı" görünümü sağlar.

### 7.2 Noisy Neighbor Önlemi

Bir tenant'ın yüksek event hacmi (örn. büyük bir müşterinin binlerce webhook/dakika göndermesi), paylaşılan altyapıda diğer tenant'ların gecikmesini artırmamalı:

- **Kuyruk/işleme seviyesinde tenant-adil paylaşım (fair scheduling):** event ingestion ve AI özetleme kuyrukları, FIFO değil, tenant bazlı adil paylaşım (round-robin veya weighted fair queuing, plan seviyesine göre ağırlıklandırılmış) ile işlenmeli — tek bir tenant kuyruğu domine edip diğerlerini aç bırakmamalı.
- **Kaynak kotası izolasyonu:** yoğun tenant'lar için, mümkünse ayrı worker pool/partition (örn. connector polling job'ları tenant_id'ye göre partition'lanmış bir iş kuyruğunda çalışır) — bu, "bir tenant'ın job'u patladı, worker pool'u tükendi, herkes etkilendi" senaryosunu sınırlar.
- **Circuit breaker tenant bazlı da olmalı:** V1'in Polly tabanlı circuit breaker'ı büyük olasılıkla connector/servis bazlı; platformda buna ek olarak **tenant bazlı** circuit breaker eklenmeli — bir tenant'ın connector'ı sürekli hata veriyorsa (örn. yanlış konfigüre edilmiş bir webhook sonsuz döngüde retry tetikliyorsa), bu tenant'ın trafiği izole edilip kesilebilmeli, platform-genelinde retry fırtınasına dönüşmemeli.
- **Erken uyarı eşiği:** bir tenant'ın hacmi, o tenant'ın plan/geçmiş ortalamasına göre anormal şekilde arttığında (örn. son 24 saat ortalamasının 10 katı), otomatik olarak geçici bir yumuşak sınırlama (soft throttle) + platform operasyon ekibine bilgi — sert kesme öncesi bir ara adım.

---

## 8. Compliance Hazırlığı Notları (SOC2/GDPR — İleriye Dönük Farkındalık)

Bu bölüm tam bir compliance programı tasarlamaz; V2+'ta gerçek müşteri verisiyle çalışılacağı için **şimdiden mimariye gömülmesi ucuz, sonradan eklenmesi pahalı olacak** noktaları işaretler:

- **Veri envanteri ve sınıflandırma en baştan:** her veri türünün (event metadata, ticket içeriği, connector credential, audit log) hassasiyet sınıfı ve retention süresi şemada/dokümantasyonda açıkça etiketlenmeli — sonradan "hangi tablo PII içeriyor" arkeolojisi yapmak yerine.
- **Silme hakkı (GDPR right to erasure) için tasarım desteği:** tenant/kullanıcı verisi silme talebi geldiğinde, hangi tabloların/hangi downstream sistemlerin (AI özetleme cache'i, audit log — audit log'un silinemez olması ile silme hakkının çatışması ayrı bir tasarım/hukuki karar gerektirir, bu doküman bunu çözmez ama işaretler) etkileneceği baştan haritalanmalı; RLS/tenant_id disiplini (bölüm 1) bu haritalamayı kolaylaştırır çünkü veri zaten tenant sınırlarıyla ayrışmış durumda.
- **Erişim denetimi (access review) altyapısı:** kimin hangi tenant'ın hangi verisine ne zaman eriştiğinin loglanması (bölüm 5.5'teki "audit'i denetleyen audit" deseninin genel hali) SOC2'nin temel taleplerinden; bu, V2'de erken kurulursa V4'te kritik.
- **Şifreleme standartları:** in-transit (TLS her yerde, connector'lar dahil) ve at-rest (bölüm 2.1'deki secret manager + veritabanı seviyesi encryption) — SOC2 denetiminin standart kontrol maddeleri, mimari kararlar şimdiden bunlarla uyumlu.
- **Alt işlemci (sub-processor) şeffaflığı:** her connector, teknik olarak bir "alt işlemci" (müşteri verisini işleyen üçüncü taraf) haline gelir — GDPR/SOC2 bağlamında bu ilişkilerin listelenebilir olması (hangi tenant hangi connector'ı, dolayısıyla hangi üçüncü tarafı kullanıyor) gerekecek; connector manifest modeli (bölüm 2.2) bu listeyi doğal olarak üretebilecek şekilde tasarlanmalı.
- **Incident bildirim yükümlülüğü hazırlığı:** bir veri ihlali durumunda "hangi tenant'lar, hangi veri türü, ne zamandır" sorusuna hızlı cevap verebilmek regülasyon gereği (72 saat GDPR bildirim penceresi gibi) kritik — bölüm 1.2'deki izolasyon test/alarm altyapısı ve bölüm 5.5'teki audit trail bu soruya hızlı cevap vermeyi teknik olarak mümkün kılan altyapıdır.

Not: Bu maddeler "V2'de tam SOC2 sertifikasyonu alınsın" demiyor — sertifikasyon süreci ayrı bir program (politika, üçüncü taraf denetim, personel eğitimi vb. içerir). Burada işaretlenen, **mimarinin sertifikasyon yolunu tıkamaması**, aksine kolaylaştırmasıdır.

---

## 9. Incident Response Olgunluk Yol Haritası

Platformun kendi incident response (kendi altyapısında bir şey bozulduğunda) olgunluğu, fazlarla birlikte artmalı — bu, FI ürününün müşterilere sunduğu yeteneğin platformun **kendi** operasyonuna da uygulanması anlamına gelir (dogfooding fırsatı, ayrıca not edilir).

| Faz | Olgunluk seviyesi | Karakteristik |
|---|---|---|
| **V1** | Reaktif, temel | Health check endpoint'leri (liveness/readiness), correlation-id ile log izlenebilirliği, Polly retry/circuit breaker ile geçici hataların otomatik toparlanması. Alarm = insan log'a bakar. On-call resmi değil (tek/küçük ekip). |
| **V2** | Yapılandırılmış, tenant-farkında | Bölüm 7'deki tenant-etki-ağırlıklı alarming devreye girer. İlk resmi "P1/P2/P3" önem sınıflandırması ve yanıt süresi hedefleri (SLO değil ama iç hedef). Postmortem şablonu standartlaşır (V1'in kendi FI ürününün ürettiği kök-neden özeti formatına benzer şekilde — kendi ürününü kendi incident'larında kullanmayı dener). |
| **V3** | Proaktif, runbook-destekli | Platformun kendi incident'larında Runbook Engine'in ürettiği öneriler kullanılır (dogfooding — hem ürünü test eder hem incident yanıtını hızlandırır). Noisy neighbor / connector sağlık matrisi (bölüm 7.1) proaktif tespit sağlar — incident olay olmadan önce degradasyon sinyali yakalanır. Resmi on-call rotasyonu ve eskalasyon politikası. |
| **V4** | Tam SRE pratiği | Hata bütçesi (error budget) ve SLO'lar tanımlı (tenant-plan bazlı farklılaşmış olabilir — enterprise için daha sıkı). Remediation Engine'in ürettiği audit trail, postmortem'in kanıt kaynağı haline gelir. Chaos engineering (kontrollü hata enjeksiyonu — özellikle blast radius/rollback garantilerini gerçek koşullarda doğrulamak için, bölüm 5) düzenli pratik. Cross-tenant güvenlik/izolasyon testleri (bölüm 1.2) sürekli/otomatik pentest ritmine bağlanır, tek seferlik değil. |

Bu yol haritasının altında yatan ilke: her faz, bir önceki fazın **manuel/reaktif** pratiğini **yapılandırılmış/proaktif** hale getiriyor — ve platform, kendi büyüyen karmaşıklığına (multi-tenant, connector sayısı, remediation gücü) orantılı bir olgunlukla yanıt veriyor, geriden gelmiyor.

---

## 10. Connector Onboarding Güvenlik Checklist'i

Her yeni connector türü (Stripe, HubSpot, Salesforce, ...) platforma eklenmeden önce, aşağıdaki kontrol listesi geçilmeden **prod'a alınmaz**. Bu, "her yeni connector = yeni saldırı yüzeyi" gerçeğini süreçsel olarak yönetir:

1. **Credential modeli tanımlı mı?** Hangi credential türü (API key / OAuth token / webhook secret), hangi scope'larla (bölüm 2.2), secret manager'da hangi path altında saklanacak.
2. **Minimum scope doğrulandı mı?** Connector'ın gerçekte ihtiyaç duyduğu en dar izin seti belirlendi mi (örn. "read-only" yeterliyse "write" istenmiyor); üçüncü taraf servisin restricted-key/scoped-token mekanizması varsa o kullanılıyor mu.
3. **Rate limit davranışı dokümante edildi mi?** Üçüncü taraf servisin resmi rate limit'i biliniyor mu, connector implementasyonu client-side throttling ile buna uyuyor mu (bölüm 6).
4. **Hata sınıflandırması tanımlı mı?** Connector'a özel hata kodlarının (401/403/429/5xx + servis-özel hata formatları) platform-genelinde hangi kategoriye (credential-stale, rate-limited, transient, permanent) map edileceği belirlendi mi.
5. **Redaction kapsamı genişletildi mi?** Bu connector'ın taşıdığı veri türlerinde (bölüm 3'teki gibi freeform alan var mı) PII/secret redaction pipeline'ının kapsaması gereken yeni alan/patern var mı, test edildi mi.
6. **Remediation kataloğu (varsa, V4+) gözden geçirildi mi?** Bu connector için önerilecek/izin verilecek remediation aksiyonları var mı; varsa her biri risk seviyesi, rollback stratejisi (otomatik/manuel/geri alınamaz) ve blast radius tahminiyle katalogda tanımlı mı (bölüm 4-5).
7. **İzolasyon testi eklendi mi?** Bu connector'ın verisi/ayarları, tenant izolasyon test paketine (bölüm 1.2) dahil edildi mi — yeni connector türü, yeni bir tenant-scoped veri modeli getiriyorsa bu zorunlu.
8. **Rotation/expiry davranışı doğrulandı mı?** Credential süresi dolduğunda/rotate edildiğinde connector'ın "degraded" durumuna doğru geçtiği (bölüm 2.3) manuel/otomatik test ile doğrulandı mı — sessizce başarısız olma (silent failure) senaryosu ekarte edildi mi.
9. **Üçüncü taraf servisin kendi güvenlik/uptime geçmişi değerlendirildi mi?** (Örn. bilinen büyük ihlal geçmişi, SLA garantileri) — bu bir "connector güven skoru" olarak platformda saklanabilir, düşük güvenli connector'lar sandbox-mode (bölüm 5.2) varsayılanıyla başlayabilir.
10. **Offboarding/silme yolu tanımlı mı?** Tenant bu connector'ı kaldırdığında, credential'ın secret manager'dan silindiği, ilgili verinin retention politikasına göre işlendiği (bölüm 8'deki silme hakkı ile uyumlu) doğrulandı mı.

---

## 11. Özet — Fazlar Arası Risk/Kontrol Matrisi

| Faz | Yeni risk | Ana kontrol |
|---|---|---|
| V2 — Multi-tenancy | Tenant'lar arası veri sızıntısı | RLS (taban) + query filter (ek katman) + otomatik izolasyon test paketi |
| V2 — Connector Framework | Üçüncü taraf credential ifşası, aşırı geniş yetki | Secret manager + minimum scope manifest + rotation-farkında health check |
| V2 — Support Correlation | Serbest metinde yoğun PII/secret | İki geçişli redaction (PII + secret) + confidence-tabanlı insan gözden geçirme + ayrı RBAC sınıfı |
| V3 — Runbook Engine | Yanlış/tehlikeli önerinin körü körüne uygulanması | Sabit aksiyon kataloğu (serbest metin değil) + risk etiketleme + tenant/rol bazlı izin ayrımı |
| V4 — Controlled Remediation | Yetkisiz/hatalı gerçek dünya aksiyonu, geri alınamaz hasar | State machine zorunlu onay + dry-run + rollback sınıflandırması + blast radius sınırı + immutable audit trail |
| Platform geneli | Noisy neighbor, ölçek arttıkça alarm yorgunluğu | Tenant-adil kuyruklama + tenant bazlı circuit breaker + tenant-etki-ağırlıklı alerting |

Bu matris, dokümanın geri kalanının ayrıntılandırdığı kararların tek sayfalık referansı olarak kullanılabilir.
