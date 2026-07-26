using FI.Application.Outbox;
using FI.Domain.Outbox;
using FI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FI.Api.Controllers;

/// <summary>
/// Bkz. docs/CTO_REVIEW_ANALYSIS.md Due Diligence D4 - outbox'ın Failed durumu daha önce hiçbir
/// yerde görünür değildi (yalnızca bir LogError satırı). Bu, o başarısız mesajları admin
/// kimlik doğrulaması (AdminBasicAuthMiddleware, bkz. "/api/v1/admin" öneki) arkasında gözlemlenebilir
/// hale getiren minimal bir salt-okunur uç nokta.
/// </summary>
[ApiController]
[Route("api/v1/admin/outbox")]
public class OutboxController : ControllerBase
{
    private readonly FiDbContext _db;

    public OutboxController(FiDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OutboxMessageResponse>>> GetAll(
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var query = _db.OutboxMessages.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<OutboxMessageStatus>(status, ignoreCase: true, out var parsedStatus))
                return BadRequest(new { error = $"Geçersiz status değeri: {status}" });

            query = query.Where(m => m.Status == parsedStatus);
        }

        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(200)
            .Select(m => new OutboxMessageResponse(
                m.Id,
                m.MessageType.ToString(),
                m.Status.ToString(),
                m.FailureCount,
                m.LastFailedAt,
                m.LastError,
                m.CreatedAt,
                m.DispatchedAt))
            .ToListAsync(cancellationToken);

        return Ok(messages);
    }
}
