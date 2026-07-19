namespace FI.Domain.Connectors;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 34. İmza doğrulaması ham (parse edilmemiş)
/// body byte'ları üzerinden yapılmalıdır; bu yüzden connector'lara model-bound bir DTO değil,
/// ham string body + header sözlüğü verilir.
/// </summary>
public sealed record RawInboundPayload(string RawBody, IReadOnlyDictionary<string, string> Headers)
{
    public string? Header(string name) =>
        Headers.TryGetValue(name, out var value) ? value : null;
}
