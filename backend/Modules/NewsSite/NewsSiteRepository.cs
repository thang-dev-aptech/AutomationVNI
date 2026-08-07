using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Data;
using Backend.Shared.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Modules.NewsSite;

public partial class NewsSiteRepository(
    AppDbContext context, IOptions<NewsSiteOptions> options)
{
    /// <summary>
    /// Đường dẫn công khai của bài. Trả null khi chưa cấu hình PublicBaseUrl — thà không có
    /// link còn hơn dán một đường dẫn tương đối mà bấm từ Facebook là 404.
    /// </summary>
    public string? PublicUrlOf(string? slug)
    {
        var baseUrl = options.Value.PublicBaseUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(slug)) return null;
        return baseUrl + NewsHtml.ArticlePath(slug);
    }

    public async Task<NewsArticleModel?> GetAsync(Guid id, CancellationToken ct = default)
        => await context.Set<NewsArticleModel>().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

    public async Task<NewsArticleModel?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => await context.Set<NewsArticleModel>().FirstOrDefaultAsync(x => x.Slug == slug && !x.IsDeleted, ct);

    public async Task<NewsArticleModel?> GetByCrawledAsync(Guid crawledId, CancellationToken ct = default)
        => await context.Set<NewsArticleModel>()
            .FirstOrDefaultAsync(x => x.CrawledArticleId == crawledId && !x.IsDeleted, ct);

    public async Task<List<NewsArticleModel>> GetPublishedAsync(
        string? categorySlug = null, int take = 12, CancellationToken ct = default)
    {
        var q = context.Set<NewsArticleModel>()
            .Where(x => !x.IsDeleted && x.Status == NewsArticleStatus.Published);

        if (!string.IsNullOrWhiteSpace(categorySlug))
            q = q.Where(x => x.CategorySlug == categorySlug);

        return await q.OrderByDescending(x => x.PublishedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);
    }

    /// <summary>Bài đang chờ AI viết hoặc chờ dựng trang.</summary>
    public async Task<List<NewsArticleModel>> GetPendingAsync(int take = 5, CancellationToken ct = default)
        => await context.Set<NewsArticleModel>()
            .Where(x => !x.IsDeleted
                        && (x.Status == NewsArticleStatus.Composing || x.Status == NewsArticleStatus.Ready))
            .OrderBy(x => x.CreatedAt)
            .Take(Math.Clamp(take, 1, 20))
            .ToListAsync(ct);

    public async Task<List<NewsArticleModel>> GetTopReadAsync(int take = 5, CancellationToken ct = default)
        => await context.Set<NewsArticleModel>()
            .Where(x => !x.IsDeleted && x.Status == NewsArticleStatus.Published)
            .OrderByDescending(x => x.ViewCount).ThenByDescending(x => x.PublishedAt)
            .Take(Math.Clamp(take, 1, 20))
            .ToListAsync(ct);

    public async Task SaveAsync(NewsArticleModel article, CancellationToken ct = default)
    {
        article.UpdatedAt = DateTime.UtcNow;
        if (context.Entry(article).State == EntityState.Detached)
            context.Set<NewsArticleModel>().Add(article);
        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Slug duy nhất từ tiêu đề. Trùng thì nối -2, -3…
    ///
    /// Bỏ dấu tiếng Việt: slug có dấu bị mã hoá percent trong URL, dán vào Facebook thành một
    /// dãy %C3%A1 dài loằng ngoằng, nhìn như link rác.
    /// </summary>
    public async Task<string> BuildUniqueSlugAsync(string title, CancellationToken ct = default)
    {
        var baseSlug = Slugify(title);
        if (string.IsNullOrWhiteSpace(baseSlug)) baseSlug = "tin";

        var slug = baseSlug;
        for (var i = 2; i < 200; i++)
        {
            if (!await context.Set<NewsArticleModel>().AnyAsync(x => x.Slug == slug, ct)) return slug;
            slug = $"{baseSlug}-{i}";
        }
        return $"{baseSlug}-{Guid.NewGuid():N}"[..Math.Min(110, baseSlug.Length + 33)];
    }

    public static string Slugify(string? input)
    {
        var text = VietnameseTextHelper.StripDiacritics(input).ToLowerInvariant();
        text = NonSlug().Replace(text, "-");
        text = DashRuns().Replace(text, "-").Trim('-');
        return text.Length <= 90 ? text : text[..90].TrimEnd('-');
    }

    public static List<string> ReadKeyPoints(string? json) => ReadList<string>(json);

    public static List<TimelineEntry> ReadTimeline(string? json) => ReadList<TimelineEntry>(json);

    private static List<T> ReadList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<T>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlug();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex DashRuns();
}
