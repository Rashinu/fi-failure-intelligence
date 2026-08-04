# M20 — Design Partner Readiness Review

> Rol: bu doküman boyunca mühendis değil, ürün/pazar tarafı (SaaS Founder, PM, Design Partner,
> Integration Consultant, Support Lead, Enterprise Buyer, VP Eng) gözüyle yazıldı. Hiçbir kod
> değişikliği yapılmadı. Değerlendirme, gerçek dosyalar okunarak yapıldı:
> `Pages/Incidents/Detail.cshtml`, `Pages/Incidents/Index.cshtml`, `Pages/Shared/_Layout.cshtml`,
> ve M19'da canlı doğrulanmış Golden Incident senaryosu (PaymentSync, 43 event / 12 operasyon /
> 7 müşteri). Docker stack bu oturumda ayakta olmadığı için ekran görüntüsü yerine gerçek
> render edilen Razor/CSS kaynağı üzerinden okundu — görsel bir iddia yapılmadı, yalnızca
> kaynaktan doğrulanabilir olan söylendi.

## Executive Summary

FI, M19 sonrasında mühendislik açısından gerçek bir ürün: honest coverage semantics, gerçek bir
resolve lifecycle'ı, ve teşhis edilmiş bir AI grounding düzeltmesi var. Ama bu doküman
mühendislik olgunluğunu değil, **birinin buna gerçekten değer verip vermeyeceğini** soruyor.

Cevap: **henüz değil, ama yakın.** Ürün hâlâ iki dilli (Türkçe/İngilizce karışık arayüz metni),
hâlâ "event/operation/incident" gibi üç örtüşen sayım kavramını açıklamadan yan yana gösteriyor,
ve dashboard hâlâ bir "iş etkisi" hikayesi değil bir "hata sayacı" hikayesi anlatıyor. Golden
Incident senaryosu gerçek ve gösterilebilir, ama şu anda **kendi kendini anlatmıyor** — bir demo
sunan kişi devreye girip açıklama yapmak zorunda kalıyor. Bu M20 (Design Partner) görüşmeleri
için engelleyici değil, ama bir demo öncesi düzeltilmeli.

