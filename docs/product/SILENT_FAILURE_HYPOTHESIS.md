# Silent Failure — Documented Hypothesis (Not Implemented)

> Bkz. M19 Prompt Bölüm 17. Bu doküman bir spesifikasyon değil, bir HİPOTEZ. M19'da hiçbir kod
> değişikliği yapılmadı; bu, yalnızca gelecekteki bir müşteri doğrulaması (M20+) için kaydedilmiş
> bir düşünce.

## Silent Failure Nedir

FI'nin bugünkü tüm modeli, **bir hatanın gözlemlenebilir olduğu** varsayımına dayanıyor: bir HTTP
401/429/500, bir exception, bir webhook imza hatası. Deterministik sınıflandırıcı, bu
gözlemlenebilir sinyalleri (status code, hata metni, header'lar) 11 kategoriye ayırıyor.

Ama bazı gerçek entegrasyon başarısızlıkları **hiçbir hata üretmez**. Örnek:

```
Stripe ödeme başarılı (200 OK, webhook doğru imzalı, doğru işlendi)
    ↓ beklenen
Subscription "Active" olmalı
    ↓ gözlemlenen
Subscription "Pending" kalıyor (bir downstream job sessizce başarısız oldu,
ya da hiç tetiklenmedi, ya da bir race condition'da kayboldu)
```

Burada:
- `500` yok
- `401` yok
- `exception` yok
- imza hatası yok

FI'nin bugünkü hiçbir kod yolu bunu **tespit edemez** — çünkü tespit edilecek "başarısız" bir
teknik event hiç oluşmuyor. Bu, "kaç event başarısız oldu" sorusunun tanım gereği cevapsız
kaldığı bir sınıf.

## FI Bunu Bugün Neden Tespit Edemiyor

FI'nin tüm mimarisi **reaktif**: `IntegrationEvent` → sınıflandırma → `Incident`. Girdi her
zaman "bir şey oldu ve bunu gözlemledik" biçiminde. Silent failure'ı tespit etmek,
**proaktif/beklenti-tabanlı** bir modele geçiş gerektirir:

```
ExpectedState        (örn: "subscription-18372, PaymentSync sonrası Active olmalı")
ExpectedTransition    (örn: "Pending → Active, ödeme onayından sonra")
Deadline              (örn: "ödeme onayından 5 dakika içinde")
ObservedState         (gerçek durum, ayrı bir sorgu/webhook ile alınması gerekir)
StateDivergence       (Expected != Observed && Deadline geçti → bu bir incident)
Reconciliation        (periyodik kontrol mekanizması - "hiçbir event gelmedi" durumunu
                       tespit etmek için POLLING veya ikinci bir sistemin state'ini
                       sorgulamak gerekir, FI'ye push edilen bir event değil)
```

Bu, FI'nin bugünkü "event geldiğinde sınıflandır" modelinden temelde farklı — event gelmediğinde
de bir şeyin yanlış gittiğini anlamak gerekiyor. Bu bir motor değişikliği değil, **yeni bir veri
kaynağı kategorisi ve yeni bir zamanlama modeli** (periyodik reconciliation job'ları) gerektirir.

## Bunu İnşa Etmeyi Haklı Çıkaracak Müşteri Kanıtı

M19'da bu inşa EDİLMEDİ çünkü:
1. Gerçek bir müşteriden "bizim asıl acımız bu" sinyali henüz yok (M20'nin işi).
2. `ExpectedState`/`Deadline` kavramları entegrasyon-özel — her müşterinin "ne zaman ne
   olmalı" beklentisi farklı, genel bir şema tasarlamak erken.
3. Reconciliation job'ları gerçek bir maliyet/karmaşıklık kalemi (periyodik polling, üçüncü
   taraf API'lere ekstra çağrı, false-positive riski yüksek).

**M20 görüşmelerinde aranacak somut sinyal:** Bölüm 26 soru 6 — "Silent failures — operasyonlar
hiç tamamlanmıyor ama hiçbir teknik hata üretmiyor — bunu yeterince sık yaşıyor musunuz ki
reconciliation özellikleri haklı çıksın?" Eğer birden fazla görüşmede somut, isimlendirilmiş bir
örnek ("geçen ay X müşterisinin ödemesi başarılıydı ama hesabı hiç aktifleşmedi, 3 gün fark
etmedik") çıkarsa, bu inşa etmeye değer bir sinyal. Tek bir belirsiz "evet böyle bir şey olabilir"
yeterli değil.
