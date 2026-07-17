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

        if (string.IsNullOrWhiteSpace(seedSettings.AdminEmail) ||
            string.IsNullOrWhiteSpace(seedSettings.AdminPassword))
            return;

        var adminEmail = seedSettings.AdminEmail.Trim();
        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin is not null) return;

        admin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(admin, seedSettings.AdminPassword);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Không thể seed admin user: {errors}");
        }

        await userManager.AddToRoleAsync(admin, "Admin");
    }
}
