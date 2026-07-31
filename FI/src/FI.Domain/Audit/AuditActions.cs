namespace FI.Domain.Audit;

/// <summary>Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 16.11 örnek değerler.</summary>
public static class AuditActions
{
    public const string ApiKeyRotated = "API_KEY_ROTATED";
    public const string WebhookSecretRotated = "WEBHOOK_SECRET_ROTATED";
    public const string IntegrationUpdated = "INTEGRATION_UPDATED";

    /// <summary>Bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md P0-B.</summary>
    public const string IncidentResolved = "INCIDENT_RESOLVED";
}

public static class AuditEntityTypes
{
    public const string Integration = "Integration";
    public const string Incident = "Incident";
}
