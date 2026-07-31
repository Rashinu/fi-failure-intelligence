using System.Net.Http.Json;
using FI.Application.Ingestion;
using FI.Application.Integrations;
using FI.Infrastructure.Jobs;
using FI.Infrastructure.Persistence;
using FI.Integration.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FI.Integration.Tests.Classification;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bolum 38.2 (M3 Acceptance Criteria).
/// Outbox dispatcher/Hangfire zamanlamasina bagli kalmamak icin ClassifyJobHandler DI
/// uzerinden dogrudan cagrilir; bu, "outbox mesaji dogru yazildi mi" ile "classify+fingerprint+
/// incident mantigi dogru mu" sorularini ayri, deterministik testler olarak ele alir.
/// </summary>
public class ClassificationToIncidentTests : IClassFixture<FiApiFactory>
{
    private readonly FiApiFactory _factory;

    public ClassificationToIncidentTests(FiApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<(Guid IntegrationId, string ApiKey)> CreateIntegrationAsync(HttpClient client)
    {
        var createResponse = await client.PostAsJsonAsync("/api/v1/integrations", new CreateIntegrationRequest(
            Name: $"Stripe {Guid.NewGuid():N}",
            Provider: "stripe",
            Environment: "production",
            Owner: "backend-team",
            EndpointUrl: null,
            BusinessCriticality: "Medium"));

        var created = await createResponse.Content.ReadFromJsonAsync<CreateIntegrationResponse>();
        return (created!.IntegrationId, created.ApiKey);
    }

    private async Task<Guid> IngestEventAsync(HttpClient client, string apiKey, Guid integrationId, int statusCode, object? response = null, string? customerRef = null, string? operationRef = null)
    {
        var ingestResponse = await client.PostAsJsonAsync("/api/v1/events",
            new IngestEventRequest(integrationId, "ApiCall", statusCode, null, response, 100, DateTimeOffset.UtcNow, customerRef, operationRef, operationRef is null ? null : "PaymentSync"));
        ingestResponse.EnsureSuccessStatusCode();
        var body = await ingestResponse.Content.ReadFromJsonAsync<IngestEventResponse>();
        return body!.EventId;
    }

    private async Task RunClassifyAsync(Guid eventId, Guid correlationId)
    {
        using var scope = _factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ClassifyJobHandler>();
        await handler.ExecuteAsync(eventId, correlationId);
    }

    [Fact]
    public async Task RepeatedEvent_UpdatesExistingIncident_DoesNotCreateNew()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        for (var i = 0; i < 5; i++)
        {
            var eventId = await IngestEventAsync(client, apiKey, integrationId, 401);
            await RunClassifyAsync(eventId, Guid.NewGuid());
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var incidents = await db.Incidents.Where(i => i.IntegrationId == integrationId).ToListAsync();

        incidents.Should().HaveCount(1);
        incidents[0].EventCount.Should().Be(5);
    }

    /// <summary>
    /// TD3 doğrulaması: ClassifyJobHandler'ın her sınıflandırılan event'e set ettiği IncidentId
    /// FK'sının, o event'i gerçekten doğru incident'a bağladığını doğrudan kontrol eder (bir
    /// zaman-penceresi tahmini değil, gerçek bir ilişki).
    /// </summary>
    [Fact]
    public async Task ClassifiedEvents_AreAssignedToTheirIncidentViaForeignKey()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var eventIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var eventId = await IngestEventAsync(client, apiKey, integrationId, 401);
            await RunClassifyAsync(eventId, Guid.NewGuid());
            eventIds.Add(eventId);
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var incident = await db.Incidents.SingleAsync(i => i.IntegrationId == integrationId);
        var events = await db.IntegrationEvents.Where(e => eventIds.Contains(e.Id)).ToListAsync();

        events.Should().HaveCount(3);
        events.Should().OnlyContain(e => e.IncidentId == incident.Id);
    }

