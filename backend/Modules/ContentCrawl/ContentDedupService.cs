using System.Text;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.ContentCrawl.Enums;
using Backend.Shared.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Modules.ContentCrawl;

/// <summary>Một ứng viên có thể trùng, đã kèm điểm.</summary>
public sealed record SimilarityCandidate(
    Guid OwnerId,
    FingerprintOwner OwnerType,
    string? TitleSnippet,
    DateTime ContentAt,
    int HammingDistance,
    double TitleJaccard)
{
    /// <summary>Điểm gộp 0..1 — lấy cái nào cao hơn giữa độ gần SimHash và chồng lấp tiêu đề.</summary>
    public double Score => Math.Max(1.0 - HammingDistance / 64.0, TitleJaccard);
}

public sealed record DedupVerdict(
    bool IsDuplicate,
    DedupMethod Method,
    Guid? DuplicateOfId,
    DuplicateTarget Target,
    double? Score,
    string? Reason);

/// <summary>
/// Vân tay đã nạp sẵn cho một lượt chấm. Nạp MỘT lần cho cả batch rồi so trong bộ nhớ —
/// rẻ hơn nhiều so với bắn query cho từng bài.
/// </summary>
public sealed record DedupWindow(List<DedupWindowRow> Rows)
{
    public static readonly DedupWindow Empty = new([]);
}

public sealed record DedupWindowRow(
    Guid OwnerId, FingerprintOwner OwnerType, string ContentHash,
    long SimHash, string? TitleSnippet, DateTime ContentAt);

