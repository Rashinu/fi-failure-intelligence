# Architecture Review — Nihai Karar Dokümanı

**Rol:** Ana Mimar (Principal .NET Architect) — 12 uzman analiz dokümanının (6 FI + 6 OP) ve iki sentez dokümanının (`FAILURE_INTELLIGENCE_ARCHITECTURE.md`, `OPERATIONS_PLATFORM_ARCHITECTURE.md`) üzerinden geçilerek verilen nihai karar.
**Tarih:** 2026-07-12
**Kapsam:** Bu doküman kod üretmez; yalnızca hangi projeye, ne zaman, hangi sırayla başlanacağına dair kararı ve gerekçesini içerir.

---

## 1. Özet Karar

| Soru | Karar |
|---|---|
| Bugün hangi proje geliştirilir? | **Yalnızca FI (AI Integration Failure Intelligence)** |
| OP (Operations Platform) ne zaman devreye girer? | Hiç girmeyebilir — yalnızca `FAILURE_INTELLIGENCE_ARCHITECTURE.md` Bölüm 44 ve `OPERATIONS_PLATFORM_ARCHITECTURE.md` Bölüm 3.2'deki Gate 1 sinyalleri (≥2 pilot devam + ≥1 ödeme niyeti + haftalık kullanım + ayda 8-10 gerçek incident) karşılanırsa |
| İki dokümanın kapsamı çakışıyor mu? | Hayır. FI dokümanı kendi post-MVP'sini (Bölüm 43) OP'a taşımıyor; OP dokümanı FI'nin şemasını/sözleşmesini "sabit referans" sayıp yeniden tasarlamıyor. Bu tutarlılık her iki sentez agent'ı tarafından da korunmuş, ben de doğruladım. |
| Bugün atılacak ilk teknik adım | `FAILURE_INTELLIGENCE_ARCHITECTURE.md` Bölüm 50'deki M1 görev tanımı — **kullanıcının açık onayı olmadan bu adıma geçilmeyecek** (görev talimatı gereği). |

---

## 2. Mimari gereğinden fazla karmaşık mı?

**FI (bugün geliştirilecek): Hayır, sınırda ama disiplinli.**

12 agent'ın önerdiği bazı unsurlar (RLS öneri, plan-bazlı rate limit, ML tabanlı PII tespiti) MVP'ye sızmıştı; sentez sırasında bunlar post-MVP'ye taşındı (bkz. `FAILURE_INTELLIGENCE_ARCHITECTURE.md` §8, §43, ADR-012). Geriye kalan mimari — modular monolith, tek PostgreSQL+Redis, Hangfire+outbox, deterministik sınıflandırma + evidence-only AI — bir MVP için makul bir taban çizgisidir; hiçbiri "gösteriş" amaçlı değil, her biri ürünün kendi iddiasını (evidence-backed root cause) kanıtlamak için gerekli.

Benim eklediğim tek düzeltme: **`AiAnalysisLog`, `PromptVersion`, `IncidentReview` üç ayrı tablo** olarak tasarlanmış (04'ün önerisi, 03'ün şemasında yoktu). Bu, MVP için gerçekten sınırda bir karmaşıklık artışı — 3 tablo yerine `ai_analyses` tablosuna birkaç nullable kolon (`retry_count`, `parse_success`, `reviewed_by`, `review_decision`) eklenerek de aynı gözlemlenebilirlik sağlanabilirdi. Ancak `AiAnalysisLog`'un "her deneme dahil, başarısızlar dahil" append-only olması gerekliliği (debugging ve golden-dataset genişletme için) ve `IncidentReview`'ın audit-amaçlı ayrı kayıt olması gerekliliği teknik olarak haklı; **bu üç tabloyu koruyorum, ayırmıyorum** — ama M1-M2'de yalnızca `Integrations`/`ApiKeys` implemente edilecek, bu üç tablo M5-M6'ya kadar gerekmeyecek, sıra buna göre zaten doğru kurulmuş.