**Final Decision: B — Needs one more UX milestone** (detay Part 10'da).

---

## PART 1 — Product Demo Review (Golden PaymentSync Incident)

İlk 30 saniye testi, `Detail.cshtml`'in gerçek render sırasına göre:

1. **Başlık:** `@Model.IntegrationName — @Model.Category` → "PaymentService (Prod) —
   AuthenticationError" gibi bir şey. **Ne oldu sorusuna kısmen cevap** ama "AuthenticationError"
   bir son kullanıcıya değil bir logcu'ya hitap ediyor.
2. **Meta satırı:** "İlk görülme ... Son görülme ... N event" — teknik, zaman odaklı, iş odaklı
   değil.
3. **İş Etkisi kutusu** (yeni, M19): "Teknik event: 43 / Bilinen operasyon: 12 / Etkilenen
   müşteri: 7 / Süre: X". Bu, **doğru bilgiyi** taşıyor ama dört farklı sayıyı açıklamasız yan
   yana koyuyor. Bir support engineer bunu görünce ilk sorusu "'operasyon' nedir, 'event'ten
   farkı ne?" olacaktır — cevap ekranda yok.
4. **"Şimdi ne yapmalıyım?"** — bu satır iyi, gerçekten operasyonel bir soru soruyor. Ama hemen
   altındaki AI kartı "Olası neden (AI destekli yorum, kesinleştirilmiş kanıt değil)" gibi
   hukuki/temkinli bir dille başlıyor — güven verici değil, savunmacı.
5. **Resolve formu** en üstte değil, ortada bir yerde gömülü; "Adınız (opsiyonel)" placeholder'lı
   iki serbest metin alanı — kimin resolve ettiğini sistem zaten (admin auth ile) biliyor olması
   gerekirken kullanıcıya soruyor.

**30 saniyelik test sonucu: KISMEN GEÇTİ.**
- "Ne oldu" → evet (kategori + entegrasyon adı var).
- "Kim etkilendi" → evet ama açıklamasız bir sayı olarak (7 müşteri, isim/liste yok).
- "Neden" → yalnızca AI analizi varsa, ve "kesinleşmemiş yorum" diliyle sunuluyor.
- "Ne yapmalıyım" → var ama tek satır, altında gerekçe yok.

**Hâlâ mühendislik aracı gibi hissettiren yerler:**
- Karışık dil: aynı ekranda "İş Etkisi", "Şimdi ne yapmalıyım?" (TR) ile "AI-Assisted Analysis",
  "Confidence", "Resolve Incident" (EN) yan yana. Bir kurumsal alıcıya gösterilecek tek bir
  ekranın iki dil arasında geçiş yapması ciddiye alınmayı zorlaştırır.
- "Fingerprint" bölümü sayfanın en altında ham bir hash/string kartı olarak duruyor — bu apaçık
  bir debug artığı, bir operasyon ürününde müşteriye gösterilecek bir şey değil.
- Üç örtüşen sayım birimi (event / operasyon / incident) hiçbir yerde tek cümlelik bir sözlükle
  açıklanmıyor.
- "Evidence" ve "Timeline" bölümleri ham liste — hikâyeleştirilmemiş, en son NE ZAMAN bir şeyin
  değiştiğini vurgulamıyor.

---

## PART 2 — First Impression Review (Persona Bazlı)

**Support Engineer:**
"Tamam, PaymentService'te bir hata var, 7 müşteri etkilenmiş. Ama hangi müşteriler? Onlara ne
söyleyeceğim, bir isim/e-posta/hesap ID listesi yok. 'Bilinen operasyon: 12' ne demek, 12 farklı
ödeme mi yoksa 12 farklı müşteri işlemi mi? 'Fingerprint' ne işe yarıyor, bana lazım değil gibi
duruyor." → **Eksik: müşteri kimlik listesi, müşteriye ne söyleyeceğini öneren bir şablon.**

**Backend Engineer:**
"Deterministik sınıflandırma + evidence + AI ayrımı mantıklı, mimariyi anlıyorum. Ama neden hâlâ
Türkçe/İngilizce karışık? Bu bir iç araç gibi duruyor, dışarı satılacak bir şey gibi değil."
→ **Eksik: tutarlı tek dil (muhtemelen İngilizce, çünkü hedef "gerçek şirketler").**

**Engineering Manager:**
"Bu bana zaten bildiğim şeyi (Sentry/Datadog zaten alarm veriyor) tekrar mı söylüyor, yoksa yeni
bir şey mi? Ekranda bunu ayıran hiçbir cümle yok — 'bu incident 43 teknik event'i 12 gerçek
operasyona ve 7 müşteriye indirgedi' cümlesi potada ama söylenmiyor, kullanıcı kendisi
çıkarsamak zorunda." → **Eksik: sayının kendisinin YANINDA, neden bunun önemli olduğunu
söyleyen tek cümlelik bir 'so what'.**

**CTO:**
"Resolve butonuna bastım — bu bir audit trail'e mi yazılıyor, kim sorumlu tutuluyor? Not alanı
var ama zorunlu değil, hiçbir governance/approval akışı yok. Bunu enterprise'a satmak için henüz
erken ama bir pilot için yeterli." → **Doğru okuma: bu şu an bir pilot aracı, enterprise-satış
aracı değil — ki M19 kapsamı zaten bunu iddia etmiyordu.**

**Integration Consultant:**
"Stripe'tan metadata çekiyor, güzel. Ama biz müşterilerimize kaç entegrasyon türü destekliyorsunuz
diye sorduğumuzda cevap bugün sınırlı (Stripe, email delivery, GitHub deployment, generic webhook)
— bu, 'evrensel bir entegrasyon gözlemleyicisi' iddiasını henüz karşılamıyor." →
**Doğru okuma: konektör sayısı azlığı bir pazarlama riski, ama M20'nin sorusu (validasyon) için
engel değil — hedef müşteri profili zaten Stripe-ağırlıklı seçilebilir (bkz. Part 6).**

---

## PART 3 — 3-Minute Demo Script

**Minute 1 — Problem (0:00–1:00)**
> "Bir müşterinin ödemesi başarısız oldu. Bugün bunu nasıl öğreniyorsunuz? Sentry bir exception
> gösterir. Datadog bir metrik grafiği gösterir. Ama hiçbiri şu soruyu cevaplamaz: *kaç gerçek
> müşteri işlemi etkilendi, ve onlara ne söylemem gerekiyor?* 43 tane HTTP 401 logu görürsünüz —
> ama bunun 12 farklı ödeme denemesini ve 7 farklı müşteriyi temsil ettiğini bilmezsiniz. Log
> sayısı ile iş etkisi arasındaki bu boşluk, support ekiplerinin saatlerini yiyor."

**Minute 2 — Golden Incident Walkthrough (1:00–2:00)**
> "İşte gerçek bir örnek: Stripe entegrasyonumuzda bir API key rotasyonu unutuldu. 43 teknik
> hata event'i geldi. FI bunları otomatik olarak tek bir incident'e topladı, deterministik olarak
> `AuthenticationError` olarak sınıflandırdı, ve — burası önemli — bu 43 event'in **12 farklı
> ödeme operasyonunu** ve **7 farklı müşteriyi** temsil ettiğini çıkardı. Bir AI katmanı kanıta
> dayalı bir olası kök neden özeti üretti, ama bunu kesin bir gerçek gibi değil, doğrulanabilir
> bir yorum gibi sunuyor. Sonra: tek tıkla resolve ediyorum, kim çözdüğü ve ne yapıldığı
> kaydediliyor. Ve eğer aynı hata 30 dakika içinde tekrar gelirse, sistem bunu otomatik olarak
> yeniden açıyor — 'çözüldü sanıp unutulan' sorunu ortadan kalkıyor."

**Minute 3 — Neden FI, Sentry/Hookdeck/Datadog Değil? (2:00–3:00)**
> "Sentry ve Datadog size *teknik olarak ne bozuldu*'yu söyler. Hookdeck webhook'ların *iletildiğini*
> garanti eder. Hiçbiri şunu söylemez: bu teknik hatalar toplamda kaç **gerçek iş operasyonunu**
> ve kaç **gerçek müşteriyi** etkiledi. FI, log seviyesinde değil, iş operasyonu seviyesinde
> düşünüyor — ve bunu iddia etmekle kalmıyor, dürüstçe işaretliyor: eğer bir entegrasyon bize
> hangi müşterinin etkilendiğini söylemiyorsa, FI 'sıfır müşteri etkilendi' demez, 'bilinmiyor'
> der. Bu dürüstlük, güven inşa etmenin temeli."

---

## PART 4 — Value Proposition

**One sentence:**
> FI, dağınık entegrasyon hatalarını gerçek iş operasyonlarına ve etkilenen müşterilere dönüştüren
> incident intelligence katmanıdır.

**30-second pitch:**
> Entegrasyonlarınız (Stripe, webhook'lar, deployment'lar) başarısız olduğunda, bugün elinizde
> yüzlerce ham log satırı oluyor ama hiçbiri şunu söylemiyor: kaç gerçek işlem etkilendi, hangi
> müşteriler etkilendi, ve ekibinizin şimdi ne yapması gerekiyor. FI bu logları otomatik olarak
> tek bir incident'e toplar, kanıta dayalı bir olası neden üretir, ve iş etkisini dürüstçe
> (bilinen/bilinmeyen ayrımıyla) raporlar — böylece support ekibiniz "kaç müşteriye e-posta
> atmalıyım?" sorusunu dakikalar içinde, saatler içinde değil, cevaplayabilir.

**Landing page hero:**
> **Log'lardan iş etkisine, otomatik olarak.**
> FI, entegrasyon hatalarınızı gerçek müşteri etkisine dönüştürür — tahmin değil, kanıt.

**Three customer pain bullets:**
- Bir entegrasyon başarısız olduğunda, kaç gerçek müşterinin etkilendiğini bulmak saatler
  sürüyor — genelde manuel log grep'lemeyle.
- Support ekibi müşteriye "etkilendiniz mi" diye tek tek sormak zorunda kalıyor çünkü hiçbir
  sistem bunu otomatik bağlamıyor.
- "Çözüldü" dedikten sonra aynı hata sessizce tekrar ediyor ve kimse fark etmiyor ta ki müşteri
  şikayet edene kadar.

**Three outcome bullets:**
- Dakikalar içinde: kaç gerçek operasyon ve kaç gerçek müşteri etkilendi, dürüst bir sayı olarak.
- Kanıta dayalı, kesinlik iddia etmeyen bir olası kök neden — insan onayına açık.
- Otomatik reopen: "çözüldü" sonrası aynı hata tekrarlarsa, incident kendiliğinden yeniden açılır.

**One CTA:**
> "Bize 15 dakikanızı ayırın — gerçek bir entegrasyon hatanızı FI'ye gösterin, biz size ne
> çıkardığını gösterelim."

---

## PART 5 — Competitor Review (Yalnızca Bugün Var Olan Fonksiyonellik)

| Rakip | Ne yaptıklarını daha iyi yapıyorlar | FI'nin M19 sonrası daha iyi yaptığı |
|---|---|---|
| **Sentry** | Exception-seviyesinde stack trace, kod satırı, release/deploy correlation; devasa dil/framework SDK kapsamı; olgun triage/assign iş akışı. | Sentry bir exception'ı "başarısız" olarak işaretler ama kaç gerçek iş operasyonunu/müşteriyi etkilediğini bilmez — FI bunu event→operasyon→müşteri olarak açıkça sayar ve bilinmeyeni "bilinmiyor" der, sıfır varsaymaz. |
| **Datadog** | Uçtan uca observability (metrik, log, trace, APM, infra); devasa entegrasyon kataloğu; enterprise-grade dashboard/alerting. | Datadog bir "iş operasyonu başarısız oldu, resolve edildi, sonra tekrar açıldı" lifecycle'ını birinci sınıf bir kavram olarak sunmaz — FI'de bu native bir domain modeli (Incident.Resolve + otomatik reopen-in-cooldown). |
| **Hookdeck** | Webhook teslimatını garantiler (retry, sıraya alma, replay) — altyapı seviyesinde çok güvenilir. | Hookdeck webhook'un *iletildiğini* doğrular ama iletilen webhook'un temsil ettiği *iş sonucunun* (ödeme, abonelik güncellemesi) başarılı olup olmadığını bilmez — bu FI'nin tam odaklandığı boşluk. |
| **Svix** | Webhook gönderim altyapısı (kendi ürününüzden müşterilerinize) — yayıncı tarafı, tüketici tarafı değil. | Svix bir webhook *gönderen* için araç; FI bir webhook/entegrasyon *tüketen* taraf için — farklı taraf, doğrudan rekabet yok, ama karıştırılabilir konumlandırma riski var (bunu netleştirmek gerek). |

**Dürüst özet:** FI hiçbirinin yaptığı gözlemlenebilirlik/teslimat işini daha iyi yapmıyor ve
yapmaya çalışmamalı. FI'nin farkı dar ama gerçek: ham teknik event'i **iş operasyonu + müşteri**
seviyesine indirgeyen dürüst bir katman. Bu M19'dan önce iddia edilebilir değildi (event sayısı
= operasyon sayısı varsayılıyordu); M19 sonrası artık gerçek ve test edilmiş bir farklılaşma.

