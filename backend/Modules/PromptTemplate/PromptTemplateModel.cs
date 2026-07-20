using Backend.Modules.PromptTemplate.Enums;
using Backend.Shared;

namespace Backend.Modules.PromptTemplate;

/// <summary>
/// Template theo danh mục: một dòng cấu hình cả prompt text và prompt ảnh.
/// Name = tên danh mục (VD: Bán hàng). Legacy rows may still use TemplateType Text/Image + Body.
/// </summary>
public class PromptTemplateModel : BaseEntity
{
    /// <summary>Tên danh mục hiển thị (VD: Bán hàng).</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public PromptTemplateType TemplateType { get; set; } = PromptTemplateType.Category;

    /// <summary>Legacy single body (TextContent / Image cũ).</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Prompt sinh nội dung text (placeholder {{title}}, {{category}}...).</summary>
    public string TextBody { get; set; } = string.Empty;

    /// <summary>Prompt sinh ảnh.</summary>
    public string ImageBody { get; set; } = string.Empty;

    /// <summary>Template mặc định khi bài không chọn danh mục (chỉ một default Category).</summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public string ResolveTextBody()
    {
        if (!string.IsNullOrWhiteSpace(TextBody)) return TextBody;
        if (TemplateType == PromptTemplateType.TextContent && !string.IsNullOrWhiteSpace(Body))
            return Body;
        return string.Empty;
    }

    public string ResolveImageBody()
    {
        if (!string.IsNullOrWhiteSpace(ImageBody)) return ImageBody;
        if (TemplateType == PromptTemplateType.Image && !string.IsNullOrWhiteSpace(Body))
            return Body;
        return string.Empty;
    }
}
