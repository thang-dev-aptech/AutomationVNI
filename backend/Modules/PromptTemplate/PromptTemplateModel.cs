using Backend.Modules.PromptTemplate.Enums;
using Backend.Shared;

namespace Backend.Modules.PromptTemplate;

/// <summary>
/// Reusable prompt template (thư viện). Body chứa biến động dạng {{title}}, {{brand}}...
/// được thay thế lúc sinh nội dung. Một template TextContent và một Image có thể được đánh dấu
/// IsDefault để dùng khi Post/PageContext không chỉ định template cụ thể.
/// </summary>
public class PromptTemplateModel : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PromptTemplateType TemplateType { get; set; } = PromptTemplateType.TextContent;

    /// <summary>Nội dung prompt, chứa placeholder {{variable}}.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Template mặc định cho loại của nó (chỉ một mặc định mỗi loại).</summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;
}
