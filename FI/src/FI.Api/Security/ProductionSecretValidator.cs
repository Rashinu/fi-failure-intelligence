using FI.Api.Middleware;
using Microsoft.Extensions.Configuration;

namespace FI.Api.Security;

/// <summary>
/// Bkz. docs/reviews/M20_1_PRODUCTION_SAFETY_REPORT.md P0-A. Development/Docker Compose'ta
/// bilinçli olarak kullanılan zayıf placeholder değerlerin (Admin:SharedSecret, ApiKeys:Pepper)
/// Production'da sessizce kabul edilmesini engelleyen, saf ve host'suz test edilebilir bir
/// doğrulayıcı. Yalnızca hangi config KEY'lerinin sorunlu olduğunu döner - hiçbir zaman değeri
/// loglamaz veya fırlatılan mesaja gömmez.
/// </summary>
public static class ProductionSecretValidator
{
    /// <summary>
    /// Production DIŞINDA (Development, Testing, Docker Compose'un varsayılan ortamı vb.) her
    /// zaman boş liste döner - yalnızca gerçek Production ortamında zorunlu kılınır.
    /// </summary>
    public static IReadOnlyList<string> Validate(IConfiguration configuration, bool isProduction)
    {
        if (!isProduction) return Array.Empty<string>();

        var problems = new List<string>();

        var adminSecret = configuration["Admin:SharedSecret"];
        if (string.IsNullOrWhiteSpace(adminSecret) || adminSecret == AdminBasicAuthMiddleware.LocalDevDefault)
            problems.Add("Admin:SharedSecret");

        var apiKeyPepper = configuration["ApiKeys:Pepper"];
        if (string.IsNullOrWhiteSpace(apiKeyPepper) || apiKeyPepper == ApiKeyAuthMiddleware.LocalDevPepperDefault)
            problems.Add("ApiKeys:Pepper");

        return problems;
    }
}
