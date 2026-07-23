using Backend.Shared;

namespace Backend.Modules.PageContext;

public class CreatePageContextRequest
{
    public Guid SocialChannelId { get; set; }
    public string BrandName { get; set; } = string.Empty;
    public string? ToneOfVoice { get; set; }
    public Guid? LogoMediaId { get; set; }
    public string? CtaText { get; set; }
    public string? CtaUrl { get; set; }
    public string? Hotline { get; set; }
    public string? Website { get; set; }
    public string? BrandColors { get; set; }
    public string? DefaultHashtags { get; set; }
    public string? PromptTemplateText { get; set; }
    public string? PromptTemplateImage { get; set; }
    public Guid? DefaultTextTemplateId { get; set; }
    public Guid? DefaultImageTemplateId { get; set; }
}

/// <summary>1 dòng import PageContext. Có thể trỏ kênh bằng SocialChannelId hoặc ChannelName (tên page).</summary>
public class PageContextImportItem : CreatePageContextRequest
{
    /// <summary>Tên page để resolve kênh khi không có SocialChannelId.</summary>
    public string? ChannelName { get; set; }
}

public class PageContextImportRequest
{
    public List<PageContextImportItem> Items { get; set; } = [];
}

public class PageContextImportResult
{
    public int Created { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; set; } = [];
}

public class UpdatePageContextRequest
{
    public string? BrandName { get; set; }
    public string? ToneOfVoice { get; set; }
    public Guid? LogoMediaId { get; set; }
    public string? CtaText { get; set; }
    public string? CtaUrl { get; set; }
    public string? Hotline { get; set; }
    public string? Website { get; set; }
    public string? BrandColors { get; set; }
    public string? DefaultHashtags { get; set; }
    public string? PromptTemplateText { get; set; }
    public string? PromptTemplateImage { get; set; }
    public Guid? DefaultTextTemplateId { get; set; }
    public Guid? DefaultImageTemplateId { get; set; }
}

public class PageContextFilterRequest : PagedFilterRequest
{
    public Guid? SocialChannelId { get; set; }
}

public class PageContextResponse
{
    public Guid Id { get; set; }
    public Guid SocialChannelId { get; set; }
    public string BrandName { get; set; } = string.Empty;
    public string? ToneOfVoice { get; set; }
    public Guid? LogoMediaId { get; set; }
    public string? CtaText { get; set; }
    public string? CtaUrl { get; set; }
    public string? Hotline { get; set; }
    public string? Website { get; set; }
    public string? BrandColors { get; set; }
    public string? DefaultHashtags { get; set; }
    public string? PromptTemplateText { get; set; }
    public string? PromptTemplateImage { get; set; }
    public Guid? DefaultTextTemplateId { get; set; }
    public Guid? DefaultImageTemplateId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
