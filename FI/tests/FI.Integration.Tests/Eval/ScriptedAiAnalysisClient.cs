using System.Text.Json;
using FI.Domain.AiAnalysis;
using FI.Domain.AiAnalysis.Eval;

namespace FI.Integration.Tests.Eval;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 26.4. Gerçek bir model DEĞİLDİR —
/// golden dataset'teki her senaryo için "ideal davranışı" (doğru echo, evidence'a sadık kök
/// neden, beklenen confidence/needsHumanReview aralığı) üreten scripted bir test double'dır.
/// Amacı Claude'u değerlendirmek değil, <see cref="EvalHarness"/>'in kendisinin (puanlama +
/// eşik kararı) doğru çalıştığını CI'da ağdan/API maliyetinden bağımsız, deterministik şekilde
/// kanıtlamaktır. Gerçek model kalitesi değerlendirmesi için aynı harness, gerçek
/// <c>AnthropicMessagesClient</c> ile manuel çalıştırılabilir (bkz. README).
/// </summary>
public sealed class ScriptedAiAnalysisClient : IAiAnalysisClient
{
    private readonly IReadOnlyDictionary<string, GoldenScenario> _scenariosById;

    /// <summary>Belirli bir senaryo id'si için davranışı override eder (kötü-davranış testleri için).</summary>
    public string? OverrideScenarioId { get; set; }
    public AiAnalysisRawResponse? OverrideResponse { get; set; }

    public ScriptedAiAnalysisClient(IReadOnlyList<GoldenScenario> scenarios)
    {
        _scenariosById = scenarios.ToDictionary(s => s.Id);
    }

    public Task<AiAnalysisRawResponse> AnalyzeAsync(string systemPrompt, string userPayloadJson, string modelId, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(userPayloadJson);
        var incidentId = doc.RootElement.GetProperty("incidentId").GetString()!;

        if (incidentId == OverrideScenarioId && OverrideResponse is not null)
            return Task.FromResult(OverrideResponse);

        var scenario = _scenariosById[incidentId];
        var det = scenario.Deterministic;
        var expectation = scenario.Expectation;

        var rootCause = string.Join(" ", scenario.Evidence.Select(e => e.Summary));
        var confidence = (expectation.MinConfidence + expectation.MaxConfidence) / 2;

        var response = new
        {
            schemaVersion = "1.0",
            incidentTitle = $"{det.Category} on {det.AffectedIntegration}",
            category = det.Category,
            severity = det.Severity,
            affectedIntegration = det.AffectedIntegration,
            affectedRequests = det.AffectedRequests,
            probableRootCause = rootCause,
            evidence = scenario.Evidence.Select(e => e.Summary).ToArray(),
            evidenceRefs = scenario.Evidence.Select(e => e.SourceType).Distinct().ToArray(),
            recommendedActions = new[] { "Review the identified evidence", "Confirm with the integration owner" },
            confidence,
            needsHumanReview = expectation.ExpectNeedsHumanReview,
            outOfEvidenceClaimsDetected = false
        };

        return Task.FromResult(new AiAnalysisRawResponse(true, JsonSerializer.Serialize(response), 120, 60, 15, null));
    }
}
