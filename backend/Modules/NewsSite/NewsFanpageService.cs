using Backend.Data;
using Backend.Modules.ContentCrawl;
using Backend.Modules.PageContext;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Modules.NewsSite;

public sealed record FanpageResult(Guid BatchId, int Created, List<string> Channels, string? NewsUrl);

/// <summary>
/// CỬA 2 — đăng một bài ĐÃ LÊN WEB sang các fanpage.
///
/// Khác CỬA 1 ở chỗ tư liệu không còn là toàn văn báo gốc mà là BÀI CỦA MÌNH:
///   - Caption rút từ keyPoints (~800 token/page thay vì 9.000)
///   - sourceUrl trỏ về tintuc.vni.edu.vn, nên bình luận đầu tiên tự dẫn về web mình
///     mà KHÔNG phải sửa một dòng nào trong PublishPipelineService
///   - Bài web đã qua bộ soi dữ kiện nên caption thừa hưởng tính sạch đó
///
/// Vẫn dùng lại toàn bộ đường ray sẵn có: fan-out → PostGenerationWorker sinh nội dung theo
/// góc riêng từng page → lên lịch hoặc đăng ngay.
/// </summary>
public class NewsFanpageService(
    AppDbContext context,
    NewsSiteRepository repository,
    PostRepository postRepository,
    PageContextRepository pageContextRepository,
    IOptions<ContentCrawlOptions> crawlOptions,
    ILogger<NewsFanpageService> logger)
{
    public async Task<FanpageResult> PublishAsync(
        NewsArticleModel news, List<Guid> channelIds, bool autoPublish, CancellationToken ct = default)
    {
        if (news.Status != NewsArticleStatus.Published)
            throw new ArgumentException("Bài chưa lên web, không đăng fanpage được");
        if (channelIds.Count == 0)
            throw new ArgumentException("Chưa chọn page nào");

        var newsUrl = repository.PublicUrlOf(news.Slug);

        // Link web CHƯA dựng xong thì rơi về link báo gốc. Không kiểm thì bình luận trỏ vào
        // 404 dưới một bài đã "đăng thành công" — không ai phát hiện vì bài vẫn lên bình
        // thường, chỉ có độc giả bấm vào là cụt.
        var linkTarget = newsUrl ?? news.SourceUrl;
        if (newsUrl is null)
            logger.LogWarning(
                "Chưa cấu hình NewsSite:PublicBaseUrl — bình luận sẽ dẫn về báo gốc, không phải web mình");

        var brief = new SourceArticleBrief(
            Title: news.Title,
            Summary: news.Sapo,
            // Tên hiện trong bình luận. Trỏ về web mình thì ghi tên mình; rơi về báo gốc thì
            // phải ghi đúng tên báo, không được nhận vơ.
            SourceName: newsUrl is null ? news.SourceName : "Tin tức VNI Education",
            SourceUrl: linkTarget,
            PublishedAt: news.PublishedAt,
            Content: null,
            Angle: null,
            KeyPoints: NewsSiteRepository.ReadKeyPoints(news.KeyPointsJson));

        var pageMap = await pageContextRepository.GetMapByChannelsAsync(channelIds, ct);

        var bulk = await postRepository.CreateFanOutQueuedAsync(
            title: news.Title,
            channelIds: channelIds,
            generationFlow: GenerationFlow.TextOnly,
            promptTemplateId: null,
            pageContextByChannel: pageMap,
            objective: crawlOptions.Value.DefaultObjective,
            categoryId: news.CategoryId,
            sourceArticle: brief,
            ct: ct);

        if (autoPublish && bulk.PostIds.Count > 0)
        {
            // Dùng lại đúng cơ chế của luồng Telegram: đánh dấu để worker đăng rồi báo kết quả.
            var crawled = news.CrawledArticleId is Guid cid
                ? await context.Set<CrawledArticleModel>().FirstOrDefaultAsync(x => x.Id == cid, ct)
                : null;
            if (crawled is not null)
            {
                crawled.AutoPublishRequested = true;
                crawled.ResultBatchId = bulk.BatchId;
                crawled.ResultPostCount = bulk.Created;
                crawled.TelegramResultAt = null;
                await context.SaveChangesAsync(ct);
            }
        }

        var names = await context.SocialChannels
            .Where(c => channelIds.Contains(c.Id))
            .Select(c => c.PageName)
            .ToListAsync(ct);

        logger.LogInformation(
            "CỬA 2 — bài web {Slug} → {N} bài fanpage, link {Link}", news.Slug, bulk.Created, linkTarget);

        return new FanpageResult(bulk.BatchId, bulk.Created, names, newsUrl);
    }
}
