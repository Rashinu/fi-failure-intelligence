namespace FI.Domain.Classification;

/// <summary>
/// Bölüm 21.1 — deterministik severity, kategoriden bağımsız ek kural. Pencere sayıları
/// (affectedRequests) çağıran katman (ClassifyJobHandler) tarafından DB'den sorgulanır.
/// </summary>
public static class SeverityCalculator
{
    private static readonly HashSet<EventCategory> CriticalEligibleCategories = new()
    {
        EventCategory.ProviderError, EventCategory.AuthenticationError
    };

    private static readonly HashSet<EventCategory> HighEligibleCategories = new()
    {
        EventCategory.SignatureError, EventCategory.SchemaMismatch
    };

    public static IncidentSeverity Calculate(
        EventCategory category,
        int affectedRequestsLast10Min,
        int affectedRequestsLast15Min,
        int affectedRequestsLast30Min,
        bool isBusinessCriticalityCritical)
    {
        if ((CriticalEligibleCategories.Contains(category) && affectedRequestsLast10Min >= 50) || isBusinessCriticalityCritical)
            return IncidentSeverity.Critical;

        if (affectedRequestsLast15Min >= 20 || HighEligibleCategories.Contains(category))
            return IncidentSeverity.High;

        if (affectedRequestsLast30Min >= 5)
            return IncidentSeverity.Medium;

        return IncidentSeverity.Low;
    }
}
