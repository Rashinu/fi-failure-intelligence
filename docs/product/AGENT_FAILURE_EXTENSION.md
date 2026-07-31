# AI Agent Tool Call Failures — Strategic Extension Note (Not Implemented)

> Bkz. M19 Prompt Bölüm 18. Bu doküman bir spesifikasyon değil, bir STRATEJİK NOT. M19'da hiçbir
> kod değişikliği yapılmadı.

## Fikir

Market research, AI agent tool call hatalarının gelecekte güçlü bir kaynak tipi olabileceğini
işaret ediyor. FI, eninde sonunda şunları ingest edebilir:

```
AgentRunId
ToolCallId
ToolName
OperationRef            (P0-A'da eklenen aynı kavram - yeniden kullanılabilir)
External outcome        (tool call'un gerçek dünyada ne yaptığı)
```

## FI Neden Bir LangSmith Rakibi OLMAMALI

LangSmith (ve benzerleri) şu soruyu cevaplıyor: **"Agent ne yaptı?"** — prompt'lar, tool
çağrıları, token kullanımı, latency, trace'ler. Bu, agent'ın kendi iç yürütme mekaniğine
odaklanan bir gözlemlenebilirlik aracı.

FI'nin potansiyel farklı sorusu: **"Hangi gerçek iş operasyonu başarısız oldu, kimi etkiledi,
neden, ve operasyon şimdi ne yapmalı?"** — agent'ın kendi mekaniği değil, agent'ın TETİKLEDİĞİ
gerçek dünya sonucunun (bir ödeme, bir CRM güncellemesi, bir email) başarısız olup olmadığı.

Somut fark: bir agent bir "charge müşteriden" tool'unu çağırdı, tool çağrısı teknik olarak
BAŞARILI döndü (200 OK), ama gerçek Stripe işlemi reddedildi çünkü agent yanlış bir müşteri ID'si
kullandı. LangSmith bu tool çağrısını "başarılı" olarak gösterir (HTTP 200). FI'nin ilgilendiği
şey ayrı: gerçek iş sonucu (ödeme) başarısız oldu mu, hangi müşteriyi etkiledi, iş operasyonu
açısından ne yapılmalı. Bu, FI'nin mevcut Operation/Customer/Evidence modelinin agent-tetiklemeli
operasyonlara doğal bir genişlemesi olurdu — ama agent'ın kendi execution trace'ini
görüntülemek DEĞİL.

## M19'da Neden Yapılmadı

1. M19'un P0 açıkları (Operation Identity, Resolution, Grounding) zaten kapsamlı; agent desteği
   bunlardan hiçbirini kapatmıyor, kapsamı büyütüyor.
2. Gerçek bir müşteri/pilot sinyali yok — bu tamamen ileriye dönük bir pazar hipotezi.
3. `OperationRef`/`BusinessRecordRef` (P0-A'da eklendi) zaten agent-tetiklemeli operasyonları
   temsil edebilecek kadar genel — ileride `AgentRunId`/`ToolCallId` eklemek, mevcut şemaya
   ek nullable alanlar eklemekten ibaret olur, yeniden mimari gerekmez.

## Ne Zaman Yeniden Değerlendirilmeli

M20 görüşmelerinde bir otomasyon danışmanı veya entegrasyon geliştiricisi, "AI agent'larımız
gerçek iş operasyonlarını tetikliyor ve bunların başarısız olduğunu fark etmemiz saatler
alıyor" gibi somut bir sinyal verirse. O ana kadar bu yalnızca bir stratejik genişleme notu.
