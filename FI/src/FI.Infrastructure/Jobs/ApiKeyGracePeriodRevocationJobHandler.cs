using FI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FI.Infrastructure.Jobs;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 33.4 — "yeni key üretilir, eski key
/// 24 saat grace period sonrası revoked_at". Rotasyon anında yalnızca <c>LastRotatedAt</c>
/// işaretlenir (bkz. <see cref="FI.Domain.Ingestion.ApiKey.MarkRotated"/>); bu Hangfire recurring
/// job'u, grace period'u dolmuş ama henüz revoke edilmemiş key'leri periyodik olarak revoke eder.
/// </summary>
public class ApiKeyGracePeriodRevocationJobHandler
{
    public static readonly TimeSpan GracePeriod = TimeSpan.FromHours(24);

    private readonly FiDbContext _db;
    private readonly ILogger<ApiKeyGracePeriodRevocationJobHandler> _logger;

    public ApiKeyGracePeriodRevocationJobHandler(FiDbContext db, ILogger<ApiKeyGracePeriodRevocationJobHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - GracePeriod;

        var expired = await _db.ApiKeys
            .Where(k => k.RevokedAt == null && k.LastRotatedAt != null && k.LastRotatedAt <= cutoff)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0) return;

        foreach (var key in expired)
            key.Revoke();

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("ApiKeyGracePeriodRevocationJob: {Count} rotasyon grace period'u dolan key revoke edildi.", expired.Count);
    }
}
