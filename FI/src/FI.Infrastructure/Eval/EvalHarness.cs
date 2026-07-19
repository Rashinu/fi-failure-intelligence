using System.Text.Json;
using FI.Domain.AiAnalysis;
using FI.Domain.AiAnalysis.Eval;
using FI.Domain.Redaction;

namespace FI.Infrastructure.Eval;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 26.4. <see cref="AiAnalysisJobHandler"/>
/// ile aynı input-payload şeklini üretir (evidence-only contract, Bölüm 25.1) ama bir incident'a
/// değil, sabit bir golden senaryoya karşı çalışır — DB'ye dokunmaz. <see cref="IAiAnalysisClient"/>
/// gerçek <c>AnthropicMessagesClient</c> (canlı, gerçek maliyet/latency ölçümü için — Bölüm 49,
/// Open Decision 1) veya deterministik bir test double olabilir; harness ikisi için de aynıdır.
/// </summary>
public static class EvalHarness
{
    public static async Task<EvalReport> RunAsync(
        IAiAnalysisClient client,
        string systemPrompt,
        string modelId,
        IReadOnlyList<GoldenScenario> scenarios,
        CancellationToken cancellationToken = default)
    {
        var scores = new List<ScenarioScore>();

        foreach (var scenario in scenarios)
        {
            // Bkz. Bolum 33.3 - Asama B: AiAnalysisJobHandler ile birebir aynı ikinci redaction
            // pass'i, harness'i gercek uretim davranışıyla tutarlı tutmak icin burada da uygulanır.
            var redactedEvidence = scenario.Evidence
                .Select(e => new EvidenceInput(e.SourceType, PayloadRedactor.RedactText(e.Summary) ?? e.Summary, e.CollectedAt))
                .ToList();

            var userPayload = BuildUserPayload(scenario, redactedEvidence);
            var rawResponse = await client.AnalyzeAsync(systemPrompt, userPayload, modelId, cancellationToken);

            if (!rawResponse.CallSucceeded)
            {
                scores.Add(new ScenarioScore(scenario.Id, 0, 0, 0, 0, 0, 0, FormatCompliance: 0));
                continue;
            }

            var (validation, output) = AiAnalysisValidator.Validate(
                rawResponse.ResponseText, scenario.Deterministic, redactedEvidence);

            scores.Add(RubricScorer.Score(scenario, validation, output));
        }

        return new EvalReport(scores);
    }

    private static string BuildUserPayload(GoldenScenario scenario, IReadOnlyList<EvidenceInput> evidence)
    {
        var det = scenario.Deterministic;
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            incidentId = scenario.Id,
            deterministicClassification = new
            {
                category = det.Category,
                severity = det.Severity,
                affectedIntegration = det.AffectedIntegration,
                affectedRequests = det.AffectedRequests,
                firstSeenAt = det.FirstSeenAt,
                lastSeenAt = det.LastSeenAt,
                statusCodeSample = det.StatusCodeSample,
                errorSignature = det.ErrorSignature
            },
            evidence = evidence.Select(e => new { sourceType = e.SourceType, summary = e.Summary, collectedAt = e.CollectedAt }),
            constraints = new { maxEvidenceItems = 10, evidenceOnly = true }
        });
    }
}
