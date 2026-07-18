# AI Integration Operations Platform (OP) — FI'dan Platforma Geçiş: Delivery, Testing & Go-To-Market Planı

**Rol:** Delivery, Testing & Go-To-Market Planner (Platform Ufku)
**Kapsam:** FI (V1) validasyonu başarılı olursa V2 (Support Correlation + Workflow) → V3 (Connector SDK + Multi-tenant + Billing + Analytics) → V4 (Controlled Remediation) geçiş kriterleri, milestone bazlı delivery planı, test stratejisi evrimi, CI/CD evrimi, billing'in teknik/GTM etkisi, repo yapısı evrimi, platform seviyesi ADR listesi, pilot genişletme planı, GTM mesajlaşma evrimi, continue/pivot kararları, kapsam şişmesi riski ve disiplin mekanizmaları.
**Önkoşul:** Bu doküman, `docs/analysis/failure-intelligence/06-delivery-testing-validation.md`'de tanımlanan FI (V1) 14 günlük MVP planının ve Bölüm 13'teki "continue" kararının **başarılı** sonuçlandığını varsayar. FI validasyonu başarısız olursa (kill/pivot) bu doküman devreye girmez — OP bir "V1 başarılıysa" dalıdır, paralel bir plan değildir.
**Not:** Bu doküman sadece plandır — kod içermez. Granülerlik FI'daki gibi günlük değil, **haftalık/aylık**dır; V2-V4 aylar süren bir yatırımdır.

---

## 0. Yönetici Özeti

FI, dar bir problemi ("entegrasyon hatası neden oldu") kanıtlanabilir şekilde çözen bir V1'dir. OP, bu V1'in üzerine "entegrasyon operasyonlarının merkezi katmanı" olma iddiasıyla inşa edilen bir platform vizyonudur. Bu geçiş iki temel riskle karşı karşıyadır:

1. **Erken genişleme riski:** FI henüz para ödeyen bir müşteriye sahip değilken ya da tek bir zayıf sinyalle V2'ye başlamak, doğrulanmamış bir platforma aylarca yatırım yapmak anlamına gelir — bu, "kitchen sink" platformu riskinin kök nedenidir.
2. **Geç genişleme riski:** FI'nin sağladığı dar değeri gören ilk pilotlar, "bu sadece bir tanı aracı, workflow'uma giremiyor" dediğinde harekete geçmemek, o müşterileri rakiplere (Sentry, Datadog, PagerDuty entegrasyon modülleri) kaptırma riski taşır.

Bu doküman bu iki riski dengelemek için her faz öncesine **zorunlu validasyon gate'i** koyar (Bölüm 1 ve 11), her fazı bağımsız olarak "durdurulabilir" tasarlar (her faz kendi içinde satılabilir bir artefakt üretir, önceki fazın üzerine "bitmemiş" bir katman olarak kalmaz) ve platformun büyümesini müşteri sinyaliyle senkronize eder — vizyon dokümanındaki V2→V3→V4 sırası korunur ama **zaman çizelgesi sinyale bağlıdır, takvime değil.**

---

## 1. FI → OP Geçiş Kriterleri (V2'ye Başlama Gate'i)

FI'nin kendi kill/pivot/continue kararı (kaynak dokümanın Bölüm 13'ü) "gerçek ürünleştirmeye devam" kararını verir ama bu otomatik olarak "V2'ye başla" anlamına gelmez. V2, yeni bir mühendislik yatırımı (support ticket entegrasyonu, historical incident benzerliği, workflow motoru) gerektirdiği için **kendi ayrı gate'i** vardır.

### 1.1 V2'ye başlamayı haklı çıkaran sinyaller (hepsi gerekli — AND)

| # | Sinyal | Eşik | Neden bu eşik |
|---|---|---|---|
| S1 | Aktif/tamamlanmış pilot sayısı | En az 2 pilot, ikisi de pilot sonunda "devam" demiş (ücretli ya da somut ücretli-devam taahhüdü) | Tek pilot bir anekdot; 2. pilotun aynı deseni doğrulaması, sorunun tek şirkete özgü olmadığını gösterir. |
| S2 | Ödeme niyeti | En az 1 imzalı/yazılı ücretli sözleşme (aylık ücret farketmez, düşük bile olsa) **veya** 2 pilotun ikisinin de somut fiyat teklifine yazılı "evet" demesi | FI Bölüm 13'teki "en az 1 net ödeme niyeti" eşiği V1 için yeterliydi; V2 aylarca sürecek bir yatırım olduğu için çıta net bir sözleşmeye/yazılı taahhüde yükseltilir. |
| S3 | Kullanım deseni: "workflow'a girme" talebi | Pilot kullanıcılarının en az 2'sinden, **spontane** (sorulmadan) gelen "bu incident bilgisi support ticket'ıma/Slack'ime/on-call akışıma bağlansa" tarzı talep | Bu, V2'nin (support correlation + workflow) gerçekten bir sonraki acı noktası olduğunun kanıtı — icat edilmiş değil, gözlemlenmiş bir ihtiyaç olmalı. |
| S4 | Kullanım sıklığı | Pilot süresince ürün haftada en az 3-4 gün aktif olarak açılıyor/kontrol ediliyor (kurulup unutulmamış) | FI'nin kendi pilot ölçütü (Bölüm 10, madde 3) burada da geçerli; "unutulan araç" üzerine platform inşa etmenin anlamı yok. |
| S5 | Tekrarlayan incident hacmi | Pilot ortamında ayda en az 8-10 gerçek incident (sentetik değil) üretiliyor | V2'nin "similar historical incident" özelliği ancak yeterli hacimde geçmiş veri varsa değer üretir; hacim yoksa özellik boşta kalır. |

### 1.2 V2'ye başlamayı **haklı çıkarmayan** durumlar (yaygın yanılgılar)
- Tek bir pilotun "bu harika olurdu" demesi (talep değil, nezaket cümlesi olabilir) — S3'te "spontane ve en az 2 kaynaktan" şartı bu yüzden var.
- İç motivasyon ("platform vizyonu zaten böyleydi, devam edelim") — vizyon dokümanı bir yol haritasıdır, bir taahhüt değildir; sinyal yoksa yol haritası bekler.
- Rakip analizi ("Sentry'de bu özellik yok, biz önce yapalım") — rekabet avantajı, müşteri sinyalinin yerine geçmez.

### 1.3 Gate geçilemezse ne olur
S1-S5'ten 4'ü karşılanıp 1'i eksikse: **"extend" modu** — FI'yı olduğu gibi sat, eksik sinyali tamamlayacak yeni pilot/görüşme turuna çık, V2 mühendisliğine başlama. 2'den fazlası eksikse: FI'da kalınır, platform vizyonu dondurulur (bkz. Bölüm 10).

