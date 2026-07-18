namespace FI.Domain.Incidents;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bolum 16.6, Bolum 23.
/// CONFIG_CHANGE, gercek config-degisiklik audit gunlugu kuruluncaya kadar M4'te uretilmiyor
/// (bkz. README M4 notu) - taksonomide yer aliyor ama collector henuz bu kaynagi doldurmuyor.
/// </summary>
public enum EvidenceSourceType
{
    Deployment,
    ConfigChange,
    PreviousEvent,
    HistoricalIncident,
    ManualNote
}
