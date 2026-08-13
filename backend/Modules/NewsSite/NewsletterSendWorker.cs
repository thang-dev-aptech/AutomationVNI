using Backend.Shared.Email;
using Microsoft.Extensions.Options;

namespace Backend.Modules.NewsSite;

/// <summary>
/// Gửi email báo bài mới cho subscriber, quét theo <see cref="NewsArticleModel.NewsletterSentAt"/>
/// — khuôn theo <c>ThreadsTokenRefreshService</c> (vòng lặp try/catch + delay, scope riêng mỗi
/// lượt quét).
///
/// Vì sao gửi ở worker nền chứ không gửi thẳng trong <c>NewsPublisher.PublishAsync</c>: admin
/// bấm duyệt bài (hoặc bot Telegram tự duyệt) không nên phải đợi gửi xong N email mới xong
/// request — request duyệt bài trả về ngay như cũ, worker này tự nhặt bài mới ở nền.
/// </summary>
public class NewsletterSendWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<EmailOptions> emailOptions,
    IOptions<NewsSiteOptions> newsOptions,
    ILogger<NewsletterSendWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!emailOptions.Value.Enabled)
        {
            logger.LogInformation("NewsletterSendWorker bị tắt (Email:Enabled=false)");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "NewsletterSendWorker lỗi vòng lặp");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SendPendingAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<NewsSiteRepository>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        if (!emailSender.IsConfigured()) return;

        var pending = await repository.GetPendingNewsletterAsync(10, ct);
        if (pending.Count == 0) return;

        var subscribers = await repository.GetActiveSubscribersAsync(ct);
        if (subscribers.Count == 0)
        {
            // Không ai đăng ký thì vẫn phải đánh dấu đã "gửi" — nếu không, ngay khi có người
            // đăng ký đầu tiên, worker sẽ đột ngột gửi dồn mọi bài cũ từng bị bỏ qua.
            foreach (var a in pending) await repository.MarkNewsletterSentAsync(a.Id, ct);
            return;
        }

        foreach (var article in pending)
        {
            if (ct.IsCancellationRequested) break;

            var articleUrl = repository.PublicUrlOf(article.Slug);
            if (articleUrl is null)
            {
                // Chưa gắn PublicBaseUrl thì chưa có link để gửi — bỏ qua lượt này, thử lại
                // lượt sau (KHÔNG đánh dấu đã gửi), vì đây là lỗi cấu hình tạm thời, không phải
                // đặc điểm của bài viết.
                logger.LogWarning(
                    "Chưa cấu hình NewsSite:PublicBaseUrl — hoãn gửi email cho bài {Id}", article.Id);
                continue;
            }

            var sent = 0;
            var failed = 0;
            foreach (var sub in subscribers)
            {
                try
                {
                    await emailSender.SendAsync(
                        sub.Email, article.Title, BuildHtml(article, articleUrl, sub.UnsubscribeToken), ct);
                    sent++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Lỗi 1 người nhận không được dừng cả lô — log rồi đi tiếp, giống tinh thần
                    // NotificationService "không để lỗi phụ làm hỏng việc chính".
                    logger.LogWarning(ex, "Gửi email bài {Id} tới {Email} thất bại", article.Id, sub.Email);
                    failed++;
                }
            }

            await repository.MarkNewsletterSentAsync(article.Id, ct);
            logger.LogInformation(
                "Đã gửi email bài {Id} ({Title}) tới {Sent}/{Total} subscriber ({Failed} lỗi)",
                article.Id, article.Title, sent, subscribers.Count, failed);
        }
    }

    private string BuildHtml(NewsArticleModel article, string articleUrl, string unsubscribeToken)
    {
        var apiBase = emailOptions.Value.PublicApiBaseUrl.TrimEnd('/');
        var unsubscribeUrl = $"{apiBase}/api/news-public/unsubscribe?token={unsubscribeToken}";

        return $"""
            <div style="font-family:system-ui,sans-serif;max-width:560px;margin:0 auto">
              <h2 style="margin-bottom:8px">{NewsHtml.Esc(article.Title)}</h2>
              <p style="color:#555">{NewsHtml.Esc(article.Sapo ?? "")}</p>
              <p><a href="{NewsHtml.Esc(articleUrl)}" style="color:#2563eb">Đọc toàn bộ bài viết →</a></p>
              <hr style="margin:24px 0;border:none;border-top:1px solid #eee">
              <p style="font-size:12px;color:#999">
                Bạn nhận được email này vì đã đăng ký nhận tin từ {NewsHtml.Esc(newsOptions.Value.SiteName)}.
                <a href="{NewsHtml.Esc(unsubscribeUrl)}" style="color:#999">Huỷ đăng ký</a>.
              </p>
            </div>
            """;
    }
}