---

## 2. Milestone Bazlı Delivery Planı (V2 → V3 → V4)

Granülerlik FI'dakinden farklıdır: burada milestone'lar **hafta/ay** bazında, her faz kendi içinde 3-6 aylık bir bloktur. Tarihler sinyale bağlı olduğu için mutlak takvim değil, **faz içi göreli sıra** verilir.

### FAZ 2 — V2: Support Correlation + Connector Framework (Temel) + Workflow (tahmini 8-12 hafta)

**Vizyon dokümanındaki kapsam:** Support ticket correlation, similar historical incident, workflow.

**V2.1 — Connector Framework İskeleti (Hafta 1-3)**
- Kapsam: FI'daki mock Stripe/GitHub connector'ların yerini alacak, gerçek bir "connector arayüzü" (SDK'nın ilk versiyonu, henüz 3. taraf geliştiriciye açık değil, sadece kendi ekibinizin yazacağı) tanımlanır. İlk gerçek (mock değil) connector: pilot müşterinin fiilen kullandığı destek aracı (Zendesk/Intercom/Freshdesk — pilot müşteriden gelen S3 sinyaline göre seçilir, varsayımla değil).
- Acceptance Criteria: AC1 — connector arayüzü (auth, event polling/webhook, normalize edilmiş event şeması) FI'nin ingestion pipeline'ına, mock connector'lardaki gibi kod değişikliği gerektirmeden eklenebiliyor. AC2 — gerçek destek aracından gelen en az 1 ticket türü, normalize edilmiş event olarak ingestion'a giriyor ve FI'nin mevcut classifier/fingerprint mantığından geçiyor (yeniden yazılmıyor, genişletiliyor).

**V2.2 — Support Ticket Correlation (Hafta 4-6)**
- Kapsam: Bir incident'ın, aynı zaman aralığında açılan support ticket'larıyla eşleştirilmesi (müşteri X'ten gelen ticket, incident Y ile aynı kök nedene işaret ediyor mu).
- Acceptance Criteria: AC1 — bir incident'a en az 1 ilişkili ticket varsa, incident detail ekranında ticket referansı (başlık, açılış zamanı, müşteri) görünür. AC2 — yanlış eşleştirme oranı (pilot verisiyle manuel doğrulanan) kabul edilebilir seviyede (ilk sürümde %70+ isabet hedefi, kesin eşik pilot geri bildirimiyle kalibre edilir — ADR'de kayıt altına alınır).

**V2.3 — Similar Historical Incident (Hafta 6-8)**
- Kapsam: Yeni bir incident açıldığında, geçmişteki benzer (aynı fingerprint ailesi ya da semantik olarak yakın) incident'lar ve o zaman uygulanan çözüm/aksiyon gösterilir.
- Acceptance Criteria: AC1 — en az 5 geçmiş incident'ı olan bir fingerprint için, yeni bir örnek geldiğinde "3 ay önce aynı şey oldu, şu aksiyon uygulanmıştı" bilgisi gösterilir. AC2 — benzerlik hesaplaması (fingerprint tabanlı mı, embedding tabanlı mı) bir ADR'de gerekçelendirilmiş.

**V2.4 — Workflow (Hafta 8-11)**
- Kapsam: Incident'ın yaşam döngüsü durumları (yeni → araştırılıyor → çözüldü → kapatıldı) ve bunlara bağlı bildirimler (Slack/e-posta), atama (kime atandığı).
- Acceptance Criteria: AC1 — bir incident, durum değiştikçe geçmişi (audit trail) tutar. AC2 — en az 1 bildirim kanalı (Slack webhook) durum değişikliğinde tetiklenir. AC3 — pilot ekip, workflow'u kendi on-call sürecine gerçekten entegre ediyor (kullanım verisiyle doğrulanır, varsayımla değil).

**V2 Faz Sonu Kabul Kriteri (release gate):** En az 1 pilot, V2 özelliklerini (support correlation + historical + workflow) en az 4 hafta gerçek kullanımda deneyimlemiş ve devam ediyor. Bu karşılanmazsa V3'e geçilmez (bkz. Bölüm 1 mantığının V3 versiyonu, Bölüm 11).

### FAZ 3 — V3: Connector SDK (genişletilmiş) + Multi-tenant + Billing + Team Workflows + Analytics (tahmini 4-6 ay)

**V3.1 — Gerçek Multi-Tenancy (Ay 1-2)**
- Kapsam: FI/V2'de "her pilot kendi izole ortamında/veritabanında" model muhtemelen yeterliydi (bkz. ADR-P2). V3'te tek bir platform üzerinde birden fazla müşterinin **paylaşımlı altyapıda ama izole veride** çalışması.
- Acceptance Criteria: AC1 — tenant A'nın verisi, hiçbir API çağrısında, hiçbir log/hata mesajında tenant B'ye sızmıyor (izolasyon test paketiyle kanıtlı, bkz. Bölüm 3.3). AC2 — yeni tenant onboarding'i (self-servis ya da yarı-otomatik) 1 iş gününden kısa sürede tamamlanabiliyor.

**V3.2 — Connector SDK (3. taraf geliştiriciye açık) (Ay 2-3)**
- Kapsam: V2'de kendi ekibin yazdığı connector'lar, artık dokümante edilmiş bir SDK (arayüz + örnek connector + test kiti) haline gelir. Hedef: ajans/entegrasyon ortağı ekosisteminin kendi connector'ını yazabilmesi.
- Acceptance Criteria: AC1 — SDK dokümantasyonu ve "reference connector" (örnek, çalışan, testli) yayında. AC2 — en az 1 harici geliştirici (ya da bağımsız bir iç ekip üyesi, dokümana **sadece dokümana bakarak**) yeni bir connector'ı SDK dışı destek almadan yazabiliyor — bu, SDK'nın gerçekten kendi kendine yeterli olduğunun kanıtı.

**V3.3 — Billing (Stripe entegrasyonu, kendi platformunu monetize etme) (Ay 3-4, bkz. Bölüm 5 detay)**

**V3.4 — Team Workflows + Analytics (Ay 4-6)**
- Kapsam: Rol tabanlı erişim (RBAC), takım bazlı görünüm, incident/connector sağlığı üzerine dashboard/rapor (örn. "bu ay en çok hangi entegrasyon sorun çıkardı", "MTTR trendi").
- Acceptance Criteria: AC1 — en az 2 rol (admin, member) farklı yetki setiyle çalışıyor. AC2 — analytics dashboard'u, gerçek pilot verisiyle doğru sayılar üretiyor (manuel çapraz kontrol edilmiş).

**V3 Faz Sonu Kabul Kriteri:** En az 3 ödeyen müşteri, Stripe üzerinden gerçek ödeme yapıyor; en az 1 müşteri SDK üzerinden kendi connector'ını (ya da platformun sağladığı 3.'süncü bir connector'ı) kullanıyor.

### FAZ 4 — V4: Controlled Remediation (Approval-based) (tahmini 3-5 ay, V3'ten sonra ayrı gate ile)

**V4.1 — Dry-Run Runbook Motoru (Ay 1-2)**
- Kapsam: Bir incident için önerilen aksiyon (örn. "API key rotasyonu tetikle", "rate limit ayarını geçici yükselt") **gerçekte uygulanmadan** simüle edilir, etkisi gösterilir.
- Acceptance Criteria: AC1 — dry-run modu, hiçbir gerçek dış sistem çağrısı yapmadan "bu aksiyon uygulansaydı şu değişirdi" raporu üretir. AC2 — dry-run ve gerçek çalıştırma arasında kod yolu paylaşılır (ayrı, senkron tutulması gereken iki implementasyon değil) — bu bir ADR kararıdır (bkz. Bölüm 7).

**V4.2 — Approval Akışı (Ay 2-3)**
- Kapsam: Önerilen aksiyon, yetkili bir kullanıcının onayı olmadan çalışmaz; onay/red audit trail'e yazılır.
- Acceptance Criteria: AC1 — onaylanmamış hiçbir aksiyon dış sisteme etki etmiyor (negatif test ile kanıtlı). AC2 — onay veren kişi, zaman damgası, gerekçe kayıt altında.

**V4.3 — Gerçek Çalıştırma + Rollback (Ay 3-5)**
- Kapsam: Onaylanan aksiyon gerçekten uygulanır; başarısız olursa ya da istenmeyen sonuç doğarsa geri alınabilir (rollback).
- Acceptance Criteria: AC1 — desteklenen her aksiyon tipi için tanımlı bir rollback prosedürü var (otomatik ya da en azından belgelenmiş manuel adım). AC2 — en az 1 kontrollü üretim-benzeri ortamda gerçek aksiyon + rollback senaryosu uçtan uca test edilmiş, veri kaybı/yan etki olmadan.

**V4 Faz Sonu Kabul Kriteri:** En az 1 müşteri, gerçek (simülasyon değil) remediation aksiyonuna onay veriyor ve bu, incident çözüm süresini ölçülebilir şekilde kısaltıyor. V4, platformun en riskli fazı olduğu için bu faza girmeden önceki gate en katıdır (bkz. Bölüm 11).

---

## 3. Test Stratejisi Evrimi

FI'nin test piramidi (unit/integration/contract/e2e, kaynak dokümanın Bölüm 3'ü) temel iskelet olarak kalır; her faz kendine özgü **yeni bir test kategorisi** ekler, var olanları kaldırmaz.

