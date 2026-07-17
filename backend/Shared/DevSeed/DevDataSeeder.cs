using System.Text.Json;
using Backend.Data;
using Backend.Modules.Category;
using Backend.Modules.PageContext;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared.SocialPublish;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Shared.DevSeed;

public class DevDataSeeder(
    AppDbContext context,
    RoleManager<ApplicationRole> roleManager,
    UserManager<ApplicationUser> userManager,
    IOptions<DevSeedOptions> options,
    ILogger<DevDataSeeder> logger) : IDevDataSeeder
{
    private const string SeedActor = "dev-seed";
    private const string DevAccessToken = SocialChannelTokenConstants.DevEncryptedToken;

    private static readonly string DefaultHashtagsJson = JsonSerializer.Serialize(new[]
    {
        "#VNI",
        "#TuyenSinh",
        "#Automation"
    });

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("DevDataSeeder is disabled by configuration");
            return;
        }

        try
        {
            await SeedRolesAsync(ct);
            await SeedAdminUserAsync(settings, ct);
            await SeedSocialChannelAsync(settings, ct);
            await SeedPageContextAsync(settings, ct);
            await SeedCategoryAsync(settings, ct);
            logger.LogInformation("Dev seed data completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dev seed failed");
        }
    }

    private async Task SeedRolesAsync(CancellationToken ct)
    {
        foreach (var role in IdentitySeeder.DefaultRoles)
        {
            if (await roleManager.RoleExistsAsync(role))
                continue;

            var result = await roleManager.CreateAsync(new ApplicationRole { Name = role });
            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Không thể seed role '{role}': {errors}");
            }

            logger.LogInformation("Dev seed created role {Role}", role);
        }
    }

    private async Task SeedAdminUserAsync(DevSeedOptions settings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.AdminEmail) ||
            string.IsNullOrWhiteSpace(settings.AdminPassword))
        {
            logger.LogWarning("Dev seed skipped admin user: AdminEmail or AdminPassword is empty");
            return;
        }

        var email = settings.AdminEmail.Trim();
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
            return;

        var admin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(admin, settings.AdminPassword);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Không thể seed admin user: {errors}");
        }

        await userManager.AddToRoleAsync(admin, "Admin");
        logger.LogInformation("Dev seed created admin user {Email}", email);
    }

    private async Task SeedSocialChannelAsync(DevSeedOptions settings, CancellationToken ct)
    {
        if (await ExistsAsync<SocialChannelModel>(settings.DefaultSocialChannelId, ct))
            return;

        var now = DateTime.UtcNow;
        context.SocialChannels.Add(new SocialChannelModel
        {
            Id = settings.DefaultSocialChannelId,
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            PageName = "VNI Dev Facebook Page",
            ExternalPageId = "dev_fb_page_001",
            AccessToken = DevAccessToken,
            TokenExpiresAt = now.AddDays(30),
            IsActive = true,
            CreatedAt = now,
            CreatedBy = SeedActor,
            IsDeleted = false
        });

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Dev seed created SocialChannel {Id}", settings.DefaultSocialChannelId);
    }

    private async Task SeedPageContextAsync(DevSeedOptions settings, CancellationToken ct)
    {
        if (await ExistsAsync<PageContextModel>(settings.DefaultPageContextId, ct))
            return;

        if (!await ExistsAsync<SocialChannelModel>(settings.DefaultSocialChannelId, ct))
        {
            logger.LogWarning(
                "Dev seed skipped PageContext: SocialChannel {SocialChannelId} not found",
                settings.DefaultSocialChannelId);
            return;
        }

        var channelHasContext = await context.PageContexts
            .AnyAsync(x => x.SocialChannelId == settings.DefaultSocialChannelId, ct);
        if (channelHasContext)
            return;

        var now = DateTime.UtcNow;
        context.PageContexts.Add(new PageContextModel
        {
            Id = settings.DefaultPageContextId,
            SocialChannelId = settings.DefaultSocialChannelId,
            BrandName = "VNI Automation",
            ToneOfVoice = "Thân thiện, rõ ràng, chuyên nghiệp",
            CtaText = "Đăng ký ngay",
            DefaultHashtags = DefaultHashtagsJson,
            CreatedAt = now,
            CreatedBy = SeedActor,
            IsDeleted = false
        });

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Dev seed created PageContext {Id}", settings.DefaultPageContextId);
    }

    private async Task SeedCategoryAsync(DevSeedOptions settings, CancellationToken ct)
    {
        if (await ExistsAsync<CategoryModel>(settings.DefaultCategoryId, ct))
            return;

        var slugExists = await context.Categories
            .AnyAsync(x => !x.IsDeleted && x.Slug == "tuyen-sinh", ct);
        if (slugExists)
            return;

        var now = DateTime.UtcNow;
        context.Categories.Add(new CategoryModel
        {
            Id = settings.DefaultCategoryId,
            Name = "Tuyển sinh",
            Slug = "tuyen-sinh",
            CreatedAt = now,
            CreatedBy = SeedActor,
            IsDeleted = false
        });

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Dev seed created Category {Id}", settings.DefaultCategoryId);
    }

    private async Task<bool> ExistsAsync<TEntity>(Guid id, CancellationToken ct)
        where TEntity : BaseEntity
        => await context.Set<TEntity>().AnyAsync(x => x.Id == id, ct);
}
