# AI Integration Failure Intelligence — Product & Domain Analysis

**Doküman türü:** Ürün & domain analizi (03. faz öncesi keşif)
**Tarih:** 2026-07-12
**Kapsam:** Problem tanımı, persona/JTBD, MVP sınırları, domain glossary, ana use case'ler, rekabet konumlandırması, riskler.

---

## 1. Product One-Liner

> **"Your integration broke. We show what likely failed, why it happened, who is affected, and what to do next."**

FI (Failure Intelligence), genel gözlemlenebilirlik (observability) aracı değildir. Belirli bir alt kümeye — **third-party SaaS entegrasyonları (payment API + webhook, deployment webhook, email delivery API)** — odaklanan, kanıta dayalı (evidence-backed) bir **incident triage ve root-cause özetleme** katmanıdır. Ham log/metric/trace toplamaz ve saklamaz; entegrasyon olaylarını (event) alır, sınıflandırır, parmak izi (fingerprint) çıkarır, ilişkilendirir (correlate) ve "bu neden oldu, kim etkilendi, ne yapmalısın" sorularına insan-gözden-geçirilebilir bir cevap üretir.

---

## 2. Problem Statement

**Problem:** 5-50 kişilik SaaS şirketlerinde ve ajanslarda, bir üçüncü taraf entegrasyonu (Stripe webhook'u, GitHub deployment webhook'u, SendGrid/SES) bozulduğunda, ekip şu üç şeyi **hızlı ve güvenle** bilemiyor:

1. **Ne bozuldu?** — Tek bir 500 hatası mı, yoksa sistemik bir imza doğrulama hatası mı, yoksa rate-limit mi?
2. **Neden bozuldu?** — Bir secret mi rotate edildi, bir deploy mı config'i değiştirdi, karşı taraf mı şema değiştirdi?
3. **Kim/ne etkilendi ve şimdi ne yapmalıyız?** — Kaç müşteri, kaç request, hangi işlem etkilendi; ilk aksiyon ne olmalı?

Mevcut araçlar bu üçlüyü **birlikte ve entegrasyona özel bağlamda** cevaplamıyor:
- Sentry/Datadog gibi genel observability araçları exception/trace/metric üzerinde çok güçlü, ama "bu payment webhook'u neden Stripe'tan gelen imzalarla uyuşmuyor" gibi entegrasyon-spesifik nedensellik zincirini kurmak mühendise kalıyor.
- Hookdeck/Svix/EventDock gibi webhook altyapı araçları event'i güvenilir şekilde **teslim etmeye** (delivery/retry/replay) odaklı; "neden başarısız oldu"nun kök-neden analizini derinlemesine yapmıyorlar (EventDock'un yeni "AutoGuard AI" anomalisi istisna, ama o da anomali tespiti, kanıt-temelli kök-neden raporu değil).
- PagerDuty/incident.io gibi olay yönetimi araçları alarm/eskalasyon/ML korelasyonunda güçlü ama genel altyapı sinyalleri (metrik/trace/topology) üzerinden çalışıyor; "hangi müşterinin hangi işlemi etkilendi" gibi entegrasyon/iş-bağlamı seviyesinde bir çıktı vermiyorlar.
- Küçük ekiplerin bu araçların çoğuna (Datadog Bits, PagerDuty AIOps/Advance) bütçesi yok — bu araçlar 150+ kişilik organizasyonlar için fiyatlanmış (örn. PagerDuty AIOps add-on ~$699/ay + Advance ~$415/ay, yıllık toplamda onbinlerce dolar).

**Sonuç:** Küçük/orta ekipler, entegrasyon arızalarında ya Slack'te manuel log kazıp tahmin yürütüyor ya da genel-amaçlı, pahalı, entegrasyon-bağlamından yoksun araçlara bel bağlıyor.

---

## 3. Target Personas

### Persona A — "Ali, Backend Lead" (5-15 kişilik SaaS, payment/webhook yoğun)
- Ürünün ödeme akışı Stripe-benzeri bir API + webhook üzerine kurulu.
- Günde birkaç kez "webhook 401 veriyor" / "ödeme confirm gelmedi" tipi alarmlarla uğraşıyor.
- Sentry kullanıyor ama Sentry ona "hangi müşterinin hangi ödemesi etkilendi" demiyor, sadece exception stack'i veriyor.
- İhtiyacı: 5 dakikada "bu neden oldu, kaç müşteri etkilendi, ne yapmalıyım" cevabı.

