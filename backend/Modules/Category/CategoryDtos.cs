using Backend.Shared;

namespace Backend.Modules.Category;

public class CreateCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentCategoryId { get; set; }
}

public class UpdateCategoryRequest
{
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public Guid? ParentCategoryId { get; set; }
}

public class CategoryFilterRequest : PagedFilterRequest
{
    public Guid? ParentCategoryId { get; set; }
}

/// <summary>Import nhanh nhiều loại bài — mỗi tên 1 dòng; slug tự sinh, trùng thì bỏ qua.</summary>
public class CategoryImportRequest
{
    public List<string> Names { get; set; } = [];
    public Guid? ParentCategoryId { get; set; }
}

public class CategoryImportResult
{
    public int Created { get; set; }
    public int Skipped { get; set; }
    public List<CategoryResponse> Items { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}

public class CategoryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public DateTime CreatedAt { get; set; }
}
