using System.Security.Cryptography;
using System.Text;
using FI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FI.Api.Middleware;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 18 (Ingestion API: X-Api-Key),
/// Bölüm 33.4 (HMAC-SHA256 + pepper). Yalnızca /api/v1/events ve /api/v1/deployments için
/// zorunludur; Product API (integrations CRUD) bu middleware'in kapsamı dışındadır (M2'de
/// henüz session/JWT yok — ADR-009 gereği bu MVP'de kabul edilen bir sınırdır).
/// </summary>
public class ApiKeyAuthMiddleware
{
    public const string HeaderName = "X-Api-Key";
    public const string ItemsKey = "IntegrationId";

    private static readonly PathString[] ProtectedPathPrefixes =
    {
        "/api/v1/events",
        "/api/v1/deployments"
    };

    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context, FiDbContext db)
    {
        var isProtected = ProtectedPathPrefixes.Any(p => context.Request.Path.StartsWithSegments(p));
        if (!isProtected)
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var apiKeyValues) || apiKeyValues.Count == 0)
        {
            await WriteUnauthorized(context, "X-Api-Key header eksik.");
            return;
        }

        var rawKey = apiKeyValues.ToString();
        var pepper = _configuration["ApiKeys:Pepper"] ?? "local-dev-pepper-change-me";
        var keyHash = ComputeHash(rawKey, pepper);

        var apiKey = await db.ApiKeys
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash && k.RevokedAt == null);

        if (apiKey is null)
        {
            await WriteUnauthorized(context, "Geçersiz veya iptal edilmiş API key.");
            return;
        }

        apiKey.RecordUsage();
        await db.SaveChangesAsync();

        context.Items[ItemsKey] = apiKey.IntegrationId;
        context.Items["ApiKeyId"] = apiKey.Id;

        await _next(context);
    }

    private static string ComputeHash(string rawKey, string pepper)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pepper));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawKey)));
    }

    private static async Task WriteUnauthorized(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync($"{{\"error\":\"{message}\"}}");
    }
}

public static class HttpContextIntegrationExtensions
{
    public static Guid? GetAuthenticatedIntegrationId(this HttpContext context) =>
        context.Items.TryGetValue(ApiKeyAuthMiddleware.ItemsKey, out var value) && value is Guid guid
            ? guid
            : null;

    public static Guid? GetAuthenticatedApiKeyId(this HttpContext context) =>
        context.Items.TryGetValue("ApiKeyId", out var value) && value is Guid guid
            ? guid
            : null;
}
