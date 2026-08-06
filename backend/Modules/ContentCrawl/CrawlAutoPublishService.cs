using System.Net;
using System.Text;
using Backend.Data;
using Backend.Modules.ContentCrawl.Enums;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Modules.PublishLog;
using Backend.Shared.Notification;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.ContentCrawl;

/// <summary>
/// Đưa tin đã duyệt từ Telegram đi hết đường: chờ sinh nội dung xong → đăng thật lên Facebook
/// → nhắn link bài về Telegram.
///
/// Vì sao phải là một vòng quét chứ không làm thẳng trong lệnh /dang: sinh nội dung chạy bất
/// đồng bộ ở PostGenerationWorker, mỗi page mất vài chục giây. Giữ HTTP request của Telegram
/// mở suốt ngần ấy thời gian là chắc chắn timeout, mà timeout xong thì bài đã tạo rồi — không
/// biết đường nào lần. Vòng quét thì lệnh trả lời ngay, còn kết quả tới sau như một tin nhắn
/// riêng, đúng cách người ta dùng bot.
///
/// Đăng lại đúng đường publish-now của web (PostController.PublishNow): PublishNowAsync tạo
/// job + log rồi ProcessPendingForPostAsync xử lý. Không tự gọi Graph API ở đây — làm vậy là
/// đẻ ra một đường đăng thứ hai không có retry, không có PublishLog, không ai soi được.
/// </summary>
public class CrawlAutoPublishService(
    AppDbContext context,
    ContentCrawlRepository repository,
    PostWorkflowService workflow,
    IPublishPipelineService publishPipeline,
    TelegramClient telegram,
    ILogger<CrawlAutoPublishService> logger)
{
    /// <summary>
    /// Quá hạn này mà batch vẫn chưa xong thì báo về những gì đang có rồi thôi.
    /// Không có trần thì một bài kẹt ở Generating sẽ khiến bot im lặng vĩnh viễn — sếp gõ
    /// /dang rồi ngồi đợi một tin nhắn không bao giờ tới.
    /// </summary>
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(20);

    public async Task<int> TickAsync(CancellationToken ct = default)
    {
        var articles = await context.Set<CrawledArticleModel>()
            .Where(a => !a.IsDeleted
                        && a.Status == CrawledArticleStatus.Approved
                        && a.AutoPublishRequested
                        && a.TelegramResultAt == null
                        && a.ResultBatchId != null
                        && a.TelegramChatId != null)
            .OrderBy(a => a.ReviewedAt)
            .Take(5)
            .ToListAsync(ct);

        var done = 0;
        foreach (var article in articles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await AdvanceAsync(article, ct)) done++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Đăng tự động tin {Id} lỗi", article.Id);
            }
        }
        return done;
    }

    /// <summary>Trả về true khi tin đã đi hết đường và đã báo về Telegram.</summary>
    private async Task<bool> AdvanceAsync(CrawledArticleModel article, CancellationToken ct)
    {
        var posts = await context.Set<PostModel>()
            .Where(p => !p.IsDeleted && p.BatchId == article.ResultBatchId)
            .ToListAsync(ct);

        if (posts.Count == 0) return false;

        // Bài nào sinh xong (Approved) thì đẩy đi đăng. Bài còn Queued/Generating để lượt sau.
        foreach (var post in posts.Where(p => p.Status == PostStatus.Approved))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await workflow.PublishNowAsync(post.Id, ct);
                await publishPipeline.ProcessPendingForPostAsync(post.Id, ct);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // Y hệt PostController.PublishNow: lỗi precondition thì gỡ post khỏi Publishing,
                // nếu không nó kẹt ở đó mãi và vòng quét này đợi vô hạn.
                logger.LogWarning(ex, "Đăng bài {PostId} thất bại", post.Id);
                await publishPipeline.RevertStuckPublishingAsync(post.Id, ct);
            }
        }

        // Nạp lại: trạng thái và ExternalPostId vừa đổi ở trên.
        posts = await context.Set<PostModel>()
            .Where(p => !p.IsDeleted && p.BatchId == article.ResultBatchId)
            .ToListAsync(ct);

        var settled = posts.All(p => p.Status is PostStatus.Published or PostStatus.Failed);
        var overdue = DateTime.UtcNow - (article.ReviewedAt ?? article.UpdatedAt ?? DateTime.UtcNow) > MaxWait;
        if (!settled && !overdue) return false;

        return await ReportAsync(article, posts, overdue && !settled, ct);
    }

    /// <summary>Trả về true khi ĐÃ gửi được — gửi hỏng thì để lượt sau thử lại.</summary>
    private async Task<bool> ReportAsync(
        CrawledArticleModel article, List<PostModel> posts, bool timedOut, CancellationToken ct)
    {
        var channels = await context.SocialChannels
            .Where(c => posts.Select(p => p.SocialChannelId).Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.PageName, ct);

        var sb = new StringBuilder();
        var ok = posts.Count(p => p.Status == PostStatus.Published);
        sb.AppendLine(ok == posts.Count ? "🚀 <b>Đã đăng</b>" : $"⚠️ <b>Đăng {ok}/{posts.Count} bài</b>");
        sb.AppendLine(Esc(article.Title));
        sb.AppendLine();

        foreach (var p in posts)
        {
            var page = channels.GetValueOrDefault(p.SocialChannelId, "(page không rõ)");
            if (p.Status == PostStatus.Published && !string.IsNullOrWhiteSpace(p.ExternalPostId))
                sb.AppendLine($"✅ {Esc(page)}\n{PermalinkOf(p.ExternalPostId!)}");
            else if (p.Status == PostStatus.Failed)
                sb.AppendLine($"❌ {Esc(page)} — đăng lỗi, xem lại trên web");
            else
                sb.AppendLine($"⏳ {Esc(page)} — còn ở trạng thái {p.Status}");
        }

        if (timedOut)
            sb.AppendLine($"\n<i>Đã đợi quá {MaxWait.TotalMinutes:0} phút, báo tạm những gì đang có.</i>");

        var messageId = await telegram.SendMessageAsync(article.TelegramChatId!.Value, sb.ToString().TrimEnd(), null, ct);
        if (messageId is null)
        {
            // Không gửi được thì ĐỪNG đánh dấu đã báo — để lượt sau thử lại.
            logger.LogWarning("Không báo được kết quả đăng tin {Id} về Telegram", article.Id);
            return false;
        }

        article.TelegramResultAt = DateTime.UtcNow;
        await repository.UpdateAsync(article, ct);
        return true;
    }

    /// <summary>
    /// ExternalPostId của Facebook có dạng "{page-id}_{post-id}" → facebook.com/{page}/posts/{post}.
    ///
    /// ĐÃ THỬ THẬT trên một bài vừa đăng: dạng "{page}/{post}" (chỉ đổi gạch dưới thành gạch
    /// chéo) trả về 404. Chỉ "{page}/posts/{post}" và nguyên chuỗi có gạch dưới mới ra bài.
    /// Không dùng permalink_url của Graph vì phải tốn thêm một request cho chuỗi tự dựng được.
    /// </summary>
    private static string PermalinkOf(string externalPostId)
    {
        var i = externalPostId.IndexOf('_');
        return i > 0
            ? $"https://www.facebook.com/{externalPostId[..i]}/posts/{externalPostId[(i + 1)..]}"
            : $"https://www.facebook.com/{externalPostId}";
    }

    private static string Esc(string? s) => WebUtility.HtmlEncode(s ?? "");
}