---

## PART 6 — Design Partner Search: Ideal Customer Profile

**Hedef profil (şirket türü değil, somut profil):**
- **10–40 mühendis** büyüklüğünde bir SaaS şirketi (daha küçükte support ekibi ayrı bir fonksiyon
  değildir — mühendis kendi hatasına bakar; daha büyükte muhtemelen kendi iç aracını kurmuşlardır).
- **Stripe (veya benzeri bir ödeme/faturalama sağlayıcısı) kullanıyor** — Golden Incident
  senaryosu tam olarak bu, ve StripeConnector bugün en olgun konektör.
- **Webhook üzerinden en az bir dış sistemle entegre** (CRM, faturalama, e-posta sağlayıcı) —
  entegrasyon hatası zaten bilinen, hissedilen bir acı olmalı.
- **Ayrı bir support/customer success fonksiyonu var** (2+ kişi) — "müşteriye ne söyleyeceğim"
  sorusunu gerçekten soran biri olmalı, yoksa değer önerisi havada kalır.
- **2–5 backend mühendisi entegrasyon/webhook kodunu sahipleniyor**, ama adanmış bir
  observability/SRE ekibi YOK (yani Datadog'u zaten "yeterli" görmüyorlar veya hiç kurmamışlar).
- **Son 90 günde en az bir kez** "bir entegrasyon sessizce/yarı-sessizce bozuldu ve fark etmemiz
  zaman aldı" tarzı bir olay yaşamış olmalı (bu, görüşmede doğrudan sorulmalı, bkz. Part 7).

**Neden bu profil:**
Daha büyük şirketler zaten Datadog/PagerDuty + kendi iç tooling'lerini kurmuş olur ve "bir tane
daha araç" satmak zor olur. Daha küçük şirketlerde support ayrı bir fonksiyon olmadığından "kaç
müşteri etkilendi" sorusunu soran kimse yoktur — değer önerisi hedefsiz kalır. Stripe/webhook
şartı, bugünkü FI'nin gerçekten iyi çalıştığı konektörle birebir örtüşüyor — konektör kapsamının
dar olduğu bir gerçek (Part 1/2), o yüzden ilk pilotları bu dar kapsamla mükemmel örtüşen
şirketlerden seçmek, ürünün gerçek gücünü gösterme şansını maksimize eder.

---

## PART 7 — Design Partner Interview (12 Soru, Hepsi Açık Uçlu)

1. Geçen ay bir entegrasyonunuz (Stripe, webhook, ödeme sağlayıcısı vb.) beklenmedik şekilde
   başarısız olduğunda, bunu ilk **nasıl** fark ettiniz?
2. Bunu fark ettikten sonra, "kaç müşteri etkilendi" sorusunu cevaplamak için hangi adımları
   attınız?
3. O soruyu cevaplamak ne kadar sürdü, ve kaç kişi bu sürece dahil oldu?
4. Bugün bu tür olayları takip etmek için hangi araçları (Sentry, Datadog, kendi dashboard'unuz,
   Slack, tablo vb.) bir arada kullanıyorsunuz?
5. Bir hatayı "çözüldü" olarak işaretledikten sonra aynı hatanın sessizce tekrar ettiği bir örnek
   hatırlıyor musunuz — bunu nasıl fark ettiniz (ya da hiç fark etmediniz mi)?
6. Bir entegrasyon operasyonu (ödeme, senkronizasyon, güncelleme) hiçbir hata üretmeden ama
   beklenen sonucu vermeden "sessizce" başarısız olduğu bir durum yaşadınız mı — bir örnek
   verebilir misiniz?
7. Bir hata olduğunda etkilenen müşterilere ulaşma kararını kim veriyor, ve bu karar neye
   dayanıyor?
8. Support ekibiniz bir müşteriden "benim ödemem neden başarısız oldu" sorusunu aldığında,
   cevap vermek için hangi ekranlara/kişilere bakıyor?
9. En son büyük bir entegrasyon olayında, "keşke elimizde şu bilgi olsaydı" dediğiniz şey neydi?
10. Bugünkü araçlarınızdan hangisini kaldırıp yerine yenisini koymaya en az istekli olursunuz,
    ve neden?
11. Bir incident'i "çözüldü" işaretlemek sizin organizasyonunuzda ne anlama geliyor — kim
    onaylıyor, nereye kaydediliyor?
12. Eğer bir araç size "bu 40 teknik hata, aslında 12 gerçek işlemi ve 7 gerçek müşteriyi
    temsil ediyor" deseydi, bu bilgiyle ilk yapacağınız şey ne olurdu?

---

## PART 8 — Feature Triage

**Build Before Validation (M19'da zaten yapıldı, ek onay gerekmiyor):**
- Operation/Customer coverage semantics (yapıldı).
- Incident Resolve lifecycle + otomatik reopen (yapıldı).
- AI grounding false-positive düzeltmesi (yapıldı).

**Build Only After Validation (M20 görüşmelerinden somut sinyal gerektirir):**
- Silent Failure / reconciliation (zaten `docs/product/SILENT_FAILURE_HYPOTHESIS.md`'de
  belgelendi — sinyal kriteri orada net: isimlendirilmiş, somut bir örnek gerekiyor).
- Agent-tetiklemeli operasyon takibi (zaten `docs/product/AGENT_FAILURE_EXTENSION.md`'de
  belgelendi).
- Etkilenen müşteri **listesi/kimliği** (isim/e-posta) gösterimi — bugün yalnızca sayı var; bir
  design partner "bana liste lazım" derse önceliklendirilmeli.
- Ek konektörler (HubSpot, Salesforce, generic REST) — yalnızca ICP görüşmelerinde tekrar eden
  bir entegrasyon adı çıkarsa.
- Çok kullanıcılı/rol tabanlı erişim (RBAC) — yalnızca birden fazla kişi aynı FI kurulumunu
  kullanmaya başlarsa.

**Never Build Unless Requested:**
- Workflow builder / otomatik remediation / replay.
- Kendi başına bir agent-observability platformu (LangSmith rakibi).
- Yeni bir genel gözlemlenebilirlik altyapısı (Sentry/Datadog'un yaptığını tekrar etmek).

---

## PART 9 — Product Score (1–10)

| Boyut | Puan | Not |
|---|---|---|
| Problem | 7 | Gerçek ve hissedilen bir acı (log→iş etkisi boşluğu), ama henüz bir design partner ağzından doğrulanmadı. |
| Market | 6 | Dar ama gerçek bir niş (Stripe/webhook-ağırlıklı orta ölçek SaaS); büyük observability oyuncularıyla doğrudan rekabet değil. |
| Differentiation | 7 | M19 sonrası gerçek ve savunulabilir (event≠operasyon≠müşteri ayrımı, dürüst "bilinmiyor" semantiği). |
| Product | 6 | Domain modeli sağlam, ama konektör kapsamı dar, hâlâ prototip hissi veren köşeler var (Fingerprint kartı gibi). |
| UX | 4 | Karışık dil, açıklamasız üç örtüşen sayım birimi, resolve formu gömülü — bir demo'yu insan anlatmadan izleyemez. |
| Trust | 6 | AI'nin "kesinleşmemiş yorum" dili doğru bir güven sinyali; ama "bilinmiyor" durumlarının UI'da hâlâ biraz gizli kalması güveni tam kullanmıyor. |
| Demo | 5 | Golden Incident gerçek ve etkileyici bir hikâye ama şu anki ekran bunu kendi kendine anlatmıyor — sunan kişiye bağımlı. |
| Pricing Potential | 5 | Değer önerisi net değilse fiyatlandırma konuşulamaz; bugünkü haliyle "kaç saatlik support zamanı kurtarıyor" sorusuna sayısal bir cevap yok. |
| Design Partner Readiness | 6 | Görüşme yapmaya hazır (ürün göstermeden de sorular sorulabilir), ama canlı demo göstermeden önce UX düzeltmesi şart. |
| **Overall** | **6/10** | Mühendislik olarak sağlam bir temel, ürün olarak henüz "kendi kendini satan" değil. |

---

## PART 10 — Final Decision

### **B — Needs one more UX milestone.**

**Gerekçe:** Problem, differentiation ve domain modeli M19 sonrası gerçek ve savunulabilir
durumda (A'ya yakın). Ama Part 1/2'de tespit edilen üç somut, düşük-maliyetli UX sorunu
(karışık dil, açıklamasız sayım birimleri, gömülü/ham "Fingerprint" kartı gibi debug artıkları)
demo'yu **insan anlatmadan** izlenemez hale getiriyor — bu, gerçek bir "major rethink" değil
(C değil), ama görüşmelere gitmeden önce çözülmesi gereken gerçek bir engel (A değil).

**Önerilen minimum UX milestone (M20 öncesi, kod değişikliği gerektirir ama kapsamı dar):**
1. Tüm kullanıcı-yüzü metnini tek dile (İngilizce, hedef "gerçek şirketler" olduğu için)
   normalize et.
2. "İş Etkisi" kutusuna tek satırlık bir açıklama ekle: "Event = ham teknik hata; Operasyon =
   gerçek bir iş işlemi; Müşteri = etkilenen gerçek kullanıcı."
3. "Fingerprint" kartını sayfanın altına, açıkça "Geliştirici / Teknik Detay" etiketli, katlanır
   bir bölüme taşı (Swagger/Hangfire footer düzeltmesiyle aynı mantık).
4. Resolve formundaki "Adınız (opsiyonel)" alanını, zaten var olan admin auth kimliğinden
   otomatik doldur (kullanıcıya sormadan).

Bu dört değişiklik küçük, düşük riskli, ve M19'un mimarisine dokunmuyor — sadece M19'da zaten
doğru hesaplanan bilgiyi doğru şekilde sunuyor. Bunlar tamamlandıktan sonra durum A'ya döner.