    /// <summary>
    /// TD3 doğrulaması: affected-customer sayısının artık IncidentId FK'sına göre doğru
    /// hesaplandığını (M18'de yalnızca canlı doğrulanmış, otomatik test edilmemiş bir davranışı)
    /// doğrular.
    /// </summary>
    [Fact]
    public async Task AffectedCustomerCount_ReflectsOnlyEventsAssignedToThisIncident()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var e1 = await IngestEventAsync(client, apiKey, integrationId, 401, customerRef: "cus_a");
        await RunClassifyAsync(e1, Guid.NewGuid());
        var e2 = await IngestEventAsync(client, apiKey, integrationId, 401, customerRef: "cus_b");
        await RunClassifyAsync(e2, Guid.NewGuid());
        var e3 = await IngestEventAsync(client, apiKey, integrationId, 401, customerRef: "cus_a");
        await RunClassifyAsync(e3, Guid.NewGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var incident = await db.Incidents.SingleAsync(i => i.IntegrationId == integrationId);

        var response = await client.GetAsync($"/api/v1/incidents/{incident.Id}");
        var body = await response.Content.ReadFromJsonAsync<FI.Application.Incidents.IncidentDetailResponse>();

        body!.AffectedCustomerCount.Should().Be(2);
    }

    /// <summary>
    /// M19 P0-A doğrulaması (bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md, Preflight Constraint
    /// 1): 5 event'ten yalnızca 3'ü OperationRef taşıyorsa, KnownOperationCount TOPLAM etki gibi
    /// sunulamaz - "Partial" coverage ile birlikte yalnızca bilinen alt küme (2 distinct operasyon)
    /// dönmeli.
    /// </summary>
    [Fact]
    public async Task OperationCoverage_WhenSomeEventsLackOperationRef_ReportsPartial()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var e1 = await IngestEventAsync(client, apiKey, integrationId, 401, operationRef: "payment-sync-1");
        await RunClassifyAsync(e1, Guid.NewGuid());
        var e2 = await IngestEventAsync(client, apiKey, integrationId, 401, operationRef: "payment-sync-1");
        await RunClassifyAsync(e2, Guid.NewGuid());
        var e3 = await IngestEventAsync(client, apiKey, integrationId, 401, operationRef: "payment-sync-2");
        await RunClassifyAsync(e3, Guid.NewGuid());
        var e4 = await IngestEventAsync(client, apiKey, integrationId, 401); // no operationRef
        await RunClassifyAsync(e4, Guid.NewGuid());
        var e5 = await IngestEventAsync(client, apiKey, integrationId, 401); // no operationRef
        await RunClassifyAsync(e5, Guid.NewGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var incident = await db.Incidents.SingleAsync(i => i.IntegrationId == integrationId);

        var response = await client.GetAsync($"/api/v1/incidents/{incident.Id}");
        var body = await response.Content.ReadFromJsonAsync<FI.Application.Incidents.IncidentDetailResponse>();

        body!.EventCount.Should().Be(5);
        body.KnownOperationCount.Should().Be(2, "yalnızca 2 distinct OperationRef biliniyor (payment-sync-1, payment-sync-2)");
        body.OperationCoverage.Should().Be("Partial");
    }

    [Fact]
    public async Task OperationCoverage_WhenNoEventsHaveOperationRef_ReportsNoneAndNullCount()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var e1 = await IngestEventAsync(client, apiKey, integrationId, 401);
        await RunClassifyAsync(e1, Guid.NewGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var incident = await db.Incidents.SingleAsync(i => i.IntegrationId == integrationId);

        var response = await client.GetAsync($"/api/v1/incidents/{incident.Id}");
        var body = await response.Content.ReadFromJsonAsync<FI.Application.Incidents.IncidentDetailResponse>();

        body!.KnownOperationCount.Should().BeNull("hiçbir event OperationRef taşımıyorsa 'bilinmiyor' anlamına gelmeli, 'sıfır' değil");
        body.OperationCoverage.Should().Be("None");
    }

    [Fact]
    public async Task OperationCoverage_WhenAllEventsHaveOperationRef_ReportsComplete()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var e1 = await IngestEventAsync(client, apiKey, integrationId, 401, operationRef: "payment-sync-1");
        await RunClassifyAsync(e1, Guid.NewGuid());
        var e2 = await IngestEventAsync(client, apiKey, integrationId, 401, operationRef: "payment-sync-2");
        await RunClassifyAsync(e2, Guid.NewGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var incident = await db.Incidents.SingleAsync(i => i.IntegrationId == integrationId);

        var response = await client.GetAsync($"/api/v1/incidents/{incident.Id}");
        var body = await response.Content.ReadFromJsonAsync<FI.Application.Incidents.IncidentDetailResponse>();

        body!.KnownOperationCount.Should().Be(2);
        body.OperationCoverage.Should().Be("Complete");
    }

