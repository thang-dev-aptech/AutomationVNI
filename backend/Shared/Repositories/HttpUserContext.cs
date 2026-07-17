using System.Security.Claims;

namespace Backend.Shared.Repositories;

public class HttpUserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    public Guid? GetCurrentUserId()
    {
        var userId = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userId, out var id) ? id : null;
    }

    public string? GetCurrentUserName()
    {
        return httpContextAccessor.HttpContext?.User?.Identity?.Name
            ?? httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name)
            ?? httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email);
    }

    public IReadOnlyList<string> GetCurrentUserRoles()
    {
        return httpContextAccessor.HttpContext?.User?
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList() ?? [];
    }
}
