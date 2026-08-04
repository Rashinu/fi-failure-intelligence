# FI — Canlı Üç-Perspektif Test (Kullanıcı / Saldırgan / Alıcı)

> Bu test **gerçek, canlı** ortamlara karşı yapıldı: `https://fi-api-0bif.onrender.com`
> (backend + dashboard) ve `https://landing-three-blush-27.vercel.app` (landing page).
> Hiçbir yıkıcı/DoS testi yapılmadı (gerçek paylaşılan bir production ortamı olduğu için kasıtlı
> olarak load/flood testleri hariç tutuldu — bu sınır aşağıda açıkça not edildi). Bulgular gerçek
> HTTP istekleri ve gerçek yanıtlarla doğrulandı, varsayılmadı.

---

## 1. NORMAL KULLANICI Perspektifi

*(Admin paylaşılan-sırrı elinde olan, dashboard'u amaçlandığı gibi kullanan biri.)*

**Ne çalışıyor:**
- `/Incidents` dashboard'u, filtreler, Golden Incident detay sayfası, Resolve akışı — hepsi
  M20.1 sırasında canlıda uçtan uca doğrulandı (43/12/7, Complete/Complete, Resolve → Reopen).
- Sayfa yükleme süreleri makul (`/Incidents` ~0.3s, health check'ler anlık).
- Dil tutarlılığı (M20 UX fix'i sonrası) İngilizce'de tutarlı.

**Gerçek bir kullanıcı deneyimi sorunu:**
- **Etkilenen müşteri sayısı var ama kimliği yok.** "7 müşteri etkilendi" görüyorsun ama hangi 7
  müşteri olduğunu FI içinden öğrenemiyorsun — support mühendisi bu bilgiyi başka bir yerden
  (Stripe dashboard'u, CRM) bulmak zorunda. (M20 raporunda zaten not edilmişti, burada canlı
  olarak tekrar doğrulandı — hâlâ geçerli.)
- Dashboard listesinde (Index) operation/customer sütunu yok — yalnızca detay sayfasında görünüyor,
  bu yüzden "hangi incident'a önce bakmalıyım" kararı listeden verilemiyor.
- Free-tier Render "sleep after inactivity" — birkaç saat kullanılmazsa ilk istek birkaç saniye
  soğuk başlama gecikmesi yaşayacak (bu test sırasında servis zaten "sıcak"tı, gecikme
  gözlemlenmedi, ama bilinen bir risk olarak kalıyor).

---

## 2. SALDIRGAN / KÖTÜ NİYETLİ AKTÖR Perspektifi

*(Hiçbir kimlik bilgisi olmadan, dışarıdan.)*

### 2.1 Bulunanlar — İYİ (savunma çalışıyor)

| Test | Sonuç |
|---|---|
| `/hangfire`, `/Incidents`, `/api/v1/integrations` kimliksiz | 401 (hepsi) ✅ |
| `/swagger/index.html` (Production'da) | 404 — dev-only doğru kapatılmış ✅ |
| Yanlış admin şifresi | 401 ✅ |
| Basic Auth query string ile (header yerine) | 401 — yalnızca header kabul ediliyor ✅ |
| TRACE HTTP metodu | 405 ✅ |
| SQL-injection-benzeri string (`'; DROP TABLE...`) query param'da | **403 "Blocked"** — Cloudflare WAF edge'de yakalayıp engelliyor (Render'ın önündeki Cloudflare, uygulamamızın kendi kodu değil, ama gerçek bir savunma katmanı) ✅ |
| Path'te `' OR '1'='1` | Aynı şekilde Cloudflare WAF tarafından 403 ile bloklandı ✅ |
| Geçersiz Guid route param'ı (`/api/v1/incidents/not-a-guid`) | 404, ham 500/stack trace YOK (M20.1'in yeni global exception handler'ı + ASP.NET Core'un route constraint'i) ✅ |
| Geçersiz `businessCriticality` gönderimi | 400 ProblemDetails, traceId var, stack trace/SQL/dosya yolu sızmıyor ✅ |
| Eski (pepper-rotasyonu öncesi) ham API key | 401 — pepper migration'ı gerçekten işe yaramış ✅ |

### 2.2 Bulunanlar — GERÇEK, DİKKAT GEREKTİREN

**🔴 Webhook endpoint'i, imzasız isteği REDDETMİYOR, kabul edip işliyor.**

`POST /api/v1/webhooks/stripe/{integrationId}/events` isteğine **hiç `Stripe-Signature` header'ı
olmadan** gönderilen bir istek → **HTTP 201**, `isSignatureVerified:false` ile kabul edildi ve
gerçek bir incident'a dönüştü (`category: SignatureError, severity: High`). Yani sistem:
- İmzasız isteği HTTP seviyesinde reddetmiyor (401/403 dönmüyor) — imza doğrulaması yalnızca
  *sonradan sınıflandırma sinyali* olarak kullanılıyor, bir *erişim kontrolü* olarak değil.
- **İyi haber:** deterministik sınıflandırıcı bunu doğru şekilde `SignatureError` (yüksek
  severity) olarak işaretliyor — yani sahte bir "gerçek ödeme hatası" gibi görünmüyor, dashboard'da
  "biri bize sahte/imzasız bir istek gönderdi" olarak doğru okunuyor.
- **Gerçek risk:** bir `integrationId` GUID'ini bilen/ele geçiren biri (GUID pratik olarak
  brute-force edilemez, ama bir log/hata mesajı/URL üzerinden sızarsa) bu endpoint'e sınırsızca
  sahte istek gönderip incident dashboard'unu `SignatureError` kayıtlarıyla doldurabilir
  (bir "noise injection" / dashboard-spam riski — gerçek veri bütünlüğü ihlali değil, ama gerçek
  bir kullanılabilirlik/güven riski). Standart webhook güvenlik pratiği (Stripe'ın kendi önerdiği
  dahil) imza geçersizse isteği HTTP seviyesinde reddetmektir (401/400) — FI bunu yapmıyor.

**🟡 API, girdi sanitizasyonu yapmadan kabul ediyor (stored, henüz doğrulanmış bir XSS değil).**

`POST /api/v1/integrations` ile `name: "<script>alert(1)</script>"` gönderildi → **201 Created**,
hiçbir doğrulama/temizleme olmadan kabul edildi. Razor Pages'in `@` sözdizimi varsayılan olarak
HTML-encode ettiği için bugünkü UI'da bunun çalışan bir XSS'e dönüşmesi **muhtemel değil**
(bu incelemede ayrı bir render-doğrulaması yapılmadı — API kabul ettiğini doğrulandı, UI'da
gerçekten kaçtığını render ederek doğrulamadım, bu yüzden "muhtemelen güvenli" diyorum, "kesin
güvenli" değil). Yine de API katmanının hiç girdi doğrulaması yapmaması, ileride Razor dışı bir
tüketici (kendi API'sini yazan bir entegrasyon danışmanı gibi) bu veriyi kaçırmadan render ederse
gerçek bir XSS'e dönüşebilir.

**🟢 Cloudflare edge güvenlik header'ları eksik.** `Strict-Transport-Security`,
`Content-Security-Policy`, `X-Content-Type-Options`, `X-Frame-Options` yanıtlarda hiç yok —
düşük öncelikli ama standart bir sertleştirme eksikliği.

### 2.3 Test Edilmeyen (bilerek)

Gerçek, paylaşılan bir production ortamı olduğu için: rate-limit'i gerçekten kırmaya çalışan bir
flood testi, gerçek bir DoS denemesi, veya Postgres'e doğrudan bağlanmaya çalışan bir ağ taraması
**bilerek yapılmadı** — bunlar sistemi gerçekten bozma riski taşırdı.

---

## 3. GERÇEKTEN ALICI OLAN BİRİ Perspektifi

*(Landing page'i bulan, "Canlı demoyu incele"ye tıklayan potansiyel bir müşteri.)*

**Landing page kendisi:** 200, ~0.7s yükleniyor, mesaj net ("tahmin etme, kanıtla"), Golden
Incident kartı ikna edici bir görsel hikaye anlatıyor.

**🔴 Kritik bulgu: "Canlı demoyu incele" linkine tıklayan biri hiçbir şey GÖREMİYOR.**

Landing page'deki her iki CTA da (`hero` ve alt CTA) doğrudan
`https://fi-api-0bif.onrender.com`'a gidiyor — ki bu **kimlik doğrulaması gerektiren** bir admin
dashboard'u. Canlı olarak doğrulandı: hiçbir kimlik bilgisi olmadan bu linke gidildiğinde
**401 Unauthorized** ekranı çıkıyor. Yani:
- Landing page "canlı demoyu incele" vaat ediyor.
- Gerçekte tıklayan kişi bir login/401 ekranından başka hiçbir şey görmüyor.
- Hiçbir yerde (landing page'de veya 401 sayfasında) "demo kimlik bilgisi için bize ulaşın" gibi
  bir yönlendirme yok.

Bu, bir alıcı gözünden **en ciddi bulgu** — ürünün en güçlü kanıtı (Golden Incident) potansiyel
müşteriye asla gösterilmiyor, tam tersine ilk temasta bir hata ekranıyla karşılaşıyorlar.

**Diğer alıcı-gözü gözlemler:**
- CTA e-postası (`mailto:`) çalışıyor ve doğru adrese gidiyor (M20 UX fix'i sonrası doğrulandı).
- Fiyatlandırma hiçbir yerde yok (muhtemelen bilinçli, "beta" konumlandırması için makul).
- Golden Incident kartı gerçek ürünün bire bir aynısı değil, statik bir mockup — gerçek ürünle
  görsel/dil tutarlılığı iyi (M20 UX fix'i sonrası ikisi de İngilizce), ama alıcı gerçek ürünü
  hiç göremediği için bu tutarlılığı fark edecek durumda bile değil.

---

## Özet Tablo

| Perspektif | En kritik bulgu |
|---|---|
| Kullanıcı | Etkilenen müşteri kimliği/listesi yok (zaten bilinen bir M20 bulgusu, canlı teyit edildi) |
| Saldırgan | Webhook endpoint'i imzasız istekleri HTTP seviyesinde reddetmiyor — yalnızca sınıflandırma sinyali olarak kullanıyor |
| Alıcı | Landing page'in "canlı demo" linki, kimlik bilgisi olmayan hiç kimseye hiçbir şey göstermiyor (401) |

## Önerilen En Öncelikli İki Düzeltme

1. **Webhook imza doğrulaması, HTTP seviyesinde reddetmeli** (401/400) geçersiz/eksik imzada —
   bugünkü "kabul et, sınıflandır" yaklaşımı yerine, standart webhook güvenlik pratiğine uygun.
2. **Landing page'e ya gerçek, kimlik gerektirmeyen bir salt-okunur demo linki EKLENMELİ, ya da
   CTA metni "demo talep et" gibi değiştirilip bir form/e-posta akışına yönlendirilmeli** —
   bugünkü haliyle "canlı demoyu incele" vaadi yanlış bir beklenti yaratıyor.
