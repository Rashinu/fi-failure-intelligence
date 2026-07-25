using Hangfire.Dashboard;

namespace FI.Api.Middleware;

/// <summary>
/// Bkz. AdminBasicAuthMiddleware ve docs/CTO_REVIEW_ANALYSIS.md Due Diligence D7/D8. Hangfire'ın
/// kendi varsayılan yalnızca-localhost filtresi yerine kullanılır - "/hangfire" isteği bu noktaya
/// ulaştıysa AdminBasicAuthMiddleware zaten geçerli admin kimlik bilgisini doğrulamış demektir,
/// bu yüzden burada her zaman izin verilir. Bu filtreyi Hangfire'ın kendi varsayılanıyla
/// DEĞİŞTİRMEK kasıtlı: aksi halde iki bağımsız, tutarsız yetkilendirme kontrolü (biri paylaşılan
/// sır, diğeri "istek localhost'tan mı geliyor" varsayımı) üst üste biner.
/// </summary>
public class AlwaysAllowDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