### Persona B — "Zeynep, Support Engineer" (ajans, çoklu müşteri entegrasyonu yönetiyor)
- 10-20 müşterinin entegrasyonlarını (deployment webhook'ları, email delivery) tek başına izliyor.
- Müşteri "entegrasyonum çalışmıyor" dediğinde, hangi müşteri, hangi olay, ne zamandır olduğunu bulmak saatler alıyor.
- İhtiyacı: müşteri bazlı etki özeti + "bunu müşteriye nasıl anlatırım" seviyesinde kanıt listesi.

### Persona C — "Emre, Founding Engineer / DevOps" (10-50 kişilik, tüm entegrasyonlardan sorumlu tek kişi)
- Deploy sonrası "bir şey bozuldu ama ne" sorusuyla sık karşılaşıyor (config/secret değişikliği ile arıza arasındaki korelasyonu elle kuruyor).
- Datadog/PagerDuty'e bütçesi yok veya onlar "overkill" — asıl istediği entegrasyon-seviyesinde hızlı, ucuz bir triage aracı.
- İhtiyacı: deploy/config event'i ile hata artışını otomatik ilişkilendiren, "muhtemel neden budur" diyen bir sistem — ama nihai kararı kendisi verecek (human review flag'i önemli).

---

## 4. Jobs To Be Done (JTBD)

1. **Ali için:** "Bir webhook hata verdiğinde, stack trace kazmak yerine, olayın kategorisini (auth/signature/rate-limit/5xx/timeout/schema) ve olası nedenini 2 dakikada görmek istiyorum, böylece doğru kişiye doğru aksiyonu hızlıca yönlendirebilirim."
2. **Zeynep için:** "Bir müşteri şikayet ettiğinde, o müşteriye özel hangi request'lerin/işlemlerin etkilendiğini kanıtlarıyla birlikte görmek istiyorum, böylece müşteriye somut ve güven verici bir açıklama sunabilirim."
3. **Emre için:** "Bir deploy veya secret rotasyonundan sonra hata oranı artarsa, bunun deploy ile ilişkili olup olmadığını otomatik olarak görmek istiyorum, böylece 'bu bizim değişikliğimiz mi yoksa karşı taraf mı' sorusunu hızlı cevaplayabilirim."
4. **Genel olarak (tüm personalar):** "Bir incident'ın timeline'ını (ilk hata → tekrar → pattern → çözüm) tek bir yerde görmek istiyorum, böylece post-mortem yazarken olayları yeniden inşa etmek zorunda kalmam."
5. **Genel olarak:** "AI'ın ürettiği kök-neden özetine ne kadar güvenebileceğimi (confidence + human review flag) bilmek istiyorum, böylece kör güvenmek yerine ne zaman kendim doğrulamam gerektiğini anlarım."
6. **Genel olarak:** "Yeni bir entegrasyonu (payment, deployment webhook, email) sisteme birkaç dakikada kaydedip event almaya başlamak istiyorum, ağır bir kurulum sürecine girmeden."

---

## 5. User Stories

- Backend lead olarak, bir entegrasyonu API key ile kaydedebilmek istiyorum, böylece o entegrasyondan gelen event'leri (HTTP status, latency, request/response metadata) ingest edebilirim.
- Backend lead olarak, gelen bir event 401/403/429/5xx/timeout/schema-mismatch olarak otomatik sınıflandırılsın istiyorum, böylece manuel log okumadan olayın türünü bileyim.
- Backend lead olarak, tekrar eden benzer hataların bir "fingerprint" altında gruplanmasını istiyorum, böylece 50 ayrı hata yerine 1 pattern göreyim.
- Support engineer olarak, bir incident'ın hangi müşterileri/request sayısını etkilediğini görmek istiyorum, böylece müşteriye doğru bilgi verebileyim.
- Backend lead olarak, incident için AI tarafından üretilen, kanıt listesine (evidence) dayanan bir kök-neden özeti okumak istiyorum, böylece hipotez kurmadan önce bir başlangıç noktam olsun.
- DevOps olarak, AI'ın özetine eşlik eden bir confidence skoru ve "human review gerekli mi" bayrağı görmek istiyorum, böylece ne zaman körü körüne güvenmemem gerektiğini bileyim.
- Backend lead olarak, bir deploy/config/secret değişikliği event'i ile hata artışı arasında zamansal korelasyon görmek istiyorum, böylece "bu bizim değişikliğimiz mi" sorusunu hızlı cevaplayabileyim.
- Herhangi bir kullanıcı olarak, incident'ın timeline'ını (ilk görülme, tekrarlar, çözülme) görmek istiyorum, böylece olayı post-mortem için yeniden anlatabileyim.
- Backend lead olarak, sistemin health check ve correlation-id destekli loglama (Serilog/Seq) ile çalıştığını görmek istiyorum, böylece kendi altyapımda debug edebileyim.
- DevOps olarak, sistemi Docker Compose ile (API+PostgreSQL+Redis+Seq) tek komutla ayağa kaldırmak istiyorum, böylece kurulum maliyeti düşük olsun.