**OP: Kasıtlı olarak "fazla" — ve bu doğru.** Bir vizyon dokümanı, olası genişleme yüzeyini eksik göstermemeli. Kitchen-sink riskine karşı disiplin mekanizması (ADR-P10, her fazın "hayır listesi", zorunlu gate'ler) yeterli.

---

## 3. MVP iki haftada gerçekleştirilebilir mi?

**Gerçekçi cevap: Sıkı ama mümkün — tek koşulla: tam zamanlı, tek geliştirici (Claude Code ile birlikte), sıfır scope creep.**

14 günlük plan (`FAILURE_INTELLIGENCE_ARCHITECTURE.md` Bölüm 42, kaynak `06`'nın gözden geçirilmiş hali) gün 8-11 arasında (Hangfire+AI job, structured output+prompt, evidence collector+deployment correlation, Serilog+Seq+health check+OpenTelemetry) yüksek yoğunluklu 4 gün içeriyor. Bu blok gerçek risk taşıyor: AI structured-output validasyon zincirini (parse/şema/echo/confidence/grounding, `04`'ün tasarımı) ilk denemede sorunsuz kurmak genellikle 1 günden fazla sürer.

**Değerlendirmem:** 14 gün "iddialı ama savunulabilir" bir hedef; 18-21 gün daha gerçekçi bir bütçe. Ancak bu bir sorun değil — plan zaten milestone-bazlı yapılandırılmış (M1-M6) ve her milestone kendi DoD'siyle bağımsız test edilebilir/deploy edilebilir durumda tasarlanmış. Gecikme olursa hangi milestone'da olunduğu her zaman nettir; "14 gün" bir tavan değil, bir hedef tarih olarak ele alınmalı. GTM/validasyon süreci zaten teknik takvimle paralel ve ondan bağımsız ilerleyecek şekilde tasarlanmış (Bölüm 44-46), bu yüzden teknik gecikme validasyon sürecini bloklamaz.

---

## 4. En büyük üç teknik risk

1. **AI evidence-only kısıtının gerçekte zorlanması.** Prompt seviyesinde "sadece evidence'ı kullan" demek yeterli değildir; `04`'ün önerdiği post-hoc grounding kontrolü (evidence'ta olmayan iddiaların substring/kelime-örtüşmesiyle tespiti) basit ama kırılgan bir yöntemdir — yanlış pozitif (geçerli ama farklı kelimelerle ifade edilmiş bir çıkarımı reddetme) riski yüksektir. Golden dataset (20 senaryo) bu riski azaltır ama MVP'de üretim öncesi tam güven vermez. **Mitigasyon zaten planda var** (confidence eşiği + human review + golden dataset); risk kabul edilebilir ama izlenmeli.

2. **Fingerprint algoritmasının gerçek dünya verisiyle "yanlış gruplama" yapması.** Kategoriye-özgü `errorSignature` normalizasyonu (UUID/timestamp maskesi) sentetik/mock event'lerde iyi çalışır ama gerçek pilot verisiyle (Bölüm 44, pilot hedefi #1: "gerçek üretim verisiyle classifier/fingerprint doğruluğu") beklenmedik biçimde farklı hataları birleştirebilir veya aynı hatayı gereksiz yere bölebilir. Bu, ürünün temel değer önermesini doğrudan zedeler.

3. **AI provider maliyet/latency öngörülemezliği.** Open Decision #1 (Bölüm 49) olarak zaten işaretlenmiş — model seçimi gerçek test yapılmadan kesinleştirilmemiş. Token bütçesi/model-tier stratejisi kağıt üzerinde iyi tasarlanmış ama gerçek incident hacmiyle doğrulanmadı.

---

## 5. En büyük üç ürün riski

1. **Pazar boşluğu boş değil.** FI Agent 1'in WebSearch bulgusu: EventDock (AutoGuard AI) ve HookHound, FI'ya kavramsal olarak zaten yakın küçük rakipler. "Evidence-backed root cause" iddiası pazarlama cümlesi olarak değil, MVP'de gerçekten kanıtlanması gereken bir varsayım olarak ele alınmalı (bu, analiz dokümanında da doğru şekilde işaretlenmiş).

