namespace Backend.Shared.Ai;

public class AiProvidersOptions
{
    public string DefaultProvider { get; set; } = "9router";
    public Dictionary<string, AiProviderConfig> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class AiProviderConfig
{
    public string Api { get; set; } = "openai-completions";
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ChatCompletionsPath { get; set; } = "/chat/completions";
    public string DefaultTextModel { get; set; } = string.Empty;
    /// <summary>Model vision cho phân tích ảnh media. Trống = dùng DefaultTextModel.</summary>
    public string DefaultVisionModel { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 8192;
    public double Temperature { get; set; } = 0.7;
    public int ContextWindow { get; set; } = 128000;
}
