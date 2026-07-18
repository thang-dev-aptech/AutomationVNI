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
    public string? DefaultHashtags { get; set; }
    public string? PromptTemplateText { get; set; }
    public string? PromptTemplateImage { get; set; }
    public Guid? DefaultTextTemplateId { get; set; }
    public Guid? DefaultImageTemplateId { get; set; }
}

public class UpdatePageContextRequest
{
    public string? BrandName { get; set; }
    public string? ToneOfVoice { get; set; }
    public Guid? LogoMediaId { get; set; }
    public string? CtaText { get; set; }
    public string? CtaUrl { get; set; }
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
    public string? DefaultHashtags { get; set; }
    public string? PromptTemplateText { get; set; }
    public string? PromptTemplateImage { get; set; }
    public Guid? DefaultTextTemplateId { get; set; }
    public Guid? DefaultImageTemplateId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
