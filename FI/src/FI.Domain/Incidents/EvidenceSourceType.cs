namespace FI.Domain.Incidents;

/// <summary>Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bolum 16.6, Bolum 23.</summary>
public enum EvidenceSourceType
{
    Deployment,
    ConfigChange,
    PreviousEvent,
    HistoricalIncident,
    ManualNote
}
