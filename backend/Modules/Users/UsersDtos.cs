namespace Backend.Modules.Users;

/// <summary>
/// DTO an toàn cho CRM/inbox — chỉ id, tên hiển thị, vai trò, trạng thái.
/// Không mang email, phone, password hash, security stamp hay token.
/// </summary>
public class UserListItemResponse
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public IReadOnlyList<string> Roles { get; set; } = [];
    public bool IsActive { get; set; }
}