---

## 6. Main Use Cases

### UC-1: Entegrasyon Kaydetme (Integration Registration)
1. Kullanıcı admin UI/Swagger üzerinden yeni bir entegrasyon kaydı oluşturur (ör. "Stripe Payments – Prod").
2. Sistem entegrasyona özgü bir API key üretir.
3. Kullanıcı entegrasyon tipini seçer (payment API+webhook / deployment webhook / email delivery).
4. Sistem entegrasyonu "Registry"de aktif olarak işaretler ve ingestion endpoint'i döner.

### UC-2: Event Ingestion
1. Müşterinin sistemi (veya bir proxy/webhook relay) API key ile FI'ya bir integration event gönderir (HTTP status, latency, request/response metadata, timestamp, correlation id).
2. Sistem API key'i doğrular, event'i ilgili entegrasyona bağlar.
3. Event ham haliyle kalıcı hale getirilir (audit/evidence amaçlı) ve sınıflandırma pipeline'ına girer.

### UC-3: Hata Sınıflandırma ve Fingerprinting
1. Sistem event'i durum koduna, header'lara, latency'ye ve response şemasına göre bir kategoriye ayırır (401/403/429/5xx/timeout/schema mismatch).
2. Sistem benzer hataları (aynı entegrasyon + aynı kategori + benzer response şekli) bir "fingerprint" altında gruplar.
3. Var olan bir fingerprint'e event eklenirse, o fingerprint'in sayaçları (count, ilk/son görülme, etkilenen müşteri/request sayısı) güncellenir.

