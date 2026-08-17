using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Data;
using Backend.Shared;
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
    /// <summary>Địa chỉ CÔNG KHAI — dùng cho og:url, sitemap, và link dán ở bình luận Facebook.</summary>
    public string? PublicUrlOf(string? slug)
    {
        var baseUrl = options.Value.PublicBaseUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl) || IsTempSlug(slug)) return null;
        return baseUrl + NewsHtml.ArticlePath(slug);
    }

    /// <summary>
    /// Địa chỉ XEM THỬ cho nút trên giao diện quản trị. Chưa gắn tên miền thì đây là đường duy
    /// nhất mở được bài vừa duyệt.
    ///
    /// Slug TẠM ("cho-…" của bài đang chờ viết, "loi-…" của bài hỏng) thì trả null: những bài
    /// đó chưa có file HTML nào, dựng link cho chúng là mời người dùng bấm vào một trang 404.
    /// </summary>
    public string? PreviewUrlOf(string? slug)
    {
        if (IsTempSlug(slug)) return null;
        var baseUrl = options.Value.PreviewBaseUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) return PublicUrlOf(slug);
        return baseUrl + NewsHtml.ArticlePath(slug);
    }

    /// <summary>Slug máy tự đặt lúc chưa có tít thật — chưa ứng với file HTML nào.</summary>
    private static bool IsTempSlug(string? slug)
        => string.IsNullOrWhiteSpace(slug)
           || slug.StartsWith("cho-", StringComparison.Ordinal)
           || slug.StartsWith("loi-", StringComparison.Ordinal);

    public async Task<NewsArticleModel?> GetAsync(Guid id, CancellationToken ct = default)
        => await context.Set<NewsArticleModel>().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

    public async Task<NewsArticleModel?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => await context.Set<NewsArticleModel>().FirstOrDefaultAsync(x => x.Slug == slug && !x.IsDeleted, ct);

    /// <summary>
    /// Danh sách cho MÀN QUẢN TRỊ — mọi trạng thái, mới trước.
    ///
    /// Khác GetPublishedAsync vốn chỉ trả bài đã lên web (dùng để dựng trang tĩnh). Màn quản
    /// trị cần thấy cả bài ĐANG VIẾT và bài HỎNG: chỉ trả bài đã lên thì người duyệt bấm
    /// Duyệt xong nhìn vào danh sách thấy không có gì, tưởng mất tin.
    /// </summary>
    public async Task<List<NewsArticleModel>> GetForAdminAsync(
        string? categorySlug, int take, CancellationToken ct = default)
    {
        var q = context.Set<NewsArticleModel>().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(categorySlug))
            q = q.Where(x => x.CategorySlug == categorySlug);

        return await q.OrderByDescending(x => x.PublishedAt ?? x.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);
    }

    /// <summary>Bài đang ở một trạng thái, cũ trước — hàng đợi phải theo thứ tự vào.</summary>
    public async Task<List<NewsArticleModel>> GetByStatusAsync(
        NewsArticleStatus status, int take, CancellationToken ct = default)
        => await context.Set<NewsArticleModel>()
            .Where(x => !x.IsDeleted && x.Status == status)
            .OrderBy(x => x.CreatedAt)
            .Take(Math.Clamp(take, 1, 50))
            .ToListAsync(ct);

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

    /// <summary>
    /// Tìm bài đã lên web theo từ khoá — chỉ soi Title/Sapo, KHÔNG soi BodyHtml (dễ khớp nhầm
    /// vào rác thẻ HTML còn sót). EF dịch Contains sang LIKE của SQLite, không có FTS5 nên đây
    /// chỉ là so khớp chuỗi con, không xếp hạng liên quan.
    /// </summary>
    public async Task<List<NewsArticleModel>> SearchPublishedAsync(
        string q, int take = 20, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return [];
        var needle = q.Trim();

        return await context.Set<NewsArticleModel>()
            .Where(x => !x.IsDeleted && x.Status == NewsArticleStatus.Published
                        && (x.Title.Contains(needle) || (x.Sapo != null && x.Sapo.Contains(needle))))
            .OrderByDescending(x => x.PublishedAt)
            .Take(Math.Clamp(take, 1, 50))
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

    // ── Đăng ký nhận tin ───────────────────────────────────────────────────

    /// <summary>
    /// Đăng ký là nhận luôn — không xác nhận email. Idempotent: email đã đăng ký (kể cả đã
    /// huỷ trước đó) thì chỉ bật lại <c>IsActive</c>, không tạo dòng trùng.
    /// </summary>
    public async Task<NewsSubscriberModel> SubscribeAsync(string email, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var existing = await context.Set<NewsSubscriberModel>()
            .FirstOrDefaultAsync(x => x.Email == normalized, ct);

        if (existing is not null)
        {
            existing.IsActive = true;
            existing.UnsubscribedAt = null;
            existing.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(ct);
            return existing;
        }

        var sub = new NewsSubscriberModel
        {
            Id = Guid.NewGuid(),
            Email = normalized,
            IsActive = true,
            UnsubscribeToken = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
        };
        context.Set<NewsSubscriberModel>().Add(sub);
        await context.SaveChangesAsync(ct);
        return sub;
    }

    /// <summary>Trả true khi tìm thấy token và huỷ thành công — false thì token sai/đã dùng lạ.</summary>
    public async Task<bool> UnsubscribeAsync(string token, CancellationToken ct = default)
    {
        var sub = await context.Set<NewsSubscriberModel>()
            .FirstOrDefaultAsync(x => x.UnsubscribeToken == token, ct);
        if (sub is null) return false;

        sub.IsActive = false;
        sub.UnsubscribedAt = DateTime.UtcNow;
        sub.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<NewsSubscriberModel>> GetActiveSubscribersAsync(CancellationToken ct = default)
        => await context.Set<NewsSubscriberModel>().Where(x => x.IsActive).ToListAsync(ct);

    /// <summary>Trang quản lý admin — khác <see cref="GetActiveSubscribersAsync"/> (chỉ dùng nội bộ
    /// cho worker gửi mail), ở đây trả CẢ người đã huỷ để admin nhìn được toàn cảnh.</summary>
    public async Task<PagedResult<NewsSubscriberModel>> GetSubscribersForAdminAsync(
        string? keyword, bool? isActive, int index, int size, CancellationToken ct = default)
    {
        var query = context.Set<NewsSubscriberModel>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(x => x.Email.Contains(keyword.Trim()));
        if (isActive.HasValue)
            query = query.Where(x => x.IsActive == isActive.Value);

        var total = await query.CountAsync(ct);
        var idx = Math.Max(1, index);
        var sz = Math.Clamp(size, 1, 100);
        var items = await query.OrderByDescending(x => x.CreatedAt)
            .Skip((idx - 1) * sz).Take(sz).ToListAsync(ct);

        return new PagedResult<NewsSubscriberModel> { Items = items, Total = total, Index = idx, Size = sz };
    }

    /// <summary>Admin bật/tắt theo Id — khác <see cref="UnsubscribeAsync"/> (theo token, luồng độc
    /// giả tự bấm từ email).</summary>
    public async Task<NewsSubscriberModel?> SetSubscriberActiveAsync(
        Guid id, bool isActive, CancellationToken ct = default)
    {
        var sub = await context.Set<NewsSubscriberModel>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (sub is null) return null;

        sub.IsActive = isActive;
        sub.UnsubscribedAt = isActive ? null : DateTime.UtcNow;
        sub.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
        return sub;
    }

    /// <summary>Bài đã lên web nhưng chưa gửi email báo — NewsletterSendWorker quét đúng danh sách này.</summary>
    public async Task<List<NewsArticleModel>> GetPendingNewsletterAsync(
        int take = 10, CancellationToken ct = default)
        => await context.Set<NewsArticleModel>()
            .Where(x => !x.IsDeleted && x.Status == NewsArticleStatus.Published && x.NewsletterSentAt == null)
            .OrderBy(x => x.PublishedAt)
            .Take(Math.Clamp(take, 1, 50))
            .ToListAsync(ct);

    public async Task MarkNewsletterSentAsync(Guid articleId, CancellationToken ct = default)
    {
        var article = await context.Set<NewsArticleModel>().FirstOrDefaultAsync(x => x.Id == articleId, ct);
        if (article is null) return;
        article.NewsletterSentAt = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
    }

    /// <summary>Admin bấm "Gửi lại email" — đưa bài về lại hàng chờ để NewsletterSendWorker nhặt
    /// ở lượt quét kế tiếp (tối đa ~60 giây), dùng khi lần gửi trước hỏng hết vì cấu hình SMTP
    /// sai mà lúc đó chưa có bản vá không-đánh-dấu-khi-hỏng-hết.</summary>
    public async Task<bool> ResetNewsletterSentAsync(Guid articleId, CancellationToken ct = default)
    {
        var article = await context.Set<NewsArticleModel>().FirstOrDefaultAsync(x => x.Id == articleId, ct);
        if (article is null) return false;
        article.NewsletterSentAt = null;
        await context.SaveChangesAsync(ct);
        return true;
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
