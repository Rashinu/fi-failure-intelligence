# AI Integration Failure Intelligence (FI) — Delivery, Testing & Go-To-Market Planı

**Rol:** Delivery, Testing & Go-To-Market Planner
**Kapsam:** 14 günlük geliştirme takvimi doğrulaması, milestone/DoD, test piramidi, demo senaryosu, CI/CD, deployment, repo/README planı, ADR listesi, market validasyon operasyonu (interview, pilot, cold outreach, LinkedIn takvimi), karar kriterleri.
**Not:** Bu doküman sadece plandır — kod içermez.

---

## 0. Yönetici Özeti

FI projesi iki paralel hedefe hizmet ediyor: (a) güçlü bir portföy/MVP artefaktı, (b) gerçek bir B2B problemine dair pazar sinyali. Bu ikilik plana iki kısıt getiriyor:

1. **Geliştirme, izlenebilir ve gösterilebilir olmalı** — her gün sonunda demo edilebilir bir artefakt üretilmeli (build kırık kalmamalı, her gün git'e commit + LinkedIn'de iz bırakılabilir bir "günlük" olmalı).
2. **Test, gün 13'e sıkıştırılamaz** — 13 günlük bir "önce kodla, sonra test et" yaklaşımı hem teknik risk (geç bulunan tasarım hataları, özellikle fingerprinting/classifier gibi çekirdek algoritmalarda) hem de anlatı riski taşır (portföyde "test coverage %0, gün 13'te toplu eklendi" görünmesi, deneyimli bir işe alım yöneticisi ya da CTO'ya kötü sinyal verir). Bu yüzden plan, testi **her milestone'a yayarak** revize edilmiştir; gün 13 "test tamamlama ve sertleştirme" günü olarak kalır, "testin ilk yazıldığı gün" değil.

Aşağıdaki bölüm 1'de taslak 14 günlük plan gün gün değerlendirilip revize edilmiş, ardından bölüm 2'den itibaren milestone yapısına (M1–M6) geçilmiştir.

---

## 1. 14 Günlük Planın Doğrulanması ve Revizyonu

### 1.1 Taslak planın güçlü yanları
- Gün sırası mimari olarak mantıklı: önce iskelet (Gün1-2), sonra ingestion (Gün3-4), sonra domain zekası (Gün5-7), sonra AI katmanı (Gün8-10), sonra operasyonel olgunluk (Gün11-12), sonra sertleştirme+lansman (Gün13-14). Bu sıralama "her gün önceki günün üzerine inşa eder" ilkesine uyuyor — iyi.
- Modular monolith + PostgreSQL + Docker Compose ile Gün1'de başlamak, altyapı sürprizlerini erken çözüyor — doğru karar.

### 1.2 Tespit edilen riskler ve revizyonlar

**Risk 1 — Test her yere yayılmalı, tek güne değil.**
Taslakta test sadece Gün13'te var. Revizyon: her milestone'ın DoD'sine "o günün çekirdek biriminin testi yazılmış ve yeşil" şartı eklendi (bkz. Bölüm 3). Gün13 artık "yeni test yazma" günü değil, **"test piramidini tamamlama + e2e zincir testi + Testcontainers entegrasyon paketi + coverage sertleştirme"** günü. Classifier (Gün5) ve fingerprinting (Gün6) gibi saf-mantık, yüksek-risk kod, yazıldığı gün unit test ile kilitlenmeli — bunlar portföyün "bu kişi doğru düşünüyor mu" sorusuna cevap veren kod parçaları.

**Risk 2 — Gün3-4 çok yüklü (auth + ingestion endpoint + validation + raw event store + correlation id middleware tek günde/iki günde).**
Revizyon: Gün3 kapsamı "API key auth + ingestion endpoint iskeleti (persist yok, sadece kabul+200)" olarak daraltıldı; Gün4 "validation + raw event store + correlation id middleware + ingestion'ın gerçek persistansı" olarak genişletildi. Böylece Gün3 sonunda "endpoint var ama event kayboluyor" değil, Gün4 sonunda "event güvenilir şekilde saklanıyor" garantisi net bir kilometre taşı oluyor.

**Risk 3 — Gün8 "Hangfire + AI analysis job" tek günde iki büyük parça.**
Revizyon: Gün8 sadece Hangfire kurulumu + iş kuyruğu + sahte (stub) job (gerçek AI çağrısı yok, "incident geldi → job tetiklendi → log" zinciri kanıtlanıyor). Gerçek AI entegrasyonu (prompt + structured output) Gün9'a kaydırıldı — taslakta zaten Gün9 buydu, sadece Gün8'in beklentisi netleştirildi ki iki gün birbirine karışmasın.

**Risk 4 — Gün11 çok geniş (Serilog + Seq + health checks + OpenTelemetry aynı günde).**
Bunlar birbiriyle örtüşen "observability" işleri olduğu ve çoğu boilerplate/config işi olduğu için (yeni iş mantığı değil) tek günde kalması makul, ancak DoD'de "en az bir gerçek hata senaryosunda uçtan uca trace görülebiliyor" şartı eklendi — yoksa observability "kuruldu ama doğrulanmadı" riskiyle biter.

**Risk 5 — Gün12 "mock Stripe ve GitHub connector" ile Gün13 "demo seed data" arasında bağımlılık var ama sırayla değil.**
Mock connector'lar demo seed data'nın kaynağı. Revizyon: Gün12 sonunda mock connector'lardan üretilen event'lerin ingestion pipeline'ından geçtiği doğrulanmalı (bu zaten Gün13'ün "seed data" işini kolaylaştırır, tekrar iş üretmez).

