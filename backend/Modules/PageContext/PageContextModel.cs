using Backend.Shared;

namespace Backend.Modules.PageContext;

public class PageContextModel : BaseEntity
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

    /// <summary>Template prompt mặc định của page (thư viện PromptTemplate). No FK.</summary>
    public Guid? DefaultTextTemplateId { get; set; }
    public Guid? DefaultImageTemplateId { get; set; }
}