    [Fact]
    public async Task DifferentRootCauses_ProduceSeparateIncidents()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        for (var i = 0; i < 3; i++)
        {
            var eventId = await IngestEventAsync(client, apiKey, integrationId, 401);
            await RunClassifyAsync(eventId, Guid.NewGuid());
        }

        for (var i = 0; i < 4; i++)
        {
            var eventId = await IngestEventAsync(client, apiKey, integrationId, 503);
            await RunClassifyAsync(eventId, Guid.NewGuid());
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var incidents = await db.Incidents.Where(i => i.IntegrationId == integrationId).OrderBy(i => i.Category).ToListAsync();

        incidents.Should().HaveCount(2);
        incidents.Sum(i => i.EventCount).Should().Be(7);
    }

    [Fact]
    public async Task StatusCode401_ClassifiesEventAsAuthenticationError()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var eventId = await IngestEventAsync(client, apiKey, integrationId, 401);
        await RunClassifyAsync(eventId, Guid.NewGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var evt = await db.IntegrationEvents.FirstAsync(e => e.Id == eventId);

        evt.Category.Should().Be("AuthenticationError");
    }

    [Fact]
    public async Task StatusCode500_ClassifiesEventAsProviderError()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var eventId = await IngestEventAsync(client, apiKey, integrationId, 500);
        await RunClassifyAsync(eventId, Guid.NewGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var evt = await db.IntegrationEvents.FirstAsync(e => e.Id == eventId);

        evt.Category.Should().Be("ProviderError");
    }

    /// <summary>
    /// DueDiligence D1 regresyon testi: ClassifyJobHandler'daki count10/15/30 sorguları,
    /// evt.SetCategory(...) sadece bellekte set edildikten ve SaveChangesAsync'ten ÖNCE
    /// çalıştığından, şu an sınıflandırılan event DB'de hâlâ eski (null) category ile duruyor
    /// ve sorgudan kendi kendini dışlıyordu (off-by-one). Düzeltme, güncel event'i düştüğü her
    /// pencereye elle +1 ekliyor. Severity Medium eşiği (30dk'da >=5) RateLimitError için
    /// Critical/High-eligible olmadığından temiz bir gözlem noktası: düzeltmeden önce bu test
    /// "Low" (count30=4) buluyordu, düzeltmeden sonra doğru şekilde "Medium" (count30=5) buluyor.
    /// </summary>
    [Fact]
    public async Task FifthRateLimitEventInWindow_ReachesMediumSeverity_IncludingTriggeringEventItself()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        Guid lastEventId = Guid.Empty;
        for (var i = 0; i < 5; i++)
        {
            lastEventId = await IngestEventAsync(client, apiKey, integrationId, 429);
            await RunClassifyAsync(lastEventId, Guid.NewGuid());
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var incident = await db.Incidents.FirstAsync(i => i.IntegrationId == integrationId);

        incident.EventCount.Should().Be(5, "EventCount tüm 5 event'i doğru sayıyor (bu alan RecordNewEvent ile artırılıyor, count sorgusuyla değil)");
        incident.Severity.ToString().Should().Be("Medium",
            "D1 düzeltmesi: severity hesaplaması artık henüz DB'ye yazılmamış (SaveChanges öncesi) 5. event'i de sayıyor, count30=5 ile Medium eşiği (>=5) doğru tetikleniyor");
    }

    [Fact]
    public async Task IngestedEvent_AlwaysWritesOutboxMessageInSameTransaction()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var eventId = await IngestEventAsync(client, apiKey, integrationId, 401);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var messages = await db.OutboxMessages.ToListAsync();
        var outboxExists = messages.Any(m => m.Payload.Contains(eventId.ToString()));

        outboxExists.Should().BeTrue();
    }
}