### UC-4: Incident Oluşturma ve Timeline
1. Bir fingerprint'in belirlenen eşiği (örn. sayı, süre, oran) aştığında sistem otomatik bir Incident açar.
2. Incident; severity, ilişkili entegrasyon, etkilenen request/customer sayısı ile başlatılır.
3. Yeni event'ler geldikçe incident timeline'ına eklenir (ilk görülme, tekrarlar, ilgili deploy/config event'leri, çözülme).

### UC-5: Kanıta Dayalı AI Kök-Neden Analizi
1. Incident belli bir olgunluğa (yeterli event/kanıt) ulaştığında AI analiz süreci tetiklenir.
2. Sistem; ilgili event'leri, deploy/config/secret değişiklik event'lerini ve zaman korelasyonunu "evidence" olarak toplar.
3. AI bu kanıtlara dayanarak olası kök-nedeni, kanıt listesini, önerilen aksiyonları ve bir confidence skoru üretir.
4. Confidence düşükse veya kanıt çelişkiliyse sistem "human review required" bayrağını set eder.

### UC-6: Etki Analizi (Affected Customers/Requests)
1. Kullanıcı bir incident'a girer.
2. Sistem, ilişkili event'lerden etkilenen benzersiz müşteri/hesap sayısını ve request sayısını hesaplar.
3. Kullanıcı bu listeyi (varsa müşteri bazlı kırılım) dışa aktarabilir veya destek yanıtı hazırlarken referans alabilir.

### UC-7: Deploy/Config Korelasyonu
1. Kullanıcı bir deployment event'ini veya config/secret değişiklik event'ini FI'ya gönderir (manuel veya CI/CD entegrasyonu ile).
2. Sistem bu event'i zaman ekseninde hata artışlarıyla çakışma açısından tarar.
3. Çakışma varsa, bu bulgu AI kök-neden analizinin bir kanıt kalemi olarak eklenir ("Bu deploy'dan 3 dakika sonra 5xx oranı %40'a çıktı").

### UC-8: İzlenebilirlik ve Operasyonel Sağlık
1. Sistem her request'e bir correlation id atar, Serilog ile yapılandırılmış loglar üretir, Seq'e yollar.
2. Health check endpoint'leri (liveness/readiness) DB/Redis/Seq bağlantılarını doğrular.
3. Operasyon ekibi Docker Compose ile API+PostgreSQL+Redis+Seq'i tek komutla ayağa kaldırır ve Swagger/minimal admin UI üzerinden sistemi test eder.

---

## 7. Domain Glossary

| Terim | Tanım |
|---|---|
| **Integration** | FI'ya kayıtlı, izlenen üçüncü taraf sistem bağlantısı (örn. "Stripe Payments – Prod"). Kendi API key'i, tipi (payment/deployment/email) ve durumu (active/paused) vardır. |
| **Event** | Bir entegrasyondan ingest edilen tekil olay kaydı: HTTP status code, latency, request/response metadata, timestamp, correlation id. FI'nın en atomik girdisi. |
| **Classification** | Bir event'e otomatik atanan hata kategorisi: `Auth401`, `Forbidden403`, `RateLimit429`, `ServerError5xx`, `Timeout`, `SchemaMismatch`, `Success` vb. |
| **Fingerprint** | Benzer event'leri gruplayan imza (örn. entegrasyon + kategori + response şekli hash'i). Tekrar eden hataları "50 ayrı olay" yerine "1 pattern, 50 tekrar" olarak göstermeyi sağlar. |
| **Incident** | Bir fingerprint'in eşik aşımı sonucu açılan, zaman içinde gelişen olay kaydı. Severity, ilişkili entegrasyon, etkilenen request/customer sayısı, timeline ve (varsa) AI analizi içerir. |
| **Timeline** | Bir incident'a bağlı kronolojik olay dizisi (ilk görülme, tekrarlar, ilgili deploy/config event'leri, çözülme anı). |
| **Evidence** | AI kök-neden özetini destekleyen somut veri kalemleri: örnek event'ler, zaman korelasyonu bulunan deploy/config event'i, tekrar sayısı/oranı, response örnekleri. |
| **AI Analysis** | Evidence'a dayanarak üretilen, olası kök-neden açıklaması + önerilen aksiyonlar + confidence skorunu içeren çıktı bloğu. |
| **Confidence** | AI analizinin ne kadar güvenilir olduğunu gösteren skor (0-1 veya düşük/orta/yüksek). Kanıt yetersizse veya çelişkiliyse düşük olur. |
| **Human Review Flag** | Confidence eşiğin altındaysa veya kanıtlar arasında çelişki varsa set edilen bayrak; kullanıcıyı "bu analize körü körüne güvenme, kendin doğrula" konusunda uyarır. |
| **Severity** | Incident'ın önem derecesi (örn. Low/Medium/High/Critical) — etkilenen request/customer sayısı ve hata kategorisine göre hesaplanır. |
| **Affected Request/Customer Count** | Bir incident'a bağlı event'lerden türetilen, kaç benzersiz request ve (varsa) kaç benzersiz müşteri/hesabın etkilendiğine dair sayım. |
| **Deployment Event** | Bir deploy/release'in gerçekleştiğini bildiren event (örn. GitHub Deployment webhook'undan gelen). Kök-neden korelasyonunda kullanılır. |
| **Config/Secret Change Event** | Bir config değeri veya secret'ın değiştiğini bildiren metadata event'i (değerin kendisi değil, değişim olduğu bilgisi). |
| **Correlation Id** | Bir isteği uçtan uca izlemeye yarayan tekil kimlik; loglama ve debugging için kullanılır. |
| **Ingestion Endpoint** | Bir entegrasyonun event göndermesi için kullandığı, API key ile korunan HTTP endpoint'i. |

---

## 8. MVP Scope

**MVP'de OLACAKLAR:**
- Integration Registry (kayıt, API key üretimi, entegrasyon tipi)
- API key ile event ingestion (payment API+webhook, deployment webhook, email delivery — bu üç entegrasyon tipiyle kanıtlanacak)
- Otomatik sınıflandırma: 401/403/429/5xx/timeout/schema mismatch
- Error fingerprinting (tekrar eden hataları gruplama)
- Incident oluşturma + timeline
- Kanıta dayalı AI kök-neden özeti (evidence listesiyle)
- Confidence skoru + human review flag
- Serilog + Seq + correlation id + health check'ler
- Docker Compose ile tek komutla ayağa kalkan API + PostgreSQL + Redis + Seq
- Swagger veya minimal admin UI (event/incident görüntüleme)

**Kapsam gerekçesi:** MVP'nin tek amacı, üç somut entegrasyon senaryosunda (Stripe-benzeri payment, GitHub deployment, SES/SendGrid) "evidence-backed root cause" değer önerisinin gerçekten çalıştığını kanıtlamaktır — genel bir platform kurmak değil.

---

## 9. Out-of-Scope List (MVP'de KESİNLİKLE olmayacaklar)