### 3.1 V2: Connector Contract Testleri
- **Amaç:** Her connector (gerçek destek aracı entegrasyonu), FI'nin ingestion pipeline'ının beklediği normalize şemaya uyduğunu kanıtlamalı — connector'ın iç detayları (Zendesk'in kendi API şeması) değişse bile normalize çıktı sabit kalmalı.
- **Yaklaşım:** Her connector için bir "contract test seti": (a) gerçek API'den kayıtlı örnek yanıtlar (cassette/fixture) → normalize şemaya dönüşüm doğru mu, (b) API'nin hata durumları (rate limit, auth hatası, kısmi/bozuk yanıt) connector tarafından sessizce yutulmuyor, tanımlı bir hata tipine dönüşüyor mu.
- **Workflow test:** Durum makinesi (state machine) geçişleri (yeni→araştırılıyor→çözüldü→kapatıldı) için geçerli/geçersiz geçiş matrisi test edilir (örn. "kapatıldı"dan doğrudan "yeni"ye geçiş engellenmeli mi — bu bir üründen karar, testle kilitlenir).

### 3.2 V3: Connector SDK Test Kiti + Ekosistem Testleri
- **Amaç:** V2'de kendi yazdığınız connector'lar için sizin yazdığınız contract testler yeterliydi; V3'te SDK'yı 3. taraf kullanacağı için **SDK'nın kendisi bir test kiti sağlamalı** — yeni bir connector'ı yazan geliştirici, kendi connector'ının SDK sözleşmesine uyup uymadığını kendi CI'ında çalıştırabilmeli.
- **Yaklaşım:** SDK ile birlikte dağıtılan "conformance test suite" (soyut test sınıfı/arayüzü, örn. "her connector şu N testi geçmeli: auth hatası düzgün yansıtılıyor mu, rate limit backoff uygulanıyor mu, normalize şema şu JSON Schema'ya uyuyor mu"). Bu, tek tek connector'ları elle test etmek yerine yeni connector eklendikçe otomatik doğrulanan bir çerçeve kurar (detay CI bölümünde, 4.2).

### 3.3 V3: Multi-Tenant İzolasyon Testleri
- **Amaç:** Bir tenant'ın verisinin başka bir tenant'a asla sızmadığını kanıtlamak — bu, platformun güven temelidir; tek bir izolasyon açığı tüm platformu itibarsızlaştırır.
- **Yaklaşım:**
  - **Negatif erişim testi:** Tenant A'nın kimlik bilgileriyle, Tenant B'nin kaynağına (incident id, connector id, kullanıcı id) doğrudan erişim denenir — her uçta 403/404 (bilgi sızdırmayan bir hata) dönmeli, asla veri dönmemeli. Bu test **her yeni endpoint eklendiğinde** otomatik/parametrik olarak koşulmalı (yeni endpoint yazan geliştiricinin izolasyon testini unutması en yaygın hata sınıfı olduğu için, bir "tüm endpoint'leri tarayan" generic izolasyon test şablonu tercih edilir).
  - **Veri sızıntısı testi (log/hata mesajı düzeyinde):** Hata mesajlarının, stack trace'lerin başka tenant'a ait id/veri içermediği doğrulanır.
  - **Kaynak paylaşım testi:** Rate limit, kuyruk (Hangfire job) gibi paylaşılan kaynaklarda bir tenant'ın yoğun kullanımının başka tenant'ı etkilemediği (noisy neighbor) yük testiyle doğrulanır.
  - **Neden bu kadar ağır:** Multi-tenant izolasyon, "büyük ihtimalle çalışır"ın kabul edilemeyeceği tek test kategorisidir — bir güvenlik/güven olayı, tüm V3 yatırımını sıfırlayabilir.

