namespace Backend.Modules.PromptTemplate.Enums;

/// <summary>
/// Category = 1 gói danh mục (TextBody + ImageBody).
/// TextContent / Image = bản cũ (một Body) — vẫn resolve được khi migrate.
/// </summary>
public enum PromptTemplateType
{
    Category = 0,
    TextContent = 1,
    Image = 2
}
