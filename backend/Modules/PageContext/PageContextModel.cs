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

    /// <summary>Hotline in trên banner — model sinh ảnh phải render đúng nguyên văn.</summary>
    public string? Hotline { get; set; }

    /// <summary>Website in trên banner (vd https://vni.edu.vn/).</summary>
    public string? Website { get; set; }

    /// <summary>Bộ màu thương hiệu cho prompt ảnh, vd "#1565C0, #F59E0B, #22C55E".</summary>
    public string? BrandColors { get; set; }

    public string? DefaultHashtags { get; set; }
    public string? PromptTemplateText { get; set; }
    public string? PromptTemplateImage { get; set; }

    /// <summary>Template prompt mặc định của page (thư viện PromptTemplate). No FK.</summary>
    public Guid? DefaultTextTemplateId { get; set; }
    public Guid? DefaultImageTemplateId { get; set; }
}