### 3.4 V4: Remediation Dry-Run ve Rollback Testleri
- **Amaç:** Gerçek dış sistemlere etki eden aksiyonların güvenli olduğunu, hem çalıştırılmadan önce hem çalıştırıldıktan sonra kanıtlamak.
- **Yaklaşım:**
  - **Dry-run doğruluğu testi:** Dry-run'ın ürettiği "bu aksiyon şunu değiştirecek" tahmini, gerçek çalıştırmanın sonucuyla (kontrollü test ortamında) eşleşiyor mu — dry-run'ın "yalan söylememesi" kritik, çünkü kullanıcı onayını dry-run raporuna dayanarak veriyor.
  - **Yetkisiz çalıştırma negatif testi:** Onay olmadan hiçbir aksiyonun tetiklenmediği, API seviyesinde (onay adımı bypass edilmeye çalışılarak) test edilir.
  - **Rollback testi:** Her aksiyon tipi için "uygula → doğrula → geri al → orijinal duruma dönüldüğünü doğrula" döngüsü otomatik test edilir; rollback'i olmayan bir aksiyon tipi **prod'a çıkamaz** (release gate kuralı, bkz. 4.3).
  - **Kısmi başarısızlık testi:** Bir aksiyon zincirinin ortasında hata olursa (örn. 3 adımlı bir remediation'ın 2. adımı başarısız olursa) sistemin tutarsız bir ara durumda kalmadığı, ya tamamının geri alındığı ya da güvenli bir "manuel müdahale gerekli" durumuna düştüğü test edilir.
  - **Bu katman gerçek dış servislere karşı asla CI'da otomatik koşulmaz** — sandbox/mock dış servisler kullanılır; gerçek servise karşı çalıştırma sadece kontrollü, manuel onaylı bir ortamda (staging + gerçek ama izole test hesabı) yapılır.

---

## 4. CI/CD Evrimi

### 4.1 V1 (FI) → V2: Değişen Az
FI'nin CI pipeline'ı (Build → Test → Docker Build + Migration Check, kaynak dokümanın Bölüm 5'i) V2'de yapısal olarak aynı kalır; sadece test job'ı büyür (yeni connector contract testleri, workflow state machine testleri eklenir). Yeni bir pipeline aşaması gerekmez.

### 4.2 V3: Connector Contract Test Matrisi
- **Sorun:** SDK'ya connector sayısı arttıkça (kendi yazdıklarınız + 3. taraf), her connector için ayrı ayrı, elle tetiklenen testler ölçeklenmez.
- **Çözüm:** CI'da bir **matris job'ı** kurulur — repo içindeki `connectors/` klasöründeki her connector alt projesi otomatik keşfedilir (glob/convention-based), her biri için aynı conformance test suite (bkz. 3.2) paralel olarak koşulur. Yeni bir connector eklendiğinde CI matrisi otomatik büyür, pipeline dosyasının elle güncellenmesi gerekmez.
- **3. taraf connector'lar için:** Eğer connector'lar ayrı repolarda (bağımsız ekosistem paketleri olarak) geliştirilecekse, SDK'nın conformance test kiti bir NuGet paketi olarak dağıtılır; 3. taraf kendi CI'ında bu paketi bağımlılık olarak kullanır. Platformun kendi CI'ı sadece "resmi/first-party" connector'ları test eder.
- **Contract versiyonlama:** Normalize event şeması değiştiğinde (breaking change), eski connector'ların hangi şema versiyonunu desteklediği CI'da doğrulanır — bir şema versiyonu deprecate edilmeden önce, ona bağlı tüm connector'ların migrate olduğu kontrol edilir.

### 4.3 V3-V4: Multi-Tenant Test Ortamı
- **Sorun:** Tek tenant'lı bir test ortamı (FI/V2'deki gibi), izolasyon hatalarını yakalayamaz — izolasyon hatası tanım gereği en az 2 tenant gerektirir.
- **Çözüm:** CI'daki integration/e2e test aşamasında, test veritabanı **her zaman en az 2 sentetik tenant** ile seed edilir (varsayılan test fixture'ı tek tenant değil, iki tenant + çapraz erişim senaryoları içerir). Bu, "unuttum, tek tenant'la test ettim" hatasını yapısal olarak imkânsız hale getirir.
- **V4 için ek:** Remediation testleri, gerçek dış servislerin sandbox/mock modlarını kullanan **ayrı bir CI job'ı** olarak izole edilir (ana pipeline'ı yavaşlatmaması ve maliyetli/riskli işlemlerin ana akıştan ayrı gözden geçirilebilmesi için); bu job'ın başarısız olması, remediation özelliğini prod'a alan release'i bloklar ama diğer platform özelliklerinin release'ini bloklamaz (bağımsız release kapıları, bkz. 6.2).
- **Staging ortamı zorunluluğu:** V1/V2'de "canlıya doğrudan deploy" kabul edilebilirdi (kaynak dokümanın Bölüm 6'sı). V3'ten itibaren (gerçek ödeyen müşteriler + multi-tenant veri olduğu için) bir **staging ortamı** zorunlu hale gelir — her release önce staging'e, orada smoke test + izolasyon testi + (V4 için) dry-run doğrulaması geçtikten sonra prod'a gider.

---

## 5. Billing (Stripe) — Teknik ve GTM Etkisi

