namespace FI.Api.Middleware;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 30.
/// İstemci X-Correlation-Id gönderebilir; göndermezse üretilir. HttpContext.Items ve response
/// header'ına yazılır. Serilog LogContext entegrasyonu M6'da (Gün 11) eklenecek.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemsKey = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var headerValue)
            && Guid.TryParse(headerValue, out var parsed)
                ? parsed
                : Guid.NewGuid();

        context.Items[ItemsKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId.ToString();

        await _next(context);
    }
}

public static class HttpContextCorrelationIdExtensions
{
    public static Guid GetCorrelationId(this HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdMiddleware.ItemsKey, out var value) && value is Guid guid
            ? guid
            : Guid.Empty;
}
