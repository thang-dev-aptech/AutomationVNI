namespace Backend.Modules.Crm.Assignment;

public class CrmAutoAssignSettingsResponse
{
    public bool IsEnabled { get; set; }
    public IReadOnlyList<Guid> AssigneeUserIds { get; set; } = [];
    public int NextIndex { get; set; }
}

public class UpdateCrmAutoAssignSettingsRequest
{
    public bool IsEnabled { get; set; }
    public List<Guid> AssigneeUserIds { get; set; } = [];
}
