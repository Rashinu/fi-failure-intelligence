namespace FI.Infrastructure.Ai;

public class AnthropicOptions
{
    public const string SectionName = "Ai";

    public string? AnthropicApiKey { get; set; }
    public string DefaultModel { get; set; } = "claude-haiku-4-5";
    public string EscalatedModel { get; set; } = "claude-sonnet-4-5";
    public string ApiVersion { get; set; } = "2023-06-01";
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";
    public int MaxOutputTokens { get; set; } = 1024;
}
