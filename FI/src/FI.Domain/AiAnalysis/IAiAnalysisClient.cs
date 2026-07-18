namespace FI.Domain.AiAnalysis;

/// <summary>
/// Bkz. Bolum 24.3 - provider-agnostic arayuz. Infrastructure katmani tek adaptorle
/// (AnthropicMessagesClient) implemente eder; provider degisimi bu interface'i etkilemez.
/// </summary>
public interface IAiAnalysisClient
{
    Task<AiAnalysisRawResponse> AnalyzeAsync(
        string systemPrompt,
        string userPayloadJson,
        string modelId,
        CancellationToken cancellationToken);
}

/// <summary>Ham HTTP/SDK cagri sonucu - parse islemi ayri bir katmanda (AiAnalysisValidator) yapilir.</summary>
public sealed record AiAnalysisRawResponse(
    bool CallSucceeded,
    string? ResponseText,
    int? InputTokens,
    int? OutputTokens,
    long LatencyMs,
    string? ErrorMessage);
