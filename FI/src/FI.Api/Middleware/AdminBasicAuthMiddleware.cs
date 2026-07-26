using System.Security.Cryptography;
using System.Text;

namespace FI.Api.Middleware;

/// <summary>
/// Bkz. docs/CTO_REVIEW_ANALYSIS.md - Due Diligence D7: control-plane (entegrasyon CRUD,
/// API-key/webhook-secret rotasyonu, incident dashboard, prompt-version promosyonu, Hangfire
/// dashboard) daha önce hiç authentication gerektirmiyordu - ağ erişimi olan HERKES ham bir
/// webhook secret'ı kendine rotate edebilir, tüm incident verisini görebilirdi. Bu, o açığı
/// kapatan minimal bir paylaşılan-sır (HTTP Basic) kapısı - tam bir RBAC/kullanıcı sistemi
/// değil, ADR-009'un MVP sınırıyla tutarlı, ama artık en azından bir sır gerektiriyor.
/// Ingestion endpoint'leri (/api/v1/events, /api/v1/webhooks, /api/v1/deployments) bu kapsamın
/// DIŞINDA - onlar zaten ApiKeyAuthMiddleware ile ayrı, machine-to-machine bir şemayla korunuyor.
/// </summary>
public class AdminBasicAuthMiddleware
{
    private const string ConfigKey = "Admin:SharedSecret";

    /// <summary>Yalnızca "Admin:SharedSecret" hiç yapılandırılmamışsa kullanılır - herhangi bir
    /// paylaşılan/production ortamında gerçek bir değerle override edilmelidir. Testlerin
    /// (FiApiFactory) bu sabiti kullanabilmesi için public.</summary>
    public const string LocalDevDefault = "local-dev-admin-secret-change-me";

    private static readonly PathString[] ProtectedPathPrefixes =
    {
        "/api/v1/integrations",
        "/api/v1/prompt-versions",
        "/api/v1/incidents",
        "/api/v1/admin",
        "/Incidents",
        "/hangfire"
    };

    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public AdminBasicAuthMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var isProtected = ProtectedPathPrefixes.Any(p => context.Request.Path.StartsWithSegments(p));
        if (!isProtected)
        {
            await _next(context);
            return;
        }

        var expectedSecret = _configuration[ConfigKey] ?? LocalDevDefault;

        if (TryGetBasicAuthPassword(context.Request, out var providedSecret) && FixedTimeEquals(providedSecret, expectedSecret))
        {
            await _next(context);
            return;
        }

        context.Response.Headers.WWWAuthenticate = "Basic realm=\"FI Admin\"";
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("""{"error":"Control-plane erişimi için admin kimlik bilgisi gerekiyor (HTTP Basic)."}""");
    }

    private static bool TryGetBasicAuthPassword(HttpRequest request, out string password)
    {
        password = "";
        if (!request.Headers.TryGetValue("Authorization", out var authHeaderValues))
            return false;

        var authHeader = authHeaderValues.ToString();
        if (!authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authHeader["Basic ".Length..].Trim()));
            var separatorIndex = decoded.IndexOf(':');
            if (separatorIndex < 0)
                return false;

            password = decoded[(separatorIndex + 1)..];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool FixedTimeEquals(string provided, string expected)
    {
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return providedBytes.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
