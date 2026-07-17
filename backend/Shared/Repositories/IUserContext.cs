namespace Backend.Shared.Repositories;

public interface IUserContext
{
    Guid? GetCurrentUserId();
    string? GetCurrentUserName();
    IReadOnlyList<string> GetCurrentUserRoles();
}
