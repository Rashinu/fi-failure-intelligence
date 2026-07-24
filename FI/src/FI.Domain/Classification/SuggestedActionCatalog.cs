namespace FI.Domain.Classification;

/// <summary>
/// Bkz. CTO review (docs/CTO_REVIEW_ANALYSIS.md, Faz 3) — AI'nin serbest metin
/// `recommendedActions`'ından bağımsız, kategoriye bağlı, deterministik ve anında görünen bir
/// öneri. AI analizi henüz tamamlanmamış (NEEDS_HUMAN_REVIEW veya evidence bekleniyor) bir
/// incident için bile kullanıcıya "şimdi ne yapmalıyım" sorusuna hemen bir cevap verir.
/// AI'nin kendi önerileriyle çelişmez — ikisi tamamlayıcıdır: bu katalog "ilk bakılacak yer",
/// AI analizi ise evidence'a dayalı, incident'a özgü ayrıntı.
/// </summary>
public static class SuggestedActionCatalog
{
    public static string For(EventCategory category) => category switch
    {
        EventCategory.SignatureError => "Webhook secret'ın son rotasyonunu ve gönderen tarafın kullandığı secret'ı karşılaştırın.",
        EventCategory.AuthenticationError => "API key'in geçerliliğini ve son rotasyon zamanını kontrol edin.",
        EventCategory.AuthorizationError => "Entegrasyonun izin/scope kapsamını ve son yetki değişikliklerini kontrol edin.",
        EventCategory.RateLimitError => "Retry-After header'ına göre backoff uygulayın; istek hacmini/eşzamanlılığı azaltın.",
        EventCategory.SchemaMismatch => "Provider'ın API/şema sürümünde yakın zamanda bir değişiklik olup olmadığını kontrol edin.",
        EventCategory.DuplicateEvent => "İstemci tarafındaki retry/idempotency-key mantığını kontrol edin.",
        EventCategory.Timeout => "Provider'ın durum sayfasını ve ağ/DNS yapılandırmasını kontrol edin.",
        EventCategory.ProviderError => "Provider'ın durum sayfasını kontrol edin — büyük olasılıkla geçici bir outage.",
        EventCategory.ClientErrorOther => "İstek gövdesini/parametrelerini son değişikliklere göre kontrol edin.",
        EventCategory.NetworkError => "DNS, firewall ve ağ yapılandırmasındaki son değişiklikleri kontrol edin.",
        EventCategory.UnknownError => "Otomatik sınıflandırma bu hatayı tanımadı — ham event verisini manuel inceleyin.",
        _ => "Ham event verisini manuel inceleyin."
    };
}
