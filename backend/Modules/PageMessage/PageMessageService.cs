using System.Text.Json;
using Backend.Data;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;
using Backend.Shared.PageMessage;
using Backend.Shared.Repositories;
using Backend.Shared.SocialPublish;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Modules.PageMessage;

public class PageMessageService(
    AppDbContext db,
    FacebookPageMessagingProvider provider,
    IUserContext userContext,
    IOptions<SocialPublishOptions> publishOptions,
    ILogger<PageMessageService> logger)
{
    private static readonly TimeSpan StandardReplyWindow = TimeSpan.FromHours(24);

    public async Task<PagedResult<PageConversationResponse>> FilterAsync(
        PageConversationFilterRequest request,
        CancellationToken ct = default)
    {
        var query = db.PageConversations.AsNoTracking().Where(x => !x.IsDeleted);
        if (request.SocialChannelId.HasValue)
            query = query.Where(x => x.SocialChannelId == request.SocialChannelId.Value);
        if (request.InboxStatus.HasValue)
            query = query.Where(x => x.InboxStatus == request.InboxStatus.Value);
        if (request.UnreadOnly == true)
            query = query.Where(x => x.UnreadCount > 0);
        if (request.OpenWindowOnly == true)
        {
            var cutoff = DateTime.UtcNow.Subtract(StandardReplyWindow);
            query = query.Where(x => x.LastCustomerMessageAt >= cutoff);
        }
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x =>
                (x.ParticipantName != null && x.ParticipantName.Contains(keyword))
                || x.ParticipantExternalId.Contains(keyword)
                || (x.Snippet != null && x.Snippet.Contains(keyword)));
        }

        var total = await query.CountAsync(ct);
        var index = Math.Max(1, request.Index);
        var size = Math.Clamp(request.Size, 1, 100);
        var entities = await query
            .OrderByDescending(x => x.LastMessageAt ?? x.UpdatedAt ?? x.CreatedAt)
            .Skip((index - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        var channelIds = entities.Select(x => x.SocialChannelId).Distinct().ToList();
        var channels = await db.SocialChannels.AsNoTracking()
            .Where(x => channelIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.PageName, ct);

        return new PagedResult<PageConversationResponse>
        {
            Items = entities.Select(x => ToResponse(x, channels.GetValueOrDefault(x.SocialChannelId))).ToList(),
            Total = total,
            Index = index,
            Size = size
        };
    }

    public async Task<MessageInboxSummaryResponse> SummaryAsync(CancellationToken ct = default)
    {
        var query = db.PageConversations.AsNoTracking().Where(x => !x.IsDeleted);
        var cutoff = DateTime.UtcNow.Subtract(StandardReplyWindow);
        return new MessageInboxSummaryResponse
        {
            Total = await query.CountAsync(ct),
            NewCount = await query.CountAsync(x => x.InboxStatus == MessageInboxStatus.New, ct),
            InProgress = await query.CountAsync(x => x.InboxStatus == MessageInboxStatus.InProgress, ct),
            Unread = await query.CountAsync(x => x.UnreadCount > 0, ct),
            ReplyWindowOpen = await query.CountAsync(x => x.LastCustomerMessageAt >= cutoff, ct)
        };
    }

    public async Task<PageConversationResponse?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var conversation = await db.PageConversations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (conversation is null) return null;

        var channelName = await db.SocialChannels.AsNoTracking()
            .Where(x => x.Id == conversation.SocialChannelId)
            .Select(x => x.PageName)
            .FirstOrDefaultAsync(ct);
        var messages = await db.PageMessages.AsNoTracking()
            .Where(x => x.PageConversationId == id && !x.IsDeleted)
            .OrderBy(x => x.SentAt ?? x.CreatedAt)
            .ToListAsync(ct);

        var result = ToResponse(conversation, channelName);
        result.Messages = messages.Select(ToMessageResponse).ToList();
        return result;
    }

    public async Task<SyncPageMessagesResult> SyncAsync(
        SyncPageMessagesRequest request,
        CancellationToken ct = default)
    {
        var result = new SyncPageMessagesResult();
        var channelsQuery = db.SocialChannels.Where(x =>
            !x.IsDeleted
            && x.IsActive
            && x.Platform == SocialPlatform.Facebook
            && x.ChannelType == SocialChannelType.Page);
        if (request.SocialChannelId.HasValue)
            channelsQuery = channelsQuery.Where(x => x.Id == request.SocialChannelId.Value);

        var channels = await channelsQuery.ToListAsync(ct);
        foreach (var channel in channels)
        {
            try
            {
                var (conversations, messages) = await SyncChannelAsync(
                    channel,
                    request.Full ? 500 : 100,
                    ct);
                result.ChannelsProcessed++;
                result.ConversationsUpserted += conversations;
                result.MessagesUpserted += messages;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Messenger sync failed for channel {ChannelId}", channel.Id);
                result.Errors.Add($"{channel.PageName}: {ex.Message}");
            }
        }
        return result;
    }

    public async Task<(int Conversations, int Messages)> SyncChannelAsync(
        SocialChannelModel channel,
        int maxConversations,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(channel.ExternalPageId)
            || string.IsNullOrWhiteSpace(channel.AccessToken))
            throw new InvalidOperationException("Page thiếu ExternalPageId hoặc access token");

        var conversationCount = 0;
        var messageCount = 0;
        string? cursor = null;
        while (conversationCount < maxConversations)
        {
            var (items, next) = await provider.ListConversationsAsync(
                channel.ExternalPageId,
                channel.AccessToken,
                cursor,
                Math.Min(50, maxConversations - conversationCount),
                ct);
            if (items.Count == 0) break;

            foreach (var item in items)
            {
                var conversation = await UpsertConversationAsync(channel, item, ct);
                conversationCount++;
                foreach (var message in item.Messages)
                {
                    await UpsertMessageAsync(channel, conversation, message, ct);
                    messageCount++;
                }
                await RefreshConversationStatsAsync(conversation, ct);
            }

            cursor = next;
            if (string.IsNullOrWhiteSpace(cursor)) break;
        }
        return (conversationCount, messageCount);
    }

    public async Task<PageConversationResponse> SendAsync(
        Guid conversationId,
        SendPageMessageRequest request,
        CancellationToken ct = default)
    {
        var conversation = await GetConversationOrThrow(conversationId, ct);
        var channel = await GetChannelOrThrow(conversation.SocialChannelId, ct);
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("Nội dung tin nhắn không được để trống");

        if (!IsReplyWindowOpen(conversation))
        {
            throw new InvalidOperationException(
                "Cửa sổ phản hồi 24 giờ đã đóng. MVP không tự dùng message tag để tránh vi phạm chính sách Meta.");
        }

        var sendResult = await provider.SendTextAsync(
            channel.ExternalPageId,
            channel.AccessToken,
            conversation.ParticipantExternalId,
            request.Text,
            ct);

        await AddActionLogAsync(
            conversation.Id,
            MessageActionType.Reply,
            request.Text.Trim(),
            sendResult.Success,
            sendResult.ErrorMessage,
            sendResult.MessageId,
            ct);

        if (!sendResult.Success)
            throw new InvalidOperationException(sendResult.ErrorMessage ?? "Gửi tin nhắn thất bại");

        if (!string.IsNullOrWhiteSpace(sendResult.MessageId))
        {
            await UpsertMessageAsync(channel, conversation, new ProviderPageMessageDto
            {
                ExternalMessageId = sendResult.MessageId,
                SenderExternalId = channel.ExternalPageId,
                RecipientExternalId = conversation.ParticipantExternalId,
                Text = request.Text.Trim(),
                SentAt = DateTime.UtcNow,
                IsEcho = true
            }, ct);
        }

        conversation.Snippet = request.Text.Trim();
        conversation.LastMessageAt = DateTime.UtcNow;
        conversation.LastPageMessageAt = DateTime.UtcNow;
        conversation.InboxStatus = MessageInboxStatus.Replied;
        conversation.UnreadCount = 0;
        conversation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await GetAsync(conversation.Id, ct))!;
    }

    public async Task<PageConversationResponse> SetStatusAsync(
        Guid id,
        MessageInboxStatus status,
        CancellationToken ct)
    {
        var conversation = await GetConversationOrThrow(id, ct);
        conversation.InboxStatus = status;
        conversation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await AddActionLogAsync(id, MessageActionType.SetStatus, status.ToString(), true, null, null, ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<PageConversationResponse> AssignAsync(
        Guid id,
        string? assignedTo,
        CancellationToken ct)
    {
        var conversation = await GetConversationOrThrow(id, ct);
        conversation.AssignedTo = string.IsNullOrWhiteSpace(assignedTo) ? null : assignedTo.Trim();
        if (conversation.InboxStatus == MessageInboxStatus.New)
            conversation.InboxStatus = MessageInboxStatus.InProgress;
        conversation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await AddActionLogAsync(id, MessageActionType.Assign, conversation.AssignedTo, true, null, null, ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<PageConversationResponse> NoteAsync(Guid id, string? note, CancellationToken ct)
    {
        var conversation = await GetConversationOrThrow(id, ct);
        conversation.InternalNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        conversation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await AddActionLogAsync(id, MessageActionType.AddNote, conversation.InternalNote, true, null, null, ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task IngestMetaWebhookAsync(JsonElement root, CancellationToken ct)
    {
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return;

        foreach (var entry in entries.EnumerateArray())
        {
            var pageId = GetString(entry, "id");
            if (string.IsNullOrWhiteSpace(pageId)
                || !entry.TryGetProperty("messaging", out var messaging)
                || messaging.ValueKind != JsonValueKind.Array)
                continue;

            var channel = await db.SocialChannels.FirstOrDefaultAsync(x =>
                !x.IsDeleted
                && x.IsActive
                && x.Platform == SocialPlatform.Facebook
                && x.ExternalPageId == pageId, ct);
            if (channel is null) continue;

            foreach (var evt in messaging.EnumerateArray())
            {
                var senderId = GetNestedString(evt, "sender", "id");
                var recipientId = GetNestedString(evt, "recipient", "id");
                var timestamp = GetUnixMilliseconds(evt, "timestamp") ?? DateTime.UtcNow;
                var isFromPage = senderId == pageId;
                var participantId = isFromPage ? recipientId : senderId;
                if (string.IsNullOrWhiteSpace(participantId)) continue;

                var conversation = await db.PageConversations.FirstOrDefaultAsync(x =>
                    !x.IsDeleted
                    && x.SocialChannelId == channel.Id
                    && x.ParticipantExternalId == participantId, ct);
                if (conversation is null)
                {
                    conversation = new PageConversationModel
                    {
                        Id = Guid.NewGuid(),
                        SocialChannelId = channel.Id,
                        ExternalConversationId = $"pending:{pageId}:{participantId}",
                        ParticipantExternalId = participantId,
                        ParticipantName = participantId,
                        InboxStatus = MessageInboxStatus.New,
                        CreatedAt = DateTime.UtcNow
                    };
                    db.PageConversations.Add(conversation);
                    await db.SaveChangesAsync(ct);
                }

                if (evt.TryGetProperty("message", out var message))
                {
                    var messageId = GetString(message, "mid");
                    if (string.IsNullOrWhiteSpace(messageId)) continue;
                    var isEcho = message.TryGetProperty("is_echo", out var echo)
                                 && echo.ValueKind == JsonValueKind.True;
                    var attachments = message.TryGetProperty("attachments", out var attachmentElement)
                        ? attachmentElement.GetRawText()
                        : null;

                    var isNewMessage = await UpsertMessageAsync(channel, conversation, new ProviderPageMessageDto
                    {
                        ExternalMessageId = messageId,
                        SenderExternalId = senderId,
                        RecipientExternalId = recipientId,
                        Text = GetString(message, "text"),
                        AttachmentsJson = attachments,
                        SentAt = timestamp,
                        IsEcho = isEcho
                    }, ct);

                    if (!conversation.LastMessageAt.HasValue || timestamp >= conversation.LastMessageAt.Value)
                    {
                        conversation.Snippet = GetString(message, "text")
                                               ?? (attachments is null ? "Tin nhắn" : "Tệp đính kèm");
                        conversation.LastMessageAt = timestamp;
                    }
                    if (isFromPage || isEcho)
                    {
                        if (!conversation.LastPageMessageAt.HasValue || timestamp > conversation.LastPageMessageAt.Value)
                            conversation.LastPageMessageAt = timestamp;
                    }
                    else if (isNewMessage)
                    {
                        if (!conversation.LastCustomerMessageAt.HasValue || timestamp > conversation.LastCustomerMessageAt.Value)
                            conversation.LastCustomerMessageAt = timestamp;
                        conversation.UnreadCount++;
                        conversation.InboxStatus = MessageInboxStatus.New;
                    }
                    conversation.UpdatedAt = DateTime.UtcNow;
                }

                if (evt.TryGetProperty("read", out var read))
                {
                    var watermark = GetUnixMilliseconds(read, "watermark");
                    if (watermark.HasValue)
                    {
                        var messages = await db.PageMessages.Where(x =>
                            x.PageConversationId == conversation.Id
                            && x.IsFromPage
                            && x.SentAt <= watermark.Value).ToListAsync(ct);
                        foreach (var item in messages) item.IsRead = true;
                    }
                }

                if (evt.TryGetProperty("delivery", out var delivery))
                {
                    var watermark = GetUnixMilliseconds(delivery, "watermark");
                    if (watermark.HasValue)
                    {
                        var messages = await db.PageMessages.Where(x =>
                            x.PageConversationId == conversation.Id
                            && x.IsFromPage
                            && x.SentAt <= watermark.Value).ToListAsync(ct);
                        foreach (var item in messages) item.IsDelivered = true;
                    }
                }
                await db.SaveChangesAsync(ct);
            }
        }
    }

    public async Task SubscribeFacebookPagesAsync(CancellationToken ct)
    {
        var channels = await db.SocialChannels.Where(x =>
            !x.IsDeleted
            && x.IsActive
            && x.Platform == SocialPlatform.Facebook
            && x.ChannelType == SocialChannelType.Page).ToListAsync(ct);
        var fb = publishOptions.Value.Facebook;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        foreach (var channel in channels)
        {
            if (string.IsNullOrWhiteSpace(channel.AccessToken)
                || string.IsNullOrWhiteSpace(channel.ExternalPageId))
                continue;
            var url = $"{fb.GraphBaseUrl.TrimEnd('/')}/{fb.GraphVersion}/{Uri.EscapeDataString(channel.ExternalPageId)}/subscribed_apps";
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["subscribed_fields"] = "feed,messages,messaging_postbacks,message_deliveries,message_reads",
                ["access_token"] = channel.AccessToken
            });
            using var response = await client.PostAsync(url, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Messenger subscription failed for {Page}: {Body}", channel.PageName, body);
        }
    }

    private async Task<PageConversationModel> UpsertConversationAsync(
        SocialChannelModel channel,
        ProviderConversationDto dto,
        CancellationToken ct)
    {
        var conversation = await db.PageConversations.FirstOrDefaultAsync(x =>
            !x.IsDeleted
            && x.SocialChannelId == channel.Id
            && (x.ExternalConversationId == dto.ExternalConversationId
                || (!string.IsNullOrWhiteSpace(dto.ParticipantExternalId)
                    && x.ParticipantExternalId == dto.ParticipantExternalId)), ct);
        if (conversation is null)
        {
            conversation = new PageConversationModel
            {
                Id = Guid.NewGuid(),
                SocialChannelId = channel.Id,
                ExternalConversationId = dto.ExternalConversationId,
                ParticipantExternalId = dto.ParticipantExternalId,
                InboxStatus = dto.UnreadCount > 0 ? MessageInboxStatus.New : MessageInboxStatus.Replied,
                CreatedAt = DateTime.UtcNow
            };
            db.PageConversations.Add(conversation);
        }

        conversation.ExternalConversationId = dto.ExternalConversationId;
        if (!string.IsNullOrWhiteSpace(dto.ParticipantExternalId))
            conversation.ParticipantExternalId = dto.ParticipantExternalId;
        if (!string.IsNullOrWhiteSpace(dto.ParticipantName))
            conversation.ParticipantName = dto.ParticipantName;
        if (!string.IsNullOrWhiteSpace(dto.ParticipantAvatarUrl))
            conversation.ParticipantAvatarUrl = dto.ParticipantAvatarUrl;
        conversation.Snippet = dto.Snippet ?? conversation.Snippet;
        conversation.LastMessageAt = dto.UpdatedAt ?? conversation.LastMessageAt;
        conversation.UnreadCount = dto.UnreadCount;
        conversation.MessageCount = Math.Max(dto.MessageCount, conversation.MessageCount);
        conversation.LastSyncedAt = DateTime.UtcNow;
        conversation.UpdatedAt = DateTime.UtcNow;
        if (dto.UnreadCount > 0 && conversation.InboxStatus == MessageInboxStatus.Replied)
            conversation.InboxStatus = MessageInboxStatus.New;
        await db.SaveChangesAsync(ct);
        return conversation;
    }

    private async Task<bool> UpsertMessageAsync(
        SocialChannelModel channel,
        PageConversationModel conversation,
        ProviderPageMessageDto dto,
        CancellationToken ct)
    {
        var message = await db.PageMessages.FirstOrDefaultAsync(x =>
            !x.IsDeleted
            && x.SocialChannelId == channel.Id
            && x.ExternalMessageId == dto.ExternalMessageId, ct);
        var isNew = message is null;
        if (message is null)
        {
            message = new PageMessageModel
            {
                Id = Guid.NewGuid(),
                PageConversationId = conversation.Id,
                SocialChannelId = channel.Id,
                ExternalMessageId = dto.ExternalMessageId,
                CreatedAt = DateTime.UtcNow
            };
            db.PageMessages.Add(message);
        }

        message.SenderExternalId = dto.SenderExternalId;
        message.RecipientExternalId = dto.RecipientExternalId;
        message.Text = dto.Text;
        message.AttachmentsJson = dto.AttachmentsJson;
        message.IsFromPage = dto.SenderExternalId == channel.ExternalPageId || dto.IsEcho;
        message.IsEcho = dto.IsEcho;
        message.SentAt = dto.SentAt;
        message.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return isNew;
    }

    private async Task RefreshConversationStatsAsync(PageConversationModel conversation, CancellationToken ct)
    {
        var messages = await db.PageMessages.AsNoTracking()
            .Where(x => x.PageConversationId == conversation.Id && !x.IsDeleted)
            .OrderBy(x => x.SentAt ?? x.CreatedAt)
            .ToListAsync(ct);
        if (messages.Count == 0) return;

        conversation.MessageCount = Math.Max(conversation.MessageCount, messages.Count);
        conversation.LastMessageAt = messages.Max(x => x.SentAt ?? x.CreatedAt);
        var customerDates = messages
            .Where(x => !x.IsFromPage)
            .Select(x => x.SentAt ?? x.CreatedAt)
            .ToList();
        var pageDates = messages
            .Where(x => x.IsFromPage)
            .Select(x => x.SentAt ?? x.CreatedAt)
            .ToList();
        conversation.LastCustomerMessageAt = customerDates.Count > 0 ? customerDates.Max() : null;
        conversation.LastPageMessageAt = pageDates.Count > 0 ? pageDates.Max() : null;
        var last = messages.Last();
        conversation.Snippet = last.Text ?? (last.AttachmentsJson is null ? "Tin nhắn" : "Tệp đính kèm");
        conversation.LastSyncedAt = DateTime.UtcNow;
        conversation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task AddActionLogAsync(
        Guid conversationId,
        MessageActionType action,
        string? payload,
        bool success,
        string? error,
        string? externalResultId,
        CancellationToken ct)
    {
        db.MessageActionLogs.Add(new MessageActionLogModel
        {
            Id = Guid.NewGuid(),
            PageConversationId = conversationId,
            ActionType = action,
            ActorUserId = userContext.GetCurrentUserId(),
            ActorUserName = userContext.GetCurrentUserName(),
            PayloadJson = payload,
            Success = success,
            ErrorMessage = error,
            ExternalResultId = externalResultId,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task<PageConversationModel> GetConversationOrThrow(Guid id, CancellationToken ct)
        => await db.PageConversations.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
           ?? throw new KeyNotFoundException("Hội thoại không tồn tại");

    private async Task<SocialChannelModel> GetChannelOrThrow(Guid id, CancellationToken ct)
        => await db.SocialChannels.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
           ?? throw new KeyNotFoundException("Facebook Page không tồn tại");

    private static bool IsReplyWindowOpen(PageConversationModel conversation)
        => conversation.LastCustomerMessageAt.HasValue
           && conversation.LastCustomerMessageAt.Value.Add(StandardReplyWindow) > DateTime.UtcNow;

    private static PageConversationResponse ToResponse(PageConversationModel entity, string? channelName)
    {
        var closesAt = entity.LastCustomerMessageAt?.Add(StandardReplyWindow);
        return new PageConversationResponse
        {
            Id = entity.Id,
            SocialChannelId = entity.SocialChannelId,
            ChannelName = channelName,
            ExternalConversationId = entity.ExternalConversationId,
            ParticipantExternalId = entity.ParticipantExternalId,
            ParticipantName = entity.ParticipantName,
            ParticipantAvatarUrl = entity.ParticipantAvatarUrl,
            Snippet = entity.Snippet,
            LastMessageAt = entity.LastMessageAt,
            LastCustomerMessageAt = entity.LastCustomerMessageAt,
            LastPageMessageAt = entity.LastPageMessageAt,
            ReplyWindowClosesAt = closesAt,
            IsReplyWindowOpen = closesAt > DateTime.UtcNow,
            UnreadCount = entity.UnreadCount,
            MessageCount = entity.MessageCount,
            InboxStatus = entity.InboxStatus,
            AssignedTo = entity.AssignedTo,
            InternalNote = entity.InternalNote,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private static PageMessageResponse ToMessageResponse(PageMessageModel entity) => new()
    {
        Id = entity.Id,
        ExternalMessageId = entity.ExternalMessageId,
        SenderExternalId = entity.SenderExternalId,
        RecipientExternalId = entity.RecipientExternalId,
        Text = entity.Text,
        AttachmentsJson = entity.AttachmentsJson,
        IsFromPage = entity.IsFromPage,
        IsEcho = entity.IsEcho,
        IsDelivered = entity.IsDelivered,
        IsRead = entity.IsRead,
        SentAt = entity.SentAt
    };

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetNestedString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var part in path)
        {
            if (!current.TryGetProperty(part, out current)) return null;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static DateTime? GetUnixMilliseconds(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.TryGetInt64(out var raw)
            ? DateTimeOffset.FromUnixTimeMilliseconds(raw).UtcDateTime
            : null;
}