| Özellik | Neden dışarıda |
|---|---|
| **Gerçek multi-tenancy** | MVP tek-tenant/tek-organizasyon varsayımıyla çalışır; tenant izolasyonu, RBAC, organizasyon yönetimi karmaşıklığı değer kanıtlamayı geciktirir. |
| **Billing/faturalama** | Ürün henüz ticari bir teklif değil; ödeme entegrasyonu MVP'nin öğrenme hedefiyle alakasız, erken optimizasyon olur. |
| **20 connector'lük geniş entegrasyon kütüphanesi** | Genişlik yerine derinlik hedefleniyor — 3 entegrasyon tipinde (payment, deployment, email) gerçek değer kanıtlanmadan connector sayısını artırmak "mile wide, inch deep" tuzağına düşürür. |
| **Kafka / Kubernetes** | Event hacmi MVP aşamasında bu altyapı karmaşıklığını gerektirmiyor; PostgreSQL+Redis yeterli. Operasyonel yük, öğrenme hızını azaltır. |
| **Microservice ayrımı** | Tek bir modüler monolith, MVP hızını ve debug edilebilirliğini artırır; erken microservice bölünmesi dağıtık sistem karmaşıklığı katar ama karşılığında değer katmaz. |
| **Otomatik production düzeltmesi (auto-remediation)** | Ürünün wedge'i "ne olduğunu anlamak", "otomatik düzeltmek" değil — yanlış bir otomatik aksiyon (örn. yanlış secret rotate etmek) güven kaybına ve gerçek hasara yol açabilir; ayrıca bu, rakiplerin (Sentry Autofix, Datadog Bits) zaten agresifçe yatırım yaptığı, çok daha riskli bir alan. |
| **Agent swarm / çoklu-ajan orkestrasyon** | Tek, kanıta dayalı analiz akışı yeterli; çoklu ajan mimarisi MVP'de gözlemlenebilirliği ve güveni azaltan bir karmaşıklık katmanı ekler. |

---

## 10. Product Risks

