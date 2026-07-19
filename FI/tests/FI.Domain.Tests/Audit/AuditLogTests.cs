using FI.Domain.Audit;
using FluentAssertions;

namespace FI.Domain.Tests.Audit;

public class AuditLogTests
{
    [Fact]
    public void Create_WithValidData_SetsExpectedFields()
    {
        var entityId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var log = AuditLog.Create(
            AuditActorType.User, actorId: null, AuditActions.ApiKeyRotated, AuditEntityTypes.Integration,
            entityId, correlationId, changes: """{"newKeyPrefix":"fi_live_ab12"}""");

        log.Id.Should().NotBeEmpty();
        log.ActorType.Should().Be(AuditActorType.User);
        log.Action.Should().Be(AuditActions.ApiKeyRotated);
        log.EntityType.Should().Be(AuditEntityTypes.Integration);
        log.EntityId.Should().Be(entityId);
        log.CorrelationId.Should().Be(correlationId);
        log.Changes.Should().Contain("fi_live_ab12");
    }

    [Theory]
    [InlineData("", "Integration")]
    [InlineData(null, "Integration")]
    [InlineData("ACTION", "")]
    [InlineData("ACTION", null)]
    public void Create_WithMissingRequiredField_Throws(string? action, string? entityType)
    {
        var act = () => AuditLog.Create(AuditActorType.System, null, action!, entityType!, null, null, null);

        act.Should().Throw<ArgumentException>();
    }
}
