# FI — Positioning

## Hedef Kullanıcı (ICP)

**Stripe/GitHub/SES gibi entegrasyonları olan, Hookdeck veya Svix gibi bir webhook altyapısı
zaten kullanan (ya da hiç izleme katmanı olmayan) 5-20 kişilik SaaS ekipleri.**

Somut profil:
- 5-20 mühendis, ayrı bir SRE/DevOps fonksiyonu yok — entegrasyon hatasıyla kim karşılaşırsa o
  ilgileniyor.
- En az bir gerçek ödeme/iletişim entegrasyonu (Stripe, GitHub deployments, SES/SendGrid) var.
- Webhook altyapısı ya Hookdeck/Svix gibi bir teslimat katmanıyla (ama iş etkisi katmanı
  olmadan) ya da hiçbir izleme katmanı olmadan çalışıyor.
- Bir entegrasyon hatası olduğunda "kaç gerçek müşteri etkilendi" sorusunu cevaplamak elle,
  log grep'leyerek yapılıyor.

## Konumlandırma Cümlesi

> **"Hookdeck/Svix size neyin patladığını gösterir, FI neden patladığını ve ne yapmanız
> gerektiğini söyler."**

Bu cümle bilinçli olarak Hookdeck/Svix'i düşman değil, **tamamlayıcı** olarak konumlandırıyor —
onlar teslimat katmanı (webhook ulaştı mı, retry oldu mu), FI ise **iş etkisi + kök neden**
katmanı (bu teslim edilen webhook'un temsil ettiği gerçek işlem başarılı oldu mu, kaç müşteriyi
etkiledi, neden, ne yapılmalı). Rakip değil, üstüne oturan bir katman — bu, mevcut Hookdeck/Svix
kullanıcılarına "onu değiştir" değil "onun üstüne ekle" mesajı veriyor, satış sürtünmesini azaltır.

## README.md ve Mevcut Landing Page ile Uyum Kontrolü

- **README.md**: mevcut açıklama ("evidence-backed failure intelligence", deterministik
  sınıflandırma + AI analiz) genel olarak tutarlı ama Hookdeck/Svix'e karşı **konumlandırma
  cümlesi hiç yok** — rakip-bağlamı eksik. Landing page'e eklendi (aşağıda), README henüz
  güncellenmedi (kapsam dışı bırakıldı, yalnızca landing page + bu doküman istendi).
- **Mevcut landing page** (`landing/index.html`, Vercel'de canlı): hero mesajı
  ("Entegrasyon bozulduğunda tahmin etme, kanıtla") bu ICP'ye ters değil ama **rakip-bağlamsız,
  daha genel bir mesaj**. Bu görev kapsamında hero + "nasıl çalışır" bölümü, yukarıdaki
  konumlandırma cümlesini kullanacak şekilde güncellendi (aşağıda).