1. **AI halüsinasyon riski:** Kök-neden özeti, kanıtla desteklenmeyen veya yanlış bir neden öne sürerse, kullanıcı yanlış aksiyon alabilir. Confidence + human review flag bunu azaltır ama tamamen ortadan kaldırmaz.
2. **Evidence yetersizliği:** Küçük ekiplerin event hacmi düşük olabilir; fingerprint ve korelasyon için yeterli veri birikmeden AI analizi "erken ve zayıf" kalabilir.
3. **Entegrasyon derinliği vs genişlik gerilimi:** Sadece 3 entegrasyon tipiyle başlamak, potansiyel kullanıcıların "benim stack'imde yok" diyerek erken vazgeçmesine yol açabilir.
4. **Rakip hareketi:** Hookdeck/EventDock gibi webhook altyapı oyuncuları zaten "AI anomali tespiti" yönüne kayıyor (EventDock AutoGuard AI); Sentry Seer ve Datadog Bits kök-neden alanına güçlü yatırım yapıyor. FI'nın "entegrasyon-özel, evidence-backed" konumu zamanla bu araçlar tarafından aşınabilir.
5. **Fiyatlandırma/ölçek belirsizliği:** Rakiplerin fiyat aralığı çok geniş ($26/ay Sentry Team'den $87K/yıl PagerDuty kurumsal pakete kadar); FI'nın hedef segmentine (5-50 kişi) uygun sürdürülebilir bir fiyat noktası henüz doğrulanmadı.
6. **"Insan onayı" sürtünmesi:** Human review flag doğru güven kalibrasyonu sağlamazsa (çok sık tetiklenirse) kullanıcılar aracı "hep şüpheli" bulup güvenmeyebilir; nadiren tetiklenirse yanlış güven oluşabilir.
7. **Veri gizliliği/hassasiyet:** Request/response metadata'sı (özellikle payment context'inde) hassas veri içerebilir; MVP'de PII/secret sızıntısı riskini azaltacak maskeleme/sanitization stratejisi henüz tanımlı değil.
8. **Operasyonel maliyet:** AI analiz çağrıları (LLM maliyeti) her incident için tetiklenirse, düşük fiyatlı hedef segmentte birim ekonomisi zorlanabilir (bkz. Datadog Bits'in incident başına $25-50 maliyeti — benzer bir tuzağa düşme riski).

---

## 11. Validation Assumptions (Doğrulanması Gereken Varsayımlar)

1. **Talep varsayımı:** 5-50 kişilik SaaS ekipleri, mevcut Sentry/Slack-manuel-triyaj kombinasyonundan, entegrasyon-özel bir araca geçmeye değer bir acı yaşıyor. *(Doğrulama: hedef persona görüşmeleri, mevcut triyaj sürecinin ortalama süresi ölçümü.)*
2. **Evidence yeterliliği varsayımı:** Sağlanan girdi seti (HTTP status, latency, deploy/config event, log) gerçek dünya senaryolarında güvenilir bir kök-neden hipotezi üretmeye yeterlidir. *(Doğrulama: gerçek/sentetik Stripe-webhook arıza senaryolarıyla AI çıktı kalitesi testi.)*
3. **Fiyatlandırma toleransı varsayımı:** Hedef segment, PagerDuty/Datadog'un sunduğu genel gözlemlenebilirlikten ayrı, dar kapsamlı ama ucuz bir araç için ödeme yapmaya isteklidir. *(Doğrulama: fiyat duyarlılığı görüşmeleri / willingness-to-pay testi.)*
4. **Confidence kalibrasyonu varsayımı:** Kullanıcılar, confidence skoru + human review flag kombinasyonunu anlamlı ve eyleme geçirilebilir bulacaktır (ne "kör güven" ne de "hep şüphe"). *(Doğrulama: kullanılabilirlik testleri, gerçek incident'larda flag doğruluğu ölçümü.)*
5. **Entegrasyon kapsamı varsayımı:** 3 entegrasyon tipiyle (payment, deployment, email) başlamak, genişlemeden önce yeterli bir "aha moment" yaratır. *(Doğrulama: erken kullanıcı geri bildirimi — "bu 3 entegrasyon benim için yeterli mi, yoksa X entegrasyonu olmadan kullanmam" sorusu.)*
6. **Rekabet farklılaşması varsayımı:** "General observability değil, evidence-backed SaaS integration root cause" konumlandırması, kullanıcı zihninde Sentry/Datadog/Hookdeck'ten yeterince ayrışıyor. *(Doğrulama: konumlandırma mesajı testleri, "bunun yerine neden Sentry kullanmıyorsun" sorusuna verilen yanıtların analizi.)*
7. **Otomatik incident açma eşiği varsayımı:** Fingerprint eşik değerleri (sayı/süre/oran) yanlış ayarlanırsa ya çok fazla gürültü (alert fatigue) ya da geç tespit riski oluşur — doğru eşik gerçek kullanım verisiyle kalibre edilmelidir. *(Doğrulama: pilot kullanıcılarla eşik ayarı deneyleri.)*

---

## 12. Competitive Landscape

Aşağıdaki bulgular 2026-07-12 tarihinde yapılan güncel web araştırmasına dayanmaktadır.

### Sentry (genel exception/hata izleme, AI destekli kök-neden)
- 2026 fiyatlandırması kullanım-bazlı: Developer (ücretsiz, 5.000 hata/ay), Team ($26/ay), Business ($80/ay).
- **Seer** AI eklentisi yalnızca Business/Enterprise planlarına ek olarak, aktif katkıcı başına $40/ay.
- Seer; hataları otomatik gruplar (stack trace benzerliği değil, kök-neden benzerliğine göre), root-cause hipotezi ve bazen kod düzeltmesi önerir (Autofix), etki skoru atar.
- **Fark:** Sentry kod-seviyesinde exception/stack trace odaklı; entegrasyon-seviyesinde "hangi müşteri/işlem etkilendi", "bu bir webhook imza/rate-limit sorunu mu" gibi iş-bağlamlı sınıflandırma sunmuyor. FI, Sentry'nin baktığı katmanın *üstünde*, entegrasyon-özel bir yorumlama katmanı olabilir.

### Datadog (Bits Investigation / AIOps)
- Bits Investigation; alarmları otomatik araştırıyor, telemetriyi (trace/metric/log) ilişkilendiriyor, kök-neden özeti ve önerilen aksiyon üretiyor.
- Fiyatlandırma incident başına: yıllık faturalamada ~$25/inceleme (20 inceleme/$500), aylık faturalamada ~$30/inceleme; yüksek-önem olaylarında incident başına $50'ye çıkabiliyor; büyüme aşamasındaki şirketler ayda $2.000-$5.000 arası harcıyor.
- **Fark:** Datadog, tüm altyapı telemetrisini (trace/metric/log) kapsayan devasa bir platform; küçük ekipler için hem fiyat hem karmaşıklık olarak aşırı büyük. FI, sadece "üçüncü taraf entegrasyon arızaları" dar kapsamında, çok daha düşük maliyetli bir alternatif olmayı hedefliyor.

### PagerDuty (alerting/escalation + AIOps/Advance)
- AIOps add-on (gürültü azaltma, kök-neden analizi, event orkestrasyonu): ~$699/ay.
- PagerDuty Advance (generative AI, Slack/Teams): ~$415/ay, aksiyon kotalı (1.000-20.000 AI Action, plana göre).
- Örnek toplam maliyet: 150 kişilik takım için yıllık ~$87.168 (seat + AI add-on).
- **Fark:** PagerDuty temelde bir alerting/eskalasyon platformu; kök-neden analizi ML korelasyonuna dayanıyor ama entegrasyon-özel "hangi müşteri etkilendi, evidence nedir" çıktısı vermiyor. Fiyat noktası hedef segmentin (5-50 kişi) bütçesinin çok üzerinde.

### Hookdeck (webhook altyapısı / event gateway)
- Team planı $39/ay + kullanım ($0,33/100K ek event); Growth planı $499/ay (uptime/latency SLA, Datadog metrik export, SSO).
- Event kuyruklama/buffer, otomatik issue yönetimi ve replay, CLI ile local development, %99.999 uptime hedefi.
- **Fark:** Hookdeck'in odak noktası **teslimat güvenilirliği** (delivery/retry/replay) — "olay neden başarısız oldu"nun derin, kanıta dayalı kök-neden anlatısını üretmiyor. FI, Hookdeck'in *tamamlayıcısı* olabilir: Hookdeck event'i güvenilir teslim eder, FI o event'lerin *anlamını* çıkarır.

### Svix (webhook-as-a-service, gönderen taraf altyapısı)
- Ücretsiz katman (200 mesaj/sn), Professional $490/ay'dan başlıyor, Enterprise özel.
- SOC2/HIPAA/PCI-DSS uyumluluğu, çoklu bölge veri saklama, gömülebilir tüketici portalı, geniş hedef matrisi (Kafka, SQS, RabbitMQ vb.).
- **Fark:** Svix, webhook *gönderen* SaaS şirketleri için altyapı (Clerk, Brex, Lithic gibi müşterileri var) — FI'nın hedefi olan webhook *alan* taraf (entegrasyon tüketen ekipler) için değil. Farklı bir müşteri segmentine hitap ediyor; doğrudan rakip değil, potansiyel veri kaynağı/entegrasyon noktası.

### Yeni/az bilinen oyuncular
- **EventDock** — Webhook proxy/DLQ + gerçek zamanlı dashboard; "Smart AutoGuard AI" özelliği trafik anomalilerini (spike, latency artışı, olağandışı hata oranı) proaktif tespit ediyor. Bu, FI'nın "evidence-backed root cause" iddiasına en yakın rakip özelliklerden biri, ama anomali tespiti ile kanıt-listeli kök-neden özeti farklı derinlik seviyeleri.
- **HookHound** — "AI kod üretir ama webhook'lar production'da sessizce bozulur" tezini işliyor; şema drift (payload yapısı değişiklikleri) tespiti ve production webhook izleme sunuyor. Konumlandırması FI'ya kavramsal olarak en yakın küçük oyunculardan biri — ama kapsamı şema/payload drift ile sınırlı, incident/timeline/multi-kategori sınıflandırma sunmuyor gibi görünüyor.
- **Moesif** — API analitik/monitoring, kullanıcı davranışı ve API monetizasyonuna odaklı; hata izleme var ama kök-neden anlatısı sunmuyor.
- **Treblle** — Gerçek zamanlı API request/response loglama, governance skorlama, güvenlik/uyumluluk analizi; debugging'i kolaylaştırıyor ama otomatik kök-neden özeti üretmiyor.
- **Apideck / Merge.dev** — Unified API platformları; entegrasyon sağlığı dashboard'u sunuyorlar (hata, şema değişikliği, senkronizasyon başarısızlığı tespiti) ama bu, kendi unified-API ürünlerinin bir yan özelliği — bağımsız bir "her türlü entegrasyon için failure intelligence" aracı değil, sadece kendi platformlarından geçen entegrasyonlar için geçerli.

### Sonuç: Konumlandırma (dürüst ve savunulabilir)

FI'nın iddia edebileceği **gerçekçi** fark şudur:

> FI, genel gözlemlenebilirlik (Sentry/Datadog) ile webhook teslimat altyapısı (Hookdeck/Svix) arasındaki boşlukta oturur: ham event'leri alır, entegrasyon-özel bir taksonomiyle sınıflandırır, kanıt listesiyle desteklenmiş bir kök-neden hipotezi üretir ve bunu açık bir güven skoruyla sunar — büyük gözlemlenebilirlik platformlarının fiyat/karmaşıklık yükü olmadan, küçük ekiplerin bütçesine uygun şekilde.

Bu iddia **abartılı değildir** çünkü:
- EventDock ve HookHound gibi küçük oyuncular zaten bu boşluğa yöneliyor — pazar boşluğu var ama "boş" değil, rekabetçi.
- Sentry/Datadog'un kök-neden özellikleri (Seer, Bits) çok daha geniş bir veri yüzeyine (kod, trace, metrik) dayanıyor ve çok daha pahalı; FI'nın dar kapsamı hem bir zayıflık (daha az veri) hem bir güç (daha ucuz, daha odaklı, daha hızlı kurulum) olarak çerçevelenmelidir.
- "Evidence-backed" iddiası MVP'de gerçekten kanıtlanmalıdır — aksi halde bu sadece bir pazarlama cümlesi olur, doğrulanması gereken bir varsayım olarak Bölüm 11'de işaretlenmiştir.

---

## Sources

- [Sentry Pricing 2026: Complete Cost Guide & Alternatives](https://blog.struct.ai/sentry-pricing-error-monitoring-2026/)
- [Sentry Expands Seer AI Debugging Agent](https://sentry.io/about/press-releases/sentry-expands-seer-ai-debugging-agent/)
- [AI in Sentry — Docs](https://docs.sentry.io/product/ai-in-sentry/)
- [Sentry Pricing Guide 2026 — Bugsly](https://bugsly.dev/blog/sentry-pricing-guide-2026)
- [Sentry Pricing 2026 — Last9](https://last9.io/blog/sentry-pricing/)
- [Datadog Pricing](https://www.datadoghq.com/pricing/)
- [Bits Investigation — Datadog](https://www.datadoghq.com/product/ai/bits-ai-sre/)
- [Incident AI — Datadog Docs](https://docs.datadoghq.com/incident_response/incident_management/investigate/incident_ai/)
- [Datadog Bits Pricing — Struct.ai](https://blog.struct.ai/datadog-bits-ai-pricing-2026/)
- [Our AI tool costs $0.12 per incident report — Medium](https://medium.com/lets-code-future/our-ai-tool-costs-0-12-per-incident-report-datadog-charges-4-167-month-how-is-this-sustainable-d285b739b297)
- [Hookdeck Pricing 2026 — StackScored](https://www.stackscored.com/pricing/api-platforms/hookdeck/)
- [Event Gateway Pricing — Hookdeck](https://hookdeck.com/pricing)
- [Hookdeck — Reliable webhook infrastructure](https://hookdeck.com/)
- [Best Webhook Monitoring Tools in 2026 — EventDock](https://eventdock.app/blog/best-webhook-monitoring-tools-2026-comparison)
- [Hookdeck vs EventDock — EventDock](https://eventdock.app/blog/eventdock-vs-hookdeck-webhook-platform-comparison)
- [EventDock — Never Lose a Webhook Again](https://eventdock.app/)
- [Svix Pricing](https://www.svix.com/pricing/)
- [Svix — Webhooks as a Service](https://www.svix.com/)
- [Best Webhook Infrastructure Platforms 2026 — Svix](https://www.svix.com/webhooks/best-webhook-infrastructure-platforms/)
- [Svix vs Hookdeck vs Convoy — PkgPulse](https://www.pkgpulse.com/guides/svix-vs-hookdeck-vs-convoy-webhook-infrastructure-2026)
- [Incident management pricing comparison 2026 — incident.io](https://incident.io/blog/incident-management-pricing-comparison-2026)
- [Generative AI — PagerDuty](https://www.pagerduty.com/platform/generative-ai/)
- [AIOps — PagerDuty](https://www.pagerduty.com/platform/aiops/)
- [PagerDuty Pricing 2026 — CheckThat.ai](https://checkthat.ai/brands/pagerduty/pricing)
- [How Moesif API Observability Compares to Treblle](https://www.moesif.com/blog/monitoring/How-Moesif-API-Observability-Compares-To-Treblle/)
- [API Monitoring — Moesif](https://www.moesif.com/features/api-monitoring)
- [API Monitoring and Observability — Treblle Knowledge Base](https://treblle.com/knowledgebase/management-phase/api-monitoring-and-observability)
- [AI Makes It Easy to Build SaaS. Production Still Breaks Your Webhooks. — HookHound](https://www.hookhound.dev/guides/ai-shipping-production-webhooks-break)
- [How to Monitor Webhooks in Production — HookHound](https://www.hookhound.dev/guides/monitor-webhooks-production)
- [6 Apideck alternatives and competitors to consider in 2026 — Merge.dev](https://www.merge.dev/blog/apideck-alternatives)
- [Apideck Unify — A smarter way to build SaaS integrations](https://www.apideck.com/products/unify)
- [Merge vs Apideck — Truto Blog](https://truto.one/blog/merge-vs-apideck-which-unified-api-is-better-in-2026)
