using Backend.Modules.PromptTemplate.Enums;
using Backend.Shared;

namespace Backend.Modules.PromptTemplate;

public class CreatePromptTemplateRequest
{
    /// <summary>Tên danh mục.</summary>
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TextBody { get; set; } = string.Empty;
    public string ImageBody { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdatePromptTemplateRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? TextBody { get; set; }
    public string? ImageBody { get; set; }
    public bool? IsDefault { get; set; }
    public bool? IsActive { get; set; }
}

public class PromptTemplateFilterRequest : PagedFilterRequest
{
    /// <summary>Mặc định chỉ lấy gói danh mục (Category). Truyền Text/Image nếu cần bản legacy.</summary>
    public PromptTemplateType? TemplateType { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsDefault { get; set; }
}

public class PromptTemplateResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PromptTemplateType TemplateType { get; set; }
    public string TextBody { get; set; } = string.Empty;
    public string ImageBody { get; set; } = string.Empty;
    /// <summary>Legacy field — kept for older clients.</summary>
    public string Body { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BulkImportPromptTemplatesRequest
{
    public List<CreatePromptTemplateRequest> Items { get; set; } = [];
    /// <summary>true = cập nhật nếu trùng tên danh mục; false = bỏ qua trùng.</summary>
    public bool UpdateExisting { get; set; }
}

public class BulkImportPromptTemplatesResult
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; set; } = [];
    public string? Message { get; set; }
}
