using Backend.Data;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment;
using Backend.Shared.SocialComment;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.PageMetrics;

public sealed record ChannelSyncOutcome(
    string PageName, bool Ok, int Posts, int? Followers, int Likes, int Comments, int Shares, string? Error);

public sealed record MetricsSyncResult(int Channels, int Ok, int Failed, List<ChannelSyncOutcome> Details);

/// <summary>
/// Kéo chỉ số tương tác của mọi page về CSDL và chốt một mốc cho mỗi ngày.
///
/// ═══ VÌ SAO TÁCH KHỎI LUỒNG ĐỒNG BỘ BÌNH LUẬN ═══
///
/// Đồng bộ bình luận đã có sẵn và cũng đi qua danh sách bài, nên nhìn thì thấy thừa. Nhưng hai
/// việc có nhịp khác hẳn nhau:
///
///   · Bình luận cần gần thời gian thực — khách bình luận mà nửa ngày sau mới thấy là hỏng việc.
///     Đổi lại mỗi bài phải gọi thêm một lượt lấy danh sách bình luận.
///   · Chỉ số chỉ cần mỗi ngày một lần, nhưng phải quét ĐỦ mọi page để con số tổng có nghĩa.
///
/// Gộp làm một thì hoặc là gọi API gấp nhiều lần cần thiết, hoặc là chỉ số chỉ đúng với những
/// page tình cờ có bình luận mới.
///
/// Ở đây KHÔNG lấy bình luận. Một lượt gọi mỗi trang 25 bài, cộng một lượt lấy hồ sơ page.
/// </summary>
public class PageMetricsSyncService(
    AppDbContext db,
    IEnumerable<ISocialCommentProvider> providers,
    ILogger<PageMetricsSyncService> logger)
{
    /// <summary>Múi giờ chốt ngày. Xem ghi chú ở <see cref="ChannelMetricDailyModel.Date"/>.</summary>
    private static readonly TimeZoneInfo VnTime =
        Backend.Modules.Post.PendingScheduleHelper.ResolveTimeZone("Asia/Ho_Chi_Minh");

    public async Task<MetricsSyncResult> SyncAllAsync(int maxPostsPerChannel = 100, CancellationToken ct = default)
    {
        var channels = await db.SocialChannels
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.PageName)
            .ToListAsync(ct);

        var details = new List<ChannelSyncOutcome>();
        foreach (var channel in channels)
        {
            ct.ThrowIfCancellationRequested();
            details.Add(await SyncChannelAsync(channel, maxPostsPerChannel, ct));
        }

        var ok = details.Count(x => x.Ok);
        logger.LogInformation(
            "Đồng bộ chỉ số: {Ok}/{Total} page thành công", ok, channels.Count);
        return new MetricsSyncResult(channels.Count, ok, details.Count - ok, details);
    }

    public async Task<ChannelSyncOutcome> SyncChannelAsync(
        SocialChannelModel channel, int maxPosts, CancellationToken ct)
    {
        var name = channel.PageName ?? "(không tên)";

        if (string.IsNullOrWhiteSpace(channel.AccessToken) || string.IsNullOrWhiteSpace(channel.ExternalPageId))
            return await RecordAsync(channel, null, 0, 0, 0, 0, "Kênh thiếu token hoặc mã page", ct);

        var provider = providers.FirstOrDefault(p => p.Platform == channel.Platform);
        if (provider is null)
            return await RecordAsync(channel, null, 0, 0, 0, 0, $"Chưa hỗ trợ nền tảng {channel.Platform}", ct);

        // Thử lại một lần khi lỗi mạng.
        //
        // Lượt chạy thật đầu tiên có một page hỏng vì "Connection reset by peer" giữa chừng — mạng
        // chập, không phải token hỏng. Không thử lại thì page đó mất số suốt 6 tiếng tới, và giao
        // diện treo cờ cảnh báo cho một sự cố đã tự hết từ lâu.
        //
        // CHỈ thử lại lỗi mạng. Token hỏng hay page bị xoá thì gọi lại vẫn hỏng y hệt, chỉ tốn thêm
        // thời gian và làm chậm những page phía sau.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SyncChannelOnceAsync(channel, provider, maxPosts, ct);
            }
            catch (Exception ex) when (attempt == 1 && IsTransient(ex))
            {
                logger.LogInformation(
                    "Page {Page} lỗi mạng ({Message}) — thử lại lần 2", name, ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(3), ct); }
                catch (OperationCanceledException) { throw; }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Đồng bộ chỉ số hỏng cho page {Page}", name);
                return await RecordAsync(channel, null, 0, 0, 0, 0, Trim(ex.Message), ct);
            }
        }
    }

    private static bool IsTransient(Exception ex)
        => ex is HttpRequestException or TaskCanceledException or IOException
           || ex.InnerException is HttpRequestException or IOException or System.Net.Sockets.SocketException;

    private async Task<ChannelSyncOutcome> SyncChannelOnceAsync(
        SocialChannelModel channel, ISocialCommentProvider provider, int maxPosts, CancellationToken ct)
    {
        {
            var profile = await provider.GetPageProfileAsync(channel.ExternalPageId, channel.AccessToken, ct);

            // Nạp MỘT LẦN toàn bộ bài đã biết của page, rồi đối chiếu trong bộ nhớ.
            //
            // Bản đầu tôi viết tra CSDL và SaveChanges cho TỪNG bài: 100 bài × 15 page = 1.500 giao
            // dịch ghi. SQLite chỉ cho một người ghi tại một thời điểm, nên nó đâm vào worker cào tin
            // và worker bình luận đang chạy cùng lúc — hai page hỏng thật với lỗi
            // "SQLite Error 5: database is locked", và cả lượt mất 85 giây.
            //
            // Gom lại thành một lượt đọc và một lượt ghi cho mỗi page thì hết đâm nhau.
            var existing = await db.SocialPosts
                .Where(x => x.SocialChannelId == channel.Id && !x.IsDeleted)
                .ToDictionaryAsync(x => x.ExternalPostId, x => x, ct);

            var posts = 0;
            string? cursor = null;
            while (posts < maxPosts)
            {
                var (batch, next) = await provider.ListPostsAsync(
                    channel.ExternalPageId, channel.AccessToken, cursor,
                    Math.Min(25, maxPosts - posts), ct);
                if (batch.Count == 0) break;

                foreach (var dto in batch)
                    ApplyMetrics(channel, dto, existing);

                posts += batch.Count;
                cursor = next;
                if (string.IsNullOrWhiteSpace(cursor)) break;
            }

            await db.SaveChangesAsync(ct);

            // Cộng từ CSDL chứ không cộng từ lô vừa tải: lô chỉ có maxPosts bài gần nhất, còn khách
            // nhìn thẻ "Tương tác" là muốn tổng của mọi bài mình từng biết.
            var totals = await db.SocialPosts
                .Where(x => x.SocialChannelId == channel.Id && !x.IsDeleted)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Likes = g.Sum(x => x.LikeCount),
                    Comments = g.Sum(x => x.PlatformCommentCount),
                    Shares = g.Sum(x => x.ShareCount),
                })
                .FirstOrDefaultAsync(ct);

            return await RecordAsync(channel, profile?.Followers, posts,
                totals?.Likes ?? 0, totals?.Comments ?? 0, totals?.Shares ?? 0, null, ct);
        }
    }

    /// <summary>Cập nhật trong bộ nhớ. Người gọi lo phần <c>SaveChanges</c>.</summary>
    private void ApplyMetrics(
        SocialChannelModel channel, ProviderPostDto dto, Dictionary<string, SocialPostModel> existing)
    {
        if (!existing.TryGetValue(dto.ExternalPostId, out var entity))
        {
            // Bài có trên page mà chưa có trong CSDL — đăng tay trên Facebook chẳng hạn. Vẫn phải
            // tính, vì khách nhìn tổng tương tác của PAGE, không quan tâm bài do đâu mà ra.
            entity = new SocialPostModel
            {
                Id = Guid.NewGuid(),
                SocialChannelId = channel.Id,
                Platform = channel.Platform,
                ExternalPostId = dto.ExternalPostId,
                CreatedAt = DateTime.UtcNow,
            };
            db.SocialPosts.Add(entity);
            // Cùng một bài có thể xuất hiện ở hai trang phân trang khi page đăng bài mới giữa chừng.
            // Không ghi vào từ điển thì lần gặp sau lại Add thêm một bản nữa, và tổng tương tác của
            // page bị đếm gấp đôi cho bài đó.
            existing[dto.ExternalPostId] = entity;
        }

        entity.Message ??= dto.Message;
        entity.PermalinkUrl ??= dto.PermalinkUrl;
        entity.PostedAt ??= dto.PostedAt;
        if (dto.LikeCount.HasValue) entity.LikeCount = dto.LikeCount.Value;
        if (dto.ShareCount.HasValue) entity.ShareCount = dto.ShareCount.Value;
        if (dto.CommentCount.HasValue) entity.PlatformCommentCount = dto.CommentCount.Value;
        entity.MetricsSyncedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Ghi mốc của HÔM NAY. Chạy lại trong cùng ngày thì ĐÈ LÊN, không thêm dòng.
    ///
    /// Người ta bấm "Đồng bộ ngay" vài lần trong một buổi là chuyện thường. Mỗi lần thêm một dòng
    /// thì biểu đồ xu hướng có ba điểm cho cùng một ngày, và phép trừ "hôm nay hơn hôm qua" ra số
    /// vô nghĩa.
    /// </summary>
    private async Task<ChannelSyncOutcome> RecordAsync(
        SocialChannelModel channel, int? followers, int posts,
        int likes, int comments, int shares, string? error, CancellationToken ct)
    {
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VnTime).Date;

        var row = await db.ChannelMetricDaily
            .FirstOrDefaultAsync(x => x.SocialChannelId == channel.Id && x.Date == today, ct);
        if (row is null)
        {
            row = new ChannelMetricDailyModel
            {
                Id = Guid.NewGuid(),
                SocialChannelId = channel.Id,
                Date = today,
                CreatedAt = DateTime.UtcNow,
            };
            db.ChannelMetricDaily.Add(row);
        }

        // Lượt hỏng KHÔNG được ghi đè số đúng đã đo sáng nay. Giữ số cũ, chỉ ghi thêm lý do hỏng —
        // dashboard nhờ vậy vẫn hiện số thật kèm cảnh báo "số này đo lúc mấy giờ", thay vì tụt về 0.
        if (error is null)
        {
            if (followers.HasValue) row.Followers = followers.Value;
            row.PostsPublished = posts;
            row.TotalLikes = likes;
            row.TotalComments = comments;
            row.TotalShares = shares;
        }
        row.SyncError = error;
        row.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return new ChannelSyncOutcome(
            channel.PageName ?? "(không tên)", error is null, posts,
            followers, likes, comments, shares, error);
    }

    private static string Trim(string? s, int max = 300)
        => string.IsNullOrEmpty(s) ? "Lỗi không rõ" : s.Length <= max ? s : s[..max];
}
