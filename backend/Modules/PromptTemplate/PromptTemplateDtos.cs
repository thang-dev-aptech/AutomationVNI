using Backend.Modules.PromptTemplate.Enums;
using Backend.Shared;

namespace Backend.Modules.PromptTemplate;

public class CreatePromptTemplateRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PromptTemplateType TemplateType { get; set; } = PromptTemplateType.TextContent;
    public string Body { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdatePromptTemplateRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Body { get; set; }
    public bool? IsDefault { get; set; }
    public bool? IsActive { get; set; }
}

public class PromptTemplateFilterRequest : PagedFilterRequest
{
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
    public string Body { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
