namespace FI.Domain.Incidents;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 16.5 (ADR: 04'ün 7 durumlu state machine'i
/// kanonik alındı — AI incident açmaz/kapatmaz ilkesi doğrudan bu duruma bağlı).
/// M3'te yalnızca Open/Reopened üretilir; Investigating/AiAnalyzed/NeedsHumanReview M4-M5'te devreye girer.
/// </summary>
public enum IncidentStatus
{
    Open,
    Investigating,
    AiAnalyzed,
    NeedsHumanReview,
    Resolved,
    Reopened,
    Ignored
}
