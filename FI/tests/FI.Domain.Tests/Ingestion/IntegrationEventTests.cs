using FI.Domain.Ingestion;
using FluentAssertions;
using Xunit;

namespace FI.Domain.Tests.Ingestion;

public class IntegrationEventTests
{
    [Theory]
    [InlineData(99)]
    [InlineData(600)]
    public void Create_WithOutOfRangeStatusCode_Throws(int statusCode)
    {
        var act = () => IntegrationEvent.Create(
            Guid.NewGuid(), IntegrationEventType.ApiCall, statusCode, null, null, null,
            Guid.NewGuid(), null, null, null, 0, false, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(100)]
    [InlineData(401)]
    [InlineData(599)]
    public void Create_WithValidStatusCode_Succeeds(int statusCode)
    {
        var evt = IntegrationEvent.Create(
            Guid.NewGuid(), IntegrationEventType.ApiCall, statusCode, null, null, null,
            Guid.NewGuid(), null, null, null, 0, false, DateTimeOffset.UtcNow);

        evt.StatusCode.Should().Be(statusCode);
        evt.Id.Should().NotBeEmpty();
        evt.ReceivedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Create_WithoutAffectedCustomerRef_DefaultsToNull()
    {
        var evt = IntegrationEvent.Create(
            Guid.NewGuid(), IntegrationEventType.ApiCall, 200, null, null, null,
            Guid.NewGuid(), null, null, null, 0, false, DateTimeOffset.UtcNow);

        evt.AffectedCustomerRef.Should().BeNull();
    }

    [Fact]
    public void Create_WithAffectedCustomerRef_SetsIt()
    {
        var evt = IntegrationEvent.Create(
            Guid.NewGuid(), IntegrationEventType.WebhookIn, 401, null, null, null,
            Guid.NewGuid(), null, null, null, 0, false, DateTimeOffset.UtcNow,
            affectedCustomerRef: "cus_demo_a");

        evt.AffectedCustomerRef.Should().Be("cus_demo_a");
    }

    [Fact]
    public void Create_DefaultsIncidentIdToNull()
    {
        var evt = IntegrationEvent.Create(
            Guid.NewGuid(), IntegrationEventType.ApiCall, 200, null, null, null,
            Guid.NewGuid(), null, null, null, 0, false, DateTimeOffset.UtcNow);

        evt.IncidentId.Should().BeNull();
    }

    [Fact]
    public void AssignToIncident_SetsIncidentId()
    {
        var evt = IntegrationEvent.Create(
            Guid.NewGuid(), IntegrationEventType.ApiCall, 401, null, null, null,
            Guid.NewGuid(), null, null, null, 0, false, DateTimeOffset.UtcNow);
        var incidentId = Guid.NewGuid();

        evt.AssignToIncident(incidentId);

        evt.IncidentId.Should().Be(incidentId);
    }

    // --- M19 P0-A: Business Operation Identity (bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md) ---

    [Fact]
    public void Create_WithoutOperationFields_DefaultToNull()
    {
        var evt = IntegrationEvent.Create(
            Guid.NewGuid(), IntegrationEventType.ApiCall, 401, null, null, null,
            Guid.NewGuid(), null, null, null, 0, false, DateTimeOffset.UtcNow);

        evt.OperationRef.Should().BeNull();
        evt.OperationType.Should().BeNull();
        evt.BusinessRecordRef.Should().BeNull();
    }

    [Fact]
    public void Create_WithOperationFields_SetsThem()
    {
        var evt = IntegrationEvent.Create(
            Guid.NewGuid(), IntegrationEventType.WebhookIn, 401, null, null, null,
            Guid.NewGuid(), null, null, null, 0, false, DateTimeOffset.UtcNow,
            operationRef: "payment-sync-74921",
            operationType: "PaymentSync",
            businessRecordRef: "subscription-18372");

        evt.OperationRef.Should().Be("payment-sync-74921");
        evt.OperationType.Should().Be("PaymentSync");
        evt.BusinessRecordRef.Should().Be("subscription-18372");
    }
}
