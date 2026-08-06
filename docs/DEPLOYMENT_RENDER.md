# FI — Render Deployment Notes & Migration Runbook

## Servisler

- **Postgres**: `fi-postgres` (id `dpg-d9nlm0jm8hqs73ekkcgg-a`), plan **`basic_256mb`** ($6/ay +
  $0.30/GB depolama), region `oregon`.
  - **2026-08-06'da free plandan `basic_256mb`'a yükseltildi** (30 günlük otomatik silinme riski
    kapatıldı — `basic_256mb`'ın API cevabında artık hiç `expiresAt` alanı yok). Yükseltme
    **yerinde** oldu: aynı instance ID, aynı connection string, sıfır kod/config değişikliği,
    sıfır downtime (health check'ler kesintisiz 200 döndü).
  - Yükseltmeden HEMEN ÖNCE tam bir `pg_dump` yedeği alındı (`backups/fi_render_backup_2026-08-06.dump`,
    27 tablo, doğrulandı — yalnızca bu makinede, asla commit edilmedi) — yükseltme başarısız olsaydı
    diye önlem, fiilen kullanılmadı çünkü yükseltme sorunsuz geçti.
  - Database adı Render tarafından `fi` yerine `fi_120k` olarak oluşturuldu (otomatik suffix).
- **Web Service**: `fi-api` (id `srv-d9nm21ijnfac73bc8pkg`), plan **hâlâ `free`**, region `oregon`.
  - URL: `https://fi-api-0bif.onrender.com`
  - Dockerfile: `FI/docker/Dockerfile`, context `FI/`.
  - ⚠️ **Önemli ayrım:** Postgres'in silinme riski kapandı, ama web service hâlâ Free planda —
    bu yüzden aşağıdaki "manuel migration runbook" (preDeployCommand'ın paid-only olması nedeniyle)
    **hâlâ geçerli**. Bu ikisi ayrı sorunlar: biri veri kaybı riskiydi (çözüldü), diğeri
    operasyonel bir zahmet (web service Starter'a geçmeden çözülmeyecek).

## Env Var Notları

- `ConnectionStrings__FiDatabase`: Render'ın `internalConnectionString`'inden Npgsql formatına
  (`Host=...;Port=...;Database=...;Username=...;Password=...;Ssl Mode=Require;Trust Server
  Certificate=true`) elle dönüştürülüp web service'e set edildi (render.yaml'da `fromDatabase`
  kullanılamadı çünkü Render'ın kendi URI formatı `postgres://...` ile Npgsql'in beklediği
  key=value format arasında otomatik bir dönüşüm yok).
- `Admin__SharedSecret`, `ApiKeys__Pepper`: rastgele üretilip set edildi (değerleri bu dosyaya
  veya repoya hiç YAZILMADI — yalnızca Render dashboard'da mevcut). Bkz.
  `docs/reviews/M20_1_PRODUCTION_SAFETY_REPORT.md` P0-A — Production artık bu ikisi placeholder
  değerdeyken hiç başlamayı reddediyor (fail-fast guard), bu yüzden bunların gelecekte yanlışlıkla
  placeholder'a dönmesi yapısal olarak imkânsız.
- `Ai__AnthropicApiKey`: bilerek set edilmedi — kullanıcı Render dashboard'dan elle girecek.

---

## ⚠️ Deployment Tier Sınıflandırması — bu ayrım her migration kararından ÖNCE okunmalı

Bkz. M20.1 onay kısıtı #4. Bugünkü Free-tier manuel migration süreci **yalnızca** aşağıdaki ilk
kategori için kabul edilebilir — diğer ikisi için AÇIKÇA yetersizdir:

| Kullanım aşaması | Bugünkü Free-tier manuel süreç kabul edilebilir mi? |
|---|---|
| **DESIGN-PARTNER DEMO (sentetik veri)** | ✅ Geçici olarak kabul edilebilir — aşağıdaki runbook'a uyulması şartıyla. |
| **PRIVATE PILOT (gerçek müşteri verisi)** | ❌ Yetersiz — paid Render tier (`preDeployCommand`) veya başka güvenli bir mekanizma **zorunlu**, onboarding'den ÖNCE. |
| **FIRST PAYING CUSTOMER** | ❌ Kesinlikle kabul edilemez — Postgres'i rutin migration'lar için `0.0.0.0/0`'a açmak operasyonel bir süreç olarak KABUL EDİLEMEZ. |

Bu doküman bugünkü süreci genel olarak "pilot-ready" ilan ETMEZ — yalnızca sentetik-veri demo
aşaması için geçerlidir.

---

## Migration Runbook (Free Tier — yalnızca Design-Partner Demo aşaması için)

Free plan `preDeployCommand`'ı desteklemediğinden (yalnızca paid instance type'larda mevcut,
Render'ın kendi dokümantasyonundan doğrulandı), her yeni migration şu adımlarla, elle ve
tekrarlanabilir şekilde uygulanmalıdır:

**0. Ön koşul — bu prosedürü başlatmadan önce**
- Hangi migration'ın uygulanacağını doğrula: `dotnet ef migrations list --project FI/src/FI.Infrastructure --startup-project FI/src/FI.Api` çıktısını en son `git log` ile karşılaştır.
- CI'daki `migration-check` job'ının (sıfır bir Postgres'e aynı migration setini uygulayan) yeşil olduğunu doğrula — bu, üretim DB'sine dokunmadan ÖNCE migration'ın en azından sözdizimsel/şema-seviyesinde güvenli olduğunun kanıtıdır.

**1. Pre-migration doğrulama**
- Render dashboard'dan mevcut Postgres disk kullanımı ve tablo satır sayılarını not al (kabaca bir "önce" durumu — Render Free Postgres'te otomatik point-in-time backup YOK, bu yüzden bu adım tek "geri dönüş" referansımız).
- `docker compose` ile lokal bir kopyada aynı migration'ı önce dene (gerçek Render verisine dokunmadan).

**2. Erişimi aç (yalnızca bu pencerede)**
- Render Postgres `ipAllowList`'ini geçici olarak `0.0.0.0/0`'a aç — **her seferinde kullanıcı onayı alınarak**, asla otomatik/sessizce.
- Bağlantı her zaman `Ssl Mode=Require` ile yapılır (asla düz metin).

**3. Migration'ı çalıştır**
- `dotnet FI.Api.dll --migrate` (bu makineden, Postgres'in external connection string'ine karşı).
- Migrator modu yalnızca migration + prompt-bootstrap + Hangfire şema kurulumu yapar, HTTP sunucusu hiç başlamaz (bkz. Program.cs) — bu adım prod trafiğini hiçbir zaman etkilemez.

**4. Post-migration doğrulama**
- `dotnet ef migrations list` çıktısının artık en son migration'ı "Applied" gösterdiğini doğrula.
- `curl https://fi-api-0bif.onrender.com/health/ready` → 200 (DB bağlantısı hâlâ sağlıklı).
- Yeni eklenen kolon/tablo varsa, ilgili API endpoint'inden bir örnek satırla spot-check yap.

**5. Erişimi kapat**
- `ipAllowList`'i hemen `[]` (boş — yalnızca Render'ın internal network'ü) olarak geri al.
- Kapatmanın gerçekten uygulandığını (`ipAllowList: null`) API'den tekrar sorgulayarak doğrula.

**6. Başarısızlık/rollback durumu**
- Migration yarıda başarısız olursa: **erişimi hemen kapat** (adım 5), sonra sorunu teşhis et — kısmi bir migration durumunda EF Core'un `__EFMigrationsHistory` tablosu hangi migration'ların gerçekten commit edildiğini gösterir; bir sonraki `--migrate` çalıştırması yalnızca eksik kalanları tekrar dener (idempotent).
- Free tier'da point-in-time restore YOK — bu yüzden yıkıcı (veri kaybettirebilecek) bir migration önce mutlaka lokal bir kopyada denenmeli (adım 1).

---

## Known Limitations (Web Service hâlâ Free Tier)

- `preDeployCommand` yok → yukarıdaki manuel süreç hâlâ zorunlu (bu, Postgres'in plan'ından
  değil, **web service**'in Free planda olmasından kaynaklanıyor).
- Web service "sleep after inactivity" (15 dakika) — ilk istekte soğuk başlama gecikmesi.
- ~~Postgres 2026-09-01'de otomatik siliniyor~~ — **ÇÖZÜLDÜ** (2026-08-06, `basic_256mb`'a
  yükseltildi, bkz. yukarıdaki "Servisler" bölümü). Artık PITR/backup da mevcut (paid tier).

## Recommendation

Postgres tarafı artık **Private Pilot** için yeterli (silinme riski kapandı, backup var).
Kalan tek engel: **web service** hâlâ Free planda, bu yüzden `preDeployCommand` kullanılamıyor ve
yukarıdaki manuel IP-allowlist runbook'u her migration'da hâlâ gerekiyor. Web service Starter
(veya üstü) plana yükseltilmeden gerçek müşteri verisiyle onboarding **yapılmamalıdır** —
yalnızca bu tek adım kaldı.
