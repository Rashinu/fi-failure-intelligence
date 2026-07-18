# Incident & AI Intelligence — V2-V4 Derinleşme Tasarımı

**Doküman kapsamı:** AI Integration Operations Platform (OP), fazlar V2 (Incident Intelligence / Support Correlation), V3 (Runbook Engine, çoklu tenant AI maliyet kontrolü), V4 (Controlled Remediation).

**Kapsam dışı:** V1 — AI Integration Failure Intelligence (FI). FI'nin deterministik sınıflandırma, evidence-only prompt, confidence + human review tasarımı ayrı bir ekip tarafından yürütülüyor. Bu doküman FI'yi miras alınan bir temel olarak referans alır, yeniden tasarlamaz.

**Miras kural (platform genelinde bağlayıcı):**
1. AI hiçbir zaman incident oluşturmanın veya temel sınıflandırmanın tek sorumlusu değildir — deterministik kurallar her zaman önce çalışır, AI yalnızca zenginleştirir.
2. Otomatik remediation her zaman human approval gerektirir — bu kural V4'te mimari olarak zorlanır, politika olarak değil.
3. Hassas veri (secrets, credential, PII, tam request/response body) AI'a gönderilmeden maskelenir/redakte edilir.

---

## 1. V2 — Support Ticket Correlation'da AI'ın Rolü

### 1.1 Problem çerçevesi

Support Correlation'ın amacı: "Bu 14 destek talebi ile şu anki INC-482 teknik incident'ı aynı kök soruna mı işaret ediyor?" sorusuna güvenilir cevap üretmek. Burada iki farklı hata sınıfı var ve her biri farklı bir motor gerektirir:

- **Yanlış pozitif riski düşük, deterministik olarak çözülebilir eşleşmeler** (zaman penceresi + aynı entegrasyon/servis) → kural motoru.
- **Doğal dilde ifade edilmiş, farklı kelimelerle aynı sorunu anlatan talepler** ("ödeme geçmiyor" / "checkout'ta hata alıyorum" / "kartım işlenmiyor") → semantik benzerlik gerektirir, kural motoru ile yakalanamaz.

Kural: **deterministik sinyal her zaman AI sinyalinden önce ve daha yüksek ağırlıkla çalışır.** AI, deterministik olarak hiçbir zaman eşleşmeyecek talepleri keşfetmek için vardır — deterministik eşleşmenin yerini almaz, tamamlar.

### 1.2 Katman ayrımı

**Katman A — Deterministik korelasyon (birincil, açıklanabilir, AI'sız çalışabilir)**

Aşağıdaki sinyaller kural tabanlı, tamamen yorumlanabilir skorlarla hesaplanır:

| Sinyal | Kaynak | Ağırlık mantığı |
|---|---|---|
| Zaman penceresi çakışması | ticket.createdAt vs incident.detectedAt / incident.timeline | Ticket, incident'ın başlangıcından `-15dk` ile hâlâ açıkken arası bir pencerede açıldıysa güçlü sinyal |
| Etkilenen entegrasyon/servis eşleşmesi | ticket.metadata.affectedService (varsa) veya customer→integration mapping | Ticket'ın bağlı olduğu müşteri hesabı, incident'ın etkilediği entegrasyonu kullanıyorsa güçlü sinyal |
| Hacim anomalisi eşleşmesi | Aynı zaman diliminde açılan ticket sayısında ani artış | Baseline'ın N standart sapma üzerinde ticket açılışı, aktif incident ile aynı pencerede ise güçlü sinyal |
| Müşteri etki listesi çakışması | Incident'ın affected customer/tenant listesi (varsa, ör. rate-limit incident'ında hangi API key'ler etkilendi) | Ticket açan müşteri bu listede ise çok güçlü sinyal |

Bu katman **AI olmadan** bir "correlation candidate" seti üretir ve her adayın nedenini insan-okur formatta gösterir ("Bu ticket, incident penceresinde ve aynı entegrasyonu kullanan bir müşteriden geldiği için aday gösterildi"). Skoru belirli bir eşiğin üzerinde olan adaylar zaten yüksek güvenle önerilir; AI'a gitmeden UI'da gösterilebilir.

**Katman B — Semantik korelasyon (ikincil, AI/embedding destekli, yalnızca zenginleştirme)**

Katman A'nın kaçırdığı ama içerik olarak aynı soruna işaret eden ticket'ları bulmak için:

1. Ticket başlığı + gövdesi (PII/secret maskelendikten sonra) embedding modeline verilir, bir vektöre dönüştürülür.
2. Aktif/son N gün içindeki incident'ların "özet metni" (deterministik olarak üretilmiş: kategori + etkilenen entegrasyon + hata mesajı örüntüsü, FI'nin ürettiği kanıt özeti) de embedding'e çevrilir.
3. Cosine similarity ile ticket-incident çiftleri sıralanır.
4. Belirli bir benzerlik eşiğinin (ör. 0.80) üzerindeki çiftler "AI-suggested correlation" olarak işaretlenir — **asla otomatik olarak Katman A'nın ürettiği "confirmed correlation" statüsüne yükseltilmez.**

