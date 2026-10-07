namespace Backend.Modules.Crm.Tags;

public class CrmTagResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateCrmTagRequest
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#607D8B";
}

public class UpdateCrmTagRequest
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#607D8B";
}

public class CrmTagTargetRequest
{
    public CrmTagTargetType TargetType { get; set; }
    public Guid TargetId { get; set; }
}
