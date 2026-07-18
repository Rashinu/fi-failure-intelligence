using System.Diagnostics;
using Serilog.Context;

namespace FI.Api.Middleware;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 30.
/// İstemci X-Correlation-Id gönderebilir; göndermezse üretilir. HttpContext.Items, response
/// header'ı, Serilog LogContext ve aktif OpenTelemetry Activity (fi.correlation_id tag'i) ile
/// yayılır — alt log/trace kayıtlarında tutarlı şekilde görünür.
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

        Activity.Current?.SetTag("fi.correlation_id", correlationId.ToString());

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}

public static class HttpContextCorrelationIdExtensions
{
    public static Guid GetCorrelationId(this HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdMiddleware.ItemsKey, out var value) && value is Guid guid
            ? guid
            : Guid.Empty;
}
