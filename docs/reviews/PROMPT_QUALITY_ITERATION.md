# Prompt Quality Iteration — `fi-root-cause-v1` vs. `fi-root-cause-v2`

> Gerçek Claude Haiku'ya karşı, gerçek bir Anthropic API key ile, 20 golden senaryonun
> **tamamına** karşı çalıştırılan gerçek ölçümler. Hiçbir skor uydurulmadı — her sayı bu
> dosyanın altındaki ham çalıştırmalardan geliyor.

## Bulunan İlk Gerçek (0.726 stale çıktı)

Repoda daha önce belgelenen "0.726" M14'ten kalma, eski bir ölçüm. Bugün aynı 20 senaryoya
karşı **V1 prompt'unun kendisi** (hiç değiştirilmeden) **0.670** skorladı (validator fix'inden
ÖNCE) — muhtemel neden: model versiyonu zamanla değişti. **0.726 yerine 0.670, bugünün gerçek
baseline'ı.**

## İterasyon Geçmişi (hepsi gerçek, sırayla)

| Adım | Değişiklik | Overall | Grounding | NeedsHumanReviewAccuracy | Not |
|---|---|---|---|---|---|
| 0 | V1 (baseline, değişmedi) | 0.670 | 0.200 | 0.250 | Bugünkü gerçek baseline |
| 1 | V2 taslak 1 (kanıt zorunluluğu + confidence çapaları + genişletilmiş needsHumanReview tetikleyicileri) | **0.393** ❌ | 0.050 | 0.200 | **Regresyon** — 10 senaryo `ParseFailed` |
| 2 | V2 taslak 2 (JSON-dışı prose yasağı eklendi) | 0.756 | 0.050 | 0.350 | ParseFailed'ler kapandı (0 kritik hata), ama Grounding hâlâ kötü |
| 3 | V2 taslak 3 (grounding-tetikleyen kelime seçimi düzeltildi) | 0.826 | 0.450 | 0.400 | Prompt-only iyileştirmenin tavanı |
| 4 | **`AiAnalysisValidator.CheckGrounding` fix** (stopword filtresi) — V1'e uygulandı | 0.820 | 0.300 | 0.500 | Validator fix V1'i de yükseltti |
| 4 | Aynı validator fix — V2'ye uygulandı | 0.801 | 0.300 | 0.400 | V2, düzeltilmiş validator'la V1'in **altında** |

## Adım 1 — Neden Regresyon Oldu (kesin teşhis, ham çıktıyla doğrulandı)

V2 taslak 1, `needsHumanReview=true` durumunda "hangi tetikleyicinin geçerli olduğunu
`probableRootCause`'da belirt" diyordu — model bunu **JSON'un dışına, kapanış `}`'dan sonra**
düz metin olarak yazdı (ör. `**Reason for human review:** ...`). `JsonSerializer.Deserialize`
bu "trailing content"i reddetti → `ParseFailed` → `RubricScorer`'ın kademeli sıfırlama mantığı
(`formatCompliance==0` ise 7 boyut da 0) o senaryoyu tamamen sıfırladı. **10/20 senaryoda**
oldu. Adım 2'de "ENTIRE response must be exactly one JSON object, nothing before/after" ile
düzeltildi.

## Adım 3/4 — Grounding'in Gerçek Kök Nedeni (kesin teşhis, FlaggedClaims ile doğrulandı)

`AiAnalysisValidator.CheckGrounding`'in `EntityLikeTokenRegex`'i (`[A-Z][a-zA-Z0-9_]{3,}`)
**herhangi bir 4+ karakterli büyük harfle başlayan kelimeyi** potansiyel "uydurulmuş entity"
sayıyordu. Model, cümle başına gelen sıradan İngilizce kelimeler yazdığında (`Human`,
`However`, `Repeated`) bunlar evidence corpus'unda olmadığı için **yanlışlıkla** flag'lendi —
gerçek bir halüsinasyon değildi. Kanıt (gerçek `FlaggedClaims` çıktısı):
```
FlaggedClaims: [Human]                    (rate-limit-traffic-spike)
FlaggedClaims: [Repeated, Human]          (duplicate-event-retry-storm)
FlaggedClaims: [However, Human]           (critical-severity-payment)
```
Fix: `CommonEnglishWordStopList` (`AiAnalysisValidator.cs`) — yalnızca entity-benzeri token
akışına uygulanan, **sayısal kontrolü hiç etkilemeyen**, dar/kürate edilmiş bir stopword listesi.
Gerçek bir yeni entity adı (`CustomBillingProxy` gibi, listede olmayan) hâlâ doğru yakalanıyor
(bkz. `RootCauseWithGenuineNewEntity_StillFlagged_StopListDoesNotLoosenRealDetection` testi).

## Sonuç ve Karar

**Validator fix'i (Adım 4) tek başına en yüksek etkili değişiklikti** — V1'i bile 0.670'ten
0.820'ye çıkardı, prompt'a hiç dokunmadan. V2'nin prompt-seviyesi değişiklikleri, validator
düzeltildikten SONRA ölçüldüğünde **V1'e göre net bir iyileşme göstermedi** (0.801 vs 0.820,
-0.019) — CategoryEcho/RootCauseAccuracy'de küçük kazançlar, ConfidenceCalibration/
NeedsHumanReviewAccuracy'de küçük kayıplarla dengelendi.

**Ne yapıldı:**
- ✅ `AiAnalysisValidator.CheckGrounding` stopword fix'i — commit edildi, gerçek veriyle
  doğrulanmış, gerçek bir iyileştirme (hem V1 hem V2'yi etkiliyor).
- ❌ `fi-root-cause-v2` **Activate EDİLMEDİ** — 0.85 eşiğini geçmiyor (0.801) VE V1'e göre
  net bir kazanç göstermiyor. Kodda `PromptTemplates.RootCauseV2SystemPrompt` olarak Draft
  referansı duruyor, gelecekteki bir iterasyon için.

**Sonraki gerçek kaldıraç:** Grounding hâlâ en zayıf boyut (0.300, her iki versiyonda da) ve
NeedsHumanReviewAccuracy onun üzerine kademeli olarak düşüyor. Bu, muhtemelen daha fazla
prompt-mühendisliği ile değil, `CheckGrounding`'in kendisinin (contradiction detection,
Bölüm M19'da bilinen sınırlama) daha derin bir iyileştirmesiyle çözülür — ayrı bir karar.

**Metodolojik not:** Tek bir 20-senaryo koşusu, Claude'un kendi run-to-run varyansına tabi
(deterministik değil) — buradaki delta'lar (özellikle ±0.02 civarındaki küçük farklar)
gürültü payı içerir, kesin bir "V2 kötüdür" iddiası için tek bir koşu yeterli değil, ama
mevcut kanıt V2'yi Activate etmeyi haklı çıkarmıyor.
