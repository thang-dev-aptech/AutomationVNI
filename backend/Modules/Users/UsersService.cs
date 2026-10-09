using Backend.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Users;

public class UsersService(UserManager<ApplicationUser> userManager)
{
    /// <summary>Người dùng đang hoạt động (không bị khoá), DTO không lộ trường nhạy cảm.</summary>
    public async Task<IReadOnlyList<UserListItemResponse>> ListActiveAsync(CancellationToken ct = default)
    {
        // Lọc LockoutEnd phía CLR — SQLite/EF không dịch ổn định DateTimeOffset so sánh.
        var users = await userManager.Users
            .AsNoTracking()
            .OrderBy(u => u.UserName)
            .ToListAsync(ct);

        var result = new List<UserListItemResponse>();
        foreach (var user in users)
        {
            if (!IsActive(user)) continue;
            var roles = await userManager.GetRolesAsync(user);
            result.Add(new UserListItemResponse
            {
                Id = user.Id,
                DisplayName = ResolveDisplayName(user),
                Roles = roles.OrderBy(r => r, StringComparer.Ordinal).ToList(),
                IsActive = true
            });
        }

        return result;
    }

    public async Task<ApplicationUser?> FindActiveByIdAsync(Guid userId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !IsActive(user)) return null;
        return user;
    }

    public static string ResolveDisplayName(ApplicationUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.UserName))
            return user.UserName.Trim();
        return user.Id.ToString("N");
    }

    public static bool IsActive(ApplicationUser user)
        => user.LockoutEnd is null || user.LockoutEnd <= DateTimeOffset.UtcNow;
}
