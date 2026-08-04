# FI — Technical Debt Register

> Kaynak: `docs/reviews/ENGINEERING_REVIEW_M20.md`, M20.1 kapsamında (secret guard, global
> exception handling, migration safety) ele alınmayan kalan bulgular. Sınıflandırma mimari saflık
> değil, **müşteri/iş riski** temelli yapıldı — bkz. M20.1 onay talimatı.

| Bulgu | Zamanlama | Gerekçe |
|---|---|---|
| `FI.Application` katmanının fiilen boş olması (yalnızca DTO'lar) | **Post-Validation** | Bugünkü ölçekte müşteriye görünür bir risk değil; yalnızca kod tabanı büyüdükçe controller'ların "god class"a dönüşme riskini artırıyor. Gerçek bir ikinci use-case akışı (M20 görüşmeleri sonrası) ortaya çıkınca yeniden değerlendirilmeli. |
| Kullanılmayan `DomainEvent` soyutlaması | **Only If Scale Requires** | Ölü kod, ama hiçbir davranışı etkilemiyor — hiçbir müşteri/iş riski yok, yalnızca bir netlik meselesi. |
| API versioning gerçek bir strateji değil (yalnızca route string'i) | **Before Private Pilot** | Tek gerçek istemci (kendi UI'ımız) olduğu sürece risk yok; ama bir pilot müşteri kendi entegrasyon kodunu API'mize karşı yazmaya başlarsa, versiyonsuz bir breaking change onları sessizce kırabilir. |
| `ResolutionSource`'un enum değil düz string olması | **Only If Scale Requires** | Bugüne kadar hiçbir yanlış string production'da görülmedi (kod incelemesiyle korunuyor); gerçek bir veri bütünlüğü sorunu ortaya çıkarsa enum'a geçiş kolay ve izole bir değişiklik. |
| Webhook replay koruması yalnızca timestamp-tolerance, nonce/dedupe yok | **Before Private Pilot** | Sentetik demo verisiyle risk teorik; gerçek bir müşterinin gerçek ödeme/iş verisiyle replay riski, gerçek parasal/iş etkisi olan bir senaryo haline gelebilir. |
| Kod coverage eşiklenmiyor/izlenmiyor | **Post-Validation** | Bugünkü test sayısı (164 domain + 96+ integration) zaten gerçek bir disiplin gösteriyor; eşik/trend takibi, ekip büyüyüp katkı sayısı artınca değerli olur. |
| Load/performans testi hiç yok | **Before First Paying Customer** | Demo/pilot ölçeğinde (tek haneli entegrasyon, düşük hacim) gereksiz; gerçek bir ödeme yapan müşterinin gerçek trafik hacmine geçmeden önce zorunlu. |
| Çoklu-kullanıcı/gerçek kimlik (AdminBasicAuthMiddleware'in paylaşılan-sır modeli) | **Before Private Pilot** | Tek operatör (biz) olduğu sürece hesap verebilirlik sorunu yok; birden fazla gerçek kullanıcı (müşteri tarafında bir operatör dahil) sisteme erişmeye başlarsa "kim ne yaptı" sorusu gerçek bir ihtiyaç haline gelir. |
| `OutboxDispatcher`'ın Hangfire scheduling semantiğine bağımlılığı (kod seviyesinde defense-in-depth yok) | **Only If Scale Requires** | Bugün doğru ve güvenli (Hangfire'ın kendi distributed lock'u); yalnızca biri gelecekte tetikleme mekanizmasını değiştirirse risk oluşur — proaktif bir refactor bugün gereksiz karmaşıklık ekler. |

---

## Bu Register'ın Kullanımı

Her yeni milestone (M21+) öncesi bu tablo gözden geçirilmeli: bir önceki sütunun eşiği
(Design Partner → Private Pilot → First Paying Customer geçişi) aşıldığında, o zamanlamaya
etiketlenmiş kalemler yeniden önceliklendirilmeli.