### 5.1 Teknik Etki
- **Yeni domain:** Subscription, plan (tier), usage-based limit (örn. "ayda X incident'a kadar", "Y connector'a kadar") kavramları platform veri modeline girer. Bu, mevcut Integration/Incident modellerinden bağımsız yeni bir "Billing" modülüdür (modular monolith sınırlarına uygun, bkz. ADR-001 ilkesi V3'e taşınır).
- **Stripe entegrasyonu şekli:** Stripe Checkout/Billing Portal kullanılarak ödeme akışının kendi UI'ınızda yeniden inşa edilmesinden kaçınılır (PCI kapsamını daraltır, geliştirme süresini kısaltır). Webhook tabanlı senkronizasyon (subscription.created, invoice.paid, subscription.canceled) platformun kendi tenant/plan durumunu günceller — burada ironik bir simetri var: **platformun kendisi, kendi FI/OP ürününün izlediği türden bir "webhook entegrasyonu" riski taşır** (Stripe webhook'u kaçırılırsa fatura durumu senkron kaçar). Bu risk, platformun kendi ingestion/idempotency mantığının bir iç müşterisi olarak ele alınabilir (dogfooding fırsatı, GTM'de anlatılabilir bir detay).
- **Kritik test alanı:** Webhook idempotency (aynı Stripe event'i 2 kez işlenirse çift faturalama/çift aktivasyon olmamalı — FI'daki idempotency ADR'sinin (ADR-008) burada gerçek bir bedeli var, artık "MVP'de kapsam dışı" denemez), plan downgrade/upgrade sırasında kullanım limitlerinin doğru uygulanması, ödeme başarısız olduğunda (dunning) erişimin nazik bir şekilde kısıtlanması (aniden kesmek yerine uyarı süreci).
- **Faz sırası içindeki yeri:** Billing, multi-tenancy'den (V3.1) **sonra** gelir — plan/kullanım limitleri tenant kavramına bağımlı olduğu için önce tenant izolasyonu sağlam olmalı.

### 5.2 GTM Etkisi
- **Fiyatlandırma modeli kararı:** FI/V2 döneminde muhtemelen elle anlaşılan sabit fiyat (pilot sonrası "aylık X TL/USD") vardı. V3 ile birlikte **self-servis fiyatlandırma sayfası** gerekir — bu, satış sürecini "her müşteriyle görüşerek fiyat belirleme"den "gel-kaydol-öde" modeline kaydırma kararıdır ve bir ADR olarak kayıt altına alınmalı (bkz. Bölüm 7).
- **Plan yapısı önerisi (ilk sürüm, pilot geri bildirimiyle kalibre edilecek):** Başlangıç planı (tek connector, sınırlı incident hacmi, workflow yok) → Büyüme planı (birden fazla connector, support correlation + workflow) → Platform planı (SDK, analytics, remediation — V4 hazır olduğunda). Bu üçlü yapı, V2→V3→V4 özellik sırasını fiyatlandırma katmanlarına doğal olarak eşler — mühendislik yol haritası ile GTM yol haritası aynı hikayeyi anlatır.
- **Mevcut pilot müşterilere geçiş:** İlk pilotlar (FI döneminde elle/özel anlaşmayla) yeni self-servis plan yapısına geçirilirken fiyat artışı olacaksa, bu **önceden, sürpriz olmadan** iletilmeli ("erken destekçi" indirimi/kilitli fiyat önerilmesi ilişkiyi korur — bu müşteriler aynı zamanda referans/vaka çalışması kaynağıdır, bkz. Bölüm 8).
- **GTM mesajlaşmasına etkisi:** "Ücretsiz pilot" anlatısından "ücretli platform" anlatısına geçiş, GTM mesajlaşmasının kendisini de değiştirir — bu Bölüm 9'da detaylandırılır.

---

## 6. GitHub Repo Yapısının Evrimi

### 6.1 V1-V2: Monorepo (Modular Monolith ile Uyumlu)
FI'nin repo yapısı (`/src`, `/tests`, `/docs`, `/docker`, kaynak dokümanın Bölüm 7'si) V2'de büyür ama **tek repo kalır**. Yeni modüller (`Support`, `Workflow`) mevcut modüler monolith yapısına (`Integrations`, `Ingestion`, `Incidents`, `Shared`) eklenir. Connector'lar `/src/Connectors/{ConnectorAdı}` altında toplanır.

**Neden hâlâ monorepo:** V2'de hâlâ tek bir ekip (muhtemelen 1-3 kişi) tüm kod tabanına dokunuyor; ayrı repo/deploy pipeline'ı, bu ölçekte koordinasyon maliyetini artırır, fayda getirmez.

### 6.2 V3: Seçici Ayrışma Başlar
- **Connector SDK ayrışması:** SDK, 3. tarafa dağıtılacağı andan itibaren (V3.2) **ayrı bir repoya/paket olarak** çıkarılmalı — bir NuGet paketi (örn. `OP.Connectors.Sdk`) olarak versiyonlanır, semantic versioning ile (bkz. `api-design` ilkeleri: extend-only, breaking change'ler major versiyon). Bu ayrışmanın nedeni teknik değil **sözleşme netliği**dir — 3. taraf geliştirici, platformun ana kod tabanına erişmeden, sadece SDK sürüm notlarına bakarak çalışabilmeli.
- **Ana platform hâlâ monorepo kalır, ama iç modül sınırları sıkılaştırılır:** Billing, Multi-tenant, Analytics gibi yeni modüller eklendikçe, modüller arası çapraz referansların sadece tanımlı arayüzler üzerinden olması kuralı (ADR-001, FI dönemi) artık **derleme zamanında zorlanabilir** hale getirilmeli (örn. proje referans kısıtlamaları, mimari test — architecture fitness function). Bu, gelecekteki bir servis ayrışmasını (V4+ sonrası, eğer gerekirse) ucuzlaştırır.
- **Ayrı repo gerektiren başka bir sinyal:** Eğer bağımsız bir deploy takvimi ihtiyacı doğarsa (örn. billing modülünün, ana platformdan bağımsız hotfix'lenmesi gerekiyorsa, ya da farklı bir ekip billing'e sahip olacaksa) o modül ayrışır. **Kural: ayrışma, organizasyonel/deploy ihtiyacı doğduğunda yapılır, "büyüdü" diye peşin yapılmaz** (erken mikroservisleşme, FI'nin ADR-001'inde reddedilen aynı hatanın platform seviyesindeki tekrarıdır).

### 6.3 V4: Remediation Motoru — Ayrışma Değerlendirmesi
Remediation motoru (dry-run + approval + execution), riskli ve güvenlik açısından hassas olduğu için **ayrı bir deploy/release döngüsü** düşünülebilir (daha sıkı review, daha yavaş release kadansı, ayrı bir "production access" yetki modeli). Ancak kod tabanı hâlâ monorepo içinde kalabilir (modül olarak ayrışır, repo olarak ayrışmaz) — asıl ihtiyaç **release izolasyonu**dur, kod izolasyonu değil. Repo ayrışması ancak ayrı bir ekip bu modülün tam sahipliğini alırsa gerekçelenir.

### 6.4 Genel İlke
Repo yapısı kararı her fazda şu soruya göre verilir: **"Bu ayrışma hangi somut sürtünmeyi çözüyor?"** (deploy bağımsızlığı, 3. taraf sözleşme netliği, takım sahipliği). Soyut "büyük platformlar mikroservis/multi-repo olur" varsayımıyla ayrışma yapılmaz — bu, kapsam şişmesi riskinin (Bölüm 11) mimari versiyonudur.

---

## 7. Platform Seviyesi ADR Listesi

FI'nin ADR listesi (kaynak dokümanın Bölüm 8'i, ADR-001 – ADR-010) geçerliliğini korur ve bazıları V2+'da **yeniden ziyaret edilmesi gereken** kararlar olarak işaretlenir. Yeni platform seviyesi ADR'ler:

1. **ADR-P1: V2'ye geçiş tetikleyicisi** — hangi sinyallerin (Bölüm 1) V2 yatırımını başlattığı, karar tarihinde hangi verinin mevcut olduğu; gelecekte "neden o zaman başladık" sorusuna somut kayıt.
2. **ADR-P2: Gerçek multi-tenancy'ye ne zaman ve neden geçildi** — V1/V2'deki "tenant başına izole ortam" modelinden V3'teki "paylaşımlı altyapı, izole veri" modeline geçiş gerekçesi (maliyet, operasyonel yük, onboarding hızı), ve bu geçişin veri migrasyon stratejisi.
3. **ADR-P3: Connector SDK'nın plugin modeli seçimi** — SDK'nın kod-tabanlı bir arayüz (derleme zamanı, NuGet paketi) mi yoksa konfigürasyon/manifest-tabanlı (çalışma zamanı, kod yazmadan bağlanan) bir model mi olduğu; ilk sürümün neden kod-tabanlı (daha esnek, daha az soyutlama maliyeti) seçildiği ve manifest-tabanlı modelin hangi büyüme aşamasında (çok sayıda düşük-kodlu entegratör talebi doğarsa) yeniden değerlendirileceği.
4. **ADR-P4: Normalize event şemasının versiyonlama stratejisi** — connector sayısı arttıkça şema evrimi nasıl yönetiliyor (additive-only mi, breaking change'lerde nasıl bir deprecation penceresi var).
5. **ADR-P5: Remediation approval mimarisi kararı** — onay akışının tek-kişi mi çok-kişi (4-eyes) mi olduğu, hangi aksiyon tiplerinin hangi onay seviyesini gerektirdiği (örn. "rate limit ayarı" tek onay, "veri silme içeren aksiyon" iki onay), ve bu eşiklerin nasıl belirlendiği (müşteri risk iştahına göre mi, sabit mi).
6. **ADR-P6: Dry-run ve gerçek çalıştırma arasında kod paylaşımı kararı** — aynı kod yolunun mu kullanıldığı yoksa ayrı bir simülasyon katmanının mı yazıldığı (Bölüm 2, V4.1); bu kararın rollback güvenilirliği üzerindeki etkisi.
7. **ADR-P7: Self-servis fiyatlandırma ve Stripe Billing modeli seçimi** — neden kendi ödeme UI'ı değil Stripe Checkout/Billing Portal, plan yapısının özellik fazlarıyla nasıl eşlendiği (Bölüm 5.2).
8. **ADR-P8: Repo ayrışma kriterleri** — hangi somut sinyalin (deploy bağımsızlığı, 3. taraf sözleşmesi, takım sahipliği) hangi modülün ayrışmasını tetiklediği (Bölüm 6.4), monorepo'da kalma varsayılan/default kararının gerekçesi.
9. **ADR-P9: Analytics veri modeli — gerçek zamanlı mı, batch mi** — V3.4'teki analytics dashboard'unun canlı sorgu mu yoksa periyodik agregasyon mu kullandığı, multi-tenant ortamda sorgu maliyetinin nasıl kontrol edildiği.
10. **ADR-P10: "Kitchen sink" karşıtı kapsam disiplini** — her faz için hangi özelliklerin bilinçli olarak **dışarıda bırakıldığı** (örn. V2'de "çoklu dil desteği yok", V3'te "on-prem deploy seçeneği yok"), bu kararların hangi sinyal gelirse yeniden açılacağı (Bölüm 11 ile bağlantılı).

Her ADR, FI'deki formatla tutarlı (bağlam, karar, sonuç/trade-off, yarım-1 sayfa), `/docs/adr/` altında numaralı, FI'nin ADR'leriyle aynı klasörde ama `P` (platform) öneki ile ayrılmış dosyalar halinde tutulur.

---

## 8. Pilot / Müşteri Genişletme Planı (V2+)

### 8.1 Mevcut FI Pilot(lar)ının V2'ye Taşınması
- **Erken erişim çerçevesi:** FI pilotlarına V2 özellikleri, genel kullanıma açılmadan önce "erken erişim" olarak sunulur — bu hem gerçek kullanım verisiyle V2'yi doğrulama fırsatı hem de ilişkiyi derinleştiren bir jest (Bölüm 5.2'deki "erken destekçi" mantığıyla tutarlı).
- **Geçiş mekaniği:** Mevcut pilot verisi (incident geçmişi, connector konfigürasyonu) kaybolmadan V2 şemasına migrate edilir — bu bir teknik gereklilik değil **güven gerekliliğidir**: bir pilotun "platform büyüdü ama benim verim sıfırlandı" deneyimi yaşaması, referans değerini yok eder.
- **Geri bildirim döngüsü:** V2 milestone'larının her birinin (Bölüm 2) acceptance criteria'sında geçen "pilot" terimi öncelikle bu mevcut pilotları işaret eder — yeni özellik, önce onlarla doğrulanır.

### 8.2 Yeni Pilot Bulma — Hedef Segment Genişlemesi
FI'nin hedef kitlesi (Bölüm 9.1, kaynak doküman: backend/platform mühendisleri, 3. parti entegrasyonu olan küçük-orta ekipler) V2+'da iki yeni segmentle **genişletilir, değiştirilmez**:

1. **Support-heavy SaaS ekipleri:** Ürünlerinde yoğun destek talebi üreten entegrasyon noktaları olan (ödeme, webhook, veri senkronizasyonu ağırlıklı) SaaS şirketleri — V2'nin support ticket correlation özelliği doğrudan bu segmentin acı noktasına hitap eder. Bulma kanalı: destek ekibi büyüklüğü (Zendesk/Intercom kullanan, "support engineer" pozisyonu açan şirketler LinkedIn'de aranabilir), mühendislik değil **destek ekibi liderleri** (Head of Support, Support Engineering Manager) de artık hedef kitleye eklenir — FI'da sadece mühendislere odaklanılıyordu, V2'de destek tarafı yeni bir alıcı personası olur.
2. **Ajanslar / entegrasyon ortakları:** Birden fazla müşteri için entegrasyon kurulumu/bakımı yapan dijital ajanslar, sistem entegratörleri — bunlar için OP, tek bir platformdan **birden fazla müşterinin** entegrasyon sağlığını izleme aracı olur (bu, multi-tenant V3'ün bir alıcı segmenti olarak da işlev görür — ajans, kendi müşterilerini "tenant" gibi yönetebilir). Bulma kanalı: Upwork/Clutch gibi ajans dizinlerinde "Stripe/webhook entegrasyon" hizmeti sunan ajanslar, Shopify/e-ticaret entegrasyon ajansları (yüksek webhook hacmi olan bir dikey).

### 8.3 Genişletme Kadansı
V2 gate'i geçildikten sonra: FI'daki 15 görüşme modeli (kaynak doküman Bölüm 9) tekrarlanır ama **iki ayrı huni** olarak — biri mevcut segment derinleştirme (support-heavy SaaS), biri yeni segment keşfi (ajanslar). Her huninin kendi 8-10 görüşmelik ilk turu, kendi devam/pivot kararını üretir (aynı kriterler, Bölüm 1.1 sinyalleri, segment bazında ayrı ayrı değerlendirilir) — bir segment çalışmazsa diğerini durdurmaz.

---

## 9. GTM Mesajlaşma Evrimi: FI'dan OP'a

### 9.1 Mesajlaşma Çekirdeğinin Değişimi
| Boyut | FI (V1) mesajı | OP (V2+) mesajı |
|---|---|---|
| Konumlandırma | "Entegrasyon hatası neden oldu, kanıta dayalı hızlı teşhis" | "Entegrasyon operasyonlarının merkezi katmanı — tespit, ilişkilendirme, çözüm tek yerde" |
| Alıcı personası | Backend/platform mühendisi | Mühendis + destek ekibi lideri + (V3'te) engineering manager/CTO (bütçe sahibi) |
| Kanıt türü | Tek senaryo demo'su (Stripe 401 burst) | Çoklu senaryo + gerçek müşteri vaka çalışması (V2 pilotlarının somut MTTR/verimlilik kazanımı) |
| Duygusal çekiş | "30 dakikalık log kazmayı 30 saniyeye indirir" (bireysel zaman tasarrufu) | "Ekipler arası (mühendislik+destek) koordinasyon maliyetini düşürür" (organizasyonel verimlilik) |

### 9.2 İçerik Stratejisi Değişimi
- **FI dönemi (kaynak doküman Bölüm 12):** Build-in-public, 14 günlük teknik yolculuk, bireysel geliştiricinin güvenilirliğini inşa eden içerik.
- **V2+ dönemi:** İçerik iki katmana ayrılır:
  1. **Ürün derinliği içeriği** (mühendis kitlesi için devam eder): connector SDK tasarımı, multi-tenant izolasyon yaklaşımı gibi teknik derinlik paylaşımları — güvenilirlik inşası devam eder ama artık "MVP yaptım" değil "platform işletiyorum" tonunda.
  2. **Sonuç/vaka odaklı içerik** (yeni, karar vericiler için): "X ekibi, entegrasyon incident'larını %Y azalttı" tarzı somut sonuç paylaşımları — bu içerik FI döneminde mümkün değildi (henüz sonuç yoktu), V2 pilotlarının olgunlaşmasıyla mümkün hale gelir.
- **LinkedIn kadansı:** FI'daki "4 gönderi / 14 gün" yoğunluğu sürdürülebilir değildir uzun vadede; V2+'da haftalık 1 içerik ritmine geçilir (build-in-public'ten "ürün + müşteri hikayesi" karışımına).

### 9.3 Cold Outreach'in Değişimi
- FI'nin outreach'i (Bölüm 11, kaynak doküman) "problem var mı" sorusuyla başlıyordu — keşif amaçlı. V2+'da outreach'e **sosyal kanıt** eklenir ("[X] gibi ekipler bunu kullanıyor" referansı, ilk pilotlar izin verdiği ölçüde) — bu, soğuk mailin cevap oranını FI dönemine göre yükseltmesi beklenen tek somut değişikliktir.
- Yeni segment (ajanslar) için outreach mesajı farklılaşır: "sizin müşterileriniz için tek panelden entegrasyon sağlığı" çerçevesi, mühendis-to-mühendis mesajından farklı, iş değeri odaklı bir dil kullanır.

### 9.4 Fiyatlandırma Sayfasının GTM'e Girmesi
V3'ten itibaren (Bölüm 5.2), GTM akışına ilk kez **kendi kendine hizmet eden bir dönüşüm hunisi** eklenir (içerik → fiyatlandırma sayfası → self-servis kayıt → deneme → ödeme) — FI/V2'de her adım elle (görüşme, pilot daveti) yürütülüyordu. Bu, GTM operasyonunun kendisinin de bir "platform"a dönüştüğü anlamına gelir; elle yürütülen outreach tamamen bitmez ama artık huninin tek girişi değildir.

---

## 10. Continue / Pivot Kararı — Platform Seviyesinde

FI'nin kendi continue/pivot/kill çerçevesi (kaynak doküman Bölüm 13) tek bir ürün (V1) için tanımlıydı. Platform seviyesinde bu çerçeve **her faz geçişinde tekrar uygulanır**, tek seferlik bir karar değildir.

### 10.1 "V2'ye yatırım yapmaya değer" kararı
Bölüm 1'deki S1-S5 sinyallerinin tamamı karşılanıyorsa: V2'ye geç. Bu doküman zaten bu kararın somutlaştırılmış hali.

### 10.2 "FI'da kal, platforma genişleme" kararı
Aşağıdaki durumlardan **herhangi biri** gerçekleşirse platform genişlemesi durdurulur, FI olduğu haliyle (belki küçük iyileştirmelerle) sürdürülür:
- Bölüm 1'deki sinyallerden 2 ya da daha fazlası, 2 ek pilot turundan sonra hâlâ karşılanmıyor.
- Pilotlar FI'nin dar kapsamından memnun, "daha fazlasını istemiyoruz, sadece bunu iyi yapın" sinyali veriyor (bu bir başarısızlık değil, **net bir konumlandırma sinyalidir** — "nokta çözüm" olarak kalmak da meşru bir iş modelidir, her B2B araç platform olmak zorunda değildir).
- FI'nin kendisi hâlâ (V2 mühendisliğine ayrılacak kaynakla kıyaslandığında) yeterince kârlı/büyüyen bir iş değilse — platform yatırımı, mevcut geliri büyütmekten daha riskli bir bahis haline gelir.

### 10.3 Faz-arası kararların bağımsızlığı
V2'ye geçmiş olmak, V3'e otomatik geçiş anlamına gelmez — V3'ün kendi gate'i (Bölüm 2 sonundaki "V2 Faz Sonu Kabul Kriteri") ayrıca karşılanmalı. Aynı mantık V3→V4 için de geçerli, V4'ün gate'i (Bölüm 11) en katı olanıdır çünkü remediation riski en yüksek fazdır. **Her faz kendi bağımsız "durma noktası"dır** — platform, herhangi bir fazda "burada duruyoruz, bu yeterince iyi bir ürün" diyerek sağlıklı bir şekilde durabilmelidir; bu, tasarımın bir zayıflığı değil kasıtlı bir özelliğidir (bkz. Bölüm 11).

---

## 11. En Büyük Delivery Riski: Kapsam Şişmesi ("Kitchen Sink" Platformu) ve Disiplin Mekanizmaları

### 11.1 Riskin Tanımı
Vizyon dokümanındaki V2→V3→V4 yol haritası, kendi başına bir **davet**tir: her faz bir öncekinin üzerine özellik ekler, hiçbiri "bitmiş" hissettirmez, mühendislik ekibi "bir sonraki özellik platformu tamamlayacak" yanılgısına düşebilir. Bu, iki somut şekilde gerçekleşir:
1. **Süre şişmesi:** "8-12 hafta" tahmini olan V2, sinyalsiz özellik eklemeleriyle (pilot talep etmediği "nice-to-have"ler) 6 aya çıkar; bu sürede pazar/rakip durumu değişebilir, ekip motivasyonu düşebilir, nakit/zaman bütçesi tükenebilir.
2. **Kitchen sink platformu:** Her müşterinin farklı bir isteğine "olur, ekleyelim" denerek platform, hiçbir segment için gerçekten derin olmayan, genel-geçer bir araç haline gelir — FI'nin dar ama keskin değer önermesi (kaynak dokümanın Bölüm 0'ındaki temel felsefe) kaybolur.

### 11.2 Disiplin Mekanizmaları

1. **Zorunlu faz-öncesi validasyon gate'i (bu dokümanın omurgası):** Bölüm 1 (V2 için), Bölüm 2 sonundaki faz kabul kriterleri (V3, V4 için) — hiçbir faza, önceki fazın gate'i geçilmeden başlanmaz. Bu kural yazılı ve **istisnasız** olmalı; "bu sefer özel, hemen başlayalım" istisnası, disiplinin ilk kırıldığı yerdir.
2. **Her milestone'da "hayır listesi":** Her faz planlanırken (Bölüm 2), o fazda **bilinçli olarak yapılmayacak** özellikler açıkça listelenir (ADR-P10) ve bu liste pilot/müşteri taleplerine karşı bir referans olarak kullanılır — "bu istek geçerli ama bu fazın kapsamı dışında, roadmap'e not edildi" cevabı standart hale getirilir.
3. **Tek-müşteri özelleştirme yasağı:** Bir özellik, sadece tek bir müşterinin talebiyle platforma eklenmez — Bölüm 1'deki "en az 2 kaynaktan spontane talep" ilkesi (S3) her yeni özellik kararı için genelleştirilir. İstisna: ücretli bir "custom development" anlaşması varsa, bu platformun ana kod tabanına değil ayrı bir eklenti/entegrasyon katmanına yazılır (mimari olarak izole, ADR-P8'in ayrışma mantığıyla tutarlı).
4. **Zaman kutulama (timeboxing) + geriye dönük gözden geçirme:** Her faz için Bölüm 2'deki süre tahminleri (V2: 8-12 hafta, V3: 4-6 ay, V4: 3-5 ay) birer üst sınır olarak ele alınır; tahmini 1.5 katını aşan bir faz, otomatik olarak bir "devam mı, kapsamı daralt mı" karar noktasını tetikler (sessizce uzamaya bırakılmaz).
5. **"Durabilir platform" ilkesi (Bölüm 10.3):** Her fazın kendi başına satılabilir, tam bir artefakt olması zorunluluğu (Bölüm 2'deki her faz sonu kabul kriteri), "bitirmeden duramayız" baskısını ortadan kaldırır — V2'de durup yıllarca o hâlde kalmak, mimari ya da iş açısından bir başarısızlık değildir.
6. **Remediation (V4) için ekstra fren:** V4, geri dönüşü en zor fazdır (gerçek dış sistemlere etki eder); bu fazın gate'i sadece kullanım sinyaliyle değil, **güven sinyaliyle** de ölçülür (Bölüm 2'deki V4 kabul kriteri: "bir müşteri gerçek onaya razı oluyor" — bu, teknik hazır olmaktan daha yüksek bir çıtadır, kasıtlı olarak). V4'e hiç girilmemesi (platformun V3'te "yeterince iyi" olarak durması) tamamen kabul edilebilir bir sonuçtur.
7. **Roadmap'in içe değil dışa açık olması:** Vizyon dokümanındaki V2-V4 sıralaması, ekibe "yapılacaklar listesi" olarak değil, "sinyal gelirse hazır olunacak olasılıklar listesi" olarak sunulur — bu çerçeveleme farkı, ekibin "roadmap'i bitirme" refleksiyle çalışmasını, "sinyali takip etme" refleksiyle çalışmasına dönüştürür ve kapsam şişmesinin kök nedenini (roadmap'i bir taahhüt gibi okumak) ortadan kaldırır.

