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
    /// Prompt người dùng đã dựng sẵn từ template (đã thay biến). Khi có giá trị, service gắn
    /// vào user prompt cùng context + ràng buộc chất lượng. System prompt (JSON schema) vẫn giữ.
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
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? RawResponse { get; set; }
}

/// <summary>
/// Yêu cầu LLM viết banner-prompt chi tiết TỪ caption đã sinh (bước ngầm trong image job).
/// Trả về prompt tiếng Anh giàu art-direction; nhận diện brand (logo/hotline/website) vẫn khóa
/// ở AppendBrandLock nên phần này chỉ lo bố cục + phong cách để ảnh mỗi lần một khác.
/// </summary>
public class AiImagePromptRequest
{
    public string? Provider { get; set; }
    public string? Model { get; set; }

    /// <summary>Caption / nội dung bài đã sinh — nguồn ngữ cảnh chính.</summary>
    public string? Caption { get; set; }
    public string? Title { get; set; }
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public string? BrandColors { get; set; }
    public string? Cta { get; set; }
    public string? BannerHeadline { get; set; }
    public string? BannerSubheadline { get; set; }
    public string? BannerCta { get; set; }

    /// <summary>Hotline/Website thật — LLM phải in đúng vào khối CTA (không bịa, không sửa).</summary>
    public string? Hotline { get; set; }
    public string? Website { get; set; }

    /// <summary>Danh sách nhãn tính năng (đúng, không lặp) để LLM không tự bịa/nhân đôi chip.</summary>
    public string? FeatureLabels { get; set; }

    /// <summary>Gợi ý phong cách/bố cục từ template ImageBody (đã render biến) — LLM tham chiếu, không copy nguyên.</summary>
    public string? StyleGuide { get; set; }

    /// <summary>Prompt ngắn AI text đã đề xuất — dùng làm gợi ý bổ sung.</summary>
    public string? ImagePromptHint { get; set; }

    /// <summary>Prompt/bố cục lần sinh trước — LLM phải đề xuất bố cục KHÁC để tránh ảnh na ná.</summary>
    public string? AvoidLayout { get; set; }

    /// <summary>Hướng sáng tạo bốc ngẫu nhiên (composition/typography/style/mood/hero) để mỗi lần một bố cục khác.</summary>
    public string? CreativeBrief { get; set; }

    /// <summary>Có logo tham chiếu kèm không — để nhắc chừa chỗ đặt logo, không tự vẽ.</summary>
    public bool HasLogoReference { get; set; }
}

public class SuggestIdeasRequest
{
    public string Topic { get; set; } = string.Empty;
    public int Count { get; set; } = 5;
    public string? Category { get; set; }
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
