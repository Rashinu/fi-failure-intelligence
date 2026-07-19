namespace FI.Domain.Audit;

/// <summary>Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 16.11 örnek değerler.</summary>
public static class AuditActions
{
    public const string ApiKeyRotated = "API_KEY_ROTATED";
    public const string WebhookSecretRotated = "WEBHOOK_SECRET_ROTATED";
    public const string IntegrationUpdated = "INTEGRATION_UPDATED";
}

public static class AuditEntityTypes
{
    public const string Integration = "Integration";
}
