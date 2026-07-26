using System.Net;
using System.Net.Http.Json;
using FI.Domain.Outbox;
using FI.Infrastructure.Persistence;
using FI.Integration.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FI.Integration.Tests.Outbox;

/// <summary>
/// Bkz. docs/CTO_REVIEW_ANALYSIS.md Due Diligence D4 - başarısız outbox mesajlarının artık
/// admin-görünür bir uç noktada (FailureCount/LastFailedAt/LastError ile) gözlemlenebildiğini
/// doğrular.
/// </summary>
public class OutboxControllerTests : IClassFixture<FiApiFactory>
{
    private readonly FiApiFactory _factory;

    public OutboxControllerTests(FiApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<Guid> SeedFailedMessageAsync(string error)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();

        var message = FI.Domain.Outbox.OutboxMessage.Create(OutboxMessageType.ClassifyJob, "{\"eventId\":\"11111111-1111-1111-1111-111111111111\"}");
        message.MarkFailed(error);

        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();

        return message.Id;
    }

    [Fact]
    public async Task GetAll_FilteredByFailedStatus_ReturnsFailureMetadata()
    {
        var messageId = await SeedFailedMessageAsync("System.Exception: something broke");
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/outbox?status=Failed");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var messages = await response.Content.ReadFromJsonAsync<List<OutboxMessageDto>>();
        var seeded = messages!.Single(m => m.Id == messageId);

        seeded.Status.Should().Be("Failed");
        seeded.FailureCount.Should().Be(1);
        seeded.LastError.Should().Be("System.Exception: something broke");
        seeded.LastFailedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAll_WithInvalidStatus_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/outbox?status=NotARealStatus");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed record OutboxMessageDto(
        Guid Id,
        string MessageType,
        string Status,
        int FailureCount,
        DateTimeOffset? LastFailedAt,
        string? LastError,
        DateTimeOffset CreatedAt,
        DateTimeOffset? DispatchedAt);
}
