namespace Backend.Shared.Ai;

public class AiImageProvidersOptions
{
    public string DefaultProvider { get; set; } = "gemini";
    public Dictionary<string, AiImageProviderConfig> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class AiImageProviderConfig
{
    /// <summary>API style. Currently supported: "gemini-generatecontent".</summary>
    public string Api { get; set; } = "gemini-generatecontent";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public string ApiKey { get; set; } = string.Empty;
    public string DefaultImageModel { get; set; } = "gemini-2.5-flash-image";

    /// <summary>Path template appended to BaseUrl; "{model}" is replaced at call time.</summary>
    public string GenerateContentPath { get; set; } = "/models/{model}:generateContent";

    /// <summary>
    /// Gemini generationConfig.responseModalities. Image-only models accept ["IMAGE"];
    /// some models require ["TEXT","IMAGE"] — override here without code changes if needed.
    /// </summary>
    public List<string> ResponseModalities { get; set; } = ["IMAGE"];

    public int TimeoutSeconds { get; set; } = 60;
}