### 1.3 Karar birleştirme mantığı

```
correlation_status(ticket, incident) =
  DETERMINISTIC_MATCH   -> otomatik "linked" (yüksek güven, deterministik kanıt UI'da gösterilir)
  DETERMINISTIC_PARTIAL -> "suggested", insan onayı ile "linked"
  AI_SEMANTIC_ONLY       -> "suggested (AI)", her zaman insan onayı ile "linked"
  NO_MATCH                -> gösterilmez
```

AI hiçbir zaman tek başına bir ticket'ı incident'a "linked" statüsüne getiremez; yalnızca deterministik sinyal hiç yokken veya zayıfken bir "aday" üretir ve bu aday support/ops ekibinin gözden geçirme kuyruğuna düşer. Bu, madde 1'deki miras kuralın (AI tek sorumlu olamaz) doğrudan uygulamasıdır.

### 1.4 Evidence zorunluluğu

Her AI-suggested correlation, FI'nin evidence-only prensibiyle aynı disipline tabidir: öneri, ham similarity skorunu ve karşılaştırılan iki metin parçasının (maskelenmiş) kısa alıntısını UI'da göstermek zorundadır. "Bu ticket şuna benziyor çünkü..." serbest metin gerekçelendirmesi yasak; yalnızca skor + kaynak alıntı gösterilir. Böylece hallucinated gerekçe riski en baştan kapatılır.

---

## 2. V2 — Similar Historical Incident Özelliği

### 2.1 Amaç

Yeni açılan bir incident için "bu duruma daha önce rastladık mı, nasıl çözüldü" sorusuna hızlı, kaynak gösterir cevap. Bu özellik V3'teki Runbook Engine'in doğrudan öncüsüdür — V2'de sadece "benzer geçmiş incident'ları listele", V3'te "bu geçmişten öneri üret".

### 2.2 Mimari: pgvector yeterli, ayrı vector store gerekmiyor

**Karar: PostgreSQL + pgvector, ayrı bir vector database (Pinecone, Weaviate, Qdrant vb.) gerektirmez.**

Gerekçe:
- OP zaten işlemsel veriyi (incident, evidence, tenant, ticket) PostgreSQL'de tutuyor. Embedding'i ayrı bir sisteme taşımak, incident verisiyle vektör verisi arasında senkronizasyon sorunu, ek operasyonel yük ve ek maliyet demek.
- Platformun beklenen ölçeği (çoklu tenant, ama tenant başına yüzlerce-binlerce incident/yıl mertebesinde, milyonlarca değil) pgvector'ın HNSW/IVFFlat index performansı için rahatlıkla yeterli. Ayrı bir vector store'un asıl avantajı olan "milyar ölçekli, çok yüksek QPS'li ANN arama" burada gerekmiyor.
- Tek veritabanında tutmak, tenant izolasyonunu (row-level security / tenant_id filtreleme) embedding araması için de bedava sağlar — ayrı bir vector store'da bu izolasyonu ikinci kez inşa etmek gerekirdi.
- Migration/backup/disaster-recovery tek sistem üzerinden yürür.