/// <summary>
/// Chống trùng 4 bậc, rẻ dần lên đắt. Nguyên tắc bao trùm: LỖI THÌ MỞ, KHÔNG ĐÓNG —
/// báo trùng nhầm là mất nội dung âm thầm, báo sạch nhầm chỉ tốn người duyệt một cú click.
/// </summary>
public class ContentDedupService(
    AppDbContext context,
    IAiJudgeService aiJudge,
    IOptions<ContentCrawlOptions> options,
    ILogger<ContentDedupService> logger)
{
    private const string JudgeSystemPrompt =
        """
        Bạn kiểm tra trùng lặp tin tức giáo dục Việt Nam.
        Nhiệm vụ: xác định bài MỚI có phải CÙNG MỘT SỰ VIỆC với một trong các bài ĐÃ CÓ không.

        CÙNG sự việc = cùng sự kiện, cùng chủ thể, cùng mốc thời gian — dù khác báo, khác cách đặt tít.
        KHÁC sự việc, kể cả khi câu chữ rất giống:
        - Khác địa phương (Hà Tĩnh vs Tuyên Quang) → KHÁC
        - Khác năm học / khác kỳ thi (2023 vs 2026) → KHÁC
        - Tin cập nhật tiếp theo của cùng chủ đề nhưng diễn biến mới → KHÁC
        - Cùng chủ đề chung chung (đều nói về học phí) nhưng khác trường, khác quyết định → KHÁC

        Chỉ trả về JSON, không giải thích ngoài JSON:
        {"duplicateIndex": <số thứ tự bài đã có trùng với bài mới, hoặc 0 nếu không trùng>, "reason": "<lý do ngắn gọn tiếng Việt>"}
        """;

    /// <summary>Nạp cửa sổ vân tay dùng chung cho cả batch.</summary>
    public async Task<DedupWindow> LoadWindowAsync(CancellationToken ct = default)
    {
        var opt = options.Value.Dedup;
        var since = DateTime.UtcNow.AddDays(-Math.Max(1, opt.LookbackDays));

        var all = await context.Set<ContentFingerprintModel>()
            .Where(x => !x.IsDeleted && x.ContentAt >= since)
            .OrderByDescending(x => x.ContentAt)
            .Take(Math.Max(100, opt.MaxWindowRows))
            .Select(x => new DedupWindowRow(
                x.OwnerId, x.OwnerType, x.ContentHash, x.SimHash, x.TitleSnippet, x.ContentAt))
            .ToListAsync(ct);

        // BỎ vân tay không còn chủ.
        //
        // Vân tay chỉ được ghi thêm, không ai dọn. Khi bài gốc biến mất, dòng vân tay ở lại và
        // vẫn tham gia so sánh — kết quả là tin bị đánh "trùng với <một Guid không tồn tại>".
        // Người duyệt mở tab Trùng ra không có gì để đối chiếu, và tin thật nằm đó chờ mãi.
        //
        // Đo thật: 8/100 vân tay mồ côi, kéo theo 4/13 phán quyết trùng (31%) trỏ vào hư không.
        var articleIds = all.Where(r => r.OwnerType == FingerprintOwner.CrawledArticle)
            .Select(r => r.OwnerId).ToList();
        var postIds = all.Where(r => r.OwnerType == FingerprintOwner.Post)
            .Select(r => r.OwnerId).ToList();

        // Bài đã BỊ LOẠI không được làm mốc so trùng.
        //
        // Đánh một tin mới là "trùng với bài mình đã vứt đi" nghĩa là vứt nốt tin mới — trong
        // khi bài kia sẽ không bao giờ lên web. Cả hai nửa cùng mất.
        //
        // Đây cũng là thứ làm vòng lặp: hồi sinh bài trùng xong, lượt worker kế tiếp so lại
        // với đúng bài đã bị lọc đó và đánh trùng lần nữa. Đã thấy thật — 3 tin vừa cứu bị
        // đánh trùng lại trong vòng một nhịp, kèm lý do "AI xác nhận trùng: Cùng sự việc…".
        //
        // Chỉ giữ tin còn sống trong luồng: chờ xử lý, chờ duyệt, đã duyệt.
        var usable = new[]
        {
            CrawledArticleStatus.New, CrawledArticleStatus.Deduping, CrawledArticleStatus.Rewriting,
            CrawledArticleStatus.Pending, CrawledArticleStatus.Approved,
        };

        var liveArticles = await context.Set<CrawledArticleModel>()
            .Where(x => articleIds.Contains(x.Id) && !x.IsDeleted && usable.Contains(x.Status))
            .Select(x => x.Id).ToListAsync(ct);
        var livePosts = await context.Posts
            .Where(x => postIds.Contains(x.Id) && !x.IsDeleted)
            .Select(x => x.Id).ToListAsync(ct);

        var live = liveArticles.Concat(livePosts).ToHashSet();
        var rows = all.Where(r => live.Contains(r.OwnerId)).ToList();

        var dropped = all.Count - rows.Count;
        if (dropped > 0)
            logger.LogInformation(
                "Bỏ {N}/{Total} vân tay không dùng được — chủ đã bị xoá, bị lọc, hoặc đã loại",
                dropped, all.Count);

        logger.LogDebug("Dedup: nạp {Count} vân tay trong {Days} ngày", rows.Count, opt.LookbackDays);
        return new DedupWindow(rows);
    }

    public async Task<DedupVerdict> CheckAsync(
        CrawledArticleModel article, DedupWindow window, CancellationToken ct = default)
    {
        var opt = options.Value.Dedup;

        // ── Bậc 0: trùng guid feed hoặc trùng URL đã chuẩn hoá ─────────────────
        var byIdentity = await FindByIdentityAsync(article, ct);
        if (byIdentity is not null)
            return new DedupVerdict(true, DedupMethod.SourceGuid, byIdentity,
                DuplicateTarget.CrawledArticle, 1.0, "Trùng guid feed hoặc trùng URL bài gốc");

        // ── Bậc 1: hash tuyệt đối ─────────────────────────────────────────────
        var exact = window.Rows.FirstOrDefault(r =>
            r.ContentHash == article.ContentHash && r.OwnerId != article.Id);
        if (exact is not null)
            return new DedupVerdict(true, DedupMethod.ExactHash, exact.OwnerId,
                ToTarget(exact.OwnerType), 1.0, "Nội dung y hệt bài đã có");

        // ── Bậc 2: SimHash + chồng lấp tiêu đề ────────────────────────────────
        var candidates = window.Rows
            .Where(r => r.OwnerId != article.Id)
            .Select(r => new SimilarityCandidate(
                r.OwnerId, r.OwnerType, r.TitleSnippet, r.ContentAt,
                SimHashCalculator.HammingDistance(article.SimHash, r.SimHash),
                SimHashCalculator.TitleJaccard(article.Title, r.TitleSnippet)))
            .Where(c => c.HammingDistance <= opt.BorderlineHammingMax
                        || c.TitleJaccard >= opt.BorderlineJaccardMin)
            .OrderByDescending(c => c.Score)
            // GOM THEO TIÊU ĐỀ trước khi cắt lấy 3.
            //
            // Một bài đăng lên 15 page sinh 15 dòng vân tay CÙNG tiêu đề. Không gom thì cả 3
            // suất ứng viên rơi vào đúng một bài, và tầng AI — thứ đang bắt 16/20 ca trùng —
            // được cho ăn một danh sách chỉ có một lựa chọn lặp lại ba lần.
            //
            // Đo thật: 28 dòng vân tay loại Post nhưng chỉ 10 tiêu đề khác nhau; một tiêu đề
            // lặp 15 lần.
            .GroupBy(c => c.TitleSnippet ?? c.OwnerId.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(Math.Max(1, opt.AiJudgeMaxCandidates))
            .ToList();

        if (candidates.Count == 0)
            return new DedupVerdict(false, DedupMethod.None, null, DuplicateTarget.None, null, null);

        var best = candidates[0];
        if (best.TitleJaccard >= opt.DuplicateJaccardMin)
            return new DedupVerdict(true, DedupMethod.SimHash, best.OwnerId,
                ToTarget(best.OwnerType), best.Score,
                $"Tiêu đề trùng {best.TitleJaccard:P0} với \"{Shorten(best.TitleSnippet)}\"");

        // ── Bậc 3: AI thẩm định vùng chồng lấn ────────────────────────────────
        // Đo trên feed thật: cặp trùng và cặp khác tin đều nằm ở d∈[13,21], j∈[0.50,0.60].
        // Không ngưỡng số học nào tách được, nên để AI đọc và phán.
        if (!opt.AiJudgeEnabled || !aiJudge.IsAvailable())
            return new DedupVerdict(false, DedupMethod.None, null, DuplicateTarget.None, best.Score,
                "Điểm nằm vùng nghi ngờ nhưng AI thẩm định đang tắt — cần người kiểm tra");

        return await JudgeWithAiAsync(article, candidates, ct);
    }

    private async Task<DedupVerdict> JudgeWithAiAsync(
        CrawledArticleModel article, List<SimilarityCandidate> candidates, CancellationToken ct)
    {
        var opt = options.Value.Dedup;

        var prompt = new StringBuilder();
        prompt.AppendLine("## BÀI MỚI");
        prompt.AppendLine($"Tiêu đề: {article.Title}");
        if (!string.IsNullOrWhiteSpace(article.Summary))
            prompt.AppendLine($"Tóm tắt: {Shorten(article.Summary, 300)}");
        prompt.AppendLine();
        prompt.AppendLine("## CÁC BÀI ĐÃ CÓ");
        for (var i = 0; i < candidates.Count; i++)
            prompt.AppendLine($"{i + 1}. {candidates[i].TitleSnippet}");

        var answer = await aiJudge.AskAsync(
            JudgeSystemPrompt, prompt.ToString(),
            maxTokens: 200, temperature: 0,
            timeout: TimeSpan.FromSeconds(Math.Max(5, opt.AiJudgeTimeoutSeconds)), ct);

        if (string.IsNullOrWhiteSpace(answer))
        {
            // LỖI THÌ MỞ: không được lặng lẽ đánh trùng rồi vứt bài chỉ vì AI không trả lời.
            logger.LogWarning("Dedup: AI thẩm định không trả lời cho bài {Id}", article.Id);
            return new DedupVerdict(false, DedupMethod.None, null, DuplicateTarget.None,
                candidates[0].Score, "AI thẩm định lỗi — cần người kiểm tra");
        }

        if (!TryParseJudgement(answer, out var index, out var reason))
        {
            logger.LogWarning("Dedup: không đọc được phán quyết AI: {Answer}", Shorten(answer, 200));
            return new DedupVerdict(false, DedupMethod.None, null, DuplicateTarget.None,
                candidates[0].Score, "AI trả lời không đúng định dạng — cần người kiểm tra");
        }

        if (index <= 0 || index > candidates.Count)
            return new DedupVerdict(false, DedupMethod.AiJudge, null, DuplicateTarget.None,
                candidates[0].Score, $"AI xác nhận KHÔNG trùng: {reason}");

        var picked = candidates[index - 1];
        return new DedupVerdict(true, DedupMethod.AiJudge, picked.OwnerId,
            ToTarget(picked.OwnerType), picked.Score, $"AI xác nhận trùng: {reason}");
    }

    private static bool TryParseJudgement(string answer, out int index, out string reason)
    {
        index = 0;
        reason = string.Empty;
        var json = StripJsonFence(answer);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("duplicateIndex", out var idx))
                index = idx.ValueKind == JsonValueKind.Number ? idx.GetInt32()
                    : int.TryParse(idx.GetString(), out var parsed) ? parsed : 0;
            if (root.TryGetProperty("reason", out var r))
                reason = r.GetString() ?? string.Empty;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string StripJsonFence(string value)
    {
        var text = value.Trim();
        if (!text.StartsWith("```")) return text;
        var start = text.IndexOf('\n');
        if (start < 0) return text;
        text = text[(start + 1)..];
        var end = text.LastIndexOf("```", StringComparison.Ordinal);
        return (end < 0 ? text : text[..end]).Trim();
    }

    /// <summary>Trùng guid trong cùng nguồn, hoặc trùng URL đã bỏ query ở bất kỳ nguồn nào.</summary>
    private async Task<Guid?> FindByIdentityAsync(CrawledArticleModel article, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(article.SourceGuid))
        {
            var byGuid = await context.Set<CrawledArticleModel>()
                .Where(x => !x.IsDeleted && x.Id != article.Id
                            && x.CrawlSourceId == article.CrawlSourceId
                            && x.SourceGuid == article.SourceGuid)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(ct);
            if (byGuid is not null) return byGuid;
        }

        if (!string.IsNullOrWhiteSpace(article.NormalizedUrl))
        {
            return await context.Set<CrawledArticleModel>()
                .Where(x => !x.IsDeleted && x.Id != article.Id
                            && x.NormalizedUrl == article.NormalizedUrl)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(ct);
        }

        return null;
    }

    private static DuplicateTarget ToTarget(FingerprintOwner owner)
        => owner == FingerprintOwner.Post ? DuplicateTarget.PublishedPost : DuplicateTarget.CrawledArticle;

    private static string Shorten(string? value, int max = 80)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }
}
