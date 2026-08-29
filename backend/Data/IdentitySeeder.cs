using Backend.Data;
using Backend.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Backend.Data;

public static class IdentitySeeder
{
    public static readonly string[] DefaultRoles =
    [
        "Admin",
        "ContentManager",
        "Reviewer",
        "Viewer"
    ];

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var seedSettings = scope.ServiceProvider.GetRequiredService<IOptions<SeedSettings>>().Value;

        foreach (var role in DefaultRoles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new ApplicationRole { Name = role });
        }

        await SeedUserAsync(userManager, seedSettings.AdminEmail, seedSettings.AdminPassword, "Admin");

        // Tài khoản reviewer (role Viewer) — optional, để trống 2 biến thì bỏ qua, không lỗi.
        // Mục đích: có sẵn 1 tài khoản chỉ-xem để đưa cho bên thứ 3 audit (TikTok App Review, ...)
        // mà không phải chia sẻ tài khoản Admin thật.
        await SeedUserAsync(userManager, seedSettings.ReviewerEmail, seedSettings.ReviewerPassword, "Viewer");
    }

    private static async Task SeedUserAsync(
        UserManager<ApplicationUser> userManager, string? email, string? password, string role)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        email = email.Trim();
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null) return;

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Không thể seed user '{email}' (role {role}): {errors}");
        }

        await userManager.AddToRoleAsync(user, role);
    }
}
