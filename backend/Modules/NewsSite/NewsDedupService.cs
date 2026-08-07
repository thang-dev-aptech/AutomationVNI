using Backend.Modules.ContentCrawl;
using Backend.Shared.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Modules.NewsSite;

/// <param name="IsDuplicate">Có trùng bài đã lên web không.</param>
/// <param name="OfNewsId">Bài đã lên web mà nó trùng.</param>
/// <param name="Reason">Câu giải thích cho người duyệt đọc, không phải mã lỗi.</param>
/// <param name="Method">Tầng nào bắt được — để đo lại ngưỡng sau này.</param>
public sealed record NewsDupVerdict(
    bool IsDuplicate, Guid? OfNewsId, string? Reason, string Method = "none")
{
    public static readonly NewsDupVerdict Clean = new(false, null, null);
}

/// <summary>
/// Chống trùng ở KHÂU XUẤT BẢN — so bài sắp lên web với bài ĐÃ Ở TRÊN WEB.
///
/// ═══ VÌ SAO PHẢI CÓ, KHI ĐÃ CÓ ContentDedupService ═══
///
/// Bộ chống trùng lúc cào trả lời câu hỏi "hai văn bản này có phải bản sao của nhau không".
/// Trang tin cần câu trả lời khác: "hai bài này có nói với độc giả cùng một điều không".
/// Chồng lấp từ ngữ trả lời được câu đầu, KHÔNG BAO GIỜ trả lời được câu sau.
///
/// Đo trên cụm 16 tin về vụ Tuyên Quang trong CSDL thật:
///
///   ≥0,70 chặn thẳng          0 cặp
///   0,40–0,70 đẩy sang AI      3 cặp
///   &lt;0,40 lọt hẳn           117 cặp   ← 97,5%
///
/// 117/120 cặp thậm chí không tới được tầng AI, vì mỗi báo dùng chữ khác nhau cho cùng một
/// việc: "Bộ GD&amp;ĐT: Thi lại với thí sinh chuyên Tuyên Quang" và "Vụ gian lận điểm thi ở
/// Tuyên Quang: Khởi tố thầy…" chỉ chồng lấp 0,12.
///
/// Ba lỗ hổng cấu trúc, không phải một ngưỡng đặt sai:
///
///   1. Hỏi sai câu hỏi     — đếm từ chung, trong khi cần biết có cùng SỰ VIỆC không.
///                            Vá bằng EventKey do AI sinh ngay trong lượt viết bài.
///   2. So sai văn bản      — chỉ so tít GỐC của báo, nhưng AI VIẾT LẠI tít. Chữ mà độc giả
///                            thực sự so sánh chưa bao giờ được đem đi so. Hai bài trên trang
///                            chủ hiện tại đạt 0,688 — nằm trong vùng AI, nhưng ở khâu đó
///                            không có gì chạy cả.
///   3. Thiếu nửa cửa sổ    — ContentFingerprints chỉ có CrawledArticle và Post. Bài đã lên
///                            web VÔ HÌNH. Tuần sau cào được tin cùng sự việc thì không có gì
///                            để so.
///
/// ═══ BA TẦNG, RẺ TRƯỚC ĐẮT SAU ═══
///
///   Tầng 1  EventKey khớp chính xác   0 đồng, 0 giây   bắt "cùng việc, khác chữ"
///   Tầng 2  Chồng lấp tít+sapo ≥0,70  0 đồng, 0 giây   bắt "khác việc nhưng viết y hệt"
///   Tầng 3  AI đọc và phán 0,45–0,70  ~300 token       vùng số học không tách được
///
/// Tầng 3 chỉ chạy cho bài lọt hai tầng đầu mà vẫn nghi — trên dữ liệu thật là 3/120 cặp,
/// nên chi phí gần như không đổi.
/// </summary>
public class NewsDedupService(
    NewsSiteRepository repository,
    IAiJudgeService ai,
    IOptions<NewsSiteOptions> options,
    ILogger<NewsDedupService> logger)
{
    private const string JudgeSystem =
        """
        Bạn là biên tập viên trực trang tin. Việc của bạn: quyết định có đăng bài MỚI hay không,
        khi trang đã có sẵn một bài gần giống.

        Đăng TRÙNG nghĩa là độc giả đọc hai bài mà biết đúng một lượng thông tin như nhau.

        Trả lời "TRUNG" nếu bài mới không cho độc giả biết thêm gì so với bài đã có — cùng một
        việc, cùng những con số và mốc thời gian đó, chỉ khác cách diễn đạt.

        Trả lời "KHAC" nếu bài mới có DIỄN BIẾN, số liệu, hoặc quyết định mà bài cũ chưa có —
        kể cả khi cùng một vụ việc. Tin tiếp diễn là chuyện bình thường của trang tin: "khởi tố
        bị can" là bài khác với "tổ chức thi lại", dù cùng một vụ.

        Nghi ngờ thì chọn KHAC. Chặn nhầm một tin thật tệ hơn để lọt một bài na ná, vì người
        duyệt không bao giờ biết mình đã mất tin gì.

        Trả về ĐÚNG một dòng: TRUNG|<lý do ngắn>   hoặc   KHAC|<lý do ngắn>
        """;

    private NewsSiteOptions Opt => options.Value;

    public async Task<NewsDupVerdict> CheckAsync(
        ComposedArticle composed, Guid? selfNewsId, CancellationToken ct = default)
    {
        if (!Opt.PublishDedupEnabled) return NewsDupVerdict.Clean;

        var published = await repository.GetPublishedAsync(null, 200, ct);
        var others = published.Where(x => x.Id != selfNewsId).ToList();
        if (others.Count == 0) return NewsDupVerdict.Clean;

        // ── Tầng 1: cùng khoá sự việc ────────────────────────────────────────
        // Đây là tầng DUY NHẤT bắt được "cùng việc, khác chữ" — 97,5% số cặp bỏ lọt trước đây.
        if (!string.IsNullOrWhiteSpace(composed.EventKey))
        {
            var sameEvent = others.FirstOrDefault(x => x.EventKey == composed.EventKey);
            if (sameEvent is not null)
                return new NewsDupVerdict(true, sameEvent.Id,
                    $"Đã có bài về cùng sự việc trên web: \"{Shorten(sameEvent.Title)}\"", "eventKey");
        }

        // ── Tầng 2: chồng lấp chữ trên tít + sapo ────────────────────────────
        // So tít+sapo chứ không chỉ tít: tít sau khi AI viết lại có thể khác hẳn nhau trong khi
        // sapo kể đúng một câu chuyện. Đây là chữ độc giả thực sự đọc trên thẻ ngoài trang chủ.
        var scored = others
            .Select(x => (Article: x, Score: SimHashCalculator.TitleJaccard(
                $"{composed.Title} {composed.Sapo}", $"{x.Title} {x.Sapo}")))
            .OrderByDescending(x => x.Score)
            .ToList();

        var best = scored[0];
        if (best.Score >= Opt.PublishDuplicateMin)
            return new NewsDupVerdict(true, best.Article.Id,
                $"Trùng {best.Score:P0} với bài đã có: \"{Shorten(best.Article.Title)}\"", "overlap");

        // ── Tầng 3: AI đọc vùng chồng lấn ────────────────────────────────────
        if (best.Score < Opt.PublishBorderlineMin) return NewsDupVerdict.Clean;

        // AI tắt thì CHO ĐĂNG, không chặn. Chặn khi không ai kiểm được là âm thầm nuốt tin
        // thật — người duyệt bấm Duyệt, thấy báo trùng, mà không có bài nào để đối chiếu.
        if (!ai.IsAvailable())
        {
            logger.LogWarning(
                "Bài \"{Title}\" trùng {Score:P0} với bài đã có nhưng AI thẩm định đang tắt — vẫn cho đăng",
                Shorten(composed.Title), best.Score);
            return NewsDupVerdict.Clean;
        }

        var answer = await ai.AskAsync(
            JudgeSystem,
            $"""
             BÀI ĐÃ CÓ TRÊN WEB
             Tít : {best.Article.Title}
             Sapo: {best.Article.Sapo}

             BÀI MỚI ĐỊNH ĐĂNG
             Tít : {composed.Title}
             Sapo: {composed.Sapo}
             Ý chính: {string.Join(" · ", composed.KeyPoints.Take(5))}
             """,
            maxTokens: 120,
            timeout: TimeSpan.FromSeconds(Opt.PublishDedupTimeoutSeconds),
            ct: ct);

        // Không trả lời được thì CHO ĐĂNG. Cùng lý do trên: im lặng mà chặn là mất tin.
        if (string.IsNullOrWhiteSpace(answer))
        {
            logger.LogWarning("AI không phán được trùng cho \"{Title}\" — cho đăng", Shorten(composed.Title));
            return NewsDupVerdict.Clean;
        }

        var verdict = answer.Trim();
        if (!verdict.StartsWith("TRUNG", StringComparison.OrdinalIgnoreCase))
            return NewsDupVerdict.Clean;

        var why = verdict.Split('|', 2) is [_, var tail] ? tail.Trim() : "AI xác định cùng nội dung";
        return new NewsDupVerdict(true, best.Article.Id,
            $"{why} — trùng bài \"{Shorten(best.Article.Title)}\"", "ai");
    }

    private const string KeySystem =
        """
        Đọc tít và sapo của một bài báo, trả về định danh SỰ VIỆC CỤ THỂ mà bài đó tường thuật.

        Viết thường không dấu, nối bằng gạch ngang, 3–6 từ.
        Ghi việc CỤ THỂ chứ không ghi chủ đề chung:
          ĐÚNG  to-chuc-thi-lai-thpt-chuyen-tuyen-quang
          SAI   giao-duc · thi-cu (quá rộng, bài nào cũng khớp)
        Cùng vụ nhưng khác việc thì phải khác khoá.

        Chỉ trả về khoá, không thêm chữ nào.
        """;

    /// <summary>
    /// Bù khoá sự việc cho bài ĐÃ lên web từ trước khi có cơ chế này.
    ///
    /// Không bù thì tầng 1 mù với đúng những bài đang nằm trên trang — bài mới về cùng sự việc
    /// vẫn lọt, và bản sửa hở ngay chỗ cần nhất. Chạy được nhiều lần, chỉ đụng bài còn thiếu.
    /// </summary>
    public async Task<int> BackfillEventKeysAsync(CancellationToken ct = default)
    {
        if (!ai.IsAvailable()) return 0;

        var missing = (await repository.GetPublishedAsync(null, 200, ct))
            .Where(x => string.IsNullOrWhiteSpace(x.EventKey))
            .ToList();

        var filled = 0;
        foreach (var a in missing)
        {
            ct.ThrowIfCancellationRequested();

            var answer = await ai.AskAsync(
                KeySystem, $"Tít : {a.Title}\nSapo: {a.Sapo}",
                maxTokens: 40,
                timeout: TimeSpan.FromSeconds(Opt.PublishDedupTimeoutSeconds), ct: ct);

            var key = NormalizeKey(answer);
            if (key is null) continue;

            a.EventKey = key;
            await repository.SaveAsync(a, ct);
            filled++;
            logger.LogInformation("Bù khoá sự việc: {Key} ← {Title}", key, Shorten(a.Title));
        }

        return filled;
    }

    /// <summary>Cùng luật với NewsComposeService — hai nơi sinh khoá phải ra cùng dạng chuỗi.</summary>
    private static string? NormalizeKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var slug = NewsSiteRepository.Slugify(raw);
        var parts = slug.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 3 ? null : string.Join('-', parts.Take(8));
    }

    private static string Shorten(string? s, int max = 60)
        => string.IsNullOrWhiteSpace(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
