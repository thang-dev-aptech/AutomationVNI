namespace Backend.Shared.Ai;

public class AiTextGenerationRequest
{
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? Title { get; set; }
    public string? Objective { get; set; }
    public string? Category { get; set; }
    public string? Audience { get; set; }
    public string? Tone { get; set; }
    public string? BrandContext { get; set; }
    public string? CtaText { get; set; }
    public string? Hashtags { get; set; }

    /// <summary>
    /// Prompt người dùng đã dựng sẵn từ template (đã thay biến). Khi có giá trị, service dùng
    /// thẳng làm user prompt thay cho phần ghép mặc định. System prompt (JSON schema) vẫn giữ nguyên
    /// để đảm bảo output parse được.
    /// </summary>
    public string? PromptOverride { get; set; }
}

public class AiTextGenerationResult
{
    public string Caption { get; set; } = string.Empty;
    public List<string> Hashtags { get; set; } = [];
    public string Cta { get; set; } = string.Empty;
    public string BannerHeadline { get; set; } = string.Empty;
    public string BannerSubheadline { get; set; } = string.Empty;
    public string BannerCta { get; set; } = string.Empty;
    public string ImagePrompt { get; set; } = string.Empty;
    public string? RawResponse { get; set; }
}

public class AiTestTextGenerationResponse
{
    public bool ProviderAvailable { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? Message { get; set; }
    public AiTextGenerationResult? Result { get; set; }
}
