using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FI.Domain.AiAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FI.Infrastructure.Ai;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bolum 24.3 (ADR-013). Semantic Kernel'in resmi,
/// stabil bir Anthropic konnektoru bulunmadigi icin (arastirma notu) dogrudan Anthropic Messages
/// API'sine (basit, iyi dokumante edilmis REST sozlesmesi) HttpClient ile baglaniyoruz - ayni
/// mimari niyeti (tek adaptor, IAiAnalysisClient arkasinda provider-agnostik) daha az bagimlilik
/// riskiyle karsiliyor. Provider degisimi yalnizca bu sinifi etkiler.
/// </summary>
public class AnthropicMessagesClient : IAiAnalysisClient
{
    private readonly HttpClient _httpClient;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicMessagesClient> _logger;

    public AnthropicMessagesClient(HttpClient httpClient, IOptions<AnthropicOptions> options, ILogger<AnthropicMessagesClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiAnalysisRawResponse> AnalyzeAsync(
        string systemPrompt, string userPayloadJson, string modelId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AnthropicApiKey))
        {
            return new AiAnalysisRawResponse(false, null, null, null, 0, "Ai:AnthropicApiKey yapılandırılmamış.");
        }

        var requestBody = new
        {
            model = modelId,
            max_tokens = _options.MaxOutputTokens,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userPayloadJson } }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", _options.AnthropicApiKey);
        request.Headers.Add("anthropic-version", _options.ApiVersion);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Anthropic API hata döndü: {StatusCode} {Body}", response.StatusCode, responseBody);
                return new AiAnalysisRawResponse(false, null, null, null, stopwatch.ElapsedMilliseconds, $"HTTP {(int)response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            var text = root.GetProperty("content")[0].GetProperty("text").GetString();
            int? inputTokens = root.TryGetProperty("usage", out var usage) && usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : null;
            int? outputTokens = usage.ValueKind != JsonValueKind.Undefined && usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : null;

            return new AiAnalysisRawResponse(true, text, inputTokens, outputTokens, stopwatch.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Anthropic API çağrısı başarısız oldu.");
            return new AiAnalysisRawResponse(false, null, null, null, stopwatch.ElapsedMilliseconds, ex.Message);
        }
    }
}
