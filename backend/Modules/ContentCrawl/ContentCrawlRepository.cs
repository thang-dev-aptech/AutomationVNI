using System.Text.Json;
using Backend.Data;
using Backend.Modules.ContentCrawl.Enums;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.ContentCrawl;

/// <summary>
/// Một repository cho cả module (nguồn / lượt cào / tin) — theo quy ước dự án là không tách
/// quá nhiều file nhỏ.
/// </summary>
public class ContentCrawlRepository(AppDbContext context, IUserContext userContext)
    : GenericRepository<CrawledArticleModel>(context, userContext)
{
    // ── Nguồn cào ───────────────────────────────────────────────────────────

    public async Task<List<CrawlSourceModel>> GetSourcesAsync(bool onlyActive, CancellationToken ct = default)
    {
        var query = Context.Set<CrawlSourceModel>().Where(x => !x.IsDeleted);
        if (onlyActive) query = query.Where(x => x.IsActive);
        return await query.OrderBy(x => x.Name).ToListAsync(ct);
    }

    public async Task<CrawlSourceModel?> GetSourceAsync(Guid id, CancellationToken ct = default)
        => await Context.Set<CrawlSourceModel>().FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);

    /// <summary>
    /// Nguồn tới hạn cào. Hai chế độ: theo GIỜ CỐ ĐỊNH (nếu đặt CrawlTimes) hoặc theo CHU KỲ.
    /// Nguồn lỗi liên tiếp bị giãn theo bội số (chặn ở 8 lần) để một nguồn chết không nện vào
    /// server của người ta liên tục — chỉ áp cho chế độ chu kỳ, giờ cố định thì vẫn đúng giờ.
    /// </summary>
    public async Task<List<CrawlSourceModel>> GetDueSourcesAsync(int max, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var sources = await Context.Set<CrawlSourceModel>()
            .Where(x => !x.IsDeleted && x.IsActive)
            .ToListAsync(ct);

        return sources
            .Where(s => IsDue(s, now))
            .OrderBy(s => s.LastRunAt ?? DateTime.MinValue)
            .Take(Math.Max(1, max))
            .ToList();
    }

    public static bool IsDue(CrawlSourceModel source, DateTime nowUtc, string timezone = "Asia/Ho_Chi_Minh")
    {
        var slots = ParseCrawlTimes(source.CrawlTimes);
        return slots.Count > 0
            ? IsDueBySlots(source, nowUtc, slots, timezone)
            : IsDueByInterval(source, nowUtc);
    }

    private static bool IsDueByInterval(CrawlSourceModel s, DateTime nowUtc)
    {
        if (s.LastRunAt is null) return true;
        var backoff = Math.Min(Math.Max(1, s.ConsecutiveFailures), 8);
        return s.LastRunAt.Value.AddMinutes(Math.Max(1, s.IntervalMinutes) * backoff) <= nowUtc;
    }

    /// <summary>
    /// Tới hạn khi có một MỐC GIỜ đã qua mà lần cào gần nhất còn trước mốc đó.
    ///
    /// Cách này TỰ BÙ khi backend tắt: đặt 08:00 mà 9 giờ mới bật máy thì vẫn cào ngay, vì mốc
    /// 08:00 đã qua và LastRunAt vẫn nằm trước nó. Nếu chỉ so "đang đúng 08:00 không" thì lỡ
    /// giờ là mất luôn lượt cào hôm đó mà không ai biết.
    ///
    /// Chưa tới mốc nào trong hôm nay thì lấy mốc CUỐI của hôm qua làm mốc so — nhờ vậy máy bật
    /// lúc 6 giờ sáng vẫn cào bù cho lượt 20:00 tối qua nếu tối qua máy tắt.
    /// </summary>
    private static bool IsDueBySlots(
        CrawlSourceModel s, DateTime nowUtc, List<TimeSpan> slots, string timezone)
    {
        var tz = Backend.Modules.Post.PendingScheduleHelper.ResolveTimeZone(timezone);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);

        DateTime? lastPassedLocal = null;
        foreach (var slot in slots)
        {
            var todaySlot = nowLocal.Date + slot;
            if (todaySlot <= nowLocal && (lastPassedLocal is null || todaySlot > lastPassedLocal))
                lastPassedLocal = todaySlot;
        }
        lastPassedLocal ??= nowLocal.Date.AddDays(-1) + slots.Max();

        if (s.LastRunAt is null) return true;
        var lastRunLocal = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(s.LastRunAt.Value, DateTimeKind.Utc), tz);
        return lastRunLocal < lastPassedLocal.Value;
    }

    /// <summary>Đọc JSON array "HH:mm". Mốc hỏng thì bỏ qua, không làm chết cả nguồn.</summary>
    public static List<TimeSpan> ParseCrawlTimes(string? json)
    {
        return DeserializeStringList(json)
            .Select(x => TimeSpan.TryParse(x?.Trim(), out var ts) && ts >= TimeSpan.Zero && ts < TimeSpan.FromDays(1)
                ? ts : (TimeSpan?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
    }

    public async Task<CrawlSourceModel> CreateSourceAsync(CreateCrawlSourceRequest request, CancellationToken ct = default)
    {
        var entity = new CrawlSourceModel
        {
            Name = request.Name.Trim(),
            SourceType = request.SourceType,
            Url = request.Url.Trim(),
            SiteDomain = ExtractDomain(request.Url),
            CategoryId = request.CategoryId,
            IsActive = request.IsActive,
            CrawlTimes = SerializeList(NormalizeTimes(request.CrawlTimes)),
            IntervalMinutes = Math.Clamp(request.IntervalMinutes, 5, 1440),
            MaxItemsPerRun = Math.Clamp(request.MaxItemsPerRun, 1, 200),
            LookbackHours = Math.Clamp(request.LookbackHours, 1, 720),
            IncludeKeywords = SerializeList(request.IncludeKeywords),
            ExcludeKeywords = SerializeList(request.ExcludeKeywords),
            DefaultChannelIds = SerializeList(request.DefaultChannelIds),
            BrowserProfile = request.BrowserProfile?.Trim(),
        };
        StampCreate(entity);
        Context.Set<CrawlSourceModel>().Add(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<CrawlSourceModel?> UpdateSourceAsync(Guid id, UpdateCrawlSourceRequest request, CancellationToken ct = default)
    {
        var entity = await GetSourceAsync(id, ct);
        if (entity is null) return null;

        if (request.Name is not null) entity.Name = request.Name.Trim();
        if (request.SourceType.HasValue) entity.SourceType = request.SourceType.Value;
        if (request.Url is not null)
        {
            entity.Url = request.Url.Trim();
            entity.SiteDomain = ExtractDomain(entity.Url);
        }
        if (request.CategoryId.HasValue) entity.CategoryId = request.CategoryId;
        if (request.IsActive.HasValue) entity.IsActive = request.IsActive.Value;
        if (request.CrawlTimes is not null) entity.CrawlTimes = SerializeList(NormalizeTimes(request.CrawlTimes));
        if (request.IntervalMinutes.HasValue) entity.IntervalMinutes = Math.Clamp(request.IntervalMinutes.Value, 5, 1440);
        if (request.MaxItemsPerRun.HasValue) entity.MaxItemsPerRun = Math.Clamp(request.MaxItemsPerRun.Value, 1, 200);
        if (request.LookbackHours.HasValue) entity.LookbackHours = Math.Clamp(request.LookbackHours.Value, 1, 720);
        if (request.IncludeKeywords is not null) entity.IncludeKeywords = SerializeList(request.IncludeKeywords);
        if (request.ExcludeKeywords is not null) entity.ExcludeKeywords = SerializeList(request.ExcludeKeywords);
        if (request.DefaultChannelIds is not null) entity.DefaultChannelIds = SerializeList(request.DefaultChannelIds);
        if (request.BrowserProfile is not null) entity.BrowserProfile = request.BrowserProfile.Trim();

        StampUpdate(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<bool> SoftDeleteSourceAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await GetSourceAsync(id, ct);
        if (entity is null) return false;
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = GetCurrentUserName();
        await Context.SaveChangesAsync(ct);
        return true;
    }

    // GenericRepository ràng buộc audit theo TEntity (= CrawledArticleModel), nên nguồn cào
    // phải tự đóng dấu. Giữ đúng quy ước: UtcNow + tên người dùng hiện tại.
    private void StampCreate(BaseEntity entity)
    {
        if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
        entity.CreatedAt = DateTime.UtcNow;
        entity.CreatedBy = GetCurrentUserName();
    }

    private void StampUpdate(BaseEntity entity)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = GetCurrentUserName();
    }

    // ── Lượt cào ────────────────────────────────────────────────────────────

    public async Task<List<CrawlRunModel>> GetRecentRunsAsync(Guid? sourceId, int take, CancellationToken ct = default)
    {
        var query = Context.Set<CrawlRunModel>().Where(x => !x.IsDeleted);
        if (sourceId.HasValue) query = query.Where(x => x.CrawlSourceId == sourceId.Value);
        return await query.OrderByDescending(x => x.StartedAt).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct);
    }

    // ── Tin đã cào ──────────────────────────────────────────────────────────

    public async Task<PagedResult<CrawledArticleModel>> FilterArticlesAsync(
        CrawledArticleFilterRequest request, CancellationToken ct = default)
    {
        var query = QueryActive();

        if (request.CrawlSourceId.HasValue)
            query = query.Where(x => x.CrawlSourceId == request.CrawlSourceId.Value);
        if (request.Status.HasValue)
            query = query.Where(x => x.Status == request.Status.Value);
        if (request.Statuses is { Count: > 0 })
            query = query.Where(x => request.Statuses.Contains(x.Status));
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim();
            query = query.Where(x => x.Title.Contains(kw) || (x.Summary != null && x.Summary.Contains(kw)));
        }
        if (request.FromDate.HasValue)
            query = query.Where(x => x.FetchedAt >= request.FromDate.Value);
        if (request.ToDate.HasValue)
            query = query.Where(x => x.FetchedAt <= request.ToDate.Value);

        query = query.OrderByDescending(x => x.PublishedAt ?? x.FetchedAt);
        return await PaginateAsync(query, request.Index, request.Size, ct);
    }

    public async Task<List<CrawledArticleModel>> GetPendingProcessAsync(int take, CancellationToken ct = default)
        => await QueryActive()
            .Where(x => x.Status == CrawledArticleStatus.New
                        || x.Status == CrawledArticleStatus.Deduping
                        || x.Status == CrawledArticleStatus.Rewriting)
            .OrderBy(x => x.FetchedAt)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);

    /// <summary>
    /// Cấp số thứ tự nhỏ nhất còn trống cho những tin đang chờ duyệt mà chưa có số.
    ///
    /// Chỉ worker (một luồng) và luồng Telegram gọi hàm này, nên không khoá: hai lượt cùng
    /// cấp một số là chuyện gần như không xảy ra, mà nếu có thì tra ra 2 tin và người dùng
    /// bị bắt gõ rõ hơn — chứ không đăng nhầm.
    /// </summary>
    public async Task<int> EnsureShortCodesAsync(CancellationToken ct = default)
    {
        var pending = await QueryActive()
            .Where(x => x.Status == CrawledArticleStatus.Pending)
            .OrderBy(x => x.FetchedAt)
            .ToListAsync(ct);

        var taken = pending.Where(x => x.ShortCode.HasValue).Select(x => x.ShortCode!.Value).ToHashSet();
        var next = 1;
        var assigned = 0;

        foreach (var a in pending.Where(x => !x.ShortCode.HasValue))
        {
            while (taken.Contains(next)) next++;
            a.ShortCode = next;
            taken.Add(next);
            assigned++;
        }

        if (assigned > 0) await context.SaveChangesAsync(ct);
        return assigned;
    }

    /// <summary>Tin đang chờ người duyệt, mới nhất trước.</summary>
    public async Task<List<CrawledArticleModel>> GetPendingAsync(int take, CancellationToken ct = default)
        => await QueryActive()
            .Where(x => x.Status == CrawledArticleStatus.Pending)
            .OrderByDescending(x => x.FetchedAt)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);

    /// <summary>Tin sạch, chờ duyệt, CHƯA báo Telegram lần nào (TelegramMessageId rỗng).</summary>
    public async Task<List<CrawledArticleModel>> GetUnnotifiedPendingAsync(int take, CancellationToken ct = default)
        => await QueryActive()
            .Where(x => x.Status == CrawledArticleStatus.Pending && x.TelegramMessageId == null)
            .OrderBy(x => x.FetchedAt)
            .Take(Math.Clamp(take, 1, 20))
            .ToListAsync(ct);

    /// <summary>
    /// Tra tin theo mã ngắn 6 ký tự đầu của Guid (mã người dùng gõ trong Telegram).
    ///
    /// Lọc trong bộ nhớ chứ không dịch sang SQL: SQLite lưu Guid dạng TEXT hoa có gạch nối,
    /// so tiền tố bằng LINQ-to-SQL là trò may rủi theo provider. Tập ứng viên chỉ vài chục
    /// dòng (tin chưa xử lý), nạp về lọc tay vừa chắc vừa đủ nhanh.
    ///
    /// Trả về nhiều hơn 1 dòng nghĩa là mã bị đụng — người gọi phải bắt bạn gõ dài thêm,
    /// KHÔNG được tự chọn bừa cái đầu tiên rồi đăng nhầm bài.
    /// </summary>
    public async Task<List<CrawledArticleModel>> FindByShortIdAsync(string shortId, CancellationToken ct = default)
    {
        var trimmed = (shortId ?? "").Trim().TrimStart('#');

        // Số thuần → tra theo ShortCode. Đây là đường đi thường ngày; nhánh hex bên dưới chỉ
        // để tin nhắn Telegram cũ (đang còn trên màn hình của sếp) vẫn bấm được.
        if (int.TryParse(trimmed, out var code) && code > 0)
        {
            return await QueryActive()
                .Where(x => x.ShortCode == code
                            && (x.Status == CrawledArticleStatus.Pending
                                || x.Status == CrawledArticleStatus.Duplicate))
                .ToListAsync(ct);
        }

        var key = new string(trimmed.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (key.Length < 3) return [];

        var candidates = await QueryActive()
            .Where(x => x.Status == CrawledArticleStatus.Pending
                        || x.Status == CrawledArticleStatus.Duplicate)
            .OrderByDescending(x => x.FetchedAt)
            .Take(300)
            .ToListAsync(ct);

        return candidates
            .Where(x => x.Id.ToString("N").StartsWith(key, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<Dictionary<CrawledArticleStatus, int>> CountByStatusAsync(CancellationToken ct = default)
        => await QueryActive()
            .GroupBy(x => x.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    /// <summary>
    /// URL bài đã cào của MỌI nguồn — crawler lọc trước khi mở bài, khỏi tốn một lượt mở
    /// trình duyệt (~15s) cho bài rồi cũng bị vứt ở bước chấm trùng.
    /// Lấy cả nguồn khác vì hai báo có thể đăng lại cùng một URL.
    /// </summary>
    public async Task<HashSet<string>> GetKnownUrlsAsync(Guid sourceId, CancellationToken ct = default)
    {
        var urls = await QueryActive()
            .Where(x => x.CrawlSourceId == sourceId)
            .Select(x => x.SourceUrl)
            .ToListAsync(ct);
        return urls.Where(u => !string.IsNullOrWhiteSpace(u))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Guid feed đã có, để bỏ qua ngay lúc cào mà khỏi tạo bản ghi rác.</summary>
    public async Task<HashSet<string>> GetKnownGuidsAsync(Guid sourceId, CancellationToken ct = default)
    {
        var guids = await QueryActive()
            .Where(x => x.CrawlSourceId == sourceId && x.SourceGuid != null)
            .Select(x => x.SourceGuid!)
            .ToListAsync(ct);
        return guids.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // ── Helper ──────────────────────────────────────────────────────────────

    public static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    public static List<Guid> DeserializeGuidList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<Guid>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>Chuẩn hoá về "HH:mm", bỏ mốc không đọc được, sắp xếp và loại trùng.</summary>
    private static List<string>? NormalizeTimes(List<string>? raw)
    {
        if (raw is null) return null;
        return raw
            .Select(x => TimeSpan.TryParse(x?.Trim(), out var ts) && ts >= TimeSpan.Zero && ts < TimeSpan.FromDays(1)
                ? ts : (TimeSpan?)null)
            .Where(x => x.HasValue).Select(x => x!.Value)
            .Distinct().OrderBy(x => x)
            .Select(x => x.ToString(@"hh\:mm"))
            .ToList();
    }

    private static string? SerializeList<T>(List<T>? values)
        => values is null || values.Count == 0 ? null : JsonSerializer.Serialize(values);

    private static string? ExtractDomain(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    public static CrawlSourceResponse ToResponse(CrawlSourceModel e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        SourceType = e.SourceType,
        Url = e.Url,
        SiteDomain = e.SiteDomain,
        CategoryId = e.CategoryId,
        IsActive = e.IsActive,
        CrawlTimes = ParseCrawlTimes(e.CrawlTimes).Select(t => t.ToString(@"hh\:mm")).ToList(),
        IntervalMinutes = e.IntervalMinutes,
        MaxItemsPerRun = e.MaxItemsPerRun,
        LookbackHours = e.LookbackHours,
        LastRunAt = e.LastRunAt,
        LastSuccessAt = e.LastSuccessAt,
        LastError = e.LastError,
        ConsecutiveFailures = e.ConsecutiveFailures,
        IncludeKeywords = DeserializeStringList(e.IncludeKeywords),
        ExcludeKeywords = DeserializeStringList(e.ExcludeKeywords),
        DefaultChannelIds = DeserializeGuidList(e.DefaultChannelIds),
        BrowserProfile = e.BrowserProfile,
        CreatedAt = e.CreatedAt,
    };

    public static CrawlRunResponse ToResponse(CrawlRunModel e, string? sourceName = null) => new()
    {
        Id = e.Id,
        CrawlSourceId = e.CrawlSourceId,
        SourceName = sourceName,
        Status = e.Status,
        StartedAt = e.StartedAt,
        FinishedAt = e.FinishedAt,
        ItemsFetched = e.ItemsFetched,
        ItemsNew = e.ItemsNew,
        ItemsDuplicate = e.ItemsDuplicate,
        ItemsFiltered = e.ItemsFiltered,
        DurationMs = e.DurationMs,
        ErrorMessage = e.ErrorMessage,
        TriggerSource = e.TriggerSource,
    };

    public static CrawledArticleResponse ToResponse(CrawledArticleModel e, string? sourceName = null) => new()
    {
        Id = e.Id,
        CrawlSourceId = e.CrawlSourceId,
        SourceName = sourceName,
        Title = e.Title,
        Summary = e.Summary,
        SourceUrl = e.SourceUrl,
        Author = e.Author,
        SourceCategory = e.SourceCategory,
        ThumbnailUrl = e.ThumbnailUrl,
        PublishedAt = e.PublishedAt,
        FetchedAt = e.FetchedAt,
        Status = e.Status,
        DuplicateMethod = e.DuplicateMethod,
        DuplicateTarget = e.DuplicateTarget,
        DuplicateOfId = e.DuplicateOfId,
        DuplicateScore = e.DuplicateScore,
        DuplicateReason = e.DuplicateReason,
        CategorySlug = e.CategorySlug,
        ShortCode = e.ShortCode,
        QualityScore = e.QualityScore,
        ScreenTopic = e.ScreenTopic,
        ScreenReason = e.ScreenReason,
        ScreenSummary = e.ScreenSummary,
        RejectReason = e.RejectReason,
        ErrorMessage = e.ErrorMessage,
        ReviewedBy = e.ReviewedBy,
        ReviewedAt = e.ReviewedAt,
        ResultBatchId = e.ResultBatchId,
        ResultPostCount = e.ResultPostCount,
    };
}
