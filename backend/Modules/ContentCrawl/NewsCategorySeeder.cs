using Backend.Data;
using Backend.Modules.Category;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.ContentCrawl;

/// <summary>
/// Đưa các chuyên mục trong <see cref="NewsTaxonomy"/> vào bảng Categories, để
/// <c>Post.CategoryId</c> có khoá thật và biến {{category}} trong prompt có nghĩa.
///
/// Chạy lại nhiều lần không tạo trùng: khớp theo Slug (đã có unique index).
/// KHÔNG bao giờ xoá mục cũ — bài đã đăng có thể đang trỏ vào đó.
/// </summary>
public static class NewsCategorySeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>().CreateLogger(nameof(NewsCategorySeeder));

        try
        {
            var slugs = NewsTaxonomy.All.Select(c => c.Slug).ToList();
            var existing = await context.Set<CategoryModel>()
                .Where(c => slugs.Contains(c.Slug))
                .ToDictionaryAsync(c => c.Slug, ct);

            var added = 0;
            foreach (var cat in NewsTaxonomy.All)
            {
                if (existing.ContainsKey(cat.Slug)) continue;

                context.Set<CategoryModel>().Add(new CategoryModel
                {
                    Id = Guid.NewGuid(),
                    Name = cat.Name,
                    Slug = cat.Slug,
                    Description = cat.Hint,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "system",
                });
                added++;
            }

            if (added > 0)
            {
                await context.SaveChangesAsync(ct);
                logger.LogInformation("Đã thêm {N} chuyên mục tin", added);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Seed hỏng thì hệ thống vẫn phải khởi động được: chuyên mục thiếu chỉ làm bài
            // rơi vào "khac", không làm chết luồng cào.
            logger.LogError(ex, "Không seed được chuyên mục tin");
        }
    }

    /// <summary>Slug → CategoryId. Nạp một lần cho cả lô, đừng truy vấn theo từng bài.</summary>
    public static async Task<Dictionary<string, Guid>> MapAsync(
        AppDbContext context, CancellationToken ct = default)
    {
        var slugs = NewsTaxonomy.All.Select(c => c.Slug).ToList();
        return await context.Set<CategoryModel>()
            .Where(c => slugs.Contains(c.Slug) && !c.IsDeleted)
            .ToDictionaryAsync(c => c.Slug, c => c.Id, ct);
    }
}
