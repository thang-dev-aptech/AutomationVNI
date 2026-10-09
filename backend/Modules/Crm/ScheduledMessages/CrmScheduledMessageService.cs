using Backend.Data;
using Backend.Modules.PageMessage;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Crm.ScheduledMessages;

public class CrmScheduledMessageService(
    AppDbContext db,
    IUserContext userContext,
    PageMessageService messages,
    TimeProvider clock)
{
    public const int MaxTextLength = 2000; // giới hạn Messenger
    public static readonly TimeSpan MinLeadTime = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan ReplyWindow = TimeSpan.FromHours(24);
    public static readonly TimeSpan StuckSendingThreshold = TimeSpan.FromMinutes(5);
    public const string UnknownOutcomeError = "Không rõ kết quả gửi, vui lòng kiểm tra hội thoại";

    private static readonly TimeZoneInfo VietnamTz = ResolveVietnamTz();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<CrmScheduledMessageResponse>> ListByConversationAsync(
        Guid conversationId, CancellationToken ct = default)
    {
        var list = await db.CrmScheduledMessages.AsNoTracking()
            .Where(x => !x.IsDeleted && x.PageConversationId == conversationId)
            .OrderBy(x => x.ScheduledAtUtc)
            .ToListAsync(ct);
        return list.Select(ToResponse).ToList();
    }

    public async Task<CrmScheduledMessageResponse> CreateAsync(
        CreateCrmScheduledMessageRequest request, CancellationToken ct = default)
    {
        var text = NormalizeText(request.Text);
        var scheduledAt = AsUtc(request.ScheduledAtUtc);
        var conversation = await db.PageConversations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.PageConversationId && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Hội thoại không tồn tại");
        ValidateSchedule(scheduledAt, conversation.LastCustomerMessageAt);

        var entity = new CrmScheduledMessageModel
        {
            Id = Guid.NewGuid(),
            PageConversationId = conversation.Id,
            Text = text,
            ScheduledAtUtc = scheduledAt,
            Status = CrmScheduledMessageStatus.Pending,
            CreatedByUserId = userContext.GetCurrentUserId(),
            CreatedAt = Now,
            CreatedBy = userContext.GetCurrentUserName()
        };
        db.CrmScheduledMessages.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task<CrmScheduledMessageResponse?> UpdateAsync(
        Guid id, UpdateCrmScheduledMessageRequest request, CancellationToken ct = default)
    {
        var entity = await db.CrmScheduledMessages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (entity is null) return null;
        if (entity.Status != CrmScheduledMessageStatus.Pending)
            throw new InvalidOperationException("Chỉ sửa được tin đang chờ gửi");

        var text = NormalizeText(request.Text);
        var scheduledAt = AsUtc(request.ScheduledAtUtc);
        var lastCustomer = await db.PageConversations.AsNoTracking()
            .Where(x => x.Id == entity.PageConversationId)
            .Select(x => x.LastCustomerMessageAt)
            .FirstOrDefaultAsync(ct);
        ValidateSchedule(scheduledAt, lastCustomer);

        // Có điều kiện Status=Pending: tránh sửa đè tin worker vừa claim.
        var now = Now;
        var updated = await db.CrmScheduledMessages
            .Where(x => x.Id == id && x.Status == CrmScheduledMessageStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Text, text)
                .SetProperty(x => x.ScheduledAtUtc, scheduledAt)
                .SetProperty(x => x.UpdatedAt, now), ct);
        if (updated == 0)
            throw new InvalidOperationException("Chỉ sửa được tin đang chờ gửi");

        return ToResponse((await db.CrmScheduledMessages.AsNoTracking().FirstAsync(x => x.Id == id, ct)));
    }

    public async Task CancelAsync(Guid id, CancellationToken ct = default)
    {
        if (!await db.CrmScheduledMessages.AnyAsync(x => x.Id == id && !x.IsDeleted, ct))
            throw new KeyNotFoundException("Tin hẹn giờ không tồn tại");
        var now = Now;
        var cancelled = await db.CrmScheduledMessages
            .Where(x => x.Id == id && x.Status == CrmScheduledMessageStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, CrmScheduledMessageStatus.Cancelled)
                .SetProperty(x => x.UpdatedAt, now), ct);
        if (cancelled == 0)
            throw new InvalidOperationException("Chỉ huỷ được tin đang chờ gửi");
    }

    /// <summary>Khi khởi động: tin kẹt ở Sending quá ngưỡng → Failed (KHÔNG tự gửi lại).</summary>
    public Task<int> RecoverStuckAsync(CancellationToken ct = default)
    {
        var now = Now;
        var cutoff = now - StuckSendingThreshold;
        return db.CrmScheduledMessages
            .Where(x => x.Status == CrmScheduledMessageStatus.Sending
                        && (x.ClaimedAtUtc == null || x.ClaimedAtUtc <= cutoff))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, CrmScheduledMessageStatus.Failed)
                .SetProperty(x => x.Error, UnknownOutcomeError)
                .SetProperty(x => x.UpdatedAt, now), ct);
    }

    public async Task<List<Guid>> ListDueIdsAsync(int limit, CancellationToken ct = default)
    {
        var now = Now;
        return await db.CrmScheduledMessages.AsNoTracking()
            .Where(x => x.Status == CrmScheduledMessageStatus.Pending && x.ScheduledAtUtc <= now)
            .OrderBy(x => x.ScheduledAtUtc)
            .Select(x => x.Id)
            .Take(limit)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Claim nguyên tử Pending → Sending (UPDATE có điều kiện). Chỉ khi claim thành công mới kiểm tra
    /// cửa sổ/token rồi gọi PageMessageService.SendAsync. Trả false nếu worker khác đã claim.
    /// </summary>
    public async Task<bool> ProcessAsync(Guid id, CancellationToken ct = default)
    {
        var now = Now;
        var token = Guid.NewGuid();
        var claimed = await db.CrmScheduledMessages
            .Where(x => x.Id == id
                        && x.Status == CrmScheduledMessageStatus.Pending
                        && x.ScheduledAtUtc <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, CrmScheduledMessageStatus.Sending)
                .SetProperty(x => x.ClaimedAtUtc, now)
                .SetProperty(x => x.ClaimToken, token)
                .SetProperty(x => x.UpdatedAt, now), ct);
        if (claimed == 0) return false;

        var entity = await db.CrmScheduledMessages.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        try
        {
            var conversation = await db.PageConversations.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == entity.PageConversationId && !x.IsDeleted, ct);
            if (conversation is null)
            {
                await FinishAsync(id, token, CrmScheduledMessageStatus.Failed, "Hội thoại không còn tồn tại", null, ct);
                return true;
            }

            var closesAt = conversation.LastCustomerMessageAt?.Add(ReplyWindow);
            if (closesAt is null || closesAt <= Now)
            {
                await FinishAsync(id, token, CrmScheduledMessageStatus.Failed,
                    $"Cửa sổ 24h đã đóng{(closesAt is null ? "" : $" lúc {FormatVn(closesAt.Value)}")} — không gửi được tin hẹn giờ",
                    null, ct);
                return true;
            }

            var channel = await db.SocialChannels.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == conversation.SocialChannelId && !x.IsDeleted, ct);
            if (channel is null || !channel.IsActive || string.IsNullOrWhiteSpace(channel.AccessToken))
            {
                await FinishAsync(id, token, CrmScheduledMessageStatus.Failed,
                    "Kênh đã tắt hoặc hết token — không gửi được tin hẹn giờ", null, ct);
                return true;
            }

            await messages.SendAsync(conversation.Id, new SendPageMessageRequest { Text = entity.Text }, ct);

            var echoId = await db.PageMessages.AsNoTracking()
                .Where(x => x.PageConversationId == conversation.Id && x.IsEcho)
                .OrderByDescending(x => x.SentAt)
                .Select(x => x.ExternalMessageId)
                .FirstOrDefaultAsync(ct);
            await FinishAsync(id, token, CrmScheduledMessageStatus.Sent, null, echoId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Dừng host giữa chừng: để nguyên Sending → RecoverStuck sẽ đánh Failed (không gửi lại).
            throw;
        }
        catch (Exception ex)
        {
            await FinishAsync(id, token, CrmScheduledMessageStatus.Failed, Truncate(ex.Message), null, CancellationToken.None);
        }

        return true;
    }

    private async Task FinishAsync(
        Guid id, Guid token, CrmScheduledMessageStatus status, string? error, string? sentMessageId, CancellationToken ct)
    {
        var now = Now;
        // Chỉ ghi kết quả nếu vẫn đúng lượt claim của mình.
        await db.CrmScheduledMessages
            .Where(x => x.Id == id && x.ClaimToken == token && x.Status == CrmScheduledMessageStatus.Sending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.Error, error)
                .SetProperty(x => x.SentMessageId, sentMessageId)
                .SetProperty(x => x.SentAtUtc, status == CrmScheduledMessageStatus.Sent ? now : (DateTime?)null)
                .SetProperty(x => x.UpdatedAt, now), ct);
    }

    private void ValidateSchedule(DateTime scheduledAtUtc, DateTime? lastCustomerMessageAt)
    {
        var now = Now;
        if (scheduledAtUtc < now + MinLeadTime)
            throw new InvalidOperationException("Giờ hẹn phải cách hiện tại ít nhất 1 phút");

        var closesAt = lastCustomerMessageAt?.Add(ReplyWindow);
        if (closesAt is null || closesAt <= now)
            throw new InvalidOperationException("Cửa sổ 24h của hội thoại đã đóng — không thể hẹn giờ gửi");
        if (scheduledAtUtc > closesAt)
            throw new InvalidOperationException(
                $"Giờ hẹn vượt quá cửa sổ 24h. Cửa sổ đóng lúc {FormatVn(closesAt.Value)} (giờ Việt Nam)");
    }

    private static string NormalizeText(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) throw new InvalidOperationException("Nội dung tin nhắn không được để trống");
        if (t.Length > MaxTextLength)
            throw new InvalidOperationException($"Nội dung tối đa {MaxTextLength} ký tự");
        return t;
    }

    private static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string Truncate(string message)
        => message.Length <= 1000 ? message : message[..1000];

    public static string FormatVn(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), VietnamTz)
            .ToString("HH:mm dd/MM/yyyy");

    private static CrmScheduledMessageResponse ToResponse(CrmScheduledMessageModel x) => new()
    {
        Id = x.Id,
        PageConversationId = x.PageConversationId,
        Text = x.Text,
        ScheduledAtUtc = x.ScheduledAtUtc,
        Status = x.Status,
        Error = x.Error,
        SentMessageId = x.SentMessageId,
        SentAtUtc = x.SentAtUtc,
        CreatedAt = x.CreatedAt,
        CreatedBy = x.CreatedBy
    };

    private static TimeZoneInfo ResolveVietnamTz()
    {
        foreach (var tzId in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(tzId); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone("VN", TimeSpan.FromHours(7), "VN", "VN");
    }
}
