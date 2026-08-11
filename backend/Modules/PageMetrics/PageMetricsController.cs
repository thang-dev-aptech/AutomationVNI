using Backend.Data;
using Backend.Modules.NewsSite;
using Backend.Modules.Post.Enums;
using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.PageMetrics;

/// <summary>
/// Số liệu cho dashboard KHÁCH HÀNG — trả lời "page đang chạy thế nào", không phải
/// "hệ thống đang chạy thế nào".
///
/// Cố ý tách khỏi <c>DashboardController</c>. Cái kia là bảng vận hành: hàng đợi job, dead-letter,
/// log đăng lỗi, độ phủ page-context — người vận hành cần, khách mở ra không hiểu gì.
/// Hai đối tượng khác nhau thì hai endpoint khác nhau, thay vì nhét thêm trường vào một cục rồi
/// để giao diện tự lọc.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PageMetricsController(
    AppDbContext db,
    PageMetricsSyncService sync,
    IServiceScopeFactory scopeFactory,
    ILogger<PageMetricsController> logger) : ControllerBase
{
    private static readonly TimeZoneInfo VnTime =
        Backend.Modules.Post.PendingScheduleHelper.ResolveTimeZone("Asia/Ho_Chi_Minh");

    /// <summary>
    /// Đóng dấu UTC cho mốc thời gian trước khi trả ra JSON.
    ///
    /// ═══ VÌ SAO CẦN ═══
    ///
    /// SQLite không lưu múi giờ, nên EF đọc lên là <c>DateTimeKind.Unspecified</c>. Bộ tuần tự
    /// JSON thấy Unspecified thì ghi "2026-08-11T04:26:55" — KHÔNG có chữ Z. Trình duyệt gặp chuỗi
    /// không có Z sẽ hiểu là GIỜ ĐỊA PHƯƠNG, tức giờ Việt Nam, và mọi mốc lệch đúng 7 tiếng.
    ///
    /// Đã thấy tận mắt: vừa đồng bộ xong mà dashboard ghi "cập nhật 7 giờ trước". Sai kiểu này
    /// không làm hỏng gì, chỉ làm người xem tin rằng số đang cũ và đi bấm đồng bộ lại vô ích.
    /// </summary>
    private static DateTime? Utc(DateTime? value)
        => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

    /// <param name="days">Cửa sổ so sánh, mặc định 30 ngày.</param>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 7, 365);
        var nowVn = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VnTime);
        var today = nowVn.Date;
        var windowStart = today.AddDays(-days + 1);
        var windowStartUtc = TimeZoneInfo.ConvertTimeToUtc(windowStart, VnTime);

        var channels = await db.SocialChannels
            .Where(x => !x.IsDeleted && x.IsActive)
            .Select(x => new { x.Id, x.PageName, x.ExternalPageId })
            .ToListAsync(ct);
        var channelIds = channels.Select(x => x.Id).ToList();

        // ─── Mốc chỉ số: bản mới nhất, và bản của ĐẦU cửa sổ để tính biến động ───
        var snapshots = await db.ChannelMetricDaily
            .Where(x => !x.IsDeleted && channelIds.Contains(x.SocialChannelId))
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        var latest = snapshots
            .GroupBy(x => x.SocialChannelId)
            .ToDictionary(g => g.Key, g => g.Last());

        // Mốc so sánh = bản gần nhất TRƯỚC cửa sổ. Không có thì không so — trả null chứ không lấy
        // bản cũ nhất trong cửa sổ làm gốc, vì như thế "tăng 30 ngày" thật ra là "tăng 2 ngày" mà
        // vẫn hiện nhãn 30 ngày.
        var baseline = snapshots
            .Where(x => x.Date < windowStart)
            .GroupBy(x => x.SocialChannelId)
            .ToDictionary(g => g.Key, g => g.Last());

        int? Delta(Guid id, Func<ChannelMetricDailyModel, int> pick)
        {
            if (!latest.TryGetValue(id, out var now) || !baseline.TryGetValue(id, out var before)) return null;
            return pick(now) - pick(before);
        }

        // ─── Bài đăng trong cửa sổ, theo từng page ───
        var postsInWindow = await db.SocialPosts
            .Where(x => !x.IsDeleted && channelIds.Contains(x.SocialChannelId)
                        && x.PostedAt != null && x.PostedAt >= windowStartUtc)
            .GroupBy(x => x.SocialChannelId)
            .Select(g => new
            {
                ChannelId = g.Key,
                Posts = g.Count(),
                Likes = g.Sum(x => x.LikeCount),
                Comments = g.Sum(x => x.PlatformCommentCount),
                Shares = g.Sum(x => x.ShareCount),
            })
            .ToListAsync(ct);
        var byChannel = postsInWindow.ToDictionary(x => x.ChannelId, x => x);

        // ═══ Chuỗi theo thời gian để vẽ biểu đồ ═══
        //
        // Dựng lại từ NGÀY ĐĂNG của từng bài, không phải từ bảng mốc ChannelMetricDaily.
        //
        // Vì sao: bảng mốc mới chạy từ hôm nay nên chỉ có một điểm, trong khi SocialPosts đã có
        // sẵn 8 tháng lịch sử — 28/30 ngày gần nhất đều có bài. Vẽ từ ngày đăng là có biểu đồ
        // thật ngay, thay vì một ô trống chờ hai tuần nữa mới đủ dữ liệu.
        //
        // Điều này gán tương tác vào NGÀY ĐĂNG BÀI, không phải ngày người ta bấm thích. Đó là
        // cách đọc đúng cho câu hỏi "bài đăng hôm đó chạy tốt không" — cũng là câu hỏi người làm
        // nội dung thực sự hỏi. Ngày phát sinh từng lượt thích thì Facebook không trả về.
        var windowPosts = await db.SocialPosts
            .Where(x => !x.IsDeleted && channelIds.Contains(x.SocialChannelId)
                        && x.PostedAt != null && x.PostedAt >= windowStartUtc)
            .Select(x => new { x.PostedAt, x.LikeCount, x.PlatformCommentCount, x.ShareCount })
            .ToListAsync(ct);

        // Gộp theo tuần khi cửa sổ dài, để số cột không vượt quá 30. 90 cột trên một khung rộng
        // 600px thì mỗi cột dưới 5px — không đọc được và không trỏ trúng để xem tooltip.
        var bucketDays = days <= 31 ? 1 : 7;
        var bucketCount = (int)Math.Ceiling(days / (double)bucketDays);

        var buckets = Enumerable.Range(0, bucketCount)
            .Select(i => new
            {
                Index = i,
                Start = windowStart.AddDays(i * bucketDays),
                End = windowStart.AddDays((i + 1) * bucketDays),
            })
            .Select(b => new
            {
                b.Start,
                b.End,
                Posts = 0, Likes = 0, Comments = 0, Shares = 0,
            })
            .ToList();

        var acc = buckets.Select(b => new int[4]).ToList();
        foreach (var p in windowPosts)
        {
            var localDay = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(p.PostedAt!.Value, DateTimeKind.Utc), VnTime).Date;
            var idx = (int)((localDay - windowStart).TotalDays / bucketDays);
            if (idx < 0 || idx >= acc.Count) continue;
            acc[idx][0] += 1;
            acc[idx][1] += p.LikeCount;
            acc[idx][2] += p.PlatformCommentCount;
            acc[idx][3] += p.ShareCount;
        }

        var series = buckets.Select((b, i) => new
        {
            from = b.Start,
            // Cắt về HÔM NAY, không để nhãn nhận cả những ngày chưa xảy ra.
            //
            // Mốc 90 ngày gộp theo tuần, nên ô cuối luôn là tuần đang chạy dở. Ghi "6/8–12/8"
            // trong khi mới tới 11/8 là hứa một tuần đầy đủ, người xem thấy cột đó thấp hơn hẳn
            // và tưởng tương tác đang tụt — thật ra tuần mới đi được một nửa.
            to = b.End.AddDays(-1) > today ? today : b.End.AddDays(-1),
            partial = b.End.AddDays(-1) > today,
            posts = acc[i][0],
            likes = acc[i][1],
            comments = acc[i][2],
            shares = acc[i][3],
        }).ToList();

        // Chuỗi người theo dõi lấy từ bảng mốc — cộng mọi page theo từng ngày.
        // Ngày nào chưa đo đủ 15 page thì tổng sẽ thấp giả tạo, nên trả kèm số page đo được để
        // giao diện tự quyết có vẽ điểm đó hay không.
        var followerSeries = snapshots
            .GroupBy(x => x.Date)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                date = g.Key,
                followers = g.Sum(x => x.Followers),
                measuredPages = g.Count(x => x.SyncError == null),
            })
            .ToList();

        var pages = channels.Select(c =>
        {
            var w = byChannel.GetValueOrDefault(c.Id);
            var snap = latest.GetValueOrDefault(c.Id);
            var engagement = (w?.Likes ?? 0) + (w?.Comments ?? 0) + (w?.Shares ?? 0);
            return new
            {
                id = c.Id,
                name = c.PageName,
                followers = snap?.Followers,
                followersDelta = Delta(c.Id, s => s.Followers),
                posts = w?.Posts ?? 0,
                likes = w?.Likes ?? 0,
                comments = w?.Comments ?? 0,
                shares = w?.Shares ?? 0,
                engagement,
                // Tương tác trung bình mỗi bài — con số so sánh được giữa page to và page nhỏ.
                // Tổng tương tác thì page nào đăng nhiều cũng thắng, không nói lên chất lượng bài.
                engagementPerPost = (w?.Posts ?? 0) == 0
                    ? (double?)null
                    : Math.Round(engagement / (double)w!.Posts, 1),
                syncedAt = Utc(snap?.UpdatedAt ?? snap?.CreatedAt),
                syncError = snap?.SyncError,
                neverSynced = snap is null,
            };
        })
        .OrderByDescending(x => x.engagement)
        .ThenByDescending(x => x.followers ?? 0)
        .ToList();

        // ─── Bài hiệu quả nhất trong cửa sổ ───
        var channelNames = channels.ToDictionary(x => x.Id, x => x.PageName);
        var topPosts = await db.SocialPosts
            .Where(x => !x.IsDeleted && channelIds.Contains(x.SocialChannelId)
                        && x.PostedAt != null && x.PostedAt >= windowStartUtc)
            .OrderByDescending(x => x.LikeCount + x.PlatformCommentCount + x.ShareCount)
            .Take(5)
            .Select(x => new
            {
                x.Id, x.SocialChannelId, x.Message, x.PermalinkUrl, x.PostedAt,
                likes = x.LikeCount, comments = x.PlatformCommentCount, shares = x.ShareCount,
            })
            .ToListAsync(ct);

        // ─── Việc cần làm: thứ khách mở dashboard ra là muốn thấy ngay ───
        var waitingReview = await db.Posts.CountAsync(
            x => !x.IsDeleted && x.Status == PostStatus.WaitingReview, ct);
        var scheduled = await db.Posts.CountAsync(
            x => !x.IsDeleted && x.Status == PostStatus.Scheduled && x.ScheduledPublishAt > DateTime.UtcNow, ct);
        var newsPending = await db.Set<Backend.Modules.ContentCrawl.CrawledArticleModel>()
            .CountAsync(x => !x.IsDeleted
                             && x.Status == Backend.Modules.ContentCrawl.Enums.CrawledArticleStatus.Pending, ct);

        var upcoming = await db.Posts
            .Where(x => !x.IsDeleted && x.Status == PostStatus.Scheduled
                        && x.ScheduledPublishAt > DateTime.UtcNow)
            .OrderBy(x => x.ScheduledPublishAt)
            .Take(6)
            .Select(x => new { x.Id, x.Title, x.ScheduledPublishAt, x.SocialChannelId })
            .ToListAsync(ct);

        // ─── Trang tin ───
        var newsPublished = await db.Set<NewsArticleModel>()
            .CountAsync(x => !x.IsDeleted && x.Status == NewsArticleStatus.Published, ct);
        var newsThisWindow = await db.Set<NewsArticleModel>()
            .CountAsync(x => !x.IsDeleted && x.Status == NewsArticleStatus.Published
                             && x.PublishedAt >= windowStartUtc, ct);

        var totalFollowers = pages.Sum(x => x.followers ?? 0);
        var followersMeasured = pages.Count(x => x.followers.HasValue);
        var totalFollowersDelta = pages.Where(x => x.followersDelta.HasValue).Sum(x => x.followersDelta!.Value);
        var anyBaseline = pages.Any(x => x.followersDelta.HasValue);

        var lastSync = snapshots.Count == 0
            ? (DateTime?)null
            : snapshots.Max(x => x.UpdatedAt ?? x.CreatedAt);

        return Ok(ApiResponse.Ok((object)new
        {
            window = new { days, from = windowStart, to = today },
            followers = new
            {
                total = totalFollowers,
                delta = anyBaseline ? totalFollowersDelta : (int?)null,
                // Bao nhiêu page thực sự đo được — giao diện cần con số này để nói "18/18" hay
                // "12/18 page", thay vì hiện một tổng trông đầy đủ mà thật ra thiếu 6 page.
                measured = followersMeasured,
                totalPages = pages.Count,
            },
            engagement = new
            {
                likes = pages.Sum(x => x.likes),
                comments = pages.Sum(x => x.comments),
                shares = pages.Sum(x => x.shares),
                total = pages.Sum(x => x.engagement),
            },
            posts = new { published = pages.Sum(x => x.posts), scheduled },
            series,
            followerSeries,
            bucketDays,
            todo = new { waitingReview, newsPending, scheduled },
            news = new { published = newsPublished, inWindow = newsThisWindow },
            pages,
            topPosts = topPosts.Select(p => new
            {
                p.Id,
                pageName = channelNames.GetValueOrDefault(p.SocialChannelId),
                message = p.Message,
                url = p.PermalinkUrl,
                postedAt = Utc(p.PostedAt),
                p.likes, p.comments, p.shares,
                engagement = p.likes + p.comments + p.shares,
            }),
            upcoming = upcoming.Select(u => new
            {
                u.Id, u.Title, scheduledPublishAt = Utc(u.ScheduledPublishAt),
                pageName = channelNames.GetValueOrDefault(u.SocialChannelId),
            }),
            sync = new
            {
                lastAt = Utc(lastSync),
                failedPages = pages.Count(x => x.syncError != null),
                neverSyncedPages = pages.Count(x => x.neverSynced),
            },
            // Nói thẳng cái mình KHÔNG đo được, ngay trong dữ liệu. Giao diện hiện câu này lên
            // để không ai đi tìm ô "Lượt tiếp cận" rồi tưởng hệ thống thiếu sót.
            unavailable = new[]
            {
                new { metric = "Lượt tiếp cận", reason = "Cần quyền read_insights của Facebook, phải qua App Review" },
                new { metric = "Lượt xem trang", reason = "Cần quyền read_insights của Facebook, phải qua App Review" },
            },
        }));
    }

    /// <summary>Đồng bộ ngay, không đợi worker. Chạy nền để không giữ request 20 giây.</summary>
    [HttpPost("sync")]
    public IActionResult SyncNow()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<PageMetricsSyncService>();
                await svc.SyncAllAsync(100, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Đồng bộ chỉ số theo yêu cầu bị hỏng");
            }
        });

        return Ok(ApiResponse.Ok((object)new { started = true },
            "Đang đồng bộ chỉ số từ Facebook. Số sẽ tự cập nhật sau khoảng 20–40 giây."));
    }

    /// <summary>Đồng bộ đồng bộ (chờ xong mới trả) — dùng để kiểm chứng, trả chi tiết từng page.</summary>
    [HttpPost("sync/wait")]
    public async Task<IActionResult> SyncAndWait(CancellationToken ct)
        => Ok(ApiResponse.Ok((object)await sync.SyncAllAsync(100, ct)));
}