---

## 12. Özet Karar Akışı

```
FI (V1) "continue" kararı (kaynak doküman Bölüm 13)
        │
        ▼
Bölüm 1 gate: S1-S5 sinyalleri karşılanıyor mu?
        │
   ┌────┴────┐
  Hayır      Evet
   │          │
   ▼          ▼
FI'da kal   V2 başlar (Bölüm 2, 8-12 hafta)
(Bölüm 10.2) │
             ▼
        V2 faz-sonu gate: ≥1 pilot 4+ hafta V2'de kalıcı kullanımda mı?
             │
        ┌────┴────┐
       Hayır      Evet
        │          │
        ▼          ▼
   V2'de dur    V3 başlar (Bölüm 2, 4-6 ay: multi-tenant→SDK→billing→analytics)
   (Bölüm 10.3) │
                ▼
           V3 faz-sonu gate: ≥3 ödeyen müşteri + ≥1 SDK kullanımı?
                │
           ┌────┴────┐
          Hayır      Evet
           │          │
           ▼          ▼
      V3'te dur   V4 başlar (Bölüm 2, 3-5 ay: dry-run→approval→execution+rollback)
      (Bölüm 10.3) │
                   ▼
              V4 faz-sonu gate: ≥1 müşteri gerçek remediation'a onay veriyor mu?
                   │
              ┌────┴────┐
             Hayır      Evet
              │          │
              ▼          ▼
         V4'te (dry-run  Platform olgunlaşmış,
         seviyesinde) dur  sonraki genişleme sinyale bağlı
```

Her ok, Bölüm 11'deki disiplin mekanizmalarıyla korunur — hiçbir geçiş otomatik/varsayılan değildir, her biri açık bir kanıt gerektirir.
