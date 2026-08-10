using Backend.Data;
using Backend.Modules.ContentCrawl;
using Backend.Modules.NewsSite;
using Backend.Shared.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Shared.Startup;

/// <summary>
/// Soi cấu hình một lượt lúc khởi động và KÊU TO những gì thiếu.
///
/// ═══ VÌ SAO ═══
///
/// Hệ thống này hỏng câm rất giỏi. Thiếu khoá AI thì tin vẫn cào về, chỉ không ai chấm điểm —
/// hàng chờ đầy tin không điểm, worker vẫn quay, log vẫn xanh. Thiếu nguồn cào thì worker chạy
/// đủ nhịp mà không có gì để lấy. Tắt NewsSite thì bấm Duyệt vẫn báo thành công, chỉ không có
/// bài nào lên web.
///
/// Ba lỗi đó đều đã xảy ra thật trong lúc dựng. Không cái nào ném exception.
///
/// Đặc biệt nguy khi lên VPS: hai công tắc và toàn bộ khoá API đang nằm trong user-secrets —
/// thứ KHÔNG theo git. Đẩy code lên máy mới là mất sạch, mà biểu hiện chỉ là "sao không thấy
/// tin nào".
///
/// KHÔNG chặn khởi động. Nửa hệ thống vẫn dùng được khi thiếu vài thứ; chặn thì mất luôn phần
/// còn lại. Chỉ ghi log ở mức Error để không ai bỏ sót.
/// </summary>
public class StartupHealthCheck(
    IServiceScopeFactory scopeFactory,
    IAiJudgeService ai,
    IOptions<ContentCrawlOptions> crawl,
    IOptions<NewsSiteOptions> news,
    IWebHostEnvironment env,
    ILogger<StartupHealthCheck> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        // Chờ migration và seed xong rồi mới đếm nguồn.
        try { await Task.Delay(TimeSpan.FromSeconds(25), ct); }
        catch (OperationCanceledException) { return; }

        var problems = new List<string>();
        var warnings = new List<string>();

        if (!crawl.Value.Enabled)
            problems.Add("ContentCrawl:Enabled = false → KHÔNG cào tin. "
                         + "Đặt true trong appsettings.Production.json hoặc biến môi trường.");

        if (!news.Value.Enabled)
            problems.Add("NewsSite:Enabled = false → bấm Duyệt vẫn báo thành công nhưng "
                         + "KHÔNG bài nào lên web.");

        if (!ai.IsAvailable())
            problems.Add("Chưa có khoá AI → tin cào về KHÔNG được chấm điểm và KHÔNG viết được "
                         + "bài. Đặt AiProviders:Providers:<tên>:ApiKey.");

        if (string.IsNullOrWhiteSpace(news.Value.PublicBaseUrl))
            problems.Add("NewsSite:PublicBaseUrl trống → link dán ở bình luận Facebook sẽ rỗng, "
                         + "và og:url thiếu nên thẻ chia sẻ không có ảnh.");

        if (crawl.Value.CrawlScheduleTimes.Count == 0)
            warnings.Add("ContentCrawl:CrawlScheduleTimes trống → mỗi nguồn chạy theo chu kỳ "
                         + "riêng trong CSDL, không theo lịch chung.");

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var active = await db.Set<CrawlSourceModel>()
                .CountAsync(x => !x.IsDeleted && x.IsActive, ct);
            if (active == 0)
                problems.Add("KHÔNG có nguồn cào nào đang bật → worker chạy đủ nhịp nhưng không "
                             + "lấy được gì. Nguồn nằm trong CSDL, không theo git: dùng "
                             + "POST /api/ContentCrawl/sources/import để đưa sang.");

            // Thư mục xuất bản không ghi được là lỗi câm kinh điển: build báo thành công mỗi
            // lượt, trang ngoài không bao giờ đổi.
            var builder = scope.ServiceProvider.GetRequiredService<NewsSiteBuilder>();
            if (news.Value.Enabled && !builder.CanWrite(out var why))
                problems.Add($"Không ghi được thư mục trang tin — {why}");
        }
        catch (Exception ex)
        {
            warnings.Add($"Không kiểm được CSDL lúc khởi động: {ex.Message}");
        }

        Report(problems, warnings);
    }

    private void Report(List<string> problems, List<string> warnings)
    {
        if (problems.Count == 0 && warnings.Count == 0)
        {
            logger.LogInformation("Kiểm khởi động: mọi thứ sẵn sàng (môi trường {Env})", env.EnvironmentName);
            return;
        }

        foreach (var w in warnings) logger.LogWarning("Kiểm khởi động — {Warning}", w);

        if (problems.Count == 0) return;

        // Gộp thành MỘT khối để không bị trôi giữa hàng trăm dòng log EF.
        var body = string.Join("\n", problems.Select((p, i) => $"  {i + 1}. {p}"));
        logger.LogError(
            "\n"
            + "════════════════════════════════════════════════════════════════\n"
            + " HỆ THỐNG CHƯA CHẠY ĐỦ — {Count} vấn đề (môi trường {Env})\n"
            + "════════════════════════════════════════════════════════════════\n"
            + "{Body}\n"
            + "════════════════════════════════════════════════════════════════",
            problems.Count, env.EnvironmentName, body);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