**Ne zaman yeniden değerlendirilmeli:** Tenant başına milyonlarca doküman/incident birikirse, ya da alt-saniye çok yüksek eşzamanlı arama yükü (ör. gerçek zamanlı müşteri destek widget'ı embedding araması) oluşursa, o zaman pgvector'dan özel bir vector store'a geçiş değerlendirilir. V2-V3 ölçeğinde bu eşik aşılmaz; bu yüzden platform pgvector ile başlar ve "vector store'u ayırma" kararını ölçüm verisine (index boyutu, p95 arama gecikmesi) bağlı bir sonraki-faz kararı olarak bırakır, spekülatif olarak şimdiden inşa etmez.

### 2.3 Veri modeli (kavramsal, kod değil)

- `incident_embeddings` tablosu: incident_id, tenant_id, embedding_vector, embedding_source_summary (deterministik olarak üretilmiş, kanıta dayalı özet metni — serbest AI yorumu değil), model_version, created_at.
- Embedding, incident'ın *kanıta dayalı özetinden* üretilir (kategori, etkilenen entegrasyon, hata imzası/fingerprint, kısa evidence alıntıları) — incident'ın ham AI analiz metninden değil. Böylece bir incident'taki olası halüsinasyon, benzerlik aramasının temelini kirletmez.
- Arama sorgusu: yeni incident açıldığında aynı yöntemle bir sorgu embedding'i üretilir, `WHERE tenant_id = :tenant` filtresiyle (tenant izolasyonu asla atlanmaz) cosine distance sıralaması yapılır, en yakın K sonuç + skor döner.

### 2.4 Sunum ve güven sınırı

- "Similar historical incidents" listesi her zaman: similarity skoru, geçmiş incident'ın kategorisi/kök nedeni/çözüm notu, ve linke tıklanabilir kaynak gösterir.
- Bu özellik **hiçbir zaman** otomatik olarak "bu incident'ın kök nedeni de aynıdır" sonucuna varmaz — sadece insan analistin/AI root-cause motorunun girdisi olarak sunulur. Bu, FI'nin "AI tek sorumlu olamaz" kuralının V2 uzantısıdır.

---

## 3. V3 — Runbook Engine AI Mimarisi (RAG)

### 3.1 Amaç ve sınır

Runbook Engine, "bu incident için geçmişte ne yapıldı, ne işe yaradı" sorusuna kaynak-atıflı bir öneri üretir. **Asla bir aksiyonu kendisi çalıştırmaz** (bu V4'ün konusu) — yalnızca metin öneri üretir, insan uygular.

### 3.2 İndeksleme (retrieval tarafı)

Kaynak korpüsü üç türden oluşur, her biri ayrı ayrı indekslenir ve kaynak türü öneri çıktısında her zaman etiketlenir:

1. **Resolved incident kayıtları** — kök neden, uygulanan çözüm adımı, resolution note (insan tarafından yazılmış), sonuç (işe yaradı mı / tekrarladı mı).
2. **Postmortem dokümanları** — V2 Incident Workflow'un ürettiği yapılandırılmış postmortem'ler.
3. **Elle yazılmış runbook/prosedür dokümanları** — ops ekibinin önceden yazdığı statik prosedürler (ör. "Stripe webhook signature hatası → şu 4 adımı izle").

İndeksleme adımları (kavramsal):
- Her kaynak, chunk'lara bölünür (ör. bir resolved incident = 1 chunk: kök neden + çözüm adımı + sonuç; uzun postmortem = bölüm bazlı birkaç chunk).
- Her chunk, `runbook_source_id`, `tenant_id` (veya `is_global` — bazı runbook'lar platform-genelinde paylaşılabilir, tenant'a özel değil), `source_type`, `category` (FI'nin deterministik kategorileriyle hizalı: auth/timeout/rate-limit/schema-mismatch/...), embedding_vector ve `outcome_confidence` (bu çözüm gerçekten işe yaradı mı, yoksa incident tekrarladı mı — bu alan geri besleme ile güncellenir, bkz. 3.4) alanlarıyla pgvector'a yazılır.
- Hassas veri, indekslemeden önce maskelenir — chunk içine ham credential, PII veya tam request/response body asla girmez (aynı FI evidence maskeleme disiplini).

### 3.3 Öneri üretimi (generation tarafı)

1. Aktif incident için deterministik özet + kategori + etkilenen entegrasyon çıkarılır (FI'nin ürettiği evidence paketi).
2. Bu özetin embedding'i ile en yakın K runbook chunk'ı retrieve edilir (aynı kategori filtresiyle önce daraltılır, sonra semantik sıralama yapılır — burada da deterministik filtre önce, AI ikinci sırada).
3. LLM'e giden prompt **yalnızca** retrieve edilen chunk'ları ve aktif incident'ın maskelenmiş kanıt özetini içerir. Modelin kendi genel bilgisinden runbook adımı "hayal etmesi" prompt seviyesinde açıkça yasaklanır: sistem talimatı, "yalnızca sağlanan kaynak metinlerden alıntı/sentezle öneri üret; kaynaklarda karşılığı olmayan hiçbir adım ekleme, emin değilsen 'yeterli kaynak yok' de" şeklinde sıkı bir kısıtlama içerir.
4. Çıktı şeması zorunlu olarak her önerilen adımı en az bir `source_chunk_id`'ye bağlar. Kaynak atfı olmayan adım, post-processing katmanında otomatik olarak reddedilir/filtrelenir — modelin "kaynaksız ama makul görünen" bir adım eklemesi çıktıya sızamaz.

### 3.4 Hallucination kontrolü — çok katmanlı

FI'nin "evidence-only prompt + confidence + human review" ilkesinin RAG bağlamındaki genişletilmiş hali:

| Kontrol | Nasıl |
|---|---|
| Kaynak zorunluluğu | Şema seviyesinde: her öneri adımı `source_chunk_id[]` taşımak zorunda; boş/eksikse adım çıktıdan düşürülür (kod tarafında validasyon, modele güvenilmez). |
| Retrieval tabanlı sınırlama | Prompt'a hiçbir zaman "genel bilgiye dayanarak öner" izni verilmez; yalnızca retrieve edilen chunk'lar bağlam penceresine girer. |
| Düşük-güven reddi | Retrieve edilen en iyi eşleşmenin similarity skoru eşiğin altındaysa (ör. <0.65), sistem öneri üretmez, "yeterli geçmiş kayıt yok" der — zorla bir cevap üretmeye çalışmaz. |
| Outcome geri beslemesi | Bir runbook önerisi uygulandıktan sonra insan "işe yaradı / yaramadı / kısmen" olarak işaretler; bu, kaynak chunk'ın `outcome_confidence` alanını günceller. Düşük başarı oranlı chunk'lar retrieval sıralamasında geriye düşer veya eşik altına inerse artık önerilmez. |
| Human review zorunluluğu | Runbook önerisi hiçbir zaman "otomatik uygulanabilir adım" olarak işaretlenmez (V4 ile ayrım — bkz. Bölüm 5); her zaman bir insanın okuyup uygulaması gereken metin olarak sunulur. |
| Prompt/model versiyon izlenebilirliği | Her öneri, hangi prompt versiyonu ve hangi model ile üretildiği bilgisini taşır (bkz. Bölüm 7) — bir hallucination raporu geldiğinde hangi prompt/model'in sorumlu olduğu geriye izlenebilir. |

---

## 4. V3 — Çoklu Incident/Tenant Ölçeğinde AI Maliyet Kontrolü (genişletilmiş)

### 4.1 Prensip: her analiz türü kendi taze-lik (freshness) gereksinimine göre zamanlanır

V1'de FI, tek incident bazında senkron/near-real-time AI analizi yapıyordu. V3'te tenant ve incident hacmi arttıkça "her şeyi gerçek zamanlı AI'a gönder" maliyeti doğrusal değil süper-doğrusal büyür (ticket correlation × incident sayısı × runbook retrieval). Bu yüzden analiz türleri üçe ayrılır:

| Analiz türü | Zamanlama | Gerekçe |
|---|---|---|
| Root-cause analizi (FI, V1 mirası) | Gerçek zamanlı, incident açılışında/reanalyze tetiklendiğinde | Kullanıcı incident'ın "ne" olduğunu hemen bilmek zorunda; iş kritik. |
| Support ticket ↔ incident deterministik korelasyon (Katman A) | Gerçek zamanlı | Zaten AI değil, ucuz; gecikme eklemenin anlamı yok. |
| Support ticket ↔ incident semantik korelasyon (Katman B, AI/embedding) | Batch, kısa aralıklı (ör. 2-5 dk'da bir toplu iş) | Embedding üretimi ucuz ama toplu yapılırsa (batching) API çağrı sayısı ve token overhead'i azalır; birkaç dakikalık gecikme kabul edilebilir çünkü bu "aday öneri", zaten insan onayı bekliyor. |
| Similar historical incident arama (V2) | Gerçek zamanlı ama önbelleklenmiş | Sorgu embedding'i incident açılışında bir kez üretilir ve incident kaydına yazılır (yeniden hesaplanmaz); pgvector araması ucuzdur, tekrar tekrar embedding üretmek pahalı olan kısımdır. |
| Runbook önerisi üretimi (V3) | Yarı-gerçek zamanlı: retrieval anlık, LLM generation isteğe bağlı tetiklenir ("öneri getir" butonu) | Her incident için otomatik olarak LLM generation çağrısı yapmak yerine, kullanıcı/analist explicit olarak istediğinde üretilir — bu tek başına en büyük maliyet kalemini talep bazlı hale getirir. |
| Batch trend/pattern analizi (tenant bazlı haftalık özet, tekrarlayan incident kümeleri) | Tamamen batch, günlük/haftalık zamanlanmış iş | Zaman kritikliği yok; toplu işlenerek hem token hem çağrı sayısı optimize edilir. |

### 4.2 Maliyet kontrol mekanizmaları

- **Embedding cache:** Aynı içerik (ör. aynı hata imzası tekrar eden bir incident açıldığında) için embedding yeniden üretilmez; içerik hash'i (fingerprint) üzerinden cache kontrolü yapılır.
- **Kategori-öncelikli daraltma:** LLM'e gitmeden önce deterministik kategori/entegrasyon filtresi ile aday seti daraltılır (embedding araması N=1000 yerine N=20 üzerinde çalışır) — bu hem doğruluğu hem maliyeti iyileştirir.
- **Prompt/response caching (LLM sağlayıcı seviyesinde):** Runbook chunk'ları ve sistem talimatı gibi sık tekrar eden statik prompt bölümleri, sağlayıcının prompt caching özelliğiyle (destekleniyorsa) önbelleklenir; yalnızca incident'a özgü kısım her seferinde değişir.
- **Tenant/plan bazlı hız sınırlama:** Düşük plana sahip tenant'lar için gerçek zamanlı runbook generation çağrı sayısına günlük üst sınır konur; sınır aşıldığında istek batch kuyruğuna düşer (kullanıcıya "birkaç dakika içinde hazır olacak" bildirimi).
- **Model tiering:** Ucuz/hızlı model embedding ve basit sınıflandırma-yardımcı görevlerde, daha güçlü/pahalı model yalnızca son adım generation'da (runbook sentezi, root-cause sentezi) kullanılır — her görev için tek bir "en pahalı model her yerde" yaklaşımı reddedilir.
- **Batch API kullanımı:** Gerçek zamanlı olması gerekmeyen tüm işler (haftalık trend özeti, düşük öncelikli semantik korelasyon) sağlayıcının batch/async API'si üzerinden çalıştırılır (genelde belirgin fiyat avantajı sağlar).

---

## 5. V4 — Controlled Remediation'da AI'ın Rolünün Mimari Sınırlanması

### 5.1 Temel ilke

V4'te AI'ın rolü kesin ve tek yönlüdür: **AI yalnızca "önerilen aksiyon" (recommended action) üretir. AI hiçbir zaman bir aksiyonu tetikleyemez, tetikleme yetkisi hiçbir AI çıktısına bağlı değildir.** Bu, güvenlik politikası değil, mimari zorunluluktur — yani "AI'a talimat veririz, uymasını umarız" değil, "AI'ın teknik olarak tetikleme yeteneği hiç yoktur" tasarımıdır.

### 5.2 Şema seviyesinde garanti

AI'ın ürettiği çıktı şeması (Remediation Suggestion) ile gerçek execution'ı tetikleyen şema (Remediation Action) **kasıtlı olarak farklı, birbirine dönüştürülemeyen iki nesne** olarak tasarlanır:

**Remediation Suggestion (AI çıktısı, salt-okunur, executable değil):**
- `suggestion_id`
- `incident_id`
- `suggested_step_description` (insan-okur metin)
- `source_chunk_ids[]` (runbook kaynak atfı, Bölüm 3.3 ile aynı disiplin)
- `risk_category` (AI'ın kendi öz-değerlendirmesi: "low/medium/high risk" — bilgilendirici, yetkilendirici değil)
- `model_version`, `prompt_version`
- **Bu şemada `executable`, `action_type`, `target_resource`, `execute_endpoint` gibi çalıştırılabilirlik ifade eden HİÇBİR alan yoktur.** AI çıktısı yapısal olarak "ne yapılabileceğini" makineye anlatacak bir formatta değildir — yalnızca insanın okuyup yorumlayacağı bir açıklamadır.

**Remediation Action (insan tarafından oluşturulan, ayrı API, ayrı yetkilendirme):**
- `action_id`
- `incident_id`
- `action_type` (önceden tanımlı, sabit bir enum: ör. `restart_webhook_listener`, `rotate_api_key`, `clear_stale_cache_entry` — yeni bir action_type eklemek kod değişikliği ve deploy gerektirir, AI çalışma zamanında yeni bir action_type "icat edemez")
- `linked_suggestion_id` (opsiyonel, izlenebilirlik için — "bu aksiyon şu AI önerisinden esinlendi" bağı, ama bu alan yalnızca audit/analitik amaçlıdır, yetkilendirme mantığına girmez)
- `approved_by` (insan kullanıcı kimliği — zorunlu, null olamaz)
- `approved_at`
- `executed_by_system`, `executed_at`, `execution_result`

Remediation Action, **yalnızca** önceden kayıtlı, sabit bir action_type kataloğundan seçilerek, insan onaylı ayrı bir API (`POST /api/v1/incidents/{id}/remediation-actions`) üzerinden oluşturulur. Bu API'nin request body'sinde AI çıktısı doğrudan kabul edilmez — yalnızca `action_type` (kataloğa dahil olmalı) ve `approved_by` zorunlu alanlarıdır. AI'ın ürettiği `suggestion_id` en fazla referans/bağlam olarak eklenebilir, asla `action_type`'ı belirleyen taraf olamaz.

### 5.3 Prompt seviyesinde garanti (savunmada ikinci katman)

Şema garantisi birincil kontrol olsa da, prompt seviyesinde de ek bir savunma katmanı bulunur:
- Sistem talimatı, modele "çalıştırılabilir komut, script, API çağrısı, veya `execute` niyeti taşıyan hiçbir çıktı üretme; yalnızca insanın uygulayacağı açıklayıcı adım metni üret" der.
- Çıktı post-processing'inde, serbest metin alanlarında komut-benzeri örüntüler (ör. shell komutu, HTTP metodu + endpoint, SQL) tespit edilirse, bu çıktı otomatik olarak flag'lenir ve insan incelemesine düşer, doğrudan UI'da "öneri" olarak gösterilmez. (Bu bir ek güvenlik katmanıdır; birincil garanti yine 5.2'deki şema ayrımıdır — prompt talimatına asla tek başına güvenilmez.)

### 5.4 Audit zorunluluğu

Her Remediation Action kaydı zorunlu olarak şunları taşır: kim onayladı (approved_by), ne zaman, hangi AI önerisine (varsa) referans verildi, kim/hangi sistem bileşeni execute etti, sonucu neydi. Bu audit izi immutable (append-only) tutulur ve incident timeline'ında görünür — V2 Incident Workflow'un audit altyapısıyla aynı mekanizma kullanılır, V4 için özel bir audit sistemi icat edilmez.

### 5.5 Neden bu yeterli (ve "politika" değil "mimari" olması neden önemli)

Eğer kontrol yalnızca "prompt'a yazık AI'a çalıştırma deme" seviyesinde kalsaydı, bir prompt injection, model güncellemesi, ya da edge-case girdi bu kısıtı atlatabilirdi. Şema seviyesinde AI çıktısının hiçbir alanı execution API'sinin beklediği alanlarla örtüşmediği için, AI çıktısını execution'a bağlayan hiçbir kod yolu yoktur — geliştirici kasıtlı olarak yeni bir entegrasyon kodu yazmadıkça AI çıktısı hiçbir şeyi tetikleyemez. Bu, "AI uysun umuyoruz" yerine "AI'ın fiziksel olarak yapamayacağı" bir tasarımdır.

---

## 6. Platform Ölçeğinde AI Evaluation Stratejisi (genişletilmiş)

### 6.1 Senaryo tipi başına ayrı golden dataset + rubric

FI'nin V1'de kurduğu (varsayılan) root-cause evaluation disiplini, OP'ta üç ayrı AI görevine ayrı ayrı genişletilir. Her görev türü farklı hata modlarına sahip olduğu için tek bir ortak rubric yeterli değildir:

**A. Root-cause analizi (V1 miras, platform genelinde sürdürülür)**
- Golden dataset: gerçek/sentetik incident + evidence paketleri, doğru kategori + doğru kök neden + doğru confidence bandı etiketli.
- Rubric: kategori doğruluğu (deterministik ölçülebilir), kanıt atfı doğruluğu (üretilen açıklamadaki her iddia gerçekten sağlanan evidence'ta var mı — evidence-grounding skoru), gereksiz/uydurma iddia sayısı (0 olmalı), confidence kalibrasyonu (yüksek confidence dediği durumların gerçekten daha yüksek doğruluk oranına sahip olup olmadığı).

**B. Ticket-correlation (V2, yeni)**
- Golden dataset: geçmişte insan tarafından doğrulanmış "bu ticket gerçekten bu incident'a aitti / değildi" etiketli ticket-incident çiftleri (hem pozitif hem negatif örnekler — negatifler kritik, çünkü asıl risk yanlış-pozitif korelasyon).
- Rubric: precision (özellikle yüksek — yanlış korelasyon, ops ekibinin yanlış yöne gitmesine sebep olur), recall, semantik katmanın deterministik katmana göre marjinal katkısı (AI kapatılsa kaç doğru eşleşme kaybedilirdi — bu, AI'ın gerçek katma değerini ölçer).

**C. Runbook-suggestion (V3, yeni)**
- Golden dataset: geçmiş incident + "ideal runbook önerisi" (insan uzman tarafından yazılmış referans) çiftleri, farklı kategori/entegrasyon kombinasyonlarını kapsayacak şekilde.
- Rubric: kaynak-atıf doğruluğu (her önerilen adımın gerçekten referans aldığı chunk'ta karşılığı var mı — otomatik + insan örneklem denetimi), halüsinasyon oranı (kaynakta olmayan adım üretme sıklığı — hedef: sıfıra yakın, herhangi bir tespit kırmızı bayrak), kapsam yeterliliği (uygun kaynak yokken doğru şekilde "yeterli veri yok" deme oranı — "aşırı özgüvenli halüsinasyon" yerine "dürüst boşluk" tercih edilir), pratik kullanılabilirlik (insan değerlendiricinin "bu öneriyi olduğu gibi uygulardım" oranı).

### 6.2 Ortak altyapı, ayrı içerik

Üç senaryo da aynı evaluation harness'ı paylaşır (aynı çalıştırma pipeline'ı, aynı raporlama formatı, aynı regresyon-alarm mekanizması), ama golden dataset'leri ve rubric ağırlıkları görev türüne özgüdür. Yeni bir AI görevi platforma eklendiğinde (ör. V4 sonrası bir "impact scoring" görevi), önce bu görev için ayrı bir golden dataset + rubric tanımlanır — var olan rubric'lerden biri "yaklaşık olarak yeterli" diye ödünç alınmaz.

### 6.3 Regresyon kapısı

Her prompt/model versiyon değişikliği, ilgili golden dataset üzerinde otomatik çalıştırılır; skor önceki versiyona göre kabul edilen eşiğin altına düşerse (özellikle halüsinasyon oranı ve yanlış-pozitif korelasyon oranı gibi "kırmızı çizgi" metriklerde), yeni versiyon production'a çıkamaz. Bu, V1'in "confidence + human review" güven modelinin platform ölçeğinde sürdürülebilir kalmasının ön koşuludur — insan gözden geçirme kapasitesi sabitken AI görev sayısı arttıkça, her görevin kendi kalite kapısından geçmiş olması zorunludur.

---

## 7. Prompt Versiyonlama ve Model Yönetimi (platform ölçeği)

### 7.1 Neden tek bir "AI ayarı" yetmez

V1'de tek bir görev (root-cause) için tek bir prompt/model çifti yönetmek yeterliydi. V2-V4'te en az dört farklı AI görevi var (root-cause, ticket-correlation embedding, runbook-suggestion, gelecekte impact-scoring gibi ek görevler) ve her biri farklı hızda evrilir, farklı model gereksinimlerine sahip olabilir (embedding modeli vs. generation modeli), farklı ekiplerce iyileştirilebilir. Bu yüzden platform, görev bazlı bir **prompt/model registry** ile yönetilir.

### 7.2 Registry yapısı (kavramsal)

Her AI görevi için ayrı bir kayıt:

| Alan | Açıklama |
|---|---|
| `task_key` | Sabit tanımlayıcı: `root_cause_analysis`, `ticket_correlation_embedding`, `runbook_suggestion`, vb. |
| `prompt_version` | Semantik sürüm (ör. `v3.2.0`), her değişiklikte artar, önceki sürümler asla silinmez (geriye dönük izlenebilirlik + rollback için). |
| `prompt_template` | Versiyon başına immutable şablon metni. |
| `model_id` + `model_provider_version` | Hangi model, hangi sağlayıcı sürümü kullanılıyor (ör. belirli bir model snapshot'ı — "en güncel model" gibi kaymalı bir referans kullanılmaz, sabitlenir). |
| `eval_baseline_score` | Bölüm 6'daki golden dataset üzerinde bu versiyonun aldığı skor. |
| `status` | `shadow` (canlıya etki etmeden paralel çalıştırılıyor) / `canary` (küçük trafik payı) / `active` / `deprecated`. |
| `rollback_target` | Bir sorun tespit edilirse otomatik/manuel geri dönülecek önceki stabil versiyon. |

### 7.3 Yaşam döngüsü

1. Yeni prompt/model adayı → golden dataset üzerinde offline eval (Bölüm 6) → eşik geçilirse `shadow` moduna alınır (gerçek trafik üzerinde, gerçek kullanıcıya gösterilmeden, sonuçlar sadece loglanıp karşılaştırılır).
2. Shadow sonuçları da eşiği geçerse `canary` (ör. tenant'ların %5'i) moduna alınır.
3. Canary'de belirlenen süre boyunca (kırmızı çizgi metriklerde) regresyon yoksa `active` olur, önceki versiyon `deprecated` işaretlenir ama silinmez.
4. Her incident/ticket/runbook önerisi kaydı, üretildiği anki `task_key + prompt_version + model_id` üçlüsünü saklar — bu, hem evaluation hem "bu hatalı öneri hangi versiyondan geldi" sorgusu için zorunludur (Bölüm 3.4'teki izlenebilirlik gereksinimiyle doğrudan bağlantılı).

### 7.4 Görevler arası izolasyon

Bir görevin (ör. runbook-suggestion) prompt'unu değiştirmek, başka bir görevin (ör. root-cause analysis) davranışını hiçbir şekilde etkilemez — her `task_key` bağımsız olarak versiyonlanır, deploy edilir, rollback edilir. Bu, platformun büyümesiyle ortaya çıkacak "bir görevi iyileştirirken başka bir görevi bozma" riskini yapısal olarak ortadan kaldırır.

---

## 8. AI Güven / İnsan-Döngüsü Politikasının Olgunluk Modeli (V1 → V4)

### 8.1 Model özeti

| Faz | Güven seviyesi | İnsan-döngüsü modeli | Otomatik execution |
|---|---|---|---|
| **V1 — Failure Intelligence** | Başlangıç güveni: yok/düşük | **Her incident'ta** insan review mümkün ve varsayılan beklenti; AI yalnızca analiz üretir, hiçbir aksiyon önermez/çalıştırmaz. Confidence düşükse UI'da açıkça "düşük güven, doğrulama gerekli" işaretlenir. | Yok. |
| **V2 — Incident Intelligence** | Sınıflandırılmış güven: deterministik sinyal her zaman AI sinyalinden ayrıştırılır. | AI-suggested correlation'lar (ticket-incident) her zaman bir "öneri kuyruğu"na düşer; insan "linked" statüsüne yükseltmeden bu bağ resmi sayılmaz. Similar-incident önerileri salt bilgi amaçlı, karar insanda. | Yok. |
| **V3 — Operations Platform (Runbook Engine)** | Kaynak-atıflı güven: her öneri, hangi geçmiş kayda dayandığını gösterir; outcome-feedback ile kaynakların güvenilirliği zamanla ölçülür (bkz. 3.4). | Runbook önerisi her zaman insan tarafından okunur, yorumlanır ve **elle** uygulanır. AI'ın önerisi ile insanın gerçekte ne yaptığı arasında bilinçli bir ayrım korunur — insan öneriyi aynen, kısmen veya hiç uygulamayabilir; bu tercih audit'e yazılır. | Yok — Runbook Engine hiçbir eylemi tetiklemez, yalnızca metin üretir. |
| **V4 — Controlled Remediation** | Kanıtlanmış güven: yalnızca (a) sabit, önceden tanımlı action_type kataloğundaki, (b) geçmişte defalarca insan onaylı olarak başarıyla uygulanmış, (c) düşük risk kategorisinde sınıflandırılmış aksiyon türleri için "hızlandırılmış onay" (tek tıkla approve, önceden dolu action formu) sunulabilir. **Execution'ın kendisi hiçbir zaman AI kararıyla otomatik tetiklenmez** — hızlanan şey onay sürtünmesi, onay zorunluluğunun kendisi değil. | Her Remediation Action, approved_by alanı dolu olmadan var olamaz (Bölüm 5.2). "Otomatik execution" ifadesi platformda yalnızca "insan onayından sonra sistemin mekanik olarak çalıştırması" anlamına gelir — "insan onayı olmadan tetiklenme" anlamına asla gelmez. | **Yalnızca insan onaylı**, hiçbir zaman AI-tetikli. |

### 8.2 "Ne zaman, hangi kanıt eşiğiyle" — V4 hızlandırılmış onay kriterleri

V4'te "otomatik execution'a AI kararıyla izin ver" seçeneği platform tasarımında **hiçbir zaman** açılmaz. Ancak insan onay sürtünmesini azaltmak (tam manuel form doldurma yerine tek-tık onay) için bir aksiyon türünün bu hızlandırılmış akışa girebilmesi şu kanıt eşiklerinin **hepsinin** sağlanmasını gerektirir:

1. **Tekrar sayısı eşiği:** Aynı `action_type`, aynı `incident kategorisi` kombinasyonu, en az N (ör. 20) kez insan tarafından manuel onaylanıp uygulanmış olmalı.
2. **Başarı oranı eşiği:** Bu kombinasyonun geçmiş uygulamalarında ölçülen başarı oranı (incident'ın gerçekten kapanması, tekrarlamaması) belirlenen eşiğin (ör. %95) üzerinde olmalı.
3. **Geri alınabilirlik:** Aksiyonun etkisi geri alınabilir/yan etkisiz olmalı (ör. "cache temizle", "webhook listener'ı restart et" — "kredi kartı iadesi yap" gibi geri alınamaz mali etkili aksiyonlar bu kategoriye asla giremez, risk_category'den bağımsız olarak sabit bir kısıtlı-liste ile engellenir).
4. **İnsan gözden geçirme periyodu:** Bir action_type hızlandırılmış onay listesine alındıktan sonra dahi, periyodik olarak (ör. her çeyrek) yeniden gözden geçirilir; başarı oranı düşerse liste dışına çıkarılır.
5. **Audit ve geri çekilebilirlik:** Hızlandırılmış onay akışı bile her zaman bir `approved_by` (tek tık de olsa bir insan kimliği, bilinçli tıklama) ve tam audit izi üretir; "insan hiç görmeden" bir yol platformda yoktur.

Bu kriterlerin hepsi karşılansa dahi sonuç şudur: **onay süreci hızlanır, onay zorunluluğu ortadan kalkmaz.** Platformun hiçbir fazında AI'ın kendi kararıyla bir remediation action'ı execute etmesine izin veren bir kod yolu inşa edilmez — bu, madde 8.1 tablosundaki "Execution: yalnızca insan onaylı" satırının V4'ün sonuna kadar (ve ötesinde) değişmeyen sabiti olduğu anlamına gelir.

### 8.3 Olgunluk modelinin özeti (tek cümle)

V1'den V4'e ilerledikçe değişen şey **AI'ın ne kadar yetkiye sahip olduğu değil, insanın ne kadar hızlı ve ne kadar iyi bilgilendirilmiş karar verebildiğidir** — güven arttıkça AI'a devredilen şey "karar" değil, "kararı destekleyen kanıtın kalitesi ve onay sürecinin sürtünmesi" olur.
