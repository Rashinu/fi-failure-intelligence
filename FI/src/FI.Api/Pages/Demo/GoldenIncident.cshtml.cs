using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FI.Api.Pages.Demo;

/// <summary>
/// Bkz. docs/reviews/M20_2_DEMO_AND_SECURITY.md P0-2. Seçilen çözüm: STATIC INCIDENT.
///
/// Neden bu seçildi (Read-only demo / Anonymous incident / Demo snapshot / Demo tenant
/// arasından): bu sayfa hiçbir DB sorgusu yapmaz, hiçbir POST/form içermez, ve
/// AdminBasicAuthMiddleware'in korumalı prefix listesinde ("/Incidents", "/hangfire",
/// "/api/v1/...") olmayan yeni bir route'ta yaşar - bu yüzden kimlik doğrulama gerektirmez.
/// Bu, "Demo tenant" (çoklu-kiracılık gerektirir - kesinlikle yapılmaması istenen bir mimari
/// genişleme) veya canlı verinin salt-okunur bir görünümünü (gelecekteki gerçek incident'ların
/// yanlışlıkla bu sayfada görünmesi/DB'ye bağımlılık riski taşır) sunmaktan daha güvenli:
/// production DB'ye hiç dokunmadığı için veri sızıntısı veya yıkıcı eylem riski YAPISAL OLARAK
/// sıfırdır.
///
/// Veriler, bu projenin M19/M20.1 sırasında gerçekten çalıştırılmış ve canlı doğrulanmış Golden
/// Incident senaryosundan (PaymentSync credential failure - 43 event / 12 operasyon / 7 müşteri)
/// birebir alınmıştır - hiçbir sayı/metin uydurulmadı. AI analizi kasıtlı olarak "NeedsHumanReview"
/// olarak gösteriliyor çünkü gerçek çalıştırmada da (Ai:AnthropicApiKey yapılandırılmadan) tam
/// olarak bu gerçekleşti - bu, ürünün "kanıt yoksa/yetersizse AI hiç çağrılmaz, dürüstçe insana
/// devreder" iddiasının kendisini kanıtlayan gerçek bir örnek, uydurma bir AI çıktısı değil.
/// </summary>
public class GoldenIncidentModel : PageModel
{
    public void OnGet()
    {
        // Bilerek boş - tüm veri view'da sabit (hardcoded) olarak render ediliyor, hiçbir
        // DbContext/servis enjekte edilmiyor.
    }
}