2. **"İnsan onayı" sürtünmesi ikili risk taşır.** Çok sık `NEEDS_HUMAN_REVIEW` tetiklenirse ürün "otomasyon" değil "biraz daha organize edilmiş manuel iş" gibi hissettirir; çok nadir tetiklenirse yanlış güven riski oluşur. Bu kalibrasyon yalnızca gerçek pilot verisiyle yapılabilir, MVP tasarımında önceden çözülemez.

3. **Derinlik-genişlik gerilimi validasyon sürecini yavaşlatabilir.** Üç mock connector (Stripe/GitHub/SES-SendGrid) ile sınırlı kalmak doğru bir MVP kararı, ama görüşülen 15 kişiden çoğu "benim asıl entegrasyonum bunlardan biri değil" derse (örn. Salesforce, Twilio, HubSpot), problem-interview sinyali zayıf çıkabilir — bu, ürünün kendisinin değil, seçilen ilk 3 senaryonun temsil gücünün riski.

---

## 6. Hangi kararlar ertelenmeli?

FI ve OP dokümanlarındaki Open Decision listeleri (FI §49, OP §27) zaten disiplinli şekilde ayrılmış. En kritik üçü:

- **AI provider/model kesin seçimi** (FI Open Decision #1) — golden dataset ile gerçek maliyet/latency testi yapılmadan kilitlenmemeli.
- **RLS'in MVP'de bile eklenip eklenmeyeceği** (FI Open Decision #5) — **benim ek tavsiyem:** `tenant_id`/RLS'in kendisini eklemeyin (post-MVP kalsın), ama `integrations` tablosuna nullable, kullanılmayan bir `organization_id uuid NULL` placeholder kolonu M1'de eklemek hemen hemen sıfır maliyetlidir ve V2'ye geçilirse backfill migrasyonunu ucuzlaştırır. Bu bir "erken multi-tenancy" değil, ucuz bir sigorta — ekip karar verirse uygulanabilir, zorunlu değil.
- **OP'un tüm V2-V4 kararları** — bunların hiçbiri bugün karara bağlanmamalı; ilk karar noktası Gate 1 sinyalleri geldiğinde, o günün gerçek pilot verisiyle yeniden değerlendirilmeli. Bugünden "V3'te connector SDK böyle olacak" gibi kararları donduruyormuş gibi okumak yanlış olur — OP dokümanı bir taahhüt değil, bir hazır-referans çerçevesidir.

---

## 7. İlk hangi entegrasyon kanıtlanmalı?

**Mock Stripe (payment API + webhook)** — hem FI hem OP dokümanları bunda hemfikir, ben de aynı gerekçelerle onaylıyorum:
- İş etkisi (para kaybı) demo/anlatım için en güçlü senaryo.
- Auth, webhook signature, rate-limit, 5xx, schema-mismatch gibi FI'nın 11 kategorilik taksonomisinin çoğunu tek entegrasyonda sergileyebiliyor.
- Gerçek müşteri verisi olmadan mock connector ile tam sergilenebiliyor.

GitHub Deployment ikinci sırada doğru — "deploy sonrası hata" korelasyonu, ürünün en çarpıcı demo anı (Bölüm 4, J4). SES/SendGrid üçüncü, en düşük öncelikli: Open Decision #7'de zaten işaretlendiği gibi, bu bağlamda "core taksonomiye tam derinlikte mi yoksa light connector olarak mı" sorusu hâlâ açık — **eğer 14 günlük takvim baskı altına girerse, ilk kısılacak kapsam SES/SendGrid'in derinliği olmalı**, Stripe/GitHub değil.

---

## 8. Bugün atılması gereken ilk teknik adım

`docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md` **Bölüm 50**'de tam olarak tanımlanmış: M1 — Solution Skeleton (solution+4 katman projesi, Integration/ApiKey entity, EF Core+PostgreSQL migration, health check, Testcontainers ile ilk integration test, Docker Compose).

**Ancak görev talimatı açık:** *"Mimari doküman tamamlandıktan sonra kullanıcıdan açık onay almadan M1 kodlamasına geçme."* Bu doküman o onayı temsil etmez — sadece "ilk adım budur" sorusuna cevap verir. Kodlamaya başlamak için ayrı, açık bir onay gereklidir.

---

## 9. Bu sistem portföy projesi olarak güçlü mü?

**Evet, net biçimde.** Tek başına şu unsurları gösterebilir bir portföy artefaktı üretir:
- Modular monolith + Clean/Onion Architecture disiplini (katman ayrımı, dependency inversion, gerçek proje referanslarıyla)
- Deterministik/AI ayrımının mimari seviyede (tip sistemi, şema) zorlanması — "AI'yı doğru yerde kullanma" konusunda olgun bir mühendislik kararı, çoğu "AI wrapper" portföy projesinden ayrışır
- EF Core + PostgreSQL (JSONB, partition, RLS-hazır ama-henüz-eklenmemiş şema disiplini)
- Hangfire + outbox pattern ile asenkron iş garantisi
- Testcontainers ile gerçek entegrasyon testi
- Serilog + OpenTelemetry ile uçtan uca correlation
- Golden-dataset tabanlı AI evaluation (çoğu geliştiricinin atladığı bir adım)
- Disiplinli bir ADR/Open-Decision kültürü

Bu, "CRUD + ChatGPT API çağrısı" seviyesindeki tipik AI-portföy projelerinden belirgin şekilde daha derin. Portföy değeri, ürün ticari olarak başarısız olsa bile (Bölüm 44 Kill senaryosu) korunur — bu zaten dokümanın kendisinde açıkça tasarlanmış bir özellik.

## 10. SaaS'a dönüşebilmesi için hangi kanıtlar gerekir?

`FAILURE_INTELLIGENCE_ARCHITECTURE.md` §44 (Continue kriteri) ve `OPERATIONS_PLATFORM_ARCHITECTURE.md` §3.2 (Gate 1) zaten bunu somutlaştırmış; özet:

- ≥5/15 problem-interview'de "düzenli yaşanıyor" + ortalama öncelik ≥7/10
- ≥1 net (belirsiz olmayan) ödeme niyeti
- ≥2 pilotun "devam" kararı + ≥1 imzalı ücretli sözleşme veya yazılı fiyat onayı
- Haftada 3-4 gün aktif kullanım deseni (araç iş akışına gerçekten giriyor mu)
- Ayda 8-10 gerçek incident hacmi (istatistiksel olarak anlamlı bir doğrulama örneklemi)

Bu beşi birden (AND, OR değil) karşılanmadan V2 mühendisliğine — yani OP'un ilk fazına — başlanmamalı. Bu eşik kasıtlı olarak yüksek tutulmuş; gerekçesi hem FI hem OP dokümanında tutarlı şekilde işlenmiş ve ben de bu kısıtı aynen koruyorum.

---

## 11. Dosya Envanteri

```
docs/
├── analysis/
│   ├── failure-intelligence/        (6 dosya — ham uzman analizleri)
│   └── operations-platform/         (6 dosya — ham uzman analizleri)
├── FAILURE_INTELLIGENCE_ARCHITECTURE.md   (nihai, uygulanabilir mimari — 50 bölüm)
├── OPERATIONS_PLATFORM_ARCHITECTURE.md    (nihai, uzun-vadeli vizyon — 28 bölüm)
└── ARCHITECTURE_REVIEW.md                 (bu doküman)
```

---

## 12. Sonuç

FI mimarisi bugün geliştirilmeye hazır durumdadır; gereksiz karmaşıklık ayıklanmış, çelişkiler çözülmüş, açık kararlar işaretlenmiştir. OP, FI başarılı olursa hazır bekleyen, disiplinli bir yol haritasıdır — bugün için hiçbir mühendislik eylemi gerektirmez. Bir sonraki adım, kullanıcının M1 kodlamasına açık onay vermesidir.
