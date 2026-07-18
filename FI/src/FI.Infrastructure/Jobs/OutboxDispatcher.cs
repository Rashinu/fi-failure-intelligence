using System.Text.Json;
using FI.Domain.Outbox;
using FI.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FI.Infrastructure.Jobs;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 20.3 — transactional outbox dispatcher.
/// Hangfire recurring job olarak birkaç saniyede bir çalışır, bekleyen outbox kayıtlarını okuyup
/// gerçek job'u enqueue eder. Şu an yalnızca ClassifyJob tüketiliyor (M3); diğer mesaj tipleri
/// (FingerprintJob ayrı adım değil — classify+fingerprint tek job'da; EvidenceCollectorJob/
/// AiAnalysisJob) M4-M5'te eklenecek.
/// </summary>
public class OutboxDispatcher
{
    private const int BatchSize = 50;

    private readonly FiDbContext _db;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(FiDbContext db, IBackgroundJobClient backgroundJobClient, ILogger<OutboxDispatcher> logger)
    {
        _db = db;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    public async Task DispatchPendingAsync()
    {
        var pending = await _db.OutboxMessages
            .Where(m => m.Status == OutboxMessageStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync();

        foreach (var message in pending)
        {
            try
            {
                switch (message.MessageType)
                {
                    case OutboxMessageType.ClassifyJob:
                    {
                        var payload = JsonDocument.Parse(message.Payload).RootElement;
                        var eventId = payload.GetProperty("eventId").GetGuid();
                        var correlationId = payload.GetProperty("correlationId").GetGuid();
                        _backgroundJobClient.Enqueue<ClassifyJobHandler>(h => h.ExecuteAsync(eventId, correlationId, CancellationToken.None));
                        break;
                    }
                    case OutboxMessageType.EvidenceCollectorJob:
                    {
                        var payload = JsonDocument.Parse(message.Payload).RootElement;
                        var incidentId = payload.GetProperty("incidentId").GetGuid();
                        var correlationId = payload.GetProperty("correlationId").GetGuid();
                        _backgroundJobClient.Enqueue<EvidenceCollectorJobHandler>(h => h.ExecuteAsync(incidentId, correlationId, CancellationToken.None));
                        break;
                    }
                    case OutboxMessageType.AiAnalysisJob:
                    {
                        var payload = JsonDocument.Parse(message.Payload).RootElement;
                        var incidentId = payload.GetProperty("incidentId").GetGuid();
                        var correlationId = payload.GetProperty("correlationId").GetGuid();
                        _backgroundJobClient.Enqueue<AiAnalysisJobHandler>(h => h.ExecuteAsync(incidentId, correlationId, CancellationToken.None));
                        break;
                    }
                    default:
                        _logger.LogWarning("OutboxDispatcher: {MessageType} için henüz bir tüketici yok, atlanıyor.", message.MessageType);
                        break;
                }

                message.MarkDispatched();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutboxDispatcher: mesaj {MessageId} işlenirken hata oluştu.", message.Id);
                message.MarkFailed();
            }
        }

        if (pending.Count > 0)
            await _db.SaveChangesAsync();
    }
}
