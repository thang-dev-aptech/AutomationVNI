using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Notification;

public sealed record NotificationItem(
    Guid Id, NotificationKind Kind, NotificationSource Source, string Actor,
    string Title, string? Message, string? LinkUrl, DateTime CreatedAt, bool IsRead);

public sealed record NotificationFeed(List<NotificationItem> Items, int Unread);

/// <summary>
/// Ghi và đọc nhật ký hoạt động cho chuông thông báo.
///
/// Mục đích thật của tính năng này KHÔNG phải là báo cho vui, mà là chống làm trùng: sếp bấm
/// cào trên Telegram xong, người ngồi web thấy ngay "Telegram vừa cào Dân trí" nên không bấm
/// lại. Vì vậy trường Actor và Source là bắt buộc và luôn hiện ra — một dòng "Đã cào xong"
/// không nói ai làm thì vô dụng đúng ở tình huống nó sinh ra để giải quyết.
/// </summary>
public class NotificationService(AppDbContext context, ILogger<NotificationService> logger)
{
    /// <summary>
    /// Ghi một thông báo. KHÔNG BAO GIỜ ném: chỗ gọi là giữa luồng cào/duyệt/đăng, ghi nhật ký
    /// hỏng mà làm chết cả việc chính thì đúng là lấy phụ đè lên chính.
    /// </summary>
    public async Task AddAsync(
        NotificationKind kind, NotificationSource source, string actor,
        string title, string? message = null, string? linkUrl = null, Guid? refId = null,
        CancellationToken ct = default)
    {
        try
        {
            context.Set<AppNotificationModel>().Add(new AppNotificationModel
            {
                Id = Guid.NewGuid(),
                Kind = kind,
                Source = source,
                Actor = Cut(actor, 100) ?? "",
                Title = Cut(title, 200) ?? "",
                Message = Cut(message, 1000),
                LinkUrl = Cut(linkUrl, 500),
                RefId = refId,
                CreatedAt = DateTime.UtcNow,
            });
            await context.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Không ghi được thông báo '{Title}'", title);
        }
    }

    public async Task<NotificationFeed> GetFeedAsync(int take = 30, CancellationToken ct = default)
    {
        var query = context.Set<AppNotificationModel>().Where(x => !x.IsDeleted);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(x => new NotificationItem(
                x.Id, x.Kind, x.Source, x.Actor, x.Title, x.Message, x.LinkUrl,
                x.CreatedAt, x.ReadAt != null))
            .ToListAsync(ct);

        var unread = await query.CountAsync(x => x.ReadAt == null, ct);
        return new NotificationFeed(items, unread);
    }

    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await context.Set<AppNotificationModel>()
            .Where(x => !x.IsDeleted && x.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, now), ct);
    }

    /// <summary>
    /// Dọn thông báo cũ. Bảng này chỉ ghi thêm, không ai xoá — chạy vài tháng là hàng chục
    /// nghìn dòng cho một cái chuông chỉ hiện 30 dòng gần nhất.
    /// </summary>
    public async Task<int> PurgeOlderThanAsync(int days, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, days));
        return await context.Set<AppNotificationModel>()
            .Where(x => x.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    // Giữ null là null: Message/LinkUrl rỗng và null khác nhau ở phía giao diện — chuỗi rỗng
    // vẫn khiến React vẽ ra một dòng trống, còn null thì không vẽ gì.
    private static string? Cut(string? s, int n)
        => string.IsNullOrEmpty(s) ? null : s.Length <= n ? s : s[..n];
}