**Risk 6 — Gün14 tek günde "deploy + README + GIF/video + LinkedIn" — gerçekçi değil.**
Bu dört iş farklı beceri ve bekleme süreleri gerektiriyor (deploy'da DNS/SSL bekleme olabilir, video kurgusu zaman alır). Revizyon: Deploy hazırlığı (Docker image, env config, health check doğrulama) Gün13'ün DoD'sine taşındı; Gün14 sadece "canlıya alma + doğrulama + README finalize + demo materyali + duyuru" olarak kalıyor ama deploy **riski** önceden alınmış oluyor. Ayrıca gerçekçi olmak adına Gün14, "14. takvim günü" değil "14. çalışma bloğu" olarak okunmalı; video/GIF prodüksiyonu isterse yarım gün taşabilir — bu esneklik plana not düşülüyor.

**Risk 7 — Market validasyon işleri (interview, cold mail) 14 günlük teknik plana hiç değinilmemiş, ayrı bir taslak olarak duruyor.**
Bu en önemli revizyon: **Bölüm 11-13**'te teknik takvimle senkronize edilmiş bir paralel iş akışı tanımlandı. Görüşmeler Gün1'den itibaren paralel yürümeli — "önce ürünü bitir sonra sat" değil, "ürünü yaparken paralel doğrula" modeli, çünkü kill/pivot kararının geç gelmesi (Gün14 sonrası) 2 haftalık emeği riske atar.

### 1.3 Revize 14 Günlük Plan (özet tablo)

| Gün | Teknik Kapsam | O Günün Test Şartı | Paralel GTM İşi |
|---|---|---|---|
| 1 | Repo, .slnx solution, modular monolith klasörleri, PostgreSQL, Docker Compose ayağa kalkıyor | Smoke: `docker compose up` + boş API health check yeşil | Hedef kitle listesi çıkarma (LinkedIn/Reddit arama), interview sorularının taslağı |
| 2 | Integration entity, DbContext, migration, CRUD | Integration CRUD için ilk integration test (Testcontainers PostgreSQL) | İlk 10 kişiye problem interview daveti gönderimi |
| 3 | API key auth, ingestion endpoint iskeleti (kabul+200, persist yok) | Auth middleware unit test (geçerli/geçersiz key) | Interview'lara devam, LinkedIn Paylaşım #1 hazırlığı |
| 4 | Validation, raw event store persist, correlation id middleware | Validation unit test + ingestion→DB integration test | LinkedIn Paylaşım #1 yayında ("neden bu problemi çözüyorum") |
| 5 | Rule-based error classifier | Classifier unit test (yüksek kapsam, tablo-güdümlü test — her hata kategorisi için örnek) | İlk gerçekleşen görüşmeler (3-5) |
| 6 | Fingerprinting algoritması, incident entity | Fingerprint unit test (aynı hata → aynı fingerprint, farklı hata → farklı fingerprint, edge case'ler) | Görüşmelere devam, notlar sentezi |
| 7 | Incident list/detail endpointleri | Contract test (response şeması) + integration test | Ara sentez: 5-7 görüşme sonrası ilk gidiş/kalış sinyali kontrolü |
| 8 | Hangfire kurulumu, stub AI job (gerçek AI çağrısı yok) | Job tetikleme integration test (incident→job kuyruğa girdi mi) | LinkedIn Paylaşım #2 hazırlığı ("mimari kararlar") |
| 9 | Structured output şeması, gerçek AI prompt entegrasyonu | AI response parsing unit test (şema doğrulama, hatalı JSON senaryosu) | LinkedIn Paylaşım #2 yayında |
| 10 | Evidence collector, deployment correlation | Evidence collector unit test + correlation integration test | Demo videosu için senaryo taslağı hazırlığı |
| 11 | Serilog, Seq, health checks, OpenTelemetry | En az 1 uçtan uca trace doğrulaması (manuel/otomatik) | Pilot adayı belirleme (görüşmelerden en sıcak 3-5 kişi) |
| 12 | Mock Stripe ve GitHub connector | Mock connector→ingestion e2e doğrulama | Pilot daveti maili gönderimi |
| 13 | Test piramidi tamamlama (e2e zincir: event→incident→AI analiz), demo seed data, deploy hazırlığı (image, env, health check) | Tüm piramit yeşil, coverage raporu üretildi, CI'da görünür | LinkedIn Paylaşım #3 hazırlığı, demo video kurgusu |
| 14 | Canlıya deploy, doğrulama, README finalize | Canlı ortamda smoke test (health check + 1 ingestion + 1 incident görüntüleme) | LinkedIn Paylaşım #3 + demo video yayında, GitHub repo public |

---

## 2. Milestone Yapısı (M1–M6)

Milestone'lar günlerin üstünde bir gruplama katmanı — "bu noktada elimde ne var, gösterilebilir mi" sorusuna cevap verir. Her milestone bir demo checkpoint'idir.

### M1 — Solution Skeleton (Gün 1-2)
**Kapsam:** Repo, çözüm yapısı, modular monolith klasörleri, PostgreSQL, Docker Compose, Integration entity + CRUD.

**Acceptance Criteria:**
- AC1: `docker compose up` tek komutla API + PostgreSQL ayağa kalkar, `/health` endpoint'i 200 döner (< 30 saniye içinde).
- AC2: Modular monolith klasör yapısı en az şu modülleri ayırır: Integrations, Ingestion, Incidents, Shared/Kernel — her modülün kendi klasörü ve namespace'i var, çapraz referanslar sadece tanımlı arayüzler (interface) üzerinden.
- AC3: Integration entity için Create/Read/Update/Delete uçları çalışıyor, EF Core migration repoya commit edilmiş ve `dotnet ef database update` ile temiz bir veritabanında sıfırdan çalışıyor.
- AC4: En az 1 integration test (Testcontainers PostgreSQL) CRUD akışını doğruluyor.

**Definition of Done:**
- Kod main/trunk branch'e merge edilmiş, build kırmızı değil.
- README'de "Quick Start" bölümü bu milestone'ın sonucuyla güncel (docker compose up ile ayağa kalkma adımları).
- Postman/HTTP dosyası ya da OpenAPI (Swagger) üzerinden CRUD manuel doğrulanmış.

### M2 — Ingestion (Gün 3-4)
**Kapsam:** API key auth, event ingestion endpoint, validation, raw event store, correlation id middleware.

**Acceptance Criteria:**
- AC1: Geçersiz/eksik API key ile istek 401 döner; geçerli key ile 202/200 döner — bu iki senaryo otomatik testle kanıtlı.
- AC2: Şema dışı (zorunlu alan eksik, yanlış tip) event gönderildiğinde 400 + anlamlı hata mesajı döner.
- AC3: Geçerli bir event gönderildiğinde ham event, değişmemiş haliyle (raw) veritabanına yazılır — bu "AI'nın orijinal veriyi asla kaybetmemesi" ilkesinin temelidir, bu yüzden kritik.
- AC4: Her istek bir correlation id taşır (gelen header'da varsa kullanılır, yoksa üretilir) ve bu id response header'ında da döner; loglarda görünür.
- AC5: Aynı payload'ı 2 kez gönderme (idempotency) davranışı en azından belgeleniyor (kabul mü, reddediliyor mu, dedupe mi) — MVP'de "kabul edilir, dedupe yok" olsa bile açıkça tasarım kararı olarak not düşülmeli (bkz. ADR listesi).

**Definition of Done:**
- Auth ve validation için unit test seti mevcut, negatif senaryolar dahil.
- Ingestion→raw store zinciri için integration test (Testcontainers PostgreSQL) yeşil.
- Rate limit / payload boyutu sınırı gibi temel kötüye kullanım koruması en azından placeholder olarak var (ya da ADR'de "MVP'de kapsam dışı" diye not edilmiş).

### M3 — Classifier + Incident (Gün 5-7)
**Kapsam:** Rule-based error classifier, fingerprinting, incident entity, incident list/detail endpointleri.

**Acceptance Criteria:**
- AC1: Classifier en az 5-6 farklı hata kategorisini (örn. auth_failure, rate_limit, timeout, malformed_response, schema_mismatch, unknown) doğru şekilde ayırt ediyor — her kategori için en az 1 pozitif ve 1 negatif test örneği.
- AC2: Fingerprinting algoritması aynı kök nedenden gelen tekrarlayan hataları aynı fingerprint altında gruplar; farklı kök nedenli hatalar farklı fingerprint alır. Bu, kontrollü test verisiyle (örn. 20 event, 3 gerçek kök neden) doğrulanmış olmalı — "aynı fingerprint sayısı = gerçek kök neden sayısı" testi geçmeli.
- AC3: Belirli bir fingerprint altında N. tekrar eden event, yeni incident açmak yerine mevcut incident'a event sayacını/son görülme zamanını günceller (incident patlaması/flood önleme).
- AC4: `GET /incidents` sayfalama ve temel filtreleme (durum, tarih aralığı) destekler; `GET /incidents/{id}` ilgili tüm ham event'leri ve fingerprint bilgisini döner.
- AC5: Response şemaları contract test ile doğrulanmış (bkz. Bölüm 4.3).

**Definition of Done:**
- Classifier ve fingerprint algoritmaları için unit test coverage'ı yüksek (bu modüller saf mantık olduğu için pratik hedef %85+).
- Incident endpointleri için integration test.
- Manuel demo: 10-15 sahte event gönderildiğinde, doğru sayıda incident'ın oluştuğu gözle görülüyor (ör. Swagger/Postman ile).

### M4 — Evidence (Gün 8-10, kısmen)
**Kapsam:** Hangfire, evidence collector, deployment correlation. (AI analiz job'ının stub/iskelet kısmı burada, gerçek AI entegrasyonu M5'e.)

**Acceptance Criteria:**
- AC1: Bir incident oluştuğunda arkaplan job'ı (Hangfire) tetikleniyor — bu, job dashboard'unda ya da loglarda gözlenebilir.
- AC2: Evidence collector, bir incident için ilgili ham event'leri, varsa yakın zamanlı deployment/config değişikliği kaydını ve zaman çizelgesini bir araya topluyor — bu "kanıt paketi" AI'ye gönderilecek girdinin temelini oluşturur.
- AC3: Deployment correlation: bir incident'ın oluşma zamanı ile en yakın deployment event'i arasındaki zaman farkı hesaplanıp incident üzerinde gösteriliyor (örn. "bu hata, deploy'dan 4 dakika sonra başladı").
- AC4: Evidence paketi, AI'ye giden girdinin JSON şeması olarak sabitlenmiş (bu, M5'in prompt tasarımı için sözleşmedir).

**Definition of Done:**
- Job tetikleme integration test.
- Evidence collector unit test (kanıt toplama mantığı sahte veriyle doğrulanmış).
- Deployment correlation hesaplama unit test (zaman farkı, en yakın deployment seçimi edge case'leri: hiç deployment yok, birden fazla aday var).

### M5 — AI (Gün 9-10, ana odak Gün 9, evidence entegrasyonu Gün 10)
**Kapsam:** Structured output şeması, prompt tasarımı, AI analiz job'ının gerçek çağrısı, evidence-only kısıt.

**Acceptance Criteria:**
- AC1: AI'ye giden prompt, yalnızca toplanan evidence paketini (M4) kullanır — AI'nin dışarıdan bilgi "uydurmasını" (hallucination) azaltmak için prompt açıkça "sadece verilen kanıta dayan, kanıt yoksa 'evidence insufficient' de" talimatı içerir. Bu MVP'nin güven inşa eden en kritik ürün kararlarından biri (bkz. ADR "AI evidence-only kısıtı").
- AC2: AI çıktısı sabit bir structured schema'ya (örn. root_cause_hypothesis, confidence, evidence_refs[], suggested_action) uyar; şema dışı/bozuk JSON döndüğünde sistem çökmez, "analiz başarısız" durumuna düşer ve tekrar denenebilir.
- AC3: En az 3 farklı hata senaryosu (örn. auth_failure, rate_limit, malformed_response) için AI analizinin makul ve kanıta dayalı bir hipotez ürettiği manuel olarak gözden geçirilmiş ve örnek çıktılar README/demo materyaline eklenmiş.
- AC4: AI çağrısı başarısız olduğunda (timeout, API hatası, rate limit) sistem incident'ı "analiz bekliyor/başarısız" durumunda bırakır, kullanıcıya/loglara görünür şekilde hata bildirir — sessiz başarısızlık yok.

**Definition of Done:**
- AI response parsing için unit test (geçerli şema, bozuk JSON, boş yanıt, evidence insufficient senaryoları).
- Prompt + şema versiyonlanmış (kod içinde sabit, değişiklik geçmişi izlenebilir).
- En az 3 gerçek (ya da mock AI ile simüle edilmiş, ama gerçek prompt üzerinden) örnek çıktı demo materyaline dahil.

### M6 — Observability + Demo (Gün 11-14)
**Kapsam:** Serilog, Seq, health checks, OpenTelemetry, mock connector'lar, test piramidinin tamamlanması, seed data, deploy, README, demo materyali.

**Acceptance Criteria:**
- AC1: Bir event'in ingestion'dan incident'a, incident'dan AI analizine kadarki tüm yolculuğu, correlation id üzerinden Seq'te tek sorguyla izlenebiliyor.
- AC2: `/health` endpoint'i PostgreSQL, Hangfire ve (varsa) AI sağlayıcı bağlantısını ayrı ayrı raporluyor (liveness/readiness ayrımı).
- AC3: Mock Stripe ve mock GitHub connector, gerçekçi event akışı üretip ingestion pipeline'ından başarıyla geçiyor — bu, demo ve seed data'nın kaynağı.
- AC4: Test piramidinin tüm katmanları (unit, integration, contract, e2e) CI'da çalışıyor ve yeşil (bkz. Bölüm 4).
- AC5: Canlı ortamda deploy edilmiş sistem, en az 1 uçtan uca senaryoyu (event gönder → incident oluş → AI analiz sonucu görüntülenebilir) internet üzerinden erişilebilir şekilde gösterebiliyor.
- AC6: README, demo GIF/video ve LinkedIn paylaşımı yayında.

**Definition of Done:**
- CI pipeline yeşil (build, test, docker build, migration check — bkz. Bölüm 6).
- Canlı URL çalışıyor, health check yeşil.
- Demo videosu (2 dakika) kayıt altında ve README'de linkli.
- GitHub repo public, README tam.

---

## 3. Test Piramidi Tasarımı

### 3.1 Neden ve nasıl: her katmanın amacı

**Unit test (taban, en geniş katman):**
- **Kapsam:** Classifier (kural motoru), fingerprinting algoritması, evidence collector'ın saf mantığı, AI response parser, validation kuralları, deployment correlation hesaplama.
- **Neden burada yoğunlaşılmalı:** Bunlar saf fonksiyon/domain mantığı, dış bağımlılık yok, hızlı çalışır, ve projenin "zeki" kısmı burada — bir işe alım yöneticisi ya da potansiyel müşteri kod tabanına baktığında classifier/fingerprint testlerinin kalitesi, projenin ciddiyetinin en güçlü göstergesi olur.
- **Yaklaşım:** Tablo-güdümlü (data-driven) testler — özellikle classifier için "girdi event → beklenen kategori" eşleşmeleri bir tablo/liste halinde tutulmalı, yeni kural eklendikçe tabloya satır eklenir. Fingerprint için "eşdeğerlik sınıfları" testi: aynı kök nedenden türeyen farklı yüzeysel varyasyonların (farklı zaman damgası, farklı mesaj detayı ama aynı hata tipi+entegrasyon) aynı fingerprint'e düştüğü, gerçekten farklı olanların düşmediği kanıtlanmalı.

**Integration test (Testcontainers ile PostgreSQL + Redis):**
- **Kapsam:** Ingestion→raw store yazımı, Integration CRUD, incident sorgulama/sayfalama, Hangfire job tetikleme (Hangfire storage PostgreSQL/Redis kullanıyorsa gerçek container üzerinden).
- **Neden Testcontainers:** In-memory veritabanı (örn. EF Core InMemory provider) SQL davranış farklarını (constraint, index, JSON kolon sorguları, migration uyumluluğu) gizler — özellikle bu projede PostgreSQL'e özgü JSON/JSONB sorgu kullanımı olası (raw event store, evidence paketi) olduğundan gerçek PostgreSQL container'ı şart. Redis kullanılıyorsa (cache, rate limit sayaçları gibi) aynı gerekçe geçerli.
- **Nasıl:** Test projesinde bir "shared fixture" (xUnit `IClassFixture`/`ICollectionFixture`) ile container'lar test sınıfları arasında paylaşılır, her testten önce veritabanı temizlenir/respawn edilir (Respawn kütüphanesi ya da migration'ı sıfırdan çalıştırma). CI'da Docker-in-Docker ya da GitHub Actions'ın native Docker desteği kullanılır — bu nedenle GitHub Actions runner'ı (ubuntu-latest, Docker önceden kurulu) tercih edilir.
- **Kapsam sınırı:** AI sağlayıcı gibi harici/pahalı/ücretli servisler Testcontainers'a değil, mock/stub'a bağlanır (aşağıya bakınız).

**Contract test (API şema doğrulama):**
- **Kapsam:** Ingestion endpoint'inin request şeması, incident list/detail endpoint'lerinin response şeması, AI structured output şeması.
- **Neden:** Bu proje hem gerçek müşterilerin göndereceği event'leri hem de AI'nin ürettiği yapılandırılmış çıktıyı işliyor — şema kayması (breaking change) sessizce ilerlerse hem entegrasyon hem AI parsing tarafında sessiz veri kaybına yol açar. Contract test, şemanın kod ile senkron kalmasını, ayrıca ileride harici müşterilerle paylaşılacak bir API sözleşmesinin belgelenmiş/test edilmiş olmasını sağlar (satış görüşmelerinde "API'niz stabil mi" sorusuna somut kanıt).
- **Nasıl:** JSON Schema (ya da OpenAPI'den türetilen şema) dosyaları repo'da versiyonlanır; test, gerçek response'u şemaya karşı doğrular. AI response için de aynı yaklaşım — "AI'nin çıktısı her zaman bu şemaya uysun" garantisi runtime'da da (M5 AC2), test zamanında da uygulanır.

**E2E test (event → incident → AI analiz zinciri):**
- **Kapsam:** Gerçek (test) API key ile bir event gönderilir → classifier çalışır → fingerprint hesaplanır → incident açılır/güncellenir → arkaplan job tetiklenir → evidence toplanır → AI analiz edilir (gerçek ya da kayıtlı/cassette AI yanıtı ile) → incident detail endpoint'inde analiz sonucu görünür hale gelir.
- **Neden:** Bu, ürünün "değer vaadinin" tamamının tek testte kanıtlanmasıdır — pazarlama/demo materyalinde anlatılan hikayenin gerçekten çalıştığının kanıtı. Diğer katmanlar parça parça doğru olsa da entegrasyon noktalarında (job tetikleme, evidence-to-prompt eşleme, response parsing) kırılma riski en yüksek yer burasıdır.
- **Nasıl:** Testcontainers ile gerçek PostgreSQL + gerçek Hangfire storage; AI çağrısı için gerçek API yerine kayıtlı yanıt (VCR/cassette tarzı yaklaşım ya da basit bir `IAiClient` arayüzü üzerinden test double) kullanılır — hem maliyet hem determinizm için. Gerçek AI sağlayıcıya karşı çalışan **ayrı, daha az sıklıkla koşulan** bir "smoke e2e" (örn. sadece manuel tetiklenen ya da haftalık) de tutulabilir ama CI'nın her push'ta çalışan ana e2e testi mock AI ile deterministik olmalı.

### 3.2 Katman dağılımı ve pratik hedefler
- Unit: çoğunluk (~%60-70 test sayısı), hızlı (<10sn toplam), her PR'da çalışır.
- Integration: orta (~%20-30), Testcontainers nedeniyle daha yavaş (~1-3dk), her PR'da çalışır.
- Contract: az sayıda ama kritik (~%5-10), şema dosyası değiştiğinde mutlaka tetiklenir.
- E2E: az sayıda (~2-5 senaryo), zincirin tamamını kapsar, her PR'da çalışır ama mock AI ile hızlı tutulur.

---

## 4. Demo Senaryosu — Somut Adım Adım (Stripe 401 Burst)

**Senaryo adı:** "Stripe Webhook Auth Patlaması" — gerçek dünyada sık görülen bir arıza modeli: bir API key rotasyonu/expiry sonrası entegrasyonun art arda 401 almaya başlaması ve kimsenin fark etmemesi.

**Adım 1 — Ortam hazırlığı:**
- Demo öncesi, mock Stripe connector "sağlıklı" modda çalışıyor, ekranda incident listesi boş/temiz.
- Sunucuda Seq ekranı ve incident dashboard'u yan yana açık.

**Adım 2 — Tetikleyici event'lerin gönderimi:**
- Mock Stripe connector, API key'in geçersiz olduğu bir duruma alınır (demo script'i bunu tetikler).
- Kısa aralıklarla (örn. 30 saniyede 8-10 kez) `stripe.webhook.delivery_failed` tipinde event'ler, `401 Unauthorized` detayıyla ingestion endpoint'ine gönderilir — her biri farklı zaman damgası, aynı kök neden (invalid_api_key).

**Adım 3 — Beklenen sistem davranışı:**
- İlk event geldiğinde: classifier bunu `auth_failure` kategorisine, entegrasyon `stripe` olarak sınıflandırır; fingerprint hesaplanır (stripe + auth_failure + ilgili key/endpoint kombinasyonu).
- Aynı fingerprint'e sahip sonraki event'ler: yeni incident açmaz, mevcut incident'ın "occurrence count"unu ve "last seen" alanını günceller — ekranda "1 incident, 9 tekrar" olarak görünür (10 ayrı incident değil — bu, ürünün "gürültüyü tek sinyale indirger" değer önermesinin canlı kanıtı).
- Incident açıldığında arkaplan job tetiklenir, evidence toplanır (ham event'ler + varsa yakın deployment kaydı), AI analiz job'ı çalışır.

**Adım 4 — Ekranda gösterilecekler (bu sırayla):**
1. Incident listesi: yeni bir incident'ın "Stripe – Authentication Failure" başlığıyla, artan occurrence sayacıyla belirdiği (canlı yenilenen ya da yenile butonuyla) gösterilir.
2. Incident detail sayfası açılır: tüm ham event'lerin zaman çizelgesi, occurrence count, ilk/son görülme zamanı.
3. AI analiz sonucu: root cause hipotezi (örn. "Stripe API key muhtemelen rotate edilmiş/expire olmuş, webhook doğrulaması bu yüzden başarısız oluyor"), confidence skoru, önerilen aksiyon (örn. "API key'i kontrol edin ve yenileyin"), ve bu hipotezin dayandığı kanıt referansları (hangi event'lere, hangi zaman aralığına dayandığı).
4. Seq ekranına geçilir: aynı correlation id ile tüm zincirin (ingestion → classify → fingerprint → incident update → job tetikleme → AI çağrısı) tek sorguda izlenebildiği gösterilir.
5. (Varsa) deployment correlation: eğer demo senaryosuna bir "sahte deployment" event'i de eklenmişse, incident'ın "bu deploy'dan X dakika sonra başladı" bilgisini gösterdiği vurgulanır.

**Adım 5 — Anlatı kapanışı (demo videosu için):**
- "10 ayrı hata bildirimi yerine, 1 net incident + kök neden hipotezi + önerilen aksiyon — bu, bir on-call mühendisin 30 dakikalık log kazma işini 30 saniyeye indiriyor" mesajıyla kapatılır.

**Not:** Bu senaryo hem M6'nın demo materyali hem de görüşme/pilot adaylarına gönderilecek 2 dakikalık video için temel akıştır (bkz. Bölüm 12).

---

## 5. CI/CD Pipeline Taslağı (GitHub Actions)

**Tetikleyiciler:** `push` (her branch), `pull_request` (main'e), opsiyonel `workflow_dispatch` (manuel).

**Job 1 — Build:**
- .NET SDK kurulumu (pinned sürüm, `global.json` ile).
- NuGet restore (cache'lenmiş).
- `dotnet build` — uyarılar hata olarak ele alınabilir (isteğe bağlı, MVP'de gevşek tutulabilir).

**Job 2 — Test (build'e bağımlı):**
- Unit testler çalıştırılır (hızlı katman, ayrı adım — hızlı geri bildirim için önce bu çalışır, başarısızsa integration/e2e'ye geçilmez).
- Integration + contract + e2e testler çalıştırılır (Testcontainers, Docker runner üzerinde native çalışır — GitHub Actions `ubuntu-latest` bunu destekler).
- Coverage raporu üretilir (örn. coverlet), artifact olarak yüklenir; PR'da özet yorum olarak gösterilebilir.

**Job 3 — Docker Build (test'e bağımlı):**
- API için Docker image build edilir (multi-stage Dockerfile).
- Image bir container registry'ye push edilmez (MVP CI'da gerek yok) ya da sadece main branch'te push edilir (deploy adımı için).
- Image boyutu ve build süresi loglanır (basit bir sağlık göstergesi).

**Job 4 — Migration Check (test'e bağımlı):**
- Geçici bir PostgreSQL container'ı ayağa kaldırılır.
- Tüm migration'lar sıfırdan uygulanır (`dotnet ef database update`), başarısızsa pipeline kırmızı olur — bu, "migration'lar birbirini bozmuş" sınıfı hataları merge öncesi yakalar.
- (İsteğe bağlı, ileri seviye) `dotnet ef migrations script` ile üretilen SQL'in gözden geçirilebilir bir artifact olarak saklanması.

**Branch koruması (main):**
- Build + Test + Migration Check job'ları zorunlu status check olarak ayarlanır; bunlar geçmeden merge engellenir.

**Neden bu sıralama:** Build → Test → (Docker Build + Migration Check paralel) — en ucuz/hızlı kontrol en önce, en pahalı (Docker image) en sonda; başarısız bir unit test için Docker image build etmeye gerek yok, CI süresi ve maliyeti düşer.

---

## 6. Deployment Planı

**MVP için önerilen:** Basit bir VPS (örn. Hetzner/DigitalOcean droplet) üzerinde Docker Compose ile tek makine deploy, ya da Fly.io (Docker image'dan doğrudan deploy, ücretsiz katman yeterli, otomatik HTTPS, düşük operasyonel yük).

**Neden aşırı mühendislik yapılmamalı (Kubernetes, çok bölgeli, otomatik ölçeklenen altyapı MVP'de gereksiz):**
- Bu aşamada amaç "canlıda çalışan, gösterilebilir, güvenilir bir demo ortamı" — trafik/ölçek sorunu yok, tek instance yeterli.
- Fly.io/basit VPS, Dockerfile'ı olan bir projeyi dakikalar içinde canlıya alır, DNS+TLS otomatik/yarı-otomatik halledilir — bu, Gün14'teki zaman baskısı altında riski azaltır.
- Kubernetes gibi bir seçim, hem öğrenme eğrisi hem operasyonel yük olarak 14 günlük plana orantısız risk katar; ayrıca portföy/demo bağlamında "aşırı mühendislik yapmamış, doğru ölçekte karar vermiş" izlenimi de bir sinyaldir (bu karar bir ADR olarak kayıt altına alınmalı, bkz. Bölüm 8).

**Somut adımlar:**
1. Gün13'te: Dockerfile ve docker-compose.prod.yml (ya da Fly.io `fly.toml`) hazırlanır, ortam değişkenleri (.env.example) belgelenir, health check endpoint'i doğrulanır.
2. Gün14'te: Hedef platforma deploy edilir; PostgreSQL için ya platformun yönetilen veritabanı (Fly Postgres, DigitalOcean Managed DB) ya da aynı VPS'te container olarak çalıştırılır (MVP'de ikincisi de kabul edilebilir, maliyeti düşürür).
3. Migration, deploy pipeline'ının bir parçası olarak (deploy öncesi/sonrası bir adım) otomatik çalıştırılır — manuel migration koşturma unutulma riski taşır.
4. Basit bir uptime/health check (örn. UptimeRobot ücretsiz katman) bağlanır — demo linkinin görüşme/pilot adaylarına gönderildiği dönemde "çöktü mü" sorusuna erken haber verir.
5. Secrets (AI API key, DB connection string) ortam değişkeni olarak platformun secret yönetimiyle set edilir, repoya asla commit edilmez.

---

## 7. GitHub Repo Yapısı ve README Planı

**Repo yapısı (üst düzey):**
```
/src            → modular monolith kaynak kodu (Integrations, Ingestion, Incidents, AI, Shared)
/tests          → unit / integration / contract / e2e test projeleri (katmana göre ayrı proje)
/docs           → ADR'ler, mimari notlar, bu tür planlama dokümanları
/docker         → Dockerfile, docker-compose.yml, docker-compose.prod.yml
/.github/workflows → CI pipeline tanımı
README.md
CONTRIBUTING.md (opsiyonel, portföy projesinde kısa tutulabilir)
LICENSE
```

**README bölüm başlıkları (önerilen sıra — okuyucunun 30 saniyede "bu ne, neden önemli" anlaması hedeflenir):**
1. **Başlık + tek cümlelik değer önermesi** (örn. "AI destekli entegrasyon hata istihbaratı — 3. parti API hatalarını gürültüden sinyale indirir").
2. **Demo GIF/video** (en üstte, ilk ekranda görünecek şekilde — Stripe 401 burst senaryosunun kısaltılmış GIF hali + tam video linki).
3. **Problem** (kısa, 3-4 cümle: neden bu problem gerçek, kimin yaşadığı).
4. **Nasıl çalışır** (mimari diyagram: event ingestion → classify → fingerprint → incident → AI analiz; basit bir kutu-ok diyagramı).
5. **Öne çıkan teknik kararlar** (modular monolith, evidence-only AI kısıtı, rule-based + AI ayrımı — ADR'lere link).
6. **Quick Start** (docker compose up ile lokal ayağa kaldırma adımları, örnek event gönderme curl komutu).
7. **Test stratejisi** (piramit özeti, coverage badge, CI badge).
8. **Canlı demo linki** (varsa, health check yeşilse).
9. **Roadmap / kapsam dışı bırakılanlar** (MVP'de yapılmayanlar açıkça listelenir — bu şeffaflık hem teknik hem satış görüşmelerinde güven inşa eder).
10. **İletişim / geri bildirim** (pilot/interview davetine link, LinkedIn profili).

**Görseller:**
- 1 mimari diyagram (statik PNG/SVG).
- 1 demo GIF (10-15 saniyelik, incident listesi + AI analiz sonucu odaklı).
- 1 tam demo video linki (YouTube/Loom, 2 dakika).
- CI badge, coverage badge (opsiyonel ama ucuz ve güven artırıcı).

---

## 8. ADR (Architecture Decision Record) Listesi

Aşağıdaki kararlar, geri dönüşü zor ya da başkalarına (görüşülen kişiler, pilot adayları, işe alım yöneticileri) açıklanması gereken kararlar olduğu için ADR olarak yazılmalı:

1. **ADR-001: Modular Monolith seçimi** — neden mikroservis değil (MVP aşamasında operasyonel karmaşıklık maliyeti fayda getirmiyor), modül sınırlarının nasıl çizildiği, gelecekte servislere ayrıştırma yolu açık mı.
2. **ADR-002: AI evidence-only kısıtı** — AI'nin sadece toplanan kanıta dayanarak analiz yapması, dışarıdan bilgi/varsayım eklememesi kararı; bu kararın güven ve doğruluk üzerindeki etkisi, "confidence" alanının anlamı.
3. **ADR-003: Rule-based classification + AI analiz ayrımı** — neden sınıflandırma (kategori, fingerprint) deterministik kurallarla yapılıyor, AI sadece kök neden hipotezi/aksiyon önerisi için kullanılıyor (maliyet, hız, güvenilirlik, test edilebilirlik gerekçeleri).
4. **ADR-004: Fingerprinting algoritması tasarımı** — hangi alanların fingerprint'e girdiği, neden (zaman damgası hariç, hata tipi+entegrasyon+ilgili kaynak dahil gibi), yanlış pozitif/negatif riskleri ve kabul edilen trade-off.
5. **ADR-005: PostgreSQL + JSON/JSONB kullanımı (raw event store)** — neden ayrı bir belge veritabanı yerine PostgreSQL'de JSONB, şema esnekliği ile sorgulanabilirlik dengesi.
6. **ADR-006: Testcontainers ile entegrasyon testi stratejisi** — neden in-memory DB yerine gerçek PostgreSQL/Redis container, CI maliyeti/süre trade-off'u.
7. **ADR-007: Deployment platformu seçimi (Fly.io/VPS, Kubernetes değil)** — MVP ölçeğinde aşırı mühendislikten kaçınma gerekçesi.
8. **ADR-008: Idempotency/dedupe kapsamı (MVP'de var mı yok mu)** — hangi senaryoların kapsam dışı bırakıldığı, ileride nasıl ele alınacağı.
9. **ADR-009: API key auth (OAuth/JWT değil)** — MVP aşamasında basit API key'in yeterliliği, gelecekte çok kiracılı (multi-tenant) senaryoda ne değişmesi gerektiği.
10. **ADR-010: Mock connector stratejisi (Stripe/GitHub)** — neden gerçek entegrasyon değil mock, demo/pilot için yeterliliği, gerçek entegrasyona geçiş planı.

Her ADR kısa tutulmalı (bağlam, karar, sonuç/trade-off — yarım-1 sayfa), `/docs/adr/` altında numaralı dosyalar halinde.

---

## 9. Problem Interview Planı

### 9.1 Hedef kitle ve bulma kanalları
- **Profil:** Backend/platform mühendisleri, tech lead'ler, CTO'lar — özellikle 3. parti API/ödeme/webhook entegrasyonu olan (Stripe, GitHub, ödeme sağlayıcıları, SaaS entegrasyon katmanı) küçük-orta ekiplerde çalışan kişiler.
- **Kanallar (somut):**
  - **LinkedIn:** Kişisel ağ + 2. derece bağlantılar; "backend engineer", "platform engineer", "engineering manager" + "Stripe/webhook/integration" anahtar kelimeleriyle arama; ayrıca ilgili Türkiye tech topluluklarının (örn. Türkiye Yazılımcı grupları, ilgili Slack/Discord toplulukları) üyelerine kişiselleştirilmiş mesaj.
  - **Reddit:** r/ExperiencedDevs, r/webdev, r/SaaS, r/devops — doğrudan DM yerine önce ilgili thread'lere değer katan yorumlar, sonra profil üzerinden ulaşım; doğrudan spam/reklam yapılmamalı.
  - **Indie Hackers:** "Ask IH" ve ilgili SaaS/DevTools kategorilerinde; kendi build-in-public gönderisiyle organik ilgi toplama + doğrudan mesajlaşma.
  - **Türk tech topluluğu:** Btech/Turkish Tech Slack toplulukları, ilgili Discord sunucuları, Twitter/X'te "#buildinpublic" ve backend/devtools içeriği paylaşan Türk geliştiriciler.
  - **Soğuk e-posta** (bkz. Bölüm 11) — LinkedIn/şirket sitesinden bulunan e-postalara, kişiselleştirilmiş kısa davet.

### 9.2 Görüşme soruları (geliştirilmiş)
**Açılış (bağlam, 2-3 dk):**
1. Ekibinizde kaç farklı 3. parti API/servisle entegrasyonunuz var (Stripe, ödeme sağlayıcıları, webhook alan/gönderen servisler)?
2. Bu entegrasyonlarda bir şey ters gittiğinde (401, timeout, beklenmeyen payload) genelde nasıl haberdar oluyorsunuz?

**Problem derinleştirme (ana kısım, 10-15 dk):**
3. Son 3 ayda entegrasyon kaynaklı bir üretim sorunu yaşadınız mı? Ne olmuştu, nasıl fark edildi, ne kadar sürede çözüldü?
4. Bu tür sorunları teşhis ederken en çok zaman aldığınız kısım nedir (log kazma, hangi servisin hatalı olduğunu bulma, kök nedeni anlama)?
5. Aynı hata farklı zamanlarda tekrar ettiğinde bunu fark ediyor musunuz, yoksa her seferinde sıfırdan mı araştırıyorsunuz?
6. Şu an bunun için kullandığınız araçlar neler (Sentry, Datadog, kendi log altyapınız, hiçbiri)? Bu araçlar "kök neden hipotezi" veriyor mu, yoksa sadece "bir şey oldu" mu diyor?
7. Bu problem ekibinizde ne sıklıkta yaşanıyor — haftada, ayda, nadiren? (Bu, "en az 5 ekip düzenli yaşıyor" başarı kriterinin doğrudan verisi.)
8. 1-10 arası, bu problem şu an sizin için ne kadar öncelikli (diğer teknik borç/özelliklere kıyasla)?

**Çözüm/ödeme sinyali (kapanış, 5 dk):**
9. Eğer bir araç, entegrasyon hatalarını otomatik gruplasa ve her grup için "muhtemel kök neden + önerilen aksiyon" verse, bu sizin için ne kadar değerli olurdu? Neden?
10. Böyle bir araca aylık ne kadar bütçe ayırırdınız (rakam vermeleri zorlanırsa: "ekip başına X-Y aralığında bir SaaS aracı" gibi çapa verilebilir)?
11. Böyle bir aracı 2 haftalık ücretsiz pilot olarak denemek ister misiniz?
12. Ekibinizde bu konuda karar veren/etkileyen başka biri var mı, tanıştırabilir misiniz? (Referans zinciri için.)

### 9.3 Görüşme lojistiği
- Süre: 20-25 dakika, video görüşme (Zoom/Meet) tercih, notlar görüşme sırasında ya da hemen sonrasında standart bir şablona (problem var mı/yok mu, sıklık, mevcut araç, öncelik skoru, ödeme sinyali, pilot ilgisi) işlenir.
- Her 5 görüşmede bir ara sentez yapılır (Bölüm 14'teki karar kriterleriyle karşılaştırılır) — 15 görüşmeyi bitirmeden erken sinyal kaçırılmaz.

---

## 10. Pilot Planı

**Kimin uygun olduğu:**
- Problem interview'de 8-10/10 öncelik verenler, "düzenli (haftalık/aylık) yaşıyoruz" diyenler, mevcut araçlarından memnun olmayanlar, gerçek üretim entegrasyonu (Stripe/webhook benzeri) olan ekipler.
- Tercihen kendi PostgreSQL/log altyapısına erişimi olan ya da en azından örnek/anonim veri paylaşabilecek teknik karar vericiler (CTO, tech lead) — "gerçek veri" hedefiyle çakışan aday havuzu.

**Pilot süresi:** 2 hafta (taslakla uyumlu) — bu süre, ekibin en az birkaç gerçek entegrasyon hatası yaşamasına ve aracın değer üretip üretmediğini görmesine yetecek kadar uzun, ama karar verme sürecini geciktirmeyecek kadar kısa.

**Pilot'tan öğrenilmek istenenler:**
1. Gerçek üretim verisiyle classifier/fingerprint doğruluğu ne durumda (yanlış pozitif/negatif oranı) — sentetik demo verisinden farklı davranış olabilir.
2. AI'nin ürettiği kök neden hipotezleri gerçek mühendisler tarafından "isabetli/faydalı" bulunuyor mu (nitel geri bildirim + varsa "bu hipotez doğruydu/yanlıştı" işaretlemesi).
3. Ekip aracı gerçekten günlük/haftalık akışına sokuyor mu, yoksa kurulup unutuluyor mu (kullanım sıklığı bir sinyal).
4. Pilot sonunda ödeme niyeti netleşiyor mu (somut fiyat teklifine "evet" ya da "hayır ama şu şartla evet" gibi net bir cevap alınmalı — belirsiz "belki sonra" kabul edilmemeli).
5. Entegrasyon sürtünmesi ne kadar (API key kurulumu, event gönderme entegrasyonu ne kadar sürdü) — bu, satış/onboarding sürecinin gerçekçiliğini test eder.

**Pilot mekaniği:** Pilot süresince haftalık kısa (15 dk) check-in görüşmesi; pilot sonunda 30 dk kapanış görüşmesi (yukarıdaki 5 madde + fiyatlandırma teklifi + devam/vazgeçme kararı).

---

## 11. Cold Outreach Sequence

**Hedef:** Problem interview daveti (birincil), pilot daveti (ikincil, interview'den sonra sıcak adaylara).

**Problem interview cold mail sequence:**
- **Mail 1 (Gün 0):** Kısa (5-6 cümle), kişiselleştirilmiş (alıcının şirketi/rolüne özel 1 cümle), net tek istek ("20 dakikanızı entegrasyon hataları hakkında birkaç soru için alabilir miyim"), takvim linki (Calendly benzeri) ile sürtünmesiz randevu.
- **Follow-up 1 (Gün 4, mail 1'e cevap yoksa):** Çok kısa (2-3 cümle), "üstte kalmış olabilir, hâlâ ilgileniyorsanız..." tonunda, yeni bir açı/soru eklenebilir (örn. "özellikle Stripe/webhook entegrasyonu olan ekiplerle konuşuyorum").
- **Follow-up 2 (Gün 9, hâlâ cevap yoksa):** Son kez, çok kısa, "kapatıyorum ama ilerde ilginizi çekerse..." tonunda; bu mail sonrası o kişiye tekrar ulaşılmaz (spam algısını önlemek için).
- **Kadans:** Toplam 3 mail, ~4-5 gün aralıklarla, ~9-10 günlük pencere. Bu, 14 günlük geliştirme takvimiyle paralel yürüyecek şekilde Gün1-Gün9 arası başlatılmalı ki Gün11'e kadar en sıcak adaylar netleşsin (M6'nın pilot daveti zamanlamasıyla örtüşür).

**Pilot daveti maili (interview'den sonra sıcak adaylara, tek mail + 1 follow-up):**
- **Mail 1:** Görüşmede konuşulanlara referans ("konuştuğumuz [X] problemi için bir MVP hazır, sizinle 2 haftalık ücretsiz pilot yapmak isterim"), demo video/GIF linki, net bir sonraki adım (kısa kurulum görüşmesi).
- **Follow-up (5-6 gün sonra cevap yoksa):** Kısa hatırlatma, demo videosunu tekrar öne çıkarır.

**Ton ve prensip:** Her mail somut, kişiselleştirilmiş, tek net istek içerir; genel/şablon hissi veren toplu postalardan kaçınılır — özellikle 10-15 kişilik küçük bir hedef kitlede kalite, hacimden önemlidir.

---

## 12. LinkedIn Build-in-Public Takvimi (14 günlük planla senkronize)

| Gün | İçerik | Amaç |
|---|---|---|
| Gün 1 | "Yeni bir proje başlıyorum: AI Integration Failure Intelligence — [problem cümlesi]. Neden bu problemi çözüyorum, 14 günde build-in-public yapacağım." | Erken görünürlük, ilk interview davetlerine destek |
| Gün 4 (Paylaşım #1) | "Gün 4: ingestion pipeline'ı ayakta — API key auth, raw event store, correlation id. [kısa teknik öğrenme/karar notu]" + görsel (kod/terminal ekran görüntüsü) | Teknik güvenilirlik inşası, mühendis kitlesine ulaşma |
| Gün 9 (Paylaşım #2) | "Gün 9: mimari kararlar — neden modular monolith, neden rule-based classification + AI ayrımı. [ADR özeti]" + mimari diyagram | Derinlik gösterme, potansiyel işe alım yöneticisi/CTO kitlesine sinyal |
| Gün 13-14 (Paylaşım #3) | Demo videosu (2 dk) + "14 günde neler öğrendim, ürün ne yapıyor, canlı link" + net CTA ("entegrasyon hataları sizin için de sorunsa, DM atın") | Lansman, demo talebi toplama (hedef: 3 anlamlı demo talebi) |

**Not:** Ara günlerde (opsiyonel, düşük efor) kısa "bugün ne yaptım" tarzı mikro-güncellemeler (1-2 cümle) tutarlılık algısını güçlendirir ama zorunlu değildir — asıl 3 paylaşım (Gün1 açılış hariç, toplamda 4 gönderi) yeterli sinyal taşır.

---

## 13. Continue / Pivot / Portfolio-Only Karar Kriterleri

Kaynak dokümandaki başarı eşikleri netleştirilip karar ağacına bağlanmıştır:

**Devam et (continue — gerçek ürün olarak ilerlet):**
- 15 görüşmenin en az 5'inde "problem düzenli (haftalık/aylık) yaşanıyor" + öncelik skoru ortalama 7+/10 **VE**
- En az 1 net ödeme niyeti (somut fiyat aralığına "evet" ya da pilot sonunda ücretli devam niyeti) **VE**
- En az 1 pilot başlatılabilmiş/başlatılmış.
→ Bu durumda: pilot'u tamamla, 2. pilot adayı bul, ürünleştirme (multi-tenant, gerçek Stripe/GitHub entegrasyonu, faturalama) için ayrı bir yol haritası başlat.

**Şartlı devam / daha fazla veri topla (extend):**
- 5-9 görüşme arası yapılmış ve sinyal karışık (bazı ekipler ilgili, bazıları değil) — henüz 15 görüşme eşiğine ulaşılmamış.
→ Bu durumda: portföy/teknik iş zaten Gün14'te bitmiş olacağı için ürün geliştirmeyi durdurmadan, +1 hafta boyunca sadece görüşme/outreach'e devam et, 15. görüşmede kesin karara var.

**Pivot (problemi/segmenti değiştir):**
- 15 görüşmenin çoğunda problem tanınıyor ama öncelik düşük (ortalama <5/10) **VEYA** mevcut araçlar (Sentry/Datadog) "yeterli" bulunuyor **VEYA** segment yanlış (örn. büyük şirketlerde zaten kurumsal çözüm var, küçük ekiplerde bütçe yok) **AMA** en az birkaç görüşmede net bir komşu problem/segment sinyali var (örn. "asıl sorun webhook değil, genel API rate limit yönetimi").
→ Bu durumda: aynı teknik temeli (ingestion, classification, fingerprinting altyapısı) koru, problem çerçevesini/hedef segmenti değiştirip 5-8 yeni görüşme ile hızlı doğrula.

**Kill condition — portföy projesi olarak bırak (kaynak dokümanla birebir uyumlu, netleştirilmiş):**
- 15+ görüşme tamamlanmış **VE** ("problem düşük öncelikli" **VEYA** "ödeme niyeti yok") ise → gerçek ürünleştirme çabası durdurulur.
→ Bu durumda: proje bir portföy/vaka çalışması olarak README'de dürüstçe konumlandırılır ("15 görüşme yaptım, şu sinyalleri gördüm, şu kararla bıraktım" — bu şeffaflık, başarısız olsa bile sürecin ciddiyetini gösterdiği için hâlâ güçlü bir portföy/mülakat anlatısıdır). Kod tabanı, mimari kararlar ve test disiplini portföy değerini korur; GTM çabası sonlandırılır.

**Karar zamanlaması:** 15. görüşme, teknik takvimle paralel yürütüldüğü için (Bölüm 1.3) Gün14 civarında ya da kısa süre sonra tamamlanmış olmalı — böylece "ürünü bitir, sonra 15 görüşme için ayrıca 3-4 hafta harca" gecikmesi yaşanmaz; teknik lansman ile GTM kararı yakın zamanda çakışır.

---

## 14. Riskler ve Notlar (ek)

- **Kapsam kayması riski:** 14 gün sıkı bir takvim; her günün AC'si "must-have" olarak okunmalı, "nice-to-have" özellikler (örn. gelişmiş filtreleme, çoklu AI sağlayıcı desteği) roadmap'e itilmeli.
- **AI maliyet/rate limit riski:** Gün9-10'da gerçek AI çağrıları test edilirken maliyet/rate limit izlenmeli; e2e testlerin CI'da mock AI kullanması (Bölüm 3.1) bu riski geliştirme sürecinde zaten azaltıyor.
- **Görüşme hızı riski:** 15 görüşmeyi 9 günde (Gün1-9 outreach penceresi) toplamak iddialı olabilir; gerekirse Bölüm 13'teki "şartlı devam" yolu ile pencere +1 hafta uzatılabilir, bu teknik takvimi etkilemez çünkü paralel yürüyor.
