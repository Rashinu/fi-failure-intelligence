# AI Integration Operations Platform (OP) — Ürün & Domain Analizi

**Doküman türü:** Uzun vadeli vizyon / domain analizi (MVP tanımı değildir)
**Kapsam:** V1 sonrası — yani "AI Integration Failure Intelligence" (FI) tamamlandıktan sonra platformun nasıl büyüyeceği
**Durum:** Taslak — validasyon öncesi
**Tarih:** 2026-07-12

---

## 0. Bu Doküman Ne Değildir

- Bu, FI'nin MVP kapsamını tekrar tanımlayan bir doküman değildir. FI ayrı bir ekip tarafından ayrı olarak analiz ediliyor ve kendi domain dokümantasyonuna sahip. Burada FI'ye yalnızca "V1" olarak, temel taş olarak referans verilir.
- Bu bir sprint planı veya backlog değildir. Bugün kod yazılmayacak; bu doküman "OP nereye gidiyor, neden, hangi sinyallerle" sorularına cevap arayan bir konumlandırma ve kapsam netleştirme dokümanıdır.
- Bu doküman FI'nin başarısını varsaymaz. FI validate olmadan OP'a geçiş gerekçesizdir (bkz. Bölüm 10).

---

## 1. Vizyon Cümlesi ve Konumlandırma

> **"Know what failed, why it failed, who is affected, and what to do next."**

FI bu cümlenin ilk iki parçasını (ne, neden) tek entegrasyon / tek ekip ölçeğinde çözer. OP aynı cümleyi **çoklu entegrasyon, çoklu ekip, çoklu müşteri (tenant) ölçeğinde** ve cümlenin son iki parçasına (kimler etkilendi, şimdi ne yapılmalı) tam kapsamlı cevap üretecek şekilde genişletir.

Rekabet araştırması (bkz. Kaynaklar) OP'un konumlanacağı boşluğu netleştiriyor:

