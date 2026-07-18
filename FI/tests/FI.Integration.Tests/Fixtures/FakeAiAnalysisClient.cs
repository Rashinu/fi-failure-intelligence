using System.Text.Json;
using FI.Domain.AiAnalysis;

namespace FI.Integration.Tests.Fixtures;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bolum 38.1 - "E2E: ... Testcontainers + IAiClient
/// test double". Gercek Anthropic API cagrisi yerine, deterministik testlerde kullanilan sahte
/// istemci. Varsayilan davranis: girdideki deterministicClassification alanlarini dogru echo eden,
/// evidence'tan turetilmis (uydurulmamis) bir cevap uretir. Testler NextResponseOverride ile
/// belirli senaryolari (parse hatasi, echo uyumsuzlugu, dusuk confidence) simule edebilir.
/// </summary>
public class FakeAiAnalysisClient : IAiAnalysisClient
{
    public string? NextResponseOverride { get; set; }
    public bool SimulateCallFailure { get; set; }

    public Task<AiAnalysisRawResponse> AnalyzeAsync(
        string systemPrompt, string userPayloadJson, string modelId, CancellationToken cancellationToken)
    {
        if (SimulateCallFailure)
        {
            return Task.FromResult(new AiAnalysisRawResponse(false, null, null, null, 10, "Simulated failure"));
        }

        if (NextResponseOverride is not null)
        {
            return Task.FromResult(new AiAnalysisRawResponse(true, NextResponseOverride, 100, 50, 10, null));
        }

        using var doc = JsonDocument.Parse(userPayloadJson);
        var det = doc.RootElement.GetProperty("deterministicClassification");
        var evidence = doc.RootElement.GetProperty("evidence");

        var evidenceSummaries = evidence.EnumerateArray().Select(e => e.GetProperty("summary").GetString()).ToList();
        var evidenceRefs = evidence.EnumerateArray().Select(e => e.GetProperty("sourceType").GetString()).Distinct().ToList();

        var response = new
        {
            schemaVersion = "1.0",
            incidentTitle = $"{det.GetProperty("category").GetString()} on {det.GetProperty("affectedIntegration").GetString()}",
            category = det.GetProperty("category").GetString(),
            severity = det.GetProperty("severity").GetString(),
            affectedIntegration = det.GetProperty("affectedIntegration").GetString(),
            affectedRequests = det.GetProperty("affectedRequests").GetInt32(),
            // Kok neden metni, evidence ozetinin kendisiyle basliyor - grounding kontrolunun
            // evidence corpus'unda bulunmayan buyuk harfli bir kelimeyi (orn. "Based") yanlislikla
            // yakalamamasi icin ekstra sozcuk eklemiyoruz.
            probableRootCause = evidenceSummaries.Count > 0
                ? evidenceSummaries[0]
                : "insufficient evidence to determine root cause",
            evidence = evidenceSummaries,
            evidenceRefs,
            recommendedActions = new[] { "Verify the affected configuration", "Monitor for recurrence" },
            confidence = evidenceSummaries.Count > 0 ? 0.85 : 0.3,
            needsHumanReview = evidenceSummaries.Count == 0,
            outOfEvidenceClaimsDetected = false
        };

        return Task.FromResult(new AiAnalysisRawResponse(true, JsonSerializer.Serialize(response), 100, 50, 10, null));
    }
}
