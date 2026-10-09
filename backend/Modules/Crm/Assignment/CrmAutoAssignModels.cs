using Backend.Shared;

namespace Backend.Modules.Crm.Assignment;

/// <summary>
/// Cấu hình tự chia hội thoại Messenger (singleton).
/// </summary>
public class CrmAutoAssignSettingsModel : BaseEntity
{
    public static readonly Guid SingletonId = Guid.Parse("c1a7e0b2-4d5f-4a8e-9b3c-1d2e3f4a5b6c");

    public bool IsEnabled { get; set; }

    /// <summary>JSON mảng Guid người nhận theo thứ tự round-robin.</summary>
    public string AssigneeUserIdsJson { get; set; } = "[]";

    /// <summary>Chỉ số người nhận tiếp theo trong danh sách (modulo sau mỗi lần gán thành công).</summary>
    public int NextIndex { get; set; }
}