- **Genel gözlemlenebilirlik / AIOps platformları** (Datadog Watchdog+Bits AI, PagerDuty AIOps/Event Intelligence) altyapı/metrik/log seviyesinde sinyal korelasyonu yapar; "bu deploy mu bozdu, bu metrik mi anormal" sorusuna cevap verir. Entegrasyon-özel semantiği (hangi webhook, hangi connector, hangi üçüncü taraf servis, hangi credential) bilmezler — bunu sizin etiketlemeniz/instrument etmeniz gerekir.
- **Incident yönetim platformları** (Rootly, incident.io, FireHydrant — 2026 itibarıyla Freshworks'e satıldı) incident'ın *yaşam döngüsünü* (declare → coordinate → postmortem) mükemmelleştirir, çoğunlukla Slack-native. AI-native kök neden analizi (Rootly'nin confidence-scored RCA'sı, incident.io'nun PR-citation yapan AI SRE'si) gündemde, ama bunlar da entegrasyon/connector seviyesinde değil, genel mühendislik incident'ları seviyesinde çalışır.
- **Unified API / connector platformları** (Merge.dev, Nango, Unified.to, Apideck) entegrasyonun kendisini (bağlantıyı kurmayı, şemayı normalize etmeyi) satar; Merge "gözlemlenebilirlik" iddiası taşısa da bu, bağlantının sağlığını izlemekle sınırlıdır — incident yönetimi, root-cause, runbook, postmortem gibi operasyonel katmanları içermez.
- **iPaaS / workflow otomasyon platformları** (Workato, Tray.ai, Zapier, n8n) entegrasyonu *çalıştırır* (orkestrasyon), ama bir entegrasyon bozulduğunda "neden bozuldu, kim etkilendi, nasıl çözülür" sorusuna odaklı bir ürün değildir — hata orada bir "workflow run failed" log satırıdır, incident değildir.

**OP'un boşluğu:** Hiçbiri "üçüncü taraf entegrasyon hatası" özelinde uçtan uca zinciri (kayıt → olay → incident → kök neden → runbook → kontrollü remediation) tek bir domain modeliyle kapatmıyor. OP genel gözlemlenebilirlik platformu **değildir** ve genel workflow otomasyon platformu **değildir** — entegrasyon-özelinde incident + runbook + (ileride) remediation platformudur. Bu yüzden connector ekosistemine (Stripe, GitHub, SES, SendGrid, HubSpot vb.) ve entegrasyon domain kavramlarına (credential reference, SLA, owner) Datadog/PagerDuty'nin sahip olmadığı kadar derinlemesine sahip olmak zorundadır; buna karşılık incident workflow ve postmortem'de Rootly/incident.io kadar olgun olmayı hedeflemez — bu alanlarda "yeterince iyi" olup asıl farkı entegrasyon-özel bağlamda yaratır.

---

## 2. OP'un Çözdüğü Problem (FI'dan Farkı)

FI'nin çözdüğü problem: **"Bu entegrasyon şu an neden başarısız oluyor?"** — tek servis, tek ekip, reaktif teşhis.

OP'un çözdüğü problem, organizasyonel ölçekte üç ayrı boşluk:

1. **Parçalanmış görünürlük boşluğu:** Bir ajans veya orta ölçekli SaaS ekibinin 5-50 arası aktif entegrasyonu (Stripe, HubSpot, SendGrid, özel webhook'lar...) farklı dashboard'larda, farklı sahiplerde, farklı log sistemlerinde yaşar. Kimse "şu an kaç entegrasyon sağlıklı, kaç tanesi degrade" sorusuna tek bakışta cevap veremez. FI bunu tek entegrasyon için çözer; OP portföy seviyesinde çözer.
2. **Koordinasyon boşluğu:** Bir entegrasyon hatası genelde tek kişinin sorunu değildir — support bir ticket açar, integration team bir webhook hatası görür, payment ekibi bir ödeme tutarsızlığı fark eder. Bugün bu üç sinyal birbirine bağlanmaz; aynı kök nedenin üç farklı "ilk keşfi" olur. OP bunları tek incident'ta birleştirip (support correlation) sorumluluk, iletişim ve durum takibini standardize eder (incident workflow: assign/acknowledge/resolve/reopen/postmortem/audit).
3. **Tekrarlayan çözüm kaybı boşluğu:** Aynı entegrasyon hatası (örn. "Stripe webhook signature mismatch after key rotation") ayda bir tekrar eder ama her seferinde biri sıfırdan teşhis eder çünkü geçmiş çözüm bir Slack mesajında ya da bir kişinin kafasında kalmıştır. OP bunu runbook engine ile kurumsal hafızaya dönüştürür; V4'te (insan onaylı) kontrollü otomatik düzeltmeye taşır.

Özetle: FI **teşhis hızını** artırır (tek olay, tek ekip). OP **organizasyonel koordinasyonu ve kurumsal hafızayı** entegrasyon operasyonlarına ekler (çoklu olay, çoklu ekip, çoklu müşteri, zaman içinde öğrenme).

---

## 3. Hedef Persona'lar ve Jobs-to-be-Done (Organizasyonel Ölçek)

FI'nin persona'ları büyük olasılıkla "entegrasyonu yöneten mühendis" seviyesindeydi. OP bir üst organizasyonel katmanı hedefler:

### 3.1 Platform / Integration Team Lead
- **JTBD:** "Ekibimin sahip olduğu tüm üçüncü taraf entegrasyonların sağlık durumunu tek yerden görmek istiyorum, böylece hangi entegrasyonun teknik borç/risk taşıdığını üst yönetime raporlayabilirim."
- **JTBD:** "Yeni bir connector eklerken (örn. yeni bir CRM), mevcut incident/runbook altyapısını yeniden icat etmek istemiyorum — standardize bir çerçevede eklemek istiyorum." → Connector Framework (V2)
- **Acı noktası:** Her entegrasyon kendi ad-hoc monitoring'ini kurmuş; tutarlı bir SLA/owner kaydı yok.

### 3.2 Support Engineering Manager
- **JTBD:** "Destek ekibim aynı entegrasyon sorunuyla ilgili 40 ticket alıyor; bunların tek bir teknik incident'a bağlı olduğunu erken fark edip müşterilere tutarlı, doğru bir mesaj vermek istiyorum."
- **JTBD:** "Support engineer'larımın bir ticket'ı ne zaman 'teknik incident' olarak eskale edeceğini net kriterlerle bilmesini istiyorum, sezgiye bırakmak istemiyorum." → Support Correlation (V2)
- **Acı noktası:** Support ve mühendislik ayrı sistemlerde (Zendesk/Intercom vs. dahili incident tool) çalışıyor, elle köprüleme yapılıyor, gecikme ve tutarsız mesajlaşma oluyor.

### 3.3 Yazılım Ajansı CTO'su / Teknik Direktörü
- **JTBD:** "10 farklı müşterinin entegrasyonlarını (her biri farklı Stripe/HubSpot/e-posta sağlayıcı kombinasyonuyla) tek bir merkezi panelden, müşteri bazında izole (multi-tenant) şekilde izlemek istiyorum, böylece her müşteri için ayrı ayrı araç kurmak zorunda kalmıyorum."
- **JTBD:** "Bir müşteri sorunu diğer müşterinin veri/credential'ına asla sızmasın istiyorum — güçlü tenant izolasyonu şart."
- **Acı noktası:** Bugün her müşteri projesi kendi izole monitoring'ini kuruyor; ajansın kurumsal öğrenmesi (runbook) müşteriler arası paylaşılmıyor, her seferinde tekerlek yeniden icat ediliyor.

### 3.4 (Devam eden) SaaS Ürün Ekibi Lideri — FI'dan miras, OP'ta genişler
- **JTBD (V1'de vardı):** "Entegrasyonum neden bozuldu?"
- **JTBD (OP'ta eklenen):** "Geçmişte benzer bir incident nasıl çözüldü, aynı adımları tekrar uygulayabilir miyim?" → Runbook Engine (V3)
- **JTBD (OP'ta eklenen, V4):** "Düşük riskli, tekrar eden bir düzeltmeyi (örn. token yenileme) insan onayıyla otomatik uygulatmak istiyorum, her seferinde elle yapmak istemiyorum."

---

## 4. Scope: FI Üzerine Ne Eklenir (V2 → V3 → V4)

Bu doküman FI MVP'sinin (Integration Registry V1, Event Ingestion V1, Incident Engine V1, Root Cause Intelligence V1) *ne olduğunu* tekrar tanımlamaz. Sadece üzerine inşa edileni tanımlar.

### V2 — Incident Intelligence (organizasyonel koordinasyon katmanı)
- **Incident Workflow:** assign, acknowledge, resolve, reopen, postmortem, audit trail. FI'de incident zaten "oluşturulur"; V2 onun *yaşam döngüsünü* ekler.
- **Connector Framework:** Stripe/GitHub/SES/SendGrid/HubSpot/özel API connector'ların standardize edilmiş şekilde eklenmesi — kimlik doğrulama, olay şeması normalize etme, sağlık kontrolü sözleşmesi.
- **Support Correlation:** Destek ticket kümelerinin teknik incident'larla otomatik ilişkilendirilmesi.

### V3 — Operations Platform (kurumsal hafıza katmanı)
- **Runbook Engine:** Geçmiş incident'lardan ve bağlı dokümantasyondan çözüm adımı önerisi çıkarma.
- Bu noktada "Operations Platform" adı tam anlamıyla kazanılır: artık sadece incident'ları yönetmiyor, geçmişten öğreniyor.

### V4 — Controlled Automated Remediation (otomasyon katmanı)
- **Controlled Remediation:** İnsan onaylı, sınırlı kapsamlı otomatik düzeltme aksiyonları (örn. token yenileme, retry tetikleme, rate-limit ayarı). Sınırsız/insansız otomasyon bu vizyonda dahi V4 sonrası bile hedeflenmiyor — "controlled" kelimesi kasıtlı.

Bu üç faz arasındaki bağımlılık zinciri katıdır: Connector Framework olmadan Support Correlation ölçeklenemez (her connector'ın olay şeması farklıysa korelasyon kuralları patlar); Runbook Engine, Incident Workflow'un ürettiği postmortem verisi olmadan besleyecek veri bulamaz; Controlled Remediation, Runbook Engine'in ürettiği güvenilir aksiyon önerileri olmadan anlamsızdır.

---

## 5. Bu Aşamada KESİNLİKLE Geliştirilmeyecek Olanlar

Bu bir vizyon dokümanı olduğu için kapsam dışı bırakmak, kapsam içi bırakmaktan daha önemli. Bugün (FI MVP aşamasında) OP kapsamında **geliştirilmeyecek**:

- Connector Framework'ün kendisi (herhangi bir "resmi" Stripe/HubSpot/SES connector SDK'sı) — bugün FI'de olay alımı elle/webhook bazlı, standardize bir connector katmanı yok.
- Incident workflow durumları (assign/acknowledge/resolve/reopen) — FI'de incident basitçe "oluşturulan ve kapatılan" bir kayıttır, resmi bir durum makinesi yok.
- Postmortem şablonları, postmortem otomasyonu.
- Support ticket entegrasyonu (Zendesk/Intercom/Freshdesk bağlantısı) ve support-teknik korelasyon mantığı.
- Runbook önerisi/öneri motoru — geçmiş verilerden otomatik "şunu dene" çıkarımı.
- Herhangi bir otomatik remediation (insan onaylı dahi) — hiçbir sistem bugün OP adına 3. taraf sisteme yazma/aksiyon alma yetkisine sahip değil.
- Multi-tenant mimarisi (ajans senaryosu) — bugünkü FI tek organizasyon/tek workspace varsayımıyla çalışıyor; tenant izolasyonu, tenant-bazlı billing, tenant-bazlı RBAC tasarlanmadı.
- SLA takibi, SLA ihlali bildirimleri.
- Credential Reference yönetimi (secrets vault entegrasyonu, rotasyon takibi) — bugün en fazla "hangi credential'ın kullanıldığına dair referans" düzeyinde bir alan olabilir, aktif secret yönetimi değil.
- Herhangi bir "genel AIOps" iddiası (metrik/log anomali tespiti, altyapı seviyesi correlation) — bu, Datadog/PagerDuty'nin alanı; OP bunu yeniden inşa etmeyi hedeflemiyor.
- Kendi genel amaçlı workflow/otomasyon motoru (Zapier/Workato/n8n benzeri, entegrasyon dışı iş süreçlerini de kapsayan bir "her şeyi otomatikleştir" motoru).

Kısacası: **bugün sadece FI'nin dar kapsamlı MVP'si geliştiriliyor; bu doküman FI ekibinin roadmap'ini genişletme talebi değildir, gelecekteki yön için ortak bir referans çerçevesidir.**

---

## 6. Genişletilmiş Domain Glossary (FI Glossary'sine Ek)

FI'nin sözlüğünde muhtemelen zaten var olan temel kavramlara (Event, Incident, Fingerprint, Evidence, Root Cause Candidate vb.) OP şu ek/farklı kavramları getirir:

| Terim | Tanım |
|---|---|
| **Tenant** | OP'u kullanan izole organizasyon birimi (bir müşteri şirketi, ya da bir ajansın yönettiği bir müşteri projesi). Her tenant'ın kendi Integration Registry, Incident geçmişi ve erişim kontrolü vardır; tenant'lar arası veri sızıntısı olmaması temel güvenlik gereksinimidir. |
| **Connector** | Belirli bir üçüncü taraf servis (Stripe, GitHub, SES, SendGrid, HubSpot, özel API) ile OP arasındaki standardize entegrasyon adaptörü: kimlik doğrulama şeması, olay normalize etme kuralları, sağlık kontrolü sözleşmesi ve (varsa) remediation aksiyonları içerir. FI'deki "Event Ingestion"ın connector-özel, yeniden kullanılabilir hali. |
| **Connector Health Contract** | Bir connector'ın "sağlıklı" sayılması için sağlaması gereken minimum sinyal seti (heartbeat, hata oranı eşiği, son başarılı senkronizasyon zamanı). |
| **Credential Reference** | Bir entegrasyonun hangi kimlik bilgisine (API key, OAuth token) bağlı olduğunu gösteren, gerçek secret değerini tutmayan, sadece referans/rotasyon durumu taşıyan kayıt. Gerçek secret yönetimi kapsam dışıdır (bkz. Bölüm 5). |
| **SLA (Integration SLA)** | Bir entegrasyon için tanımlanmış kabul edilebilir performans/uptime/yanıt süresi eşiği; ihlal edildiğinde incident önceliğini etkiler. |
| **Owner** | Bir entegrasyonun (Integration Registry kaydının) organizasyonel sorumlusu — kişi veya takım. |
| **Incident Workflow State** | Bir incident'ın yaşam döngüsündeki durumu: `new → acknowledged → assigned → in_progress → resolved → (reopened) → closed`. FI'de incident'ın sadece "açık/kapalı" olduğu varsayılırken, OP bu durum makinesini resmileştirir. |
| **Postmortem** | Bir incident kapandıktan sonra yazılan, kök neden, zaman çizelgesi, etki ve alınan aksiyonları belgeleyen yapılandırılmış rapor; runbook motorunun ana veri kaynağıdır. |
| **Support Correlation** | Bir destek ticket kümesinin (benzer şikayet paterni gösteren birden fazla ticket) bir teknik incident ile otomatik ya da yarı-otomatik olarak eşleştirilmesi süreci ve bu eşleştirmeyi temsil eden kayıt. |
| **Runbook** | Geçmişte bir incident tipini çözmek için izlenen, yapılandırılmış adım listesi; benzer fingerprint'e sahip yeni incident'larda öneri olarak sunulur. |
| **Runbook Confidence** | Bir runbook önerisinin mevcut incident'a ne kadar uyduğuna dair güven skoru (geçmiş başarı oranı, fingerprint benzerliği temelinde). |
| **Controlled Remediation Action** | İnsan onayı gerektiren, sınırlı ve geri alınabilir otomatik düzeltme aksiyonu (örn. webhook secret'ını yeniden senkronize et, başarısız event'leri yeniden gönder). Onaysız/otonom aksiyon bu tanımın dışındadır. |
| **Approval Gate** | Bir Controlled Remediation Action'ın uygulanmadan önce geçmesi gereken insan onay adımı; kim onaylayabilir, hangi aksiyonlar onay gerektirir gibi kuralları içerir. |
| **Integration Portfolio** | Bir tenant'a (ya da ajans senaryosunda bir müşteriye) ait tüm entegrasyonların toplu görünümü — sağlık durumu, açık incident sayısı, SLA durumu dahil. |
| **Cross-Tenant Isolation Boundary** | Bir tenant'ın verisinin, credential referanslarının ve runbook'larının başka bir tenant'a hiçbir koşulda görünür/erişilir olmamasını garanti eden mimari sınır. |

---

## 7. V2–V4 Seviyesinde Ana Use Case'ler

1. **(V2 – Connector Framework + Portfolio görünürlüğü)** Bir ajans, 5 müşterisinin Stripe entegrasyonunu tek dashboard'dan, her müşteri tenant'ı izole tutularak izler; hangi müşteride entegrasyon "degraded" olduğunu tek bakışta görür.
2. **(V2 – Support Correlation)** Destek ekibine 45 dakika içinde 12 farklı müşteriden "ödeme onayı gelmiyor" ticket'ı düşer; sistem bu ticket kümesini otomatik olarak zaten açık olan "Stripe webhook gecikmesi" incident'ına bağlar, support ekibine tek, tutarlı bir müşteri mesajı şablonu önerir.
3. **(V2 – Incident Workflow)** Bir entegrasyon lead'i incident'ı kendine assign eder, "in progress" olarak işaretler, çözünce postmortem şablonunu doldurur; bu postmortem otomatik olarak Integration Registry'deki ilgili entegrasyon kaydına bağlanır (geçmiş incident sayısı, ortalama çözüm süresi gibi metrikler oluşur).
4. **(V3 – Runbook Engine)** Aynı fingerprint'e sahip yeni bir "HubSpot OAuth token expired" incident'ı açıldığında, sistem geçmişte 8 kez uygulanan ve %90 başarı oranına sahip runbook'u ("token'ı yeniden yetkilendir, webhook'ları yeniden kaydet") önerir; mühendis tek tıkla adımları görür.
5. **(V3 – Cross-tenant runbook paylaşımı, ajans senaryosu)** Ajans, bir müşteride keşfettiği "SendGrid rate-limit" runbook'unu (müşteri verisi anonimleştirilmiş şekilde) diğer müşterilerin benzer incident'larında da öneri olarak kullanabilir.
6. **(V4 – Controlled Remediation)** Sistem, düşük riskli olarak sınıflandırılmış bir aksiyonu (örn. başarısız webhook event'lerini yeniden gönderme) önerir; Integration Team Lead onaylar; sistem aksiyonu uygular ve sonucu incident zaman çizelgesine kaydeder. Onay olmadan hiçbir aksiyon tetiklenmez.
7. **(V4 – Yaygınlaşan güven)** Belirli bir aksiyon tipi (örn. token refresh) 20+ kez insan onayıyla başarıyla uygulandıktan sonra, ekip bu aksiyon tipi için "otomatik onay" politikası tanımlayabilir — ama bu bile varsayılan davranış değil, açık bir organizasyonel karardır.

---

## 8. Rakiplerden Farklılaşma (Dürüst Konumlandırma)

| Kategori | Temsilci oyuncular | Ne yapıyorlar | OP ile farkı |
|---|---|---|---|
| Genel gözlemlenebilirlik + AIOps | Datadog (Watchdog, Bits AI), PagerDuty (AIOps/Event Intelligence, Process Automation/Rundeck) | Metrik/log/APM seviyesinde anomali tespiti, alert korelasyonu, genel runbook otomasyonu (Rundeck kökenli). 2025-2026'da "agentic" runbook yürütme eklediler. | OP entegrasyon-özel semantiği (hangi 3. taraf servis, hangi connector, hangi credential) yerleşik olarak bilir; Datadog/PagerDuty'de bu bağlamı siz kurmak zorundasınız. OP genel altyapı/metrik izleme iddiasında değildir — bunları tamamlayıcı olarak görür, yerine geçmez. |
| Incident yaşam döngüsü platformları | Rootly, incident.io, FireHydrant (2026'da Freshworks'e satıldı) | Slack-native incident declare/coordinate/postmortem akışı; AI destekli kök neden özetleme (PR citation, confidence-scored RCA). | Bu platformlar genel mühendislik incident'ları için optimize; entegrasyon/connector domain modeli (Integration Registry, Credential Reference, Connector Health Contract) yok. OP'un Incident Workflow'u (V2) bu ürünler kadar zengin olmayı hedeflemez — asıl derinlik connector ve root-cause tarafındadır. |
| Unified API / connector platformları | Merge.dev, Nango, Unified.to, Apideck | Entegrasyon bağlantısını standardize eder, şema normalize eder; Merge kendi "gözlemlenebilirlik" özelliklerine sahip ama bu bağlantı sağlığıyla sınırlı. | Bu platformlar bağlantıyı *kurar*; OP bağlantı *bozulduğunda* ne olacağını yönetir (incident, kök neden, runbook, remediation). OP kendi unified API'sini inşa etmeyi hedeflemez — connector framework'ü kendi domain modeli için bir adaptasyon katmanıdır, genel amaçlı bir entegrasyon SDK'sı satmak değildir. |
| iPaaS / workflow otomasyon | Workato, Tray.ai, Zapier, n8n | İş süreçlerini/entegrasyonları orkestre eder (trigger → action zincirleri); "workflow run failed" seviyesinde hata görünürlüğü verir. | Bu platformlarda hata bir log satırıdır, incident değildir; kök neden analizi, postmortem, runbook kavramı yoktur. OP genel amaçlı bir workflow motoru inşa etmeyi hedeflemez — sadece kontrollü, onaylı, dar kapsamlı remediation aksiyonları (V4) çalıştırır. |

**Net konumlandırma cümlesi:** OP, "genel gözlemlenebilirlik" (Datadog/PagerDuty), "genel incident yönetimi" (Rootly/incident.io) veya "genel workflow otomasyonu" (Workato/Zapier/n8n) olmaya çalışmaz. OP, üçüncü taraf entegrasyon hatalarına özel — entegrasyon domain modelini (Registry, Connector, Credential Reference, SLA) merkeze koyan — bir incident + kök neden + runbook + kontrollü remediation zinciridir. Derinliği genel işlevsellikte değil, entegrasyon operasyonları dikeyinde (vertical) aranmalıdır.

---

## 9. Platform Seviyesi Riskler

1. **"Kitchen sink" ürün riski:** V1→V4 yol haritası, sırasıyla incident yönetimi (Rootly'nin alanı), connector altyapısı (Merge'in alanı) ve workflow otomasyonu (Workato'nun alanı) ile örtüşen özellikler üretiyor. Her fazda "madem incident workflow yapıyoruz, postmortem şablon editörü de ekleyelim" gibi kapsam sürünmesi riski yüksek. Disiplin: her yeni özellik "entegrasyon-özel bağlamı" güçlendiriyor mu, yoksa genel bir SaaS kategorisini yeniden mi icat ediyor sorusuyla test edilmeli.
2. **Connector ekosistemi genişletme maliyeti:** Her yeni connector (Stripe, HubSpot, SES...) sadece bir API entegrasyonu değil, kalıcı bakım yüküdür — üçüncü taraf API'ler versiyon değiştirir, rate limit politikaları değişir, webhook şemaları kırılır. Connector sayısı arttıkça bakım maliyeti doğrusal değil süper-doğrusal büyür (her connector'ın kendi edge-case'leri, kendi test suite'i gerekir). Bu, Merge.dev'in 220+ entegrasyonluk yatırımının gösterdiği gibi ciddi bir mühendislik yatırımı gerektirir; OP'un bunu "yan iş" olarak değil, ayrı bir yatırım kararı olarak görmesi gerekir.
3. **Çoklu tenant karmaşıklığı:** Ajans senaryosu (bir OP kurulumunda birden fazla müşteri tenant'ı) veri izolasyonu, RBAC, billing, ve runbook paylaşımı (bir müşterinin öğrendiği çözümün başka müşteriye anonimleştirilerek aktarılması) gibi zor problemler doğurur. Yanlış tasarlanan tenant izolasyonu güvenlik açığı (bir müşterinin verisinin/credential referansının diğerine sızması) riski taşır — bu FI'de hiç var olmayan, OP'a özgü yeni bir tehdit yüzeyidir.
4. **Runbook güven yanılgısı riski:** Otomatik önerilen runbook'lar yanlış bağlamda uygulanırsa (fingerprint benzerliği yanıltıcıysa) sorunu çözmek yerine büyütebilir. Confidence skorlama mekanizması yanlış kalibre edilirse kullanıcı güveni ya gereksiz düşük (araç kullanılmaz) ya da tehlikeli derecede yüksek (kör güvenle uygulanır) olur.
5. **Controlled Remediation'ın "controlled" kalması:** V4'te otomasyon genişledikçe, iş baskısı ("neden hâlâ onay bekliyoruz, otomatikleştirelim") approval gate'leri zayıflatma yönünde baskı yaratır. Bir üretim ortamında yanlış remediation aksiyonu (örn. yanlış müşterinin webhook'unu yeniden tetiklemek) veri bütünlüğü veya müşteri güveni açısından ciddi hasar verebilir. Bu riskin kurumsal politika seviyesinde (sadece teknik olarak değil) yönetilmesi gerekir.
6. **Rakip platformların "AI-native" ivmesi:** Rootly ve incident.io gibi oyuncular 2025-2026'da hızla AI-native RCA özellikleri ekliyor (confidence-scored kök neden, PR-citation). OP'un connector-özel derinliği bir moat olsa da, bu oyuncuların genel entegrasyon bağlamını (örn. "hangi API çağrısı" seviyesinde) yakalamaya başlaması durumunda farklılaşma daralabilir; bu bir zaman baskısı riski yaratır ama "hız için derinlikten ödün verme" tuzağına düşülmemeli.
7. **Doğrulanmamış talep riski:** Bu doküman bir vizyon dokümanıdır; V2-V4'teki hiçbir özellik için henüz gerçek kullanıcı talebi (FI kullanıcılarından gelen sinyal) doğrulanmamıştır. En büyük risk, FI henüz kanıtlanmadan OP'un mühendislik kapasitesini bağlaması, ya da FI kanıtlansa bile OP'un varsaydığı persona'ların (ajans CTO'su, support manager) gerçekte bu ölçekte bir araç için ödeme yapmaya istekli olmamasıdır.

---

## 10. FI'dan OP'a Geçiş: Validasyon Varsayımları

Bu vizyona göre hareket etmeden önce, aşağıdaki sinyallerin **gerçekleşmiş olması** gerekir. Bunlar birer varsayımdır, kanıtlanmış gerçek değildir — bu yüzden net ve ölçülebilir tutulmuştur:

1. **FI çekiş kanıtı:** FI, en az bir gerçek (ücretli ya da aktif kullanımlı) kullanıcı segmentinde, tanımlı bir süre boyunca (örn. 4-8 hafta) tutarlı kullanım gösterir — yani tek seferlik "ilginç demo" değil, tekrarlayan gerçek incident teşhisinde kullanılıyor.
2. **Çoklu entegrasyon sinyali:** FI kullanıcılarının belirgin bir kısmı (örn. %30+) organik olarak "birden fazla entegrasyonu aynı anda takip etmek istiyorum" ihtiyacını dile getirir veya bunu FI'yi birden fazla kez, birden fazla servis için kurarak davranışsal olarak gösterir.
3. **Koordinasyon acısı sinyali:** Kullanıcı geri bildiriminde (destek biletleri, satış görüşmeleri, kullanıcı röportajları) "bu incident'ı ekibime nasıl atarım", "support ekibim bunu nasıl görsün" gibi Incident Workflow ihtiyacına işaret eden tekrarlayan talepler birikir.
4. **Ajans/çoklu-müşteri talebi:** En az bir gerçek yazılım ajansı ya da çoklu-müşteri yöneten ekip, FI'yi (tek tenant sınırlamasına rağmen) birden fazla müşteri projesi için kullanmaya çalışır veya bu ihtiyacı açıkça talep eder — bu, multi-tenant yatırımının gerçek talep karşılığı olduğunun kanıtıdır.
5. **Connector bakım maliyeti kabul edilebilir:** İlk 2-3 connector'ın (Stripe + 1-2 diğeri) bakım yükü ölçülür ve bu yükün sürdürülebilir olduğu (ekip kapasitesiyle orantılı) doğrulanır — aksi halde Connector Framework yatırımı erken yapılmamalıdır.
6. **Rekabetçi boşluk hâlâ açık:** Geçiş kararı anında rakip taraması tekrarlanır — eğer Rootly/incident.io ya da Datadog bu süre zarfında entegrasyon-özel derinliğe (connector-seviyesi domain modeli) yatırım yapmışsa, OP'un farklılaşma tezi yeniden değerlendirilmelidir.

Bu sinyallerin hiçbiri tek başına yeterli değildir; OP'a geçiş kararı, en az maddeler (1), (2 veya 3) ve (4)'ün birlikte gerçekleşmesini gerektiren bir kapı (gate) olarak ele alınmalıdır.

---

## Kaynaklar

- [5 best AI-powered incident management platforms 2026 — incident.io](https://incident.io/blog/5-best-ai-powered-incident-management-platforms-2026)
- [Rootly: Best Incident.io Alternatives for Modern Incident Management Teams in 2026](https://rootly.com/blog/best-incident-io-alternatives-for-modern-incident-management-teams-in-2026)
- [Rootly vs FireHydrant vs incident.io: Slack-Native Incident Tools (2026)](https://pingfatigue.com/rootly-vs-firehydrant-vs-incident-io)
- [incident.io vs Rootly: Which Is Better in 2026? — Arvo](https://www.aurorasre.ai/blog/incident-io-vs-rootly)
- [Best Incident Management Software 2026 — OpsBrief](https://opsbrief.io/compare/best-incident-management-software)
- [Top Incident Response Platforms for DevOps in 2026 — Sherlocks.ai](https://www.sherlocks.ai/blog/incident-response-platforms-devops-2026)
- [The best unified API platforms in 2026 — Merge](https://www.merge.dev/blog/best-unified-api)
- [Merge — Connective infrastructure for production AI](https://www.merge.dev/)
- [Best Unified API Platforms of 2026: The Shift to Agent-Based Infrastructure — Deck](https://deck.co/blog/best-unified-api-platforms-of-2026-the-shift-to-agent-based-infrastructure)
- [Best unified API platforms to consider in 2026 — Nango](https://nango.dev/blog/best-unified-api/)
- [Best AIOps Platforms 2026: Top 10 Ranked and Compared — Nova AI Ops](https://novaaiops.com/blog/best-aiops-platforms-2026)
- [AIOps Use Cases for Faster Incident Resolution — PagerDuty](https://www.pagerduty.com/resources/aiops/learn/aiops-use-cases-incident-resolution/)
- [Which AI Observability Tools Accelerate Root Cause Analysis? — Logz.io](https://logz.io/blog/ai-powered-observability-tools-root-cause-analysis/)
- [Top 10 AIOps Platforms in 2026 — OpenObserve](https://openobserve.ai/blog/top-10-aiops-platforms/)
- [Tray.io vs Workato: iPaaS Comparison 2026 — APPSeCONNECT](https://www.appseconnect.com/post_articles/tray-io-vs-workato/)
- [15 Best Incident Management Tools for 2026 — Atomicwork](https://www.atomicwork.com/itsm/best-incident-management-tools)
